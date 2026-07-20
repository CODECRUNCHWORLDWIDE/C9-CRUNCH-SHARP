# Week 5 — Exercises

Three coding exercises that drill the week's mechanical skills. Each is a `.cs` file you can drop into a fresh console project and complete by filling in the `TODO`s. None should take more than 60 minutes; if you spend longer, read the hints at the bottom of the file.

| Exercise | Time | What you'll exercise |
|----------|-----:|----------------------|
| [exercise-01-linq-puzzles.cs](./exercise-01-linq-puzzles.cs) | 60 min | Ten short puzzles drilling `Where`/`Select`/`SelectMany`/`GroupBy`/`OrderBy`/`Aggregate`/`CountBy`/`AggregateBy`/`Index`/`Chunk` |
| [exercise-02-deferred-vs-immediate.cs](./exercise-02-deferred-vs-immediate.cs) | 45 min | A deliberately-broken program that re-enumerates the same `IEnumerable<T>` three times. You measure the cost, materialize at the right point, prove the fix with `Stopwatch` |
| [exercise-03-pipeline-refactor.cs](./exercise-03-pipeline-refactor.cs) | 60 min | A 120-line procedural log analyser. Refactor to a 30-line LINQ pipeline that produces the same output |

## Acceptance criteria (all three)

- `dotnet build`: 0 warnings, 0 errors.
- The smoke output in each file matches (modulo timing).
- No `var x = q.ToList()` followed by re-enumeration of `q` (deferred-execution discipline).
- Every closed type hierarchy uses `abstract record` + `sealed record` cases.
- Every `switch` expression over a closed type compiles without a `_` catch-all (exhaustiveness checked by the compiler).
- Where new-in-.NET-9 LINQ helps (`CountBy`, `AggregateBy`, `Index`), use it — do not fall back to `GroupBy(...).ToDictionary(...)` if `CountBy`/`AggregateBy` fits.

## Setup

For each exercise:

```bash
mkdir Exercise01 && cd Exercise01
dotnet new console -n Exercise01 -o src/Exercise01
# Replace src/Exercise01/Program.cs with the contents of the exercise file.
dotnet run --project src/Exercise01
```

(Exercises 1, 2, and 3 do not need any additional NuGet packages — everything is in the BCL.)

## What you'll have when you're done

A small notebook of three working programs that, together, exercise every LINQ operator in the BCL you will reach for in the rest of the curriculum. Commit each exercise to your Week 5 Git repository. Future-you in Week 6 (when you build EF Core queries) will be glad you have the reference — the LINQ surface is the same; only the receiver type changes.
