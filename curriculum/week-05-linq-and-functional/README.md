# Week 5 — LINQ and Functional Patterns

Welcome to **C9 · Crunch Sharp**, Week 5. Week 1 made the language ordinary. Week 2 put it behind an HTTP port. Week 3 put it on top of a real database. Week 4 made it concurrent. This week turns the corner from "imperative C# with occasional helpers" to *declarative C#*: LINQ pipelines that read like the question they answer, `record` and `record struct` as the default modelling tool, exhaustive `switch` expressions and list patterns as the default branching tool, and the C# 13 / .NET 9 additions — `CountBy`, `AggregateBy`, `Index`, `Append` improvements, and the `params ReadOnlySpan<T>` overloads on `string.Concat` — used deliberately, not reflexively. By Friday you should be able to read any LINQ query out loud as English, predict every materialization point without running the program, refactor a 200-line procedural loop into a 30-line LINQ pipeline that compiles to the same IL, and explain — to a code reviewer who has never written C# — why `query.Where(x => x.Active).Count()` and `query.Count(x => x.Active)` are not the same.

This is the bridge between Phase 1's foundations and Phase 2's data work. Microsoft Learn's "Language Integrated Query" article is excellent and runs ~120 pages across its subsections; we will read parts of it together. But the article stops at "here are the operators." It does not prepare you for the question that actually matters in a code review: *when this LINQ pipeline runs against a `List<T>`, against a `IAsyncEnumerable<T>`, and against EF Core's `IQueryable<T>`, what happens at each call site, and where does the work actually execute?* This week gives you a defensible answer for every LINQ chain you write from here on — and the muscle memory to write the pipeline before the loop.

The first thing to internalize is that **LINQ is two things, not one**. There is LINQ-to-objects — `IEnumerable<T>` extension methods that wrap iterator pattern (`yield return`) machinery — and there is LINQ-to-providers — `IQueryable<T>` expression trees that a provider (EF Core, MongoDB.Driver, dotnet/linq2db) translates into the target language (SQL, MQL, whatever). Both expose the same surface (`Where`, `Select`, `OrderBy`, ...). Both look identical at the call site. Both behave *completely differently*. `IEnumerable<T>.Where` runs your delegate; `IQueryable<T>.Where` captures your delegate as an `Expression<Func<T, bool>>`, never invokes it, ships its tree to a translator, and the translator emits SQL. The single biggest mistake in a junior C# code review is not knowing which of the two you are holding. By the end of this week you will check, automatically, by typing the variable's name into your IDE and reading whether it says `IEnumerable<T>` or `IQueryable<T>` in the tooltip.

The second thing to internalize is that **deferred execution is correctness, not just performance**. The query `var active = db.Users.Where(u => u.Active);` does not query the database. It builds an expression tree. The query runs when you enumerate — `foreach`, `.ToList()`, `.First()`, `.Count()`. If you call `.ToList()` twice on the same `IQueryable<T>`, you run the query twice. If you `.Where().Where().Where()` then `.ToList()`, the database sees one query with three predicates `AND`-ed together. If you forget the `.ToList()` and pass the `IQueryable<T>` to a method that re-enumerates, the database sees the query *every* time. The whole of Lecture 1 is "where does this query actually run, and how many times?" — drilled until the answer is reflexive.

The third thing to internalize is that **C# is becoming a functional-first language with imperative escape hatches, not the other way around**. C# 9 (2020) gave us records and `init` setters. C# 10 (2021) gave us record struct and global usings. C# 11 (2022) gave us list patterns and required members. C# 12 (2023) gave us primary constructors on regular classes and collection expressions. C# 13 on .NET 9 (2024) gives us `params ReadOnlySpan<T>`, the `field` contextual keyword, partial properties, the new `lock` type, and three first-class LINQ additions (`CountBy`, `AggregateBy`, `Index`). The trajectory is one direction: **immutable data, exhaustive pattern matching, declarative pipelines**. Lecture 2 makes records and pattern matching the default modelling tool — not the exception.

## Learning objectives

By the end of this week, you will be able to:

- **Read** any LINQ chain out loud as the question it answers ("the most-recently active user with at least three commits in the last 30 days, grouped by team") and predict the exact set of operators required, before you type them.
- **Distinguish** `IEnumerable<T>` from `IQueryable<T>` at every call site — in your own code, in NuGet packages you depend on, and in stack traces. Predict which delegate runs in process and which expression is translated by a provider.
- **Explain** deferred execution to a code reviewer who has never written C#: when a query is *built*, when it is *executed*, how many times each call site triggers re-enumeration, and the three operators (`ToList`, `ToArray`, `Count`, `First`, `Any`, ...) that force materialization.
- **Apply** the C# 13 LINQ additions — `CountBy`, `AggregateBy`, `Index` — and explain when each replaces a pre-.NET-9 `GroupBy(...).ToDictionary(...)` pattern with one allocation instead of two.
- **Refactor** a procedural C# program (nested loops, mutable accumulators, scattered `if` branches) into a LINQ pipeline that compiles to the same IL — and prove it with `BenchmarkDotNet`.
- **Model** a domain with `record` and `record struct`, choosing between them based on size, mutability, and equality semantics. Default to `record`; reach for `record struct` only when the type is ≤ 16 bytes and you have measured the allocation.
- **Write** exhaustive `switch` expressions over closed type hierarchies — sealed records — that the compiler statically proves cover every case (CS8509 as a build error).
- **Use** list patterns (`[]`, `[first, ..rest]`, `[_, _, last]`) and property patterns (`{ Status: Active, Score: > 50 }`) where a chain of `if`/`else` would have been the obvious approach in C# 8.
- **Pick** between `LINQ.Where(...).ToList()` and a manual `for` loop based on benchmarked allocations — and know that on `List<T>` of fewer than ~1000 elements the answer is almost always "use LINQ, it doesn't matter."
- **Author** a small set of generic LINQ extension methods (`Chunk`, `Tap`, `WhereNotNull`, `DistinctBy`) that read like first-class operators in your downstream code.

## Prerequisites

- **Weeks 1, 2, 3, and 4** of C9 complete: you can scaffold a multi-project solution from the `dotnet` CLI, you can write Minimal API endpoints with `TypedResults`, you can model an EF Core `DbContext`, you can compose `async`/`await` and `CancellationToken` through a `Channel<T>` pipeline, and your `dotnet build` reflexively prints `Build succeeded · 0 warnings · 0 errors`.
- **Basic LINQ vocabulary.** You have seen `Where`, `Select`, and `OrderBy` and you know they come from `System.Linq`. We do not teach LINQ from scratch; we teach LINQ as the disciplined, deferred, declarative tool it actually is.
- **Comfort with `IEnumerable<T>`**, the iterator pattern, and `yield return`. Week 1 covered the iterator pattern lightly; this week we lean on it heavily in Lecture 1.
- A working `dotnet --version` of `9.0.x` or later on your PATH. C# 13 ships in this SDK and we use the new BCL members (`Enumerable.CountBy`, `Enumerable.AggregateBy`, `Enumerable.Index`, the `params ReadOnlySpan<T>` overloads, and the `field` contextual keyword in property accessors).
- Nothing else. We start from `dotnet new console`, end at a benchmarked refactor of a procedural program into a LINQ pipeline, and never install a paid profiler.

## Topics covered

- **The two LINQs.** `IEnumerable<T>` (delegates run in process) vs `IQueryable<T>` (expression trees translated by a provider). Why the same `Where` call means two different things. Why `Func<T, bool>` and `Expression<Func<T, bool>>` are *different types* and the compiler picks based on the receiver.
- **Deferred execution.** The iterator pattern (`yield return`), why a `Where` clause does not run until you enumerate, the operators that force materialization (`ToList`, `ToArray`, `ToDictionary`, `ToHashSet`, `Count`, `First`, `Single`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average`, `Aggregate`), and the three operators that *sometimes* materialize and *sometimes* stream (`OrderBy`, `Reverse`, `GroupBy`).
- **The standard operators.** `Where`, `Select`, `SelectMany`, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`, `Take`, `Skip`, `TakeWhile`, `SkipWhile`, `Distinct`, `DistinctBy` (.NET 6+), `Union`, `Intersect`, `Except`, `Concat`, `Zip`, `Chunk` (.NET 6+), `GroupBy`, `Join`, `GroupJoin`, `Aggregate`, the terminal `ToList`/`ToArray`/`ToDictionary`/`ToHashSet`.
- **The .NET 9 / C# 13 LINQ additions.** `CountBy(keySelector)` — group + count in one allocation. `AggregateBy(keySelector, seed, func)` — group + fold in one allocation. `Index()` — `IEnumerable<(int Index, T Item)>` without `Select((x, i) => (i, x))`. Why all three replace patterns that previously cost two passes or a `Dictionary` build.
- **Query syntax vs method syntax.** When the query-expression form (`from u in users where u.Active select u.Name`) is clearer; when method syntax wins; why the compiler treats them identically (the language spec defines query syntax in terms of method syntax).
- **LINQ to objects internals.** What `Where` actually returns (`WhereEnumerableIterator<T>`), how `Where(...).Select(...)` collapses into a single iterator in the BCL, why `OrderBy(...).Select(...)` does *not* collapse, and the implications for allocation and cache locality.
- **`record` and `record struct`.** Value equality, `with` expressions, deconstruction, `init`-only setters, the `EqualityContract` virtual method, when `record struct` beats `record`, when `class` is still the right answer.
- **Pattern matching, exhaustively.** `switch` expressions, property patterns, positional patterns, list patterns, relational patterns, `var` patterns, `and`/`or`/`not` patterns. The CS8509 warning (`switch expression does not handle all possible values`) treated as a build error.
- **Closed type hierarchies.** `sealed record` + a discriminated-union-shaped pattern (`Result<T>` = `Ok<T>` | `Err<T>`) — what F# would call a sum type, expressed in C# 13 using `sealed` and an exhaustive `switch`.
- **Functional patterns in idiomatic C#.** Immutability by default, pure functions where possible, `with` expressions instead of mutation, `Map`/`Bind`/`Match` over discriminated unions, the `Tap` operator for side effects in a pipeline.
- **Performance honesty.** When LINQ is "fast enough" (almost always — < 1000 elements). When LINQ allocates more than a `for` loop and how much. When `Span<T>`-friendly code beats LINQ. When the answer is "use LINQ; we will not be benchmarking the unit test."

## Weekly schedule

The schedule adds up to approximately **34 hours**. Treat it as a target, not a contract.

| Day       | Focus                                                  | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | LINQ fundamentals, deferred execution, two LINQs       |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | Standard operators, .NET 9 additions, query syntax     |    1h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     4.5h    |
| Wednesday | `record`, `record struct`, pattern matching            |    1.5h  |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5h      |
| Thursday  | Functional patterns, `switch` expressions, list patterns |  1.5h    |    1.5h   |     1h     |    0.5h   |   1h     |     2h       |    0.5h    |     8h      |
| Friday    | Mini-project — procedural to pipeline refactor         |    0h    |    0h     |     0h     |    0.5h   |   1h     |     3h       |    0.5h    |     5h      |
| Saturday  | Mini-project deep work, BenchmarkDotNet run            |    0h    |    0h     |     0h     |    0h     |   1h     |     3h       |    0h      |     4h      |
| Sunday    | Quiz, review, polish                                   |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                        | **6h**   | **6h**    | **1h**     | **3h**    | **6h**   | **8.5h**     | **2.5h**   | **33.5h**   |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | Curated Microsoft Learn, .NET source, and open-source links |
| [lecture-notes/01-linq-fundamentals-and-deferred-execution.md](./lecture-notes/01-linq-fundamentals-and-deferred-execution.md) | The two LINQs, deferred execution, the standard operators, the iterator pattern under the surface, the C# 13 / .NET 9 additions |
| [lecture-notes/02-functional-patterns-records-pattern-matching.md](./lecture-notes/02-functional-patterns-records-pattern-matching.md) | `record` and `record struct`, value equality, `switch` expressions, exhaustive patterns, list patterns, closed type hierarchies, `Map`/`Bind`/`Match` |
| [exercises/README.md](./exercises/README.md) | Index of short coding exercises |
| [exercises/exercise-01-linq-puzzles.cs](./exercises/exercise-01-linq-puzzles.cs) | Ten fill-in-the-pipeline puzzles that drill the standard operators plus `CountBy`, `AggregateBy`, and `Index` |
| [exercises/exercise-02-deferred-vs-immediate.cs](./exercises/exercise-02-deferred-vs-immediate.cs) | A deliberately-broken program that re-enumerates an `IEnumerable<T>` three times; you trace the cost, materialize at the right point, and prove the fix with `Stopwatch` |
| [exercises/exercise-03-pipeline-refactor.cs](./exercises/exercise-03-pipeline-refactor.cs) | A 120-line procedural log analyser; refactor it into a 30-line LINQ pipeline that produces the same output |
| [challenges/README.md](./challenges/README.md) | Index of weekly challenges |
| [challenges/challenge-01-implement-your-own-where-select.md](./challenges/challenge-01-implement-your-own-where-select.md) | Re-implement `Where`, `Select`, and `SelectMany` from scratch over `IEnumerable<T>`, then read the BCL source and compare |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on LINQ, deferred execution, records, and pattern matching in .NET 9 / C# 13 |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for the "Procedural-to-Pipeline Refactor" — take a 500-line procedural CSV analyser, refactor to a LINQ pipeline, benchmark both with `BenchmarkDotNet`, and write a 1-page perf note |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

A nullable-reference warning is a bug. A CS8509 ("`switch` expression does not handle all possible values") in a project that defines a closed type hierarchy is a bug. A `var` inferred as `IEnumerable<T>` when you needed `IQueryable<T>` is — at minimum — a code smell and often a silent perf bug. By the end of Week 5 you will have a benchmarked refactor that compiles clean, replaces 500 lines of nested loops with ~80 lines of LINQ + records, and proves on `BenchmarkDotNet` that the pipeline allocates fewer bytes and runs in the same wall-clock time as the imperative version — all built incrementally over the week.

## A note on what's not here

Week 5 introduces LINQ to objects and the functional patterns C# 13 makes ergonomic, but it does **not** introduce:

- **LINQ to SQL / EF Core query translation.** That is the bulk of Week 6. We deliberately stay on `IEnumerable<T>` this week so you can see what LINQ-to-objects actually does without a translator in the way. Week 6 reintroduces the same operators with EF Core's translator and the rules change.
- **Reactive Extensions (`System.Reactive`) and `IObservable<T>`.** Rx is the push-based dual of LINQ-to-objects. We covered the pull-based async case (`IAsyncEnumerable<T>`) in Week 4. Push-based observables are out of scope for the foundations phase.
- **Expression trees and `Expression<Func<T, bool>>`** beyond the conceptual mention in Lecture 1. Building expression trees by hand is a Phase 3 topic (source generators); reading them is enough for Week 5.
- **F# interop.** F# is an excellent peer language that ships with .NET 9 and shares the BCL. We borrow vocabulary (discriminated unions, exhaustive pattern matching) but do not switch languages.
- **`System.Linq.Parallel` (PLINQ).** Parallelizing LINQ across cores is a niche tool. The vast majority of pipelines you write are I/O-bound or already small. PLINQ is mentioned in resources; it is not on the lecture path.
- **Custom LINQ providers.** Writing an `IQueryProvider` implementation that translates expression trees to a new target language is a Phase 3 topic. We consume `IQueryable<T>` next week; we do not produce one here.

The point of Week 5 is a sharp, narrow tool: LINQ pipelines you can read out loud, records you can pattern-match exhaustively, and a procedural-to-functional refactor reflex that becomes automatic.

## Stretch goals

If you finish the regular work early and want to push further:

- Read **Eric Lippert's "How does deferred execution work?"** archive on the old C# team blog: <https://learn.microsoft.com/en-us/archive/blogs/ericlippert/>.
- Skim the **`dotnet/runtime` LINQ source** for `Where`, `Select`, and the iterator pattern: <https://github.com/dotnet/runtime/tree/main/src/libraries/System.Linq/src/System/Linq>. The `WhereEnumerableIterator<T>` class is ~150 lines and the most-read file in the BCL.
- Read **the C# 13 spec proposal** for the `field` contextual keyword: <https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13>.
- Watch **Mads Torgersen — "Records in C#"** from .NET Conf 2020 (still the canonical talk): <https://www.youtube.com/@dotnet>.
- Build a **second refactor** that uses `Parallel.ForEachAsync` over a chunked input instead of LINQ. Note where the parallel form is faster and where it is slower (hint: contention on the result collector usually wins).
- Implement **the `Result<T>` discriminated union** from Lecture 2 and ship it as a small NuGet package. Many teams ship one of these per codebase; yours can be public.

## Up next

Continue to **Week 6 — Entity Framework Core and Dapper** once you have pushed the mini-project to your GitHub. Week 6 reintroduces every LINQ operator from this week against `IQueryable<T>` instead of `IEnumerable<T>` — and you will see, in real time, which operators translate to SQL, which throw at runtime, and which silently client-evaluate (the worst kind of bug). The reflex you build this week — *"which LINQ am I holding?"* — is the reflex Week 6 cashes in on.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
