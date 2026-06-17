# Week 7 — Challenges

These challenges take the week's tools — `string.Create<TState>`, `Span<T>`, `stackalloc`, `ArrayPool<T>`, and `ref struct` — and ask you to combine them into a real piece of performance engineering, not just a single drill. You'll rewrite an allocation-heavy method down to a single final allocation, and design and benchmark a custom stack-only collection from scratch. Each one is design work as much as coding work, with a before/after BDN table as the deliverable. Budget 30–90 minutes per challenge, and run your benchmarks early in the day on a quiet machine.

## Ground Rules

- Work inside a real benchmark project (`dotnet new console` + BenchmarkDotNet 0.14.x on `net9.0`), in **Release** configuration, with a `results.md` written next to your `Program.cs`.
- Commit to your Week 7 GitHub repository once per challenge (each has its own suggested folder, e.g. `challenges/challenge-01-query-string/`), with a clear message.
- Each challenge separates hard **requirements** (the acceptance-criteria checkboxes — the bar you must clear) from **"Going further"** stretch goals (extra credit when you finish early, no time pressure).

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|-----------:|----------:|
| 1 | [challenge-01-rewrite-a-method-to-be-zero-alloc.md](./challenge-01-rewrite-a-method-to-be-zero-alloc.md) | A zero-intermediate-allocation rewrite of a URL query-string builder using `string.Create<TState>`, a pre-computed length, a hand-rolled RFC 3986 URL escape, and an optional `ArrayPool` snapshot | Advanced | 90–120 min |
| 2 | [challenge-02-design-and-bench-a-custom-collection.md](./challenge-02-design-and-bench-a-custom-collection.md) | A `StackList<T>` `ref struct` over `stackalloc` storage with `Add`, a `ref T` indexer, `Count`/`Capacity`, `Clear`, and `AsSpan`, benchmarked against `List<T>` and `Span<T>` | Advanced | 90 min |

## How to Submit (Self-Check)

1. Both benchmarks build and run cleanly: `dotnet run -c Release` finishes with **0 warnings, 0 errors**, and your output matches the same observable behavior as the baseline (verify the fast version equals the slow version, or that `StackList<T>` sums correctly, before the BDN run).
2. Your `results.md` contains the **before and after BDN tables** plus the short writeup each challenge asks for, and your numbers are reproducible on a re-run (document your platform — for example Apple Silicon vs x86 — so a reviewer's numbers are expected to differ in absolutes but not in ratios).
3. You have **at least one Git commit per challenge** with a clear message, and every acceptance-criteria checkbox in the challenge file is satisfied — including the allocation and mean-time targets.
