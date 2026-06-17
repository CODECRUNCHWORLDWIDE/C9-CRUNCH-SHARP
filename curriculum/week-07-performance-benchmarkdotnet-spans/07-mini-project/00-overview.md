# Mini-Project — Sharp Bench: profile a slow LINQ pipeline and make it 10× faster

> Take a deliberately-slow LINQ pipeline that ingests a million-row CSV, filters it, transforms it, aggregates it, and writes the result back out. Profile it with BenchmarkDotNet. Rewrite the hot stages using spans, pools, and explicit `for` loops. Report a ≥10× speedup with a BDN table that anyone who reads your README can reproduce on their machine. By the end you have a small, well-documented, allocation-audited library that any production team would recognize: measure first, allocate less, justify with numbers.

This is the canonical "make it fast" exercise in .NET 9. Real codebases have hot pipelines that allocate aggressively, cost real money in compute and GC, and were written before anyone profiled them. Senior engineers spend a substantial fraction of their time finding these pipelines, replacing the LINQ with `for`-loops over spans, and shipping the improvement with measured numbers in a PR description. This mini-project is that experience, in microcosm.

**Estimated time:** ~8.5 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A console + library combo called `SharpBench` that:

1. Generates a one-million-row CSV file at `data/transactions.csv` with a known seed (for reproducibility). Each row is a transaction: `id,timestamp,user_id,amount,currency,category,description,is_fraud`.
2. Defines an "analytics pipeline" with a deliberately-slow LINQ implementation in `SharpBench.Slow`. The pipeline:
   - Reads the CSV row by row.
   - Parses each row into a strongly-typed `Transaction` record.
   - Filters out fraudulent transactions (`is_fraud == true`).
   - Groups by `category`.
   - For each category, computes the total amount in USD (converting non-USD currencies via a hard-coded rate table).
   - Returns the top 10 categories by total USD spent.
3. Defines a fast version of the same pipeline in `SharpBench.Fast` that produces *bitwise-identical* output and runs ≥10× faster, with ≥90% fewer allocations.
4. A benchmark project `SharpBench.Benchmarks` with a `[MemoryDiagnoser]` BDN suite comparing the slow and fast versions across `[Params(10_000, 100_000, 1_000_000)]` row counts.
5. An xUnit test project that asserts the slow and fast versions produce identical output for any input.

You ship **one solution** with four projects:

- `src/SharpBench.Core/` — domain types (`Transaction`, `CategorySummary`), the `IClock` and `ICurrencyConverter` abstractions, the CSV generator.
- `src/SharpBench.Slow/` — the LINQ-heavy baseline.
- `src/SharpBench.Fast/` — the optimised version (spans, pools, for-loops, `Utf8Parser`).
- `src/SharpBench.Benchmarks/` — the BDN suite, plus a `[ShortRunJob]` config for fast iteration.
- `tests/SharpBench.Tests/` — xUnit tests asserting the two versions agree.

---

## Rules

- **You may** read Microsoft Learn, the `dotnet/runtime` source, lecture notes, your Week 7 exercises, BDN docs, and Stephen Toub / Adam Sitnik blog posts.
- **You may NOT** depend on any third-party NuGet package other than:
  - `BenchmarkDotNet`
  - `xUnit` + `Microsoft.NET.Test.Sdk` for tests.
  - No "fast CSV parser" libraries (`Sylvan.Data.Csv`, `CsvHelper`). You write your own. (The whole point is to learn how.)
  - No "fast collection" libraries (`CommunityToolkit.HighPerformance`, `ZString`). Same reason.
- Target framework: `net9.0`. C# language version: the default (`13.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in `Directory.Build.props`.
- The `SharpBench.Slow` implementation should be *natural* LINQ — not deliberately sabotaged, just unconsidered. The kind of code a competent engineer writes when they are not thinking about performance. The fast version is the rewrite they would do once they profiled it.

---

## Acceptance criteria

The grading rubric is below. Each box maps to a specific deliverable.

### Correctness (40%)

- [ ] `SharpBench.Slow.AnalyticsPipeline.Run(string csvPath) → IReadOnlyList<CategorySummary>` returns a list of category summaries, sorted by total USD descending, top 10 only.
- [ ] `SharpBench.Fast.AnalyticsPipeline.Run(string csvPath) → IReadOnlyList<CategorySummary>` returns exactly the same data, sorted in the same order.
- [ ] An xUnit test asserts the two outputs are equal for the standard 1M-row input. The test is reproducible (uses a seeded `Random`).
- [ ] An xUnit test asserts the two outputs are equal for an edge-case input: 0 rows, 1 row, 10 rows, 100 rows with all-fraud, 100 rows with all-USD, 100 rows with mixed currencies.
- [ ] `dotnet test` passes with 0 failed tests.

### Performance (40%)

- [ ] `SharpBench.Benchmarks.AnalyticsPipelineBench` exists and has `[MemoryDiagnoser]`.
- [ ] The benchmark has `[Params(10_000, 100_000, 1_000_000)]` for the row count.
- [ ] The benchmark runs both `SharpBench.Slow` and `SharpBench.Fast` versions.
- [ ] At the 1,000,000-row size, the `Fast` version's `Mean` is **≤ 10%** of the `Slow` version's `Mean` (i.e., ≥ 10× speedup).
- [ ] At the 1,000,000-row size, the `Fast` version's `Allocated` is **≤ 10%** of the `Slow` version's `Allocated`.
- [ ] The BDN run completes in `dotnet run -c Release` with no errors.

### Documentation (20%)

- [ ] `README.md` at the solution root contains:
  - A one-paragraph description of the project.
  - The full BDN table comparing `Slow` vs `Fast` at all three row counts.
  - A bulleted list of the **specific techniques** used in `Fast` to achieve the speedup. Each bullet cites a Week 7 lecture or a Microsoft Learn / Stephen Toub URL.
- [ ] `docs/profiling.md` contains a narrative walk-through: which allocations were the biggest sources, in what order you addressed them, what BDN run motivated each rewrite.
- [ ] Inline comments in `SharpBench.Fast` explain non-obvious choices (e.g., "we use `ArrayPool<char>` for the line buffer because the file's longest line is > 1 KB").
- [ ] Both projects (`Slow` and `Fast`) compile with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

---

## Suggested implementation outline

The order matters: build the slow version first, profile it, then rewrite.

### Day 1 (Thursday — ~2 hours)

1. Scaffold the solution: `dotnet new sln`, four projects, `Directory.Build.props`. Wire up the project references.
2. Define the domain types in `SharpBench.Core`:
   ```csharp
   public sealed record Transaction(
       long Id,
       DateTime Timestamp,
       long UserId,
       decimal Amount,
       string Currency,
       string Category,
       string Description,
       bool IsFraud);

   public sealed record CategorySummary(string Category, decimal TotalUsd, int TransactionCount);

   public interface ICurrencyConverter
   {
       decimal ToUsd(decimal amount, string fromCurrency);
   }

   public sealed class StaticCurrencyConverter : ICurrencyConverter
   {
       private static readonly Dictionary<string, decimal> Rates = new()
       {
           ["USD"] = 1.0m,
           ["EUR"] = 1.08m,
           ["GBP"] = 1.27m,
           ["JPY"] = 0.0067m,
           ["CAD"] = 0.74m,
       };
       public decimal ToUsd(decimal amount, string fromCurrency)
       {
           return Rates.TryGetValue(fromCurrency, out decimal rate)
               ? amount * rate
               : amount;
       }
   }
   ```
3. Write a CSV generator. Use `Random(42)` for reproducibility. The generator writes ~1 GB of CSV to `data/transactions.csv` once; subsequent runs read the same file. Commit a small `data/transactions-sample.csv` (10,000 rows) to the repo; the 1M-row file goes in `.gitignore`.
4. Build the `SharpBench.Slow.AnalyticsPipeline.Run` method using LINQ. The natural form:
   ```csharp
   public IReadOnlyList<CategorySummary> Run(string csvPath)
   {
       var lines = File.ReadAllLines(csvPath);
       var transactions = lines
           .Skip(1)  // header
           .Select(ParseLine)
           .Where(t => !t.IsFraud)
           .ToList();
       var grouped = transactions
           .GroupBy(t => t.Category)
           .Select(g => new CategorySummary(
               g.Key,
               g.Sum(t => _converter.ToUsd(t.Amount, t.Currency)),
               g.Count()))
           .OrderByDescending(s => s.TotalUsd)
           .Take(10)
           .ToList();
       return grouped;
   }

   private Transaction ParseLine(string line) { /* string.Split */ }
   ```
5. Write the xUnit equivalence test (which will pass trivially when only `Slow` exists; you will use it to verify `Fast` later).

### Day 2 (Friday — ~3 hours)

6. Wire up the BDN benchmark. Add `[Params(10_000, 100_000, 1_000_000)]` and `[MemoryDiagnoser]`. Run on a small input (10K) first to confirm it works.
7. Run the full benchmark on the 1M-row file. Save the table. This is your **baseline**.
8. Profile by reading the `Allocated` and `Gen0` columns. Identify the top three sources of allocations:
   - **`File.ReadAllLines`** allocates a `string[]` of 1M strings.
   - **`string.Split(',')` inside `ParseLine`** allocates a `string[8]` per row and 8 substrings per row.
   - **`Select` and `Where` and `GroupBy`** allocate enumerators and closures.
   - **`decimal.Parse` and `DateTime.Parse`** allocate when given strings instead of spans.
9. Start the `SharpBench.Fast` rewrite. The plan:
   - Read the file as a byte stream (or `StreamReader.ReadLine()` if you want to stay on chars). Avoid `ReadAllLines`.
   - Parse each row from a `ReadOnlySpan<char>` (or `ReadOnlySpan<byte>` if you go full UTF-8). Use `Utf8Parser.TryParse` for the numeric fields. Use a small inline `ReadOnlySpan<char>` for `Currency` and `Category` to avoid allocating intermediate strings.
   - Filter, aggregate, and sort with explicit `for` loops over a `List<Transaction>` (or, even faster, a `List<CategoryAccumulator>` keyed by category as you stream rows).
   - For `GroupBy`, use a `Dictionary<string, CategoryAccumulator>` where `CategoryAccumulator` is a small `struct { decimal TotalUsd; int Count; }`. Stream the rows: for each transaction, look up the category in the dictionary (allocating the key string only when the category is *new*), and add to the accumulator.

### Day 3 (Saturday — ~3 hours)

10. Re-run BDN after each significant change. Save the intermediate tables in `docs/profiling.md` so you can show the progression: baseline → after avoiding `ReadAllLines` → after switching to `Utf8Parser` → after switching the group-by to a streaming dictionary → final.
11. When the speedup hits 10×, freeze. Verify the xUnit equivalence test still passes.
12. Write up `README.md` and `docs/profiling.md`. Include the final BDN table. Cite the lectures and Microsoft Learn URLs for each technique.

### Day 4 (Sunday — ~0.5 hours)

13. Run the BDN suite one final time on a quiet machine, commit the result table to `README.md`, push.

---

## Hints

- **Generate the 1M-row file once.** Subsequent benchmark runs read the same file. Put the generation in `[GlobalSetup]`. Reading the file is part of what the pipeline measures.
- **`StreamReader.ReadLine` returns a `string`** — it allocates. To avoid that, read into a `Span<char>` buffer manually using `StreamReader.ReadBlock(Span<char>)` or `Stream.Read(Span<byte>)` followed by `Encoding.UTF8.GetChars`. The full optimised form uses a 64-KB buffer, finds `\n` with `IndexOf((byte)'\n')`, slices the row, parses it, advances. This is the pattern Kestrel uses.
- **`decimal.TryParse(ReadOnlySpan<char>, ...)`** exists. Use it. Same for `DateTime.TryParse(ReadOnlySpan<char>, ...)` and `long.TryParse(ReadOnlySpan<char>, ...)`.
- **The `Currency` column is one of 5 values.** Instead of allocating a new `string` per row, intern the 5 strings up front and compare by reference (or use a `Dictionary<string, decimal>` and reuse the key). Better: skip the string entirely — convert directly to USD inline.
- **`Category` is one of ~50 values.** Same trick: intern, or use a `Dictionary<string, CategoryAccumulator>` where the key is allocated *only when the category is new*. The dictionary's `TryGetValue` + `ref` indexer (`CollectionsMarshal.GetValueRefOrAddDefault`) is the gold-standard pattern.
- **The 10× speedup is achievable without `unsafe`, without SIMD intrinsics, without P/Invoke.** Spans, pools, `Utf8Parser`, streaming I/O, dictionary tricks. If you find yourself reaching for `unsafe`, step back — the framework primitives almost certainly cover your case.

---

## Anti-goals

The following are explicitly **not** part of this mini-project. Do not pursue them; they distract from the lesson.

- **SIMD acceleration.** Hand-vectorising `IndexOf` with `Vector128<byte>` is a 30-line piece of code that earns you another 2× speedup on top of the LINQ-rewrite 10×. It is also a separate skill. Elective.
- **Writing your own CSV grammar.** Real CSV has quoted fields, escaped commas, RFC-4180-compliant edge cases. We use a simplified grammar (no quotes, no escapes, comma-only). If you want RFC compliance, that is a different exercise.
- **Async streaming for I/O.** The 1M-row file fits in memory; sync reads are fine. If your real-world target is a 100 GB file streaming from S3, async matters. Not here.
- **A web API on top.** This is a console + library project. No HTTP. No JSON. No Kestrel.

---

## Submission

Push the solution to your Week 7 GitHub repository at `mini-project/SharpBench/`. The instructor reviews by:

1. Cloning the repo.
2. Running `dotnet test` — must pass.
3. Running `dotnet run -c Release --project src/SharpBench.Benchmarks` — must complete with the expected speedup at 1M rows.
4. Reading `README.md` and `docs/profiling.md`.

A submission whose tests pass and whose BDN run reproduces the ≥10× speedup is a pass. The most common review-fail is "the README claims 12× but the BDN run shows 3×"; verify before submitting.

---

## Stretch goals (no extra grade)

- **Vectorise the inner loop.** Use `Vector128<byte>.IndexOf((byte)'\n')` to find row boundaries 16 bytes at a time. Expect another 2-3× speedup on the I/O layer.
- **Parallelise across chunks.** Split the file into 8 chunks (one per core), parse each chunk into a per-thread `Dictionary<string, CategoryAccumulator>`, merge at the end. The lock-free merge is the interesting part. Expect another 4-6× on multi-core machines.
- **Stream output.** Instead of returning `IReadOnlyList<CategorySummary>`, write the top 10 to an `IBufferWriter<byte>` directly. Zero allocations on the result side too.
- **Memory-map the file.** Use `MemoryMappedFile.CreateFromFile` and operate over the mapped `MemoryMappedViewAccessor` as a `ReadOnlySpan<byte>`. Avoids the read-buffering layer entirely. Expect another 1.5–2× speedup, and a more complex teardown story.

The stretch goals are deliberately *harder than the main project*. Do not attempt them until the main acceptance criteria pass.

---

**References**

- BenchmarkDotNet docs: <https://benchmarkdotnet.org/>
- Microsoft Learn — `Utf8Parser`: <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.utf8parser>
- Microsoft Learn — `decimal.TryParse(ReadOnlySpan<char>, ...)`: <https://learn.microsoft.com/en-us/dotnet/api/system.decimal.tryparse>
- Microsoft Learn — `CollectionsMarshal.GetValueRefOrAddDefault`: <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.collectionsmarshal.getvaluerefornulldefault>
- Stephen Toub — "Performance Improvements in .NET 9" (CSV-relevant sections on LINQ and I/O): <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
- Adam Sitnik — "Array Pool": <https://adamsitnik.com/Array-Pool/>
- `dotnet/runtime` — `Utf8Parser` source: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Utf8Parser/Utf8Parser.cs>
