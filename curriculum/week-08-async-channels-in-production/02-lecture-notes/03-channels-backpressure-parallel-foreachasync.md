# Lecture 3 — Channels in Production, Backpressure, Parallel.ForEachAsync, and ThreadPool Starvation

> **Reading time:** ~90 minutes. **Hands-on time:** ~45 minutes (you build a bounded-channel pipeline, attach `Meter` counters, and read `dotnet-counters` output live).

This is the lecture that closes the week. Lectures 1 and 2 made you fluent in the *primitives* — the `await` lowering, `SynchronizationContext`, `ConfigureAwait`, `IAsyncEnumerable<T>`. This lecture makes you fluent in the *production composition* of those primitives — the channel pipeline that backpressures correctly under load, the `Parallel.ForEachAsync` call that fans out to a fixed degree of concurrency, the `Meter` counters that an operator scrapes from Prometheus, and the `dotnet-counters` workflow that diagnoses ThreadPool starvation when something goes wrong. By the end you can build, observe, and debug a producer/consumer pipeline that an SRE would approve.

## 3.1 — Why bounded channels are the right default

Week 4 introduced `Channel.CreateUnbounded<T>()`. It is the easier of the two to demonstrate because its behaviour is unremarkable: writes always succeed immediately, reads block when the queue is empty, the queue grows without limit. Production code almost never wants an unbounded channel. Consider:

```csharp
// BAD in production
var channel = Channel.CreateUnbounded<RawEvent>();

// Producer (fast)
foreach (var ev in EventSource.Stream())
    await channel.Writer.WriteAsync(ev);

// Consumer (slow)
await foreach (var ev in channel.Reader.ReadAllAsync())
    await ExpensiveProcess(ev);
```

If the producer is faster than the consumer — and at any non-trivial scale, *something* eventually is — the queue grows. There is no signal back to the producer that it should slow down. The process's working set climbs, the GC has more work to do every collection, the tail latency on every other thread degrades, and eventually the OS kills the process for exceeding its memory limit. The bug is not in the consumer's speed; the bug is in choosing an unbounded queue for an unbounded producer.

A *bounded* channel solves this:

```csharp
var channel = Channel.CreateBounded<RawEvent>(new BoundedChannelOptions(capacity: 1_000)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = false,
    SingleWriter = true,
});
```

When the buffer fills, `WriteAsync` does not return until a reader has drained at least one slot. The producer's loop *pauses*. That pause is the queue-level backpressure: the channel propagates the consumer's slowness back to the producer, and the producer matches the consumer's pace. The system as a whole operates at the *sustainable* throughput — the rate the consumer can keep up with — rather than briefly bursting beyond it and then crashing.

When in doubt, use `CreateBounded`. The capacity is a knob you turn; the *default* should be small enough that a misbehaving consumer becomes obvious early, and large enough that brief bursts of producer activity do not stall it. A capacity of 10× the steady-state batch size is a reasonable starting point — a producer that emits 100 events per batch should be paired with a 1,000-slot channel.

## 3.2 — The four `BoundedChannelFullMode` values

When the bounded channel is full, the producer's `WriteAsync` has four possible behaviours:

| Mode | Behaviour when channel is full | When to use |
|---|---|---|
| `Wait` | `WriteAsync` pends until a slot opens. The producer is slowed by the consumer. | The default. Use when every item matters and producer-pause is acceptable. |
| `DropNewest` | `WriteAsync` returns immediately. The most recent buffered item is evicted to make space; the new item is enqueued. | Use when the consumer should see the latest state, not historical history (e.g., live telemetry updates). |
| `DropOldest` | `WriteAsync` returns immediately. The oldest buffered item is evicted; the new item is enqueued at the back. | Use when the queue is a "recent history" buffer and stale items are useless. |
| `DropWrite` | `WriteAsync` returns immediately. The new item is silently dropped. | Use when items are aggregable downstream (e.g., metric counters that accumulate elsewhere). |

The reference is at <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.boundedchannelfullmode>. The source is in `dotnet/runtime`: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Channels/src/System/Threading/Channels/BoundedChannel.cs>. The full-mode logic is short — about thirty lines — and worth reading once.

Choosing between the four is a design decision specific to your pipeline. Three real-world examples:

- **An audit-log pipeline** (every event must reach the database eventually): `Wait`. Slowness on the database side propagates back to the event producer, which is itself bounded by external input. No data is lost; throughput drops.
- **A websocket position-update feed** (clients want the latest position, not history): `DropOldest`. If a slow consumer is behind by 500 ms, the buffered intermediate positions are stale; serve the latest one.
- **A metrics-emission pipeline** (counters that aggregate downstream): `DropWrite`. If the consumer is overloaded, silently dropping individual counter increments is fine — the aggregate remains approximately correct.

There is no general rule. Pick deliberately, write a comment explaining the choice, and revisit it the first time the pipeline misbehaves.

## 3.3 — The fan-out pipeline shape

A production pipeline rarely has one producer and one consumer. The common shape is:

```text
Producer ──► BoundedChannel<Input> ──► [Consumer 1, Consumer 2, ..., Consumer N] ──► Aggregator
                                                                                      │
                                                                                      ▼
                                                                                    Sink
```

The producer writes raw inputs to the channel. N consumers read concurrently, each performing the per-item work (parsing, enrichment, validation, API calls). Each consumer pushes its output to an aggregator — sometimes via a second channel, sometimes via a thread-safe accumulator (a `ConcurrentBag<T>`, a `ConcurrentDictionary<K, V>`, or a `Channel<T>` configured `SingleWriter = false`).

Here is the canonical implementation:

```csharp
using System.Threading.Channels;

public sealed class Pipeline
{
    private readonly Channel<RawInput> _inputs;
    private readonly Channel<Processed> _outputs;
    private readonly int _consumerCount;

    public Pipeline(int capacity, int consumerCount)
    {
        _inputs = Channel.CreateBounded<RawInput>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,    // we have N concurrent consumers
            SingleWriter = true,     // exactly one producer
        });

        _outputs = Channel.CreateBounded<Processed>(new BoundedChannelOptions(capacity * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,     // exactly one aggregator
            SingleWriter = false,    // N consumers write
        });

        _consumerCount = consumerCount;
    }

    public async Task RunAsync(
        IAsyncEnumerable<RawInput> source,
        Func<RawInput, CancellationToken, ValueTask<Processed>> process,
        Func<Processed, CancellationToken, ValueTask> sink,
        CancellationToken ct = default)
    {
        Task producerTask = ProducerAsync(source, ct);
        Task[] consumerTasks = new Task[_consumerCount];
        for (int i = 0; i < _consumerCount; i++)
            consumerTasks[i] = ConsumerAsync(process, ct);
        Task aggregatorTask = AggregatorAsync(sink, ct);

        // Producer finishes first (it has a finite source); then close inputs.
        try
        {
            await producerTask.ConfigureAwait(false);
        }
        finally
        {
            _inputs.Writer.Complete();
        }

        // Wait for all consumers to drain the input channel.
        try
        {
            await Task.WhenAll(consumerTasks).ConfigureAwait(false);
        }
        finally
        {
            _outputs.Writer.Complete();
        }

        // Aggregator runs until output channel completes.
        await aggregatorTask.ConfigureAwait(false);
    }

    private async Task ProducerAsync(IAsyncEnumerable<RawInput> source, CancellationToken ct)
    {
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            await _inputs.Writer.WriteAsync(item, ct).ConfigureAwait(false);
        }
    }

    private async Task ConsumerAsync(
        Func<RawInput, CancellationToken, ValueTask<Processed>> process,
        CancellationToken ct)
    {
        await foreach (var item in _inputs.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            Processed result = await process(item, ct).ConfigureAwait(false);
            await _outputs.Writer.WriteAsync(result, ct).ConfigureAwait(false);
        }
    }

    private async Task AggregatorAsync(
        Func<Processed, CancellationToken, ValueTask> sink,
        CancellationToken ct)
    {
        await foreach (var item in _outputs.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await sink(item, ct).ConfigureAwait(false);
        }
    }
}
```

Five things are load-bearing in this code:

**Load-bearing fact 1: `Writer.Complete()` is called exactly once, after the producer finishes.** This signals to the reader's `ReadAllAsync` that no more items are coming; the consumers' `await foreach` loops exit cleanly. Forgetting `Complete()` causes the consumers to hang forever waiting for items that never arrive.

**Load-bearing fact 2: `Writer.Complete()` is in a `finally`.** If the producer throws, we still mark the channel complete so the consumers do not deadlock. If we wanted to *propagate* the producer's exception to the consumers, we would call `_inputs.Writer.Complete(producerEx)` instead of `Complete()` with no argument — the consumers' `ReadAllAsync` would then throw at the appropriate point.

**Load-bearing fact 3: Two-level completion.** The producer's completion closes the input channel; the consumers' completion closes the output channel. The aggregator finishes when the output channel is closed. This staircase pattern is the right shape for any pipeline; it generalises to N stages.

**Load-bearing fact 4: `SingleReader` / `SingleWriter` flags.** Each is a *promise* you make to the channel implementation. If you have N concurrent consumers, `SingleReader` must be `false`; if you set it to `true` and then read concurrently, you get undefined behaviour. The flags allow the implementation to take a faster code path when the promise is true. The runtime issue introducing them is at <https://github.com/dotnet/runtime/issues/27545>.

**Load-bearing fact 5: `ReadAllAsync(ct)` is the modern consumer loop.** Before `ReadAllAsync` (added in .NET 5), the idiom was a manual `WaitToReadAsync` + `TryRead` loop. `ReadAllAsync` returns an `IAsyncEnumerable<T>` that handles the loop for you, and you consume it with `await foreach`. The reference is at <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channelreader-1.readallasync>.

## 3.4 — Instrumentation with `Meter`

A pipeline that is not observable is not in production. The `System.Diagnostics.Metrics` API (`Meter`, `Counter<T>`, `Histogram<T>`) is the .NET 6+ first-party way to expose metrics that `dotnet-counters` and OpenTelemetry / Prometheus exporters can consume. The API reference: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>.

The pattern:

```csharp
using System.Diagnostics.Metrics;

public sealed class PipelineMetrics : IDisposable
{
    private readonly Meter _meter;
    public Counter<long> ItemsProduced { get; }
    public Counter<long> ItemsConsumed { get; }
    public Counter<long> ItemsDropped { get; }
    public Histogram<double> QueueDepthAtWrite { get; }
    public Histogram<double> ProcessLatencyMs { get; }

    public PipelineMetrics(string meterName = "C9.SharpPipe")
    {
        _meter = new Meter(meterName, version: "1.0.0");

        ItemsProduced = _meter.CreateCounter<long>("pipeline.items.produced",
            unit: "{items}", description: "Items written to the input channel.");
        ItemsConsumed = _meter.CreateCounter<long>("pipeline.items.consumed",
            unit: "{items}", description: "Items processed by a consumer.");
        ItemsDropped = _meter.CreateCounter<long>("pipeline.items.dropped",
            unit: "{items}", description: "Items dropped by the channel's full mode.");
        QueueDepthAtWrite = _meter.CreateHistogram<double>("pipeline.queue.depth_at_write",
            unit: "{items}", description: "Channel depth observed at producer write.");
        ProcessLatencyMs = _meter.CreateHistogram<double>("pipeline.process.latency",
            unit: "ms", description: "Wall-clock processing time per item.");
    }

    public void Dispose() => _meter.Dispose();
}
```

In the producer:

```csharp
await _inputs.Writer.WriteAsync(item, ct).ConfigureAwait(false);
_metrics.ItemsProduced.Add(1);
// approximate queue depth — exact depth requires reflection on internals
_metrics.QueueDepthAtWrite.Record(_inputs.Reader.Count);
```

In the consumer:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
Processed result = await process(item, ct).ConfigureAwait(false);
_metrics.ProcessLatencyMs.Record(sw.Elapsed.TotalMilliseconds);
_metrics.ItemsConsumed.Add(1);
```

Once instrumented, the pipeline is visible to `dotnet-counters`:

```bash
dotnet-counters monitor --process-id $(pgrep -f SharpPipe) --counters C9.SharpPipe
```

You see live values for every counter and histogram, updated once per second by default. The Microsoft Learn reference is at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters>.

For Prometheus integration, the `OpenTelemetry.Exporter.Prometheus.AspNetCore` package scrapes every `Meter` registered with `OpenTelemetry.MetricsProviderBuilder` and exposes it at `/metrics`. The lecture skips the wiring because the mini-project covers it; the key fact is that `Meter` itself is exporter-agnostic — the same `Counter<long>` instance feeds `dotnet-counters`, Prometheus, OTLP, and any other exporter you configure.

## 3.5 — `Parallel.ForEachAsync` (.NET 6+)

`Parallel.ForEachAsync` is the "bounded-degree async work over a collection" primitive. The signature:

```csharp
public static Task ForEachAsync<TSource>(
    IEnumerable<TSource> source,
    ParallelOptions parallelOptions,
    Func<TSource, CancellationToken, ValueTask> body);
```

There is also an `IAsyncEnumerable<TSource>` overload (.NET 6+) and a `CancellationToken`-only overload (no `ParallelOptions`). The reference: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync>. The source: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Tasks.Parallel/src/System/Threading/Tasks/Parallel.ForEachAsync.cs>.

The canonical use:

```csharp
var ids = new[] { 1, 2, 3, 4, 5, ..., 100 };
var options = new ParallelOptions
{
    MaxDegreeOfParallelism = 10,
    CancellationToken = ct,
};

await Parallel.ForEachAsync(ids, options, async (id, token) =>
{
    HttpResponseMessage resp = await _http.GetAsync($"/items/{id}", token)
                                          .ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    // process resp
});
```

Three semantics worth knowing:

**Semantics 1: `MaxDegreeOfParallelism` bounds the concurrency, not the threads.** At most 10 in-flight calls; the actual threads used are however many the awaits suspend on. Typical implementation uses 1–2 threads for I/O-bound work.

**Semantics 2: The first exception cancels the rest.** If one body invocation throws, `Parallel.ForEachAsync` cancels the others (via the internal `CancellationTokenSource`) and propagates an `AggregateException` (or the single exception, in some versions). The remaining items in the source are not enumerated. If you want "process all, collect errors at the end," wrap each body in `try/catch` and collect.

**Semantics 3: It is async-friendly.** The body is `Func<T, CancellationToken, ValueTask>` — fully async, with the cancellation token threaded in. Unlike the older `Parallel.ForEach`, there is no risk of blocking a `ThreadPool` thread on synchronous work; the await unblocks.

The relationship to channels: `Parallel.ForEachAsync` is functionally similar to "create a bounded channel of size `MaxDegreeOfParallelism`, launch that many consumers, push items into the channel, wait for completion." If your problem fits the pattern "iterate a collection, do bounded-concurrent async work on each item, wait for everything to finish," `Parallel.ForEachAsync` is more ergonomic than rolling your own. If your problem is "long-running pipeline with continuous input and concurrent consumers," a `Channel<T>` is the right shape.

## 3.6 — Concurrency versus parallelism

The Rob Pike formulation, restated for .NET:

- **Concurrency** is the *structuring* of independently-running pieces of work that *may* execute simultaneously. `await` is the .NET concurrency primitive.
- **Parallelism** is the *simultaneous execution* of those pieces of work on multiple CPU cores. `Parallel.ForEachAsync` and `Task.Run` are .NET parallelism primitives.

A single-threaded ASP.NET Core process can handle 4,000 concurrent requests because every request is async-structured: each request makes an HTTP call, awaits it, releases the thread, resumes when the response arrives. The structure is concurrent; the execution is not parallel (it could run on one CPU core, and would, if the CPU were the bottleneck — but the HTTP I/O is the bottleneck and the CPU is mostly idle). This is the "concurrency without parallelism" case.

A `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 10` over CPU-bound work is parallelism: ten CPU cores doing work simultaneously, each in its own continuation slot. The structure is concurrent (each is async); the execution is parallel.

The mistake the formulation prevents: assuming concurrency implies parallelism. An `async` method does *not* automatically run on a different thread. A `Channel<T>` does not automatically dispatch work to multiple CPU cores. You get parallelism only when you spawn multiple consumers, or use `Parallel.ForEachAsync`, or explicitly `Task.Run`. If your pipeline has one producer, one consumer, one aggregator — every step is `async`, but the steady-state execution is serial: only one of them runs at a time, just on whichever `ThreadPool` thread the most recent `await` released to.

## 3.7 — ThreadPool starvation: what it is and how to diagnose it

`ThreadPool` starvation is the failure mode where the `ThreadPool` does not have a free worker thread when a continuation is ready to run. The continuation queues; the queue length grows; the latency of every async operation in the process degrades. In ASP.NET Core, this manifests as `request latency p99 climbs from 50 ms to 30 s` with no corresponding increase in downstream call latency. The CPU is fine. The downstream services are fine. The pool is starved.

The cause is *almost always* sync-over-async on `ThreadPool` threads. Concretely:

```csharp
// BAD: this method blocks a ThreadPool thread for the duration of the HTTP call
public IActionResult GetUser(int id)
{
    User user = _userSvc.GetUserAsync(id).Result;  // blocks
    return Ok(user);
}
```

Every request that hits this handler consumes a `ThreadPool` thread for the entire duration of the HTTP call. If 100 requests arrive concurrently and `_userSvc.GetUserAsync` takes 200 ms each, 100 threads are blocked for 200 ms. The `ThreadPool`'s default `MinThreads` on a developer laptop is the number of CPU cores (let us say 8). Beyond `MinThreads`, the pool grows at *one thread per 500 ms* — the deliberate throttling exists to prevent runaway growth in pathological cases. The 92 requests beyond the first 8 queue. The queue length spikes; tail latency follows.

The diagnostic workflow:

```bash
# 1. Find the process ID
pgrep -f MyApp

# 2. Capture System.Runtime counters live
dotnet-counters monitor --process-id 12345 --counters System.Runtime

# 3. Read the trio:
#    - threadpool-thread-count: current worker thread count
#    - threadpool-queue-length: queued work items waiting for a worker
#    - threadpool-completed-items-count: cumulative items processed
```

The signature of starvation:

- `threadpool-thread-count` grows slowly (≈ one per 500 ms) — the throttling is visible.
- `threadpool-queue-length` is non-zero and growing.
- `threadpool-completed-items-count` is increasing more slowly than your request rate.

If all three are present, you have starvation. Confirm by finding the offending sync-over-async with `dotnet-stack report --process-id 12345` (which prints managed stacks for every thread) — look for stacks ending in `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.

Remediations, in order:

1. **Fix the sync-over-async.** Make the calling method `async`. This is the only durable fix. Every other remediation is a stopgap.
2. **Raise `MinThreads`** as an emergency mitigation: `ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200)`. This bypasses the throttling, but does not fix the root cause; the process now allocates 200 threads for no good reason. Use as a 24-hour bridge while the fix ships.
3. **Add a queue (a bounded `Channel<T>`).** If the sync-over-async is in third-party code you cannot change, move it behind a single-threaded consumer that serialises the blocking calls. The producer-side ASP.NET request thread is then async; the blocking is bounded to one slow thread.

Challenge 2 walks through a deliberately starvation-prone web app, captures the counters, and asks you to diagnose. Run it before quiz day.

## 3.8 — `Task<T>` versus `ValueTask<T>` (final restatement)

We covered the rules in Week 7 and again in lecture 1. The decision matrix for this week's pipeline code:

| Method completes synchronously most of the time? | Method is awaited exactly once by every caller? | You control all callers? | Use |
|---|---|---|---|
| No | — | — | `Task<T>` |
| Yes | No | — | `Task<T>` (the safe choice) |
| Yes | Yes | No | `Task<T>` (other callers may misuse) |
| Yes | Yes | Yes | `ValueTask<T>` (the optimisation) |

Three places in this week's code where `ValueTask<T>` is *correct*:

- **`IAsyncEnumerator<T>.MoveNextAsync`** — the BCL designs the iterator interface around `ValueTask<bool>` because synchronous yields are common. You do not control this; you receive it.
- **Internal helper methods on a hot path that complete synchronously > 50% of the time.** Example: a cache lookup that returns a cached value on hit, fetches from network on miss.
- **`ChannelReader<T>.TryRead`** returns a `bool` synchronously, but `Reader.ReadAsync` returns a `ValueTask<T>` that is synchronously complete when an item is already available. Same pattern.

In the pipeline mini-project, the consumer's per-item process method may be a `ValueTask` if you control the pipeline composition. The aggregator's sink method should be `Task` unless you have measured a synchronous-completion case.

## 3.9 — Closing the loop: the production checklist

Before any async pipeline ships, walk this list:

- [ ] Every channel is bounded.
- [ ] `BoundedChannelFullMode` is chosen deliberately and commented in code.
- [ ] Producer signals completion with `Writer.Complete()` in a `finally`.
- [ ] Consumers consume with `await foreach (var x in reader.ReadAllAsync(ct))`.
- [ ] Every async method propagates `CancellationToken` to every internal `await`.
- [ ] Library code uses `.ConfigureAwait(false)` on every `await`.
- [ ] No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` appears anywhere.
- [ ] A `Meter` instance counts items produced, items consumed, and items dropped.
- [ ] A `Histogram<double>` records per-item processing latency.
- [ ] `dotnet-counters monitor --counters <meter-name>` shows live values.
- [ ] An `IAsyncDisposable` cleanup path runs even on cancellation.
- [ ] `IAsyncEnumerable<T>` methods mark their `CancellationToken` parameter with `[EnumeratorCancellation]`.
- [ ] `Parallel.ForEachAsync` uses an explicit `MaxDegreeOfParallelism` (not the default `-1`, which is unbounded).

The checklist is the entire output of this week distilled. The mini-project produces a system that satisfies every line; the homework exercises ensure each line shows up in your repertoire at least once.

## 3.10 — Where this leaves the curriculum

Week 9 picks up with `IHostedService` and `BackgroundService` — the long-running counterparts to the channel pipelines built this week. Week 10 covers the diagnostics deeper: `dotnet-trace`, structured logging with `ILogger`, OpenTelemetry distributed tracing. The async primitives finalised here become the substrate for everything in phases 4 and 5 of C9.

For now: build the mini-project. Read the four Stephen Toub posts in `resources.md` end-to-end before you start. The Channels post in particular is the canonical reference; if you can paraphrase its first half from memory by Sunday, the week was a success.
