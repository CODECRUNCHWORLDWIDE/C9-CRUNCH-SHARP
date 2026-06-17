// Exercise 3 — ArrayPool: rent, use, return
//
// Goal: Implement Hex.Encode(ReadOnlySpan<byte>) using ArrayPool<char> for
//       the intermediate char buffer. Compare against a StringBuilder-based
//       baseline. Verify that the pooled version allocates only the final
//       string (no intermediate buffer).
//
// Estimated time: 45 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh benchmark project:
//
//      mkdir Ex03-Pool && cd Ex03-Pool
//      dotnet new console -n Ex03 -o src/Ex03 --framework net9.0
//      cd src/Ex03
//      dotnet add package BenchmarkDotNet --version 0.14.0
//
//    Replace src/Ex03/Program.cs with the contents of THIS FILE.
//
// 2. Fill in the three TODOs below.
//
// 3. Run:
//
//      dotnet run -c Release
//
// ACCEPTANCE CRITERIA
//
//   [ ] EncodeAllocating uses StringBuilder + b.ToString("x2") and is the baseline.
//   [ ] EncodePooled uses ArrayPool<char>.Shared.Rent + Return in try/finally.
//   [ ] EncodeFramework uses Convert.ToHexString (the canonical answer).
//   [ ] [Params(16, 32, 256)] runs the benchmark on three input sizes.
//   [ ] EncodePooled allocates significantly less than EncodeAllocating at every N.
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.
//   [ ] After every Rent, there is a corresponding Return — no leaks.
//   [ ] The Return call uses clearArray: false (the buffer holds non-sensitive hex).
//
// SMOKE OUTPUT (target — your machine's numbers will differ)
//
//   | Method            | N   | Mean       | Allocated  |
//   |------------------ |---- |-----------:|-----------:|
//   | EncodeAllocating  | 16  |   228.4 ns |     576 B  |
//   | EncodePooled      | 16  |    34.2 ns |      56 B  |
//   | EncodeFramework   | 16  |    21.8 ns |      56 B  |
//   | EncodeAllocating  | 256 |   3,432 ns |    8208 B  |
//   | EncodePooled      | 256 |     448 ns |     536 B  |
//   | EncodeFramework   | 256 |     192 ns |     536 B  |
//
// Inline hints at the bottom of the file.

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

    // ---------------------------------------------------------------------------
    // TODO 1 — EncodeAllocating (the baseline).
    //
    // Build the hex string with a StringBuilder, appending b.ToString("x2")
    // for each byte. This is the "obvious" form a junior writes. Note that
    // b.ToString("x2") allocates a fresh 2-char string per byte — that is
    // exactly why this baseline is slow.
    // ---------------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public string EncodeAllocating()
    {
        // YOUR CODE HERE
        return string.Empty;
    }

    // ---------------------------------------------------------------------------
    // TODO 2 — EncodePooled.
    //
    // Allocate the intermediate char[] from ArrayPool<char>.Shared.Rent(2*N),
    // fill it via a hand-rolled hex table, build the final string with
    // `new string(charSpan)`, and Return the rented buffer in a finally block.
    //
    // Rules:
    //   - ALWAYS use try/finally for Rent/Return. Even on exception, the
    //     buffer must come back.
    //   - The rented buffer may be LARGER than 2*N. Slice down to 2*N when you
    //     read from it: `rented.AsSpan(0, 2 * N)`.
    //   - Return with clearArray: false — the hex chars are not sensitive.
    //
    // Reference: https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1
    // ---------------------------------------------------------------------------

    [Benchmark]
    public string EncodePooled()
    {
        // YOUR CODE HERE
        return string.Empty;
    }

    // ---------------------------------------------------------------------------
    // TODO 3 — EncodeFramework (the canonical answer).
    //
    // The runtime ships Convert.ToHexString(ReadOnlySpan<byte>) — internally
    // optimised, often using SIMD. Our hand-rolled version should be within
    // ~2x of this; the framework's is the upper bound on what is reasonable
    // to achieve without doing SIMD ourselves.
    //
    // This benchmark also serves as a humility check: when the framework has
    // the primitive, use the framework's primitive.
    // ---------------------------------------------------------------------------

    [Benchmark]
    public string EncodeFramework()
    {
        // YOUR CODE HERE
        return string.Empty;
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<HexEncodeBenchmark>(args: args);
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — EncodeAllocating body:
//
//   var sb = new StringBuilder(_data.Length * 2);
//   for (int i = 0; i < _data.Length; i++)
//   {
//       sb.Append(_data[i].ToString("x2"));
//   }
//   return sb.ToString();
//
// HINT 2 — EncodePooled body. Note the try/finally placement: Rent OUTSIDE
// the try, Return INSIDE the finally. The "Rent inside try" pattern is also
// valid; pick one and be consistent. The most common shape is shown here.
//
//   const string Hex = "0123456789abcdef";
//
//   int charCount = _data.Length * 2;
//   char[] rented = ArrayPool<char>.Shared.Rent(charCount);
//   try
//   {
//       Span<char> chars = rented.AsSpan(0, charCount);
//       for (int i = 0; i < _data.Length; i++)
//       {
//           byte b = _data[i];
//           chars[i * 2]     = Hex[b >> 4];
//           chars[i * 2 + 1] = Hex[b & 0xF];
//       }
//       return new string(chars);
//   }
//   finally
//   {
//       ArrayPool<char>.Shared.Return(rented, clearArray: false);
//   }
//
// HINT 3 — EncodeFramework body:
//
//   return Convert.ToHexString(_data).ToLowerInvariant();
//
// (Convert.ToHexString returns UPPERCASE by default; ToLowerInvariant allocates
// a second string. To be a fair comparison with the lowercase output of the
// hand-rolled versions, this is the simplest fix. The truly fair comparison
// uses Convert.ToHexString without the lowercase, and the hand-rolled versions
// use uppercase too. Pick a convention and stick to it; document it in
// results-ex03.md.)
//
// REFLECTION QUESTIONS — answer in results-ex03.md after the table:
//
// 1. The rented buffer may be larger than what you requested. What is the
//    pool's bucket size for Rent(32)? Rent(33)? Hint: it's a power-of-two
//    ladder starting at 16.
// 2. What happens if you forget to Return a rented buffer? Does it leak?
//    Does it cause a memory issue? Does it just degrade the pool's hit rate?
// 3. The clearArray: true overload of Return zeroes the buffer before
//    returning it. When would you use it? Why is it the wrong default for
//    most use cases?
// 4. Read the source of ArrayPool<T>.Shared (the link is in resources.md).
//    Why is it called "TlsOverPerCoreLockedStacksArrayPool"? What does each
//    word mean?
