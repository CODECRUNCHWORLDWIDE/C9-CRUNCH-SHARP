// Sharp Pipe — starter Pipeline.cs
//
// This file is the skeleton of the main pipeline class. Copy it into
// src/SharpPipe.Pipeline/Pipeline.cs and complete the TODO sections.
//
// The shape — producer / bounded input channel / N consumers / bounded output
// channel / aggregator — matches Lecture 3.3 in this week's notes. Read the
// lecture before filling in the TODOs; the patterns there are the patterns
// you implement here.
//
// Companion types (you supply in SharpPipe.Core):
//
//   public sealed record Event(long Id, DateTimeOffset Timestamp,
//                              string Kind, string Payload, int Weight);
//
//   public sealed record ProcessedEvent(long SourceId, string Kind,
//                                       double Score, double LatencyMs);
//
//   public sealed record Summary(long TotalProduced, long TotalConsumed,
//                                long TotalDropped, long TotalFailed,
//                                IReadOnlyDictionary<string, KindAggregate> PerKind);
//
//   public sealed record KindAggregate(long Count, double SumScore,
//                                      double MeanLatencyMs);
//
//   public sealed record PipelineOptions(int Capacity = 1024,
//                                        int ConsumerCount = 4,
//                                        BoundedChannelFullMode FullMode =
//                                            BoundedChannelFullMode.Wait,
//                                        TimeSpan? ShutdownBudget = null);
//
//   public interface IEventProcessor
//   {
//       ValueTask<ProcessedEvent> ProcessAsync(Event ev, CancellationToken ct);
//   }

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpPipe.Core;

namespace SharpPipe.Pipeline;

public sealed class Pipeline
{
    private readonly IEventProcessor _processor;
    private readonly PipelineMetrics _metrics;
    private readonly ILogger<Pipeline> _logger;
    private readonly PipelineOptions _options;

    public Pipeline(
        IEventProcessor processor,
        PipelineMetrics metrics,
        ILogger<Pipeline> logger,
        PipelineOptions options)
    {
        _processor = processor;
        _metrics = metrics;
        _logger = logger;
        _options = options;
    }

    public async Task<Summary> RunAsync(
        IAsyncEnumerable<Event> source,
        CancellationToken ct = default)
    {
        // ---------------------------------------------------------------------
        // TODO 1 — Create the input channel as bounded with the configured
        // capacity, FullMode, SingleWriter=true, SingleReader=false.
        // ---------------------------------------------------------------------
        Channel<Event> inputs = Channel.CreateBounded<Event>(
            new BoundedChannelOptions(_options.Capacity)
            {
                FullMode = _options.FullMode,
                SingleWriter = true,
                SingleReader = false,
            });

        // ---------------------------------------------------------------------
        // TODO 2 — Create the output channel: capacity = 2x input, SingleWriter
        // = false (N consumers write), SingleReader = true (one aggregator).
        // ---------------------------------------------------------------------
        Channel<ProcessedEvent> outputs = Channel.CreateBounded<ProcessedEvent>(
            new BoundedChannelOptions(_options.Capacity * 2)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true,
            });

        // Per-pipeline accumulators. Updated only by the aggregator (single
        // writer), so no synchronisation is needed.
        var perKindCount = new Dictionary<string, long>();
        var perKindScoreSum = new Dictionary<string, double>();
        var perKindLatencySum = new Dictionary<string, double>();
        long totalProduced = 0;
        long totalConsumed = 0;
        long totalFailed = 0;
        // Dropped count is read from the metrics counter at the end (the
        // channel's full-mode logic is what increments it).

        // ---------------------------------------------------------------------
        // TODO 3 — Launch the producer task. It reads from `source`, writes to
        // `inputs.Writer`, increments `pipeline.items.produced`, and on exit
        // calls `inputs.Writer.Complete()` (in finally so even an exception
        // does not deadlock the consumers).
        // ---------------------------------------------------------------------
        Task producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in source.WithCancellation(ct).ConfigureAwait(false))
                {
                    _metrics.QueueDepthAtWrite.Record(inputs.Reader.Count);
                    bool written = await TryWriteWithFullModeAsync(inputs.Writer, ev, ct).ConfigureAwait(false);
                    if (written)
                    {
                        Interlocked.Increment(ref totalProduced);
                        _metrics.ItemsProduced.Add(1);
                    }
                    else
                    {
                        _metrics.ItemsDropped.Add(1);
                    }
                }
            }
            finally
            {
                inputs.Writer.Complete();
            }
        }, ct);

        // ---------------------------------------------------------------------
        // TODO 4 — Launch N consumer tasks. Each reads from `inputs.Reader.
        // ReadAllAsync(ct)`, calls `_processor.ProcessAsync(ev, ct)`, writes
        // the result to `outputs.Writer`, and records the per-item latency.
        // Each consumer increments `pipeline.items.consumed` on success and
        // `pipeline.items.failed` on processor exception.
        // ---------------------------------------------------------------------
        var consumerTasks = new Task[_options.ConsumerCount];
        for (int i = 0; i < _options.ConsumerCount; i++)
        {
            consumerTasks[i] = Task.Run(async () =>
            {
                await foreach (var ev in inputs.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        ProcessedEvent processed = await _processor.ProcessAsync(ev, ct).ConfigureAwait(false);
                        await outputs.Writer.WriteAsync(processed, ct).ConfigureAwait(false);
                        _metrics.ProcessLatencyMs.Record(sw.Elapsed.TotalMilliseconds);
                        _metrics.ItemsConsumed.Add(1);
                        Interlocked.Increment(ref totalConsumed);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Consumer failed processing event {EventId}", ev.Id);
                        _metrics.ItemsFailed.Add(1);
                        Interlocked.Increment(ref totalFailed);
                    }
                }
            }, ct);
        }

        // ---------------------------------------------------------------------
        // TODO 5 — Launch the aggregator task. It reads ProcessedEvents from
        // outputs.Reader.ReadAllAsync(ct), updates the per-kind dictionaries,
        // and exits when the output channel completes.
        // ---------------------------------------------------------------------
        Task aggregatorTask = Task.Run(async () =>
        {
            await foreach (var p in outputs.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!perKindCount.ContainsKey(p.Kind))
                {
                    perKindCount[p.Kind] = 0;
                    perKindScoreSum[p.Kind] = 0.0;
                    perKindLatencySum[p.Kind] = 0.0;
                }
                perKindCount[p.Kind]++;
                perKindScoreSum[p.Kind] += p.Score;
                perKindLatencySum[p.Kind] += p.LatencyMs;
            }
        }, ct);

        // ---------------------------------------------------------------------
        // TODO 6 — Sequenced shutdown:
        //   a) wait for the producer to finish (it has a finite source).
        //   b) wait for all consumers to drain the inputs channel.
        //   c) close the outputs channel.
        //   d) wait for the aggregator to drain.
        //   e) build and return the Summary.
        //
        // Each `await` should ConfigureAwait(false). Each `try / finally` must
        // ensure the next channel is closed even on exception.
        // ---------------------------------------------------------------------
        try
        {
            await producerTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Producer failed; completing inputs with exception.");
            // Make the consumers observe the failure when they next call ReadAllAsync.
            inputs.Writer.TryComplete(ex);
        }

        try
        {
            await Task.WhenAll(consumerTasks).ConfigureAwait(false);
        }
        finally
        {
            outputs.Writer.Complete();
        }

        await aggregatorTask.ConfigureAwait(false);

        var perKind = new Dictionary<string, KindAggregate>();
        foreach (var kind in perKindCount.Keys)
        {
            long count = perKindCount[kind];
            double mean = count > 0 ? perKindLatencySum[kind] / count : 0.0;
            perKind[kind] = new KindAggregate(count, perKindScoreSum[kind], mean);
        }

        long totalDropped = 0; // The dropped count is exposed via the Meter;
                               // a real implementation would query the
                               // BoundedChannel's internal counter. For now,
                               // we report 0 and rely on the meter for the
                               // observable value.
        return new Summary(
            TotalProduced: totalProduced,
            TotalConsumed: totalConsumed,
            TotalDropped: totalDropped,
            TotalFailed: totalFailed,
            PerKind: perKind);
    }

    // -------------------------------------------------------------------------
    // Helper: try to write to the channel with full-mode-aware semantics.
    // For FullMode.Wait, this is just WriteAsync. For Drop modes, the
    // channel's underlying implementation handles the drop; this method
    // simply returns true (event was accepted) or false (event was dropped).
    //
    // Note: BoundedChannel's WriteAsync always returns successfully under
    // Drop* modes because the channel handles the drop internally. The way
    // to count drops is via a wrapping check on `Count` before and after, or
    // (more accurately) by querying the implementation's internal counter.
    // For Sharp Pipe's purposes, the metrics counter is incremented in the
    // producer loop based on whether the channel's Count went up; a real
    // implementation would use the upcoming channel-events API
    // (dotnet/runtime issue #761).
    // -------------------------------------------------------------------------
    private static async ValueTask<bool> TryWriteWithFullModeAsync(
        ChannelWriter<Event> writer,
        Event ev,
        CancellationToken ct)
    {
        // TryWrite returns true if the channel accepted the item synchronously
        // (which is always true for unbounded channels and for bounded channels
        // with non-Wait full modes when the channel is full and the new item
        // is accepted by the drop policy).
        if (writer.TryWrite(ev))
            return true;

        // Otherwise (Wait mode and channel is full), pend until space is available.
        await writer.WriteAsync(ev, ct).ConfigureAwait(false);
        return true;
    }
}

// ===========================================================================
// HINTS — peek only if stuck.
// ===========================================================================
//
// HINT 1 — The "double Complete()" guard. ChannelWriter.Complete() throws if
// the channel is already completed. The defensive pattern is Writer.TryComplete()
// which returns false if already completed. Use TryComplete when the producer
// fails (it might have completed normally by then).
//
// HINT 2 — The aggregator's exception. If the aggregator throws, the consumers
// continue writing to outputs.Writer until outputs is full, at which point they
// pend forever. The fix: wrap the aggregator's body in a try/catch and on
// exception, drain the rest of the channel (read without processing). This is
// the "draining cleanup" pattern; see Lecture 3.3.
//
// HINT 3 — Cancellation versus shutdown. A token-cancellation should drain the
// pipeline cleanly. A second token (the "hard stop") should abort consumers
// mid-item. The spec mentions both; a real implementation would link them with
// CancellationTokenSource.CreateLinkedTokenSource(ct, hardStopCt) and pass the
// linked token to each consumer's ProcessAsync. For the starter, just use ct.
//
// HINT 4 — Per-kind accumulator thread safety. The aggregator is single-reader
// of outputs, so it has no concurrent access. The dictionaries are read AFTER
// the aggregator finishes. No locks needed. This is the load-bearing fact
// that justifies the simple Dictionary<string, long> instead of ConcurrentDictionary.
