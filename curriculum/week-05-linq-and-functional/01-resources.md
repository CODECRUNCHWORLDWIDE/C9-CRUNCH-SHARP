# Week 5 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `dotnet/runtime` source is MIT-licensed and public on GitHub. The "Async in depth" and "LINQ in depth" series on `devblogs.microsoft.com` are free without registration. No paywalled books are linked.

## Required reading (work it into your week)

- **Language Integrated Query (LINQ) overview** — the canonical Microsoft Learn entry point:
  <https://learn.microsoft.com/en-us/dotnet/csharp/linq/>
- **Standard query operators overview** — the full reference list, grouped by category:
  <https://learn.microsoft.com/en-us/dotnet/csharp/linq/standard-query-operators/>
- **Query expression basics** — the `from`/`where`/`select` query syntax, with the canonical translations:
  <https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/query-expression-basics>
- **Deferred execution and lazy evaluation** — Microsoft Learn's dedicated article on when LINQ actually runs:
  <https://learn.microsoft.com/en-us/dotnet/csharp/linq/standard-query-operators/classification-of-standard-query-operators-by-manner-of-execution>
- **Introduction to LINQ to Objects** — the in-memory side that we cover this week:
  <https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/introduction-to-linq-queries>
- **`Enumerable.CountBy` API reference** — new in .NET 9:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.countby>
- **`Enumerable.AggregateBy` API reference** — new in .NET 9:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.aggregateby>
- **`Enumerable.Index` API reference** — new in .NET 9:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.index>
- **Records (C# reference)** — the canonical reference for `record` and `record struct`:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record>
- **Pattern matching overview** — the consolidated reference, including list patterns:
  <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching>
- **The `switch` expression** — the dedicated reference page:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/switch-expression>
- **.NET 9 — what's new in the libraries** — the official changelog page for the BCL:
  <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/libraries>

## Authoritative deep dives

- **Eric Lippert — "Foundations of LINQ in C#"** — the archive of the canonical "how LINQ works under the hood" series from the original C# compiler team. The "When everything you know about a feature is wrong" arc is the best ~3 hours of LINQ writing anywhere:
  <https://learn.microsoft.com/en-us/archive/blogs/ericlippert/>
- **Mads Torgersen — "C# language design notes"** — the language designer's running journal on every C# release. Records, list patterns, and `field` proposals are all in here:
  <https://github.com/dotnet/csharplang/tree/main/meetings>
- **Stephen Toub — ".NET 9 Performance Improvements"** — Toub's annual deep dive. The 2024 post covers `CountBy`, `AggregateBy`, `Index`, and the new `params ReadOnlySpan<T>` overloads with measured numbers:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
- **Jon Skeet — "Edulinq" series** — Skeet re-implemented LINQ from scratch, one operator per blog post, in 2010. The posts are old but the LINQ surface has not changed; the explanations are still the clearest:
  <https://codeblog.jonskeet.uk/category/edulinq/>
- **Bart de Smet — "Use the right tool for the job"** — the canonical "`IEnumerable<T>` vs `IQueryable<T>`" essay from the language-integrated-query architect:
  <https://learn.microsoft.com/en-us/archive/blogs/bart/>
- **".NET 9 LINQ: `CountBy` and `AggregateBy`"** — the official announcement post on the .NET blog:
  <https://devblogs.microsoft.com/dotnet/announcing-dotnet-9/>

## Official .NET docs

- **`IEnumerable<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1>
- **`IQueryable<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.iqueryable-1>
- **`Enumerable` class — the LINQ extension methods on `IEnumerable<T>`**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable>
- **`Queryable` class — the LINQ extension methods on `IQueryable<T>`**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.queryable>
- **`Expression<TDelegate>` — expression trees**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.linq.expressions.expression-1>
- **`yield` statement (C# reference)**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/yield>
- **`IEqualityComparer<T>`** — used everywhere in `DistinctBy`, `GroupBy`, `ToDictionary`:
  <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iequalitycomparer-1>

## Open-source projects to read this week

You learn more from one hour reading well-written LINQ source than from three hours of tutorials.

- **`dotnet/runtime` — `System.Linq`** — every LINQ operator lives here, MIT-licensed; the whole namespace is ~10,000 lines. Start with `Where.cs` (~250 lines) and `Select.cs` (~300 lines):
  <https://github.com/dotnet/runtime/tree/main/src/libraries/System.Linq/src/System/Linq>
- **`dotnet/runtime` — `Enumerable.CountBy`** — the new-in-.NET-9 implementation:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/CountBy.cs>
- **`dotnet/runtime` — `Enumerable.AggregateBy`** — same:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/AggregateBy.cs>
- **`MoreLINQ`** — a community-maintained set of "the operators LINQ should have had": `Batch`, `Pairwise`, `Lag`, `Lead`, `RunLengthEncode`, ~150 more. MIT-licensed:
  <https://github.com/morelinq/MoreLINQ>
- **`language-ext`** — a comprehensive functional library for C# (discriminated unions, `Option<T>`, `Either<L,R>`, monadic chaining). Heavy and opinionated; read it once for the ideas:
  <https://github.com/louthy/language-ext>
- **`dotnet/efcore` — `Microsoft.EntityFrameworkCore.Query`** — the expression-tree-to-SQL translator. Worth one hour for the punch-line: every LINQ operator we love in Week 5 has a translator function on the EF side that decides whether it can or cannot be turned into SQL. (Preview of Week 6.):
  <https://github.com/dotnet/efcore/tree/main/src/EFCore/Query>

## Community deep-dives

- **Andrew Lock — "C# LINQ" series** — exhaustive: <https://andrewlock.net/category/csharp/>
- **Bart Wullems — "LINQ in depth"** — the practical end of the spectrum: <https://bartwullems.blogspot.com/search/label/LINQ>
- **Konrad Kokosa's blog** — `record` allocation, `EqualityContract`, the cost of `with` expressions:
  <https://tooslowexception.com/>
- **Khalid Abuhakmeh** — JetBrains' .NET advocate; pragmatic LINQ refactor posts:
  <https://khalidabuhakmeh.com/blog>
- **Nick Chapsas — LINQ performance videos** on YouTube (community, very clear): <https://www.youtube.com/@nickchapsas>
- **Steve Gordon — "Deep dive into LINQ allocations"**: <https://www.stevejgordon.co.uk/>

## Libraries we touch this week

- **`System.Linq`** — in the BCL; no package needed. Defines `Enumerable`, `Queryable`, the standard operators, `CountBy` / `AggregateBy` / `Index` (new in .NET 9).
- **`System.Linq.Expressions`** — in the BCL. Defines `Expression<Func<T, bool>>`. Mentioned in Lecture 1; reintroduced in Week 6.
- **`System.Collections.Generic`** — defines `IEnumerable<T>`, `IList<T>`, `List<T>`, `Dictionary<TKey, TValue>`, `HashSet<T>`.
- **`System.Collections.Frozen`** — defines `FrozenDictionary<TKey, TValue>` and `FrozenSet<T>` (since .NET 8). One pass for `ToFrozenDictionary` in the mini-project's hot path.
- **`BenchmarkDotNet`** — used in the mini-project to prove the procedural-vs-pipeline allocation comparison. NuGet:
  <https://www.nuget.org/packages/BenchmarkDotNet>

## Editors

Unchanged from Weeks 1–4.

- **VS Code + C# Dev Kit** (primary): <https://code.visualstudio.com/docs/csharp/get-started>
- **JetBrains Rider Community** (secondary, free for non-commercial): <https://www.jetbrains.com/rider/>
- The new bit this week: **the IntelliSense tooltip**. Hover over any variable in a LINQ chain and read the tooltip. If it says `IEnumerable<T>`, your delegates run in process. If it says `IQueryable<T>`, your delegates are expression trees a provider will translate. This single habit prevents 80% of EF Core's "why is this query so slow" problems in Week 6.

## Free books and chapters

- **"C# in Depth, 4th Edition" by Jon Skeet** — chapters 10–12 cover LINQ from the language-design angle. The book is paywalled but Skeet's "Edulinq" blog series (linked above) covers ~80% of the material free.
- **"Functional Programming in C#, 2nd Edition" by Enrico Buonanno** — paywalled, but the first chapter (free preview on Manning) is the best introduction to the functional shift in modern C# in print. A Google search for "Functional Programming in C# Manning preview" surfaces it.
- **"LINQ Pocket Reference" by Joseph Albahari** — paywalled, but Albahari's free LINQPad samples cover the entire operator surface:
  <https://www.linqpad.net/>

## Videos (free, no signup)

- **"LINQ Tips" — the .NET YouTube channel's playlist** — short, focused videos: <https://www.youtube.com/@dotnet>
- **".NET Conf 2024 — language sessions"** — the .NET 9 release; includes a "What's new in C# 13" talk: <https://www.youtube.com/playlist?list=PL1rZQsJPBU2StolNg0aqvQswETPcYnNKL>
- **Nick Chapsas — "Stop using LINQ like this"** (community): <https://www.youtube.com/@nickchapsas>
- **IAmTimCorey — "C# LINQ tutorial"** (community, beginner-to-intermediate): <https://www.youtube.com/@IAmTimCorey>

## Tools you'll use this week

- **`dotnet` CLI** — same as before.
- **`LINQPad 8`** — the canonical scratch-pad for LINQ experiments. Free tier; runs on Windows + the free community edition runs in WSL or via Wine on macOS/Linux. The "Dump" extension is irreplaceable: <https://www.linqpad.net/>
- **`BenchmarkDotNet`** — `dotnet add package BenchmarkDotNet`. The de facto microbenchmark tool. Used in the mini-project to measure procedural-vs-pipeline allocations.
- **`SharpLab.io`** — paste C# in the browser and see the lowered C# / IL the compiler emits. Look at what `from x in xs where x > 5 select x` lowers to: <https://sharplab.io/>
- **`dotnet-counters`** — live perf counters in the terminal. We mention it once in the mini-project for measuring GC pressure during the benchmark warm-up.

## The spec — when you need to be exact

- **C# 13 language specification** — the LINQ query expression section:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions#query-expressions>
- **C# 13 — records section of the spec**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record>
- **C# 13 — pattern matching section of the spec**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns>
- **.NET Standard query operators (legacy reference)** — useful when you need the historical lineage:
  <https://learn.microsoft.com/en-us/dotnet/csharp/linq/standard-query-operators/standard-query-operators-overview>

## Glossary cheat sheet

Keep this open in a tab.

| Term | Plain English |
|------|---------------|
| **`IEnumerable<T>`** | A pull-based sequence of `T`. Has `GetEnumerator()`. Every standard operator extends this interface. |
| **`IQueryable<T>`** | A pull-based sequence backed by an expression tree and a provider. `Where`/`Select` are translated, not invoked, until you materialize. |
| **`IEnumerator<T>`** | The cursor side of an `IEnumerable<T>`. `MoveNext`, `Current`, `Dispose`. Created by `GetEnumerator()`. |
| **Deferred execution** | A LINQ query is built when you write it and run only when you enumerate. `Where(...).Select(...)` allocates two iterators and runs *neither* until `foreach`/`ToList`/`Count`. |
| **Materialization** | Forcing a deferred query to execute and produce a concrete collection. `.ToList()`, `.ToArray()`, `.ToDictionary()`, `.ToHashSet()`, `.Count()`, `.First()`, `.Any()`, `.Sum()`. |
| **Iterator method** | A method whose body contains `yield return`. The C# compiler rewrites it into a class that implements `IEnumerable<T>` + `IEnumerator<T>` (the iterator pattern). |
| **`yield return x`** | Inside an iterator method: "produce `x` and pause; resume here on the next `MoveNext`." |
| **`yield break`** | Inside an iterator method: "stop the sequence." |
| **Expression tree** | An in-memory representation of a delegate as data, not code. `Expression<Func<T, bool>>` is the LINQ-to-providers entry point. |
| **`Func<T, bool>`** | An ordinary delegate. LINQ-to-objects passes these to `Where`. |
| **`Expression<Func<T, bool>>`** | A delegate captured as an expression tree. LINQ-to-providers (EF Core, MongoDB.Driver) captures these and translates them. |
| **Standard query operator** | One of the ~80 methods on `Enumerable` / `Queryable` defined by the LINQ spec: `Where`, `Select`, `OrderBy`, `GroupBy`, etc. |
| **Query expression syntax** | `from x in xs where x > 5 select x.Name`. Syntactic sugar over method calls. The compiler translates it to `xs.Where(x => x > 5).Select(x => x.Name)`. |
| **Method syntax** | `xs.Where(x => x > 5).Select(x => x.Name)`. The form your tooling — IntelliSense, refactor tools — knows about. |
| **`record`** | A reference type with compiler-generated structural equality, `Equals`, `GetHashCode`, `ToString`, `Deconstruct`, and `with` support. Default modelling tool in C# 13. |
| **`record struct`** | A value type with the same compiler-generated members. Best for small (≤ 16 bytes) immutable values. |
| **`init` setter** | A property setter callable only during object construction (in a constructor or in an object initializer or in `with`). Enforces immutability without making the property `readonly`. |
| **`with` expression** | `record1 with { Name = "..." }`. Returns a *new* record with the specified properties changed. Compiles to a clone + assignments. |
| **`Deconstruct`** | A method (or compiler-generated method on records) that splits an object into a tuple: `var (x, y) = point;`. |
| **Pattern matching** | The `is`/`switch` family. C# 13 has type, property, positional, list, var, constant, relational (`> 5`), and logical (`and`/`or`/`not`) patterns. |
| **`switch` expression** | The expression form: `result = obj switch { Ok ok => ok.Value, Err e => -1 }`. Exhaustiveness is checked by the compiler (CS8509). |
| **List pattern** | `[]`, `[first, ..rest]`, `[_, _, last]`. Matches arrays and lists by shape. |
| **Property pattern** | `{ Status: Active, Score: > 50 }`. Matches an object whose properties satisfy nested patterns. |
| **Discriminated union** | A closed type hierarchy where every case is a sealed record. C# does not have first-class DUs; the idiom is `abstract record` + `sealed record` per case, plus exhaustive `switch`. |
| **`Map`** | "Apply this function to the value inside, return a new wrapper." For `Result<T>`: `Ok<T>(x).Map(f) = Ok<U>(f(x))`. |
| **`Bind`** (`>>=`, `SelectMany`) | "Apply this function that *itself returns the same wrapper*, flatten." For `Result<T>`: `Ok<T>(x).Bind(f) = f(x)`. |
| **`Match`** | The exhaustive consumer of a discriminated union — a `switch` expression in C# form. |
| **`Tap`** | A custom operator: "perform a side effect on each element, yield the element through unchanged." Useful for logging mid-pipeline. |
| **`CountBy(keySelector)`** | New in .NET 9. Yields `IEnumerable<KeyValuePair<TKey, int>>` — group + count in one allocation. |
| **`AggregateBy(keySelector, seed, func)`** | New in .NET 9. Group + fold in one allocation. Replaces `GroupBy(...).ToDictionary(g => g.Key, g => g.Aggregate(...))`. |
| **`Index()`** | New in .NET 9. Yields `IEnumerable<(int Index, T Item)>`. Replaces `Select((x, i) => (i, x))`. |
| **`Chunk(size)`** | Since .NET 6. Yields `IEnumerable<T[]>` of chunks of the given size. Last chunk may be shorter. |
| **`DistinctBy(keySelector)`** | Since .NET 6. `Distinct` by a projected key. |
| **`MinBy` / `MaxBy`** | Since .NET 6. Return the *element* whose projected key is minimal/maximal — not the projected key itself. |

---

*If a link 404s, please open an issue so we can replace it.*
