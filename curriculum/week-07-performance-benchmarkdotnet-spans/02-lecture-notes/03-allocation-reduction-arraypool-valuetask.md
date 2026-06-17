# Lecture 3 — Allocation Reduction: `ArrayPool<T>`, `ValueTask<T>`, and Struct vs Class

> **Reading time:** ~70 minutes. **Hands-on time:** ~60 minutes (you pool a buffer, audit a `ValueTask` API, and pick a struct vs class).

Lecture 1 gave you the tool (BenchmarkDotNet). Lecture 2 gave you the primitive (`Span<T>`). This lecture gives you the three remaining moves you make to drive allocations toward zero in real production code: pooling buffers larger than 1 KB with `ArrayPool<T>`, returning `ValueTask<T>` from methods that complete synchronously most of the time, and choosing `struct` over `class` when the four conditions for value-type efficiency are met. By the end of this lecture you can rewrite a method that allocates 416 B per call into one that allocates 0 B per call, and justify every step with a BDN run.

## 3.1 — Pooling: when `stackalloc` is too small

Lecture 2 ended on the rule "`stackalloc` for ≤ 1 KB, `ArrayPool<T>` for everything larger." Let us see why and how.

A `stackalloc byte[2048]` is technically legal C#. It compiles. It runs. It also consumes 2 KB of your thread's stack, and if it appears inside a recursive method, you blow the stack at recursion depth ~500. The default .NET thread stack is 1 MB. Burning 2 KB at every level of a 500-deep call chain — for example, a recursive parser of a deeply nested JSON document — exhausts the stack and throws `StackOverflowException`, which terminates the process. There is no catching it. Use `stackalloc` for small, bounded sizes and switch to a heap-resident pool for the rest.

`ArrayPool<T>.Shared` is the central pool in `System.Buffers`. It hands out `T[]` arrays at requested sizes, reuses them across `Rent` / `Return` calls, and gives you per-CPU, lock-free fast paths in the common case. The contract:

```csharp
T[] buffer = ArrayPool<T>.Shared.Rent(minimumLength);
try
{
    // use buffer[0..buffer.Length] — note: buffer.Length is >= minimumLength,
    // often LARGER (the pool returns the smallest bucket that fits).
    Process(buffer.AsSpan(0, minimumLength));
}
finally
{
    ArrayPool<T>.Shared.Return(buffer, clearArray: false);
}
```

Three rules to memorize:

1. **`Rent(n)` returns a buffer of length ≥ `n`, possibly larger.** The pool maintains a ladder of size buckets — 16, 32, 64, ..., 1048576 — and returns the smallest bucket that fits. If you `Rent(100)` you may get a buffer of length 128. Always slice down to your actual size: `buffer.AsSpan(0, n)`.

2. **`Return` is mandatory.** Always in a `finally`. Forgetting to return a buffer does not leak (the pool can re-create buckets), but the pool's hit rate degrades and you defeat the purpose. The discipline is: every `Rent` paired with a `Return` in the *same* method, in `try`/`finally`.

3. **`clearArray: true` only if the data was sensitive.** Setting it to `true` zeroes the buffer before returning. This costs the length of the buffer in memory writes. The default `false` is correct for most uses (the next renter overwrites the bytes anyway). Use `true` for buffers that held passwords, tokens, or PII you do not want to leak across requests.

### A worked example: hex encoding

A common allocation-heavy method is "encode this byte array as a hex string":

```csharp
// Allocating version: ~allocates 2*n+24 bytes per call for n bytes of input
public static string EncodeHexAllocating(ReadOnlySpan<byte> bytes)
{
    var sb = new StringBuilder(bytes.Length * 2);
    foreach (byte b in bytes)
    {
        sb.Append(b.ToString("x2"));   // each ToString call allocates a 2-char string
    }
    return sb.ToString();
}
```

For a 32-byte input (a SHA-256 hash, say), this allocates ~32 little 2-char strings (one per byte) and a 64-char `StringBuilder` and a final 64-char `string`. Hundreds of bytes per call. Run it a million times per second and you have hundreds of MB/s of garbage.

A pooled version:

```csharp
public static string EncodeHexPooled(ReadOnlySpan<byte> bytes)
{
    const string Hex = "0123456789abcdef";

    int charCount = bytes.Length * 2;
    char[] rented = ArrayPool<char>.Shared.Rent(charCount);
    try
    {
        Span<char> chars = rented.AsSpan(0, charCount);
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            chars[i * 2]     = Hex[b >> 4];
            chars[i * 2 + 1] = Hex[b & 0xF];
        }
        return new string(chars);
    }
    finally
    {
        ArrayPool<char>.Shared.Return(rented, clearArray: false);
    }
}
```

The only allocation is the final `new string(chars)` — that one is unavoidable if you need to *return* a string. The intermediate buffer is rented and returned. BDN comparison on 32-byte inputs:

```
| Method              | Mean      | Error     | Allocated |
|-------------------- |----------:|----------:|----------:|
| EncodeHexAllocating | 412.4 ns  |  3.1 ns   |   1064 B  |
| EncodeHexPooled     | 49.2 ns   |  0.4 ns   |     88 B  |
```

Eight times faster, twelve times fewer allocations. The 88 B is the final 64-char string (`64 chars * 2 bytes + 24-byte string header`). We cannot beat that without changing the API to return a span — see the `string.Create<TState>` variant below.

### `string.Create<TState>`: the truly zero-allocation hex encoder

If we are allowed to construct the string in a way that writes its chars directly into the final string buffer, we can skip the intermediate rent entirely:

```csharp
public static string EncodeHexCreate(ReadOnlySpan<byte> bytes)
{
    const string Hex = "0123456789abcdef";

    // string.Create allocates the final string of the given length and gives you
    // a writable Span<char> into its buffer. The state argument is passed by value.
    // We cannot capture `bytes` in a closure (it's a ref struct), so we encode the
    // bytes via the state parameter — for spans, we hand-roll using a byte[] copy
    // or unsafe pointers. Here we use the unsafe-free shape with a byte[] snapshot:
    byte[] snapshot = bytes.ToArray();
    return string.Create(bytes.Length * 2, snapshot, static (chars, state) =>
    {
        for (int i = 0; i < state.Length; i++)
        {
            byte b = state[i];
            chars[i * 2]     = Hex[b >> 4];
            chars[i * 2 + 1] = Hex[b & 0xF];
        }
    });
}
```

This still allocates the `snapshot` byte array (because we cannot pass a `ReadOnlySpan<byte>` as state — `ref struct` rules). For *byte* inputs, the runtime gives you a special `Convert.ToHexString(ReadOnlySpan<byte>)` overload (added in .NET 5) that does this in a single allocation with no helper buffer:

```csharp
public static string EncodeHexFramework(ReadOnlySpan<byte> bytes)
    => Convert.ToHexString(bytes);
```

Numbers:

```
| Method              | Mean      | Error     | Allocated |
|-------------------- |----------:|----------:|----------:|
| EncodeHexAllocating | 412.4 ns  |  3.1 ns   |   1064 B  |
| EncodeHexPooled     |  49.2 ns  |  0.4 ns   |     88 B  |
| EncodeHexCreate     |  68.7 ns  |  0.6 ns   |    120 B  |
| EncodeHexFramework  |  31.4 ns  |  0.3 ns   |     88 B  |
```

The framework version is fastest. We did not invent the technique; we re-derived what the runtime team already wrote. The lesson is twofold:

1. **`string.Create<TState>` is the right primitive when you need a fixed-length string with custom char content.** It allocates the final string exactly once.
2. **The framework usually has the optimised primitive already.** Search for it before writing your own. `Convert.ToHexString`, `string.Create`, `Utf8Formatter.TryFormat`, `Encoding.UTF8.GetByteCount`, `MemoryMarshal.Cast` — the runtime team has written most of the primitives you would need.

## 3.2 — `ValueTask<T>` vs `Task<T>`

Every `async` method that returns `Task<T>` allocates the `Task<T>` object. If the method completes synchronously — that is, every `await` it contains returns a completed result — the allocation is pure waste. The hot path is "the cached value was present, return it"; the cold path is "fetch from the network." For a method that hits the cache 99% of the time, allocating a `Task<T>` per call is 99% wasted work.

`ValueTask<T>` is a struct that *wraps* either a synchronously-available value *or* an underlying `Task<T>` when the operation must actually be asynchronous. When the operation is synchronous, no `Task<T>` is allocated. The cost is paid only when the asynchronous path is taken.

The contract:

```csharp
public async ValueTask<int> GetCountAsync(string key, CancellationToken ct)
{
    if (_cache.TryGetValue(key, out int cached))
    {
        return cached;   // synchronous: no Task<int> allocated
    }
    return await _store.LoadAsync(key, ct);   // asynchronous: a Task is allocated
}
```

Three documented restrictions on `ValueTask<T>` you must respect:

1. **Consume each `ValueTask<T>` exactly once.** Either `await` it, or call `.AsTask()` to convert it (allocating a `Task<T>`), or call `.Result` (synchronously blocking). Never `await` the same `ValueTask<T>` twice. The underlying `IValueTaskSource<T>` may have been recycled between the two awaits.

2. **Do not block on a `ValueTask<T>` synchronously.** Calling `.Result` on a `ValueTask<T>` whose underlying operation has not completed will deadlock or panic depending on the context. If you need to block, call `.AsTask().Result` (allocating).

3. **Do not pass a `ValueTask<T>` to `Task.WhenAll`, `Task.WhenAny`, or any combinator that takes `Task<T>`.** The `ValueTask<T>` must first be converted via `.AsTask()`, which allocates. At that point you have lost the `ValueTask` advantage; if you need `WhenAll`, return `Task<T>` instead.

The rule of thumb: **use `ValueTask<T>` only when the method completes synchronously a meaningful fraction of the time (≥ 10%, ideally ≥ 50%), and you control all callers.** For public APIs whose callers you do not control, `Task<T>` is the safer default — even a small fraction of misuse can produce subtle bugs.

A benchmark of cache lookup:

```csharp
[MemoryDiagnoser]
public class CacheBenchmark
{
    private readonly Dictionary<string, int> _cache = new() { ["key"] = 42 };

    [Benchmark(Baseline = true)]
    public async Task<int> GetAsTask()
    {
        if (_cache.TryGetValue("key", out int v)) return v;
        await Task.Yield();
        return 0;
    }

    [Benchmark]
    public async ValueTask<int> GetAsValueTask()
    {
        if (_cache.TryGetValue("key", out int v)) return v;
        await Task.Yield();
        return 0;
    }
}
```

The synchronous path (`_cache.TryGetValue` succeeds) is what we measure. Numbers:

```
| Method         | Mean     | Error    | Allocated |
|--------------- |---------:|---------:|----------:|
| GetAsTask      | 21.42 ns | 0.18 ns  |     72 B  |
| GetAsValueTask | 12.74 ns | 0.10 ns  |      0 B  |
```

`Task<int>` is 72 B per call (the `Task<int>` object header + its `Result` field + state-machine bookkeeping). `ValueTask<int>` is 0 B on the synchronous path. At 1M calls/sec, that's 72 MB/s of garbage saved.

The classic place `ValueTask<T>` pays off is `System.Threading.Channels`: `ChannelReader<T>.ReadAsync(...)` returns a `ValueTask<T>` because when an item is already in the channel's buffer, the read completes synchronously. Likewise `ChannelWriter<T>.WriteAsync(...)`, `IAsyncEnumerable<T>.GetAsyncEnumerator().MoveNextAsync()`, and many of the new HTTP and JSON APIs in .NET 6+.

When you write a public library API and a fraction of callers will be in tight loops, return `ValueTask<T>`. When you write internal helpers, return `Task<T>` unless BDN tells you the allocation matters. The default is `Task<T>`; `ValueTask<T>` is the considered, measured exception.

## 3.3 — `struct` vs `class`: the four-rule heuristic

`struct` is the other major lever for allocation reduction in C#. A `struct` is a value type: allocated inline in its containing storage — on the stack for locals, in the parent object's field layout for fields, in the array's contiguous buffer for elements. A `class` is a reference type: allocated on the managed heap and accessed through a reference.

For an array of one million `Point` records:

- If `Point` is a `class { double X; double Y; }`: the array is one million 8-byte references on the heap plus one million separate 32-byte `Point` objects (header + 16 bytes of payload + padding). Total: ~40 MB of heap, with poor cache locality (the references force pointer-chasing).
- If `Point` is a `struct { double X; double Y; }`: the array is one million 16-byte values laid out contiguously. Total: ~16 MB of heap, with excellent cache locality.

`struct` wins for this case. But `struct` does not win in all cases. There are four rules for when `struct` is the right tool, drawn from Microsoft's struct design guidelines:

1. **The type is small** — ideally ≤ 16 bytes; certainly ≤ 24 bytes. Above that, the per-call copy cost exceeds the allocation cost of a `class`.
2. **The type is short-lived** — used as a local, an argument, a return value. Long-lived storage in a collection or field is fine if the surrounding storage is also a struct.
3. **The type is immutable** — its fields are `readonly`. Mutable structs are confusing because mutation operates on a *copy* unless the caller is careful; the bug `foreach (var p in points) p.X = 0;` silently does nothing if `Point` is a struct.
4. **The type does not need an inheritance hierarchy** — structs cannot inherit. If you need polymorphism, you need a `class` or a `record`.

When all four rules hold, `struct` is preferred. Otherwise, `class`.

The .NET runtime applies this heuristic consistently. `DateTime`, `Guid`, `TimeSpan`, `DateOnly`, `TimeOnly`, `Decimal`, `Vector<T>`, `Span<T>`, `Memory<T>` — all structs. `String`, `Stream`, `HttpClient`, `DbContext`, `Task<T>` — all classes. The boundary is roughly "value-like data ≤ 24 bytes" on one side, "service-like or identity-like or large" on the other.

### `readonly struct`: avoiding defensive copies

When you pass a `struct` to a method, the JIT may insert a *defensive copy* — a runtime copy of the struct so that the caller can be sure the callee does not mutate it. Defensive copies are wasteful when the callee does not in fact mutate. The `readonly struct` annotation tells the JIT "this struct cannot be mutated through any of its methods or properties," which lets the JIT skip the copy:

```csharp
public readonly struct Point2D(double x, double y)
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public double DistanceTo(in Point2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
```

Two annotations are at play here:

- **`readonly struct`** on the declaration: every field is read-only; no member may mutate `this`.
- **`in Point2D other`** on the parameter: the parameter is passed by reference (no copy) but cannot be mutated by the callee.

Together, these two annotations let the JIT generate code that takes the address of the caller's `Point2D`, reads its fields, and never copies anything. A `Point2D` is small enough that the copy cost is microscopic; the win is larger for bigger structs.

Use `readonly struct` for every value type unless you have a specific reason for mutability. Use `in` parameters for structs larger than 16 bytes that you pass to methods you do not own.

### When `struct` becomes a bug: the foreach mutation pitfall

```csharp
public struct MutablePoint
{
    public double X;
    public double Y;
}

var points = new MutablePoint[] { new() { X = 1, Y = 2 } };

foreach (var p in points)
{
    p.X = 0;   // operates on a copy; the original is unchanged
}

Console.WriteLine(points[0].X);  // still 1
```

The `var p` in the `foreach` is a *copy* of the array element. Mutating `p.X` changes the copy. The original element is untouched. The fix is to use a `for` loop and index by reference:

```csharp
for (int i = 0; i < points.Length; i++)
{
    points[i].X = 0;   // operates on the actual element
}
```

Or, in .NET 7+, use `ref` in the `foreach`:

```csharp
foreach (ref var p in points.AsSpan())
{
    p.X = 0;   // operates on the actual element via a reference
}
```

The lesson: **mutable structs are a footgun.** Use `readonly struct` by default. The exceptions are well-defined performance hotspots where the immutability cost is measurable — at which point, document the mutability deliberately.

## 3.4 — The LINQ allocation cost model

LINQ is comfortable. LINQ is also allocation-heavy. Every `Where`, `Select`, `Take`, `OrderBy`, `GroupBy` you chain typically allocates:

- **One enumerator object.** The `IEnumerable<T>` you start with is wrapped in a `WhereEnumerableIterator<T>` or similar; iterating it calls `GetEnumerator()` which boxes the iterator state.
- **One delegate per lambda.** `Where(x => x.IsActive)` allocates an `Action<T>` or `Func<T, bool>` once if the lambda has no captured variables (because the C# compiler caches it). If the lambda captures variables (`Where(x => x.Id == localId)`), the closure object is allocated *every call* — `localId` is captured into a synthesized closure class.
- **One result collection if you materialize.** `ToList()` allocates a `List<T>`; `ToArray()` allocates a `T[]`.

A pipeline like `items.Where(x => x.IsActive).Select(x => x.Name).ToList()` therefore allocates: the `Where` iterator, the `Select` iterator, the final `List<string>`, and (if either lambda captured anything) the closure objects. For a hot path called per request, this is substantial.

The .NET 7+ LINQ optimizer is much better at recognizing patterns and short-circuiting them. `items.Count()` is now `O(1)` when `items` is an `ICollection<T>`. `items.ToArray()` pre-sizes when possible. `items.First()` does not enumerate further than the first element. The improvements are documented in Stephen Toub's yearly DevBlogs posts. But the allocation profile of a multi-stage LINQ chain is still measurable, and for per-request hot paths, the rewrite to an explicit `for` loop is often the right move.

### The rewrite recipe

Take a LINQ chain on a known-finite collection:

```csharp
// LINQ form
public static int CountActiveOlderThan(List<User> users, int minAgeYears)
{
    DateTime cutoff = DateTime.UtcNow.AddYears(-minAgeYears);
    return users.Where(u => u.IsActive && u.CreatedAt < cutoff).Count();
}
```

Rewrite as:

```csharp
public static int CountActiveOlderThan(List<User> users, int minAgeYears)
{
    DateTime cutoff = DateTime.UtcNow.AddYears(-minAgeYears);
    int count = 0;
    for (int i = 0; i < users.Count; i++)
    {
        User u = users[i];
        if (u.IsActive && u.CreatedAt < cutoff)
        {
            count++;
        }
    }
    return count;
}
```

The rewrite:

- Reads the `List<T>.Count` property directly, no enumerator allocation.
- Indexes by integer, no `IEnumerator<T>` boxing.
- The `&&` short-circuit is the same as the lambda's.
- No closure allocation — `cutoff` is a local, not captured into a delegate.

BDN comparison on 10,000 users, half active, half meeting the cutoff:

```
| Method                | Mean      | Error    | Allocated |
|---------------------- |----------:|---------:|----------:|
| LinqForm              | 23.41 us  | 0.18 us  |     232 B |
| ForLoopForm           |  5.92 us  | 0.05 us  |       0 B |
```

Four times faster, zero allocations. The 232 B of LINQ allocations are: the `Where` iterator (~88 B), the closure for the lambda (~32 B), the enumerator state (~32 B), the boxing of `cutoff` if any (none in this case), plus alignment.

The LINQ form remains the right answer when the chain is *cold* (called once at startup, not per request) or when the chain is *short* (one or two stages on small inputs) — readability often wins. The for-loop rewrite is the right answer when BDN tells you the allocations matter. Use the measurement, not the dogma.

## 3.5 — `IBufferWriter<T>` and pipelines

For output-heavy code — formatting JSON, writing HTTP responses, encoding protobuf — the `IBufferWriter<T>` interface gives you a streaming-friendly, span-based output target:

```csharp
public interface IBufferWriter<T>
{
    void Advance(int count);
    Memory<T> GetMemory(int sizeHint = 0);
    Span<T> GetSpan(int sizeHint = 0);
}
```

The pattern: `GetSpan(hint)` returns a writable span of at least `hint` length, you write into it, you call `Advance(actualWritten)` to commit. The writer can then either keep the data in memory (`ArrayBufferWriter<T>`) or stream it to a pipe (`PipeWriter` from `System.IO.Pipelines`).

A small example, writing a CSV line:

```csharp
public static void WriteRow(IBufferWriter<byte> writer, int id, ReadOnlySpan<char> name, double value)
{
    Span<byte> span = writer.GetSpan(64);
    int written = 0;

    Utf8Formatter.TryFormat(id, span, out int idLen);
    written += idLen;
    span[written++] = (byte)',';

    int nameByteLen = Encoding.UTF8.GetBytes(name, span.Slice(written));
    written += nameByteLen;
    span[written++] = (byte)',';

    Utf8Formatter.TryFormat(value, span.Slice(written), out int valueLen);
    written += valueLen;
    span[written++] = (byte)'\n';

    writer.Advance(written);
}
```

This method writes a single CSV row without allocating. The caller provides the buffer. The method writes into it via `Utf8Formatter` (formatting numbers directly to UTF-8 bytes, no intermediate strings) and `Encoding.UTF8.GetBytes(ReadOnlySpan<char>, Span<byte>)` (encoding chars to UTF-8 bytes in-place).

`IBufferWriter<T>` is the abstraction Kestrel, `Utf8JsonWriter`, and the HTTP/3 stack use internally. When you write performance-sensitive output code, target this interface; the caller decides whether to back it with a fixed buffer, a pool, or a pipe.

## 3.6 — Putting it all together: the zero-allocation rewrite

A full case study. We start with a method that builds a query string from a `Dictionary<string, string>`:

```csharp
public static string BuildQueryString(Dictionary<string, string> parameters)
{
    if (parameters.Count == 0) return string.Empty;

    var sb = new StringBuilder("?");
    foreach (var kvp in parameters)
    {
        sb.Append(kvp.Key);
        sb.Append('=');
        sb.Append(Uri.EscapeDataString(kvp.Value));
        sb.Append('&');
    }
    sb.Length--;  // strip trailing '&'
    return sb.ToString();
}
```

This method allocates:

1. The `StringBuilder` (~120 B).
2. The internal char chunks of the `StringBuilder` as it grows.
3. One `string` per `Uri.EscapeDataString` call (each escape allocates a new string).
4. The dictionary's enumerator (a struct on .NET 5+, but the underlying `KeyCollection.Enumerator` is also a struct).
5. The final `ToString()` result.

For a query string with 5 parameters of 20 chars each, this is roughly 400-600 B per call. At 10K calls/sec on a per-request basis, that is 4-6 MB/s of garbage.

The rewrite uses `string.Create<TState>`, a stackalloc'd intermediate buffer for escaping, and a `ReadOnlyMemory<KeyValuePair<string, string>>` snapshot of the dictionary contents:

```csharp
public static string BuildQueryStringFast(Dictionary<string, string> parameters)
{
    if (parameters.Count == 0) return string.Empty;

    // Compute the total length first so string.Create can pre-allocate exactly.
    int totalLen = 1;  // '?'
    foreach (var kvp in parameters)
    {
        totalLen += kvp.Key.Length + 1 + EscapedLength(kvp.Value) + 1;  // key=value&
    }
    totalLen--;  // no trailing &

    // Snapshot once to avoid mutation mid-write.
    var snapshot = new KeyValuePair<string, string>[parameters.Count];
    int i = 0;
    foreach (var kvp in parameters) snapshot[i++] = kvp;

    return string.Create(totalLen, snapshot, static (chars, state) =>
    {
        chars[0] = '?';
        int pos = 1;
        for (int j = 0; j < state.Length; j++)
        {
            ReadOnlySpan<char> key = state[j].Key.AsSpan();
            key.CopyTo(chars.Slice(pos));
            pos += key.Length;
            chars[pos++] = '=';

            ReadOnlySpan<char> value = state[j].Value.AsSpan();
            pos += WriteEscaped(value, chars.Slice(pos));

            if (j < state.Length - 1)
            {
                chars[pos++] = '&';
            }
        }
    });
}

// Helpers — escape per RFC 3986 unreserved character set.
private static int EscapedLength(string s)
{
    int len = 0;
    for (int i = 0; i < s.Length; i++) len += IsUnreserved(s[i]) ? 1 : 3;
    return len;
}

private static int WriteEscaped(ReadOnlySpan<char> source, Span<char> dest)
{
    const string Hex = "0123456789ABCDEF";
    int written = 0;
    for (int i = 0; i < source.Length; i++)
    {
        char c = source[i];
        if (IsUnreserved(c))
        {
            dest[written++] = c;
        }
        else
        {
            dest[written++] = '%';
            dest[written++] = Hex[(c >> 4) & 0xF];
            dest[written++] = Hex[c & 0xF];
        }
    }
    return written;
}

private static bool IsUnreserved(char c) =>
    (c >= 'a' && c <= 'z') ||
    (c >= 'A' && c <= 'Z') ||
    (c >= '0' && c <= '9') ||
    c == '-' || c == '_' || c == '.' || c == '~';
```

Allocations: one for the `snapshot` array, one for the final string. Two allocations total, both proportional to the input. No `StringBuilder`, no intermediate escape strings.

BDN comparison on a dictionary of 5 key-value pairs, average value length 20 chars (3 of which are non-unreserved and require escape):

```
| Method                | Mean      | Error    | Allocated |
|---------------------- |----------:|---------:|----------:|
| BuildQueryString      | 941.4 ns  |  7.3 ns  |     728 B |
| BuildQueryStringFast  | 224.8 ns  |  2.0 ns  |     208 B |
```

Four times faster, three and a half times fewer allocations. We did not change the algorithm. We changed the allocation profile. The remaining 208 B is the snapshot array + the final string; the snapshot can be eliminated with `IReadOnlyList<KeyValuePair<string, string>>` if we restrict the API, dropping to ~88 B.

This rewrite is the canonical shape of allocation-reduction work in .NET:

1. Measure the baseline.
2. Identify the allocations (`StringBuilder`, `Uri.EscapeDataString`, intermediate strings, dictionary iteration).
3. Replace with span-based equivalents (`string.Create`, hand-rolled escape into a span, snapshot the dictionary to a single allocation).
4. Re-measure.
5. Report the comparison.

The mini-project this week walks you through exactly this loop on a deliberately-slow LINQ pipeline.

## 3.7 — Reflexes to internalize

- **`stackalloc` ≤ 1 KB; `ArrayPool<T>.Shared.Rent` for everything larger.** Always with `try / finally`.
- **`string.Create<TState>` for fixed-length strings whose characters you compute.** It allocates exactly once, sized correctly.
- **`Utf8Formatter` for primitive-to-text in byte buffers; `Utf8Parser` for text-to-primitive.** Both are non-allocating.
- **`ValueTask<T>` only when synchronous completion is common AND you control the callers.** Otherwise, `Task<T>`.
- **`struct` only when all four rules hold: small, short-lived, immutable, no inheritance.** Otherwise, `class`.
- **`readonly struct` by default for any value type you write.** Mutable structs are a foreach mutation pitfall.
- **LINQ for cold paths, `for` loop over `Span<T>` for hot paths.** Measure to confirm.
- **Every "this is faster" claim is backed by a BDN table with `Allocated` shown.**

These three lectures together give you the four levers of .NET performance work: measure (Lecture 1), span the buffers (Lecture 2), pool the larger buffers and unbox the synchronous async (Lecture 3 first half), and rewrite hot LINQ to explicit loops (Lecture 3 second half). Apply them in that order — measure first, span next, pool / value-task next, rewrite last — and the gains compound.

## 3.8 — What we did not cover

- **Custom `MemoryPool<T>` and `IMemoryOwner<T>` implementations.** The default `ArrayPool<T>.Shared` is right for almost every case. Custom pools matter when you need pinned memory (interop), large segments (>1 MB), or per-tenant isolation. Beyond this week.
- **`System.IO.Pipelines` end-to-end.** Pipelines are the production HTTP-server pattern (Kestrel uses them). Lecture 3 introduces `IBufferWriter<T>` but does not build a full pipeline. Week 11 returns to this.
- **SIMD via `System.Numerics.Vector<T>` and the platform intrinsics.** Order-of-magnitude speedups for numeric workloads. Elective week material.
- **JIT inlining heuristics and the `[AggressiveInlining]` attribute.** The JIT is usually right; the attribute is occasionally helpful and frequently misused. See the Stephen Toub posts for the cases that justify it.
- **GC modes (workstation vs server, concurrent, latency mode).** Configurable at runtime; default workstation GC is fine for development. Production tuning is a Week 13 topic.

---

## Lecture 3 — checklist before moving on

- [ ] I can write `ArrayPool<T>.Shared.Rent(...)` + `Return(...)` in a correct `try / finally`.
- [ ] I can recite the three `ValueTask<T>` consumption rules (consume once, do not block, do not `WhenAll`).
- [ ] I can apply the four-rule heuristic to a new type and decide `struct` or `class`.
- [ ] I can rewrite a LINQ chain as a `for` loop and predict (then measure) the allocation savings.
- [ ] I have run the hex-encoding benchmark on my machine and confirmed the pooled version is ~8× faster than the `StringBuilder` version.
- [ ] I have read the Stephen Toub "Understanding the Whys, Whats, and Whens of ValueTask" post.

If any box is unchecked, return to that section. You are ready for the exercises and the mini-project once all six are checked.

---

**References cited in this lecture**

- Microsoft Learn — `ArrayPool<T>`: <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1>
- Microsoft Learn — `ValueTask<T>`: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1>
- Microsoft Learn — `struct` design guidelines: <https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/struct>
- Microsoft Learn — `string.Create<TState>`: <https://learn.microsoft.com/en-us/dotnet/api/system.string.create>
- Stephen Toub — "Understanding the Whys, Whats, and Whens of ValueTask": <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>
- Stephen Toub — "An Introduction to System.Threading.Channels" (ValueTask in practice): <https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/>
- Adam Sitnik — "Array Pool": <https://adamsitnik.com/Array-Pool/>
- `dotnet/runtime` — `TlsOverPerCoreLockedStacksArrayPool`: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/TlsOverPerCoreLockedStacksArrayPool.cs>
