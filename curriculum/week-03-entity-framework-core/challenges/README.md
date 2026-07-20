# Week 3 — Challenges

The exercises drill basics. **Challenges stretch you.** This week's challenge takes ~2 hours and produces something you can commit to your portfolio: a diagnostic notebook of three EF Core 9 performance traps, each with a measurement, a fix, and a side-by-side SQL comparison.

## Index

1. **[Challenge 1 — Complex query performance](challenge-01-complex-query-performance.md)** — diagnose three classic EF Core performance traps (the N+1, the unbounded tracked load, and the join-with-no-index) using `LogTo`, `EXPLAIN QUERY PLAN`, and `BenchmarkDotNet`; produce a written diagnosis and a fix for each. (~120 min)

Challenges are optional. If you skip them, you can still pass the week. If you do them, you'll be measurably ahead — and the diagnostic discipline you build here will pay off every time `Sharp Notes` (Week 5+) or the capstone hits a slow query.
