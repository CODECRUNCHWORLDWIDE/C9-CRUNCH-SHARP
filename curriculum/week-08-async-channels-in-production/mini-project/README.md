# Mini-Project — Sharp Pipe: an instrumented channel pipeline

> Build a producer / bounded-channel / N-consumer / aggregator pipeline that ingests a stream of synthetic events, processes each event through a deliberately-variable workload, aggregates results, and exposes live `Meter`-based metrics. Demonstrate that the pipeline backpressures correctly when consumers are slow, drops items predictably under load, and shuts down cleanly on cancellation. By the end you have a small, well-instrumented producer/consumer library that an operator at a real company would be willing to put behind a `/health` endpoint.

This is the canonical "build the production-shaped async pipeline" exercise for .NET 8. Every senior .NET engineer has built something with this shape — an event ingestion pipeline, a webhook fan-out worker, a metrics aggregator, a job queue. The shape is identical across all of them: one producer (or a small fixed number), a bounded channel for backpressure, a configurable number of consumers running concurrent async work, an aggregator that collapses the consumer outputs into a final form, and a sink. The Sharp Pipe project is that experience in microcosm.

**Estimated time:** ~8.5 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A solution called `SharpPipe` with five projects:

- `src/SharpPipe.Core/` — domain types (`Event`, `ProcessedEvent`, `Summary`), the `IClock` abstraction, and the `PipelineOptions` configuration record.
- `src/SharpPipe.Pipeline/` — the producer/consumer pipeline itself, with the `Pipeline` class, the `Meter`-based `PipelineMetrics`, and the `IEventProcessor` interface.
- `src/SharpPipe.Host/` — a console host (`net8.0`) that wires the pipeline to a synthetic event source, runs it under a 30-second load test, and exposes the metrics via OpenTelemetry's Prometheus exporter on `http://localhost:9091/metrics`.
- `src/SharpPipe.Benchmarks/` — a BenchmarkDotNet suite that measures throughput at various consumer counts and channel capacities.
- `tests/SharpPipe.Tests/` — xUnit tests asserting backpressure, cancellation, error propagation, and the metric counters.

You ship one solution; the four `src/` projects each have their own `.csproj`. The host runs end-to-end; the tests verify each correctness property in isolation.

---

## Rules

- **You may** read Microsoft Learn, the `dotnet/runtime` source, the Week 8 lecture notes and exercises, Stephen Toub's blog posts, and any free .NET documentation.
- **You may NOT** depend on third-party NuGet packages other than:
  - `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Hosting.Abstractions`
  - `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Logging.Console`
  - `OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.Prometheus.AspNetCore`
  - `BenchmarkDotNet`
  - `xUnit` + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` + `FluentAssertions`
- Target framework: `net8.0` for every project. C# language version: the default (`12.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in `Directory.Build.props`.
- No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` anywhere in `src/` (tests may use `Task.WaitAll(timeout)` in `Dispose` patterns, but only with a justifying comment).
- Every public async method ends in `Async` and accepts a `CancellationToken` as its last parameter.

---

## Project structure

```
SharpPipe/
├── Directory.Build.props                 (Nullable, ImplicitUsings, TWA E true, net8.0)
├── SharpPipe.sln
├── src/
│   ├── SharpPipe.Core/
│   │   ├── SharpPipe.Core.csproj
│   │   ├── Event.cs                       (record: Id, Timestamp, Kind, Payload, Weight)
│   │   ├── ProcessedEvent.cs              (record: SourceId, Kind, Score, LatencyMs)
│   │   ├── Summary.cs                     (record: TotalProcessed, TotalDropped, Per-kind aggregates)
│   │   ├── IClock.cs                      (interface for DateTimeOffset.UtcNow; testable)
│   │   ├── SystemClock.cs                 (default IClock implementation)
│   │   ├── PipelineOptions.cs             (Capacity, ConsumerCount, MaxConcurrency,
│   │   │                                   BoundedChannelFullMode, BackpressureBudgetMs, etc.)
│   │   └── IEventProcessor.cs             (ValueTask<ProcessedEvent> ProcessAsync(Event, CT))
│   │
│   ├── SharpPipe.Pipeline/
│   │   ├── SharpPipe.Pipeline.csproj
│   │   ├── Pipeline.cs                    (the main class; RunAsync(IAsyncEnumerable<Event>, ...))
│   │   ├── PipelineMetrics.cs             (Meter + counters + histograms)
│   │   ├── PipelineErrors.cs              (typed exceptions: PipelineCancelledException, etc.)
│   │   └── DefaultEventProcessor.cs       (a simulation: weight-based ms delay + score)
│   │
│   ├── SharpPipe.Host/
│   │   ├── SharpPipe.Host.csproj
│   │   ├── Program.cs                     (host setup, OpenTelemetry Prometheus exporter)
│   │   ├── SyntheticEventSource.cs        (an IAsyncEnumerable<Event> producer)
│   │   └── appsettings.json               (PipelineOptions binding)
│   │
│   └── SharpPipe.Benchmarks/
│       ├── SharpPipe.Benchmarks.csproj
│       └── Program.cs                     (BDN suite; [Params] for capacity & consumer count)
│
└── tests/
    └── SharpPipe.Tests/
        ├── SharpPipe.Tests.csproj
        ├── BackpressureTests.cs
        ├── CancellationTests.cs
        ├── ErrorPropagationTests.cs
        ├── MetricsTests.cs
        └── FakeEventProcessor.cs           (test helper)
```

A starter `Pipeline.cs` is provided in `starter/Pipeline.cs` next to this README; copy it into `src/SharpPipe.Pipeline/` and complete the TODOs.

---

## Functional requirements

### F1 — Producer

The producer reads from an `IAsyncEnumerable<Event>` source. The Host project's `SyntheticEventSource` generates events at a configurable rate (default: 1,000 events/sec) with variable `Weight` values (1–10) drawn from a uniform distribution. The producer writes each event to the input channel using `WriteAsync(event, ct)`.

### F2 — Bounded input channel

The channel is `Channel.CreateBounded<Event>(new BoundedChannelOptions(options.Capacity) { FullMode = options.FullMode, SingleWriter = true, SingleReader = false })`. Default capacity 1,024. Default `FullMode = Wait` (backpressure). The capacity is a knob settable via `appsettings.json`.

### F3 — Consumer pool

N consumers run concurrently (default N = 4). Each consumer reads from the channel via `await foreach (var ev in reader.ReadAllAsync(ct))` and calls `IEventProcessor.ProcessAsync(ev, ct)` to produce a `ProcessedEvent`. The default `DefaultEventProcessor` simulates work with `Task.Delay(weight, ct)` — heavier events take longer.

### F4 — Output channel and aggregator

Consumers write `ProcessedEvent` to a second bounded channel (capacity `options.Capacity * 2`). A single aggregator consumes from this channel and accumulates per-kind statistics (count, sum of scores, mean latency). On shutdown, the aggregator produces a final `Summary` record.

### F5 — Cancellation and shutdown

The pipeline accepts a `CancellationToken` at `RunAsync`. Cancelling the token:

1. Stops the producer's source enumeration.
2. Allows in-flight consumer work to complete (per-item, not the entire pipeline).
3. Drains the input channel (no items lost).
4. Closes the output channel after the last consumer finishes.
5. The aggregator finishes processing the output channel and returns the `Summary`.
6. `RunAsync` returns the `Summary` to the caller.

Cancellation during a running pipeline should not throw; it should signal a graceful shutdown. Forcing a hard stop is supported via a second token (`hardStopCt`) that aborts the consumers mid-item.

### F6 — Error propagation

If the producer throws, the input channel is completed with the exception (`writer.Complete(ex)`); the consumers' `ReadAllAsync` throws at the appropriate point. The pipeline collects these into an `AggregateException` and surfaces it from `RunAsync`.

### F7 — Metrics

`PipelineMetrics` exposes a `Meter` named `SharpPipe` (version `1.0.0`) with:

- `Counter<long>` `pipeline.items.produced` — items written to input channel.
- `Counter<long>` `pipeline.items.consumed` — items processed by a consumer.
- `Counter<long>` `pipeline.items.dropped` — items dropped by full-mode (when `FullMode != Wait`).
- `Counter<long>` `pipeline.items.failed` — items whose processing threw.
- `Histogram<double>` `pipeline.queue.depth_at_write` (unit `{items}`) — channel depth observed at producer write.
- `Histogram<double>` `pipeline.process.latency` (unit `ms`) — per-item processing wall time.
- `Histogram<double>` `pipeline.aggregator.batch_size` (unit `{items}`) — aggregator's recent burst-size sampling.

The Host wires `Meter` to OpenTelemetry's Prometheus exporter at `http://localhost:9091/metrics`. Running `dotnet-counters monitor --process-id $(pgrep SharpPipe.Host) --counters SharpPipe` shows live values from the same `Meter`.

### F8 — Observability under load

Run the Host for 30 seconds against a 1,000 events/sec source with 4 consumers and a 1,024-capacity channel. Open `http://localhost:9091/metrics` in a browser; you should see counters incrementing in real time. Capture a screenshot for the README.

---

## Non-functional requirements

### NF1 — No deadlocks

No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` anywhere in `src/`. The pipeline must run without hanging under any of: cancellation at any point, producer exception, consumer exception, aggregator exception, channel full-mode set to any of the four values.

### NF2 — Bounded resource usage

At steady state, the process's working set should not grow unbounded. Heap allocations per processed event are not zero (we are not in Week 7 anymore) but should be < 1 KB per event in the steady-state path. Verify with a `[MemoryDiagnoser]` benchmark.

### NF3 — Cancellation latency

A cancellation request should drain the pipeline within `options.ShutdownBudgetSec` (default 10 seconds) under nominal load. The xUnit test `CancellationTests.GracefulShutdown_FinishesWithinBudget` asserts this.

### NF4 — Backpressure visibility

Running with `FullMode = Wait`, capacity 8, and a slow consumer (50 ms per item) should produce a write-latency histogram (`pipeline.queue.depth_at_write`) where p99 is non-trivial. The xUnit test `BackpressureTests.SlowConsumer_PendsProducer` asserts the producer pends.

---

## Acceptance criteria

Mini-project passes if all of these are true:

- [ ] `dotnet build` runs clean (0 warnings, 0 errors) for the solution.
- [ ] `dotnet test` runs all xUnit tests green.
- [ ] `dotnet run --project src/SharpPipe.Host` starts a 30-second pipeline run, exposes `/metrics` on port 9091.
- [ ] `curl http://localhost:9091/metrics | grep pipeline_items` returns non-zero counters after a few seconds of running.
- [ ] `dotnet-counters monitor --process-id $(pgrep -f SharpPipe.Host) --counters SharpPipe` shows live counters.
- [ ] At the end of the 30-second run, the `Summary` is printed: total events produced, total consumed, total dropped (zero with `Wait`), and per-kind aggregates.
- [ ] `BenchmarkDotNet` results in `src/SharpPipe.Benchmarks` show throughput rising linearly with consumer count up to the CPU's I/O ceiling (this is the load-test deliverable).
- [ ] The `BackpressureTests`, `CancellationTests`, `ErrorPropagationTests`, and `MetricsTests` all pass.

---

## Suggested implementation order

1. **Hour 1** — Scaffold the solution. Get `Directory.Build.props`, all five projects, and the `Event` / `ProcessedEvent` / `Summary` record types in place. `dotnet build` should succeed with empty bodies.
2. **Hour 2** — Implement `Pipeline.cs` from the starter file. Get the producer/consumer/aggregator flow working without metrics. Write a basic integration test that runs the pipeline for 1 second and asserts items are consumed.
3. **Hour 3** — Add `PipelineMetrics`. Wire it through. Write the `MetricsTests` that assert counters increment correctly.
4. **Hour 4** — Implement the four cancellation/error-propagation behaviours. Write `CancellationTests` and `ErrorPropagationTests`.
5. **Hour 5** — Implement `BackpressureTests`. Verify that `FullMode = Wait` pends the producer and `FullMode = DropOldest` drops correctly.
6. **Hour 6** — Build the Host: synthetic source, OpenTelemetry Prometheus exporter, console output. Run for 30 seconds, capture the metrics screenshot.
7. **Hour 7** — Add the `SharpPipe.Benchmarks` BDN suite. Run at consumer counts 1, 2, 4, 8, 16 and capacities 64, 256, 1024. Capture the result table.
8. **Hour 8.5** — Write the project README: architecture diagram (ASCII is fine), `Summary` of metrics under load, BDN results, screenshot of `/metrics`. Read through the whole thing and clean up.

---

## Grading rubric (30 pts)

| Criterion | Points |
|---|---:|
| `dotnet build` succeeds, 0 warnings | 2 |
| `dotnet test` all green | 4 |
| `dotnet run --project SharpPipe.Host` runs end-to-end without errors | 3 |
| `/metrics` endpoint returns live counters | 2 |
| `dotnet-counters monitor --counters SharpPipe` shows live counters | 1 |
| Backpressure test passes (Wait mode pends producer under slow consumer) | 3 |
| Cancellation test passes (graceful shutdown within 10 seconds) | 3 |
| Error propagation test passes (producer exception surfaces as AggregateException) | 2 |
| BenchmarkDotNet suite runs and produces a result table | 2 |
| No `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` in `src/` | 2 |
| Every public async method takes `CancellationToken` last | 2 |
| Project README has architecture diagram + metrics screenshot + BDN results | 2 |
| Code is editorially clean: nullable annotations, file-scoped namespaces, no dead code | 2 |
| **Total** | **30** |

**Pass:** ≥ 22/30. **Honours:** ≥ 27/30.

---

## A note on observability

Half the senior-engineer skill on this mini-project is the observability piece: `Meter`, OpenTelemetry, Prometheus, the `/metrics` endpoint. A pipeline you can read while it is running is a pipeline you can debug; a pipeline whose internal queue depth is invisible to you is a black box. The single greatest skill you can develop from this exercise is the *reflex* to add a metric when you build a queue. Practise it here; carry it everywhere.

The other half is the correctness piece: the cancellation that drains cleanly, the error that surfaces unambiguously, the backpressure that visibly slows the producer when the consumer falls behind. The xUnit tests are designed to falsify each of those properties one at a time. If a test fails, the bug is in your pipeline's semantics, not in the test. Read the test, then read your code; fix the code.

By Sunday night, you have a small library that any team would recognise as production-grade. That is the C9 promise: every week's deliverable is shippable. This week's is a queue.
