// Exercise 2 — Implement a ReadOnlySpan<char>-based CSV-line parser
//
// Goal: Implement TryParseCsvRow(ReadOnlySpan<char> input,
//                               out ReadOnlySpan<char> a,
//                               out ReadOnlySpan<char> b,
//                               out ReadOnlySpan<char> c)
//       that splits a 3-column CSV line WITHOUT allocating, and benchmark
//       it against string.Split(',') on the same input.
//
//       Expected outcome: 5-10x faster, zero allocations vs ~96 B for
//       string.Split (the string[] result plus the three substrings).
//
// Estimated time: 45 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh benchmark project:
//
//      mkdir Ex02-SpanParser && cd Ex02-SpanParser
//      dotnet new console -n Ex02 -o src/Ex02 --framework net9.0
//      cd src/Ex02
//      dotnet add package BenchmarkDotNet --version 0.14.0
//
//    Replace src/Ex02/Program.cs with the contents of THIS FILE.
//
// 2. Fill in the three TODOs below.
//
// 3. Run:
//
//      dotnet run -c Release
//
// ACCEPTANCE CRITERIA
//
//   [ ] TryParseCsvRow returns true for valid 3-column inputs, false otherwise.
//   [ ] TryParseCsvRow does not allocate (the BDN table shows 0 B for it).
//   [ ] All three out parameters are ReadOnlySpan<char>, not string.
//   [ ] string.Split-based variant is benchmarked alongside as the baseline.
//   [ ] The span parser is faster than string.Split (BDN run confirms it).
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.
//
// SMOKE OUTPUT (target — your machine's numbers will differ)
//
//   | Method              | Mean      | Allocated |
//   |-------------------- |----------:|----------:|
//   | SplitString         |  72.41 ns |     128 B |
//   | TryParseCsvRowSpan  |   8.93 ns |       0 B |
//
// Inline hints at the bottom of the file.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class CsvParseBenchmark
{
    // A representative 3-column CSV line. ~30 chars total.
    private readonly string _input = "Ada Lovelace,1815-12-10,Mathematics";

    // ---------------------------------------------------------------------------
    // TODO 1 — Implement TryParseCsvRow as a static method below.
    //
    //   public static bool TryParseCsvRow(
    //       ReadOnlySpan<char> input,
    //       out ReadOnlySpan<char> a,
    //       out ReadOnlySpan<char> b,
    //       out ReadOnlySpan<char> c)
    //   {
    //       Find the first ',' (call its index p1). If <0, return false.
    //       Find the next  ',' after p1 (call its index p2). If <0, return false.
    //       Verify there is no further ',' after p2. If there is, return false.
    //       a = input[..p1]
    //       b = input[(p1 + 1)..p2]
    //       c = input[(p2 + 1)..]
    //       return true.
    //   }
    //
    // Reminders:
    //   - input.IndexOf(',')          finds the first comma; returns -1 if none.
    //   - input.Slice(start, length) is the same as input[start..(start+length)].
    //   - You can use input[start..end] range syntax (it compiles to Slice).
    //   - Use `default` to assign an empty ReadOnlySpan<char> when returning false.
    //
    // Reference: https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/
    // ---------------------------------------------------------------------------

    // YOUR CODE HERE (TryParseCsvRow method)

    // ---------------------------------------------------------------------------
    // TODO 2 — The baseline: parse with string.Split(',').
    //
    // This returns a string[] of 3 strings. The benchmark returns the count
    // (so the JIT cannot elide the call) and asserts the count is 3.
    // ---------------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public int SplitString()
    {
        // YOUR CODE HERE
        return 0;
    }

    // ---------------------------------------------------------------------------
    // TODO 3 — The span parser. Returns the total length of the three columns
    // (a + b + c .Length) so the JIT cannot elide the call. You can return any
    // observable value derived from the parsed spans; we use the sum of lengths.
    // ---------------------------------------------------------------------------

    [Benchmark]
    public int TryParseCsvRowSpan()
    {
        // YOUR CODE HERE
        return 0;
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<CsvParseBenchmark>(args: args);
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — TryParseCsvRow body:
//
//   public static bool TryParseCsvRow(
//       ReadOnlySpan<char> input,
//       out ReadOnlySpan<char> a,
//       out ReadOnlySpan<char> b,
//       out ReadOnlySpan<char> c)
//   {
//       int p1 = input.IndexOf(',');
//       if (p1 < 0) { a = default; b = default; c = default; return false; }
//
//       ReadOnlySpan<char> rest = input[(p1 + 1)..];
//       int p2InRest = rest.IndexOf(',');
//       if (p2InRest < 0) { a = default; b = default; c = default; return false; }
//       int p2 = p1 + 1 + p2InRest;
//
//       ReadOnlySpan<char> tail = input[(p2 + 1)..];
//       if (tail.IndexOf(',') >= 0) { a = default; b = default; c = default; return false; }
//
//       a = input[..p1];
//       b = input[(p1 + 1)..p2];
//       c = input[(p2 + 1)..];
//       return true;
//   }
//
// HINT 2 — SplitString body:
//
//   string[] parts = _input.Split(',');
//   return parts.Length;
//
// HINT 3 — TryParseCsvRowSpan body:
//
//   if (TryParseCsvRow(_input.AsSpan(), out var a, out var b, out var c))
//   {
//       return a.Length + b.Length + c.Length;
//   }
//   return 0;
//
// REFLECTION QUESTIONS — answer in results-ex02.md after the table:
//
// 1. The BDN table shows 0 B allocated for TryParseCsvRowSpan. Where does
//    the three "substrings" live, given that no new memory was allocated?
// 2. Explain why a ReadOnlySpan<char> can be returned as an `out` parameter
//    but cannot be returned as the method's return type from a method that
//    allocates a `stackalloc char[N]` inside it.
// 3. Suppose you change the API to return `(string a, string b, string c)`
//    instead of three `out ReadOnlySpan<char>`. How many bytes does that
//    new method allocate per call? Why?
// 4. Suppose the input is 1 MB long, with one comma. What does
//    TryParseCsvRow do with the trailing 999_998 bytes? How long does the
//    method take? (Hint: think about IndexOf's cost.)
