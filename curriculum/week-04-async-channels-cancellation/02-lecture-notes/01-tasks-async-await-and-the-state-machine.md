# Lecture 1 — Tasks, async/await, and the State Machine

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can read the IL the C# 13 compiler generates for an `async Task<T>` method, explain where the continuation runs, distinguish `Task`/`Task<T>`/`ValueTask<T>`/`IAsyncEnumerable<T>` by allocation and consumption shape, write a deadlock from memory (and explain why it happens), and apply `ConfigureAwait(false)` correctly in library code.

If you only remember one thing from this lecture, remember this:

> **`async` is a compiler transform, not a runtime feature.** When you write `async Task<int> FooAsync()`, the C# compiler rewrites the method body into a state-machine struct. The CLR contributes the thread pool, the `SynchronizationContext`, and the `TaskScheduler` — but the *asynchrony* is generated code, not magic. Master the transform and the cooperation with the runtime, and the rest of async — `ValueTask`, `IAsyncEnumerable<T>`, `ConfigureAwait`, `Channel<T>` — is configuration on top.

---

## 1. What `Task` actually is

A `Task` is **a promise of a future operation**, not a thread. Internally a `Task` holds three things you care about:

| Field | What it stores |
|-------|----------------|
| `Status` | One of `Created`, `WaitingForActivation`, `Running`, `RanToCompletion`, `Canceled`, `Faulted`. |
| `Result` (for `Task<T>`) | The value, once the operation completes. |
| `Exception` | The captured `AggregateException`, if the operation faulted. |

A `Task` can be backed by a thread (when you wrote `Task.Run(() => ...)`), but the *common* case is that it is backed by *nothing*. A `Task<HttpResponseMessage>` returned by `HttpClient.GetAsync` is registered as a callback on an I/O completion port — the OS notifies the runtime when the bytes arrive, and only *then* is a thread pool thread grabbed to execute the continuation. The Task is alive the whole time without occupying a thread.

This is the most-misunderstood fact about async in .NET 9. People say "`await` blocks the thread." It does not. `await` *suspends the method* and *releases the thread*. If anyone in your codebase says otherwise, hand them this lecture.

### `Task.CompletedTask`, `Task.FromResult<T>(T)`, and `ValueTask.CompletedTask`

When you have a method whose body has nothing async to do — usually an interface implementation that has to be `Task`-shaped — return one of:

```csharp
public Task DoNothingAsync() => Task.CompletedTask;
public Task<int> AlwaysFortyTwoAsync() => Task.FromResult(42);
```

`Task.FromResult<T>(T)` **allocates** a small completed `Task<T>`. If you hit this on a hot path — say, an in-memory cache whose 99% case is a hit — switch to `ValueTask<T>`:

```csharp
public ValueTask<int> AlwaysFortyTwoAsync() => new(42);
```

Now the synchronous path allocates nothing. We will revisit `ValueTask` in section 5.

---

## 2. The async/await compiler transform

This is the section where async stops being magic. Open a fresh project:

```bash
mkdir AsyncDemo && cd AsyncDemo
dotnet new console -n AsyncDemo -o src/AsyncDemo
cd src/AsyncDemo
```

Replace `Program.cs` with:

```csharp
using System.Diagnostics;

await using var demo = new Demo();
Console.WriteLine(await demo.WorkAsync(2));

public sealed class Demo : IAsyncDisposable
{
    public async Task<int> WorkAsync(int seconds)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        return (int)sw.ElapsedMilliseconds;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Build and decompile with the tool of your choice — `ILSpy` (free, cross-platform), `dotPeek` (free), or `dotnet ildasm` (built into the SDK):

```bash
dotnet build -c Debug
ildasm bin/Debug/net9.0/AsyncDemo.dll
```

You will see a struct the compiler generated next to `Demo`, named something like `<WorkAsync>d__0`. The shape of it:

```csharp
// Compiler-generated (pseudo-C#). Do not write this by hand.
[CompilerGenerated]
private struct <WorkAsync>d__0 : IAsyncStateMachine
{
    public int <>1__state;
    public AsyncTaskMethodBuilder<int> <>t__builder;
    public Demo <>4__this;
    public int seconds;
    public Stopwatch <sw>5__1;
    private TaskAwaiter <>u__1;

    void IAsyncStateMachine.MoveNext()
    {
        int num = <>1__state;
        int result;
        try
        {
            TaskAwaiter awaiter;
            if (num != 0)
            {
                <sw>5__1 = Stopwatch.StartNew();
                awaiter = Task.Delay(TimeSpan.FromSeconds(seconds)).GetAwaiter();
                if (!awaiter.IsCompleted)
                {
                    num = (<>1__state = 0);
                    <>u__1 = awaiter;
                    <>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
                    return;
                }
            }
            else
            {
                awaiter = <>u__1;
                <>u__1 = default;
                num = (<>1__state = -1);
            }
            awaiter.GetResult();
            result = (int)<sw>5__1.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            <>1__state = -2;
            <sw>5__1 = null;
            <>t__builder.SetException(ex);
            return;
        }
        <>1__state = -2;
        <sw>5__1 = null;
        <>t__builder.SetResult(result);
    }

    void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine) { }
}
```

Read it twice. Internalize five facts:

1. **The state machine is a struct.** It boxes onto the heap *only if* the method actually suspends (the awaiter was not already completed). The synchronous fast-path allocates nothing.
2. **Every local variable becomes a field.** `Stopwatch sw` is now `<sw>5__1`. That is what people mean when they say "the closure escapes to the heap."
3. **Every `await` is a numbered state.** Here there is one state (`0`). A method with three `await`s has three.
4. **`<>t__builder` is an `AsyncTaskMethodBuilder<T>`.** This is the bridge between the state machine and the returned `Task<T>`. Its `SetResult(result)` is what eventually completes the `Task<int>` you returned.
5. **`AwaitUnsafeOnCompleted` is the suspension point.** It registers `this` (the state-machine struct, boxed by the first call) as a continuation on the awaiter; when the awaiter completes, the runtime calls back into `MoveNext` to resume at the saved state.

That is the entire transform. There is nothing else.

### Where does the continuation run?

When the state machine resumes, it runs on *whatever thread the runtime picks* — usually a thread pool thread, but it depends on the captured `SynchronizationContext`:

| Host | `SynchronizationContext` | Continuation runs on |
|------|--------------------------|----------------------|
| Console app | `null` | Any thread pool thread |
| WPF / WinForms | UI sync context | The UI thread |
| ASP.NET Framework (legacy) | Request sync context | An ASP.NET pool thread tied to the request |
| **ASP.NET Core 9** | **`null`** | **Any thread pool thread** |

ASP.NET Core 9 removed the `SynchronizationContext` from the request path years ago, which is why you stopped seeing `ConfigureAwait(false)` in modern ASP.NET Core tutorials. **In application code**, the captured context is a no-op. **In library code**, you still cannot know who will call you, so you write `ConfigureAwait(false)` defensively. We revisit this in section 6.

---

## 3. The awaiter pattern

`await` is not hard-coded to `Task`. The compiler looks for an awaiter — any type with the following shape:

```csharp
public TAwaiter GetAwaiter();

// where TAwaiter has:
public bool IsCompleted { get; }
public T GetResult();
public void OnCompleted(Action continuation);
// optionally: public void UnsafeOnCompleted(Action continuation);  // critical-finalizer-safe
```

This is why you can `await` anything that follows the protocol: `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, `YieldAwaitable` (from `Task.Yield()`), and any type you write yourself that exposes `GetAwaiter()`. The compiler's `await foo` lowering is:

```csharp
var awaiter = foo.GetAwaiter();
if (!awaiter.IsCompleted)
{
    // suspend: register OnCompleted callback, return from MoveNext
}
awaiter.GetResult(); // re-raises exceptions if the operation faulted
```

The takeaway: **the compiler does not know about `Task`**. It only knows about the awaiter pattern. That is how `ValueTask<T>` works without language changes — `ValueTask<T>` exposes `GetAwaiter()`.

---

## 4. `Task.Run`, the thread pool, and "I/O-bound" vs "CPU-bound"

Two phrases you will read in every async tutorial: *I/O-bound* and *CPU-bound*. They are real and they matter.

- **I/O-bound** work is "waiting for something not-on-this-CPU" — disk, network, a database, a child process. `HttpClient.GetAsync`, `File.ReadAllTextAsync`, `db.SaveChangesAsync` are all I/O-bound. **You do not need `Task.Run` for I/O-bound work.** The async API is async all the way down; `await`ing it never blocks a thread.
- **CPU-bound** work is "burn CPU cycles in this process" — parse a 50 MB JSON file, JPEG-decode a frame, compute a hash. `Task.Run(() => Heavy())` puts that work on a thread pool thread so it does not block the caller. **`Task.Run` is the bridge from sync to async for CPU-bound work.**

The hazard pattern: wrapping I/O-bound work in `Task.Run`:

```csharp
// WRONG — burns a thread pool thread for nothing
var result = await Task.Run(() => httpClient.GetStringAsync(url));
```

This actually *worsens* throughput. `httpClient.GetStringAsync(url)` already returned a `Task<string>` that completes when the I/O completion port fires. Wrapping it in `Task.Run` schedules an extra delegate on the pool, which then `await`s the inner task, which still completes on a pool thread. You doubled the scheduling overhead for zero benefit.

The right pattern:

```csharp
var result = await httpClient.GetStringAsync(url);
```

For CPU-bound work, `Task.Run` is the right tool:

```csharp
var hash = await Task.Run(() => ComputeSha256OfTenMegabyteFile());
```

### Thread-pool starvation

If you call `someTask.Result` or `someTask.Wait()` on a thread pool thread, that thread is now blocked. If enough threads do this simultaneously, the pool runs out of threads to *resume the continuations that would unblock them*. The whole process freezes.

The fix is simple: never call `.Result` or `.Wait()` on a `Task` you are not 100% certain has completed. If you find yourself wanting to, you are about to introduce a deadlock or starvation. Use `await` instead.

---

## 5. `ValueTask` and `ValueTask<T>`

`Task<T>` is a class — every async method allocates one. For a synchronous fast-path that runs millions of times per second, that allocation is the bottleneck.

`ValueTask<T>` is a struct that wraps either:
- the synchronous result (no allocation), or
- an `IValueTaskSource<T>` that the implementer pools.

The classic use case is an `async`-shaped read off a buffered stream:

```csharp
public ValueTask<int> ReadByteAsync(CancellationToken ct = default)
{
    // Fast path: buffer has data, no I/O needed.
    if (_buffer.TryReadByte(out var b))
        return new ValueTask<int>(b);

    // Slow path: need to refill from the underlying stream.
    return new ValueTask<int>(RefillAndReadByteAsync(ct));
}
```

In the fast path, no `Task<int>` is allocated. In the slow path, you fall back to a regular `Task<int>`.

**The cardinal rule of `ValueTask`: `await` it once.** A `ValueTask<T>` is not idempotent — calling `GetResult()` twice on a `ValueTask<T>` whose underlying source is pooled will return stale data or throw. The compiler enforces this by calling `GetResult()` inside the state machine exactly once.

If you need to `await` the *same* result twice, convert to a `Task<T>` first:

```csharp
var t = SomeMethodReturningValueTaskAsync().AsTask();
await t;        // first await
var x = await t; // second await — works because Task<T> is idempotent
```

When should you return `ValueTask<T>`? **Only when you have measured allocations and they matter.** For a public API on a domain service that runs once per HTTP request, `Task<T>` is fine and simpler. For an inner loop in a hot data-path library, `ValueTask<T>` may shave a measurable percentage of allocations.

The .NET 9 BCL itself returns `ValueTask<T>` from `Stream.ReadAsync`, `PipeReader.ReadAsync`, and most of the new `System.IO.Pipelines` API. For your own code: default to `Task<T>`, upgrade to `ValueTask<T>` when measurements justify it.

---

## 6. `ConfigureAwait(false)` in 2026

The `ConfigureAwait` story is the single most over-told and over-applied piece of async folklore. The truth in .NET 9:

- **Library code** (NuGet packages, shared class libraries, anything that does not own its caller): always `ConfigureAwait(false)`. You do not know if the caller is WPF, WinForms, MAUI, or ASP.NET Core. Be a good citizen; do not capture context you do not need.
- **ASP.NET Core 9 application code** (your controllers, your endpoints, your services that only run in ASP.NET Core): the `SynchronizationContext` is `null`, so `ConfigureAwait(false)` is a no-op. You may omit it.
- **WPF / WinForms / MAUI application code**: omit `ConfigureAwait(false)` on the UI thread when you *need* to resume on the UI thread (e.g. to update a control). Include it when you do not.

The 2018 rule "always `ConfigureAwait(false)` everywhere" was a defensive overcorrection. The 2026 rule is **"library code: yes. App code: it depends on the host."**

A practical convention: in shared class libraries, enable the `CA2007` analyzer warning (`Microsoft.CodeAnalysis.NetAnalyzers`); in ASP.NET Core app projects, disable it. That mirrors the actual rule.

---

## 7. The three classic async deadlocks

Read these three patterns. Then never write them.

### Deadlock 1: `.Result` on a captured context

```csharp
// WPF event handler — UI thread captured
private void OnClick(object sender, EventArgs e)
{
    var data = LoadAsync().Result; // <-- DEADLOCK
    Display(data);
}

private async Task<string> LoadAsync()
{
    return await new HttpClient().GetStringAsync("https://example.com");
}
```

The UI thread calls `LoadAsync()`. `LoadAsync` `await`s, capturing the UI `SynchronizationContext`. The HTTP request completes on a pool thread. The continuation tries to post back to the UI thread to call `GetResult()`. But the UI thread is blocked on `.Result`. Deadlock.

The fix is *not* `ConfigureAwait(false)`, even though that would unblock this one. The fix is **never call `.Result`**. Make `OnClick` `async void`:

```csharp
private async void OnClick(object sender, EventArgs e)
{
    var data = await LoadAsync();
    Display(data);
}
```

(`async void` is acceptable here because event handlers are the one place the platform contract requires it.)

### Deadlock 2: `Task.Wait` in a console-on-pool-thread scenario

A subtler variant: a `BackgroundService` runs `Heavy().Wait()` where `Heavy` is itself async and uses the thread pool. If the pool is saturated and `Wait` is blocking the only available thread, no continuation can run. Effective deadlock, manifested as a hang.

The fix is the same: always `await`.

### Deadlock 3: `async void` swallowing exceptions

```csharp
public async void DoWorkAsync()
{
    throw new InvalidOperationException("boom"); // <-- unobserved
}
```

`async void` returns `void`. There is no `Task` for anyone to `await`. An exception thrown inside is posted to the captured `SynchronizationContext`'s exception handler, which in a console app is the AppDomain's unhandled-exception handler — which terminates the process. In a WPF app, it propagates through `Dispatcher.UnhandledException`. In an ASP.NET Core app, it crashes the request and any pending request on the same thread.

The rule: **`async void` for event handlers only**. Everywhere else: `async Task` or `async Task<T>`.

---

## 8. `IAsyncEnumerable<T>` and `await foreach`

This is the .NET feature that most cleanly replaces "Rx, mostly." When a method *produces* a sequence of values asynchronously, return `IAsyncEnumerable<T>` and use `yield return`:

```csharp
public async IAsyncEnumerable<int> CountAsync(
    int upTo,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (var i = 0; i < upTo; i++)
    {
        await Task.Delay(100, ct);
        yield return i;
    }
}
```

The consumer:

```csharp
await foreach (var value in CountAsync(10).WithCancellation(ct))
{
    Console.WriteLine(value);
}
```

Three things to notice:

1. The producer is `async IAsyncEnumerable<T>` (no `Task<>` wrapper).
2. The `[EnumeratorCancellation]` attribute is essential — without it, `WithCancellation(ct)` on the consumer side is a no-op; the token never reaches the generator.
3. The consumer uses `await foreach`, not `foreach`.

`IAsyncEnumerable<T>` is **pull-based** — the consumer drives the rate. The producer does not buffer; it yields one value, suspends on the next `await`, and resumes when the consumer pulls the next item. That is exactly the back-pressure model we want for read paths that stream out of EF Core, an HTTP `GetStreamAsync`, or any unbounded source.

For "I want push-based, with explicit buffer policy" — use `System.Threading.Channels` (Lecture 2).

---

## 9. `Task.WhenAll`, `Task.WhenAny`, and `Task.WhenEach` (.NET 9)

Three composition primitives for "I have multiple Tasks; combine them."

```csharp
// WhenAll: wait for every Task; throw an AggregateException if any faulted.
var results = await Task.WhenAll(t1, t2, t3);

// WhenAny: yield as soon as one Task completes. Returns the completed Task.
var first = await Task.WhenAny(t1, t2, t3);
var value = await first; // still need to await for the result or exception

// WhenEach (.NET 9): yield each Task in *completion order*.
await foreach (var t in Task.WhenEach(t1, t2, t3))
{
    var value = await t;
    Console.WriteLine(value);
}
```

`Task.WhenEach` is the .NET 9 addition that replaces the awkward `WhenAny`-in-a-loop pattern. When you want to "process results in completion order, not start order," reach for `WhenEach`.

**Exception semantics differ between the three:**

- `WhenAll` aggregates *all* exceptions into one `AggregateException`. If three tasks fail, you see three exceptions.
- `WhenAny` only surfaces the exception of the *one* task you awaited next. Other faulted tasks may go unobserved unless you await them too.
- `WhenEach` lets you observe each task's exception individually as you iterate — usually the right model.

---

## 10. The TAP pattern — when *your* method should look async

The Task Asynchronous Pattern (TAP) is the contract every public async API in .NET follows. If you ship an async method, it should:

1. **Return `Task`, `Task<T>`, or `ValueTask`/`ValueTask<T>`.** Not `void`. Not `IAsyncResult`.
2. **End with the `Async` suffix.** `ReadAsync`, `SaveAsync`, `FetchAsync`. (ASP.NET Core handler methods are the one exception by convention.)
3. **Accept a `CancellationToken` parameter** with a `default` value, as the last parameter. Always. We will hammer this in Lecture 2.
4. **Throw `ArgumentNullException` etc. synchronously**, before the first `await`. Argument validation belongs at the top of the method, not inside a state machine that has already suspended.
5. **Propagate exceptions through the `Task`.** Do not log-and-swallow. Do not catch and rethrow.

Follow TAP and any consumer can use your API with `await`, `Task.WhenAll`, `WithCancellation`, and every async-aware analyzer without surprise.

---

## What you should be able to do now

- Open the IL of an `async Task<T>` method and identify the state-machine struct, the `MoveNext` switch, and the `<>t__builder.AwaitUnsafeOnCompleted` call.
- Explain in one sentence why ASP.NET Core 9 does not need `ConfigureAwait(false)` on the request path.
- Pick between `Task<T>` and `ValueTask<T>` for a new API based on whether the synchronous fast-path matters.
- Write a producer with `async IAsyncEnumerable<T>` and a consumer with `await foreach` that supports cancellation end-to-end.
- Recognize the three async deadlocks on sight and rewrite each to the right pattern.
- Pick between `Task.WhenAll`, `Task.WhenAny`, and `Task.WhenEach` based on the result-ordering semantics you need.

Continue to **[Lecture 2 — Channels and Cancellation](./02-channels-and-cancellation.md)**. The producer/consumer pipelines you build there will lean on every concept from this lecture — and add the back-pressure and cancellation discipline that distinguishes a real async pipeline from a tutorial one.
