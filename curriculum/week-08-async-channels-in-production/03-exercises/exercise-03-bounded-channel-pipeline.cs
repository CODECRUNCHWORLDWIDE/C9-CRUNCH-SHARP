// Exercise 3 — Build a producer / bounded-channel / 4-consumer / aggregator
//              pipeline and observe backpressure when consumers are slower
//              than the producer.
//
// Goal: Construct the canonical fan-out pipeline shape, run it under a
//       deliberately slow consumer, and demonstrate (with logged timestamps)
//       that the producer's WriteAsync pends when the channel fills.
//
// Estimated time: 75 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir Ex03-BoundedPipeline && cd Ex03-BoundedPipeline
//      dotnet new console -n Ex03 -o src/Ex03 --framework net8.0
//      cd src/Ex03
//
//    Replace src/Ex03/Program.cs with THIS FILE.
//
// 2. Fill in the four TODOs below.
//
// 3. Run:
//
//      dotnet run -c Release
//
// ACCEPTANCE CRITERIA
//
//   [ ] Channel.CreateBounded<int> with capacity=8 and FullMode=Wait.
//   [ ] One producer task writes 100 ints into the channel.
//   [ ] Four consumer tasks read concurrently via ReadAllAsync(ct).
//   [ ] Producer logs "wrote N at +XX ms"; consumer logs "consumed N at +XX ms".
//   [ ] When the producer logs N=9..N=99, the write timestamps show pauses
//       (because the channel was full and WriteAsync waited).
//   [ ] At process exit, the sum of all consumed values equals 0+1+...+99 = 4950.
//   [ ] writer.Complete() is called in a `finally` after the producer loop.
//   [ ] dotnet run -c Release succeeds with 0 warnings, 0 errors.
//
// EXPECTED OUTPUT (illustrative, abbreviated)
//
//   Producer wrote 0 at +5 ms
//   Consumer 1 consumed 0 at +6 ms
//   ...
//   Producer wrote 7 at +12 ms
//   Producer wrote 8 at +22 ms    <-- waited for consumer 1 to drain
//   Producer wrote 9 at +42 ms    <-- waited again (backpressure visible)
//   ...
//   Consumer 3 consumed 99 at +542 ms
//   Sum across consumers: 4950 (expected 4950). PASS.
//
// REFLECTION QUESTIONS — answer in results-ex03.md after running:
//
// 1. The producer writes 100 values into a channel of capacity 8 and is paired
//    with consumers that each take ~5ms to process an item. Walk through the
//    timeline of the first 12 producer writes. Where does WriteAsync pend?
// 2. Change capacity to 100. Does the producer ever pend? Why is bounding with
//    100 functionally equivalent (in this exercise) to unbounded?
// 3. Change the consumer's per-item delay from 5ms to 50ms. What happens to
//    the producer's write timestamps?
// 4. Change FullMode from Wait to DropOldest. What changes in the consumed sum?
//
// Inline hints at the bottom of the file.

#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Ex03;

public static class Program
{
    public static async Task Main()
    {
        var stopwatch = Stopwatch.StartNew();
        long Start() => stopwatch.ElapsedMilliseconds;
        using var cts = new CancellationTokenSource();
        CancellationToken ct = cts.Token;

        // ---------------------------------------------------------------------
        // TODO 1 — Create a bounded channel with capacity 8 and FullMode.Wait.
        // The single-writer and multi-reader flags should reflect the topology.
        // ---------------------------------------------------------------------
        Channel<int> channel = Channel.CreateBounded<int>(new BoundedChannelOptions(capacity: 8)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
            // FYI: SingleReader=false because we have 4 concurrent consumers.
        });

        // ---------------------------------------------------------------------
        // TODO 2 — Producer task: write 0..99 to the channel; log each write
        // with the elapsed milliseconds. Call Writer.Complete() in finally.
        // ---------------------------------------------------------------------
        Task producer = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    await channel.Writer.WriteAsync(i, ct).ConfigureAwait(false);
                    Console.WriteLine($"Producer wrote {i,3} at +{Start(),4} ms");
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        // ---------------------------------------------------------------------
        // TODO 3 — Four consumer tasks: each consumes via ReadAllAsync(ct),
        // simulates 5ms of work per item, accumulates a per-consumer sum, and
        // logs each item with the consumer index and elapsed time.
        // ---------------------------------------------------------------------
        long[] sums = new long[4];
        Task[] consumers = new Task[4];
        for (int idx = 0; idx < 4; idx++)
        {
            int id = idx + 1;  // 1..4 for prettier logs
            int slot = idx;
            consumers[slot] = Task.Run(async () =>
            {
                long sum = 0;
                await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await Task.Delay(5, ct).ConfigureAwait(false);  // simulate work
                    sum += item;
                    Console.WriteLine($"Consumer {id} consumed {item,3} at +{Start(),4} ms");
                }
                sums[slot] = sum;
            }, ct);
        }

        // ---------------------------------------------------------------------
        // TODO 4 — Wait for the producer to finish, then wait for all consumers
        // to drain the channel. (Producer.Complete() in finally ensures
        // ReadAllAsync's enumeration terminates.)
        // ---------------------------------------------------------------------
        await producer.ConfigureAwait(false);
        await Task.WhenAll(consumers).ConfigureAwait(false);

        long total = 0;
        foreach (var s in sums) total += s;
        const long expected = (99L * 100L) / 2L;  // 4950
        Console.WriteLine($"Sum across consumers: {total} (expected {expected}). " +
                          $"{(total == expected ? "PASS" : "FAIL")}");
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — The channel options. Note that SingleWriter=true is a promise: if
// you tried to launch two producer tasks, the runtime's behaviour is undefined.
// SingleReader=false is correct because we have 4 consumer tasks reading.
//
// HINT 2 — The producer's `try / finally` is the load-bearing pattern of the
// week. Writer.Complete() MUST run even on exception, or the consumers hang.
//
// HINT 3 — The consumer's `await foreach` over ReadAllAsync is the modern
// pattern (.NET 5+). Before ReadAllAsync, the loop was:
//
//   while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
//   {
//       while (channel.Reader.TryRead(out var item))
//       {
//           // process item
//       }
//   }
//
// Both forms work; ReadAllAsync is shorter.
//
// HINT 4 — `Task.Delay(5, ct)` is the simulated work. Replace it with real
// I/O (an HTTP call, a database query) when you adapt the pipeline to a
// real problem.
//
// FURTHER EXPLORATION (do at least one)
//
// A. Add a `Meter` and a Counter<long> for "items consumed", increment from
//    each consumer. Run `dotnet-counters monitor --counters Ex03` in another
//    terminal and watch the counter climb live.
//
// B. Change the producer to read from an IAsyncEnumerable<int> source (e.g.,
//    your Exercise 2's ReadNumbersAsync). The producer becomes:
//      await foreach (var x in source.WithCancellation(ct))
//          await channel.Writer.WriteAsync(x, ct);
//    Confirm the pipeline still backpressures correctly.
//
// C. Wrap one consumer's body in `try { ... } catch (Exception ex) { ... }`
//    and have it throw on item==42. Confirm the OTHER consumers continue
//    until the channel drains. What is the right "stop the whole pipeline
//    on first error" behaviour? (Answer: call cts.Cancel() in the catch.)
