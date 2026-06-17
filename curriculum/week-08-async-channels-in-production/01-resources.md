# Week 8 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `dotnet/runtime` source is MIT-licensed and public on GitHub. Stephen Toub's Parallel Programming and Channels blog posts are on the Microsoft DevBlogs at no cost. The `dotnet-counters` tool is a free first-party diagnostic. No paywalled material is linked.

## Required reading (work it into your week)

- **Microsoft Learn — Asynchronous programming with async and await**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/>
- **Microsoft Learn — Task asynchronous programming model**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/task-asynchronous-programming-model>
- **Microsoft Learn — ConfigureAwait FAQ (Stephen Toub, hosted on Learn)**:
  <https://devblogs.microsoft.com/dotnet/configureawait-faq/>
- **Microsoft Learn — Cancellation in managed threads**:
  <https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads>
- **Microsoft Learn — Generate and consume async streams (`IAsyncEnumerable<T>`)**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream>
- **Microsoft Learn — `EnumeratorCancellationAttribute`**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.enumeratorcancellationattribute>
- **Microsoft Learn — `System.Threading.Channels` overview**:
  <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>
- **Microsoft Learn — `Channel.CreateBounded<T>`**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channel.createbounded>
- **Microsoft Learn — `BoundedChannelFullMode` enum**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.boundedchannelfullmode>
- **Microsoft Learn — `Parallel.ForEachAsync`**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync>
- **Microsoft Learn — `dotnet-counters` diagnostic tool**:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters>
- **Microsoft Learn — Well-known event counters in .NET**:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/available-counters>
- **Microsoft Learn — `System.Diagnostics.Metrics` API**:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>
- **Microsoft Learn — ThreadPool — `SetMinThreads`**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.setminthreads>

## Authoritative deep dives (Stephen Toub on the .NET DevBlogs)

Stephen Toub is the .NET runtime architect for everything async; the eight posts below are the canonical references for this week's material. Every senior .NET engineer has read all of them at least once.

- **"ConfigureAwait FAQ" (2019)** — the long-form treatment of `.ConfigureAwait(false)`; required reading:
  <https://devblogs.microsoft.com/dotnet/configureawait-faq/>
- **"Async ValueTask Pooling in .NET 5"** — explains the design and the consume-once rule:
  <https://devblogs.microsoft.com/dotnet/async-valuetask-pooling-in-net-5/>
- **"Understanding the Whys, Whats, and Whens of ValueTask"** — first introduced in Week 7; re-read for this week's audit problems:
  <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>
- **"An Introduction to System.Threading.Channels"** — the canonical Channels deep dive; required reading:
  <https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/>
- **"How Async/Await Really Works in C#"** — the 2023 post that walks through the compiler-generated state machine line by line:
  <https://devblogs.microsoft.com/dotnet/how-async-await-really-works/>
- **"Performance Improvements in .NET 8" — the async and threading sections**:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/>
- **"Performance Improvements in .NET 7" — the async section**:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-7/>
- **"Async/await on the I/O thread"** (.NET 6 thread-pool changes):
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-6/#threading>

## Authoritative deep dives (other authors)

- **David Fowler — `AspNetCoreDiagnosticScenarios` Gist** — the "what bugs to avoid in production ASP.NET Core" reference, including the deadlock and starvation case studies:
  <https://github.com/davidfowl/AspNetCoreDiagnosticScenarios>
- **David Fowler — `AspNetCoreDiagnosticScenarios/AsyncGuidance.md`** — the dedicated async chapter; required reading:
  <https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md>
- **David Fowler — `AspNetCoreDiagnosticScenarios/ThreadPoolStarvation.md`** — the canonical worked example of ThreadPool starvation:
  <https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/Diagnostics.md>
- **Stephen Cleary — "Don't Block on Async Code"** — the original 2012 post that named the sync-over-async deadlock:
  <https://blog.stephencleary.com/2012/07/dont-block-on-async-code.html>
- **Stephen Cleary — "Cancellation, Part 1: Overview"** — companion reading for the cancellation lecture:
  <https://blog.stephencleary.com/2022/02/cancellation-1-overview.html>
- **Stephen Cleary — "Async Streams"** — the `IAsyncEnumerable<T>` walk-through:
  <https://blog.stephencleary.com/2021/03/async-streams.html>
- **Maoni Stephens — "Server GC" deep dive** (Maoni is the .NET GC architect; the ThreadPool starvation diagnostic frequently surfaces GC interaction):
  <https://devblogs.microsoft.com/dotnet/server-gc-and-workstation-gc/>

## Official .NET docs (API references)

- **`Task<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task-1>
- **`ValueTask<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1>
- **`CancellationToken` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken>
- **`CancellationTokenSource` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource>
- **`SynchronizationContext` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.synchronizationcontext>
- **`ConfiguredTaskAwaitable` and `ConfigureAwaitOptions` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions>
- **`IAsyncEnumerable<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1>
- **`IAsyncEnumerator<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerator-1>
- **`Channel<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channel-1>
- **`ChannelReader<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channelreader-1>
- **`ChannelWriter<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channelwriter-1>
- **`BoundedChannelOptions` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.boundedchanneloptions>
- **`System.Diagnostics.Metrics.Meter` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.meter>
- **`System.Diagnostics.Metrics.Counter<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.counter-1>
- **`System.Diagnostics.Metrics.Histogram<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.metrics.histogram-1>
- **`ThreadPool` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool>

## `dotnet/runtime` source — go read these files

The runtime is MIT-licensed and source-link works. When a lecture says "the `Channel.CreateBounded` source is 200 lines, go read it," it means literally that — open the link, scroll through it, return.

- **`System.Threading.Channels` — `BoundedChannel<T>` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Channels/src/System/Threading/Channels/BoundedChannel.cs>
- **`System.Threading.Channels` — `UnboundedChannel<T>` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Channels/src/System/Threading/Channels/UnboundedChannel.cs>
- **`System.Threading.Channels` — `ChannelReader<T>` base class**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Channels/src/System/Threading/Channels/ChannelReader.cs>
- **`Task<T>` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs>
- **`ValueTask<T>` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ValueTask.cs>
- **`SynchronizationContext` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/SynchronizationContext.cs>
- **`AsyncTaskMethodBuilder<TResult>` source — what the compiler generates around every `async` method**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs>
- **`ConfiguredTaskAwaitable<TResult>` source — what `.ConfigureAwait(false)` returns**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/ConfiguredTaskAwaitable.cs>
- **`Parallel.ForEachAsync` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Threading.Tasks.Parallel/src/System/Threading/Tasks/Parallel.ForEachAsync.cs>
- **`ThreadPool` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPool.cs>

## Selected `dotnet/runtime` issues and PRs to skim

These are closed issues and PRs that contain extended design discussion. Each is a self-contained case study in how the .NET team reasons about async correctness.

- **The Channels NuGet design discussion** (the original 2017 issue that produced `System.Threading.Channels`):
  <https://github.com/dotnet/runtime/issues/27545>
- **`Parallel.ForEachAsync` design proposal**:
  <https://github.com/dotnet/runtime/issues/1946>
- **The .NET 8 `ConfigureAwaitOptions` design**:
  <https://github.com/dotnet/runtime/issues/22144>
- **`IAsyncEnumerable<T>.ConfigureAwait` design**:
  <https://github.com/dotnet/runtime/issues/30450>
- **The `Channel.Reader.ReadAllAsync(CancellationToken)` addition** (the API we use in lecture 03):
  <https://github.com/dotnet/runtime/issues/761>
- **ThreadPool worker-thread injection design**:
  <https://github.com/dotnet/runtime/issues/29030>

## Diagnostic tools (all free, first-party)

- **`dotnet-counters`** — live event-counter monitor:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters>
- **`dotnet-trace`** — collects an ETW/`EventPipe` trace from a running process:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace>
- **`dotnet-dump`** — captures and analyses managed dumps:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump>
- **`dotnet-stack`** — prints managed stacks for every thread in a running process:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-stack>
- **PerfView** (Windows; ETW-based trace analyser; still the gold standard for deep allocation traces):
  <https://github.com/microsoft/perfview>

## Talks worth watching (all free, no account)

- **Stephen Toub — "Deep Dive Async" (.NET Conf)** — 90 minutes through `async`, `Task`, `ValueTask`, `IAsyncEnumerable`. Required watching:
  search YouTube for "Stephen Toub deep dive async".
- **David Fowler — "Diagnosing performance problems in ASP.NET Core" (.NET Conf)** — includes the canonical ThreadPool-starvation walk-through:
  search YouTube for "David Fowler ASP.NET Core diagnostics".
- **Stephen Toub — "Channels in .NET" (.NET Conf)**:
  search YouTube for "Stephen Toub channels .NET".
- **Maoni Stephens — "ThreadPool internals"**:
  search YouTube for "Maoni Stephens ThreadPool".

## How to use this resource list

The lectures cite specific URLs from this page at decision points. When a lecture says "see Microsoft Learn's `IAsyncEnumerable` overview," you can find the URL above. The links you should read end-to-end this week are:

1. **Stephen Toub — "ConfigureAwait FAQ"**. Foundational; do not skip. Plan for 45 minutes.
2. **Stephen Toub — "An Introduction to System.Threading.Channels"**. Foundational; do not skip. Plan for 60 minutes.
3. **Microsoft Learn — "Generate and consume async streams"**. Plan for 30 minutes; the examples are short.
4. **David Fowler — `AspNetCoreDiagnosticScenarios/AsyncGuidance.md`**. The single best "what not to do" reference in the .NET ecosystem. Plan for 45 minutes.

The rest are reference material. Bookmark and return to them when a specific question arises.

---

*Bookmarks decay. If a link rots, search the title — these are all canonical pieces and they reappear on the same authors' new homes.*
