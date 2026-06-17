# Lecture 2 — `Span<T>` and Stack-Only Buffers

> **Reading time:** ~80 minutes. **Hands-on time:** ~60 minutes (you write a span-based parser and benchmark it against `string.Split`).

`Span<T>` is the most important runtime addition to .NET in the last decade and the one piece of the language whose internals every serious .NET engineer is expected to understand. It was added in .NET Core 2.1 (2018) specifically to give the runtime team — and you — a way to write parsers, formatters, and copy routines that allocate zero managed-heap memory while still being type-safe and bounds-checked. By the end of this lecture you should be able to read a method signature like `ParseInt(ReadOnlySpan<char> source, out int value)`, predict what it costs, write your own span-returning method, and recognize the three compile-time rules the C# compiler enforces against `ref struct` types.

This lecture has more compile-error-prone code than any other in this week. The `ref struct` rules trip everyone the first time. We will go through them deliberately.

## 2.1 — What `Span<T>` is

A `Span<T>` is a small `ref struct` that holds two things: **a managed reference to the start of a contiguous block of memory** and **a length**. That's it. The reference can point at:

- The first element of a `T[]` array on the managed heap.
- A region of memory allocated on the current stack frame via `stackalloc`.
- A region of unmanaged memory obtained from `Marshal.AllocHGlobal` or a `fixed` pointer.
- The interior of another contiguous structure (the chars of a `string`, the bytes of a struct passed as `MemoryMarshal.AsBytes<T>(...)`).

The `Span<T>` API is the *same* against all four. You can index, slice, copy, fill, search, and pass spans without caring where the memory lives:

```csharp
ReadOnlySpan<char> s1 = "hello world".AsSpan();                              // string interior
Span<char>         s2 = stackalloc char[16];                                 // stack
Span<byte>         s3 = new byte[1024].AsSpan();                             // heap array
ReadOnlySpan<byte> s4 = "literal"u8;                                         // UTF-8 string literal (byte span)

int i1 = s1.IndexOf('o');     // 4
s2.Fill('x');                  // fills the stack buffer with 'x'
s3.Slice(0, 100).Clear();      // zero the first 100 bytes
int b1 = s4.IndexOf((byte)'r'); // 4
```

The `IndexOf`, `Fill`, `Slice`, `Clear` methods do not allocate. They operate on the memory the span points at. The slicing operation `s3.Slice(0, 100)` does not copy the underlying bytes — it returns a new `Span<byte>` whose reference points 0 bytes into `s3` and whose length is 100. The original `s3` is unaffected. This is the closest analog to a "pointer + length" you get in C#, except that the pointer is managed (the GC knows about it), the length is bounds-checked at every access, and the type system prevents the worst pointer mistakes at compile time.

The C# compiler enforces these rules by marking `Span<T>` (and `ReadOnlySpan<T>`) as `ref struct`. A `ref struct` has three restrictions you must memorize:

1. **A `ref struct` cannot be a field of a class.** (It would mean a stack-only reference living on the heap, which the GC cannot honor.)
2. **A `ref struct` cannot be a generic type argument of a non-`ref struct` generic type.** (No `List<Span<int>>`, no `Task<Span<int>>`, no `IEnumerable<Span<int>>`.)
3. **A `ref struct` cannot cross an `await`, `yield`, or `lock`.** (The compiler-generated state machine for `async` and `iterator` methods stores locals on the heap, which a `ref struct` cannot do.)

When you violate one of these, the C# compiler gives you a precise error message — `CS8345`, `CS8350`, `CS4012`, `CS8175`, depending on the violation. Read the error message, refactor, move on. You will hit each of these once; after that they become reflex.

## 2.2 — The first useful span: `string.AsSpan()`

Most spans you write start with an existing buffer. The simplest entry point is `string.AsSpan()`, which gives you a `ReadOnlySpan<char>` over the string's internal `char` data:

```csharp
string greeting = "Hello, world!";
ReadOnlySpan<char> span = greeting.AsSpan();

int commaAt = span.IndexOf(',');                // 5
ReadOnlySpan<char> first  = span.Slice(0, commaAt);     // "Hello"
ReadOnlySpan<char> second = span.Slice(commaAt + 2);    // "world!"
```

This code is interesting because **nothing allocates**. The original `greeting` already exists on the heap. The three `ReadOnlySpan<char>` values are stack-resident structs that each hold a pointer into `greeting`'s buffer plus a length. The "substrings" `first` and `second` are not new strings — they are views into the same chars.

Compare it with the allocating version:

```csharp
string greeting = "Hello, world!";
int commaAt = greeting.IndexOf(',');                // 5
string first  = greeting.Substring(0, commaAt);     // ALLOCATES a new string "Hello"
string second = greeting.Substring(commaAt + 2);    // ALLOCATES a new string "world!"
```

The two `Substring` calls each allocate a new heap string with its own char buffer. For a 13-character `greeting` you allocate roughly 32 bytes per substring (the string object header + the chars). Multiply by a million invocations per second on a hot endpoint and you have 64 MB/s of garbage.

The reason `Span<T>` exists is exactly this: when the question is "I have a buffer; I want to look inside it, at a piece of it, possibly write a parser over it" — `Span<T>` answers the question without allocating. The whole runtime has been retrofitted with span-accepting overloads in `.NET Core 2.1`, `.NET 5`, `.NET 7`, `.NET 8`, and `.NET 9`. Every `Substring`-like API now has a `Slice`-equivalent. Every `IndexOf`-on-string now has an `IndexOf`-on-`ReadOnlySpan<char>`. The runtime team's policy is: when there is an allocating API, there should be a non-allocating equivalent on `ReadOnlySpan<char>` or `Span<T>`.

## 2.3 — `stackalloc`: spans backed by the stack

Beyond reading existing buffers, the other major use of spans is *writing* into a temporary buffer that you do not want to allocate on the heap. The `stackalloc` expression gives you that buffer on the current stack frame:

```csharp
Span<char> buffer = stackalloc char[16];
buffer[0] = 'H';
buffer[1] = 'i';
buffer.Slice(2).Fill('!');

string result = new string(buffer);  // ALLOCATES exactly once, at the end
```

The `stackalloc char[16]` reserves 32 bytes on the current stack frame (16 chars × 2 bytes/char). When the method returns, the frame is destroyed and the 32 bytes are reclaimed for free. No GC involvement. The `Span<char>` you build over the stack memory is bounds-checked just like a `Span<T>` over an array — out-of-range index throws `IndexOutOfRangeException`.

Two rules apply to `stackalloc`:

**Rule 1 — keep it small.** The default stack size in .NET is 1 MB per thread (configurable, but 1 MB is the production default). A 256-byte `stackalloc` inside a deeply recursive method may blow the stack. The conventional safe ceiling for a single `stackalloc` is **1024 bytes (1 KB)**. Above that, switch to `ArrayPool<T>` (next lecture). Below that, `stackalloc` is the right tool.

**Rule 2 — check the size before `stackalloc` when the size is variable.** This is the bug pattern:

```csharp
// BUG: if `length` is attacker-controlled or unbounded, this blows the stack.
public static int Parse(int length)
{
    Span<byte> buffer = stackalloc byte[length];   // length comes from outside
    ...
}
```

The defensive form:

```csharp
public static int Parse(int length)
{
    const int StackAllocThreshold = 1024;
    Span<byte> buffer = length <= StackAllocThreshold
        ? stackalloc byte[length]
        : new byte[length];
    ...
}
```

This pattern — `stackalloc` if small, `new T[]` (or `ArrayPool<T>.Shared.Rent`) if large — appears throughout the .NET runtime source. Search `dotnet/runtime` for `StackAllocThreshold` and you will find dozens of instances. It is the right pattern.

There is one C# language subtlety to know: a `stackalloc` expression has type `Span<T>` only when assigned to a `Span<T>`. When assigned to a `T*` (a pointer), the same expression has pointer type and requires `unsafe` context. We do not use the pointer form in this course.

## 2.4 — `ReadOnlySpan<T>`, `Span<T>`, and immutability

`Span<T>` lets you write to the underlying buffer. `ReadOnlySpan<T>` does not — its indexer returns a `T` by value and the slice methods return `ReadOnlySpan<T>` back. You should prefer `ReadOnlySpan<T>` everywhere a method does not need to mutate its argument:

```csharp
// Good: signature signals "I will not mutate the buffer."
public static int CountAsciiLetters(ReadOnlySpan<char> input)
{
    int count = 0;
    for (int i = 0; i < input.Length; i++)
    {
        char c = input[i];
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
        {
            count++;
        }
    }
    return count;
}
```

Callers can pass either a `Span<char>` or a `ReadOnlySpan<char>` here — the conversion from `Span<T>` to `ReadOnlySpan<T>` is implicit. The reverse is not; the compiler will not let you pass a `ReadOnlySpan<char>` to a method expecting `Span<char>`. The asymmetry is intentional and matches the principle "narrow the surface area you grant your callees."

There is a related compile-time annotation: `[ReadOnly]` on struct fields, `readonly struct`, and `readonly ref struct`. The runtime treats `ReadOnlySpan<T>` as `readonly ref struct`, which lets the JIT skip defensive copies when you pass it around. Mark your own `ref struct` types `readonly` if all their fields are read-only.

## 2.5 — Slicing without allocating

Slicing is the operation that makes spans usable for parsing. The `Slice` method has two overloads:

```csharp
public Span<T> Slice(int start);                  // from `start` to the end
public Span<T> Slice(int start, int length);      // from `start` for `length` items
```

Both run in **O(1)**: they construct a new span struct with a new reference (pointer + `start`) and a new length. No allocation. No copy. The original span is unchanged.

C# also gives you the range syntax `[start..end]` that translates to a `Slice` call:

```csharp
ReadOnlySpan<char> s = "hello, world".AsSpan();
ReadOnlySpan<char> head = s[..5];      // "hello"   — same as s.Slice(0, 5)
ReadOnlySpan<char> tail = s[7..];      // "world"   — same as s.Slice(7)
ReadOnlySpan<char> mid  = s[2..^2];    // "llo, wor" — `^2` is "2 from the end"
```

`s[..5]` is the form most production code uses. It reads cleanly and the JIT compiles it to the same code as `s.Slice(0, 5)`.

Here is a parser that uses slicing to split a `key=value` pair without allocating:

```csharp
public static bool TryParseKeyValue(
    ReadOnlySpan<char> input,
    out ReadOnlySpan<char> key,
    out ReadOnlySpan<char> value)
{
    int eq = input.IndexOf('=');
    if (eq < 0)
    {
        key = default;
        value = default;
        return false;
    }
    key   = input[..eq].Trim();
    value = input[(eq + 1)..].Trim();
    return true;
}
```

Two things to notice:

1. **The `out` parameters are `ReadOnlySpan<char>`.** A `ref struct` cannot live on the heap, but it *can* be an `out` parameter — the storage is on the caller's stack frame, which is fine. The C# compiler verifies that the spans returned do not outlive the input.
2. **The `.Trim()` returns another `ReadOnlySpan<char>`.** It does not allocate a new string. It returns a span into the original buffer with the leading and trailing whitespace excluded.

A benchmark of this against `string.Split('=')` on a 30-character input:

```
| Method              | Mean       | Error     | Allocated  |
|-------------------- |-----------:|----------:|-----------:|
| StringSplit         |  72.41 ns  |  0.62 ns  |     128 B  |
| TryParseKeyValueSpan|   8.93 ns  |  0.07 ns  |       0 B  |
```

Eight times faster, zero allocations. The reason: `string.Split` allocates a `string[]` plus the two substrings inside it; `TryParseKeyValue` constructs three stack-resident span structs and is done.

## 2.6 — UTF-8 spans: the `"literal"u8` syntax

C# 11 (and forward) introduced the `u8` literal suffix, which produces a `ReadOnlySpan<byte>` of UTF-8 encoded bytes directly at compile time:

```csharp
ReadOnlySpan<byte> contentType = "application/json"u8;
ReadOnlySpan<byte> crlf        = "\r\n"u8;
```

This is the literal form for parsers and formatters that operate on UTF-8 bytes — HTTP headers, JSON, log lines. There is no encoding step at run time; the compiler emits the bytes directly into the assembly's read-only data section. The runtime team has retrofitted enormous swaths of `dotnet/runtime` to use `u8` literals where they used to use `Encoding.UTF8.GetBytes("...")`.

Use the `u8` form for any byte literal you would have otherwise computed at startup. It is free.

## 2.7 — The relationship to `Memory<T>` and `ReadOnlyMemory<T>`

`Span<T>` is a `ref struct`, so it cannot cross an `await`:

```csharp
public async Task ProcessAsync(Span<byte> buffer)   // ERROR CS4012
{
    await SomethingAsync();   // compiler error: ref struct cannot cross await
    Process(buffer);
}
```

When you need a span-shaped API that *can* cross `await`, the runtime gives you `Memory<T>` (and `ReadOnlyMemory<T>`). These are regular structs (not `ref struct`) that hold a reference to a memory region but live on the heap when boxed. The trade-off:

- `Memory<T>` can be a field of a class, a captured local in `async`, a generic type argument.
- `Memory<T>` is a fatter struct (24 bytes vs `Span<T>`'s 16) and may involve a virtual dispatch on `.Span` access (it is backed by a `MemoryManager<T>`).
- `Memory<T>.Span` returns a `Span<T>` for the synchronous body of the method.

Use `Memory<T>` *only* as the parameter to an `async` method that needs to read a buffer. Inside the method body, before the first `await` or after the last one, convert to `Span<T>` for the actual reading:

```csharp
public async Task<int> CountAsciiLettersAsync(ReadOnlyMemory<char> input, CancellationToken ct)
{
    await Task.Yield();  // simulates async work
    return CountAsciiLetters(input.Span);   // synchronous work on the span
}
```

The boundary between `Span<T>` and `Memory<T>` is one of the topics newcomers most often get wrong. The mental model: **`Span<T>` is the fast, stack-only form; `Memory<T>` is the form you use when stack-only is incompatible with `async`.** Prefer `Span<T>`. Fall back to `Memory<T>` only when you must.

## 2.8 — Bounds checking and bounds-check elision

Every span indexer and `Slice` call is bounds-checked. The JIT inserts a comparison + conditional branch before each access:

```csharp
public int Sum(ReadOnlySpan<int> data)
{
    int s = 0;
    for (int i = 0; i < data.Length; i++)
    {
        s += data[i];   // JIT emits: if (i >= data.Length) throw; else load data[i]
    }
    return s;
}
```

The bounds check is essential for correctness. It is also a non-trivial cost in a tight loop — the conditional branch is well-predicted but the comparison itself takes a cycle. Modern .NET JITs (Tier-1, especially in .NET 9) recognize the `for (int i = 0; i < data.Length; i++)` pattern and **elide** the per-iteration check, because it can prove statically that `i` is always within `[0, data.Length)`.

Elision works when:

- The loop's upper bound is the exact span's `.Length` property.
- The index variable is monotonically increasing by 1 (or by another span-friendly stride).
- No re-assignment of the span happens inside the loop.

Elision does *not* work when:

- The upper bound is a captured local that aliases `data.Length` (the JIT can be conservative here).
- The index variable is mutated inside the loop body.
- The loop runs backwards (`for (int i = data.Length - 1; i >= 0; i--)`) — recent JIT improvements handle this, but older runtimes may not.

You can confirm whether the JIT elided the bounds check with `[DisassemblyDiagnoser]` on the benchmark. The presence of a `cmp` + `jge` (or `jae`) instruction before each load means the check is still there. Absence means it was elided.

The practical takeaway: **write your hot loops in the canonical `for (int i = 0; i < data.Length; i++)` form** and trust the JIT. Do not micro-optimize to `Unsafe.Add` and `Unsafe.AsRef` until you have BDN evidence that the bounds check is a measurable cost — which is rare on .NET 8+.

## 2.9 — `MemoryMarshal` and reinterpretation

`System.Runtime.InteropServices.MemoryMarshal` gives you a few low-level operations that step outside the regular span surface:

- **`MemoryMarshal.Cast<TFrom, TTo>(Span<TFrom>)`** — reinterpret a span as a span of a different element type. Useful for treating a `Span<byte>` as a `Span<int>` for bulk reads/writes.
- **`MemoryMarshal.AsBytes<T>(Span<T>)`** — get the byte view of any blittable span. Useful for serialization.
- **`MemoryMarshal.GetReference(Span<T>)`** — get a `ref T` to the first element. Lower-level than indexing; used in `Unsafe.Add`-style code.
- **`MemoryMarshal.CreateReadOnlySpan(ref T, int)`** — construct a span from a single reference + a length. Used to build spans over fixed-size struct fields.

These are tools for the runtime team and the small fraction of user code that needs them. We mention them so you can recognize them in `dotnet/runtime` source; we do not use them in this week's exercises. They are pre-shadow material for an elective week.

## 2.10 — A worked example: `int.TryParse(ReadOnlySpan<char>)` from scratch

Let us put everything together and write `TryParseInt` over a span. The .NET runtime already includes `int.TryParse(ReadOnlySpan<char>, out int)`; we write our own to see what is involved.

```csharp
public static bool TryParseInt(ReadOnlySpan<char> input, out int value)
{
    value = 0;
    if (input.IsEmpty)
    {
        return false;
    }

    int  i        = 0;
    bool negative = false;

    if (input[0] == '-')
    {
        negative = true;
        i = 1;
    }
    else if (input[0] == '+')
    {
        i = 1;
    }

    if (i == input.Length)
    {
        return false;  // "-" or "+" alone is not an int
    }

    int result = 0;
    for (; i < input.Length; i++)
    {
        char c = input[i];
        if (c < '0' || c > '9')
        {
            value = 0;
            return false;
        }
        int digit = c - '0';

        // Check for overflow before multiplying.
        if (result > (int.MaxValue - digit) / 10)
        {
            value = 0;
            return false;
        }

        result = result * 10 + digit;
    }

    value = negative ? -result : result;
    return true;
}
```

Notable properties of this method:

1. **It allocates zero bytes.** No intermediate strings, no boxing, no array.
2. **It returns a `bool` and uses an `out` parameter.** This is the conventional .NET shape for non-throwing parsers. It is faster than exception-based parsing by an enormous factor.
3. **The signature accepts `ReadOnlySpan<char>`.** Callers can pass:
   - A `string` (implicit conversion via `string.op_Implicit` to `ReadOnlySpan<char>`).
   - A `string.AsSpan()` result (explicit, no conversion).
   - A `char[].AsSpan()` result.
   - A `stackalloc char[N]` buffer.
   - A `Slice` of any of the above.
4. **Overflow is handled explicitly.** The check `result > (int.MaxValue - digit) / 10` is the standard safe-multiply pattern. Real `int.TryParse` handles overflow the same way.

A benchmark against `int.Parse(string)`:

```csharp
[MemoryDiagnoser]
public class ParseBenchmark
{
    private readonly string _str = "1234567";

    [Benchmark(Baseline = true)]
    public int FrameworkParse() => int.Parse(_str);

    [Benchmark]
    public int FrameworkTryParseSpan()
    {
        int.TryParse(_str.AsSpan(), out int v);
        return v;
    }

    [Benchmark]
    public int CustomTryParseSpan()
    {
        TryParseInt(_str.AsSpan(), out int v);
        return v;
    }

    private static bool TryParseInt(ReadOnlySpan<char> input, out int value) { /* as above */ }
}
```

Numbers from my machine:

```
| Method                | Mean      | Error     | Allocated |
|---------------------- |----------:|----------:|----------:|
| FrameworkParse        |  16.21 ns |  0.13 ns  |       0 B |
| FrameworkTryParseSpan |  12.93 ns |  0.10 ns  |       0 B |
| CustomTryParseSpan    |  14.07 ns |  0.12 ns  |       0 B |

```

Three takeaways:

- **Our hand-written parser is competitive with the framework's** — within 10%, sometimes faster, sometimes slower depending on the input. The framework is doing more work (locale-aware digit parsing, optional whitespace skipping) and has SIMD-accelerated paths for long inputs.
- **`TryParse` is faster than `Parse` even on success.** The reason: `Parse` has to be ready to throw `FormatException`, which involves a try/catch path the JIT must keep alive. `TryParse` is straight-line code.
- **None of the three allocate.** Even the original `int.Parse(_str)` does not allocate (because the result is a value type and the parser is span-based internally on .NET 6+).

The lesson is not "write your own parsers." The framework's parsers are excellent. The lesson is: **a span-based parser is a non-allocating parser**, and the framework gives you span overloads for almost every parsing API. Use them.

## 2.11 — The compile-error tour

You will hit these errors. Each one teaches you a `ref struct` rule.

### `CS8345`: cannot use a `ref struct` as a field

```csharp
public class Buffer        // ERROR CS8345
{
    private Span<byte> _data;
}
```

**Fix:** make `Buffer` a `ref struct` itself, *or* store a `byte[]` and expose `.AsSpan()`.

### `CS8350`: can't capture a `ref struct` in a lambda

```csharp
public static void Process(ReadOnlySpan<char> input)
{
    Action a = () => Console.WriteLine(input.Length);  // ERROR CS8350
}
```

**Fix:** don't capture the span. Capture an `int` (the length you need) or an `ReadOnlyMemory<char>`.

### `CS4012`: can't have a `ref struct` local across an `await`

```csharp
public static async Task Process(ReadOnlyMemory<char> input)
{
    ReadOnlySpan<char> span = input.Span;     // OK so far
    await Task.Yield();                        // ERROR CS4012 — `span` cannot survive
    Console.WriteLine(span.Length);
}
```

**Fix:** convert to `Memory<T>` until after the awaits, then `.Span` again:

```csharp
public static async Task Process(ReadOnlyMemory<char> input)
{
    await Task.Yield();
    Console.WriteLine(input.Span.Length);   // Span derived AFTER the await
}
```

### `CS8175`: returning a `ref struct` that aliases a local

```csharp
public static ReadOnlySpan<int> First(int x, int y)
{
    Span<int> tmp = stackalloc int[2];      // local stack memory
    tmp[0] = x;
    tmp[1] = y;
    return tmp;                              // ERROR CS8175 — span outlives the stack frame
}
```

**Fix:** allocate on the heap (defeats the purpose), or restructure so the caller owns the buffer and passes it in:

```csharp
public static void First(Span<int> output, int x, int y)
{
    output[0] = x;
    output[1] = y;
}
```

The compiler errors are not arbitrary. They prevent the C# program from doing things the GC would not be able to honor (a span on the heap, a span surviving its backing stack frame). Read the error, ask "which rule did I break?", refactor.

## 2.12 — When *not* to use `Span<T>`

Spans are not the right tool for every problem. The non-uses, briefly:

- **When you need to return collected data from an `async` method.** A `Task<Span<T>>` does not compile. Use `Memory<T>` or a `Task<T[]>` or a `Task<ImmutableArray<T>>`.
- **When the data needs to live longer than the calling method.** Spans are stack-only. If you need the buffer next request, allocate it (and consider `ArrayPool<T>`).
- **When the API surface should be public and stable.** A `Span<T>` parameter on a public API forces every caller to think about `ref struct` rules. For an internal helper, that is fine; for a public package API, consider whether `ReadOnlyMemory<T>` is more friendly.
- **When the input is genuinely small and called rarely.** A method that runs once at startup does not need to be zero-allocation. Save the engineering effort.

The first two are hard rules. The third is a judgement call. The fourth is the difference between "engineering" and "engineering theater."

## 2.13 — Reflexes to internalize

Build the following reflexes this week:

- **Every parser signature takes `ReadOnlySpan<char>` or `ReadOnlySpan<byte>`.** Not `string`. Not `byte[]`. The span form accepts both *and* is non-allocating.
- **Every temporary buffer ≤ 1 KB starts life as `stackalloc`.** Above 1 KB, switch to `ArrayPool<T>` (Lecture 3).
- **Every "I need to look inside this string" path goes through `.AsSpan()` and `Slice`, not `Substring`.**
- **Every UTF-8 byte literal uses the `u8` suffix.** Not `Encoding.UTF8.GetBytes("...")`.
- **Every `Memory<T>` use is justified by an `await`.** Without an `await`, `Span<T>` is the right type.
- **Every compile error involving a `ref struct` is read carefully.** The compiler is preventing a real bug; the fix is structural, not a `#pragma`.

These reflexes plus Lecture 1's measurement reflexes are 80% of practical .NET performance work. Lecture 3 covers the remaining 20%: pooling for buffers larger than 1 KB, `ValueTask` for the synchronous-async case, and the struct-vs-class trade-off.

---

## Lecture 2 — checklist before moving on

- [ ] I can read the signature `ParseInt(ReadOnlySpan<char> input, out int value)` and predict what it costs.
- [ ] I can state the three `ref struct` rules without reaching for the lecture.
- [ ] I can write a method that takes `ReadOnlySpan<char>`, slices it, searches it, and returns without allocating.
- [ ] I can explain when to use `Memory<T>` instead of `Span<T>` (when the API crosses an `await`).
- [ ] I can use `stackalloc` for ≤ 1 KB temporary buffers and `ArrayPool<T>` for larger ones.
- [ ] I have looked at `[DisassemblyDiagnoser]` output for a simple span loop and located the bounds check (or its absence).

If any box is unchecked, return to that section. Lecture 3 assumes you are comfortable with span signatures and `ref struct` rules.

---

**References cited in this lecture**

- Microsoft Learn — `Span<T>` overview: <https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/>
- Microsoft Learn — `ref struct` types: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct>
- Microsoft Learn — `stackalloc` expression: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc>
- Microsoft Learn — Memory and span usage guidelines: <https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines>
- Adam Sitnik — "Span": <https://adamsitnik.com/Span/>
- `dotnet/runtime` — `MemoryMarshal` source: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs>
- Stephen Toub — "Performance Improvements in .NET 9" (Span section): <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
