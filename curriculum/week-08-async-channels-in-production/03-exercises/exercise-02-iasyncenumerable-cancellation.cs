// Exercise 2 — Write a streaming IAsyncEnumerable<int> that respects
//              [EnumeratorCancellation], consume it three different ways,
//              prove cancellation reaches the iterator in every variant.
//
// Goal: Demonstrate that the [EnumeratorCancellation] attribute is the load-
//       bearing piece that lets a single producer method work correctly with
//       both `ReadNumbersAsync(ct)` and
//       `ReadNumbersAsync().WithCancellation(ct)` consumer patterns.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir Ex02-AsyncEnum && cd Ex02-AsyncEnum
//      dotnet new console -n Ex02 -o src/Ex02 --framework net8.0
//      cd src/Ex02
//
//    Replace src/Ex02/Program.cs with THIS FILE.
//
// 2. Fill in the three TODOs below.
//
// 3. Run:
//
//      dotnet run -c Release
//
// ACCEPTANCE CRITERIA
//
//   [ ] ReadNumbersAsync is `async IAsyncEnumerable<int>` and yields values 0..N-1.
//   [ ] The `CancellationToken` parameter is marked [EnumeratorCancellation].
//   [ ] Inside the iterator body, every `await` accepts the ct parameter.
//   [ ] Three consumer patterns each demonstrate cancellation within ~120ms
//       of the 100ms CTS firing:
//         A. await foreach (var x in ReadNumbersAsync(cts.Token))
//         B. await foreach (var x in ReadNumbersAsync().WithCancellation(cts.Token))
//         C. await foreach (var x in ReadNumbersAsync(outerCt).WithCancellation(cts.Token))
//   [ ] Each catch block prints the variant name and the number of items
//       consumed before cancellation.
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.
//
// EXPECTED OUTPUT (illustrative — counts vary by ±2 depending on scheduling)
//
//   Variant A (token via parameter):       consumed=9 before cancel.
//   Variant B (token via WithCancellation): consumed=9 before cancel.
//   Variant C (linked outer + WithCancel): consumed=9 before cancel.
//   All three variants cancelled within budget. PASS.
//
// REFLECTION QUESTIONS — answer in results-ex02.md after running:
//
// 1. Remove the [EnumeratorCancellation] attribute. Which of the three variants
//    still cancel? Which loops forever (or to the 1,000-item natural end)?
// 2. The iterator yields once per 10ms. The CTS fires after 100ms. Why is
//    `consumed` always around 9–10 and not exactly 10?
// 3. The `OperationCanceledException` in the catch — is its `.CancellationToken`
//    the cts.Token, the outerCt, or the linked token? Why?
//
// Inline hints at the bottom of the file.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ex02;

public static class Numbers
{
    // ---------------------------------------------------------------------------
    // TODO 1 — Mark the ct parameter with [EnumeratorCancellation]. Inside the
    // method, pass ct to every await. Yield values 0..N-1 with a 10ms delay
    // between yields.
    // ---------------------------------------------------------------------------
    public static async IAsyncEnumerable<int> ReadNumbersAsync(
        int n = 1_000,
        /* TODO mark this parameter */ CancellationToken ct = default)
    {
        for (int i = 0; i < n; i++)
        {
            // YOUR CODE HERE — await Task.Delay(10, ct)?
            yield return i;
        }
    }
}

public static class Program
{
    public static async Task Main()
    {
        await RunVariantA().ConfigureAwait(false);
        await RunVariantB().ConfigureAwait(false);
        await RunVariantC().ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------
    // TODO 2 — Pattern A: pass the CancellationToken as the iterator parameter.
    // No .WithCancellation. The iterator's ct should already be cts.Token.
    // ---------------------------------------------------------------------------
    private static async Task RunVariantA()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        int consumed = 0;
        try
        {
            // YOUR CODE HERE — await foreach over ReadNumbersAsync(cts.Token)
            await foreach (var x in Numbers.ReadNumbersAsync(ct: cts.Token).ConfigureAwait(false))
            {
                consumed++;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Variant A (token via parameter):        consumed={consumed} before cancel.");
        }
    }

    // ---------------------------------------------------------------------------
    // TODO 3 — Pattern B: do NOT pass the token to the iterator (let the
    // parameter default to `default`). Pass the token via .WithCancellation
    // on the enumerable side. [EnumeratorCancellation] is what makes this work.
    // ---------------------------------------------------------------------------
    private static async Task RunVariantB()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        int consumed = 0;
        try
        {
            // YOUR CODE HERE
            await foreach (var x in Numbers.ReadNumbersAsync()
                                           .WithCancellation(cts.Token)
                                           .ConfigureAwait(false))
            {
                consumed++;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Variant B (token via WithCancellation): consumed={consumed} before cancel.");
        }
    }

    // ---------------------------------------------------------------------------
    // TODO 4 — Pattern C: pass an outer token to the parameter AND a linked
    // inner token via .WithCancellation. Cancel only the inner token; observe
    // that the iterator still cancels. [EnumeratorCancellation] OR's them.
    // ---------------------------------------------------------------------------
    private static async Task RunVariantC()
    {
        using var outer = new CancellationTokenSource();          // never fires
        using var inner = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        int consumed = 0;
        try
        {
            await foreach (var x in Numbers.ReadNumbersAsync(ct: outer.Token)
                                           .WithCancellation(inner.Token)
                                           .ConfigureAwait(false))
            {
                consumed++;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Variant C (linked outer + WithCancel):  consumed={consumed} before cancel.");
        }

        Console.WriteLine("All three variants cancelled within budget. PASS.");
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — The iterator parameter declaration:
//
//   public static async IAsyncEnumerable<int> ReadNumbersAsync(
//       int n = 1_000,
//       [EnumeratorCancellation] CancellationToken ct = default)
//
// HINT 2 — Inside the loop body:
//
//   for (int i = 0; i < n; i++)
//   {
//       await Task.Delay(10, ct).ConfigureAwait(false);
//       yield return i;
//   }
//
// HINT 3 — Variants A, B, and C are already implemented in the file above.
// The TODOs in the variant methods are stubs you may keep or adapt. The
// only file-level work you must do is wire up the iterator.
//
// FURTHER EXPLORATION (do at least one)
//
// A. Add a fourth variant: pass `outer.Token` to the parameter and `inner.Token`
//    via .WithCancellation, but cancel `outer.Token` after 100ms while leaving
//    `inner.Token` alone. Same result (cancellation reaches the iterator) — the
//    combined token fires on either source.
//
// B. Add `await using var resource = new MyAsyncResource();` at the top of the
//    iterator body and a Console.WriteLine in MyAsyncResource.DisposeAsync.
//    Confirm that DisposeAsync runs even when the iterator is cancelled.
//
// C. Replace `Task.Delay(10, ct)` with a manual loop that does
//    `ct.ThrowIfCancellationRequested()` every 10ms inside Task.Yield(). How
//    does the cancellation latency compare?
