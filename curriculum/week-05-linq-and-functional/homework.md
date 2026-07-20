# Week 5 Homework

Six practice problems that revisit the week's topics. The full set should take about **6 hours**. Work in your Week 5 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — Read the lowered query expression

**Problem statement.** Open <https://sharplab.io/>. Paste this query-expression form:

```csharp
using System.Linq;
using System.Collections.Generic;

public class C
{
    public static IEnumerable<string> ActiveNames(IEnumerable<User> users) =>
        from u in users
        where u.IsActive
        orderby u.LastLogin descending
        select u.Name;
}

public record User(string Name, bool IsActive, System.DateTime LastLogin);
```

Switch the "Results" panel to "C#". Read what the compiler lowered the query expression into. Save the lowered form to `notes/query-lowered.md` with a short note (3–5 sentences) explaining what the compiler did. Then add a second snippet — the same query in method syntax — and confirm the lowered output is identical.

**Acceptance criteria.**

- `notes/query-lowered.md` exists and contains both forms plus the lowered C#.
- The note explains why method syntax and query syntax compile to the same IL.
- File is committed.

**Hint.** SharpLab's left panel is C# source; the right panel toggles between C#, IL, and JIT asm. The "C#" panel is the lowered form.

**Estimated time.** 20 minutes.

---

## Problem 2 — `CountBy` + `AggregateBy` benchmark

**Problem statement.** Write a `BenchmarkDotNet` harness that compares three implementations of "count log entries per host, where host is a string":

1. `Procedural` — a `foreach` over 100,000 fake `LogEntry` records, populating a `Dictionary<string, int>` with explicit `TryGetValue` / `Add`.
2. `LinqGroupBy` — `entries.GroupBy(e => e.Host).ToDictionary(g => g.Key, g => g.Count())`.
3. `LinqCountBy` — `entries.CountBy(e => e.Host).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)`.

Pre-generate the 100,000 entries with 1,000 unique hosts (uniform distribution). Use `[MemoryDiagnoser]` on the benchmark class.

Report:

| Implementation | Mean | Allocations |
|---------------|-----:|-------------:|
| `Procedural`   | ? μs | ? KB |
| `LinqGroupBy`  | ? μs | ? KB |
| `LinqCountBy`  | ? μs | ? KB |

Write `notes/countby-benchmark.md` with the table and 100–200 words analyzing the gap.

**Acceptance criteria.**

- `dotnet run -c Release` produces a `BenchmarkDotNet` summary table.
- The summary table is committed in `notes/countby-benchmark.md`.
- The analysis paragraph explicitly answers: "How close does `CountBy` get to the procedural form, and where does the gap (if any) come from?"
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** Use `[MemoryDiagnoser]` and `[Params(...)]` if you want to compare multiple input sizes. The procedural form will be fastest. `CountBy` should be ~10–15% slower but allocate roughly the same — one internal dictionary, no intermediate `IGrouping` objects. `LinqGroupBy` is the worst on both axes.

**Estimated time.** 1 hour.

---

## Problem 3 — `Result<T>` discriminated union

**Problem statement.** Build a small `Result<T>` library with:

- `abstract record Result<T>` base.
- `sealed record Ok<T>(T Value) : Result<T>`.
- `sealed record Err<T>(string Error) : Result<T>`.
- Extension methods `Map<T, U>`, `Bind<T, U>`, `Match<T, U>` exactly as in Lecture 2 §4.

Then use it to build a 3-step parser pipeline:

```csharp
Result<int>    ParseInt(string s);
Result<int>    Double(int n);            // returns Err if n > int.MaxValue / 2
Result<string> Format(int n);
```

Wire them together with `Bind`:

```csharp
Result<string> pipeline = ParseInt(input).Bind(Double).Bind(Format);
```

Then write 8 xUnit tests covering: happy path, `ParseInt` fails, `Double` overflows, `Format` always succeeds (so it never produces an `Err`), composition with `Map`, composition with `Match`, mixed `Map`/`Bind` chain, type mismatch caught at compile time (this last one is a comment, not a test — show that swapping `Double` for a function returning `Result<bool>` fails to compile).

**Acceptance criteria.**

- All 8 tests pass.
- No `try`/`catch` in the production code. (Tests may use `Assert.Throws` for explicit exception scenarios if you add them.)
- The `Match` method is exhaustive without a `_` arm.
- `dotnet test`: all passing.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** The error message for the overflow case should mention "would overflow"; for the parse case, "not an integer." The `Match` method's signature is `U Match<U>(Func<T, U> onOk, Func<string, U> onErr)`. The "type mismatch" comment lives in a file `notes/type-safety.md` and includes a 3-line code sample showing what the compiler error looks like.

**Estimated time.** 1 hour 15 minutes.

---

## Problem 4 — Refactor a real procedural method

**Problem statement.** Pick *one* procedural method from any of your Week 1–4 mini-projects (the Crawler, the Ledger CLI, the Repo Stats, the Sharp Notes API). It must:

- Be at least 30 lines.
- Contain at least one nested loop OR at least one mutable accumulator.
- Be unit-tested already (or be testable — add tests if needed).

Refactor it into a LINQ pipeline. The new version must:

- Produce identical output for every existing test.
- Be no longer than 50% of the original line count.
- Use at least one of `CountBy`, `AggregateBy`, `Index`, or a `switch` expression with property/list patterns.
- Have a 5-sentence reflection at the top of the file (as a comment) on what was easier to read in the procedural form and what is easier in the pipeline form.

Commit both forms — the original (in a `procedural/` folder) and the new (in `pipeline/`), so a reviewer can diff them.

**Acceptance criteria.**

- Both forms exist in the repo.
- The pipeline form is ≤ 50% of the procedural form's line count.
- All existing tests pass against the pipeline form.
- The 5-sentence reflection is honest about trade-offs.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** If your Week 1–4 mini-projects don't have a candidate method, use the procedural `AnalyzeProcedural` from Exercise 3 as a starting point and re-implement it in a new repo. The win is the refactor reflex, not the specific method.

**Estimated time.** 1 hour 30 minutes.

---

## Problem 5 — Custom LINQ extension methods

**Problem statement.** Build a small `LinqExtensions` static class with the following operators:

- `Tap<T>(this IEnumerable<T>, Action<T>)` — perform a side effect on each element; yield through. Use the split-method pattern.
- `WhereNotNull<T>(this IEnumerable<T?>) -> IEnumerable<T>` where `T : class` — filter out nulls and rewrite the static type to `T`, not `T?`.
- `ChunkBy<T, TKey>(this IEnumerable<T>, Func<T, TKey>)` — group *adjacent* elements with the same key (different from `GroupBy`, which gathers all elements with the same key regardless of position). Useful for "compress runs of identical values."
- `Pairwise<T>(this IEnumerable<T>) -> IEnumerable<(T Prev, T Curr)>` — yield consecutive pairs. The first element does not appear as `Curr` alone; the last does not appear as `Prev` alone. For input `[a, b, c, d]`, yield `[(a, b), (b, c), (c, d)]`.

Write 10 xUnit tests across the four methods. Use property-based testing if you know FsCheck or AutoFixture, otherwise hand-written cases are fine.

**Acceptance criteria.**

- All four methods use the split-method pattern (eager argument validation, deferred iteration).
- `WhereNotNull<T>` has the correct nullability annotations: input `IEnumerable<T?>`, output `IEnumerable<T>`. The compiler should infer the right types without a cast at the call site.
- 10+ tests; `dotnet test` clean.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** `WhereNotNull<T>` needs a `where T : class` constraint to be unambiguous. (For value types, write a `WhereHasValue<T>(this IEnumerable<T?>) where T : struct` overload — but keep the homework to the reference-type version.) `Pairwise<T>` is two enumerators of the same sequence, one ahead of the other; or you can keep a single enumerator and a `prev` local. Either way the test for empty input matters: `Pairwise<T>([])` should yield no elements (not throw).

**Estimated time.** 1 hour.

---

## Problem 6 — Mini reflection essay

**Problem statement.** Write a 300–400 word reflection at `notes/week-05-reflection.md` answering:

1. The lecture argues that "C# 13 is a functional-first language with imperative escape hatches." After a week of LINQ pipelines and `switch` expressions, do you agree? Cite one concrete example from your homework where the functional form was clearer and one where the imperative form was clearer.
2. Where does .NET's LINQ feel familiar vs different compared to similar features in languages you've used (Python list comprehensions, Java Streams, JavaScript `.map`/`.filter`, F#, Kotlin)? Pick one familiar and one different, with concrete examples.
3. Which of the .NET 9 additions — `CountBy`, `AggregateBy`, `Index` — do you expect to reach for most? Be honest about which you might still write the pre-.NET-9 way out of habit.
4. What's one thing you'd want to learn next that this week didn't cover? (Source generators? Custom `IQueryable<T>` providers? `System.Linq.Parallel`? `IObservable<T>` and Rx?)

**Acceptance criteria.**

- File exists, 300–400 words.
- Each numbered question is addressed in its own paragraph.
- File is committed.

**Hint.** This is for *you*, not for a grade. Be honest. Future-you reading it after Week 6 (when you've written EF Core queries with the same operators) will be grateful for the honesty.

**Estimated time.** 30 minutes.

---

## Time budget recap

| Problem | Estimated time |
|--------:|--------------:|
| 1 | 20 min |
| 2 | 1 h 0 min |
| 3 | 1 h 15 min |
| 4 | 1 h 30 min |
| 5 | 1 h 0 min |
| 6 | 30 min |
| **Total** | **~5 h 35 min** |

When you've finished all six, push your repo and open the [mini-project](./mini-project/README.md). The mini-project takes everything you've practiced this week and refactors a 500-line procedural CSV analyser into a LINQ pipeline that allocates fewer bytes, runs in the same wall-clock time, and reads in 80 lines what used to take 500 — with `BenchmarkDotNet` numbers to prove it.
