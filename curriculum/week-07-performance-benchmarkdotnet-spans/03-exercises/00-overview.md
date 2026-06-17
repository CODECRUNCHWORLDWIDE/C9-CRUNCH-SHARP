# Week 7 — Exercises

These exercises drill the measurement-first habits this week is built on: scaffolding a clean BenchmarkDotNet project, decorating it with `[MemoryDiagnoser]`, and reading the resulting Markdown table the way a senior .NET engineer does. Each file is a fill-in-the-TODOs benchmark that compares a "junior" baseline against a zero- or low-allocation rewrite using `string.Create`, `ReadOnlySpan<char>`, and `ArrayPool<T>`. Aim to finish each one in a single sitting, and don't peek at the inline hints until you've genuinely tried.

## How to Run an Exercise

Each exercise is a standalone BenchmarkDotNet console project. The file's header comment has the exact scaffolding commands, but the shape is always the same:

1. Create a fresh console project and add the package:

   ```bash
   mkdir Ex01-StringConcat && cd Ex01-StringConcat
   dotnet new console -n Ex01 -o src/Ex01 --framework net9.0
   cd src/Ex01
   dotnet add package BenchmarkDotNet --version 0.14.0
   ```

2. Replace the generated `Program.cs` with the contents of the exercise file and fill in the `// YOUR CODE HERE` TODOs.

3. Run in **Release** (benchmarks are meaningless in Debug):

   ```bash
   dotnet run -c Release
   ```

BDN prints a Markdown result table at the end. Paste it into a `results-exNN.md` file next to your `Program.cs` and answer the reflection questions in the file's footer.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|-----------:|----------:|
| 1 | [exercise-01-benchmark-string-concat.cs](./exercise-01-benchmark-string-concat.cs) | Building a `[MemoryDiagnoser]` benchmark with `[Params]` and a `[Benchmark(Baseline = true)]`; proving `+=` is O(N²) versus `StringBuilder`, `string.Concat`, and `string.Create` over a `Span<char>` | Intermediate | 60 min |
| 2 | [exercise-02-implement-readonlyspan-parser.cs](./exercise-02-implement-readonlyspan-parser.cs) | Writing a zero-allocation `TryParseCsvRow` over `ReadOnlySpan<char>` with `out` span params and `IndexOf`/slicing; benchmarking it against `string.Split(',')` | Intermediate | 45 min |
| 3 | [exercise-03-pool-then-rent-then-return.cs](./exercise-03-pool-then-rent-then-return.cs) | Renting and returning an `ArrayPool<char>` buffer in `try/finally` for a hex encoder; comparing against a `StringBuilder` baseline and `Convert.ToHexString` | Intermediate+ | 45 min |

## Checking Your Work

Annotated reference solutions, the target BDN tables, and full answers to the reflection questions live in [SOLUTIONS.md](./SOLUTIONS.md). Attempt every TODO yourself before opening it — the learning is in the struggle and in reading your own numbers. When you compare against the reference, check that:

- Your **allocation ratios** match the solution within ~10–15% (absolute times may differ by up to ~2× depending on your CPU; the ratios should not).
- The low-allocation method shows **0 B** (or "approximately one final string per call") in the `Allocated` column, while the baseline shows the expected garbage — for example `Plus` at N=1000 allocating well over 1 MB per call.
- `dotnet run -c Release` completes with **0 warnings, 0 errors**, and every acceptance-criteria checkbox in the file header is satisfied.
