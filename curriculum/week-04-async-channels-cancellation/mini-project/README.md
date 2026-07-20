# Mini-Project — Typed Async Crawler

> Build a typed async web crawler that takes one or more seed URLs, fetches each page over HTTP, parses out the links, and streams a typed `CrawlResult` for every page visited — with a bounded `Channel<Uri>` for the work queue, a configurable parallelism cap on the consumers, `IAsyncEnumerable<CrawlResult>` for the output, and a `CancellationToken` that genuinely cancels in under 50 ms when the user presses Ctrl-C. By the end you have a service whose throughput is bounded by your configured parallelism, whose memory is bounded by the channel capacity, and whose shutdown latency is bounded by the slowest in-flight HTTP request.

This mini-project is the canonical async pipeline pattern in .NET. Production-grade variants of this show up as data ingestion pipelines, log aggregators, scrapers, indexers, and any service that has "input → fan-out → fan-in → output" as its shape. The crawler you build here is the substrate of every such pattern; only the work changes.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A console + library combo called `Crawler` that exposes the crawl over a single API entry-point:

```csharp
public interface ICrawler
{
    IAsyncEnumerable<CrawlResult> CrawlAsync(
        IEnumerable<Uri> seeds,
        CrawlOptions options,
        [EnumeratorCancellation] CancellationToken ct = default);
}

public sealed record CrawlOptions(
    int MaxParallelism = 4,
    int MaxPagesPerHost = 100,
    TimeSpan PerRequestTimeout = default,
    HashSet<string>? AllowedHosts = null);

public sealed record CrawlResult(
    Uri Url,
    int StatusCode,
    int BytesFetched,
    int LinksFound,
    TimeSpan Duration,
    string? ErrorMessage = null);
```

And a console host that wires it up:

```
$ dotnet run --project src/Crawler.App -- \
    --seed https://example.com \
    --seed https://example.org \
    --max-parallelism 8 \
    --max-pages-per-host 200 \
    --per-request-timeout 00:00:10 \
    --output ndjson

{"Url":"https://example.com","StatusCode":200,"BytesFetched":1256,"LinksFound":1,"Duration":"00:00:00.234"}
{"Url":"https://example.org","StatusCode":200,"BytesFetched":1256,"LinksFound":1,"Duration":"00:00:00.198"}
...
```

The service must:

- Accept seed URLs and a `CrawlOptions`; return `IAsyncEnumerable<CrawlResult>`.
- Use a **bounded `Channel<Uri>`** for the work queue, with capacity = `MaxParallelism * 4`. New URLs discovered on each page are enqueued; the channel's `BoundedChannelFullMode.Wait` policy provides natural back-pressure when the parser fans out faster than the fetcher can consume.
- Run **`MaxParallelism` concurrent consumer tasks**, each pulling URLs from the channel, fetching the page with `HttpClient.GetAsync`, parsing out links, and writing the page's `CrawlResult` to a *result* channel that feeds the `IAsyncEnumerable<CrawlResult>` back to the caller.
- Cap per-host visits with a `ConcurrentDictionary<string, int>` so a hostile / deeply-linked site cannot dominate the budget.
- Compose a per-request timeout (default 10 seconds) into the caller's `CancellationToken` via `CreateLinkedTokenSource` + `CancelAfter`.
- Shut down cleanly on Ctrl-C **in under 50 ms** — verified by a wall-clock test in the integration suite.
- Be coverable by an xUnit suite of at least **25 passing tests** against a `TestHttpMessageHandler` that returns deterministic pages.

By the end you'll have a public GitHub repo of ~600 lines of C# (excluding tests) that compiles clean, crawls real sites at the configured rate, streams `CrawlResult` over `IAsyncEnumerable<T>`, and shuts down in under 50 ms when you press Ctrl-C.

---

## Rules

- **You may** read Microsoft Learn, the `dotnet/runtime` source, lecture notes, and the source of the libraries listed below.
- **You may NOT** depend on any third-party NuGet package other than:
  - `System.Threading.Channels` (in the BCL; not a separate package since .NET 6).
  - `Microsoft.Extensions.Hosting` (for the console host pattern; optional).
  - `HtmlAgilityPack` (for parsing HTML — the *only* third-party dependency you should need).
  - `Microsoft.AspNetCore.Mvc.Testing` (in the test project, only if you wire an HTTP endpoint stretch goal).
  - `xUnit` and `Microsoft.NET.Test.Sdk`.
- **No Polly. No System.Reactive. No System.Threading.Tasks.Dataflow.** Channels handle the pipeline. `CancellationToken` handles cancellation. `HtmlAgilityPack` handles parsing. Everything else is in the BCL.
- Target framework: `net9.0`. C# language version: the default for the SDK (`13.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.

---

## Acceptance criteria

- [ ] A new public GitHub repo named `c9-week-04-async-crawler-<yourhandle>`.
- [ ] Solution layout:
  ```
  Crawler/
  ├── Crawler.sln
  ├── .gitignore
  ├── Directory.Build.props
  ├── src/
  │   ├── Crawler.Core/
  │   │   ├── Crawler.Core.csproj
  │   │   ├── ICrawler.cs
  │   │   ├── ICrawlPipeline.cs
  │   │   ├── IFetcher.cs
  │   │   ├── IParser.cs
  │   │   ├── CrawlOptions.cs
  │   │   ├── CrawlResult.cs
  │   │   ├── Crawler.cs
  │   │   ├── ChannelCrawlPipeline.cs
  │   │   ├── HttpFetcher.cs
  │   │   └── HtmlLinkParser.cs
  │   └── Crawler.App/
  │       ├── Crawler.App.csproj
  │       ├── Program.cs
  │       └── ConsoleOptions.cs
  └── tests/
      └── Crawler.Tests/
          ├── Crawler.Tests.csproj
          ├── TestHttpMessageHandler.cs
          ├── CrawlerTests.cs
          ├── PipelineTests.cs
          ├── CancellationTests.cs
          └── ConsoleAppTests.cs
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports **at least 25** passing tests covering happy paths, cancellation paths, timeout paths, and edge cases (cycles, deep nesting, malformed HTML, redirect chains).
- [ ] `dotnet run --project src/Crawler.App -- --seed https://example.com --max-parallelism 4` streams `CrawlResult` lines to stdout as NDJSON, one per page.
- [ ] **Pressing Ctrl-C while the crawler is mid-flight terminates the program within 50 ms.** This is the headline non-functional requirement; the integration test enforces it with a `Stopwatch`.
- [ ] The `Channel<Uri>` work queue uses `Channel.CreateBounded<Uri>` with `BoundedChannelFullMode.Wait`. The capacity is `options.MaxParallelism * 4`.
- [ ] The output is delivered as `IAsyncEnumerable<CrawlResult>`. The console app consumes it with `await foreach`; tests consume it the same way.
- [ ] Every async method on `ICrawler`, `IFetcher`, `IParser`, and `ICrawlPipeline` takes a `CancellationToken` as its last parameter.
- [ ] `HttpFetcher` accepts an `HttpClient` via constructor (no `new HttpClient()` inside) — the `HttpClient` is shared across the crawl.
- [ ] No `async void`. No `Task.Result`. No `Task.Wait()`. No fire-and-forget `Task` outside of internally-tracked tasks (which must be awaited or `Task.WhenAll`-ed in `DisposeAsync`).
- [ ] `OperationCanceledException` is caught at exactly one place: `Program.Main`'s outermost `try`.
- [ ] `README.md` in the repo root includes:
  - One paragraph describing the project.
  - The exact commands to clone, build, test, and run with a small seed list.
  - The Ctrl-C "shuts down in under 50 ms" claim, with the test name that proves it.
  - A "Things I learned" section with at least 4 specific items about async, channels, or cancellation in .NET 9.

---

## Suggested order of operations

You'll find it easier if you build incrementally rather than trying to write the whole thing at once. The phases below mirror the suggested-schedule split: Thursday on the pipeline, Friday on the console and a real crawl, Saturday on tests and polish.

### Phase 1 — Skeleton (~30 min)

```bash
mkdir Crawler && cd Crawler
dotnet new sln -n Crawler
dotnet new gitignore && git init

dotnet new classlib -n Crawler.Core  -o src/Crawler.Core
dotnet new console  -n Crawler.App   -o src/Crawler.App
dotnet new xunit    -n Crawler.Tests -o tests/Crawler.Tests

dotnet sln add src/Crawler.Core/Crawler.Core.csproj
dotnet sln add src/Crawler.App/Crawler.App.csproj
dotnet sln add tests/Crawler.Tests/Crawler.Tests.csproj

dotnet add src/Crawler.Core package HtmlAgilityPack
dotnet add src/Crawler.App  reference src/Crawler.Core/Crawler.Core.csproj
dotnet add tests/Crawler.Tests reference src/Crawler.Core/Crawler.Core.csproj
```

Add a `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Commit: `Skeleton: three projects + Directory.Build.props`.

### Phase 2 — Contracts and DTOs (~30 min)

```csharp
// ICrawler.cs
public interface ICrawler
{
    IAsyncEnumerable<CrawlResult> CrawlAsync(
        IEnumerable<Uri> seeds,
        CrawlOptions options,
        CancellationToken ct = default);
}

// CrawlOptions.cs
public sealed record CrawlOptions
{
    public int MaxParallelism { get; init; } = 4;
    public int MaxPagesPerHost { get; init; } = 100;
    public TimeSpan PerRequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public HashSet<string>? AllowedHosts { get; init; }
}

// CrawlResult.cs
public sealed record CrawlResult(
    Uri Url,
    int StatusCode,
    int BytesFetched,
    int LinksFound,
    TimeSpan Duration,
    string? ErrorMessage = null);

// IFetcher.cs
public interface IFetcher
{
    ValueTask<(int StatusCode, byte[] Body)> FetchAsync(Uri uri, CancellationToken ct);
}

// IParser.cs
public interface IParser
{
    IEnumerable<Uri> ParseLinks(Uri baseUri, ReadOnlySpan<byte> body);
}
```

Commit: `Contracts: ICrawler, CrawlOptions, CrawlResult, IFetcher, IParser`.

### Phase 3 — `HttpFetcher` and `HtmlLinkParser` (~45 min)

```csharp
public sealed class HttpFetcher(HttpClient http) : IFetcher
{
    public async ValueTask<(int StatusCode, byte[] Body)> FetchAsync(Uri uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        return ((int)response.StatusCode, body);
    }
}

public sealed class HtmlLinkParser : IParser
{
    public IEnumerable<Uri> ParseLinks(Uri baseUri, ReadOnlySpan<byte> body)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.Load(new MemoryStream(body.ToArray()));

        var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
        if (nodes is null) yield break;

        foreach (var n in nodes)
        {
            var href = n.GetAttributeValue("href", "");
            if (Uri.TryCreate(baseUri, href, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https"))
                yield return abs;
        }
    }
}
```

Commit: `Fetcher and parser implementations`.

### Phase 4 — `ChannelCrawlPipeline` (the core of the project, ~90 min)

This is the heart of the project. Read it carefully before you write your own.

```csharp
public sealed class ChannelCrawlPipeline(IFetcher fetcher, IParser parser) : ICrawlPipeline
{
    public async IAsyncEnumerable<CrawlResult> RunAsync(
        IEnumerable<Uri> seeds,
        CrawlOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Two channels: one for work, one for results.
        var workChannel = Channel.CreateBounded<Uri>(new BoundedChannelOptions(options.MaxParallelism * 4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        var resultChannel = Channel.CreateBounded<CrawlResult>(new BoundedChannelOptions(options.MaxParallelism * 4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        var seen      = new ConcurrentDictionary<Uri, byte>();
        var perHost   = new ConcurrentDictionary<string, int>();
        var inFlight  = 0;

        // Seed the work channel.
        foreach (var seed in seeds)
        {
            if (seen.TryAdd(seed, 0))
                await workChannel.Writer.WriteAsync(seed, ct);
        }

        // Start MaxParallelism consumer tasks. Each one fetches a URL,
        // parses links, enqueues new URLs, and emits a CrawlResult.
        var consumers = Enumerable.Range(0, options.MaxParallelism).Select(_ => Task.Run(async () =>
        {
            await foreach (var url in workChannel.Reader.ReadAllAsync(ct))
            {
                Interlocked.Increment(ref inFlight);
                try
                {
                    var result = await ProcessOneAsync(url, options, seen, perHost, workChannel.Writer, ct);
                    await resultChannel.Writer.WriteAsync(result, ct);
                }
                finally
                {
                    if (Interlocked.Decrement(ref inFlight) == 0 && workChannel.Reader.Count == 0)
                    {
                        workChannel.Writer.TryComplete();
                    }
                }
            }
        }, ct)).ToArray();

        // When all consumers finish, complete the result channel.
        _ = Task.WhenAll(consumers).ContinueWith(_ => resultChannel.Writer.Complete(), ct);

        // Stream results back to the caller.
        await foreach (var r in resultChannel.Reader.ReadAllAsync(ct))
            yield return r;
    }

    private async Task<CrawlResult> ProcessOneAsync(
        Uri url,
        CrawlOptions options,
        ConcurrentDictionary<Uri, byte> seen,
        ConcurrentDictionary<string, int> perHost,
        ChannelWriter<Uri> work,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(options.PerRequestTimeout);

            var (status, body) = await fetcher.FetchAsync(url, attemptCts.Token);

            var linkCount = 0;
            foreach (var link in parser.ParseLinks(url, body))
            {
                if (options.AllowedHosts is not null && !options.AllowedHosts.Contains(link.Host)) continue;
                var hostHits = perHost.AddOrUpdate(link.Host, 1, (_, n) => n + 1);
                if (hostHits > options.MaxPagesPerHost) continue;
                if (seen.TryAdd(link, 0))
                {
                    linkCount++;
                    await work.WriteAsync(link, ct);
                }
            }

            return new CrawlResult(url, status, body.Length, linkCount, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CrawlResult(url, 0, 0, 0, sw.Elapsed, "Per-request timeout");
        }
        catch (Exception ex)
        {
            return new CrawlResult(url, 0, 0, 0, sw.Elapsed, ex.Message);
        }
    }
}
```

Read this code carefully and **understand every line** before pasting it. The two tricky bits:

- **`workChannel.Writer.TryComplete()` inside the `finally`** — this is the "we found a quiescent moment" signal. When `inFlight` drops to zero AND the channel has no queued items, we know no new URLs will arrive; closing the writer lets all consumers' `await foreach`es exit. `TryComplete` is idempotent — calling it multiple times is safe.
- **`catch (OperationCanceledException) when (!ct.IsCancellationRequested)`** — this distinguishes "the *outer* token cancelled" (let it propagate; the caller wanted to stop) from "*our* per-request timeout fired" (catch and return a `CrawlResult` with the timeout message).

Commit: `ChannelCrawlPipeline: the core fan-out/fan-in pipeline`.

### Phase 5 — The `Crawler` facade (~30 min)

```csharp
public sealed class Crawler(IFetcher fetcher, IParser parser) : ICrawler
{
    public IAsyncEnumerable<CrawlResult> CrawlAsync(
        IEnumerable<Uri> seeds,
        CrawlOptions options,
        CancellationToken ct = default)
        => new ChannelCrawlPipeline(fetcher, parser).RunAsync(seeds, options, ct);
}
```

Commit: `Crawler facade`.

### Phase 6 — Console app + Ctrl-C handling (~60 min)

```csharp
// Program.cs
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
ICrawler crawler = new Crawler(new HttpFetcher(http), new HtmlLinkParser());

var options = ConsoleOptions.Parse(args);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await foreach (var r in crawler.CrawlAsync(options.Seeds, options.CrawlOptions, cts.Token))
    {
        Console.WriteLine(JsonSerializer.Serialize(r));
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
}
```

The `Console.CancelKeyPress` handler cancels the CTS. The `await foreach` exits when the pipeline observes the cancellation. The `catch ... when` filter swallows the resulting `OperationCanceledException` exactly once, at the top of the program.

Commit: `Console app with Ctrl-C handling`.

### Phase 7 — Integration tests (~120 min)

Build a `TestHttpMessageHandler` that returns deterministic HTML pages:

```csharp
public sealed class TestHttpMessageHandler(Dictionary<Uri, (int Status, string Html)> pages) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (pages.TryGetValue(request.RequestUri!, out var p))
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)p.Status)
            {
                Content = new StringContent(p.Html, Encoding.UTF8, "text/html"),
            });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
```

Then write at least **25 tests** across the four test files. Suggested coverage:

- `CrawlerTests`: 10 tests — single seed, multiple seeds, cycle detection, `AllowedHosts` filter, `MaxPagesPerHost` cap, malformed HTML, deep nesting (5-level chain), 404s, 500s, redirects.
- `PipelineTests`: 6 tests — channel capacity respected (no more than `MaxParallelism * 4` queued), parallelism cap respected (no more than `MaxParallelism` in flight simultaneously, verified with a counter), `Channel.Writer.Complete` actually gets called, no unobserved tasks at the end.
- `CancellationTests`: 6 tests — Ctrl-C from a CTS terminates within 50 ms; per-request timeout fires; caller cancellation distinguishes from timeout in the result; mid-stream cancellation produces a partial result list; `IAsyncEnumerable` exits cleanly when consumer disposes the enumerator.
- `ConsoleAppTests`: 3 tests — parse args, NDJSON output format, exit code on cancellation.

Commit per file as you write each cluster.

### Phase 8 — Polish + README (~30 min)

Run `dotnet format`. Run `dotnet publish` to verify the publish path. Write the root `README.md`:

```markdown
# Crawler

A typed async web crawler built on `System.Threading.Channels` + `IAsyncEnumerable<T>` + `CancellationToken`.

## Run

    dotnet run --project src/Crawler.App -- \
        --seed https://example.com \
        --max-parallelism 4

## Test

    dotnet test

The headline non-functional requirement: pressing Ctrl-C during a crawl shuts the process down in under 50 ms. Proven by `CancellationTests.CtrlC_Stops_The_Crawl_Within_50ms`.

## Things I learned

1. ...
2. ...
3. ...
4. ...
```

Commit: `Polish: README and dotnet format`.

---

## Example expected output

```json
{"Url":"https://example.com","StatusCode":200,"BytesFetched":1256,"LinksFound":1,"Duration":"00:00:00.234","ErrorMessage":null}
{"Url":"https://www.iana.org/domains/example","StatusCode":200,"BytesFetched":4823,"LinksFound":0,"Duration":"00:00:00.198","ErrorMessage":null}
```

```text
$ time dotnet run --project src/Crawler.App -- --seed https://example.com --max-parallelism 2
{"Url":"https://example.com",...}
^C
Cancelled.

real    0m1.243s
user    0m0.187s
sys     0m0.031s
```

(The shutdown latency is measured *inside* the integration test, against `DateTimeOffset.UtcNow`. Outside the test, the `^C → "Cancelled."` print should appear within one or two video frames.)

---

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 10% | `dotnet build`, `dotnet test`, and `dotnet run --project src/Crawler.App` all clean on a fresh clone |
| Pipeline correctness | 25% | The `Channel<Uri>` work queue is bounded and uses `Wait`; the result channel is bounded; both writers are completed correctly; no items are dropped or duplicated |
| Cancellation latency | 20% | Ctrl-C shuts down within 50 ms; the integration test for this enforces it |
| Streaming output | 15% | `IAsyncEnumerable<CrawlResult>` flows results lazily; `await foreach` in the console app emits one line at a time; no buffering surprises |
| Error handling | 10% | Per-request timeout converts to a `CrawlResult.ErrorMessage`; caller cancellation propagates without being caught mid-stack |
| Tests | 15% | At least 25 tests; cover happy paths, cycles, cancellation, timeout, malformed HTML, edge cases |
| README + Ctrl-C demo | 5% | A new developer can clone, build, test, and exercise the crawler in under 10 minutes |

---

## Stretch (optional)

- **Add a `BackgroundService`-hosted variant** that consumes from a `Channel<Uri>` exposed via an ASP.NET Core endpoint (`POST /api/v1/seed`), letting a long-running crawl be fed URLs over HTTP. Requires `Microsoft.Extensions.Hosting`.
- **Rate-limit the fetcher** with the `RateLimitedFetcher` from Challenge 1. Wire it as a decorator over `HttpFetcher` registered via `IServiceCollection.AddTransient<IFetcher>(...)` so the policy is composable.
- **Add a metrics endpoint** at `/metrics` exposing in-flight count, processed count, error count, and average duration as Prometheus-formatted plaintext. (`System.Diagnostics.Metrics` — built into .NET 9.)
- **Swap the JSON output for Server-Sent Events** so a browser can stream results live from `EventSource` in JavaScript.
- **Make the parallelism dynamic** — accept signals (e.g. through a separate `Channel<int>` of "new parallelism level") that scale consumers up and down on the fly. The trick is starting/stopping consumer tasks without dropping in-flight URLs.

---

## What this prepares you for

- **Week 5** introduces OOP, SOLID, and DI. Your `ICrawler`, `IFetcher`, `IParser`, `ICrawlPipeline` interfaces are textbook interface segregation; Week 5 turns them into a DI graph validated at startup with `ValidateOnBuild = true`.
- **Week 8** introduces `BackgroundService`. Your `ChannelCrawlPipeline` is structurally a `BackgroundService` minus the host integration; Week 8's mini-project plugs it into an ASP.NET Core 9 service that streams the crawl over Server-Sent Events.
- **The capstone** (Week 15+) revisits the channel pattern as a generic ETL pipeline; the lessons compound.

---

## Resources

- *`System.Threading.Channels` design*: <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>
- *`IAsyncEnumerable<T>` reference*: <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1>
- *`HtmlAgilityPack` docs*: <https://html-agility-pack.net/>
- *`HttpClient` best practices*: <https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines>
- *Cancellation guide*: <https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads>

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes the setup commands, the Ctrl-C latency claim with the test name, and 4 "things I learned" specific to async/channels/cancellation.
3. Make sure `dotnet build`, `dotnet test`, and `dotnet run --project src/Crawler.App` all green on a fresh clone.
4. Post the repo URL in your cohort tracker. You shipped a real async pipeline; show it.
