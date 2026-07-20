# Week 4 Homework

Six practice problems that revisit the week's topics. The full set should take about **6 hours**. Work in your Week 4 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — Read the state-machine IL

**Problem statement.** Scaffold a fresh `dotnet new console` project. Write the smallest interesting async method:

```csharp
public static async Task<int> SumAfterDelaysAsync(int a, int b)
{
    await Task.Delay(100);
    var x = a + b;
    await Task.Delay(50);
    return x * 2;
}
```

Build in `Debug`, then decompile the resulting DLL with `ildasm` (or `ILSpy`, or `dotPeek`). Identify the state-machine struct the compiler emitted and write a `notes/state-machine.md` file containing:

1. The **name** of the generated struct (it will look like `<SumAfterDelaysAsync>d__0`).
2. The **fields** the compiler emitted, with a short comment for each ("the hoisted local `x`", "the awaiter cache for the first `await`", etc.).
3. The **states** the `MoveNext` switch dispatches on — there are exactly **three** (`-1` initial, `0` after the first `await`, `1` after the second `await`, plus the terminal `-2`).
4. One paragraph (4–6 sentences) explaining *where the continuation runs* after each `await`, given a console app (no `SynchronizationContext`).

**Acceptance criteria.**

- File `notes/state-machine.md` exists with the four sections labelled.
- Each section references the actual decompiled output, not generic prose.
- File committed.

**Hint.** Run `dotnet build -c Debug` and look in `bin/Debug/net9.0/`. The decompiler will show types in the *containing* class; the state machine is a private nested struct on whatever class holds `SumAfterDelaysAsync`. If you're on macOS or Linux without `ildasm`, install ILSpy via VS Code's extension or use `dotnet tool install -g ilspycmd`.

**Estimated time.** 30 minutes.

---

## Problem 2 — `ValueTask<T>` vs `Task<T>` benchmark

**Problem statement.** Write a small `BenchmarkDotNet` harness that compares three implementations of "an in-memory cache lookup":

1. `public Task<int> Lookup_Task(int key)` returning `Task.FromResult(...)` on hit and `Task.Run(...)` on miss.
2. `public ValueTask<int> Lookup_ValueTask(int key)` returning `new ValueTask<int>(v)` on hit and a wrapped `Task<int>` on miss.
3. `public int Lookup_Sync(int key)` returning the value directly (no async at all) — the baseline.

Pre-populate a 1,000-entry dictionary. Run with hit ratios of 100%, 50%, and 0%. Report:

| Hit ratio | `Lookup_Task` | `Lookup_ValueTask` | `Lookup_Sync` |
|-----------|---------------|--------------------|---------------|
| 100% | ? ns / ? B | ? ns / ? B | ? ns / 0 B |
| 50% | ... | ... | ... |
| 0% | ... | ... | ... |

Write `notes/valuetask-vs-task.md` with the table and a paragraph of analysis.

**Acceptance criteria.**

- `dotnet run -c Release` produces a `BenchmarkDotNet` summary.
- The summary table is committed in `notes/valuetask-vs-task.md`.
- The analysis paragraph explicitly answers: "At what hit ratio does `ValueTask<T>` stop being worth it?"
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** Use `[MemoryDiagnoser]` on the bench class. Use `[Params(0, 50, 100)]` for the hit ratio. The miss path's `Task.Run` should `await Task.Yield()` and return the value, so all three variants have the same observable result.

**Estimated time.** 1 hour.

---

## Problem 3 — Fan-out with `Channel<T>`

**Problem statement.** Build a `console` app that reads up to 1,000 integers from `stdin`, enqueues them into a `Channel.CreateBounded<int>(capacity: 50, FullMode = Wait)`, and processes them with 4 concurrent consumers. Each consumer:

- Reads from the channel via `await foreach (var n in reader.ReadAllAsync(ct))`.
- "Processes" the integer by computing `Thread.SpinWait(1_000_000)` (CPU-bound work, simulated).
- Writes a single line to a thread-safe `ConcurrentBag<(int worker, int value)>`.

After the input is exhausted, the program prints:

- Total items processed.
- Per-worker count.
- Wall-clock duration.
- Observed throughput (items/sec).

Add a `--unbounded` flag that switches to `Channel.CreateUnbounded<int>()`. Compare the two runs on a synthetic input of 100,000 integers (generate them with `seq 1 100000 | dotnet run`); document the memory difference in `notes/bounded-vs-unbounded.md`.

**Acceptance criteria.**

- Both `--bounded` (default) and `--unbounded` produce identical totals.
- Per-worker counts are roughly balanced — no worker has more than 2x another worker's count.
- `notes/bounded-vs-unbounded.md` reports the peak working-set difference (visible in Task Manager / `top` / `dotnet-counters`).
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** For the memory measurement, run with `dotnet counters monitor --process-id <pid>` and watch `working-set`. The bounded run should plateau around the buffer capacity; the unbounded run grows linearly with input rate until the producer is exhausted.

**Estimated time.** 1 hour.

---

## Problem 4 — `IAsyncEnumerable<T>` streaming endpoint

**Problem statement.** Take Week 3's `Ledger.Api` mini-project (or scaffold a minimal copy with the same `LedgerDbContext` and `Transaction` entity). Add a new endpoint:

```
GET /api/v1/transactions/stream?afterId=0
```

The endpoint should:

- Return `IAsyncEnumerable<TransactionDto>` directly (Minimal APIs can serialize this as JSON-streaming).
- Read from EF Core via `db.Transactions.AsNoTracking().Where(t => t.Id > afterId).AsAsyncEnumerable()`.
- Project to `TransactionDto` lazily (one allocation per row, no `ToListAsync`).
- Accept `HttpContext.RequestAborted` and propagate it into the EF query.
- Be tested by an integration test that calls the endpoint, reads 100 of 10,000 rows, and then closes the response — and *proves* via `LogTo` that the SQL query was cancelled mid-stream.

**Acceptance criteria.**

- New endpoint exists and returns `IAsyncEnumerable<TransactionDto>`.
- The integration test passes; the log contains the cancelled-query line ("DbCommand cancelled").
- The endpoint emits `Content-Type: application/x-ndjson` or `Transfer-Encoding: chunked` (verify with `curl -v`).
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** Minimal APIs serialize `IAsyncEnumerable<T>` as a JSON array, streamed. The `LogTo(Console.WriteLine, LogLevel.Information)` on `LedgerDbContext` will print the cancelled-query line at `Microsoft.EntityFrameworkCore.Database.Command[20104]` when the consumer drops the connection mid-stream.

**Estimated time.** 1 hour 15 minutes.

---

## Problem 5 — Composed cancellation with `Polly`-style policies

**Problem statement.** Write a `RetryWithTimeout` helper that runs an async delegate with both a **per-attempt timeout** and an **overall timeout**:

```csharp
public static async Task<T> RetryWithTimeoutAsync<T>(
    Func<CancellationToken, Task<T>> work,
    int maxAttempts,
    TimeSpan perAttemptTimeout,
    TimeSpan overallTimeout,
    CancellationToken callerCt = default);
```

Semantics:

- The overall timeout is composed with the caller's token via `CreateLinkedTokenSource`.
- Each attempt gets its own `CreateLinkedTokenSource(overallCt)` with `CancelAfter(perAttemptTimeout)`.
- Between attempts, await an exponential backoff of `Task.Delay(100ms * 2^attempt, overallCt)`.
- After `maxAttempts` or when the overall token fires, throw `TimeoutException` *only* if the overall timeout was the reason; otherwise propagate the caller's `OperationCanceledException`.

Test with xUnit:

1. A delegate that succeeds on the first attempt — returns immediately.
2. A delegate that fails (throws) twice then succeeds — returns after two retries.
3. A delegate that always exceeds the per-attempt timeout — throws `TimeoutException`.
4. A delegate that exceeds the overall timeout mid-retry — throws `TimeoutException` with the right message.
5. A caller cancellation mid-retry — throws `OperationCanceledException`, *not* `TimeoutException`.

**Acceptance criteria.**

- All five xUnit tests pass.
- The helper uses `CreateLinkedTokenSource` correctly — verifiable by reading the code; no `Task.WhenAny` shortcuts.
- The `catch ... when` exception filter is used to distinguish the two reasons for cancellation.
- `dotnet test`: all passing.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** The cleanest implementation has *two* `using var` CTS scopes — one for `overallCt` (linked to caller), one inside the retry loop for `attemptCt` (linked to overall, then `CancelAfter`).

**Estimated time.** 1 hour 30 minutes.

---

## Problem 6 — Mini reflection essay

**Problem statement.** Write a 300–400 word reflection at `notes/week-04-reflection.md` answering:

1. The lecture argues that "`async` is a compiler transform, not a runtime feature." After a week of reading state-machine IL and watching `await` resume on whichever thread the runtime picked, do you agree? Cite one concrete example from your homework where the difference between *compiler* and *runtime* mattered.
2. If you have written async code in Go (goroutines + channels), Rust (`tokio` + `mpsc`), or Python (`asyncio`), where does .NET's model feel familiar and where does it feel different? One example of each.
3. Which primitive did you reach for most this week — `Task.WhenAll`, `Task.WhenEach`, `IAsyncEnumerable<T>`, `Channel<T>`, or `Parallel.ForEachAsync` — and why? Is that the same one you would reach for first in a production codebase?
4. What's one thing you'd want to learn next that this week didn't cover? (`System.IO.Pipelines`? `BackgroundService`? Akka.NET? Lock-free data structures?)

**Acceptance criteria.**

- File exists, 300–400 words.
- Each numbered question is addressed in its own paragraph.
- File is committed.

**Hint.** This is for *you*, not for a grade. Be honest. Future-you reading it after Week 8 (when you've shipped a real `BackgroundService`-backed service) will be grateful.

**Estimated time.** 30 minutes.

---

## Time budget recap

| Problem | Estimated time |
|--------:|--------------:|
| 1 | 30 min |
| 2 | 1 h 0 min |
| 3 | 1 h 0 min |
| 4 | 1 h 15 min |
| 5 | 1 h 30 min |
| 6 | 30 min |
| **Total** | **~5 h 45 min** |

When you've finished all six, push your repo and open the [mini-project](./mini-project/README.md). The mini-project takes everything you've practiced this week and builds a typed async crawler that streams `CrawlResult` over `IAsyncEnumerable<T>`, throttles fan-out with `Channel<Uri>`, and shuts down cleanly under both Ctrl-C and a server-side timeout — every concept from this week, applied end-to-end on a domain you can run in 5 minutes.
