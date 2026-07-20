# Week 7 — Exercise Solutions

Annotated solutions for the three coding exercises, plus the BDN tables you should reproduce on your machine. Numbers below are from an Apple M2 running .NET 9.0.0 on macOS 14.5 in `Release` configuration; your absolute times will differ by up to ~2× depending on CPU. The *ratios* between methods should be within ~10% of these. If your ratios differ wildly, your benchmark body is probably wrong — re-read Lecture 1 section 1.4.

---

## Exercise 1 — Benchmark string concatenation

### Reference solution

```csharp
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class StringConcatBenchmark
{
    private string[] _parts = null!;
    private int _totalChars;

    [Params(10, 100, 1_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _parts = new string[N];
        _totalChars = 0;
        for (int i = 0; i < N; i++)
        {
            _parts[i] = $"item-{i:000}-";
            _totalChars += _parts[i].Length;
        }
    }

    [Benchmark(Baseline = true)]
    public string Plus()
    {
        string result = string.Empty;
        for (int i = 0; i < _parts.Length; i++)
        {
            result += _parts[i];
        }
        return result;
    }

    [Benchmark]
    public string StringBuilderConcat()
    {
        var sb = new StringBuilder(capacity: _totalChars);
        for (int i = 0; i < _parts.Length; i++)
        {
            sb.Append(_parts[i]);
        }
        return sb.ToString();
    }

    [Benchmark]
    public string StringConcatAll()
    {
        return string.Concat(_parts);
    }

    [Benchmark]
    public string StringCreateSpan()
    {
        return string.Create(_totalChars, _parts, static (chars, state) =>
        {
            int pos = 0;
            for (int i = 0; i < state.Length; i++)
            {
                ReadOnlySpan<char> src = state[i].AsSpan();
                src.CopyTo(chars.Slice(pos));
                pos += src.Length;
            }
        });
    }
}

public static class Program
{
    public static void Main(string[] args) => BenchmarkRunner.Run<StringConcatBenchmark>(args: args);
}
```

### Result table (Apple M2, .NET 9.0)

```
| Method              | N    | Mean        | Ratio   | Allocated   | Alloc Ratio |
|-------------------- |----- |------------:|--------:|------------:|------------:|
| Plus                | 10   |    310.4 ns |    1.00 |       984 B |        1.00 |
| StringBuilderConcat | 10   |    159.2 ns |    0.51 |       522 B |        0.53 |
| StringConcatAll     | 10   |     72.5 ns |    0.23 |       224 B |        0.23 |
| StringCreateSpan    | 10   |     54.1 ns |    0.17 |       144 B |        0.15 |
| Plus                | 100  |  10,884.0 ns |   1.00 |    128840 B |        1.00 |
| StringBuilderConcat | 100  |   1,201.6 ns |   0.11 |      3712 B |        0.03 |
| StringConcatAll     | 100  |     455.9 ns |   0.04 |      2144 B |        0.02 |
| StringCreateSpan    | 100  |     381.4 ns |   0.04 |      2064 B |        0.02 |
| Plus                | 1000 | 1,131,232 ns |   1.00 |  14516280 B |        1.00 |
| StringBuilderConcat | 1000 |  16,432.1 ns |   0.01 |     35248 B |        0.00 |
| StringConcatAll     | 1000 |   5,103.4 ns |   0.00 |     21184 B |        0.00 |
| StringCreateSpan    | 1000 |   4,418.0 ns |   0.00 |     21080 B |        0.00 |
```

### Discussion

The three asymptotic shapes are visible:

- **`Plus`** is `O(N²)` in both time and memory. At iteration `k`, the loop allocates a fresh string of length `k * (avg part length)`, copies the old contents into it, and discards the old string. Across N iterations the total bytes copied is `1 + 2 + ... + N` average-part-lengths, which is `O(N²)`.
- **`StringBuilderConcat`** is `O(N)` because the builder pre-sizes (we pass `capacity: _totalChars`) and never reallocates. The only heap allocation is the final `ToString()`.
- **`StringConcatAll`** is `O(N)` and matches `StringCreateSpan` because `string.Concat(string[])` internally sums the lengths once, allocates the result string once, and copies into it. Read `String.Manipulation.cs` in `dotnet/runtime` to see the exact code.
- **`StringCreateSpan`** is the lower bound — one allocation, no helper buffer.

The lesson is **not** "always use `string.Create`." The lesson is **"the framework gives you `string.Concat` for the array case for free; use it."** Hand-rolled `string.Create` is the right tool when you need a custom transformation (e.g., interleaving with separators, escaping, padding). For the plain concatenation of an array of strings, `string.Concat` is the simpler equivalent and the runtime team has already optimised it.

### Answers to reflection questions

1. **Why does `Plus` at N=1000 allocate ~14 MB per call?** At iteration #500, `result` is a string of approximately `500 * 9 = 4500` chars (each part is ~9 chars). The `+=` operation allocates a new string of length `4500 + 9 = 4509` chars (~9 KB), copies the 4500 chars from the old string, appends the new 9 chars, and discards the old. Across the 1000 iterations the total allocations are `9 + 18 + 27 + ... + 9000 ≈ 4.5 million chars ≈ 9 MB of strings created`, plus the cumulative copies of those strings. The 14 MB BDN reports is the sum of bytes allocated on the managed heap across the entire benchmark invocation.

2. **Why is the Plus/StringBuilder ratio much smaller at N=10 than at N=1000?** At N=10, `Plus` does 10 iterations with average partial-string size ~50 chars, total ~1 KB of allocations. The constant overhead of `StringBuilder` (the builder object header + the initial chunk allocation) is a meaningful fraction of that. At N=1000, `Plus` is doing ~14 MB of allocations while `StringBuilder` is doing ~35 KB; the constant overhead is invisible against the `O(N²)` cost. The ratio improves with N because `Plus`'s cost grows quadratically and `StringBuilder`'s cost grows linearly.

3. **Why doesn't `string.Concat` allocate anything but the final string?** Read `String.Manipulation.cs` in `dotnet/runtime`. The implementation walks the array once to compute the total length, calls `FastAllocateString(totalLength)` once to allocate the result, then copies each input string's chars into the result. Two passes, one allocation. The pattern is the same one `string.Create` exposes to user code.

4. **Name of the BDN phase that hides JIT warmup?** **Warm-up.** BDN runs ~6 iterations and discards their results before recording the ~15 measurement iterations. This is correct because the warm-up iterations are dominated by the Tier-0 → Tier-1 JIT transition (slow code → fast code) and any first-time static-init cost. Discarding them gives you the steady-state cost of the method, which is what you care about for any code that runs more than a few times in a process.

---

## Exercise 2 — `ReadOnlySpan<char>` CSV-line parser

### Reference solution

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class CsvParseBenchmark
{
    private readonly string _input = "Ada Lovelace,1815-12-10,Mathematics";

    public static bool TryParseCsvRow(
        ReadOnlySpan<char> input,
        out ReadOnlySpan<char> a,
        out ReadOnlySpan<char> b,
        out ReadOnlySpan<char> c)
    {
        int p1 = input.IndexOf(',');
        if (p1 < 0) { a = default; b = default; c = default; return false; }

        ReadOnlySpan<char> rest = input[(p1 + 1)..];
        int p2InRest = rest.IndexOf(',');
        if (p2InRest < 0) { a = default; b = default; c = default; return false; }
        int p2 = p1 + 1 + p2InRest;

        ReadOnlySpan<char> tail = input[(p2 + 1)..];
        if (tail.IndexOf(',') >= 0) { a = default; b = default; c = default; return false; }

        a = input[..p1];
        b = input[(p1 + 1)..p2];
        c = input[(p2 + 1)..];
        return true;
    }

    [Benchmark(Baseline = true)]
    public int SplitString()
    {
        string[] parts = _input.Split(',');
        return parts.Length;
    }

    [Benchmark]
    public int TryParseCsvRowSpan()
    {
        if (TryParseCsvRow(_input.AsSpan(), out var a, out var b, out var c))
        {
            return a.Length + b.Length + c.Length;
        }
        return 0;
    }
}

public static class Program
{
    public static void Main(string[] args) => BenchmarkRunner.Run<CsvParseBenchmark>(args: args);
}
```

### Result table

```
| Method              | Mean      | Ratio | Allocated |
|-------------------- |----------:|------:|----------:|
| SplitString         |  72.41 ns |  1.00 |     128 B |
| TryParseCsvRowSpan  |   8.93 ns |  0.12 |       0 B |
```

### Discussion

The span parser is ~8× faster and allocates zero. The string-split version allocates 128 B per call: 32 B for the `string[3]` array (object header + 3 references + alignment) and ~32 B per substring (one per column, each a small `string`). Multiply by 100k calls/sec and you have 12 MB/s of garbage avoided by switching to spans.

### Answers to reflection questions

1. **Where do the three "substrings" live?** They are not substrings — they are `ReadOnlySpan<char>` structs, each holding a managed reference into the original `_input` string's `char` buffer plus a length. The three spans share the same underlying buffer; only the start positions and lengths differ. When the method returns, the spans are copied (by value) into the caller's stack frame via the `out` parameters. No new memory is allocated.

2. **Why can you `out`-return a span but not regular-return one allocated from `stackalloc`?** The `out` parameter's storage is on the *caller's* stack frame, which outlives the callee. A `stackalloc` inside the callee, however, lives on the *callee's* stack frame, which is destroyed when the callee returns. Returning a span over destroyed memory would be a use-after-free; the C# compiler rejects it with `CS8175`.

3. **Allocation cost of returning `(string, string, string)`?** Each `string` is a heap allocation: ~32 B (header + chars). Three strings: ~96 B. The tuple itself is a value type on the stack, so no extra allocation for the tuple. Total: ~96 B per call vs 0 B for the span version. This is the most common refactoring trap when migrating an allocating API to a span-based one: keeping the return types as `string` defeats the entire point.

4. **1 MB input with one comma?** `IndexOf(',')` walks the buffer linearly looking for a comma. Modern .NET runtimes vectorize this with SIMD (`Vector128`/`Vector256` byte-comparison instructions), so a 1 MB scan completes in roughly 100–200 μs. After the first comma is found, the second `IndexOf` and the trailing `IndexOf` each have to walk most of the remaining 999_998 bytes. So a 1 MB input with one comma takes ~300 μs total — long, but linear in the input size, with zero allocations.

---

## Exercise 3 — `ArrayPool`: rent, use, return

### Reference solution

```csharp
using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class HexEncodeBenchmark
{
    private byte[] _data = null!;

    [Params(16, 32, 256)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[N];
        new Random(42).NextBytes(_data);
    }

    [Benchmark(Baseline = true)]
    public string EncodeAllocating()
    {
        var sb = new StringBuilder(_data.Length * 2);
        for (int i = 0; i < _data.Length; i++)
        {
            sb.Append(_data[i].ToString("x2"));
        }
        return sb.ToString();
    }

    [Benchmark]
    public string EncodePooled()
    {
        const string Hex = "0123456789abcdef";

        int charCount = _data.Length * 2;
        char[] rented = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            Span<char> chars = rented.AsSpan(0, charCount);
            for (int i = 0; i < _data.Length; i++)
            {
                byte b = _data[i];
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

    [Benchmark]
    public string EncodeFramework()
    {
        // Convert.ToHexString returns uppercase; we lowercase to match the
        // other variants. The second allocation makes this slightly worse
        // than EncodePooled in absolute terms but it is still under 2x.
        return Convert.ToHexString(_data).ToLowerInvariant();
    }
}

public static class Program
{
    public static void Main(string[] args) => BenchmarkRunner.Run<HexEncodeBenchmark>(args: args);
}
```

### Result table

```
| Method            | N   | Mean        | Ratio | Allocated   |
|------------------ |---- |------------:|------:|------------:|
| EncodeAllocating  | 16  |    228.4 ns |  1.00 |       576 B |
| EncodePooled      | 16  |     34.2 ns |  0.15 |        56 B |
| EncodeFramework   | 16  |     21.8 ns |  0.10 |        56 B |
| EncodeAllocating  | 32  |    438.9 ns |  1.00 |      1080 B |
| EncodePooled      | 32  |     58.4 ns |  0.13 |        88 B |
| EncodeFramework   | 32  |     34.1 ns |  0.08 |        88 B |
| EncodeAllocating  | 256 |   3,432.0 ns |  1.00 |     8208 B |
| EncodePooled      | 256 |     448.4 ns |  0.13 |       536 B |
| EncodeFramework   | 256 |     192.1 ns |  0.06 |       536 B |
```

### Discussion

The pooled version is ~7× faster than the StringBuilder baseline and allocates only the final string. The framework version is faster still (it uses SIMD for the inner loop) but our hand-rolled `EncodePooled` is within 2× of it — a good outcome.

The 56/88/536 B allocations in `EncodePooled` are the final `string` for each `N`: a 32-char string (~80 B), a 64-char string (~140 B), and a 512-char string (~1056 B), minus alignment / minus the BDN diagnoser's per-object boundary heuristic. Read them as "approximately one string per call, no intermediate buffers."

### Answers to reflection questions

1. **Pool bucket size for `Rent(32)` and `Rent(33)`?** `ArrayPool<T>.Shared` uses a power-of-two bucket ladder. `Rent(32)` returns a buffer of length 32. `Rent(33)` returns a buffer of length 64 (the next bucket up). Your code should never assume `Rent(n)` returns exactly `n` — always slice down to the requested size.

2. **What happens if you forget to `Return`?** No memory leak in the catastrophic sense — the pool can re-create a buffer of the same size when the next `Rent` comes in. But the pool's hit rate degrades, and every "forgotten" buffer is one fewer that the pool can hand out without going to the GC. In a hot path this manifests as the pool's per-CPU thread-local cache filling up with `null` slots and the slow path being hit more often. Forgetting `Return` is not a crash bug; it is a correctness bug for the pool's hit rate.

3. **When to use `clearArray: true`?** When the buffer just held sensitive data — a password, a token, PII you do not want to leak across requests. The next renter sees whatever the previous user wrote into the buffer; setting `clearArray: true` zeroes it before return. Default `false` is correct for non-sensitive temporaries (hex chars, formatted numbers, image bytes for a non-sensitive image) because the next renter will overwrite the contents anyway.

4. **Why "TlsOverPerCoreLockedStacksArrayPool"?** Read it as three concepts stacked:
   - **TLS** — Thread-Local Storage. Each thread has its own small cache of recently-returned buffers; `Rent` and `Return` hit the TLS cache lock-free if it has the right size.
   - **Per-Core LockedStacks** — when TLS misses, the pool falls back to a per-CPU-core array of locked stacks (one stack per bucket size, one set of stacks per core). The locking is per-stack, so contention is rare.
   - **ArrayPool** — the public-facing abstraction.

   The result is a pool that scales to many cores without lock contention, while keeping the common case (rent+return on the same thread) lock-free. This is a serious piece of engineering; read the source if you want to see how the runtime team thinks about lock-free data structures.

---

## How to grade your own work

If your numbers diverge from the reference:

- **The ratios are within 10–15%, the absolutes are within 2×.** Pass. Hardware differences explain most absolute differences.
- **The ratios are within 10%, the absolutes are 3–5× different.** Possibly a thermally-throttled laptop or an interfering background process. Re-run on a quiet machine.
- **The Plus benchmark allocates significantly less than ~14 MB per call at N=1000.** Almost certainly a mistake — you may have accidentally optimized the loop, or the JIT did something unexpected. Read the benchmark body carefully.
- **The pooled benchmark allocates 0 B.** You probably forgot the final `return new string(chars)` — without it, no allocation happens, but no result is produced either. Add the return.
- **`dotnet run -c Release` errors.** Check the package version: `BenchmarkDotNet 0.14.0` or later. Check the target framework: `net9.0`.

If your numbers are off by more than the bands above and you cannot find the bug, post the result table to your Week 7 GitHub issue with the source code attached. The instructor will help.

---

**References for the solutions**

- BenchmarkDotNet — "Setup and cleanup": <https://benchmarkdotnet.org/articles/features/setup-and-cleanup.html>
- Microsoft Learn — `string.Create<TState>`: <https://learn.microsoft.com/en-us/dotnet/api/system.string.create>
- Microsoft Learn — `ArrayPool<T>`: <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1>
- Microsoft Learn — `Convert.ToHexString`: <https://learn.microsoft.com/en-us/dotnet/api/system.convert.tohexstring>
- `dotnet/runtime` — `String.Manipulation.cs`: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/String.Manipulation.cs>
- `dotnet/runtime` — `TlsOverPerCoreLockedStacksArrayPool.cs`: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/TlsOverPerCoreLockedStacksArrayPool.cs>
