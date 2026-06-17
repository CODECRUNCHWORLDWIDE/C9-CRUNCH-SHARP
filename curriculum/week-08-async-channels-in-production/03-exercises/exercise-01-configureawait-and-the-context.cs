// Exercise 1 — Build a SynchronizationContext, watch ConfigureAwait switch threads.
//
// Goal: Install a custom single-threaded SynchronizationContext on the current
//       thread, post an async lambda that does three awaits, observe which
//       thread each continuation resumes on, and prove that .ConfigureAwait(false)
//       causes the continuation to leave the captured context.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir Ex01-ConfigureAwait && cd Ex01-ConfigureAwait
//      dotnet new console -n Ex01 -o src/Ex01 --framework net8.0
//      cd src/Ex01
//
//    Replace src/Ex01/Program.cs with the contents of THIS FILE.
//
// 2. Fill in the four TODOs below.
//
// 3. Run:
//
//      dotnet run -c Release
//
//    The expected output prints the thread ID at six points and shows that
//    the ConfigureAwait(false) calls move execution to a ThreadPool thread.
//
// ACCEPTANCE CRITERIA
//
//   [ ] SingleThreadSyncContext.Pump runs on a dedicated background thread.
//   [ ] Post() enqueues callbacks; Send() runs them and waits for completion.
//   [ ] The first await (no ConfigureAwait) resumes on the pump thread.
//   [ ] The second await (.ConfigureAwait(false)) resumes on a ThreadPool thread.
//   [ ] The third await (no ConfigureAwait) resumes on the pump thread again,
//       because SynchronizationContext.Current is null after the previous
//       ConfigureAwait(false); the captured-context rule for this await is
//       the null one. Document this observation in your reflection notes.
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.
//
// EXPECTED OUTPUT (illustrative — thread IDs vary)
//
//   Main:                      thread=1
//   Pump started:              thread=4  (the dedicated single-thread context)
//   Posted callback running:   thread=4
//   After 1st await (default): thread=4   (captured back to the pump)
//   After 2nd await (CA false): thread=6  (a ThreadPool worker)
//   After 3rd await (default): thread=6   (no SyncCtx captured this time)
//   Pump shutting down.
//
// REFLECTION QUESTIONS — answer in results-ex01.md after running:
//
// 1. Why does the third await NOT resume on the pump thread, even though the
//    code does not call ConfigureAwait(false) explicitly?
// 2. What is the captured-context rule the runtime applies at each await?
// 3. If you wrap the entire body in SetSynchronizationContext(null), how does
//    the output change?
//
// Inline hints at the bottom of the file.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Ex01;

public sealed class SingleThreadSyncContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Cb, object? State)> _queue = new();
    private readonly Thread _pump;
    public int PumpThreadId => _pump.ManagedThreadId;

    public SingleThreadSyncContext()
    {
        _pump = new Thread(Run) { IsBackground = true, Name = "Ex01-Pump" };
        _pump.Start();
    }

    // ---------------------------------------------------------------------------
    // TODO 1 — Implement Post(): enqueue the callback. The pump thread will
    // dequeue and run it.
    // ---------------------------------------------------------------------------
    public override void Post(SendOrPostCallback d, object? state)
    {
        // YOUR CODE HERE
    }

    // ---------------------------------------------------------------------------
    // TODO 2 — Implement Send(): enqueue the callback and block until done.
    // (Use a ManualResetEventSlim inside a wrapper callback.)
    // ---------------------------------------------------------------------------
    public override void Send(SendOrPostCallback d, object? state)
    {
        // YOUR CODE HERE
    }

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(this);
        Console.WriteLine($"Pump started:              thread={Environment.CurrentManagedThreadId}");
        foreach (var (cb, state) in _queue.GetConsumingEnumerable())
            cb(state);
        Console.WriteLine("Pump shutting down.");
    }

    public void Dispose() => _queue.CompleteAdding();
}

public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine($"Main:                      thread={Environment.CurrentManagedThreadId}");

        using var ctx = new SingleThreadSyncContext();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // ---------------------------------------------------------------------
        // TODO 3 — Post the async lambda to the pump. The lambda body is the
        // load-bearing part; we want it to run with the pump's SyncCtx current.
        // ---------------------------------------------------------------------
        ctx.Post(async _ =>
        {
            Console.WriteLine($"Posted callback running:   thread={Environment.CurrentManagedThreadId}");

            // First await: default ConfigureAwait, captures pump context.
            await Task.Delay(50);
            Console.WriteLine($"After 1st await (default): thread={Environment.CurrentManagedThreadId}");

            // Second await: ConfigureAwait(false), continuation runs on ThreadPool.
            await Task.Delay(50).ConfigureAwait(false);
            Console.WriteLine($"After 2nd await (CA false): thread={Environment.CurrentManagedThreadId}");

            // Third await: default ConfigureAwait — but SyncCtx.Current is null
            // here (we lost the capture after the previous CA(false)), so the
            // continuation runs on ThreadPool. Documenting this is the point
            // of reflection question 1.
            await Task.Delay(50);
            Console.WriteLine($"After 3rd await (default): thread={Environment.CurrentManagedThreadId}");

            done.SetResult(true);
        }, null);

        // ---------------------------------------------------------------------
        // TODO 4 — Wait for the pump's work to finish before disposing.
        // (Hint: the TaskCompletionSource above signals completion.)
        // ---------------------------------------------------------------------
        await done.Task.ConfigureAwait(false);
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — Post body:
//
//   _queue.Add((d, state));
//
// HINT 2 — Send body. The wait avoids deadlocking when the caller is itself
// the pump thread.
//
//   if (Thread.CurrentThread.ManagedThreadId == _pump.ManagedThreadId)
//   {
//       d(state);
//   }
//   else
//   {
//       using var done = new ManualResetEventSlim();
//       _queue.Add((s =>
//       {
//           try { d(s); } finally { done.Set(); }
//       }, state));
//       done.Wait();
//   }
//
// HINT 3 — The Post() call in Main is already filled in for you. Read it
// carefully: the cast `async _ => { ... }` produces an `async void` lambda
// (because Post expects a SendOrPostCallback, which is `void`-returning).
// Async void is otherwise discouraged; here it is the right shape because
// the SynchronizationContext.Post API is itself void-returning.
//
// HINT 4 — `await done.Task.ConfigureAwait(false)` — by the time we reach
// here, the pump may have already completed (we set TCS at the end of the
// posted lambda). The ConfigureAwait(false) is appropriate because Main's
// continuation does not need to run on any particular context.
//
// FURTHER EXPLORATION (do at least one)
//
// A. Replace `ctx.Post(...)` with `ctx.Send(...)`. What changes? Why does
//    Send block Main, while Post does not?
//
// B. Add `Console.WriteLine($"SyncCtx.Current = {SynchronizationContext.Current}");`
//    at each of the six observation points. You will see the SyncCtx switch
//    between the pump's context and null.
//
// C. Comment out `ctx.Post(...)` and replace it with `await Task.Run(...)`.
//    Now there is no captured context anywhere. What is the thread ID at
//    each await? Why is the output identical to the CA(false) case?
