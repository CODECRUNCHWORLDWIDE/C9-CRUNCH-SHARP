# Week 4 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `dotnet/runtime` source is MIT-licensed and public on GitHub. The "Async in depth" series on `devblogs.microsoft.com` is free without registration. No paywalled books are linked.

## Required reading (work it into your week)

- **Asynchronous programming with `async` and `await`** — the canonical Microsoft Learn entry point:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/>
- **Task asynchronous programming model** — the higher-level overview:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/task-asynchronous-programming-model>
- **`async` return types** — `Task`, `Task<T>`, `ValueTask`, `void`, and the new `IAsyncEnumerable<T>`:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-return-types>
- **Generate and consume async streams (`IAsyncEnumerable<T>`)**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream>
- **`System.Threading.Channels`** — the official conceptual guide, with `Bounded` vs `Unbounded` semantics:
  <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>
- **Cancellation in managed threads** — `CancellationToken`, `CancellationTokenSource`, linked sources:
  <https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads>
- **.NET 9 release notes — `Task.WhenEach` and friends** — what changed since .NET 8:
  <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries>

## Authoritative deep dives

- **Stephen Toub — "Understanding the Whys, Whats, and Whens of `ValueTask`"** — the canonical reference; ~40 minutes:
  <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>
- **Stephen Toub — "How Async/Await Really Works in C#"** — the post that walks the state-machine transform:
  <https://devblogs.microsoft.com/dotnet/how-async-await-really-works/>
- **"ConfigureAwait FAQ"** — Stephen Toub's exhaustive answer to "should I be calling `ConfigureAwait(false)`?":
  <https://devblogs.microsoft.com/dotnet/configureawait-faq/>
- **"Cancellation patterns" — the cooperative cancellation guide**:
  <https://learn.microsoft.com/en-us/dotnet/standard/threading/how-to-listen-for-cancellation-requests-by-polling>
- **"Parallel programming patterns: `Parallel.ForEachAsync`"** — the .NET 6+ entry point for parallel-async:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.parallel.foreachasync>
- **"The Task asynchronous programming (TAP) pattern"** — when *your* method should look async:
  <https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap>

## Official .NET docs

- **`Task` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task>
- **`Task<TResult>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task-1>
- **`ValueTask<TResult>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1>
- **`Channel<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channel-1>
- **`CancellationToken` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken>
- **`IAsyncEnumerable<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1>
- **`EnumeratorCancellationAttribute`** — the attribute that wires `WithCancellation` to your generator's parameter:
  <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.enumeratorcancellationattribute>

## Open-source projects to read this week

You learn more from one hour reading well-written async C# than from three hours of tutorials.

- **`dotnet/runtime` — `Task.cs`** — the implementation of `Task` itself; dense, but the comments are gold:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs>
- **`dotnet/runtime` — `Channels`** — the entire `Channels` source is in one folder, ~2,000 lines, MIT-licensed:
  <https://github.com/dotnet/runtime/tree/main/src/libraries/System.Threading.Channels>
- **`dotnet/runtime` — `AsyncTaskMethodBuilder<T>`** — what the compiler actually calls when it lowers `async`:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs>
- **`Microsoft.Extensions.Hosting` — `BackgroundService`** — the canonical pattern for long-running async work:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs>
- **`Polly`** — the production resilience library; its retry/timeout/circuit-breaker policies are all async-token-aware:
  <https://github.com/App-vNext/Polly>

## Community deep-dives

- **Stephen Cleary's blog** — author of "Concurrency in C# Cookbook"; the single best independent async writer:
  <https://blog.stephencleary.com/>
- **David Fowler's tweets and gists** — `dotnet/aspnetcore` architect; the source of many "don't do that" lessons:
  <https://gist.github.com/davidfowl>
- **Marc Gravell's blog** — `Channels`, perf, and Stack Overflow's own concurrency lessons:
  <https://blog.marcgravell.com/>
- **Andrew Lock — "Series: Async in C#"** posts: <https://andrewlock.net/series/async-await/>
- **Nick Chapsas — Async deep dives** on YouTube (community, very clear): <https://www.youtube.com/@nickchapsas>

## Libraries we touch this week

- **`System.Threading.Tasks`** — in the BCL; no package needed. Defines `Task`, `Task<T>`, `ValueTask`, `Parallel`.
- **`System.Threading.Channels`** — in the BCL. Defines `Channel<T>`, `Channel.CreateBounded`, `Channel.CreateUnbounded`.
- **`System.Threading`** — in the BCL. Defines `CancellationToken`, `CancellationTokenSource`, `SemaphoreSlim`, `Lock`.
- **`System.Collections.Generic`** — defines `IAsyncEnumerable<T>` and the `await foreach` consumer pattern.
- **`Microsoft.Extensions.Hosting`** — needed for `BackgroundService` and `IHostedService` in the mini-project.

## Editors

Unchanged from Weeks 1–3.

- **VS Code + C# Dev Kit** (primary): <https://code.visualstudio.com/docs/csharp/get-started>
- **JetBrains Rider Community** (secondary, free for non-commercial): <https://www.jetbrains.com/rider/>
- The new bit this week: **a debugger that can step through async** — VS Code's C# Dev Kit handles async stack traces. Rider's "Parallel Stacks" window is the gold standard if you have it. Either way, set a breakpoint *after* an `await` at least once and read the call stack; you will see the state-machine frames.

## Free books and chapters

- **"Concurrency in C# Cookbook, 2nd Edition" by Stephen Cleary** — chapters 1–4 are the best 80 pages on async in print. The book is paywalled but Cleary's blog covers ~70% of the material free.
- **"Pro .NET Memory Management" by Konrad Kokosa** — the chapter on the thread pool and task scheduling is the deepest free chapter (preview on Apress): a Google search for "Pro .NET Memory Management thread pool preview" will surface it.
- **"Get started with async/await"** — free Microsoft Learn module path:
  <https://learn.microsoft.com/en-us/training/paths/csharp-asynchronous-programming/>

## Videos (free, no signup)

- **"Async in depth" — the .NET YouTube channel's playlist** — five 20-minute videos from Stephen Toub, David Fowler, and Stephen Cleary: <https://www.youtube.com/@dotnet>
- **".NET Conf 2024 — async sessions"** archive: <https://www.youtube.com/playlist?list=PL1rZQsJPBU2StolNg0aqvQswETPcYnNKL>
- **Nick Chapsas — "Stop using `Task.Run` like this"** (community): <https://www.youtube.com/@nickchapsas>

## Tools you'll use this week

- **`dotnet` CLI** — same as before.
- **`dotnet trace`** — the cross-platform .NET tracing tool. Install with `dotnet tool install -g dotnet-trace`. We use it once in Lecture 1 to capture a `Microsoft-Windows-DotNETRuntime` profile and confirm where our `await` resumed.
- **`dotnet counters`** — live perf counters in the terminal. Install with `dotnet tool install -g dotnet-counters`. We watch `ThreadPool Thread Count` and `ThreadPool Queue Length` as our mini-project runs.
- **`PerfView`** (Windows) or **`speedscope`** (cross-platform) — flamegraph viewers if you want to take the trace from `dotnet trace` further. Both free.
- **An `.http` file** — yes, even this week. The crawler exposes a single endpoint that streams `IAsyncEnumerable<CrawlResult>` via Server-Sent Events.

## The spec — when you need to be exact

- **C# 13 language specification** — the `async`/`await` chapter:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-13.0/>
- **ECMA-335 (CLI) async state machine appendix** — what the runtime actually expects:
  <https://www.ecma-international.org/publications-and-standards/standards/ecma-335/>
- **The TAP pattern guidance** — the contract every public async API should follow:
  <https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap>

## Glossary cheat sheet

Keep this open in a tab.

| Term | Plain English |
|------|---------------|
| **`Task`** | A promise of a (`void`) operation. Holds status, result, exception. Returned by `async Task` methods. |
| **`Task<T>`** | A promise of a value of type `T`. Returned by `async Task<T>` methods. |
| **`ValueTask<T>`** | A struct-based promise that avoids allocating a `Task` when the operation completes synchronously. Only `await` once. |
| **`async`** | A compiler keyword that lets you use `await` inside the method. Does not make the method run on another thread. |
| **`await`** | A compiler-generated suspension point. If the awaited operation is incomplete, the rest of the method becomes a continuation. |
| **`ConfigureAwait(false)`** | Tells the awaiter "do not capture the current `SynchronizationContext`." Mandatory in library code; unnecessary in ASP.NET Core 9 app code. |
| **State machine** | The struct the compiler emits per `async` method. Holds hoisted locals, the current state, and the `MoveNext` resumption logic. |
| **`IAsyncStateMachine`** | The interface every emitted state machine implements. Has `MoveNext` and `SetStateMachine`. |
| **`SynchronizationContext`** | A "where to run continuations" abstraction. WPF/WinForms set one; ASP.NET Core 9 does not. |
| **`Task.Run`** | "Schedule this delegate on the thread pool and give me a `Task` for it." Useful to bridge sync to async on a pool thread. |
| **`Task.WhenAll`** | Returns a `Task` that completes when *all* the input tasks complete (or any throw). |
| **`Task.WhenAny`** | Returns a `Task<Task>` that completes when *any* one of the input tasks completes. |
| **`Task.WhenEach`** | .NET 9 addition. Returns an `IAsyncEnumerable<Task<T>>` that yields each task as it completes — the right tool for "process results in completion order." |
| **`IAsyncEnumerable<T>`** | An async sequence you consume with `await foreach`. Pull-based, lazy, supports cancellation via `WithCancellation`. |
| **`[EnumeratorCancellation]`** | The attribute that wires a generator method's `CancellationToken` parameter to the consumer-side `WithCancellation` call. |
| **`Channel<T>`** | A producer/consumer queue. `Reader` reads, `Writer` writes, `Complete` signals "no more". |
| **`Channel.CreateBounded<T>`** | A channel with a fixed capacity. Provides back-pressure: writers wait when the buffer is full. |
| **`Channel.CreateUnbounded<T>`** | A channel with unlimited capacity. Writers never wait; readers can fall arbitrarily behind. Use sparingly. |
| **`BoundedChannelFullMode`** | How a bounded channel reacts when full: `Wait` (block), `DropNewest`/`DropOldest`/`DropWrite` (discard). |
| **`CancellationToken`** | A read-only handle to "should I stop?" Passed into every async API. Polled with `IsCancellationRequested`, enforced with `ThrowIfCancellationRequested`. |
| **`CancellationTokenSource`** | The mutable side of cancellation. Call `Cancel()` to flip the linked token. Disposable. |
| **`CreateLinkedTokenSource`** | Compose two tokens into one: cancellation when *either* fires. |
| **`OperationCanceledException`** | The exception thrown by `ThrowIfCancellationRequested`. Catch only at the boundary that owns the operation. |
| **`TaskCanceledException`** | A `Task`-specific subclass of `OperationCanceledException`. Catch the base class; treat the subclass as a variant. |
| **`async void`** | Async method that returns nothing — exceptions become process-level unhandled exceptions. Use ONLY for event handlers. |
| **Fire-and-forget** | Calling an async method without `await`. Almost always wrong. Use `Task.Run` + `.ContinueWith(log on fault)`, or a hosted `BackgroundService`. |
| **Back-pressure** | "Don't produce faster than the consumer can consume." Bounded channels provide it; unbounded channels do not. |
| **Thread-pool starvation** | When every pool thread is blocked on a sync wait, leaving no thread to run the work that would unblock them. Symptom of `Task.Result` abuse. |

---

*If a link 404s, please open an issue so we can replace it.*
