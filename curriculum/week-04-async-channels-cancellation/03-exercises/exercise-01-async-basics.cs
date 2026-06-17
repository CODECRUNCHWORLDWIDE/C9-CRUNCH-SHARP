// Exercise 1 — Async Basics
//
// Goal: Exercise the four fundamental async APIs from Lecture 1 — Task.WhenAll,
//       Task.WhenEach (new in .NET 9), ValueTask<T>, and IAsyncEnumerable<T> —
//       in one cohesive program. By the end you should be able to read your own
//       state-machine traces, distinguish synchronous vs asynchronous fast paths,
//       and pick the right return type for a new async API.
//
// Estimated time: 45 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir AsyncBasics && cd AsyncBasics
//      dotnet new console -n AsyncBasics -o src/AsyncBasics
//
//    Replace src/AsyncBasics/Program.cs with the contents of THIS FILE.
//
// 2. Fill in the bodies marked `// TODO`. Do not change the method signatures —
//    they are the contract the smoke output checks against.
//
// 3. Run:
//
//      dotnet run --project src/AsyncBasics
//
// 4. The output should match the SMOKE OUTPUT below (modulo exact millisecond
//    values).
//
// ACCEPTANCE CRITERIA
//
//   [ ] All TODOs implemented.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] `dotnet run` produces the SMOKE OUTPUT below.
//   [ ] No `.Result`, `.Wait()`, `async void`, or fire-and-forget Tasks.
//   [ ] Every async method accepts a `CancellationToken` (default is fine).
//   [ ] Section 4's IAsyncEnumerable<int> generator has
//       [EnumeratorCancellation] on its `ct` parameter.
//
// SMOKE OUTPUT (target)
//
//   == Section 1: Task.WhenAll on three I/O-bound tasks ==
//   Fetched 3 in ~200ms (all three ran concurrently)
//
//   == Section 2: Task.WhenEach in completion order ==
//   Completed in completion order:
//     task-200ms  (200ms)
//     task-400ms  (400ms)
//     task-600ms  (600ms)
//
//   == Section 3: ValueTask<int> cache hit vs miss ==
//   First call (miss): 42 in ~50ms
//   Second call (hit): 42 in 0ms (no allocation)
//
//   == Section 4: IAsyncEnumerable<int> with cancellation ==
//   Streamed: 0 1 2 3 4
//   Cancelled after 5; saw OperationCanceledException as expected.
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints are at the bottom of the file.

using System.Diagnostics;
using System.Runtime.CompilerServices;

await RunSection1Async();
await RunSection2Async();
await RunSection3Async();
await RunSection4Async();

Console.WriteLine();
Console.WriteLine("Build succeeded · 0 warnings · 0 errors");

// ---------------------------------------------------------------------------
// Section 1 — Task.WhenAll on three I/O-bound tasks
// ---------------------------------------------------------------------------

static async Task RunSection1Async()
{
    Console.WriteLine("== Section 1: Task.WhenAll on three I/O-bound tasks ==");

    var sw = Stopwatch.StartNew();

    // TODO: Kick off three "fetches" of 200ms each, IN PARALLEL, using
    //       SimulateFetchAsync(200), SimulateFetchAsync(200), SimulateFetchAsync(200).
    //       Collect the three Tasks first, then await Task.WhenAll on them.
    //       The whole section should take ~200ms (because they run concurrently),
    //       NOT ~600ms (which would mean you awaited them sequentially).
    //
    //       Hint: var t1 = SimulateFetchAsync(200); var t2 = ...; await Task.WhenAll(t1, t2, t3);
    throw new NotImplementedException();

    // Console.WriteLine($"Fetched 3 in ~{sw.ElapsedMilliseconds}ms (all three ran concurrently)");
    // Console.WriteLine();
}

static async Task<int> SimulateFetchAsync(int delayMs, CancellationToken ct = default)
{
    await Task.Delay(delayMs, ct);
    return delayMs;
}

// ---------------------------------------------------------------------------
// Section 2 — Task.WhenEach in completion order
// ---------------------------------------------------------------------------

static async Task RunSection2Async()
{
    Console.WriteLine("== Section 2: Task.WhenEach in completion order ==");

    // Three tasks with deliberately different delays.
    var tasks = new[]
    {
        LabeledFetchAsync("task-600ms", 600),
        LabeledFetchAsync("task-200ms", 200),
        LabeledFetchAsync("task-400ms", 400),
    };

    Console.WriteLine("Completed in completion order:");

    // TODO: Use Task.WhenEach(tasks) to iterate the tasks IN COMPLETION ORDER
    //       (not start order). For each completed task, await it and print
    //       "  {label}  ({delayMs}ms)" with 2-space indent.
    //
    //       Note: Task.WhenEach was added in .NET 9. It returns
    //       IAsyncEnumerable<Task<T>>, so you consume it with `await foreach`.
    //
    //       The expected output order is task-200ms, task-400ms, task-600ms —
    //       NOT the start order (600, 200, 400).
    throw new NotImplementedException();

    // Console.WriteLine();
}

static async Task<(string Label, int DelayMs)> LabeledFetchAsync(
    string label,
    int delayMs,
    CancellationToken ct = default)
{
    await Task.Delay(delayMs, ct);
    return (label, delayMs);
}

// ---------------------------------------------------------------------------
// Section 3 — ValueTask<int> cache hit vs miss
// ---------------------------------------------------------------------------

static async Task RunSection3Async()
{
    Console.WriteLine("== Section 3: ValueTask<int> cache hit vs miss ==");

    var cache = new ValueCache();

    var sw = Stopwatch.StartNew();
    var v1 = await cache.GetOrLoadAsync(key: 1);
    var miss = sw.ElapsedMilliseconds;
    Console.WriteLine($"First call (miss): {v1} in ~{miss}ms");

    sw.Restart();
    var v2 = await cache.GetOrLoadAsync(key: 1);
    var hit = sw.ElapsedMilliseconds;
    Console.WriteLine($"Second call (hit): {v2} in {hit}ms (no allocation)");

    Console.WriteLine();
}

public sealed class ValueCache
{
    private readonly Dictionary<int, int> _store = new();

    // TODO: Implement GetOrLoadAsync as a ValueTask<int>-returning method.
    //       - If the key is in _store, return new ValueTask<int>(value) — the
    //         synchronous fast path, no allocation.
    //       - If the key is NOT in _store, simulate a 50ms load with
    //         Task.Delay(50, ct), store the result (key * 42), and return it.
    //         The slow path returns a wrapped Task<int>.
    //
    //       Signature:
    //         public ValueTask<int> GetOrLoadAsync(int key, CancellationToken ct = default)
    //
    //       Hint: the slow path is best written by extracting an inner
    //       `async Task<int> LoadAsync(int k, CancellationToken c)` and
    //       returning `new ValueTask<int>(LoadAsync(key, ct))`.
    public ValueTask<int> GetOrLoadAsync(int key, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}

// ---------------------------------------------------------------------------
// Section 4 — IAsyncEnumerable<int> with cancellation
// ---------------------------------------------------------------------------

static async Task RunSection4Async()
{
    Console.WriteLine("== Section 4: IAsyncEnumerable<int> with cancellation ==");

    using var cts = new CancellationTokenSource();
    var values = new List<int>();

    try
    {
        // TODO: consume CountForeverAsync() with `await foreach`, calling
        //       .WithCancellation(cts.Token) so the consumer-side token
        //       reaches the producer's [EnumeratorCancellation] parameter.
        //       After 5 items, call cts.Cancel() to trigger cancellation;
        //       the next yield should throw OperationCanceledException.
        //
        //       Inside the foreach body:
        //           values.Add(v);
        //           if (values.Count == 5) cts.Cancel();
        throw new NotImplementedException();
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"Streamed: {string.Join(' ', values)}");
        Console.WriteLine($"Cancelled after {values.Count}; saw OperationCanceledException as expected.");
    }
}

// TODO: implement an `async IAsyncEnumerable<int> CountForeverAsync(...)` that
//       yields 0, 1, 2, ... with Task.Delay(20, ct) between yields. The `ct`
//       parameter MUST be annotated with [EnumeratorCancellation]:
//
//         private static async IAsyncEnumerable<int> CountForeverAsync(
//             [EnumeratorCancellation] CancellationToken ct = default)
//         {
//             for (var i = 0; ; i++)
//             {
//                 await Task.Delay(20, ct);
//                 yield return i;
//             }
//         }

// ---------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ---------------------------------------------------------------------------
//
// Section 1:
//
//   static async Task RunSection1Async()
//   {
//       Console.WriteLine("== Section 1: Task.WhenAll on three I/O-bound tasks ==");
//       var sw = Stopwatch.StartNew();
//       var t1 = SimulateFetchAsync(200);
//       var t2 = SimulateFetchAsync(200);
//       var t3 = SimulateFetchAsync(200);
//       await Task.WhenAll(t1, t2, t3);
//       Console.WriteLine($"Fetched 3 in ~{sw.ElapsedMilliseconds}ms (all three ran concurrently)");
//       Console.WriteLine();
//   }
//
// Section 2:
//
//   await foreach (var t in Task.WhenEach(tasks))
//   {
//       var (label, delayMs) = await t;
//       Console.WriteLine($"  {label}  ({delayMs}ms)");
//   }
//   Console.WriteLine();
//
// Section 3 (the cache):
//
//   public ValueTask<int> GetOrLoadAsync(int key, CancellationToken ct = default)
//   {
//       if (_store.TryGetValue(key, out var hit))
//           return new ValueTask<int>(hit);
//       return new ValueTask<int>(LoadAsync(key, ct));
//
//       async Task<int> LoadAsync(int k, CancellationToken c)
//       {
//           await Task.Delay(50, c);
//           var value = k * 42;
//           _store[k] = value;
//           return value;
//       }
//   }
//
// Section 4 (the producer):
//
//   private static async IAsyncEnumerable<int> CountForeverAsync(
//       [EnumeratorCancellation] CancellationToken ct = default)
//   {
//       for (var i = 0; ; i++)
//       {
//           await Task.Delay(20, ct);
//           yield return i;
//       }
//   }
//
// Section 4 (the consumer):
//
//   await foreach (var v in CountForeverAsync().WithCancellation(cts.Token))
//   {
//       values.Add(v);
//       if (values.Count == 5) cts.Cancel();
//   }
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// Every async API in Phase 2 of C9 — and every async API in any production
// .NET codebase — is built from these four primitives:
//
//   - Task.WhenAll for "fan out, wait for all."
//   - Task.WhenEach for "fan out, process in completion order."
//   - ValueTask<T> for "this might complete synchronously most of the time."
//   - IAsyncEnumerable<T> for "produce an async stream the consumer pulls."
//
// And every one of them must cooperate with a CancellationToken. The
// state-machine transform you saw in Lecture 1 is invisible at the API
// surface — what you see is the four primitives above. By the end of this
// exercise the syntax is muscle memory.
//
// ---------------------------------------------------------------------------
