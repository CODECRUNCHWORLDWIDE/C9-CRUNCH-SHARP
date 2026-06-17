# Challenge 1 — Rewrite an allocation-heavy method to be zero-allocation

> **Estimated time:** 90–120 minutes. Worth more than its time-cost suggests; this is the canonical shape of senior performance work.

You are given a method that builds a URL query string from a `Dictionary<string, string>`. The method works correctly, returns the right answer, and allocates aggressively. Your job is to rewrite it so that, after the rewrite, BDN reports **zero intermediate allocations** — only the final returned string allocates. You will report your before/after BDN table in a results file.

## The starting code

Drop the following into a fresh `dotnet new console` benchmark project (see Exercise 1 for the scaffolding command). The `BuildQueryStringSlow` method is the target. You may not modify its signature.

```csharp
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class QueryStringBenchmark
{
    private Dictionary<string, string> _params = null!;

    [GlobalSetup]
    public void Setup()
    {
        _params = new Dictionary<string, string>
        {
            ["q"]       = "code crunch worldwide",
            ["limit"]   = "50",
            ["page"]    = "3",
            ["lang"]    = "en-US",
            ["sort"]    = "relevance desc",
        };
    }

    [Benchmark(Baseline = true)]
    public string BuildQueryStringSlow()
    {
        if (_params.Count == 0) return string.Empty;

        var sb = new StringBuilder("?");
        foreach (var kvp in _params)
        {
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
            sb.Append('&');
        }
        sb.Length--;  // strip trailing '&'
        return sb.ToString();
    }

    // YOUR FAST VERSION GOES HERE.
    [Benchmark]
    public string BuildQueryStringFast()
    {
        // TODO — same input/output, ≤ 1 final allocation.
        throw new NotImplementedException();
    }
}

public static class Program
{
    public static void Main(string[] args) => BenchmarkRunner.Run<QueryStringBenchmark>(args: args);
}
```

## What the slow method allocates

Run BDN on the starting code first, with `BuildQueryStringFast` `throw`-ing. You will get one row (`BuildQueryStringSlow`) showing roughly:

```
| Method                | Mean     | Allocated |
|---------------------- |---------:|----------:|
| BuildQueryStringSlow  |   980 ns |     768 B |
```

Trace the 768 B:

- `new StringBuilder("?")` — 24 B object header + a 16-char initial chunk (~56 B) = ~80 B.
- `Uri.EscapeDataString(kvp.Value)` for each of the 5 values — each call allocates a new `string` of length ≥ the input. Even for ASCII inputs with no escapes, `EscapeDataString` allocates a new string identical to the input (it does not return the input unchanged). 5 × ~40 B = 200 B.
- The dictionary's enumerator — a struct on modern .NET, so ~0 B here. (Older versions allocated a `Dictionary<K,V>.Enumerator` on the heap; .NET 5+ does not.)
- `StringBuilder` chunk reallocations as it grows past its initial 16-char capacity — for our input the total is ~80 chars, so 2-3 chunk reallocations totaling ~200 B.
- The final `ToString()` — one ~120 B string.

Numbers vary; the qualitative picture does not.

## Your task

Write `BuildQueryStringFast` with the same observable behavior (same input, same output as `BuildQueryStringSlow` for any `Dictionary<string, string>`), reducing allocations to **exactly one** — the final returned `string`. Use the techniques from Lectures 2 and 3:

1. **Pre-compute the total length** of the final string. Walk the dictionary once to sum `key.Length + 1 + EscapedLength(value) + 1`, then subtract 1 (no trailing `&`).
2. **Snapshot the dictionary entries** into a small `KeyValuePair<string, string>[]` so you can pass them into `string.Create<TState>` (which requires a non-`ref struct` state argument). Yes, this is one extra allocation; you can eliminate it with `ArrayPool<KeyValuePair<string, string>>.Shared.Rent(_params.Count)` and a `try / finally`. Bonus credit for doing this.
3. **Write the final string in `string.Create<TState>`'s callback**, computing the URL-encoded value characters directly into the destination `Span<char>` without intermediate strings.

Hand-roll the URL escape per **RFC 3986 unreserved characters** (`A-Z a-z 0-9 - _ . ~`). For any character outside that set, emit `%XX` where `XX` is the uppercase hex of the *UTF-8 byte* of the character. For non-ASCII characters, you will need to encode them to UTF-8 first; you can use `Encoding.UTF8.GetBytes(ReadOnlySpan<char>, Span<byte>)` into a `stackalloc byte[4]` per character (4 bytes is the maximum UTF-8 length per char).

This is more code than the slow version, deliberately. The point is that the *runtime cost* drops dramatically.

## Acceptance criteria

- [ ] `BuildQueryStringFast(_params)` returns a string that matches `BuildQueryStringSlow(_params)` exactly, for any input dictionary. Verify by running both and asserting equality before the BDN run.
- [ ] BDN reports **≤ 200 B** of allocations for `BuildQueryStringFast` (the final string plus the small snapshot array). Bonus if you get below 150 B by pooling the snapshot.
- [ ] BDN reports a **mean time < 350 ns** for `BuildQueryStringFast` on the standard test input (5 KVPs, average 15-char values).
- [ ] `dotnet run -c Release` succeeds with 0 warnings, 0 errors.
- [ ] Your benchmark project has `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in the `csproj`.
- [ ] You write a 200-300 word `results.md` next to your project's `Program.cs` that contains:
  - The before BDN table.
  - The after BDN table.
  - A two-sentence summary of where the allocations went.
  - One paragraph on the trade-offs you made (e.g., snapshot vs pool, manual escape vs framework).

## Test inputs

Verify your implementation produces the same output as the slow version for at least these inputs:

```csharp
// Test 1: empty dictionary — both must return "".
new Dictionary<string, string>()

// Test 2: simple ASCII, no escapes needed.
new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }
// Expected: "?a=1&b=2" (or "?b=2&a=1" — dictionary order)

// Test 3: spaces, which require escape.
new Dictionary<string, string> { ["q"] = "hello world" }
// Expected: "?q=hello%20world"

// Test 4: Unicode, which requires UTF-8 encoding.
new Dictionary<string, string> { ["name"] = "café" }
// Expected: "?name=caf%C3%A9"

// Test 5: characters that look special but are unreserved.
new Dictionary<string, string> { ["x"] = "-_.~" }
// Expected: "?x=-_.~" (no escapes — these are unreserved per RFC 3986)
```

A small test harness:

```csharp
static void AssertEquals(string expected, string actual)
{
    if (expected != actual)
    {
        Console.Error.WriteLine($"FAIL: expected '{expected}', got '{actual}'");
        Environment.Exit(1);
    }
}

// Run before BenchmarkRunner.Run<...>():
var b = new QueryStringBenchmark();
b.Setup();
AssertEquals(b.BuildQueryStringSlow(), b.BuildQueryStringFast());
```

If the assertion fires, the BDN run will not start. Fix the bug; then benchmark.

## Hints

1. **Pre-computing length.** Walk the dictionary twice — first to size it, then to fill it. The second walk is `string.Create<TState>`'s callback. You will need a `KeyValuePair<string, string>[]` snapshot to satisfy `TState`'s "must not be `ref struct`" requirement.

2. **Manual URL escape.** Hex-encode each byte as `%XX` (uppercase). Two chars per non-unreserved byte. For ASCII bytes, the byte == the char. For non-ASCII, encode the char to UTF-8 first.

3. **The `string.Create<TState>` callback is `static`.** This prevents the compiler from synthesizing a closure that captures `this`. Pass everything you need via the `state` parameter (the snapshot array). The `Span<char>` parameter is the destination.

4. **Use `Convert.ToHexString(stackalloc byte[1] { b })` for a single byte?** No — that allocates. Hand-roll the two-char hex per byte: `dest[pos++] = "0123456789ABCDEF"[b >> 4]; dest[pos++] = "0123456789ABCDEF"[b & 0xF];`.

5. **Where to verify with `[DisassemblyDiagnoser]`.** Add `[DisassemblyDiagnoser(maxDepth: 2)]` to the benchmark class once your `BuildQueryStringFast` is working. Look at the disassembly of the inner loop. If you see a `call` instruction inside the loop, ask whether it can be eliminated — it might be a hidden allocation or a bounds check that escaped to a helper.

## Going further (no extra grade, no time pressure)

- Replace the snapshot `KeyValuePair[]` with `ArrayPool<KeyValuePair<string, string>>.Shared.Rent` for zero allocations beyond the final string.
- Use `Utf8Formatter.TryFormat` if any of your values are integers (so you can skip `i.ToString()`'s allocation).
- Replace `Encoding.UTF8.GetBytes` with `Rune.EncodeToUtf8` for a more allocation-free path on Unicode.
- Add a `[Params]` axis that varies the dictionary size from 1 to 50. Watch the ratio: at size 50, your fast version should be ~5× faster than the slow version; at size 1, the ratio may shrink because the constant-cost work dominates.
- Run with `[SimpleJob(RuntimeMoniker.Net80, baseline: true)]` and `[SimpleJob(RuntimeMoniker.Net90)]` to compare across runtimes. Note where .NET 9 improved.

## Submission

Commit to your Week 7 GitHub repository at `challenges/challenge-01-query-string/` containing:

- `src/QueryString/Program.cs` — the benchmark.
- `src/QueryString/QueryString.csproj` — the project file.
- `src/QueryString/results.md` — the before/after BDN tables and 200-word writeup.
- A short top-level `README.md` explaining what the project does and how to run it.

The instructor reviews these by reading `results.md` and re-running BDN locally. A submission whose results.md numbers do not match a local re-run is the most common review-fail; if your run is on Apple Silicon and the reviewer's is on x86, document the platform in `results.md` and the discrepancy is expected.

---

**References**

- Microsoft Learn — `string.Create<TState>`: <https://learn.microsoft.com/en-us/dotnet/api/system.string.create>
- Microsoft Learn — `Uri.EscapeDataString`: <https://learn.microsoft.com/en-us/dotnet/api/system.uri.escapedatastring>
- Microsoft Learn — `ArrayPool<T>`: <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1>
- RFC 3986 — "Uniform Resource Identifier (URI)" — unreserved character set: <https://datatracker.ietf.org/doc/html/rfc3986#section-2.3>
- BenchmarkDotNet — DisassemblyDiagnoser: <https://benchmarkdotnet.org/articles/configs/diagnosers.html#sample-introdisasm>
