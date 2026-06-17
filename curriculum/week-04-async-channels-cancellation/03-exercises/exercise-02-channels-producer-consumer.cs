// Exercise 2 — Channels producer/consumer pipeline
//
// Goal: Build a bounded Channel<int> pipeline with ONE producer and THREE
//       consumers. The producer emits 30 work items as fast as it can; each
//       consumer processes items at a different speed; the bounded channel's
//       BoundedChannelFullMode.Wait policy applies back-pressure so the
//       producer never runs faster than the slowest consumer.
//
//       This is the exact shape of the mini-project's crawler — one URL
//       producer, multiple HTTP fetchers, back-pressure provided by the
//       channel's capacity.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir ChannelsDemo && cd ChannelsDemo
//      dotnet new console -n ChannelsDemo -o src/ChannelsDemo
//
//    Replace src/ChannelsDemo/Program.cs with THIS FILE.
//
//    No NuGet packages are needed — System.Threading.Channels is in the BCL.
//
// 2. Fill in the bodies marked `// TODO`.
//
// 3. Run:
//
//      dotnet run --project src/ChannelsDemo
//
// 4. The output should match the SMOKE OUTPUT below (modulo exact timing and
//    the order in which the consumers grab items — that race is non-deterministic).
//
// ACCEPTANCE CRITERIA
//
//   [ ] All TODOs implemented.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] The channel uses Channel.CreateBounded<int>(capacity: 5) with
//       BoundedChannelFullMode.Wait and SingleWriter = true, SingleReader = false.
//   [ ] The producer calls channel.Writer.Complete() inside a `finally`.
//   [ ] Each consumer uses `await foreach (var item in reader.ReadAllAsync(ct))`.
//   [ ] The whole program completes only after every item is processed
//       (i.e., the consumers' Tasks all RanToCompletion).
//   [ ] The producer NEVER outpaces the slowest consumer by more than the
//       buffer capacity — log "buffer-full-wait" if WriteAsync took >5ms.
//
// SMOKE OUTPUT (target, modulo timing and worker IDs)
//
//   == Channels producer/consumer pipeline ==
//   Producer: starting; will emit 30 items into a bounded channel (capacity 5).
//   [worker-0] processing 1   (sleep 60ms)
//   [worker-1] processing 2   (sleep 20ms)
//   [worker-2] processing 3   (sleep 40ms)
//   [worker-1] processing 4   (sleep 20ms)
//   ... (output continues; consumers race in non-deterministic order) ...
//   [worker-2] processing 30  (sleep 40ms)
//   Producer: completed. Wrote 30 items; back-pressure triggered ~24 times.
//   Consumer summary:
//     worker-0  processed 6 items
//     worker-1  processed 15 items
//     worker-2  processed 9 items
//   Total: 30
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints are at the bottom of the file.

using System.Diagnostics;
using System.Threading.Channels;

const int totalItems   = 30;
const int channelCapacity = 5;
const int consumerCount = 3;

Console.WriteLine("== Channels producer/consumer pipeline ==");
Console.WriteLine($"Producer: starting; will emit {totalItems} items into a bounded channel (capacity {channelCapacity}).");

using var cts = new CancellationTokenSource();
var ct = cts.Token;

// TODO: create a Channel.CreateBounded<int>(new BoundedChannelOptions(channelCapacity)
//       {
//           FullMode = BoundedChannelFullMode.Wait,
//           SingleWriter = true,
//           SingleReader = false,
//       }).
//       Hint: `using System.Threading.Channels;` is already imported.
Channel<int> channel = throw new NotImplementedException();

var perWorkerCounts = new int[consumerCount];
var backPressureCount = 0;

// TODO: start ONE producer Task that:
//       - writes items 1..totalItems to channel.Writer via WriteAsync(item, ct).
//       - measures the time of each WriteAsync; if >5ms, increments
//         Interlocked.Increment(ref backPressureCount).
//       - in a `finally`, calls channel.Writer.Complete().
//
//   Task producer = Task.Run(async () =>
//   {
//       try
//       {
//           for (var i = 1; i <= totalItems; i++)
//           {
//               var sw = Stopwatch.StartNew();
//               await channel.Writer.WriteAsync(i, ct);
//               if (sw.ElapsedMilliseconds > 5) Interlocked.Increment(ref backPressureCount);
//           }
//       }
//       finally
//       {
//           channel.Writer.Complete();
//       }
//   }, ct);
Task producer = throw new NotImplementedException();

// TODO: start `consumerCount` consumer Tasks via Enumerable.Range(0, consumerCount).Select(workerId =>
//       Task.Run(...)).ToArray(). Each consumer:
//       - has a unique sleep time: workerId == 0 → 60ms, 1 → 20ms, 2 → 40ms.
//       - uses `await foreach (var item in channel.Reader.ReadAllAsync(ct))`.
//       - prints "[worker-{workerId}] processing {item}   (sleep {ms}ms)".
//       - delays `Task.Delay(ms, ct)` to simulate work.
//       - increments perWorkerCounts[workerId] (lock-free is fine; only one
//         worker writes to its own slot).
//
//   var sleeps = new[] { 60, 20, 40 };
//   var consumers = Enumerable.Range(0, consumerCount).Select(workerId => Task.Run(async () =>
//   {
//       var sleep = sleeps[workerId];
//       await foreach (var item in channel.Reader.ReadAllAsync(ct))
//       {
//           Console.WriteLine($"[worker-{workerId}] processing {item,2}   (sleep {sleep}ms)");
//           await Task.Delay(sleep, ct);
//           perWorkerCounts[workerId]++;
//       }
//   }, ct)).ToArray();
Task[] consumers = throw new NotImplementedException();

// TODO: await both sides. Use Task.WhenAll on producer + all consumers.
//
//   await Task.WhenAll(new[] { producer }.Concat(consumers));
throw new NotImplementedException();

Console.WriteLine($"Producer: completed. Wrote {totalItems} items; back-pressure triggered ~{backPressureCount} times.");
Console.WriteLine("Consumer summary:");
for (var i = 0; i < consumerCount; i++)
{
    Console.WriteLine($"  worker-{i}  processed {perWorkerCounts[i]} items");
}
Console.WriteLine($"Total: {perWorkerCounts.Sum()}");
Console.WriteLine();
Console.WriteLine("Build succeeded · 0 warnings · 0 errors");

// ---------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ---------------------------------------------------------------------------
//
// Channel creation:
//
//   var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(channelCapacity)
//   {
//       FullMode = BoundedChannelFullMode.Wait,
//       SingleWriter = true,
//       SingleReader = false,
//   });
//
// Producer:
//
//   Task producer = Task.Run(async () =>
//   {
//       try
//       {
//           for (var i = 1; i <= totalItems; i++)
//           {
//               var sw = Stopwatch.StartNew();
//               await channel.Writer.WriteAsync(i, ct);
//               if (sw.ElapsedMilliseconds > 5) Interlocked.Increment(ref backPressureCount);
//           }
//       }
//       finally
//       {
//           channel.Writer.Complete();
//       }
//   }, ct);
//
// Consumers:
//
//   var sleeps = new[] { 60, 20, 40 };
//   var consumers = Enumerable.Range(0, consumerCount).Select(workerId => Task.Run(async () =>
//   {
//       var sleep = sleeps[workerId];
//       await foreach (var item in channel.Reader.ReadAllAsync(ct))
//       {
//           Console.WriteLine($"[worker-{workerId}] processing {item,2}   (sleep {sleep}ms)");
//           await Task.Delay(sleep, ct);
//           perWorkerCounts[workerId]++;
//       }
//   }, ct)).ToArray();
//
// Final await:
//
//   await Task.WhenAll(new[] { producer }.Concat(consumers));
//
// ---------------------------------------------------------------------------
// EXPECTED ANALYSIS — what you should observe in the output
// ---------------------------------------------------------------------------
//
// The fastest worker (worker-1, 20ms) should process ~15 items.
// The medium worker (worker-2, 40ms) should process ~9 items.
// The slowest worker (worker-0, 60ms) should process ~6 items.
//
// Sum = 30. Always. The producer's Complete() ensures every item lands
// somewhere; the channel's internal sync ensures no item is dropped or
// duplicated.
//
// The back-pressure count should be ~24 (everything except the first 5
// items, which fit in the buffer immediately, hits the WriteAsync wait).
// If you see backPressureCount == 0, your channel is unbounded — re-check
// the BoundedChannelOptions.
//
// ---------------------------------------------------------------------------
// VARIATIONS TO TRY (optional, after the base case works)
// ---------------------------------------------------------------------------
//
// A. Change FullMode to DropOldest. Re-run. You will see total < 30 because
//    some items get dropped when the buffer is full. (Make sure to remove the
//    "Total: 30" assertion if you do this.)
//
// B. Change SingleReader to true (incorrectly, because you have three readers).
//    Re-run. The output will be subtly wrong — items may be lost or duplicated.
//    This is why "SingleReader = true with multiple readers" is a silent bug
//    you must not introduce.
//
// C. Swap the consumer body for `Parallel.ForEachAsync(channel.Reader.ReadAllAsync(ct), ...)`.
//    Does it work? (No — Parallel.ForEachAsync wants an IEnumerable<T> or
//    IAsyncEnumerable<T>; ReadAllAsync gives you the latter, but the
//    parallelism cap of Parallel.ForEachAsync is on the *input*, not on the
//    consumers. You don't need both abstractions.)
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// This is the EXACT shape of the mini-project's crawler:
//
//   - Replace `int` with `Uri`.
//   - Replace the producer's loop with "seed URLs + URLs discovered by parsing."
//   - Replace the consumer body with "fetch the URL and yield a CrawlResult."
//   - Wire the consumers' output back through an IAsyncEnumerable<CrawlResult>.
//
// The skeleton you wrote here is what you'll pull out of Exercise 2 and drop
// into the Crawler.Pipeline class on Friday.
//
// ---------------------------------------------------------------------------
