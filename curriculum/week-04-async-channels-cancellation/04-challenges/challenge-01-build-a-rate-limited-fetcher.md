# Challenge 1 — Build a rate-limited fetcher

**Time estimate:** ~2 hours.

## Problem statement

Build a `RateLimitedFetcher` that wraps `HttpClient` and guarantees no more than **10 requests per second** across all callers — even with 8 concurrent consumers hammering it. The limiter must be implemented with the primitives from this week (`Channel<T>`, `Task`, `CancellationToken`) — not with `Thread.Sleep`, not with a `SemaphoreSlim`, and not with `System.Threading.RateLimiting`. The point is to *understand* what a rate limiter is by building one; the stretch goal at the bottom invites you to compare your hand-rolled version with the BCL implementation.

Then prove with `BenchmarkDotNet` and a wall-clock measurement that the limiter holds under load.

## The contract

You will produce one repo (`c9-week-04-rate-limited-fetcher-<yourhandle>`) containing:

```
RateLimitedFetcher/
├── RateLimitedFetcher.sln
├── .gitignore
├── Directory.Build.props
├── src/
│   ├── RateLimitedFetcher.Core/
│   │   ├── RateLimitedFetcher.Core.csproj
│   │   ├── RateLimitedFetcher.cs   (the decorator)
│   │   ├── TokenBucket.cs           (the Channel<TimeSlot> + refill loop)
│   │   └── IFetcher.cs              (the interface)
│   ├── RateLimitedFetcher.Demo/
│   │   ├── RateLimitedFetcher.Demo.csproj
│   │   └── Program.cs               (the 8-consumer wall-clock test)
│   └── RateLimitedFetcher.Bench/
│       ├── RateLimitedFetcher.Bench.csproj
│       └── Program.cs               (the BenchmarkDotNet harness)
├── notes/
│   ├── design.md            (one page on the token-bucket choice)
│   └── measurements.md      (the wall-clock + BenchmarkDotNet output)
└── README.md
```

`README.md` is the entry point. `notes/design.md` is one page explaining *why* a `Channel<TimeSlot>` was the right shape; `notes/measurements.md` is the proof.

## Acceptance criteria

- [ ] A solution with three projects: `RateLimitedFetcher.Core` (class library), `RateLimitedFetcher.Demo` (console), and `RateLimitedFetcher.Bench` (console with `BenchmarkDotNet`).
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet run --project src/RateLimitedFetcher.Demo` launches 8 concurrent consumers that each call `FetchAsync("https://httpbin.org/get")` 25 times (200 requests total), and reports the wall-clock duration. With 10 req/sec, 200 requests should take *no less than* 19 seconds (200/10 - 1 for the head-start). Output the observed rate and pass if it is ≤ 10 req/sec ± 5%.
- [ ] The rate limiter is implemented with a `Channel<TimeSlot>` and a refill loop. Not `SemaphoreSlim`. Not `Thread.Sleep`.
- [ ] The limiter accepts a `CancellationToken` on `FetchAsync` and cancels in-flight token waits when the caller cancels.
- [ ] `BenchmarkDotNet` summary shows `FetchAsync` p50 latency ≤ 110ms under the 10 req/sec budget (one slot every 100ms; the call always finds a slot within one slot-width).
- [ ] `notes/design.md` answers: "Why a `Channel<TimeSlot>`? What would have been wrong about a `SemaphoreSlim(10)`? What is the token-bucket vs leaky-bucket distinction, and which did I pick?"
- [ ] `notes/measurements.md` includes the wall-clock output, the `BenchmarkDotNet` summary table, and one paragraph of analysis.

## Suggested order of operations

### Phase 1 — Skeleton (~15 min)

```bash
mkdir RateLimitedFetcher && cd RateLimitedFetcher
dotnet new sln -n RateLimitedFetcher
dotnet new gitignore && git init

dotnet new classlib -n RateLimitedFetcher.Core  -o src/RateLimitedFetcher.Core
dotnet new console  -n RateLimitedFetcher.Demo  -o src/RateLimitedFetcher.Demo
dotnet new console  -n RateLimitedFetcher.Bench -o src/RateLimitedFetcher.Bench

dotnet sln add src/RateLimitedFetcher.Core/RateLimitedFetcher.Core.csproj
dotnet sln add src/RateLimitedFetcher.Demo/RateLimitedFetcher.Demo.csproj
dotnet sln add src/RateLimitedFetcher.Bench/RateLimitedFetcher.Bench.csproj

dotnet add src/RateLimitedFetcher.Demo  reference src/RateLimitedFetcher.Core
dotnet add src/RateLimitedFetcher.Bench reference src/RateLimitedFetcher.Core
dotnet add src/RateLimitedFetcher.Bench package BenchmarkDotNet
```

Add the standard `Directory.Build.props`.

Commit: `Skeleton: three projects + Directory.Build.props`.

### Phase 2 — `IFetcher` interface and `RateLimitedFetcher` shell (~15 min)

```csharp
// IFetcher.cs
public interface IFetcher
{
    ValueTask<string> FetchAsync(Uri uri, CancellationToken ct = default);
}
```

```csharp
// RateLimitedFetcher.cs
public sealed class RateLimitedFetcher(HttpClient http, TokenBucket bucket) : IFetcher, IAsyncDisposable
{
    public async ValueTask<string> FetchAsync(Uri uri, CancellationToken ct = default)
    {
        await bucket.WaitForSlotAsync(ct);
        return await http.GetStringAsync(uri, ct);
    }

    public ValueTask DisposeAsync() => bucket.DisposeAsync();
}
```

Commit: `IFetcher + RateLimitedFetcher decorator shell`.

### Phase 3 — `TokenBucket` with `Channel<TimeSlot>` (~30 min)

The core design: a `Channel<TimeSlot>` with a capacity equal to the burst budget (default 1), and a refill loop running as a `BackgroundService`-style `Task` that emits one `TimeSlot` per `Interval`. Callers `await bucket.WaitForSlotAsync(ct)` which is just `await _channel.Reader.ReadAsync(ct)`.

```csharp
public sealed class TokenBucket : IAsyncDisposable
{
    private readonly Channel<TimeSlot> _channel;
    private readonly CancellationTokenSource _refillCts = new();
    private readonly Task _refillLoop;
    private readonly TimeSpan _interval;

    public TokenBucket(int requestsPerSecond, int burst = 1)
    {
        _interval = TimeSpan.FromMilliseconds(1000.0 / requestsPerSecond);

        _channel = Channel.CreateBounded<TimeSlot>(new BoundedChannelOptions(burst)
        {
            FullMode = BoundedChannelFullMode.DropWrite, // never block the refill loop
            SingleWriter = true,
            SingleReader = false,
        });

        _refillLoop = Task.Run(() => RefillLoopAsync(_refillCts.Token));
    }

    public async ValueTask WaitForSlotAsync(CancellationToken ct)
        => _ = await _channel.Reader.ReadAsync(ct);

    private async Task RefillLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                _channel.Writer.TryWrite(new TimeSlot(DateTimeOffset.UtcNow));
                // TryWrite returns false if the buffer is full; that's fine
                // — we just drop the slot because nobody was waiting for it.
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        finally
        {
            _channel.Writer.Complete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _refillCts.Cancel();
        try { await _refillLoop; } catch (OperationCanceledException) { }
        _refillCts.Dispose();
    }
}

public readonly record struct TimeSlot(DateTimeOffset At);
```

Key design notes for `notes/design.md`:

- **Why `Channel<TimeSlot>` over `SemaphoreSlim(10)`?** A `SemaphoreSlim(10)` lets you have **10 in-flight at any moment** — that's a concurrency limit, not a rate limit. With 8 fast consumers, you'd see bursts of 10 concurrent requests followed by zero, repeating; the per-second average would be wildly different from 10. The `Channel<TimeSlot>` enforces "one slot per 100ms" regardless of how many callers are waiting.
- **Why `BoundedChannelFullMode.DropWrite`?** The refill loop's job is to publish one slot every 100ms. If no consumer is waiting, dropping the slot is correct — you can't accumulate "credit" beyond the burst capacity, or the rate limiter wouldn't actually limit during a quiet-then-busy phase.
- **Why `PeriodicTimer`?** It is the BCL's intended primitive for "do something every N." More accurate than `Task.Delay` in a loop (which drifts), uses one OS timer per `PeriodicTimer`, and integrates with `CancellationToken`.
- **Token bucket vs leaky bucket.** This is a token-bucket (slots are produced at a fixed rate; bursts are bounded by buffer capacity). A leaky bucket would track consumption rate instead. Both are valid; token bucket is simpler and fits the `Channel<T>` shape cleanly.

Commit: `TokenBucket with Channel<TimeSlot> + PeriodicTimer refill loop`.

### Phase 4 — Demo console (~20 min)

```csharp
// Program.cs (Demo)
using var http = new HttpClient();
await using var bucket = new TokenBucket(requestsPerSecond: 10, burst: 1);
var fetcher = new RateLimitedFetcher(http, bucket);

const int consumers = 8;
const int perConsumer = 25;
const string url = "https://httpbin.org/get";

var sw = Stopwatch.StartNew();
var counts = new int[consumers];

var tasks = Enumerable.Range(0, consumers).Select(id => Task.Run(async () =>
{
    for (var i = 0; i < perConsumer; i++)
    {
        _ = await fetcher.FetchAsync(new Uri(url));
        counts[id]++;
    }
})).ToArray();

await Task.WhenAll(tasks);
sw.Stop();

var total = counts.Sum();
var rate = total / sw.Elapsed.TotalSeconds;

Console.WriteLine($"Total requests:   {total}");
Console.WriteLine($"Elapsed wall:     {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Observed rate:    {rate:F2} req/sec");
Console.WriteLine(rate > 10.5 ? "FAIL — exceeded the budget." : "PASS — under the budget.");
```

Expected output:

```
Total requests:   200
Elapsed wall:     20.13s
Observed rate:    9.94 req/sec
PASS — under the budget.
```

Commit: `Demo: 8-consumer wall-clock test`.

### Phase 5 — `BenchmarkDotNet` harness (~25 min)

```csharp
// Program.cs (Bench)
BenchmarkRunner.Run<FetcherBenchmarks>();

[MemoryDiagnoser]
public class FetcherBenchmarks
{
    private HttpClient _http = null!;
    private TokenBucket _bucket = null!;
    private RateLimitedFetcher _fetcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        _http = new HttpClient();
        _bucket = new TokenBucket(requestsPerSecond: 10, burst: 1);
        _fetcher = new RateLimitedFetcher(_http, _bucket);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _bucket.DisposeAsync();
        _http.Dispose();
    }

    [Benchmark]
    public async Task<string> FetchAsync()
        => await _fetcher.FetchAsync(new Uri("https://httpbin.org/get"));
}
```

Run with `dotnet run -c Release --project src/RateLimitedFetcher.Bench`.

Expected: p50 ~100–110ms (one slot-width plus the HTTP round trip), p95 below the slot width + the round-trip variance.

Note: `BenchmarkDotNet` runs the method on a single thread by design — the actual rate-limit pressure comes from the demo, not the bench. The benchmark measures per-call latency in steady state.

Commit: `BenchmarkDotNet harness for FetchAsync`.

### Phase 6 — Notes and README (~15 min)

`notes/design.md` — one page covering the four design notes from Phase 3.

`notes/measurements.md` — wall-clock output + `BenchmarkDotNet` summary + one paragraph of analysis.

`README.md` — paragraph + clone/build/run + summary table:

| Metric | Value | Pass? |
|--------|------:|:-----:|
| Total requests | 200 | — |
| Wall clock | 20.13s | — |
| Observed rate | 9.94 req/sec | Yes |
| BenchmarkDotNet p50 | 105 ms | Yes (≤ 110ms) |
| BenchmarkDotNet allocations | 1.2 KB/op | — |

Commit: `Notes + README with summary table`.

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 10% | `dotnet build` and `dotnet run` clean on a fresh clone |
| Correctness of the rate limiter | 30% | Wall-clock test under 10 req/sec ± 5%; not a `SemaphoreSlim`; uses `Channel<T>` and `PeriodicTimer` |
| Cancellation hygiene | 15% | `WaitForSlotAsync(ct)` cancels in-flight; `DisposeAsync` cleanly winds the refill loop |
| Benchmark quality | 15% | `BenchmarkDotNet` summary is interpretable; p50/p95/allocations all reported |
| Design notes | 15% | All four design questions answered with one substantive paragraph each |
| README | 15% | A new developer can clone, build, run the demo, and read the design page in under 10 minutes |

## Stretch (optional)

- **Implement the same limiter with `System.Threading.RateLimiting`** (the BCL's purpose-built abstraction since .NET 7) — `FixedWindowRateLimiter` or `TokenBucketRateLimiter`. Note in `design.md` where your hand-rolled version diverges (it almost certainly will, in subtle ways around burst handling).
- **Add a `burst: 5` config** so the limiter allows occasional bursts of 5 followed by a refill pause. Verify the wall-clock test still passes the rate budget.
- **Wire the limiter as a `DelegatingHandler`** so any `HttpClient` registered via `IHttpClientFactory` gets rate limiting for free. (`AddHttpMessageHandler<RateLimitingHandler>()`.) Demonstrate by injecting it into Week 2's Ledger API.
- **Add a per-host budget** — instead of one global limiter, key the limiters by `Uri.Host` so each downstream service has its own budget. Useful for "I crawl 10 sites; each gets its own rate budget."

---

## Resources

- *`System.Threading.Channels` design*: <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>
- *`PeriodicTimer` API reference*: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer>
- *`System.Threading.RateLimiting`*: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.ratelimiting>
- *Token bucket vs leaky bucket — Wikipedia*: <https://en.wikipedia.org/wiki/Token_bucket>
- *`BenchmarkDotNet` Getting Started*: <https://benchmarkdotnet.org/articles/guides/getting-started.html>
