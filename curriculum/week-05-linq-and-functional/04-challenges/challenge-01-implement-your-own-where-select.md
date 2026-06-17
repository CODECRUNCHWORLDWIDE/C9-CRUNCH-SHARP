# Challenge 1 — Implement your own `Where` / `Select` / `SelectMany`

> Write `MyWhere<T>`, `MySelect<T, U>`, and `MySelectMany<T, U>` from scratch over `IEnumerable<T>` using `yield return`. Then read the `dotnet/runtime` BCL source for the real operators. Then write a 200-word note on the fusion optimization the BCL applies — why `xs.Where(p).Select(s)` allocates one iterator instead of two when the source is a `T[]` or a `List<T>`.

**Estimated time:** ~2 hours.

---

## Why this exists

LINQ feels like magic until you have written it yourself. Reimplementing the three most-reached-for operators is the single fastest way to internalize the iterator pattern, the deferred-execution model, and the *exact* shape every BCL LINQ method takes. After this challenge you will read `Enumerable.Where` source code and recognize every line.

---

## Phase 1 — Scaffold (~10 min)

```bash
mkdir MyLinq && cd MyLinq
dotnet new console -n MyLinq -o src/MyLinq
dotnet new xunit   -n MyLinq.Tests -o tests/MyLinq.Tests
dotnet new sln -n MyLinq
dotnet sln add src/MyLinq/MyLinq.csproj tests/MyLinq.Tests/MyLinq.Tests.csproj
dotnet add tests/MyLinq.Tests reference src/MyLinq/MyLinq.csproj
```

Add a `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Commit: `Skeleton`.

## Phase 2 — Implement `MyWhere`, `MySelect`, `MySelectMany` (~30 min)

In `src/MyLinq/MyLinq.cs`:

```csharp
public static class MyLinq
{
    public static IEnumerable<T> MyWhere<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        if (source is null)    throw new ArgumentNullException(nameof(source));
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        return Iterator(source, predicate);

        static IEnumerable<T> Iterator(IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source)
            {
                if (predicate(item))
                    yield return item;
            }
        }
    }

    public static IEnumerable<U> MySelect<T, U>(this IEnumerable<T> source, Func<T, U> selector)
    {
        if (source is null)   throw new ArgumentNullException(nameof(source));
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        return Iterator(source, selector);

        static IEnumerable<U> Iterator(IEnumerable<T> source, Func<T, U> selector)
        {
            foreach (var item in source)
                yield return selector(item);
        }
    }

    public static IEnumerable<U> MySelectMany<T, U>(
        this IEnumerable<T> source,
        Func<T, IEnumerable<U>> selector)
    {
        if (source is null)   throw new ArgumentNullException(nameof(source));
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        return Iterator(source, selector);

        static IEnumerable<U> Iterator(IEnumerable<T> source, Func<T, IEnumerable<U>> selector)
        {
            foreach (var outer in source)
                foreach (var inner in selector(outer))
                    yield return inner;
        }
    }
}
```

Note the **split-method pattern** — the argument checks run at the call site, but the `yield`-using `Iterator` local function does not run until the consumer enumerates. This is the exact pattern the BCL uses.

Commit: `MyWhere, MySelect, MySelectMany — naive iterator-based implementations`.

## Phase 3 — Tests (~30 min)

In `tests/MyLinq.Tests/MyLinqTests.cs`, write at least:

- **MyWhere tests:** empty source returns empty; predicate filters correctly; predicate is deferred until enumeration (verify with a counter that increments inside the predicate); two `MyWhere`s chain.
- **MySelect tests:** projects each element; preserves order; deferred until enumeration.
- **MySelectMany tests:** flattens nested sequences; preserves order across both levels; empty inner sequences are skipped; the outer enumerator is disposed correctly even if the consumer aborts mid-enumeration.
- **Argument checks:** each method throws `ArgumentNullException` *immediately* on null source/predicate/selector, not at the first `MoveNext`. (Hint: this is the test that verifies the split-method pattern.)

Target: at least **12 passing tests**. `dotnet test` should be green and `dotnet build` clean.

Commit: `Tests for MyWhere/MySelect/MySelectMany`.

## Phase 4 — Read the BCL source and write a 200-word note (~30 min)

Open these two files in `dotnet/runtime`:

- `Where.cs` — <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/Where.cs>
- `Select.cs` — <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Linq/src/System/Linq/Select.cs>

Notice:

1. The public `Where` and `Select` methods are **dispatchers**. They check the runtime type of `source`:
   - If it is a `T[]`, return a `WhereArrayIterator<T>` / `SelectArrayIterator<T, U>`.
   - If it is a `List<T>`, return a `WhereListIterator<T>` / `SelectListIterator<T, U>`.
   - If it is itself a `WhereXxxIterator` from a previous `Where`, **return a new fused iterator** that combines both predicates into one method.
   - Otherwise, fall back to the generic `WhereEnumerableIterator<T>` / `SelectEnumerableIterator<T, U>`.

2. The `WhereSelectArrayIterator<TSource, TResult>` class fuses a `Where` followed by a `Select` into a single iterator. Two adjacent `xs.Where(p).Select(s)` calls on an array allocate one iterator and run one tight loop instead of two chained iterators with two `MoveNext` dispatches per element.

3. `Iterator<T>` is the BCL's internal abstract base class for all standard-operator iterators. It implements `IEnumerable<T>`, `IEnumerator<T>`, and `IDisposable` in one class — saving the per-call `GetEnumerator()` allocation by returning `this` if the iterator has not yet been consumed.

Write a 200-word note at `notes/fusion.md` that:

- Names the three optimizations above.
- Quotes one method header from each file.
- Explains why an unoptimized iterator chain costs ~2× the per-element work of the fused one.
- Identifies *one* shape your naive `MyWhere(...).MySelect(...)` chain does that the BCL does not (hint: it allocates a new iterator object even when the predicate excludes every element — the BCL's `Empty<T>()` fast path skips this).

Commit: `Read BCL source, write fusion note`.

## Phase 5 — Benchmark (optional, ~30 min)

If you've installed `BenchmarkDotNet`, write a small benchmark:

```csharp
[MemoryDiagnoser]
public class Benchmarks
{
    private readonly int[] _data = Enumerable.Range(0, 10_000).ToArray();

    [Benchmark(Baseline = true)]
    public int Bcl_Where_Select_Sum() =>
        _data.Where(n => n % 2 == 0).Select(n => n * n).Sum();

    [Benchmark]
    public int My_Where_Select_Sum() =>
        _data.MyWhere(n => n % 2 == 0).MySelect(n => n * n).Sum();
}
```

Run `dotnet run -c Release` and append the table to `notes/fusion.md`. You should see:

- The BCL version allocates ~80–120 bytes per call.
- Your naive version allocates ~280–320 bytes per call (two iterator objects + a few closures vs one fused iterator).
- The BCL version is ~30–40% faster on wall-clock time.

That is the fusion optimization paying off. The cost of writing it is the ~1500 lines of specialized iterator types in the BCL. You do not need to write them, but you should know they exist.

Commit: `Benchmark: BCL vs naive`.

---

## Acceptance criteria

- [ ] `MyWhere`, `MySelect`, `MySelectMany` are implemented with the split-method pattern (argument check + local `Iterator` function).
- [ ] At least 12 tests pass; `dotnet test` clean.
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `notes/fusion.md` exists, is ~200 words, references at least one method by name from each of `Where.cs` and `Select.cs`, and explains why the fusion optimization matters.
- [ ] Commits are small and named (e.g. `Skeleton`, `MyWhere/MySelect/MySelectMany`, `Tests`, `Fusion note`).

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Implementations | 30% | Three methods that match LINQ's deferred semantics; the split-method pattern visible; arguments validated up-front |
| Tests | 30% | 12+ tests; deferred-execution tests verify the counter-trick; the abort-mid-enumeration test passes |
| Fusion note | 25% | 200 words, references the BCL source, identifies at least one optimization your implementation lacks |
| Benchmark (optional) | 15% | A `BenchmarkDotNet` table with allocations; shows the gap; identifies the cause |

## What this prepares you for

- **Week 6** introduces EF Core's `IQueryable<T>` translator. The translator walks expression trees, recognizes specific operator combinations, and emits SQL. The pattern is the same as the BCL's iterator fusion — recognize the shape, emit the specialized version.
- **Week 12** introduces source generators. A custom LINQ provider built with source generators looks exactly like the dispatchers in `Where.cs` and `Select.cs` — match on the input type, emit the specialized iterator.
- **Reading any LINQ-style library** is now in your reach. `MoreLINQ`, `language-ext`, `System.Linq.Async`, `System.Reactive` — all of these are variations on the same theme.

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes a one-paragraph description, the `dotnet test` command, and a link to `notes/fusion.md`.
3. Make sure `dotnet build` and `dotnet test` are green on a fresh clone.
4. Post the repo URL in your cohort tracker.
