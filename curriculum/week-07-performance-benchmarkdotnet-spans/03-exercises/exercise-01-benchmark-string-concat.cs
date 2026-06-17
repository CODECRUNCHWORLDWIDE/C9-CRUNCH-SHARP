// Exercise 1 — Benchmark four ways to concatenate strings
//
// Goal: Install BenchmarkDotNet into a clean console project, write a
//       [MemoryDiagnoser] benchmark that compares four ways to concatenate
//       N strings, and read the resulting Markdown table. By the end you
//       should be able to defend, with numbers, the claim "+= in a loop
//       is O(N^2) and string.Create over a Span<char> is O(N) with one
//       allocation."
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh benchmark project:
//
//      mkdir Ex01-StringConcat && cd Ex01-StringConcat
//      dotnet new console -n Ex01 -o src/Ex01 --framework net9.0
//      cd src/Ex01
//      dotnet add package BenchmarkDotNet --version 0.14.0
//
//    Replace src/Ex01/Program.cs with the contents of THIS FILE.
//
// 2. Fill in the four TODOs below.
//
// 3. Run:
//
//      dotnet run -c Release
//
//    BDN will print a Markdown result table at the end. Paste it into
//    a file alongside this exercise named results-ex01.md.
//
// ACCEPTANCE CRITERIA
//
//   [ ] The benchmark class has [MemoryDiagnoser].
//   [ ] [Params(10, 100, 1000)] produces three rows per method.
//   [ ] Plus() is marked [Benchmark(Baseline = true)].
//   [ ] All four benchmark methods return string (not void).
//   [ ] [GlobalSetup] populates _parts (not the benchmark methods themselves).
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.
//   [ ] The result table shows Allocated for each method.
//   [ ] Plus at N=1000 allocates >1 MB per call (proves the O(N^2) point).
//   [ ] string.Create at N=1000 allocates ~one final string per call (no intermediates).
//
// SMOKE OUTPUT (target — your machine's numbers will differ; the ratios should not)
//
//   | Method              | N    | Mean        | Allocated   | Alloc Ratio |
//   |-------------------- |----- |------------:|------------:|------------:|
//   | Plus                | 10   |    310.4 ns |       984 B |        1.00 |
//   | StringBuilderConcat | 10   |    159.2 ns |       522 B |        0.53 |
//   | StringConcatAll     | 10   |     72.5 ns |       224 B |        0.23 |
//   | StringCreateSpan    | 10   |     54.1 ns |       144 B |        0.15 |
//   | Plus                | 1000 |  1,131,232 ns |  14516280 B |        1.00 |
//   | StringBuilderConcat | 1000 |     16,432 ns |     35248 B |        0.00 |
//   | StringConcatAll     | 1000 |      5,103 ns |     21184 B |        0.00 |
//   | StringCreateSpan    | 1000 |      4,418 ns |     21080 B |        0.00 |
//
// Inline hints at the bottom of the file.

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

    // ---------------------------------------------------------------------------
    // TODO 1 — Populate _parts and _totalChars in [GlobalSetup].
    //
    // Each part should look like $"item-{i:000}-" (so for i in 0..9 you get
    // "item-000-", "item-001-", etc.). _totalChars is the sum of the lengths
    // of every part — you will need it for StringCreateSpan().
    //
    // Reference: BDN [GlobalSetup] runs once per (method, N) pair before warm-up.
    // https://benchmarkdotnet.org/articles/features/setup-and-cleanup.html
    // ---------------------------------------------------------------------------

    [GlobalSetup]
    public void Setup()
    {
        // YOUR CODE HERE
    }

    // ---------------------------------------------------------------------------
    // TODO 2 — Implement Plus() — the baseline: build a string with +=.
    //
    // This is the form every junior writes. We measure it so we have a baseline
    // to make the other versions look as good as they are.
    // ---------------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public string Plus()
    {
        // YOUR CODE HERE
        return string.Empty;
    }

    // ---------------------------------------------------------------------------
    // TODO 3 — Implement StringBuilderConcat() using StringBuilder with a
    // pre-sized capacity of _totalChars.
    //
    // Why pre-size? Without a capacity hint, StringBuilder doubles its internal
    // chunk array as it grows, which adds allocations. With the correct hint,
    // exactly one chunk is allocated and only the final ToString() allocates.
    // ---------------------------------------------------------------------------

    [Benchmark]
    public string StringBuilderConcat()
    {
        // YOUR CODE HERE
        return string.Empty;
    }

    // ---------------------------------------------------------------------------
    // TODO 4 — Implement StringCreateSpan() using string.Create<TState>.
    //
    // string.Create takes a length, a state, and a SpanAction<char, TState>
    // delegate that writes the chars into the new string's interior buffer.
    // The result is ONE allocation total: the final string.
    //
    // Note the `static` keyword on the delegate — this prevents the compiler
    // from generating a closure that captures `this`, which would allocate.
    // Pass everything you need via the state parameter.
    //
    // Reference: https://learn.microsoft.com/en-us/dotnet/api/system.string.create
    // ---------------------------------------------------------------------------

    [Benchmark]
    public string StringCreateSpan()
    {
        // YOUR CODE HERE
        return string.Empty;
    }

    // ---------------------------------------------------------------------------
    // Optional bonus: implement StringConcatAll() using string.Concat(string[])
    // and add it to the table. Note that string.Concat is the framework's
    // "I know the array up front" path and is competitive with StringCreateSpan.
    // ---------------------------------------------------------------------------

    [Benchmark]
    public string StringConcatAll()
    {
        return string.Concat(_parts);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<StringConcatBenchmark>(args: args);
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — Setup body:
//
//   _parts = new string[N];
//   _totalChars = 0;
//   for (int i = 0; i < N; i++)
//   {
//       _parts[i] = $"item-{i:000}-";
//       _totalChars += _parts[i].Length;
//   }
//
// HINT 2 — Plus body:
//
//   string result = string.Empty;
//   for (int i = 0; i < _parts.Length; i++) result += _parts[i];
//   return result;
//
// HINT 3 — StringBuilderConcat body:
//
//   var sb = new StringBuilder(capacity: _totalChars);
//   for (int i = 0; i < _parts.Length; i++) sb.Append(_parts[i]);
//   return sb.ToString();
//
// HINT 4 — StringCreateSpan body. The static lambda + state argument is the
//          load-bearing piece — capturing `this` would cause a per-call
//          closure allocation and defeat the exercise.
//
//   return string.Create(_totalChars, _parts, static (chars, state) =>
//   {
//       int pos = 0;
//       for (int i = 0; i < state.Length; i++)
//       {
//           ReadOnlySpan<char> src = state[i].AsSpan();
//           src.CopyTo(chars.Slice(pos));
//           pos += src.Length;
//       }
//   });
//
// REFLECTION QUESTIONS — answer in results-ex01.md after the table:
//
// 1. Why does Plus at N=1000 allocate ~14 MB per call? Walk through what
//    happens at iteration #500.
// 2. The ratio between Plus and StringBuilderConcat is much smaller at N=10
//    than at N=1000. Why? Which big-O is each method?
// 3. StringConcatAll is competitive with StringCreateSpan. Why doesn't the
//    framework's string.Concat allocate anything more than the final string?
//    (Hint: look at its source on dotnet/runtime — it computes the total
//    length first and allocates the result string once.)
// 4. The first call to Plus in a fresh process is dramatically slower than
//    the steady-state mean BDN reports. BDN hides this from you. What is the
//    name of the BDN phase that does the hiding, and why is it correct to
//    discard those measurements?
