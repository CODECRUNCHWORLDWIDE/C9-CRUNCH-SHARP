# Week 4 — Async, Channels, and Cancellation

Welcome to **C9 · Crunch Sharp**, Week 4. Week 1 made the language ordinary. Week 2 put it behind an HTTP port. Week 3 put it on top of a real database. This week makes it *concurrent* — not in a "spawn threads and pray" sense, but in the disciplined, .NET-9 sense: `Task<T>` and the state machine `async`/`await` lowers it into, `ValueTask` and `IAsyncEnumerable<T>` for hot allocations and pull-based streams, `System.Threading.Channels` for producer/consumer pipelines with explicit back-pressure, and `CancellationToken` as a first-class parameter on every async API you write. By Friday you should be able to read the IL the C# compiler generates for an `await`, explain exactly why `Task.Result` deadlocks in a UI but not in ASP.NET Core 9, write a bounded `Channel<T>` pipeline that respects a `CancellationToken`, and ship a typed async crawler that streams results back through `IAsyncEnumerable<T>` while honouring `Ctrl-C` in under 50 ms.

This is the bridge between Phase 1's foundations and Phase 2's production patterns. Microsoft Learn's "C# tour" mentions `async` in passing. The official "Asynchronous programming with async and await" article runs ten pages and stops at `await Task.Delay`. Neither prepares you for the question that actually matters in a code review: *what is the difference between `Task`, `ValueTask`, and `IAsyncEnumerable<T>`, and which one are you reaching for and why?* This week gives you a defensible answer to that question for every async API you write from here on.

The first thing to internalize is that **`async` is a compiler transform, not a runtime feature**. There is no "async thread" and there is no "async magic." When you write `async Task<int> FooAsync()`, the C# compiler rewrites the method body into a state machine — a struct that implements `IAsyncStateMachine`, hoists every local variable into fields, splits the method at every `await` into a numbered state, and registers a continuation that resumes the state machine when the awaited operation completes. The CLR contributes nothing async-specific at runtime; what it contributes is the thread pool, the `SynchronizationContext`, and the `TaskScheduler`. Master the compiler transform and the runtime cooperation, and the rest of async — `ConfigureAwait`, `ValueTask`, `IAsyncEnumerable<T>`, `Channel<T>` — is configuration on top.

The second thing to internalize is that **cancellation is not optional**. In .NET 9, every async API in `System.*` accepts a `CancellationToken`. Every EF Core query accepts one. Every `HttpClient` call accepts one. ASP.NET Core 9 hands you the request's `HttpContext.RequestAborted` token automatically. The discipline is to *pass it through* — not to swallow it on a `try`/`catch (OperationCanceledException)` ten frames down — so the chain from "user pressed Cancel" to "the in-flight `SELECT` is rolled back" stays a single straight line. We will spend Lecture 2 making sure your APIs accept `CancellationToken` everywhere, that you call `ThrowIfCancellationRequested` at every yield point, and that you handle `OperationCanceledException` at exactly one place — the outermost frame that owns the operation.

## Learning objectives

By the end of this week, you will be able to:

- **Explain** what the C# 13 compiler generates for an `async Task<T>` method by reading the lowered IL: the state machine struct, the `MoveNext` switch, the awaiter pattern, and where the continuation actually lives.
- **Distinguish** `Task`, `Task<T>`, `ValueTask<T>`, and `IAsyncEnumerable<T>` — and pick the right return type for a given API based on allocation cost, hot-path behaviour, and consumer shape.
- **Apply** `ConfigureAwait(false)` correctly in library code, and explain why ASP.NET Core 9 no longer needs it on the request path.
- **Diagnose** the three classic async deadlocks — `Task.Result`/`Task.Wait` on a captured context, `async void` swallowing exceptions, fire-and-forget without `await` — and rewrite each to the right pattern.
- **Design** a producer/consumer pipeline with `System.Threading.Channels` choosing between `Channel.CreateBounded<T>` and `Channel.CreateUnbounded<T>` based on a back-pressure requirement.
- **Wire** `CancellationToken` from the outermost frame (e.g. `HttpContext.RequestAborted` or `Console.CancelKeyPress`) down to every `await` and every `Channel.Reader.ReadAsync(ct)` call.
- **Use** `CancellationTokenSource.CreateLinkedTokenSource` and the `CancellationTokenSource(TimeSpan)` constructor to compose timeouts with caller-supplied cancellation.
- **Stream** results lazily with `IAsyncEnumerable<T>` and `await foreach`, applying `WithCancellation(ct)` at the consumer side and `[EnumeratorCancellation]` at the producer side.
- **Recognize** the three flavours of "fire-and-forget" — the wrong one (`async void`), the lazy one (un-awaited `Task`), and the right one (`Task.Run` + an exception observer or a hosted `BackgroundService`).
- **Reason** about thread-pool starvation, the `SynchronizationContext`, and the difference between `Task.Yield()`, `Task.Delay(0)`, and `await Task.CompletedTask`.

## Prerequisites

- **Weeks 1, 2, and 3** of C9 complete: you can scaffold a multi-project solution from the `dotnet` CLI, write Minimal API endpoints with `TypedResults`, register services with the right lifetime, model an EF Core `DbContext`, and your `dotnet build` reflexively prints `Build succeeded · 0 warnings · 0 errors`.
- **Basic concurrency vocabulary**: you know what a thread is, what a thread pool is, and you have at least *seen* a deadlock in some other language. We do not teach concurrency from scratch; if you have written a producer/consumer in Go (with channels), Rust (with `tokio::sync::mpsc`), or even Python (with `asyncio`), you are exactly the audience.
- A working `dotnet --version` of `9.0.x` or later on your PATH. C# 13 ships in this SDK and we use a handful of its features (`params ReadOnlySpan<T>`, primary constructors on regular classes, the new `lock` type, the `??=` on `field`).
- Nothing else. We start from `dotnet new console`, end at a typed async crawler with channels and cancellation, and never install a paid profiler.

## Topics covered

- The `Task` / `Task<T>` types: what they are, where their continuations run, and why "a `Task` is a promise of a value, not a thread."
- The `async`/`await` compiler transform: the generated state-machine struct, the `IAsyncStateMachine.MoveNext` switch, the awaiter pattern (`GetAwaiter`, `IsCompleted`, `OnCompleted`, `GetResult`), and where `await` *actually* releases the calling thread.
- `ValueTask` and `ValueTask<T>`: when they help, when they hurt, and the cardinal rule "only `await` a `ValueTask` once."
- `ConfigureAwait(false)` in library code; why ASP.NET Core 9 removed the `SynchronizationContext` on the request path; what the WPF/WinForms context still does and why.
- `IAsyncEnumerable<T>` and `await foreach`: pull-based async streams, `[EnumeratorCancellation]`, `WithCancellation`, and the gotcha that `IAsyncEnumerable<T>` does not buffer by default.
- `System.Threading.Channels`: `Channel.CreateBounded<T>`, `Channel.CreateUnbounded<T>`, `BoundedChannelFullMode`, the reader/writer split, and graceful completion with `Writer.Complete()`.
- Back-pressure: what it is, why bounded channels give it to you for free, and how to compose it with multiple producers and multiple consumers.
- `CancellationToken` and `CancellationTokenSource`: the cooperative cancellation pattern, `ThrowIfCancellationRequested`, `Register` callbacks, linked sources, and the `CancellationTokenSource(TimeSpan)` constructor for timeouts.
- Composing cancellation with timeout: `using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(5));`.
- `Task.WhenAll`, `Task.WhenAny`, `Task.WhenEach` (new in .NET 9), and `Parallel.ForEachAsync`: when each is the right tool and when each is the wrong one.
- Common pitfalls: `Task.Result`, `Task.Wait`, `async void`, fire-and-forget, captured-context deadlocks, exception swallowing, the `OperationCanceledException` that propagated when nothing was cancelled, the `TaskCanceledException` vs `OperationCanceledException` distinction.
- Threading basics: the .NET thread pool, `Task.Run`, `Task.Factory.StartNew` (and why you almost never want it), `Thread`, `ThreadPool.QueueUserWorkItem`, and "what runs on the I/O completion port."

## Weekly schedule

The schedule adds up to approximately **34 hours**. Treat it as a target, not a contract.

| Day       | Focus                                                  | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | Tasks, async/await, the state machine                  |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | `ValueTask`, `IAsyncEnumerable`, `ConfigureAwait`      |    1h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     4.5h    |
| Wednesday | `System.Threading.Channels`, back-pressure             |    1.5h  |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5h      |
| Thursday  | `CancellationToken`, timeouts, `WhenEach`              |    1.5h  |    1.5h   |     1h     |    0.5h   |   1h     |     2h       |    0.5h    |     8h      |
| Friday    | Mini-project — typed async crawler                     |    0h    |    0h     |     0h     |    0.5h   |   1h     |     3h       |    0.5h    |     5h      |
| Saturday  | Mini-project deep work, integration tests              |    0h    |    0h     |     0h     |    0h     |   1h     |     3h       |    0h      |     4h      |
| Sunday    | Quiz, review, polish                                   |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                        | **6h**   | **6h**    | **1h**     | **3.5h**  | **6h**   | **8.5h**     | **2.5h**   | **33.5h**   |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | Curated Microsoft Learn, .NET source, and open-source links |
| [lecture-notes/01-tasks-async-await-and-the-state-machine.md](./lecture-notes/01-tasks-async-await-and-the-state-machine.md) | `Task`/`Task<T>`, the `async`/`await` compiler transform, `ValueTask`, `IAsyncEnumerable<T>`, `ConfigureAwait`, deadlocks |
| [lecture-notes/02-channels-and-cancellation.md](./lecture-notes/02-channels-and-cancellation.md) | `System.Threading.Channels`, back-pressure, `CancellationToken`, linked sources, timeouts, `WhenEach`, `Parallel.ForEachAsync` |
| [exercises/README.md](./exercises/README.md) | Index of short coding exercises |
| [exercises/exercise-01-async-basics.cs](./exercises/exercise-01-async-basics.cs) | Fill-in-the-TODO async pipeline that exercises `Task.WhenAll`, `ValueTask`, `ConfigureAwait`, and `IAsyncEnumerable<T>` |
| [exercises/exercise-02-channels-producer-consumer.cs](./exercises/exercise-02-channels-producer-consumer.cs) | A bounded `Channel<T>` pipeline with one producer, three consumers, and a `BoundedChannelFullMode.Wait` back-pressure policy |
| [exercises/exercise-03-cancellation-and-timeout.cs](./exercises/exercise-03-cancellation-and-timeout.cs) | A console app that responds to `Ctrl-C` in under 50 ms while honouring a 5-second timeout — composed with `CreateLinkedTokenSource` |
| [challenges/README.md](./challenges/README.md) | Index of weekly challenges |
| [challenges/challenge-01-build-a-rate-limited-fetcher.md](./challenges/challenge-01-build-a-rate-limited-fetcher.md) | Build a token-bucket-style HTTP fetcher backed by a `Channel<TimeSlot>` with measurements |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on async, channels, and cancellation in .NET 9 / C# 13 |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for the "Async Crawler" — a typed crawler with `Channel<Uri>` work queue, `IAsyncEnumerable<CrawlResult>` streaming, and `CancellationToken` shutdown |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

A nullable-reference warning is a bug. A `CA2007` ("call `ConfigureAwait`") warning in a library project is a bug. An `async void` outside of an event handler is a bug. A `Task` returned without an `await` and without an explicit `.ConfigureAwait(false).GetAwaiter().GetResult()` synchronous-bridge call is a bug. By the end of Week 4 you will have a typed async crawler that compiles clean, streams `CrawlResult` over `IAsyncEnumerable<T>` straight to a terminal, throttles fan-out with a `Channel<Uri>`, and shuts down in under 50 ms when you press `Ctrl-C` — all from ~600 lines of C# you wrote yourself.

## A note on what's not here

Week 4 introduces async, but it does **not** introduce:

- **Reactive Extensions (`System.Reactive`).** Rx is a beautiful library and an entire mental model. `IAsyncEnumerable<T>` covers ~80% of what people reach for Rx for in .NET 9. We stay on the BCL.
- **TPL Dataflow (`System.Threading.Tasks.Dataflow`).** Dataflow is the older sibling of `Channels`. It is still excellent for complex DAGs of stages. We stay on `Channels` for this week — they are smaller, newer, and the right default in 2026.
- **Akka.NET, Orleans, or any actor model.** Actors are a Phase 3 topic. Async is the substrate; actors are an architecture on top.
- **`Span<T>`, `Memory<T>`, and the performance side of async I/O.** `ReadOnlySpan<byte>` slicing belongs in Week 12 (Performance). Here we focus on correctness.
- **`SignalR`, gRPC streaming, or any async protocol-level construct.** The crawler reads HTTP via `HttpClient.GetAsync`; we do not stream over the wire. Streaming protocols come in Phase 2.
- **`IObservable<T>` and `IObserver<T>`.** Same reason as Rx: redundant with `IAsyncEnumerable<T>` for our purposes.

The point of Week 4 is a sharp, narrow tool: async APIs you can read, channels you can compose, and a cancellation chain that actually cancels.

## Stretch goals

If you finish the regular work early and want to push further:

- Read **Stephen Toub's "ValueTask<TResult> with C# 7.0 async/await improvements"** (the canonical blog post) end to end: <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>.
- Skim the **dotnet/runtime source** for `Task` — `src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs` is dense but readable: <https://github.com/dotnet/runtime/tree/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks>.
- Read **the `Channels` design document** for the back-pressure semantics: <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>.
- Watch **"Three patterns for async in .NET" by Stephen Toub** on the .NET YouTube channel.
- Build a **second crawler** that uses `Parallel.ForEachAsync` instead of a manual `Channel<Uri>`. Note where each style is cleaner and where each is constraining.
- Implement the **token-bucket rate limiter** from Challenge 1 against the new `System.Threading.RateLimiting` namespace; compare both implementations.

## Up next

Continue to **Week 5 — OOP, SOLID, and Dependency Injection in Practice** once you have pushed the mini-project to your GitHub. The crawler's `IFetcher`, `IParser`, and `IStore` interfaces become Week 5's case study for "interface segregation in C# 13" — each interface owns exactly one responsibility, each implementation is registered with the right lifetime, and the whole graph is validated at startup with `ValidateOnBuild = true`.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
