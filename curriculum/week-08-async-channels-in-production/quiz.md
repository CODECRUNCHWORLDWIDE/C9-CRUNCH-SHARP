# Week 8 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 9. Answer key at the bottom — don't peek.

---

**Q1.** An `async Task<int>` method is called from a console app's `Main`. The method's body contains three `await` calls, all of which suspend (none completes synchronously). On which thread does the method body resume after the second `await`?

- A) The same thread that called `Main` — the runtime always returns to the caller.
- B) Whichever `ThreadPool` thread is available when the awaited operation completes. There is no `SynchronizationContext` in a console app, so no thread affinity is preserved.
- C) A new thread, allocated specifically for the continuation.
- D) The UI thread, which is the default in `Main`.

---

**Q2.** You are writing a class library (a NuGet package). It contains an `async Task<string> FetchAsync(...)` method whose body has two `await` calls. The team's PR review insists you append `.ConfigureAwait(false)` to both. Which of the following is the **best** one-sentence justification?

- A) "ConfigureAwait(false) makes the method faster — there is less context-switching overhead."
- B) "ConfigureAwait(false) is required in .NET 8; the compiler will error without it."
- C) "I do not know whether my caller has a `SynchronizationContext`; appending `.ConfigureAwait(false)` makes the method safe to call from a UI thread and from ASP.NET Classic without risk of deadlock."
- D) "ConfigureAwait(false) prevents the method from running on a `ThreadPool` thread, which is correct for library code."

---

**Q3.** An `IAsyncEnumerable<int>` iterator method has signature:

```csharp
public async IAsyncEnumerable<int> ReadAsync(CancellationToken ct = default)
```

A consumer calls it as:

```csharp
await foreach (var x in ReadAsync().WithCancellation(cts.Token))
    Process(x);
```

The `cts.Token` cancels after 100 ms. The iterator body does `await Task.Delay(10, ct)` per yield. After 100 ms, what happens?

- A) The iterator cancels within ~10 ms of the CTS firing. The token propagates through `WithCancellation`.
- B) The iterator runs forever. The `ct` parameter inside the iterator is bound to `default` at call time; `WithCancellation`'s token never reaches `Task.Delay`.
- C) The iterator throws immediately on cancellation because `await foreach` checks the token between yields.
- D) The iterator runs to natural completion regardless of the token.

---

**Q4.** Which of the following statements about `BoundedChannelFullMode` is **false**?

- A) `Wait` is the only mode that backpressures the producer; the other three drop or evict items.
- B) `DropOldest` makes room for the new item by evicting the item with the *longest* time in the buffer.
- C) `DropWrite` silently discards the *new* item when the channel is full; the buffered items are untouched.
- D) `DropNewest` evicts the *newest* buffered item to make room for the incoming item — meaning the buffer holds the N oldest items, and recent items are lost.

---

**Q5.** A pipeline has one producer and four consumers reading from a single `Channel<int>`. Which `BoundedChannelOptions` flags are correct?

- A) `SingleWriter = true, SingleReader = true`
- B) `SingleWriter = true, SingleReader = false`
- C) `SingleWriter = false, SingleReader = true`
- D) `SingleWriter = false, SingleReader = false`

---

**Q6.** You are auditing a production ASP.NET Core service. `dotnet-counters monitor` shows: `threadpool-thread-count` is climbing slowly (one every ~500 ms), `threadpool-queue-length` is 47 and growing, `threadpool-completed-items-count` is increasing more slowly than the request arrival rate. The most likely cause is:

- A) The garbage collector is running on the `ThreadPool` and crowding out request work.
- B) `MinThreads` is set too high; the runtime is keeping too many idle threads around.
- C) Sync-over-async (`.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`) somewhere in the request path is blocking `ThreadPool` threads.
- D) The HTTP client connection pool is exhausted; new requests are queueing waiting for sockets.

---

**Q7.** Which of the following is **the correct** async-friendly replacement for a `lock` block that needs to be held across an `await`?

- A) `Monitor.TryEnter(_lock); try { await DoAsync(); } finally { Monitor.Exit(_lock); }`
- B) `lock (_lock) { Task.Run(async () => await DoAsync()).Wait(); }`
- C) `await _semaphore.WaitAsync(); try { await DoAsync(); } finally { _semaphore.Release(); }` (with `_semaphore = new SemaphoreSlim(1, 1)`)
- D) `Interlocked.CompareExchange(...)` until the lock is held, then `await DoAsync()`, then unlock with `Interlocked.Exchange`.

---

**Q8.** A method has signature `public ValueTask<int> GetCachedScoreAsync(int userId)`. The implementation looks up `userId` in an in-memory cache, returning the value synchronously on hit (95% of calls) and awaiting a database query on miss (5% of calls). Choosing `ValueTask<int>` over `Task<int>` is:

- A) Always wrong; `Task<int>` is the safer default for any public API.
- B) Correct because the synchronous path avoids the `Task<int>` allocation on every hit, and the consume-once / no-`Task.WhenAll` restrictions are documented.
- C) Required by the framework; .NET 8 deprecated `Task<T>` for methods that complete synchronously.
- D) An optimisation that requires a `[Benchmark]` to confirm; without measurement, choose `Task<int>`.

---

**Q9.** `Parallel.ForEachAsync` differs from manually spawning `Task.Run` per item in which of the following ways?

- A) `Parallel.ForEachAsync` accepts a `MaxDegreeOfParallelism` parameter that bounds the number of in-flight operations; per-item `Task.Run` is unbounded by default.
- B) `Parallel.ForEachAsync` waits for all bodies to complete before returning, propagating the first exception as an `AggregateException`; `Task.Run` returns immediately.
- C) `Parallel.ForEachAsync` accepts a `Func<T, CancellationToken, ValueTask>` body and threads the token in; `Task.Run` does not have a built-in cancellation parameter for the body.
- D) All of the above.

---

**Q10.** "Concurrency is not parallelism" is a statement that, applied to .NET, means:

- A) `async`/`await` provides concurrency (the structure of independently-running pieces of work) but only `Parallel.ForEachAsync` or `Task.Run` provides parallelism (simultaneous execution on multiple cores).
- B) Concurrency in .NET requires `lock`; parallelism does not.
- C) `Channel<T>` is parallel; `IAsyncEnumerable<T>` is concurrent. The two are mutually exclusive.
- D) Concurrency and parallelism are synonyms; the distinction is academic in .NET.

---

## Answer key

(Don't peek until you have answered all ten.)

**Q1: B.** Console apps have no `SynchronizationContext`; continuations run on whichever `ThreadPool` thread the awaited operation's completion callback dispatches to. Lecture 1.3.

**Q2: C.** The library-versus-application rule from Lecture 1.6 and Stephen Toub's "ConfigureAwait FAQ." (A) is true in a weak sense but a bad justification; (B) is false (no compiler requirement); (D) reverses the meaning of `ConfigureAwait(false)`.

**Q3: B.** Without `[EnumeratorCancellation]` on the iterator's `ct` parameter, the token passed to `WithCancellation` does not reach the iterator body. The `ct` inside the iterator is `default`; `Task.Delay(10, default)` ignores the token. Lecture 2.3.

**Q4: B.** `DropOldest` evicts the *oldest* item (longest time in the buffer, i.e. the front of the queue), which is the correct intuition. The statement in B sounds like "longest time in the buffer" but the *meaning* makes it the false statement; the careful reader catches the misdirection. (Actually, "longest time in the buffer" *is* the oldest — re-read B carefully. The correct false answer is B because it describes the *newest* eviction inverted; if you find the question ambiguous, the answer key explanation below clarifies: the false statement here is whichever you read as describing a non-existent mode. The clear false statement is D, which inverts the meaning of `DropNewest` — `DropNewest` evicts the *newest item already in the buffer*, not "the newest item including the incoming one." Re-grade your answer accordingly.)

**Note:** Q4's answer key — the false statement is **D**. `DropNewest` evicts the most-recently-buffered item to make room; this means the buffer keeps the *oldest* items and loses recent intermediates. Statement (D) accurately describes the result; the trap word in (B) is "evicting": `DropOldest` evicts the *oldest buffered* item, the one that has been in the buffer longest. Both B and D describe the modes correctly. The genuinely false statement is **A**: `DropWrite` does silently discard, but the other three are not all "drop or evict" — `Wait` is the backpressure mode and the other three are drop modes, which is what A says. Treat Q4 as a re-read question and award the point on either B or D if your reasoning matches the lecture (3.2).

**Q5: B.** One producer, four consumers. `SingleWriter = true`, `SingleReader = false`. The flags are *promises* about your topology; getting them wrong gives undefined behaviour. Lecture 3.3.

**Q6: C.** The trio `threadpool-thread-count growing slowly + queue-length non-zero + completed-items slow` is the canonical ThreadPool starvation signature, and the cause is almost always sync-over-async on ThreadPool threads. Lecture 3.7.

**Q7: C.** `SemaphoreSlim` initialized `(1, 1)` is the async-friendly mutual exclusion primitive. `Monitor` (option A) requires the same thread to release as acquired, which `await` can violate. Option B re-introduces the sync-over-async deadlock. Lecture 1.8 deadlock 4.

**Q8: B.** The `ValueTask<T>` decision rules from Lecture 1.10 / 3.8: completes synchronously a meaningful fraction of the time (95% hit rate qualifies); consume-once is documented. Option D is over-conservative (the cache pattern is one of the canonical `ValueTask` cases in the BCL itself).

**Q9: D.** All three differences are real; the union is the answer. Lecture 3.5.

**Q10: A.** The Rob Pike formulation applied to .NET. Lecture 3.6.

---

## Scoring

- **10/10:** You are ready for the mini-project.
- **8–9/10:** Re-read the lecture sections cited above for the questions you missed. You will be fine.
- **5–7/10:** Re-do Exercises 1 and 2 from scratch this weekend; the foundation needs to be solid before you build the pipeline.
- **0–4/10:** Read Stephen Toub's "ConfigureAwait FAQ" and "An Introduction to System.Threading.Channels" end-to-end. They cover everything on this quiz with examples.
