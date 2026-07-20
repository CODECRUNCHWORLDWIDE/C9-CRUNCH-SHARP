# Week 5 — Challenges

The exercises drill basics. **Challenges stretch you.** This week's challenge takes ~2 hours and produces something you can commit to your portfolio: a from-scratch reimplementation of `Where`, `Select`, and `SelectMany` over `IEnumerable<T>` — then a side-by-side comparison with the `dotnet/runtime` BCL source so you see how the BCL fuses chained iterators into a single specialized type.

## Index

1. **[Challenge 1 — Implement your own Where/Select](challenge-01-implement-your-own-where-select.md)** — write `MyWhere<T>`, `MySelect<T, U>`, and `MySelectMany<T, U>` from scratch over `IEnumerable<T>` using `yield return`; then read the BCL's `WhereSelectArrayIterator<T, U>` and explain how the fusion optimization changes the allocation profile. (~120 min)

Challenges are optional. If you skip them, you can still pass the week. If you do them, you'll be measurably ahead — re-implementing LINQ from scratch is the single fastest way to internalize the iterator pattern, and you will read every BCL LINQ method differently after you have written your own. The fusion optimization you study in step 4 is the same one that makes EF Core's expression-tree-to-SQL translator possible in Week 6.
