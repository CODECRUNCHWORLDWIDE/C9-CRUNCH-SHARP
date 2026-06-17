# Week 8 — Exercises

Three focused C# exercises that take this week's production-async theory and put it under your fingers: a hand-built `SynchronizationContext` that lets you watch `.ConfigureAwait(false)` switch threads, a cancellation-aware `IAsyncEnumerable<int>`, and a bounded-channel pipeline that shows backpressure in real timestamps. Each file is a self-contained console program with TODOs to fill in, acceptance criteria to hit, and reflection questions to answer once it runs clean.

## How to Run an Exercise

Each exercise is a single `.cs` file meant to become the `Program.cs` of a fresh .NET 8 console project. The header comment in every file spells out the exact scaffold; the shape is always the same:

1. Open a terminal in the `exercises/` folder.
2. Scaffold a console project and drop the exercise in as `Program.cs`, for example:

   ```bash
   mkdir Ex01-ConfigureAwait && cd Ex01-ConfigureAwait
   dotnet new console -n Ex01 -o src/Ex01 --framework net8.0
   cd src/Ex01
   # replace src/Ex01/Program.cs with the contents of the exercise file
   ```

3. Fill in the TODOs, then run in Release:

   ```bash
   dotnet run -c Release
   ```

The "build succeeded · 0 warnings · 0 errors" contract holds: a passing exercise compiles clean and prints output that matches the file's "Expected output" block. After it runs, answer the reflection questions in a `results-exNN.md` file alongside your project.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|-----------:|----------:|
| 1 | [exercise-01-configureawait-and-the-context.cs](./exercise-01-configureawait-and-the-context.cs) | Building a single-threaded `SynchronizationContext`, implementing `Post`/`Send`, and watching `.ConfigureAwait(false)` move continuations off the captured context onto the ThreadPool | Intermediate+ | 60 min |
| 2 | [exercise-02-iasyncenumerable-cancellation.cs](./exercise-02-iasyncenumerable-cancellation.cs) | Writing a streaming `async IAsyncEnumerable<int>` with `[EnumeratorCancellation]` and proving cancellation reaches the iterator across three consumer patterns (parameter, `WithCancellation`, and linked tokens) | Intermediate | 60 min |
| 3 | [exercise-03-bounded-channel-pipeline.cs](./exercise-03-bounded-channel-pipeline.cs) | Constructing a producer → bounded channel → 4 consumers → aggregator pipeline with `FullMode.Wait`, observing backpressure in logged timestamps, and completing the writer in `finally` | Advanced | 75 min |

## Checking Your Work

Annotated reference solutions live in [SOLUTIONS.md](./SOLUTIONS.md) — read them only after you finish, since the annotations are where the senior-engineer judgement lives. For each exercise, confirm that:

- `dotnet run -c Release` succeeds with 0 warnings and 0 errors, and every box in the file's acceptance-criteria list is met.
- The printed output matches the "Expected output" block (thread IDs and item counts vary slightly, but the pattern — which await resumes where, the `4950` sum, the `PASS` line — must hold).
- You can answer each reflection question in your own words; if one stumps you, re-read the cited lecture section before peeking at SOLUTIONS.md.
