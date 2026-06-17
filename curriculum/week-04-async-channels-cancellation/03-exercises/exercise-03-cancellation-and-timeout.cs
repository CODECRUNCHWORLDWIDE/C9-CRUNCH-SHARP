// Exercise 3 — Cancellation and Timeout
//
// Goal: Build a console program that runs an indefinite-length async loop and
//       responds correctly to TWO termination signals composed together:
//
//         1. Ctrl-C from the user        (CancellationTokenSource via Console.CancelKeyPress)
//         2. A 5-second server-side cap  (CancellationTokenSource(TimeSpan))
//
//       The two sources are composed with CancellationTokenSource.CreateLinkedTokenSource
//       so that whichever fires first wins. At exit, the program must print
//       WHICH reason caused the shutdown, distinguished with a `catch ... when`
//       exception filter.
//
//       This is the EXACT pattern for the mini-project's crawler shutdown —
//       respect the user's Ctrl-C, but also impose a server-side timeout so a
//       runaway crawl can't run forever.
//
// Estimated time: 45 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold:
//
//      mkdir CancelDemo && cd CancelDemo
//      dotnet new console -n CancelDemo -o src/CancelDemo
//
//    Replace src/CancelDemo/Program.cs with THIS FILE.
//
// 2. Fill in the TODOs.
//
// 3. Run TWO ways:
//
//      # Test the timeout path: let it run for 5+ seconds.
//      dotnet run --project src/CancelDemo
//
//      # Test the Ctrl-C path: start it, hit Ctrl-C before 5 seconds elapse.
//      dotnet run --project src/CancelDemo
//      (then press Ctrl-C)
//
// 4. The output should match SMOKE OUTPUT (TIMEOUT) or SMOKE OUTPUT (CTRL-C)
//    depending on which signal fired first.
//
// ACCEPTANCE CRITERIA
//
//   [ ] All TODOs implemented.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] Ctrl-C during the loop produces SMOKE OUTPUT (CTRL-C).
//   [ ] Letting the program run for 5+ seconds produces SMOKE OUTPUT (TIMEOUT).
//   [ ] The OperationCanceledException is caught at EXACTLY ONE place
//       (the outermost frame).
//   [ ] WorkLoopAsync's loop body checks ct.ThrowIfCancellationRequested()
//       before each iteration.
//   [ ] Both `cts` and `linkedCts` are wrapped in `using var ...`.
//   [ ] The shutdown latency (time from signal to exit) is under 100ms.
//
// SMOKE OUTPUT (CTRL-C)
//
//   == Cancellation + Timeout demo ==
//   Press Ctrl-C to exit early. Or wait 5 seconds for the timeout.
//   tick  1
//   tick  2
//   tick  3
//   ^C
//   Ctrl-C received; cancelling...
//   Cancelled by user (Ctrl-C). Shutdown latency: ~30 ms.
//   Build succeeded · 0 warnings · 0 errors
//
// SMOKE OUTPUT (TIMEOUT)
//
//   == Cancellation + Timeout demo ==
//   Press Ctrl-C to exit early. Or wait 5 seconds for the timeout.
//   tick  1
//   tick  2
//   tick  3
//   tick  4
//   tick  5
//   Cancelled by server-side timeout (5s elapsed). Shutdown latency: ~25 ms.
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints are at the bottom of the file.

using System.Diagnostics;

Console.WriteLine("== Cancellation + Timeout demo ==");
Console.WriteLine("Press Ctrl-C to exit early. Or wait 5 seconds for the timeout.");

// TODO: create a CancellationTokenSource for the user's Ctrl-C signal.
//       Wire Console.CancelKeyPress so the handler:
//         - sets e.Cancel = true  (so the runtime doesn't terminate immediately)
//         - calls userCts.Cancel()
//         - prints "Ctrl-C received; cancelling..."
//
//   using var userCts = new CancellationTokenSource();
//   Console.CancelKeyPress += (_, e) =>
//   {
//       e.Cancel = true;
//       userCts.Cancel();
//       Console.WriteLine("Ctrl-C received; cancelling...");
//   };
using CancellationTokenSource userCts = throw new NotImplementedException();

// TODO: create a SECOND CancellationTokenSource that auto-cancels after 5s.
//       This is the "server-side timeout."
//
//   using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using CancellationTokenSource timeoutCts = throw new NotImplementedException();

// TODO: compose them with CancellationTokenSource.CreateLinkedTokenSource.
//       The linked source fires when EITHER user or timeout fires.
//
//   using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
//       userCts.Token, timeoutCts.Token);
using CancellationTokenSource linkedCts = throw new NotImplementedException();

var startedAt = Stopwatch.StartNew();
var signalReceivedAt = 0L;
userCts.Token.Register(() => signalReceivedAt = startedAt.ElapsedMilliseconds);
timeoutCts.Token.Register(() => signalReceivedAt = startedAt.ElapsedMilliseconds);

try
{
    await WorkLoopAsync(linkedCts.Token);
}
// TODO: catch OperationCanceledException, then use a `when` filter to
//       distinguish WHICH source fired first:
//
//         catch (OperationCanceledException) when (userCts.IsCancellationRequested)
//         {
//             var latency = startedAt.ElapsedMilliseconds - signalReceivedAt;
//             Console.WriteLine($"Cancelled by user (Ctrl-C). Shutdown latency: ~{latency} ms.");
//         }
//         catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
//         {
//             var latency = startedAt.ElapsedMilliseconds - signalReceivedAt;
//             Console.WriteLine($"Cancelled by server-side timeout (5s elapsed). Shutdown latency: ~{latency} ms.");
//         }
//
//       The key insight: linkedCts.Token.IsCancellationRequested is TRUE in
//       BOTH cases, but userCts.IsCancellationRequested distinguishes them.
catch (OperationCanceledException)
{
    throw new NotImplementedException();
}

Console.WriteLine("Build succeeded · 0 warnings · 0 errors");

// ---------------------------------------------------------------------------
// The async work loop
// ---------------------------------------------------------------------------

static async Task WorkLoopAsync(CancellationToken ct)
{
    // TODO: loop forever, printing "tick {n}" once per second.
    //   - Before each iteration, call ct.ThrowIfCancellationRequested().
    //   - Use await Task.Delay(TimeSpan.FromSeconds(1), ct) so the wait itself
    //     is cancellation-aware.
    //
    //   for (var n = 1; ; n++)
    //   {
    //       ct.ThrowIfCancellationRequested();
    //       Console.WriteLine($"tick {n,2}");
    //       await Task.Delay(TimeSpan.FromSeconds(1), ct);
    //   }
    throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ---------------------------------------------------------------------------
//
// The three CTSes:
//
//   using var userCts = new CancellationTokenSource();
//   Console.CancelKeyPress += (_, e) =>
//   {
//       e.Cancel = true;
//       userCts.Cancel();
//       Console.WriteLine("Ctrl-C received; cancelling...");
//   };
//
//   using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
//   using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
//       userCts.Token, timeoutCts.Token);
//
// The catch:
//
//   try { await WorkLoopAsync(linkedCts.Token); }
//   catch (OperationCanceledException) when (userCts.IsCancellationRequested)
//   {
//       var latency = startedAt.ElapsedMilliseconds - signalReceivedAt;
//       Console.WriteLine($"Cancelled by user (Ctrl-C). Shutdown latency: ~{latency} ms.");
//   }
//   catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
//   {
//       var latency = startedAt.ElapsedMilliseconds - signalReceivedAt;
//       Console.WriteLine($"Cancelled by server-side timeout (5s elapsed). Shutdown latency: ~{latency} ms.");
//   }
//
// The loop:
//
//   for (var n = 1; ; n++)
//   {
//       ct.ThrowIfCancellationRequested();
//       Console.WriteLine($"tick {n,2}");
//       await Task.Delay(TimeSpan.FromSeconds(1), ct);
//   }
//
// ---------------------------------------------------------------------------
// VARIATIONS TO TRY
// ---------------------------------------------------------------------------
//
// A. Replace Task.Delay(TimeSpan.FromSeconds(1), ct) with
//    Task.Delay(TimeSpan.FromSeconds(1)) — no token. Re-run with Ctrl-C.
//    Notice that the loop now takes up to 1 second to respond, because the
//    Delay does not see the cancellation. Lesson: pass `ct` to every async
//    call, every time.
//
// B. Replace ct.ThrowIfCancellationRequested() with `if (ct.IsCancellationRequested) break`.
//    The loop exits "gracefully" instead of throwing. The outer catch never
//    sees the exception. This is OK if you really want graceful exit (e.g.,
//    "flush partial results") but the rest of the program no longer knows
//    WHY the loop ended.
//
// C. Change the timeout to 500ms. Re-run. The timeout almost always fires
//    first. The catch's `when (timeoutCts.IsCancellationRequested)` arm
//    runs, not the user arm.
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// Every long-running async operation in production code has BOTH of these
// signals applied to it:
//
//   - The CALLER's cancellation (HttpContext.RequestAborted for ASP.NET,
//     IHostApplicationLifetime.ApplicationStopping for a BackgroundService).
//   - A SERVER-SIDE TIMEOUT (so a slow client can't tie up a thread forever).
//
// The CreateLinkedTokenSource + CancelAfter pattern composes them into one
// token your inner code can consume. The `catch ... when (which.IsCancellationRequested)`
// pattern lets the outer frame distinguish the two reasons for shutdown —
// without throwing different exception TYPES, which the runtime does not
// support natively for cancellation.
//
// The mini-project's CrawlAsync method takes one CancellationToken. Inside,
// it composes that token with a per-crawl timeout. The outermost frame
// (the .http endpoint or the console main) decides whether a shutdown is
// "user wanted to stop" (200 OK with partial results) or "server hit its
// limit" (504 Gateway Timeout). Same primitive; different policy.
//
// ---------------------------------------------------------------------------
