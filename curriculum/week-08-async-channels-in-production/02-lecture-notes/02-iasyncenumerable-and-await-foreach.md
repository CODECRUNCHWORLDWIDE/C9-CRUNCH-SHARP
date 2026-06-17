# Lecture 2 — IAsyncEnumerable, await foreach, and Streaming-Async

> **Reading time:** ~55 minutes. **Hands-on time:** ~35 minutes (you write a streaming iterator and consume it with `await foreach`).

`IAsyncEnumerable<T>` is the type you reach for when an `async` method needs to return *many* values over time rather than one value at the end. Week 4 used `Task<List<T>>` for query results, which is fine for small queries — fetch a page of rows, return them as a list. Production code routinely needs the other shape: stream a million rows from a database without ever holding more than one row in memory; tail a log file and yield each new line; read frames from a websocket until the client disconnects. The C# 8 feature for that shape is `IAsyncEnumerable<T>`, consumed with `await foreach`. This lecture covers the surface, the lowering, the `[EnumeratorCancellation]` rule, and the production patterns that make a streaming iterator correct under load.

## 2.1 — The shape

```csharp
public async IAsyncEnumerable<Row> ReadRowsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await using var reader = await _conn.OpenReaderAsync(ct).ConfigureAwait(false);
    while (await reader.ReadAsync(ct).ConfigureAwait(false))
    {
        yield return reader.CurrentRow;
    }
}

// Consumer
await foreach (var row in ReadRowsAsync(ct).ConfigureAwait(false))
{
    Process(row);
}
```

Three pieces deserve attention:

1. The method body contains *both* `await` and `yield return`. The C# 8 compiler permits this combination by generating a state machine that implements `IAsyncEnumerator<T>` directly, with `MoveNextAsync` returning a `ValueTask<bool>`. The generated state machine is conceptually the union of the `async` state machine from lecture 1 and the synchronous `yield`-based enumerator that has existed since C# 2.
2. The `[EnumeratorCancellation]` attribute marks the parameter to which `await foreach`'s `.WithCancellation(...)` extension should be wired. Without the attribute, the parameter sits there unused — a quiet bug. With it, the C# compiler emits code in the iterator's lowered `GetAsyncEnumerator` that combines the caller's `WithCancellation` token with any token already passed at method invocation.
3. The consumer side `.ConfigureAwait(false)` applies to *every* internal `await` performed by `await foreach` — the `MoveNextAsync` calls and the `DisposeAsync` call. There is a corresponding `ConfiguredCancelableAsyncEnumerable<T>` type that returns from the `.ConfigureAwait` and `.WithCancellation` chain; the type composes both options.

If you have only seen `async Task<List<T>>` before, the immediate question is "why bother?" The answer is *memory*. A `Task<List<T>>` materialises every element before returning; `IAsyncEnumerable<T>` materialises one element at a time. For a query returning one million rows, the difference is between holding one million row objects in memory (gigabytes) and holding one row at a time (kilobytes). Streaming is the only correct shape for unbounded or large-bounded results.

## 2.2 — The interfaces

```csharp
public interface IAsyncEnumerable<out T>
{
    IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
}

public interface IAsyncEnumerator<out T> : IAsyncDisposable
{
    T Current { get; }
    ValueTask<bool> MoveNextAsync();
}

public interface IAsyncDisposable
{
    ValueTask DisposeAsync();
}
```

Four observations:

- **`GetAsyncEnumerator` takes a `CancellationToken`.** This is the parameter `[EnumeratorCancellation]` wires into the iterator method. When the consumer writes `await foreach (var x in source.WithCancellation(ct))`, the `ct` flows through `GetAsyncEnumerator(ct)` into the iterator's body.
- **`MoveNextAsync` returns `ValueTask<bool>`.** Each step is a separate awaitable. The `ValueTask` choice is deliberate: for an iterator that yields synchronously most of the time (e.g., yields buffered rows from an in-memory pre-fetch), `MoveNextAsync` returns synchronously without allocating a `Task`. For an iterator that suspends on I/O each step, the `ValueTask` wraps a `Task` and you pay one allocation per yield.
- **`Current` is *not* awaitable.** It is a plain property that returns the value produced by the most recent successful `MoveNextAsync`. Reading `Current` before `MoveNextAsync` has returned `true` is undefined behaviour. Reading it after `MoveNextAsync` has returned `false` is undefined behaviour.
- **`DisposeAsync` runs on `await foreach` exit**, including exit via exception. Inside the iterator, code in `finally` blocks runs when `DisposeAsync` is invoked. This is the streaming-async equivalent of the synchronous `IEnumerator.Dispose` contract.

`MoveNextAsync` returning `ValueTask<bool>` is the most-cited reason `ValueTask` exists at all. Re-read the `ValueTask` consumption rules from lecture 1.10 — they apply to every iteration of an `await foreach`.

## 2.3 — Why `[EnumeratorCancellation]` is load-bearing

Consider the wrong shape:

```csharp
// WRONG: ct is captured in the iterator body but never received from the caller
public async IAsyncEnumerable<Row> ReadRowsAsync(CancellationToken ct)
{
    await using var reader = await _conn.OpenReaderAsync(ct).ConfigureAwait(false);
    while (await reader.ReadAsync(ct).ConfigureAwait(false))
    {
        yield return reader.CurrentRow;
    }
}

// Consumer
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var row in ReadRowsAsync(cts.Token))
{
    Process(row);
}
```

This looks correct and is in fact correct *for this caller*. The trap appears when a different caller writes:

```csharp
await foreach (var row in ReadRowsAsync(CancellationToken.None).WithCancellation(cts.Token))
{
    Process(row);
}
```

The intent — "iterate, cancel after 30s" — is broken. The `cts.Token` passed to `.WithCancellation(...)` flows into `GetAsyncEnumerator(token)`, but the iterator method's `ct` parameter was already bound to `CancellationToken.None` at call time. The `ct` in the iterator body is `CancellationToken.None`. The 30-second timeout never reaches `reader.ReadAsync`. The iterator loops forever.

`[EnumeratorCancellation]` fixes this. With the attribute, the compiler-generated state machine merges the parameter with the token from `GetAsyncEnumerator`:

```csharp
public async IAsyncEnumerable<Row> ReadRowsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    // ct inside the body is now the combined token: caller's parameter || GetAsyncEnumerator's
    await using var reader = await _conn.OpenReaderAsync(ct).ConfigureAwait(false);
    while (await reader.ReadAsync(ct).ConfigureAwait(false))
    {
        yield return reader.CurrentRow;
    }
}
```

Both call sites now behave correctly:

```csharp
// Both work:
await foreach (var row in ReadRowsAsync(cts.Token)) { ... }
await foreach (var row in ReadRowsAsync(CancellationToken.None).WithCancellation(cts.Token)) { ... }
await foreach (var row in ReadRowsAsync(otherCt).WithCancellation(cts.Token)) { ... }
// In the third case, ct inside the iterator is the combined OR of otherCt and cts.Token
```

The rule: **every `async IAsyncEnumerable<T>` method must mark its `CancellationToken` parameter with `[EnumeratorCancellation]`, period**. If you forget, Roslyn analyzer `CA2016` (`ForwardCancellationTokenToInvocations`) will warn; treat the warning as an error. The Microsoft Learn reference is at <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.enumeratorcancellationattribute>.

## 2.4 — `await foreach` and its options

The full surface of `await foreach`:

```csharp
await foreach (var x in source.WithCancellation(ct).ConfigureAwait(false))
{
    // body
}
```

Both `.WithCancellation` and `.ConfigureAwait` are extension methods on `IAsyncEnumerable<T>`. They return a `ConfiguredCancelableAsyncEnumerable<T>` value type. The `await foreach` syntax recognises this type and uses it appropriately. The order of the two methods does not matter — `.WithCancellation(ct).ConfigureAwait(false)` and `.ConfigureAwait(false).WithCancellation(ct)` produce equivalent state machines.

Three legitimate consumer patterns:

**Pattern A: Application code, no library boundary.**

```csharp
// Inside an ASP.NET Core controller — SyncCtx is null, .ConfigureAwait(false) is a no-op
await foreach (var row in _service.GetRowsAsync(ct))
{
    await _writer.WriteAsync(row, ct);
}
```

**Pattern B: Library code, ConfigureAwait everywhere.**

```csharp
// Inside a class library — append .ConfigureAwait(false) on the enumerable
await foreach (var row in _service.GetRowsAsync(ct).ConfigureAwait(false))
{
    await Sink(row).ConfigureAwait(false);
}
```

**Pattern C: External cancellation grafted on at the call site.**

```csharp
// The producer wasn't given a token at construction; we apply one at iteration
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var row in _service.GetRowsAsync()
                                  .WithCancellation(cts.Token)
                                  .ConfigureAwait(false))
{
    Process(row);
}
```

Pattern C is the one that breaks without `[EnumeratorCancellation]`. The fix is on the producer's side, not the consumer's; this is why the producer-side discipline matters.

## 2.5 — The "buffering versus streaming" decision

When you write a method that returns many values, you choose between three shapes:

- **`Task<List<T>>`** — buffer all values, return as a list. Eager.
- **`IEnumerable<T>` from an iterator method (`yield return` without `await`)** — synchronous streaming. Caller iterates lazily, but every yield is synchronous; no I/O can happen.
- **`IAsyncEnumerable<T>`** — asynchronous streaming. Caller iterates lazily; each yield may suspend on I/O.

The decision matrix:

| Situation | Right shape |
|---|---|
| Result is small (≤ 1,000 items) and known to fit in memory | `Task<List<T>>` |
| Result is small but is produced by a sequence of small async calls | `Task<List<T>>` (just `await` them all) |
| Result is large or unbounded, produced synchronously from cached data | `IEnumerable<T>` |
| Result is large or unbounded, each item produced via async I/O | `IAsyncEnumerable<T>` |
| Result is potentially infinite (e.g., websocket frames until disconnect) | `IAsyncEnumerable<T>` |
| Caller will probably consume only the first N items | `IAsyncEnumerable<T>` (lazy yields the rest) |

The most common mistake is using `Task<List<T>>` for a result that turns out to be large in production. The method works in dev with 100 rows; ships; one day a customer has 10 million rows; the process OOMs. The senior move is to err on the side of `IAsyncEnumerable<T>` whenever the result might grow beyond your design assumptions. Callers that want a list call `await source.ToListAsync(ct)` (from `System.Linq.Async`) and accept the memory cost explicitly.

## 2.6 — Composing async streams

`IAsyncEnumerable<T>` composes through extension methods. The official `System.Linq.Async` NuGet package (separate from the core BCL because the team did not want to ship every LINQ operator with a new "Async" suffix in `System.Linq`) provides the operators you expect: `Where`, `Select`, `Take`, `Skip`, `Aggregate`, `ToListAsync`. Each operator returns a new `IAsyncEnumerable<T>`. The composition is lazy:

```csharp
IAsyncEnumerable<Row> rows = _service.GetRowsAsync(ct);
IAsyncEnumerable<Order> orders = rows
    .Where(r => r.IsOrder)
    .Select(r => new Order(r.Id, r.Amount));

await foreach (var order in orders.WithCancellation(ct).ConfigureAwait(false))
{
    // each yield pulls one row, filters, projects, returns
}
```

For the bulk of this week's exercises we will avoid `System.Linq.Async` (we do not want to add a NuGet dependency to every exercise) and write the equivalent loops by hand. The mini-project may use it where the syntactic clarity matters; the choice is documented in the spec.

## 2.7 — A worked example: streaming a database query

Imagine a method that paginates through a database table:

```csharp
public async IAsyncEnumerable<Row> ReadRowsAsync(
    string tableName,
    int pageSize = 1_000,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    long offset = 0;
    while (true)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<Row> page = await _db.QueryAsync<Row>(
            $"SELECT * FROM {tableName} ORDER BY id LIMIT @limit OFFSET @offset",
            new { limit = pageSize, offset },
            ct).ConfigureAwait(false);

        if (page.Count == 0)
            yield break;

        foreach (var row in page)
            yield return row;

        offset += page.Count;
    }
}
```

Observations:

- One database round-trip per page. The caller receives one row at a time, but the producer fetches `pageSize` at a time. This is the standard "buffered streaming" pattern — page-fetch from the source, single-yield to the consumer.
- `ct.ThrowIfCancellationRequested()` is checked once per page, not once per yield. The page is the unit of work; cancelling mid-yield is fine, but adding a per-yield check is overhead.
- `yield break` exits the iterator cleanly when the database returns an empty page. The `await using var` for any disposable resources runs at this point.
- The method *never* materialises a list of more than `pageSize` rows. Memory is `O(pageSize)`, not `O(total rows)`.

If the caller wants exactly the first 100 rows, they write `await foreach (var row in ReadRowsAsync("orders").WithCancellation(ct).Take(100))` (with `System.Linq.Async`) or terminate the loop manually with `break`. Either way, the producer only fetches as many pages as needed; the laziness is end-to-end.

## 2.8 — Errors and `DisposeAsync`

Throwing inside an iterator behaves the way you would expect from synchronous `yield`:

```csharp
public async IAsyncEnumerable<int> CountAsync([EnumeratorCancellation] CancellationToken ct = default)
{
    for (int i = 0; i < 100; i++)
    {
        if (i == 50)
            throw new InvalidOperationException("halfway crash");
        yield return i;
        await Task.Delay(10, ct).ConfigureAwait(false);
    }
}

// Consumer
try
{
    await foreach (var x in CountAsync(ct)) Console.WriteLine(x);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Caught: {ex.Message}");  // "halfway crash"
}
```

The exception surfaces at the consumer's `MoveNextAsync` (logically, at the `await foreach` line). The consumer's `catch` runs, then `DisposeAsync` runs on the enumerator — any `finally` block inside the iterator executes at that point. The result is correct cleanup even on exception.

Cancellation behaves similarly but with `OperationCanceledException`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
try
{
    await foreach (var x in CountAsync(cts.Token)) Console.WriteLine(x);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine("Cancelled.");
}
```

Two cleanup rules:

1. **`finally` in the iterator runs on `DisposeAsync`.** Anything you opened with `await using var` is properly closed even when the consumer breaks early or cancels.
2. **The consumer's `await foreach` *itself* calls `DisposeAsync` on exit.** You do not need to call it manually. Calling it twice is permitted (the second call is a no-op for well-behaved iterators) but not idiomatic.

## 2.9 — Performance notes

`IAsyncEnumerable<T>` is not free. Three costs to keep in mind:

**Cost 1: State machine allocation.** The iterator method's state machine is allocated on the heap the first time it is enumerated (the compiler-generated `GetAsyncEnumerator` returns a fresh instance per call). For 100 enumerations a day this is irrelevant. For 100,000 enumerations a day in a hot loop it shows up. Reuse the enumerable across calls when possible; do not allocate a new one per consumer if the data is the same.

**Cost 2: `ValueTask<bool>` per `MoveNextAsync`.** Synchronous yields (the buffered-page case in 2.7) are cheap — the `ValueTask<bool>` does not allocate. Asynchronous yields (one I/O round-trip per item) allocate one `Task<bool>` per yield. If you find yourself yielding one item per network call, ask whether the source could batch.

**Cost 3: Cancellation registration overhead.** Each `ReadAsync(ct)` registers and unregisters a callback on `ct`. For tight inner loops with millions of yields, the registration overhead becomes visible. The mitigation is to check `ct.IsCancellationRequested` in a `while` condition (cheap) and only pass `ct` to the inner async operations that actually need it.

In practice, these three costs are dominated by the I/O cost in any realistic production scenario, and the ergonomic win of streaming-async is decisive. The benchmark-and-optimise habit from Week 7 still applies: write the natural shape, profile, optimise only the paths that show up.

## 2.10 — Common bugs

**Bug 1: Forgetting `[EnumeratorCancellation]`.** Covered in 2.3. The Roslyn analyzer catches it; treat the warning as an error.

**Bug 2: Yielding inside `try` without `finally`.** If an exception fires after a `yield return`, the resources opened before the yield may not be disposed. Always use `await using var` for `IAsyncDisposable` resources; do not try to manage `DisposeAsync` manually.

**Bug 3: Re-enumerating without re-allocating.** `IAsyncEnumerable<T>` produced by a `yield`-iterator is *single-use per `GetAsyncEnumerator()` call*. Calling `await foreach` twice on the same `IAsyncEnumerable<T>` produces two independent enumerators that each replay the iterator body. If the body has side effects (e.g., re-reads the database), the side effects happen twice. This is the same gotcha as synchronous `IEnumerable<T>`; the rule is the same: if you need to iterate twice, materialise once with `ToListAsync` (and accept the memory cost).

**Bug 4: Mixing `await foreach` with `Parallel.ForEachAsync` incorrectly.** `Parallel.ForEachAsync` accepts `IAsyncEnumerable<T>` since .NET 6. The interaction is correct, but the consumer must understand that `Parallel.ForEachAsync` will pull from the iterator concurrently up to `MaxDegreeOfParallelism`. If your iterator is not safe to enumerate concurrently — and most are not — wrap it with a `Channel<T>` first. Lecture 3 covers the pattern.

**Bug 5: Not propagating cancellation through `await using`.** If you write `await using var x = new ResourceAsync()`, the `DisposeAsync` call at scope exit does *not* automatically receive the iterator's cancellation token. If the resource's `DisposeAsync` is long-running and you want it cancellable, you have to express it manually — `await x.DisposeAsync(ct)` requires the resource to expose a token-accepting overload, which most do not. The .NET design choice is that dispose-on-cancellation is best-effort; do not block important cancellation paths behind a `DisposeAsync` that ignores the token.

## 2.11 — Exercise preview and recap

Exercise 2 of this week has you write a streaming `IAsyncEnumerable<int>` that produces values from a slow source, accepts `[EnumeratorCancellation] CancellationToken ct = default`, threads `ct` through every internal `await`, and is consumed three different ways (parameter, `.WithCancellation`, both). You will prove with a 100-ms `CancellationTokenSource` timeout that cancellation reaches the iterator within one yield's worth of work in every variant. That exercise is the smallest possible end-to-end demonstration of every concept in this lecture.

Lecture 3 takes the same pipeline shape and extends it: producer feeding multiple consumers via a bounded channel, with backpressure, with `Parallel.ForEachAsync` as an alternative, with `Meter` instrumentation, with `ThreadPool`-starvation diagnostics. That lecture closes the week's primitives; the mini-project then asks you to deploy them.

Before lecture 3, read Stephen Toub's "An Introduction to System.Threading.Channels" (<https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/>). Sixty minutes; it is the canonical reference.
