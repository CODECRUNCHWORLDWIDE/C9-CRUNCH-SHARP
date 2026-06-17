# Lecture 1 — The Await Lowering, SynchronizationContext, and ConfigureAwait

> **Reading time:** ~75 minutes. **Hands-on time:** ~30 minutes (you write a tiny `SynchronizationContext` and watch continuations run on it).

This is the lecture that earns you the right to reason about `async` code on a whiteboard. Week 4 made you fluent in writing `async`/`await`; this lecture makes you fluent in reading what the compiler actually emits when you write it. By the end you will be able to take a fifty-line `async` method, point at every `await` and say what `SynchronizationContext` it captures, whether `.ConfigureAwait(false)` would change the behaviour, and whether either choice is correct for the context in which the method is called. That is the skill that distinguishes a senior .NET engineer from a junior one — not the writing of `await`, but the *reading* of it.

## 1.1 — What `async`/`await` is not

Before we cover what `await` is, three quick corrections to the most common mental models, because they keep producing bugs in real code:

1. **`async` does not start a thread.** Adding `async` to a method signature changes how the compiler emits the method body; it does not allocate a thread. An `async Task<int>` method called from `Main` runs on whichever thread called `Main`, until it hits its first `await`. If the awaited operation completes synchronously (and many do), the method continues on the same thread without ever yielding. If it does not, the method suspends and the calling thread is released. No thread was started, anywhere. The popular folklore "`async` makes the method run on a background thread" is wrong; the correct statement is "`async` makes the method *suspendable*."
2. **`await` does not block the thread.** `await foo` does not pause the current thread until `foo` finishes. It registers a continuation with `foo` and returns. The current thread immediately moves on to do something else — finish the calling method, return to the message pump, return to the request handler. The continuation runs *later*, on a thread chosen by the awaiter's `SynchronizationContext` and `TaskScheduler` (the rules of which we will derive in 1.4). The popular folklore "`await` is just like `.Wait()` but asynchronous" is wrong; `.Wait()` blocks the calling thread and `await` releases it. The two have opposite effects on `ThreadPool` thread availability, which is why mixing them is the most common cause of deadlocks in .NET.
3. **`Task<T>` is not a thread.** A `Task<T>` is a promise of a future value, plus a continuation registry, plus a state machine for tracking completion. The .NET runtime *may* schedule the work that produces the value on a `ThreadPool` thread, *may* schedule it on a custom `TaskScheduler`, *may* complete it synchronously, *may* never complete it at all (cancelled `TaskCompletionSource`). The mapping from `Task<T>` to "thread that runs the work" is many-to-one and not always one-to-one. A `Task<T>` returned from an `HttpClient` call is backed by an I/O completion port, not a thread; no thread is "running" it while the HTTP request is in flight.

Internalise the three corrections and the rest of this lecture follows from them.

## 1.2 — What the compiler emits for `async`

Here is a typical `async` method:

```csharp
public async Task<int> GetUserScoreAsync(int userId, CancellationToken ct)
{
    HttpResponseMessage response = await _http.GetAsync($"/users/{userId}", ct);
    string body = await response.Content.ReadAsStringAsync(ct);
    return int.Parse(body);
}
```

The C# compiler does not compile that method as written. It compiles a *transformation* of it: a private nested type implementing `IAsyncStateMachine`, with one field per local variable, one field per `await`'d awaiter, and a `MoveNext()` method whose body is a switch on a `state` integer. The actual emitted IL is verbose; if you want to see it run the `Sharplab` web tool (`https://sharplab.io`) with the C# compiler set to "Debug" and the output set to "C#" — you will see something like:

```csharp
// What the compiler effectively writes (simplified):
[CompilerGenerated]
private struct GetUserScoreAsync_StateMachine : IAsyncStateMachine
{
    public int <>1__state;
    public AsyncTaskMethodBuilder<int> <>t__builder;
    public int userId;
    public CancellationToken ct;
    public HttpClient _http;
    private TaskAwaiter<HttpResponseMessage> <>u__1;
    private TaskAwaiter<string> <>u__2;
    private HttpResponseMessage <response>5__1;
    private string <body>5__2;

    public void MoveNext()
    {
        int num = <>1__state;
        int result;
        try
        {
            TaskAwaiter<HttpResponseMessage> awaiter1;
            TaskAwaiter<string> awaiter2;
            switch (num)
            {
                default:
                    awaiter1 = _http.GetAsync($"/users/{userId}", ct).GetAwaiter();
                    if (!awaiter1.IsCompleted)
                    {
                        <>1__state = 0;
                        <>u__1 = awaiter1;
                        <>t__builder.AwaitUnsafeOnCompleted(ref awaiter1, ref this);
                        return;
                    }
                    goto IL_finish_first;
                case 0:
                    awaiter1 = <>u__1;
                    <>u__1 = default;
                    <>1__state = -1;
                    goto IL_finish_first;
                // ...similar for the second await...
            }
        IL_finish_first:
            <response>5__1 = awaiter1.GetResult();
            // ...
        }
        catch (Exception exception)
        {
            <>1__state = -2;
            <>t__builder.SetException(exception);
            return;
        }
        <>1__state = -2;
        <>t__builder.SetResult(result);
    }
}
```

Two observations matter for the rest of this lecture:

**Observation A.** Every `await` is, mechanically, three things in sequence: `GetAwaiter()` to obtain an awaiter (a struct with `IsCompleted`, `OnCompleted`, `GetResult`), a check of `awaiter.IsCompleted`, and — if it is *not* complete — a registration of the state machine itself as the continuation via `AwaitUnsafeOnCompleted`. The "is this `await` going to suspend" decision is local to the awaiter; for `Task<T>`, it is `task.IsCompleted`. For a `Task` that completes synchronously (which happens often — e.g. `Task.FromResult(42)`, a cached response, an already-completed `ValueTask<T>`), the `await` does not suspend and the method continues on the calling thread without releasing it.

**Observation B.** The state machine is initially a *struct* (zero allocation) and only gets boxed to the heap if the method actually suspends. `AwaitUnsafeOnCompleted` is the boxing point. This is why `async` methods that complete synchronously most of the time are nearly free; it is also why `ValueTask<T>` exists (covered in 1.10). The runtime's "make `async` faster than `Task`" optimisation since .NET 5 has been increasingly aggressive elision of even this boxing in the common path.

The full walk-through is in Stephen Toub's "How Async/Await Really Works in C#" (2023): <https://devblogs.microsoft.com/dotnet/how-async-await-really-works/>. We will refer to that post by section name in homework problems; read it once end-to-end this week if you have not.

## 1.3 — Where the continuation runs

When `await` suspends — `awaiter.IsCompleted` was `false`, the state machine registered itself as the continuation, the calling thread returned — *something* eventually has to resume the state machine. That "something" is `awaiter.OnCompleted(continuation)`: the awaited operation, on its own thread (an I/O completion thread, a `ThreadPool` worker, a UI message pump), invokes the continuation when the value is ready.

The question is: on *which* thread does the continuation run?

For a `Task` awaited without `.ConfigureAwait(false)`, the rule the runtime applies is:

1. Capture `SynchronizationContext.Current` at the moment of `await`.
2. If non-null, post the continuation back to that `SynchronizationContext` (which usually means "run on the originating context's threads," e.g., a UI thread or a single dedicated thread).
3. Else, capture `TaskScheduler.Current`.
4. If it is *not* `TaskScheduler.Default`, schedule the continuation through it.
5. Else, run the continuation on whichever `ThreadPool` thread happens to be available when the awaited operation completes.

This is the *captured-context* model. It exists because, in 2012 when `async`/`await` shipped, the dominant Windows GUI frameworks (WPF, WinForms) had a single UI thread that owned every UI object. A method that did `var widget = await GetWidgetAsync(); widget.Text = "Done";` *had* to resume on the UI thread, because touching `widget.Text` from any other thread would throw `InvalidOperationException`. Capturing the `SynchronizationContext` and resuming there made the syntax `await` correct for UI code by default.

The cost of the captured-context model is that **every `await` performs a hidden context-capture and a hidden post-back**. For ASP.NET Classic (the pre-Core framework, still alive in many enterprise codebases), the captured context was the request-bound `AspNetSynchronizationContext`, which serialised continuations onto a single logical thread per request. For WPF and WinForms it was the UI message pump. For console apps and ASP.NET Core (`SynchronizationContext` is `null`), the capture was a no-op, but the *code path* still ran on every `await`.

`.ConfigureAwait(false)` is the opt-out. It returns a `ConfiguredTaskAwaitable<TResult>` whose awaiter explicitly does *not* capture `SynchronizationContext.Current`. The continuation runs on whatever thread the awaited operation completes on, which in practice is a `ThreadPool` thread.

## 1.4 — `SynchronizationContext` in detail

`System.Threading.SynchronizationContext` (<https://learn.microsoft.com/en-us/dotnet/api/system.threading.synchronizationcontext>) is a base class with two methods that matter:

```csharp
public virtual void Post(SendOrPostCallback d, object? state);    // fire and forget
public virtual void Send(SendOrPostCallback d, object? state);    // wait for completion
```

The default implementation of `Post` queues the callback to the `ThreadPool`; `Send` invokes it inline on the calling thread. Concrete subclasses override the behaviour:

- **`WindowsFormsSynchronizationContext`** posts the callback to the UI thread's message queue. The UI thread's message pump dequeues and runs the callback on its next pass.
- **`DispatcherSynchronizationContext`** (WPF) similarly posts to the `Dispatcher` queue of the originating thread.
- **`AspNetSynchronizationContext`** (ASP.NET Classic) serialises callbacks onto a logical-request thread; only one continuation at a time runs per request.
- **`null`** (ASP.NET Core, console apps, generic hosts) — no capture happens; the runtime falls through to step 3 of the algorithm in 1.3.

You can write your own. Here is the simplest possible single-threaded `SynchronizationContext`, useful for demonstration:

```csharp
using System.Collections.Concurrent;
using System.Threading;

public sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;

    public SingleThreadSynchronizationContext()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "SingleThreadSyncCtx" };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Thread.CurrentThread == _thread) d(state);
        else
        {
            using var done = new ManualResetEventSlim();
            _queue.Add((s =>
            {
                try { d(s); } finally { done.Set(); }
            }, state));
            done.Wait();
        }
    }

    private void Pump()
    {
        SynchronizationContext.SetSynchronizationContext(this);
        foreach (var (cb, state) in _queue.GetConsumingEnumerable()) cb(state);
    }

    public void Dispose() => _queue.CompleteAdding();
}
```

Exercise 1 has you write a version of this, install it as the current context on a thread, and observe that `await Task.Yield()` resumes on the pump thread by default and resumes on a `ThreadPool` thread when you write `await Task.Yield().ConfigureAwait(false)`. That is the entire `.ConfigureAwait` story made tangible in 30 lines.

## 1.5 — `TaskScheduler.Current` (the secondary rule)

If `SynchronizationContext.Current` is `null`, the runtime falls back to `TaskScheduler.Current`. `TaskScheduler` is a `Task`-system primitive that decides how `Task` instances are queued. The default (`TaskScheduler.Default`) submits them to the `ThreadPool`; custom schedulers can submit them to a dedicated thread pool, a single-thread scheduler, or any other target.

For application code, `TaskScheduler.Current` is almost always `TaskScheduler.Default`. The two cases where it is not:

- You created a `Task` with `TaskFactory.StartNew(..., TaskScheduler.FromCurrentSynchronizationContext())` — uncommon outside UI code.
- You explicitly set a different `TaskScheduler` in a `TaskFactory` you constructed yourself.

In production server code you can mostly ignore `TaskScheduler.Current`. The mental model "the continuation runs on a `ThreadPool` thread when there is no `SynchronizationContext`" is correct in 99% of cases.

## 1.6 — The library-versus-application heuristic

The most common question a junior engineer asks is "should I add `.ConfigureAwait(false)` everywhere?" The senior answer, codified by Stephen Toub in the 2019 "ConfigureAwait FAQ" (<https://devblogs.microsoft.com/dotnet/configureawait-faq/>):

> **If you are writing a *library* — a NuGet package, an SDK, a class library that callers consume — append `.ConfigureAwait(false)` to every `await`. You do not know whether your caller has a `SynchronizationContext`, and the cost of being wrong is a deadlock on a UI thread.**
>
> **If you are writing *application* code in ASP.NET Core or a generic-host console app, `.ConfigureAwait(false)` is a no-op. ASP.NET Core does not set a `SynchronizationContext`. You may write it because your team's analyzer demands it; you do not strictly need to.**
>
> **If you are writing application code in WPF, WinForms, Xamarin, MAUI, or ASP.NET Classic, do not write `.ConfigureAwait(false)` in the methods that touch UI objects. They must resume on the UI thread.**

This heuristic is uncontroversial in 2026; it has been the official guidance for seven years. The reason teams still argue about it is that many codebases are mixed — a class library used from both a WPF app and an ASP.NET Core service. The senior move in those codebases is: append `.ConfigureAwait(false)` to every `await` in the library, write your UI-touching code in a thin wrapper inside the WPF project, and let the wrapper omit `.ConfigureAwait(false)` so its continuations come back to the UI thread.

The 2023 `.NET 8` addition of `ConfigureAwaitOptions` adds two new opt-ins:

- `ConfigureAwaitOptions.ContinueOnCapturedContext` — the explicit version of "do capture context" (equivalent to `.ConfigureAwait(true)`).
- `ConfigureAwaitOptions.SuppressThrowing` — `await` the task without re-throwing on failure (you read `task.Exception` yourself afterwards).
- `ConfigureAwaitOptions.ForceYielding` — guarantee the await yields even if the task is already complete; useful for stress-testing concurrent code paths.

See <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions> for the .NET 8 reference.

## 1.7 — Async-over-sync versus sync-over-async

Two phrases that come up constantly in async PR review:

**Async-over-sync** is the pattern where an `async` method wraps a synchronous operation, typically with `Task.Run`:

```csharp
public async Task<int> ComputeAsync(int input, CancellationToken ct)
{
    return await Task.Run(() => ExpensiveCpuBoundComputation(input), ct);
}
```

This is *occasionally* correct (you want to offload CPU work from a UI thread; you want a synchronous library to participate in an async pipeline) and *frequently wrong* (you wrap a tiny synchronous call, paying for a `Task` allocation and a `ThreadPool` thread to no benefit). The rule: only async-wrap operations that take more than a millisecond, and only if the calling context is one that benefits from being released (UI thread, request thread).

**Sync-over-async** is the inverse pattern — a synchronous method that internally awaits an async operation by blocking with `.Result` or `.Wait()`:

```csharp
public int GetUserScore(int userId)
{
    return GetUserScoreAsync(userId, CancellationToken.None).Result;  // BAD
}
```

This is *always* wrong in production code. The reasons are covered in 1.8 (the deadlock case studies), but the short version is: blocking a `ThreadPool` thread on a `Task` that itself needs a `ThreadPool` thread to complete is a recipe for starvation, and on a captured-context platform (UI, ASP.NET Classic) it is a recipe for deadlock. The senior move when faced with a synchronous-only API is to make the caller async, not to add `.Result`. If the caller cannot become async (e.g., it is an `IDisposable.Dispose` implementation), the right tool is `IAsyncDisposable`, not `.Result`.

## 1.8 — The four canonical async deadlocks

**Deadlock 1: `.Result` on a UI thread.**

```csharp
// WPF button click handler, runs on UI thread (has SynchronizationContext)
private void OnClick(object sender, RoutedEventArgs e)
{
    int score = GetUserScoreAsync(42).Result;  // BLOCKS the UI thread
    // ...
}

public async Task<int> GetUserScoreAsync(int userId)
{
    string body = await _http.GetStringAsync($"/users/{userId}");  // captures UI ctx
    return int.Parse(body);                                         // resumes on UI ctx
}
```

The click handler blocks the UI thread on `.Result`. The HTTP response arrives on a `ThreadPool` thread, which posts the continuation back to the UI thread's `SynchronizationContext`. The post sits in the message queue, which is not being pumped because the UI thread is blocked on `.Result`. Both sides wait forever.

**The fix:** make `OnClick` `async`, write `int score = await GetUserScoreAsync(42);`. The `.Result` deadlock disappears because the UI thread is released at the `await`.

**Deadlock 2: `.Result` in ASP.NET Classic.**

```csharp
// ASP.NET Classic (System.Web) controller method, has request-bound SyncCtx
public ActionResult Index()
{
    int score = GetUserScoreAsync(42).Result;  // BLOCKS the request thread
    return View(score);
}
```

Same story: the captured `AspNetSynchronizationContext` serialises continuations onto a single per-request logical thread. That thread is blocked on `.Result`. The continuation cannot run. Deadlock.

**The fix in ASP.NET Classic:** make the controller `async Task<ActionResult>`. The deadlock also disappears under ASP.NET Core because Core has no `SynchronizationContext`, but the *other* problem (sync-over-async on a `ThreadPool` thread, ThreadPool starvation) is still present.

**Deadlock 3: Sync-over-async in a library that captures context.**

```csharp
// In a library; nobody set ConfigureAwait(false)
public string GetDataSync(int id)
{
    return GetDataAsync(id).Result;  // ALSO BAD even in a library
}

public async Task<string> GetDataAsync(int id)
{
    return await _http.GetStringAsync($"/data/{id}");  // captures caller's ctx
}
```

When called from a UI thread or ASP.NET Classic request, this deadlocks for the same reason as deadlock 1. The fix is twofold: append `.ConfigureAwait(false)` to the `await` inside `GetDataAsync` (which makes the deadlock disappear), *and* delete `GetDataSync` (because sync-over-async is always wrong in a library). Library code that exposes both `GetDataSync` and `GetDataAsync` is a code smell; the synchronous wrapper is almost always a mistake.

**Deadlock 4: `lock` and `await` reentrancy.**

```csharp
private readonly object _lock = new();
private int _balance;

public async Task TransferAsync(int amount)
{
    lock (_lock)  // ACQUIRE
    {
        _balance -= amount;
        await SaveAsync(amount);  // CS4004: cannot await in the body of a lock
    }                              // (the compiler catches this)
}
```

The compiler *prevents* `await` inside `lock` because the continuation might run on a different thread, which would attempt to release a lock held by a different thread — undefined behaviour. The trap is that engineers move the `await` outside the `lock` and forget the deeper problem: a `Monitor` lock cannot be held across `await` boundaries at all. The right tool is `SemaphoreSlim`:

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);

public async Task TransferAsync(int amount)
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        _balance -= amount;
        await SaveAsync(amount).ConfigureAwait(false);
    }
    finally
    {
        _gate.Release();
    }
}
```

`SemaphoreSlim.WaitAsync` releases the calling thread; `Release` does not care which thread releases it. The async-friendly mutual exclusion primitive in .NET is `SemaphoreSlim` initialized with `(1, 1)`. Use it whenever you need exclusion across an `await`.

Challenge 1 contains all four deadlocks as small programs you diagnose, explain, and fix.

## 1.9 — Cancellation propagation revisited

Week 4 introduced `CancellationToken`. The Week-4 rules — accept a `CancellationToken` on every public async method, pass it down to every internal `await`, call `ct.ThrowIfCancellationRequested()` at safe points — are still the rules. This week adds three refinements:

**Refinement A: `CancellationTokenSource` is `IDisposable`.** Always wrap in `using`. The classic bug is `var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));` followed by never disposing it, which leaks the underlying `Timer`.

**Refinement B: `CancellationToken.Register` returns a `CancellationTokenRegistration` (also `IDisposable`).** If you register a callback (e.g., to cancel a third-party blocking operation), keep the registration handle and dispose it inside `finally`, otherwise the registration outlives the operation and may fire later.

**Refinement C: `CancellationTokenSource.CreateLinkedTokenSource(...)` lets you combine an outer token (e.g., the request's `HttpContext.RequestAborted`) with an inner timeout. The result is also `IDisposable`. The pattern:**

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

try
{
    return await _downstream.GetAsync(linked.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (timeout.IsCancellationRequested)
{
    throw new TimeoutException("Downstream call exceeded 5s budget.");
}
```

The `when (timeout.IsCancellationRequested)` filter distinguishes "the caller cancelled us" (re-throw `OperationCanceledException`) from "our own timeout fired" (translate to `TimeoutException`). This is the production-shaped cancellation pattern; every external call in your week-8 mini-project should look like it.

Lecture 2 covers how cancellation tokens propagate into `IAsyncEnumerable<T>` via `[EnumeratorCancellation]`.

## 1.10 — `ValueTask<T>` (briefly; full treatment in Week 7)

Week 7 covered `ValueTask<T>` from a performance angle. This week we restate the consumption rules, because they are easy to violate when you start using `IAsyncEnumerable<T>` (whose `MoveNextAsync` returns `ValueTask<bool>`):

1. **Consume exactly once.** Do not `await` the same `ValueTask` twice. The instance may have been recycled.
2. **Do not pass to `Task.WhenAll` or `Task.WhenAny`.** Convert with `.AsTask()` first, which forces a `Task<T>` allocation.
3. **Do not block with `.Result` or `.GetAwaiter().GetResult()`** unless `.IsCompletedSuccessfully` is true. `ValueTask`'s synchronous completion path is fine to read; its asynchronous path may not have a backing `Task` to block on.
4. **Use `ValueTask` as a return type only when the method completes synchronously a meaningful fraction of the time.** If it always asynchronously suspends, the `ValueTask` wrapper is overhead; use `Task<T>`.

Stephen Toub's "Understanding the Whys, Whats, and Whens of ValueTask" (<https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>) is the canonical 2018 essay; the 2020 follow-up "Async ValueTask Pooling in .NET 5" (<https://devblogs.microsoft.com/dotnet/async-valuetask-pooling-in-net-5/>) explains the pooling that makes `async ValueTask` zero-alloc on the suspended path too. Both are required reading for this week's homework.

## 1.11 — A correctness checklist for every `async` method you write this week

Before you commit any async method, walk this list:

- [ ] The method ends in `Async` and returns `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, or `IAsyncEnumerable<T>`.
- [ ] The method accepts a `CancellationToken ct` as its last parameter (defaulting to `default` only for top-level handlers).
- [ ] Every internal `await` either passes `ct` through or has a defensible reason not to.
- [ ] If this method is in a library project, every `await` ends with `.ConfigureAwait(false)`.
- [ ] If this method is in application code in ASP.NET Core or a generic host, no `await` is followed by `.Wait()`, `.Result`, or `.GetAwaiter().GetResult()`.
- [ ] If this method holds shared mutable state, the state is protected by a `SemaphoreSlim` (never a `lock` block, never a `Monitor`).
- [ ] If this method has its own timeout, it creates an inner `CancellationTokenSource`, links it with the caller's token, and disposes both.
- [ ] If this method may run for a long time, it is observable — a `Meter` counter, an `ILogger.LogDebug` at the entry, or both.

The checklist is short on purpose. Most async correctness bugs are caught by these eight items. The week-8 exercises and challenges exist to make the checklist reflex rather than a cognitive load.

## 1.12 — What this lecture leaves for next

The next lecture covers `IAsyncEnumerable<T>` and `await foreach` — the streaming counterpart to `Task<List<T>>`, the right return type for "many items but not all at once", and the C# 8 feature that finally made streaming-async ergonomic. Lecture 3 covers channels in production — `Channel.CreateBounded`, the four full modes, fan-out pipelines, `Parallel.ForEachAsync`, and the `ThreadPool`-starvation diagnostic.

Read the Stephen Toub "ConfigureAwait FAQ" before lecture 2. The single best 45-minute investment you can make this week.
