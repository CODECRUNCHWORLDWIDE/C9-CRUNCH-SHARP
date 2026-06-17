# Week 10 — Challenges

The exercises gave you the moves; these challenges make you measure them under load and defend the trade-offs in writing. Both are build-and-benchmark problems: you stand up a real schema, run the same workload several ways, capture the SQL log and the timings, and produce a one-page write-up that explains *why* the numbers came out the way they did. This is the work that turns "I added `.AsNoTracking()` and it felt faster" into a measured, citable recommendation your whole team can trust.

## Ground Rules

- **Measure, do not guess.** Every claim in your write-up must be backed by a number you produced — a SQL statement count, a wire-row count, a BenchmarkDotNet mean, an allocation figure. The ratios are the point; absolute timings vary by machine and provider.
- **Read the generated SQL.** Capture the `LogTo` output (or `ToQueryString()`) for every strategy and confirm the shape matches what you claim — the JOIN, the three split statements, the correlated subquery, the cached compiled delegate.
- **Cite your sources.** Each technique you use should carry its Microsoft Learn URL (or `dotnet/efcore` source link). A recommendation without a citation is an opinion.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|------------|-----------|
| 1 | [challenge-01-cartesian-explosion-and-split.md](./challenge-01-cartesian-explosion-and-split.md) | A console program over a two-collection (`Orders` + `Addresses`) schema that measures single-query vs `AsSplitQuery` vs projection, captures the `MultipleCollectionIncludeWarning`, and writes up the cartesian-explosion trade-off | Advanced | 90 min |
| 2 | [challenge-02-compiled-query-benchmark.md](./challenge-02-compiled-query-benchmark.md) | A BenchmarkDotNet project comparing an uncompiled `Where + Include + FirstOrDefault` query against an `EF.CompileAsyncQuery` version, computing the break-even call rate and the EF Core 8 implicit-cache nuance | Advanced | 90 min |

## How to Submit (Self-Check)

1. **Ship the code and the proof.** Submit the `Program.cs` / `CatalogDb.cs` (and `SalesDb.cs` where applicable) that reproduce your measurement table on `dotnet run` (or `dotnet run -c Release` for the benchmark), plus the pasted SQL log or `dotnet-counters` output that proves each strategy's shape.
2. **Write the one-pager.** Include the `WRITEUP.md` the challenge asks for, with your measured numbers, your explanation of *why* each strategy behaves as it does, your defended recommendation, and correct citations.
3. **Grade yourself against the rubric.** Each challenge lists a rubric (correctness of measurements, quality of the explanation, the defensibility of your recommendation, and citations). Read it back and confirm you would pass your own review before calling it done.
