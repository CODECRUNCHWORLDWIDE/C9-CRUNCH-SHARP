# Week 4 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 5. Answer key at the bottom — don't peek.

---

**Q1.** What does the C# 13 compiler emit when it lowers `async Task<int> FooAsync()`?

- A) A new thread is spawned for the method body; the `Task<int>` is a handle to that thread.
- B) A state-machine struct implementing `IAsyncStateMachine`; locals are hoisted into fields; the body is split at every `await` into numbered states with a `MoveNext` resumption switch.
- C) A delegate that the runtime invokes on a fresh thread-pool thread; `await` is a no-op syntactic sugar.
- D) A `Lazy<Task<int>>` that runs the body the first time it is awaited.

---

**Q2.** In an ASP.NET Core 9 endpoint, you write:

```csharp
public async Task<IResult> GetAsync(LedgerDbContext db, CancellationToken ct)
{
    var rows = await db.Transactions.ToListAsync(ct).ConfigureAwait(false);
    return TypedResults.Ok(rows);
}
```

What is the effect of the `.ConfigureAwait(false)`?

- A) It prevents a deadlock that would otherwise occur on the request-thread.
- B) It is a no-op: ASP.NET Core 9 does not install a `SynchronizationContext` on the request path, so there is nothing for `ConfigureAwait(false)` to opt out of.
- C) It forces the continuation onto a different thread, reducing thread-pool contention.
- D) It tells EF Core to not use a `Task` for the inner database operation.

---

**Q3.** You write a helper that may complete synchronously most of the time:

```csharp
public ??? GetOrLoadAsync(int key)
{
    if (_cache.TryGetValue(key, out var v))
        return /* synchronous path */;
    return /* asynchronous path */;
}
```

Which return type should you choose, and why?

- A) `Task<int>` — it is the standard async return type; performance is not a real concern.
- B) `ValueTask<int>` — the synchronous fast path avoids allocating a `Task<int>`; the slow path can wrap a `Task<int>`. Acceptable as long as callers `await` the result exactly once.
- C) `int` — return the synchronous value directly when present, and `null` otherwise.
- D) `Task<ValueTask<int>>` — wrap both possibilities in one type.

---

**Q4.** What is wrong with this code?

```csharp
public async Task<int> LoadAsync()
{
    var rows = await GetRowsAsync();
    return rows.Count;
}

public void OnButtonClick(object sender, EventArgs e)
{
    var count = LoadAsync().Result;
    MessageBox.Show(count.ToString());
}
```

- A) `LoadAsync` should be `async void`, not `async Task<int>`.
- B) The `.Result` on a `Task` captured against the UI `SynchronizationContext` deadlocks because the continuation cannot return to the UI thread.
- C) `MessageBox.Show` is not thread-safe.
- D) `GetRowsAsync` needs to be wrapped in `Task.Run`.

---

**Q5.** Which of these `Channel<T>` configurations gives you back-pressure (writers wait when the buffer is full)?

- A) `Channel.CreateUnbounded<T>()` with `SingleWriter = true`.
- B) `Channel.CreateBounded<T>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest })`.
- C) `Channel.CreateBounded<T>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait })`.
- D) `Channel.CreateBounded<T>(100)` with no options at all — back-pressure is impossible in `System.Threading.Channels`.

---

**Q6.** You are building a fan-in pipeline with 4 producers writing to one `Channel<int>`. When should `channel.Writer.Complete()` be called?

- A) Inside each producer's `finally`, so the first producer to finish closes the channel.
- B) Inside a `Task.WhenAll(producers).ContinueWith(_ => channel.Writer.Complete())` so that completion is signalled only after every producer is done.
- C) Never — `Channel<T>` auto-completes when all producers go out of scope.
- D) By the consumer when it has read enough items.

---

**Q7.** Given:

```csharp
public async IAsyncEnumerable<int> CountAsync(CancellationToken ct = default)
{
    for (var i = 0; i < 1000; i++)
    {
        await Task.Delay(10, ct);
        yield return i;
    }
}

// caller:
await foreach (var v in CountAsync().WithCancellation(callerCt))
{
    if (v >= 5) break;
}
```

When the caller cancels `callerCt`, does the producer's loop stop?

- A) Yes, because `WithCancellation` propagates the token into the generator automatically.
- B) No, because `CountAsync`'s `ct` parameter is missing the `[EnumeratorCancellation]` attribute; `WithCancellation` has nothing to bind the token to.
- C) Yes, because every `await` polls the ambient `CancellationToken.None`.
- D) No, because `IAsyncEnumerable<T>` does not support cancellation at all.

---

**Q8.** You need to fan out 100 HTTP requests with at most 8 in flight at any time, no streaming back, and you only care about the count of successes. Which is the right tool?

- A) `Channel.CreateBounded<Uri>(8)` with 8 consumer tasks.
- B) `Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (u, c) => ...)`.
- C) `Task.WhenAll(urls.Select(u => httpClient.GetAsync(u)))` — let the thread pool handle parallelism.
- D) `for` loop with `await` — let the runtime pipeline naturally.

---

**Q9.** What is the right place to `catch (OperationCanceledException)` in a long async call chain?

- A) Inside every async method that accepts a `CancellationToken`, so each layer can clean up its own state.
- B) Nowhere — let it escape the process.
- C) At exactly one place: the outermost frame that owns the operation (the console `Main`, the ASP.NET middleware, the `BackgroundService.ExecuteAsync`). Let it propagate everywhere in between.
- D) Inside any method that calls `ct.ThrowIfCancellationRequested()` — that's where the exception originates.

---

**Q10.** You want a method that respects both the caller's `CancellationToken` and a 5-second internal timeout. What is the standard pattern in .NET 9?

```csharp
public async Task<T> DoAsync(CancellationToken callerCt)
{
    using var cts = CancellationTokenSource.??? (callerCt);
    cts.??? (TimeSpan.FromSeconds(5));
    return await InnerAsync(cts.Token);
}
```

- A) `CreateLinkedTokenSource(callerCt)`; `CancelAfter`.
- B) `new CancellationTokenSource(callerCt)`; `Cancel`.
- C) `CreateLinkedTokenSource(callerCt)`; `Cancel`.
- D) `Token.Combine(callerCt)`; `CancelAfter`.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **B** — The compiler emits a state-machine struct implementing `IAsyncStateMachine`. Locals become hoisted fields. The method body is split at every `await` into numbered states; the `MoveNext` switch dispatches on `<>1__state`. No thread is spawned; the runtime's thread pool runs the continuation when the awaiter completes. Option A is the most common misconception about async — drill it out.

2. **B** — ASP.NET Core 9 removed the `SynchronizationContext` from the request path years ago. `ConfigureAwait(false)` is a hint to the awaiter "do not capture the current context"; with no context to capture, the call is a no-op. You may still write it for stylistic consistency, but it provides zero benefit. (In *library* code you do not own — e.g., a NuGet package — keep writing `ConfigureAwait(false)` defensively because you cannot know your caller's host.)

3. **B** — `ValueTask<int>` exists precisely for this case: a synchronous fast path that returns a value directly (allocating no `Task<int>`), and an asynchronous slow path wrapped in `new ValueTask<int>(Task<int>)`. The cardinal rule is "only `await` a `ValueTask` once" — if you need to await multiple times, call `.AsTask()` to convert. For one-time `await` patterns this is the right tool. Returning a `Task<int>` (A) allocates on every call. Returning `int` directly (C) hides the asynchronous case. (D) is nonsense.

4. **B** — Classic UI-thread deadlock. `LoadAsync` `await`s; the awaiter captures the UI's `SynchronizationContext`. The HTTP response arrives on a thread pool thread. The continuation tries to post back to the UI thread to resume the state machine. The UI thread is blocked on `.Result`. Deadlock. The fix is *not* `ConfigureAwait(false)` (although that would unblock this one); the fix is **never call `.Result`**. Make `OnButtonClick` `async void` (the one place `async void` is correct: event handlers).

5. **C** — `BoundedChannelFullMode.Wait` is back-pressure: writers wait when the buffer is full. `Drop*` modes (B) discard items instead of waiting — that is the *opposite* of back-pressure. Unbounded channels (A) never wait. (D) is false: `Channel.CreateBounded<T>(100)` *is* `BoundedChannelFullMode.Wait` by default.

6. **B** — In a fan-in scenario, the channel must be completed *exactly once*, *after all producers are done*. The clean pattern is `Task.WhenAll(producers).ContinueWith(_ => channel.Writer.Complete())` — possibly with `, TaskContinuationOptions.ExecuteSynchronously` for efficiency. Calling `Complete()` inside each producer's `finally` (A) causes the second-and-later producers' in-flight `WriteAsync` calls to throw `ChannelClosedException`. (C) is false: channels do not auto-complete. (D) is wrong: the producer must signal completion; the consumer cannot.

7. **B** — Without `[EnumeratorCancellation]` on the generator's `ct` parameter, the compiler has nothing to bind the consumer-side `WithCancellation(token)` to. The token is silently dropped. The loop continues. This is the single most-missed cancellation bug. The fix is one attribute: `[EnumeratorCancellation] CancellationToken ct = default`.

8. **B** — `Parallel.ForEachAsync` is the right tool for "iterate this collection in parallel with a fixed cap." It blocks until done, integrates with `CancellationToken`, and saves you from writing the channel/consumer wiring. The `Channel` approach (A) works but is over-engineered for batch processing. (C) saturates the pool — you wanted *at most 8*. (D) is sequential.

9. **C** — `OperationCanceledException` should propagate through every layer until it reaches the frame that owns the operation. That frame decides what to do (return 200 with partial results, log "cancelled", return a 408, exit the process). Catching it in the middle (A) silently turns cancellation into "success," hiding the fact that the operation never completed. Letting it escape the process (B) is acceptable in a console `Main` but wrong in ASP.NET (it would crash the request). (D) confuses the throw site with the catch site.

10. **A** — `CreateLinkedTokenSource(callerCt)` produces a new CTS whose token fires when either the caller's token fires or you call `cts.Cancel()` on it. `CancelAfter(TimeSpan)` schedules an automatic cancel after the delay. Now the inner call cancels for either reason — caller or timeout. Option C uses `Cancel` instead of `CancelAfter`, which would cancel immediately. (B) and (D) are not real APIs.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
