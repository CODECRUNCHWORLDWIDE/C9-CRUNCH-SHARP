# Week 8 — Exercise Solutions (annotated)

These are reference solutions, not the only correct ones. If your code differs but the acceptance criteria pass, your solution is valid. Read the annotations after you finish; the annotations are where the senior-engineer judgement lives.

---

## Exercise 1 — `SingleThreadSyncContext` and ConfigureAwait

### Reference solution

```csharp
public override void Post(SendOrPostCallback d, object? state)
{
    _queue.Add((d, state));
}

public override void Send(SendOrPostCallback d, object? state)
{
    if (Thread.CurrentThread.ManagedThreadId == _pump.ManagedThreadId)
    {
        d(state);
    }
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
```

### What the output proves

The output sequence — `Main 1, Pump 4, Posted 4, After1 4, After2 6, After3 6` — demonstrates three things:

1. **The first `await` resumed on thread 4 (the pump).** `SynchronizationContext.Current` was non-null at the `await` point, so the runtime captured it and posted the continuation back through `Post()`. The pump dequeued and ran it.
2. **The second `await` resumed on thread 6 (a `ThreadPool` worker).** `.ConfigureAwait(false)` set `continueOnCapturedContext: false` in the awaiter, so the runtime did *not* capture the SyncCtx. The continuation ran wherever `Task.Delay`'s timer callback dispatched it — typically a `ThreadPool` thread.
3. **The third `await` resumed on thread 6 (still the `ThreadPool`).** This is the reflection-question answer. At the third `await`, the calling thread is *not* on the pump; it is on the ThreadPool. There is no `SynchronizationContext.Current` on the ThreadPool worker (it is `null`). The captured-context rule found nothing to capture; the continuation ran on the ThreadPool by default.

### Reflection answers

**Q1.** The third await does not return to the pump because, after `.ConfigureAwait(false)` moved execution to the ThreadPool, the current SyncCtx became `null`. The third await captured `null`, which is the no-op case; the continuation ran on the ThreadPool. To return to the pump, you would need to manually call `SynchronizationContext.SetSynchronizationContext(myCtx)` at the top of the lambda after every `ConfigureAwait(false)`, which is what makes `.ConfigureAwait(false)` "sticky" in practice.

**Q2.** The captured-context rule:
1. Read `SynchronizationContext.Current`.
2. If non-null and not the default, capture it and post the continuation through it.
3. Otherwise, read `TaskScheduler.Current`.
4. If not `TaskScheduler.Default`, use it.
5. Otherwise, run the continuation on the ThreadPool.

**Q3.** With `SetSynchronizationContext(null)` at the start of the lambda, all three awaits resume on ThreadPool threads (because the SyncCtx capture is now `null` at every await). The output becomes `Main 1, Pump 4, Posted 4, After1 6, After2 6, After3 6`. This is the ASP.NET Core baseline behaviour — no SyncCtx anywhere, every continuation on ThreadPool.

---

## Exercise 2 — `IAsyncEnumerable` and `[EnumeratorCancellation]`

### Reference solution

```csharp
public static async IAsyncEnumerable<int> ReadNumbersAsync(
    int n = 1_000,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (int i = 0; i < n; i++)
    {
        await Task.Delay(10, ct).ConfigureAwait(false);
        yield return i;
    }
}
```

### Why it works

The `[EnumeratorCancellation]` attribute does *not* affect the generated state machine's body. What it does is mark the parameter so that the C#-emitted `GetAsyncEnumerator(CancellationToken)` overload combines the parameter's value with the value passed to `WithCancellation(...)` using `CancellationTokenSource.CreateLinkedTokenSource`. The combined token replaces the parameter inside the iterator at the *point of enumeration*.

Without the attribute, the parameter's value is bound when `ReadNumbersAsync(...)` is called. The token passed to `.WithCancellation` is held by the consumer's iterator-adaptor but never reaches the iterator's `ct` local — it only affects the await around `MoveNextAsync` itself. The `Task.Delay(10, ct)` inside the iterator keeps using whatever token was passed at call time.

### Reflection answers

**Q1.** Variant A still cancels (the token is passed at call time and reaches `Task.Delay` directly). Variant B does *not* cancel — the iterator's `ct` is `default`, so `Task.Delay` ignores cancellation entirely; the loop runs to N=1000. Variant C does not cancel via the inner CTS for the same reason; only the outer CTS would (and the test does not cancel it).

**Q2.** The CTS fires at 100ms. Each yield costs ~10ms (the `Task.Delay(10, ct)`). At 100ms the loop has run 9-10 iterations. The "around 9" answer reflects the small overhead per yield and the fact that `Task.Delay` can complete a few ms after the requested delay. If you tightened the assertion to "exactly 10," it would fail intermittently on a busy machine.

**Q3.** The `OperationCanceledException`'s `.CancellationToken` is the combined linked token (in Variants B and C), not the original `cts.Token`. The runtime constructs a linked CTS internally for the iterator's lifetime; the OCE carries that linked token. If you need to recover the original, save a reference to `cts.Token` in your outer scope and compare with `cts.IsCancellationRequested` in the catch filter (as shown in the production pattern in Lecture 1.9).

---

## Exercise 3 — Bounded channel pipeline

### Reference solution

The file as shipped is the reference solution. The four TODOs are filled in with the right shapes; you fill in only:

1. The channel options (capacity 8, `FullMode.Wait`, `SingleWriter=true`, `SingleReader=false`).
2. The producer body (write 0..99 with the timestamp log; `Writer.Complete()` in `finally`).
3. The consumer body (read via `ReadAllAsync`, simulate 5ms work, accumulate the sum).
4. The wait pattern (await producer; await all consumers).

### What the output proves

The expected timeline:

- The producer writes items 0..7 essentially instantly (capacity is 8, the channel accepts them without pending).
- Consumers begin processing items 0..3 in parallel; each takes ~5ms.
- The producer tries to write item 8. The channel is full (8 items buffered, no consumer has finished yet). `WriteAsync(8, ct)` pends.
- A consumer finishes processing item 0 around +5ms. A slot opens. `WriteAsync(8, ct)` resolves around +5..10ms.
- The producer logs "wrote 8 at +XX ms" — visibly later than the previous burst.
- From here, the producer's throughput is capped at the consumers' aggregate throughput (4 consumers × 200 items/s = 800 items/s; the producer's logged writes pace to that).

The "Sum across consumers: 4950" confirms every item was processed exactly once. The "PASS" at the end is the load test passing.

### Reflection answers

**Q1.** Items 0..7 are accepted immediately (channel goes from 0 → 8 items). Item 8 pends because the channel is full. Around +5ms a consumer finishes item 0 and reads it from the channel; the slot opens; item 8 is accepted; the producer logs and tries item 9; the channel is full again (consumer 0 is still working on item 4, others on items 1..3, plus items 5..8 buffered = 8 items + 4 in flight ≈ 12 in the system, but only 8 buffered). Item 9 pends until another consumer finishes. The pattern is "burst of 8, then one-at-a-time as consumers drain."

**Q2.** With capacity 100, the producer writes all 100 items without ever pending. The channel acts as a complete buffer; the producer finishes essentially in microseconds and `Writer.Complete()` runs before any consumer has finished its first item. The consumers then drain the buffered channel at their own pace. The end state is identical (sum == 4950), but there is no backpressure — the producer outran the consumers entirely. This is functionally equivalent to unbounded *for this workload*; for an infinite producer, the difference matters.

**Q3.** With 50ms per item, the producer's writes pace to the consumer throughput (4 × 20 items/s = 80 items/s). Each write after the first 8 is logged ~12.5ms apart (8 items per second per consumer × 4 consumers ≈ 1 item per 12.5ms). The total runtime stretches from ~250ms to ~1.5s. Tail latency dominates the wall-clock; the backpressure is now obvious in the log.

**Q4.** With `DropOldest`, the producer never pends — it always writes. When the channel is full at write of item 9, item 0 (or whichever is oldest in the buffer) is evicted. The consumer's `ReadAllAsync` skips the evicted item. The sum is no longer 4950; it is missing 8-12 items (the ones evicted before any consumer read them). For this exercise that is the wrong choice — `Wait` is correct because every item matters.

---

## Common mistakes across the three exercises

**Mistake 1: Forgetting `await using var done = new ManualResetEventSlim()` in Exercise 1's `Send`.**
A `ManualResetEventSlim` is `IDisposable`; `using` is required. Without it, the underlying kernel handle leaks. The exercise still runs (the handle is reclaimed at process exit), but in production code this is a slow leak.

**Mistake 2: Putting `[EnumeratorCancellation]` on the wrong parameter.**
The attribute must be on a `CancellationToken` parameter. The compiler enforces this — if you misplace it, you get `CS8424`. Inevitable in code review; the analyzer flags it.

**Mistake 3: Forgetting `Writer.Complete()` in Exercise 3.**
If the producer's loop finishes but `Complete()` never runs, `ReadAllAsync` waits forever for more items. The `dotnet run` hangs; you `Ctrl+C` and lose the partial output. Always `try { produce } finally { Complete() }`.

**Mistake 4: Using `cts.Cancel()` in the catch instead of letting cancellation propagate.**
In Exercise 3's "further exploration C," the right way to stop the pipeline on error is `cts.Cancel()` in the catch — which cancels the shared `ct` token. All other consumers' `ReadAllAsync(ct)` throw `OperationCanceledException` on their next iteration, draining the channel. The wrong way is `producer.Wait()` followed by `cts.Cancel()`, which has the producer drive cancellation directly — that creates an ordering bug where the consumers race against the producer's completion.

**Mistake 5: Using `.Result` to "simplify" the synchronisation in Main.**
Every solution above uses `await` exclusively. There is no `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()`. If you find yourself reaching for one, re-read Lecture 1, section 1.7. The right fix is upstream — propagate `async` rather than blocking on it.

---

## A note on the reference solutions' style

Every solution follows the eight-point checklist from Lecture 1.11:

- Method names end in `Async`.
- `CancellationToken` is the last parameter on every public method.
- Every internal `await` propagates the token.
- Every `await` in non-trivial code uses `.ConfigureAwait(false)` (we are writing example code that might be lifted into a library; the discipline is good practice).
- No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`.
- No `lock` blocks around `await`.
- `CancellationTokenSource` uses `using`.
- The code is small enough that explicit `Meter` instrumentation is omitted — but the mini-project adds it.

If a reviewer challenged any one of these in your code, the right response is the relevant lecture section, cited by number. Practise the citation; it is half the senior-engineer skill.
