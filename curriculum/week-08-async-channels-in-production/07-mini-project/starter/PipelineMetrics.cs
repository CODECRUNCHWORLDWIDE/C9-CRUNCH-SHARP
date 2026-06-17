// Sharp Pipe — starter PipelineMetrics.cs
//
// The Meter and instruments for the pipeline. Copy into
// src/SharpPipe.Pipeline/PipelineMetrics.cs.
//
// Meter name: "SharpPipe", version "1.0.0".
//
// Instruments:
//   - pipeline.items.produced      Counter<long>   {items}
//   - pipeline.items.consumed      Counter<long>   {items}
//   - pipeline.items.dropped       Counter<long>   {items}
//   - pipeline.items.failed        Counter<long>   {items}
//   - pipeline.queue.depth_at_write Histogram<double> {items}
//   - pipeline.process.latency      Histogram<double> ms
//
// In Program.cs (Host), register the Meter with OpenTelemetry's
// MeterProviderBuilder and the Prometheus exporter listens for it.

#nullable enable

using System;
using System.Diagnostics.Metrics;

namespace SharpPipe.Pipeline;

public sealed class PipelineMetrics : IDisposable
{
    public const string MeterName = "SharpPipe";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter;

    public Counter<long> ItemsProduced { get; }
    public Counter<long> ItemsConsumed { get; }
    public Counter<long> ItemsDropped { get; }
    public Counter<long> ItemsFailed { get; }
    public Histogram<double> QueueDepthAtWrite { get; }
    public Histogram<double> ProcessLatencyMs { get; }

    public PipelineMetrics()
    {
        _meter = new Meter(MeterName, MeterVersion);

        ItemsProduced = _meter.CreateCounter<long>(
            name: "pipeline.items.produced",
            unit: "{items}",
            description: "Events written to the input channel.");

        ItemsConsumed = _meter.CreateCounter<long>(
            name: "pipeline.items.consumed",
            unit: "{items}",
            description: "Events processed by a consumer.");

        ItemsDropped = _meter.CreateCounter<long>(
            name: "pipeline.items.dropped",
            unit: "{items}",
            description: "Events dropped by the channel's full-mode policy.");

        ItemsFailed = _meter.CreateCounter<long>(
            name: "pipeline.items.failed",
            unit: "{items}",
            description: "Events whose processor threw an exception.");

        QueueDepthAtWrite = _meter.CreateHistogram<double>(
            name: "pipeline.queue.depth_at_write",
            unit: "{items}",
            description: "Channel depth observed at producer write.");

        ProcessLatencyMs = _meter.CreateHistogram<double>(
            name: "pipeline.process.latency",
            unit: "ms",
            description: "Per-item processing wall time in milliseconds.");
    }

    public void Dispose() => _meter.Dispose();
}
