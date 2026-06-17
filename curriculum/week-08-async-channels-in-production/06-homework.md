# Week 8 — Homework

Six practice problems. Allocate roughly 1 hour per problem; the last two are longer and may need 90 minutes. Submit one .zip of code + a single `homework.md` write-up. Rubric at the bottom.

---

## Problem 1 — ConfigureAwait audit of a small library (60 min)

Take the small library `MiniHttp` below. Audit every `await` and append `.ConfigureAwait(false)` where appropriate, leaving none where the omission is defensible. Justify each decision in a comment on the line.

```csharp
#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiniHttp;

public sealed class JsonClient
{
    private readonly HttpClient _http;
    public JsonClient(HttpClient http) => _http = http;

    public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct = default)
    {
        var body = await GetStringAsync(url, ct);
        return System.Text.Json.JsonSerializer.Deserialize<T>(body);
    }

    public async Task<T?> RetryAsync<T>(Func<Task<T?>> op, int attempts = 3)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { return await op(); }
            catch when (i < attempts - 1) { await Task.Delay(100 * (i + 1)); }
        }
        return default;
    }
}
```

**Deliverable:** the modified file + a one-paragraph note explaining the rule you applied (library code → ConfigureAwait(false) everywhere). Identify any `await` where you chose *not* to add it and defend the choice. (Answer: none — this is library code.)

---

## Problem 2 — Write an IAsyncEnumerable, prove cancellation propagates (60 min)

Write a method `public async IAsyncEnumerable<string> ReadLinesAsync(string path, [EnumeratorCancellation] CancellationToken ct = default)` that:

- Opens the file at `path`, reads line by line using `StreamReader.ReadLineAsync(ct)`.
- Yields each non-empty line.
- Disposes the `StreamReader` correctly on enumerator dispose (use `await using var`).
- Honours the cancellation token: cancelling mid-read should throw within one line's worth of work.

Write a consumer that uses `await foreach` and demonstrate, with a 50ms `CancellationTokenSource` and a 100,000-line input file, that cancellation is observed within the budget.

**Deliverable:** the iterator code + consumer + a short note on what `await using var` guarantees that `using var` does not.

---

## Problem 3 — Pipeline with three full-mode comparisons (60 min)

Adapt Exercise 3 (the producer / bounded channel / consumers pipeline) to run three times in sequence with `FullMode = Wait`, `DropOldest`, and `DropWrite` respectively, all other parameters identical. Each run produces 100 items into a capacity-8 channel with consumers that take 5ms each.

For each run, report:

- Number of items consumed.
- Sum of consumed items.
- Wall-clock time from start to last consumer finishing.

**Deliverable:** the program + a one-table summary like:

| FullMode | Items consumed | Sum | Wall-clock |
|---|---:|---:|---:|
| Wait | 100 | 4950 | 145 ms |
| DropOldest | 87 | 4291 | 38 ms |
| DropWrite | 76 | 2917 | 32 ms |

Numbers will vary; the relative ordering should not. Explain in two sentences why `Wait` is the slowest and `DropWrite` is the fastest *for this workload*, and why for an audit-log pipeline `Wait` is the only correct choice.

---

## Problem 4 — Parallel.ForEachAsync versus Task.WhenAll over Select (60 min)

You have an `IEnumerable<int> ids` of 100 user IDs, and a method `Task<User> FetchUserAsync(int id, CancellationToken ct)`. Implement *both* of:

**Implementation A** (unbounded concurrency, the naive shape):

```csharp
var tasks = ids.Select(id => FetchUserAsync(id, ct));
User[] users = await Task.WhenAll(tasks);
```

**Implementation B** (bounded concurrency with `Parallel.ForEachAsync`, `MaxDegreeOfParallelism = 10`).

Compare the two against a mock `FetchUserAsync` that takes 50 ms ± 20 ms with `Task.Delay`. Measure wall-clock for each. Report:

| Implementation | Wall-clock | Max in-flight |
|---|---:|---:|
| A (Task.WhenAll) | ~70 ms | 100 |
| B (Parallel.ForEachAsync, MDoP=10) | ~500 ms | 10 |

Discuss in one paragraph: under what circumstances is A correct and under what circumstances is B correct? (Hint: A's "max in-flight = 100" is fine for a mock but catastrophic for a real downstream that can only handle 10 concurrent connections.)

**Deliverable:** both implementations + the comparison table + the paragraph.

---

## Problem 5 — ThreadPool starvation diagnostic, on your own machine (90 min)

Build a minimal ASP.NET Core app (`net8.0`) with two endpoints, `/fast` and `/slow`, exactly as in Challenge 2. Use Apache Bench (`ab`) or `curl` or `bombardier` to drive 500 concurrent requests against each. Capture the `dotnet-counters` output and reproduce the `threadpool-*` numbers in your write-up. Then *fix* `/slow` by removing the sync-over-async, re-run, and capture the post-fix numbers.

**Deliverable:** a side-by-side dotnet-counters capture (before/after) + a one-page postmortem describing the diagnosis. Submit screenshots of `dotnet-counters monitor` output for both runs (the simplest evidence). If your laptop cannot reproduce starvation at 500 concurrent requests, increase to 1,000.

---

## Problem 6 — Refactor a buggy production-shaped method (90 min)

Take the following deliberately bad code and rewrite it to be correct, observable, and cancellable. Submit the rewrite + a list of every change you made and why.

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public sealed class OrderService
{
    private readonly object _lock = new();
    private readonly Dictionary<int, decimal> _cache = new();

    // BUG-BAIT 1: sync-over-async at the entry point
    public decimal GetTotalForUser(int userId)
    {
        return GetTotalForUserAsync(userId).GetAwaiter().GetResult();
    }

    // BUG-BAIT 2: no CancellationToken anywhere
    public async Task<decimal> GetTotalForUserAsync(int userId)
    {
        var orders = await LoadOrdersAsync(userId);

        // BUG-BAIT 3: holds a lock across an await (compiler would catch the
        // bare form, but the rewrite hoists the await out, leaving the bug
        // structurally present in the architecture)
        decimal total = 0;
        foreach (var o in orders)
        {
            var rate = await GetRateAsync(o.Currency);
            lock (_lock)
            {
                _cache[o.Id] = o.Amount * rate;
                total += _cache[o.Id];
            }
        }

        // BUG-BAIT 4: .Result inside the body
        var multiplier = LoadMultiplierAsync(userId).Result;
        return total * multiplier;
    }

    // Stubs — assume they work as named
    private Task<List<Order>> LoadOrdersAsync(int userId) => Task.FromResult(new List<Order>());
    private Task<decimal> GetRateAsync(string currency) => Task.FromResult(1.0m);
    private Task<decimal> LoadMultiplierAsync(int userId) => Task.FromResult(1.0m);
}

public sealed record Order(int Id, decimal Amount, string Currency);
```

**Deliverable:** the rewrite + a numbered list of every change. Expected changes:

1. Remove `GetTotalForUser` entirely (or document it as `[Obsolete]`).
2. Add `CancellationToken ct = default` to `GetTotalForUserAsync` and every internal helper.
3. Propagate `ct` to every `await`.
4. Replace the `lock (_lock)` block + cache update with `SemaphoreSlim` *or* (better) move the cache update outside the loop and aggregate into a local first.
5. Replace `LoadMultiplierAsync(userId).Result` with `await LoadMultiplierAsync(userId, ct).ConfigureAwait(false)`.
6. (Optional) Convert `LoadOrdersAsync` to return `IAsyncEnumerable<Order>` if the orders count might be large.
7. (Optional) Wrap the whole method in a `Meter`-instrumented section that logs latency.

---

## Rubric (30 pts total)

| Problem | Points |
|---|---:|
| 1 — ConfigureAwait audit (correctness of every annotation) | 4 |
| 2 — IAsyncEnumerable (correctness, [EnumeratorCancellation] present, await using) | 5 |
| 3 — Three-mode pipeline (table reproduced, two-sentence analysis correct) | 5 |
| 4 — Parallel.ForEachAsync vs Task.WhenAll (both implementations, table, paragraph) | 5 |
| 5 — ThreadPool starvation reproduce + fix (before/after counters captured) | 6 |
| 6 — OrderService rewrite (all 7 changes identified and applied) | 5 |
| **Total** | **30** |

**Pass:** ≥ 24/30. **Honours:** ≥ 27/30.

## Submission

One zip file: `c9-week08-homework-<your-handle>.zip`. Inside:

```
/p1-configureawait-audit/JsonClient.cs
/p1-configureawait-audit/notes.md
/p2-iasyncenum/Program.cs
/p2-iasyncenum/notes.md
/p3-three-modes/Program.cs
/p3-three-modes/notes.md
/p4-parallel-vs-whenall/Program.cs
/p4-parallel-vs-whenall/notes.md
/p5-starvation/before-counters.png
/p5-starvation/after-counters.png
/p5-starvation/postmortem.md
/p6-rewrite/OrderService.cs
/p6-rewrite/changes.md
homework.md   <- top-level summary, problem-by-problem
```

Upload via the C9 submission portal by Sunday end-of-day.

## A note on the spirit of the homework

Every problem on this list maps to a single line on the Lecture 1.11 checklist. By the time you have done all six, the checklist is reflex. The homework is a deliberate-practice mechanism, not a test of memorisation; if a problem feels hard, the right move is to re-read the relevant lecture section, not to guess. Cite the lecture sections in your notes.

Senior engineers spend a meaningful fraction of their time auditing async code in code review. The skill is identical to the homework: read the method, walk the checklist, propose the fix. Practise it enough that the walking is fast.
