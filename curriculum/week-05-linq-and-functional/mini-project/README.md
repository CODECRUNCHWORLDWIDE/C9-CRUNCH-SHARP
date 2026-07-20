# Mini-Project — Procedural-to-Pipeline Refactor

> Take a 500-line procedural CSV analyser — written deliberately in the imperative C# style of 2010 — and refactor it into a LINQ pipeline that produces identical output, fits in ~80 lines, and proves on `BenchmarkDotNet` that it allocates fewer bytes and runs within 10% of the imperative version's wall-clock time. By the end you have two implementations of the same analytics report, a `BenchmarkDotNet` table showing the trade-offs, and a one-page perf note explaining where the gap is, where it isn't, and which form you would ship.

This is the canonical "modernize a legacy method" exercise in C# 13. Production codebases have hundreds of methods that look like the procedural version — written by smart engineers in 2010–2015 who did not yet have records, `with`, pattern matching, or `CountBy`. Re-writing one is the single best way to internalize the modern style; benchmarking the result is the single best way to internalize that "the modern style" and "the fast style" are usually the same thing.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A console + library combo called `CsvAnalyzer` that:

1. Reads a CSV file of fake e-commerce orders (~100,000 rows, two columns added per release of fake data — order id, customer id, country, product category, amount, timestamp).
2. Produces a structured `Report` with seven fields:
   - Total order count.
   - Total revenue.
   - Top 10 customers by total spend.
   - Revenue by country (sorted descending).
   - Order count by product category (sorted descending).
   - Average order amount per country.
   - The single largest order in the dataset.
3. Prints the report to stdout as readable text plus a JSON sidecar at `out/report.json`.

You ship **two implementations** of the analyzer:

- `src/CsvAnalyzer.Procedural/` — the imperative version, written with nested loops, mutable `Dictionary<string, decimal>` accumulators, explicit `if`/`else` chains, and no LINQ. Aim for ~500 lines. We provide the starting point in `data/StarterProcedural.cs` — you may use it verbatim, modify it, or write your own.
- `src/CsvAnalyzer.Pipeline/` — the LINQ-pipeline version. Aim for ~80 lines. Use `record` for every DTO, `CountBy` / `AggregateBy` / `Index` where they fit, `switch` expressions for branching, and exhaustive pattern matching for the optional "rejected row" handling.

Both implementations expose the same interface:

```csharp
public interface ICsvAnalyzer
{
    Report Analyze(string csvPath);
}

public sealed record Report(
    int TotalOrders,
    decimal TotalRevenue,
    IReadOnlyList<CustomerSpend> Top10Customers,
    IReadOnlyDictionary<string, decimal> RevenueByCountry,
    IReadOnlyDictionary<string, int> OrdersByCategory,
    IReadOnlyDictionary<string, decimal> AvgOrderByCountry,
    Order LargestOrder);

public sealed record Order(
    long OrderId, long CustomerId, string Country, string Category,
    decimal Amount, DateTime At);

public sealed record CustomerSpend(long CustomerId, decimal Total);
```

A console host runs them side by side and prints both reports plus a `BenchmarkDotNet` comparison.

---

## Rules

- **You may** read Microsoft Learn, the `dotnet/runtime` source, lecture notes, your Week 5 exercises, and the source of the libraries listed below.
- **You may NOT** depend on any third-party NuGet package other than:
  - `BenchmarkDotNet` (for measurement).
  - `xUnit` and `Microsoft.NET.Test.Sdk`.
  - `Bogus` (for generating the fake input CSV; optional — you can also commit a static CSV).
- **No `System.Reactive`. No `MoreLINQ`. No `language-ext`. No `PLINQ`.** The point is the .NET 9 BCL surface; not the ecosystem. Everything else is in the BCL.
- Target framework: `net9.0`. C# language version: the default for the SDK (`13.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.

---

## Acceptance criteria

- [ ] A new public GitHub repo named `c9-week-05-csv-analyzer-<yourhandle>`.
- [ ] Solution layout:
  ```
  CsvAnalyzer/
  ├── CsvAnalyzer.sln
  ├── .gitignore
  ├── Directory.Build.props
  ├── data/
  │   ├── orders.csv                  (~100,000 rows, generated or committed)
  │   └── StarterProcedural.cs        (the legacy imperative starting point)
  ├── src/
  │   ├── CsvAnalyzer.Core/
  │   │   ├── CsvAnalyzer.Core.csproj
  │   │   ├── Order.cs
  │   │   ├── Report.cs
  │   │   ├── ICsvAnalyzer.cs
  │   │   └── CsvReader.cs            (shared reader; both implementations use this)
  │   ├── CsvAnalyzer.Procedural/
  │   │   ├── CsvAnalyzer.Procedural.csproj
  │   │   └── ProceduralAnalyzer.cs   (~500 lines)
  │   ├── CsvAnalyzer.Pipeline/
  │   │   ├── CsvAnalyzer.Pipeline.csproj
  │   │   └── PipelineAnalyzer.cs     (~80 lines)
  │   ├── CsvAnalyzer.Benchmark/
  │   │   ├── CsvAnalyzer.Benchmark.csproj
  │   │   └── Program.cs              (BenchmarkDotNet runner)
  │   └── CsvAnalyzer.App/
  │       ├── CsvAnalyzer.App.csproj
  │       └── Program.cs              (runs both, prints both, dumps JSON sidecar)
  └── tests/
      └── CsvAnalyzer.Tests/
          ├── CsvAnalyzer.Tests.csproj
          ├── ReportEqualityTests.cs
          ├── ProceduralAnalyzerTests.cs
          ├── PipelineAnalyzerTests.cs
          └── ParityTests.cs           (asserts both produce identical reports)
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports **at least 20** passing tests, including the parity test that runs both analyzers against a deterministic seed and asserts the reports are equal.
- [ ] `dotnet run --project src/CsvAnalyzer.App -- data/orders.csv` prints both reports to stdout and writes `out/report.json` (a JSON dump of the pipeline analyzer's `Report`).
- [ ] `dotnet run -c Release --project src/CsvAnalyzer.Benchmark` produces a `BenchmarkDotNet` table comparing the two implementations across `Allocated` (bytes), `Mean` (μs or ms), and `Gen0` (collections per 1k ops).
- [ ] The pipeline analyzer fits in ≤ 100 lines (target: ~80). The procedural analyzer is ≥ 400 lines (target: ~500).
- [ ] The pipeline analyzer uses **at least 2 of** `CountBy`, `AggregateBy`, `Index`. It uses **at least 1** `switch` expression. It uses **at least 1** record with `with` expressions.
- [ ] The procedural analyzer uses **none** of the above (`for`/`foreach` only, mutable dictionaries, explicit `if`/`else` chains, no records — use plain classes with setters where mutability is needed).
- [ ] Every `IReadOnly*` collection on `Report` is a real `IReadOnly*` type, not a leaked mutable collection. Defensive copies if needed.
- [ ] `README.md` in the repo root includes:
  - One paragraph describing the project.
  - The exact commands to clone, build, test, and run with the sample CSV.
  - The `BenchmarkDotNet` table (paste the markdown summary).
  - A "Perf note" section of 200–300 words: where the gap is, where it isn't, which form you would ship.
  - A "Things I learned" section with at least 4 specific items about LINQ, records, or pattern matching in .NET 9.

---

## Suggested order of operations

You'll find it easier if you build incrementally rather than trying to write the whole thing at once.

### Phase 1 — Skeleton (~30 min)

```bash
mkdir CsvAnalyzer && cd CsvAnalyzer
dotnet new sln -n CsvAnalyzer
dotnet new gitignore && git init

dotnet new classlib -n CsvAnalyzer.Core       -o src/CsvAnalyzer.Core
dotnet new classlib -n CsvAnalyzer.Procedural -o src/CsvAnalyzer.Procedural
dotnet new classlib -n CsvAnalyzer.Pipeline   -o src/CsvAnalyzer.Pipeline
dotnet new console  -n CsvAnalyzer.Benchmark  -o src/CsvAnalyzer.Benchmark
dotnet new console  -n CsvAnalyzer.App        -o src/CsvAnalyzer.App
dotnet new xunit    -n CsvAnalyzer.Tests      -o tests/CsvAnalyzer.Tests

# Add to solution and wire references — see Acceptance criteria for the layout.
dotnet add src/CsvAnalyzer.Benchmark package BenchmarkDotNet
```

Commit: `Skeleton: 5 projects + Directory.Build.props`.

### Phase 2 — Shared types and CSV reader (~45 min)

Define `Order`, `Report`, `CustomerSpend`, and `ICsvAnalyzer` in `CsvAnalyzer.Core`. Write a `CsvReader.ReadAsync(string path)` helper that yields `IAsyncEnumerable<Order>` — you do not need async for the analysis, but the reader being async-friendly means you can compose it with the Week 4 pipeline patterns later. (For simplicity in the analyzers themselves, drop to `IEnumerable<Order>` via `.ToBlockingEnumerable()` or simply expose a synchronous `Read(path) -> IEnumerable<Order>` overload.)

Commit: `Core: Order, Report, CsvReader`.

### Phase 3 — Generate or commit the input CSV (~30 min)

If you use `Bogus`, write a small generator that produces 100,000 rows with:

- `OrderId` = monotonic Guid-derived `long`.
- `CustomerId` = uniform random 1..10,000.
- `Country` = one of 12 fixed countries.
- `Category` = one of 8 fixed categories.
- `Amount` = log-normal distribution, mean ~$50.
- `At` = uniform random in the last 365 days.

Otherwise commit a static `data/orders.csv` (≤ 5 MB). Either way, the CSV must be deterministic — every run of the analyzer must produce the same `Report` on the same input.

Commit: `Data: 100k row orders.csv (deterministic)`.

### Phase 4 — Procedural analyzer (~90 min)

In `CsvAnalyzer.Procedural/ProceduralAnalyzer.cs`, write the imperative version. The shape:

```csharp
public sealed class ProceduralAnalyzer : ICsvAnalyzer
{
    public Report Analyze(string csvPath)
    {
        var orders = new CsvReader().Read(csvPath).ToList();

        // 1. Total count and total revenue (one foreach).
        int total = 0;
        decimal totalRevenue = 0m;
        foreach (var o in orders) { total++; totalRevenue += o.Amount; }

        // 2. Top 10 customers (mutable dictionary, then sort + take).
        var customerSpend = new Dictionary<long, decimal>();
        foreach (var o in orders)
        {
            if (customerSpend.TryGetValue(o.CustomerId, out var prev))
                customerSpend[o.CustomerId] = prev + o.Amount;
            else
                customerSpend[o.CustomerId] = o.Amount;
        }
        var top10 = new List<CustomerSpend>();
        // ... sort customerSpend by value descending, take 10, convert to CustomerSpend.

        // 3. Revenue by country (same pattern).
        // 4. Orders by category (same pattern with int instead of decimal).
        // 5. Average order by country (sum then divide — careful with zero division).
        // 6. Largest order (track max during the first foreach).

        return new Report(total, totalRevenue, top10, revenueByCountry, ordersByCategory, avgByCountry, largestOrder);
    }
}
```

Aim for the verbose, careful, easy-to-review imperative style. Avoid LINQ entirely — your `Dictionary.OrderByDescending(...)` calls at the end count as LINQ, so write a small `static SortDescendingByValue(Dictionary<TKey, TValue>) -> List<KeyValuePair<TKey, TValue>>` helper using `Array.Sort` if you want to be strict. (In practice the file ends up ~500 lines either way.)

Commit: `Procedural analyzer: ~500 lines of imperative C#`.

### Phase 5 — Pipeline analyzer (~90 min)

In `CsvAnalyzer.Pipeline/PipelineAnalyzer.cs`, write the LINQ-pipeline version. The shape:

```csharp
public sealed class PipelineAnalyzer : ICsvAnalyzer
{
    public Report Analyze(string csvPath)
    {
        var orders = new CsvReader().Read(csvPath).ToList();

        var top10 = orders
            .AggregateBy(
                keySelector: o => o.CustomerId,
                seed:        0m,
                func:        (acc, o) => acc + o.Amount)
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .Select(kvp => new CustomerSpend(kvp.Key, kvp.Value))
            .ToList();

        var revenueByCountry = orders
            .AggregateBy(
                keySelector: o => o.Country,
                seed:        0m,
                func:        (acc, o) => acc + o.Amount)
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var ordersByCategory = orders
            .CountBy(o => o.Category)
            .OrderByDescending(kvp => kvp.Value)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var avgByCountry = revenueByCountry.Keys.ToDictionary(
            country => country,
            country =>
            {
                var matching = orders.Where(o => o.Country == country).ToList();
                return matching.Count == 0 ? 0m : matching.Sum(o => o.Amount) / matching.Count;
            });

        var largestOrder = orders.MaxBy(o => o.Amount)
            ?? throw new InvalidOperationException("Empty input.");

        return new Report(
            TotalOrders:       orders.Count,
            TotalRevenue:      orders.Sum(o => o.Amount),
            Top10Customers:    top10,
            RevenueByCountry:  revenueByCountry,
            OrdersByCategory:  ordersByCategory,
            AvgOrderByCountry: avgByCountry,
            LargestOrder:      largestOrder);
    }
}
```

Read this carefully:

- `AggregateBy` twice — for top-10 customers and revenue by country.
- `CountBy` once — for orders by category.
- The "average by country" step is the messy one — it groups by country, but the average can't be expressed in `AggregateBy` alone (you need both sum and count). The cleanest form uses a tuple accumulator: `AggregateBy(o => o.Country, (Sum: 0m, Count: 0), (acc, o) => (acc.Sum + o.Amount, acc.Count + 1))` then projects. Improve this if you have the time.
- `MaxBy(o => o.Amount)` for the largest order — one of the .NET 6+ helpers.

The whole thing should fit in 60–100 lines. Commit when it produces a `Report` that equals the procedural one on the same input.

Commit: `Pipeline analyzer: ~80 lines of LINQ + records`.

### Phase 6 — Tests, especially the parity test (~75 min)

The most important test is `ParityTests.cs`:

```csharp
[Fact]
public void BothAnalyzers_ProduceTheSameReport()
{
    var procedural = new ProceduralAnalyzer();
    var pipeline   = new PipelineAnalyzer();
    var path = TestData.SampleCsvPath; // 100k rows

    var r1 = procedural.Analyze(path);
    var r2 = pipeline.Analyze(path);

    Assert.Equal(r1.TotalOrders,        r2.TotalOrders);
    Assert.Equal(r1.TotalRevenue,       r2.TotalRevenue);
    Assert.Equal(r1.Top10Customers,     r2.Top10Customers);
    AssertDictEqual(r1.RevenueByCountry, r2.RevenueByCountry);
    AssertDictEqual(r1.OrdersByCategory, r2.OrdersByCategory);
    AssertDictEqual(r1.AvgOrderByCountry, r2.AvgOrderByCountry);
    Assert.Equal(r1.LargestOrder,       r2.LargestOrder);
}
```

Then per-analyzer tests covering: empty input, single-order input, tie-breakers in the top-10 (verify the order is deterministic), Unicode in country names, very large amounts (`decimal.MaxValue / 2`).

Target: at least **20 passing tests**. `dotnet test` should be green.

Commit per file as you write each cluster.

### Phase 7 — Benchmark (~60 min)

In `CsvAnalyzer.Benchmark/Program.cs`:

```csharp
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AnalyzerBenchmarks
{
    private readonly string _path = "../../../../data/orders.csv";
    private readonly ProceduralAnalyzer _proc = new();
    private readonly PipelineAnalyzer   _pipe = new();

    [Benchmark(Baseline = true)] public Report Procedural() => _proc.Analyze(_path);
    [Benchmark]                  public Report Pipeline()   => _pipe.Analyze(_path);
}

BenchmarkRunner.Run<AnalyzerBenchmarks>();
```

Run with `dotnet run -c Release --project src/CsvAnalyzer.Benchmark`. Capture the markdown summary. The pipeline version should be within 10% of the procedural's wall-clock time and allocate within 2× the bytes — the `CountBy`/`AggregateBy` form is much closer to the procedural form than the pre-.NET-9 `GroupBy(...).ToDictionary(...)` form would have been.

Commit: `Benchmark harness + first run`.

### Phase 8 — Perf note + README polish (~30 min)

Write the perf note (200–300 words) in the root `README.md`:

```markdown
## Perf note

The pipeline implementation runs in 1.1× the procedural's wall-clock time and
allocates 1.6× the bytes on 100,000-row inputs. The gap is dominated by:

1. The CsvReader allocations are identical in both forms — that's the floor.
2. The pipeline version allocates one `Order` record per row plus one
   `KeyValuePair<TKey, TValue>` per group per `CountBy`/`AggregateBy` output.
   The procedural version allocates the same `Order` records (we use the same
   reader) plus one `Dictionary<TKey, TValue>` entry per group — slightly
   less overhead per entry.
3. The `MaxBy(o => o.Amount)` call walks the full list with a comparator;
   the procedural version's `if (o.Amount > best.Amount) best = o;` is one
   comparison per element. The cost is the same; the readability is not.

I would ship the pipeline version. The 10% wall-clock cost and 1.6× allocation
cost are invisible at this scale (1.1 ms vs 1.0 ms on a hot CPU; ~3.5 MB total
allocations across the whole report) and the maintenance cost of the 500-line
procedural form is real. If we ever needed to add an eighth report field, the
pipeline version takes 4 new lines; the procedural form takes ~40.

If this analyzer ran on a hot path that processed 10,000,000 rows every
minute, the calculus might flip. But this is a batch report; the cost is
amortized over thousands of users; readability wins.
```

Add the "Things I learned" section. Run `dotnet format`. Commit.

Commit: `Perf note + README polish`.

---

## Example expected output

```text
$ dotnet run --project src/CsvAnalyzer.App -- data/orders.csv
== Procedural report ==
  TotalOrders:       100000
  TotalRevenue:      $5,124,876.12
  Top10Customers:    [{1247: $4823}, {889: $4612}, ...]
  RevenueByCountry:  US:$1,876,543  UK:$612,003  DE:$453,128  ...
  OrdersByCategory:  electronics:21456  books:18203  ...
  AvgOrderByCountry: US:$54.21  UK:$48.92  ...
  LargestOrder:      Order { OrderId = 8472..., Amount = $4,876.42 }
  (procedural elapsed: 1086 ms)

== Pipeline report ==
  TotalOrders:       100000
  TotalRevenue:      $5,124,876.12
  ...
  (pipeline elapsed:   1183 ms)

Reports equal: True
```

And the benchmark table:

```text
| Method     | Mean      | Ratio | Allocated  | Gen0    |
|------------|----------:|------:|-----------:|--------:|
| Procedural |   12.3 ms |  1.00 |   2,134 KB |   30.42 |
| Pipeline   |   13.6 ms |  1.11 |   3,412 KB |   48.71 |
```

(Your numbers will vary by ±30% depending on hardware. The gap should be in the same direction.)

---

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 10% | `dotnet build`, `dotnet test`, both `dotnet run --project ...App` and `Benchmark` clean on a fresh clone |
| Parity correctness | 25% | The parity test passes on every seed; both analyzers produce identical `Report` instances field-by-field |
| Pipeline brevity | 15% | The pipeline analyzer is ≤ 100 lines; uses ≥ 2 of `CountBy`/`AggregateBy`/`Index`; uses ≥ 1 `switch` expression; uses ≥ 1 record |
| Tests | 20% | ≥ 20 tests; cover empty input, single row, ties, Unicode, edge values |
| Benchmark | 15% | `BenchmarkDotNet` table in the README; pipeline within 1.2× wall-clock and within 2× allocations of procedural |
| Perf note | 10% | 200–300 words; honest about trade-offs; identifies the dominant source of the gap; states a clear "I would ship X" |
| README | 5% | A new developer can clone, build, test, and exercise both analyzers in under 10 minutes |

---

## Stretch (optional)

- **Add a third analyzer** — `CsvAnalyzer.Span` — that uses `Span<char>` parsing and a custom CSV reader to skip the `string.Split` allocation. Benchmark all three. The `Span` version should be measurably faster on the parsing step; the analysis step is unchanged.
- **Make the input async.** Convert `CsvReader.Read(...) -> IEnumerable<Order>` to `CsvReader.ReadAsync(...) -> IAsyncEnumerable<Order>`. Push the async surface through both analyzers. Discover which LINQ operators have `*Async` counterparts in `System.Linq.Async` (NuGet) and which you have to write yourself.
- **Add a streaming output mode.** Instead of building a `Report`, stream the analysis as `IAsyncEnumerable<ReportRow>` via Server-Sent Events from an ASP.NET Core 9 minimal API. This is a preview of Week 6 territory.
- **Plot the distribution.** Generate a histogram of order amounts per country using one of `System.Drawing.Common` (cross-platform-but-not-Linux), `SkiaSharp` (fully cross-platform), or just an ASCII art histogram. The pipeline form makes histogram construction one line per bin via `CountBy(o => Math.Floor(o.Amount / 10))`.
- **Compare to `GroupBy + ToDictionary`.** Implement a third version that uses the pre-.NET-9 `GroupBy + ToDictionary` pattern. Benchmark all three. The pre-.NET-9 form should allocate ~2× the `CountBy`/`AggregateBy` form.

---

## What this prepares you for

- **Week 6** introduces EF Core's `IQueryable<T>` translator. Every operator you used in `PipelineAnalyzer` — `Where`, `Select`, `AggregateBy`, `CountBy`, `OrderByDescending`, `Take`, `Sum`, `MaxBy` — has an EF Core translator that decides whether it can or cannot be emitted as SQL. The pipeline form translates almost directly; the procedural form cannot translate at all (it would pull every row into memory). The reflex you build this week is what makes EF Core code performant in week 6.
- **Week 8** introduces SignalR and background workers. The "produce a report from a stream of events" pattern reappears as "build a live dashboard from a `Channel<Event>`." The pipeline operators compose the same way.
- **The capstone** (Week 15+) has at least one report similar to this one. The pattern compounds.

---

## Resources

- *`Enumerable.CountBy` reference*: <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.countby>
- *`Enumerable.AggregateBy` reference*: <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.aggregateby>
- *`BenchmarkDotNet` getting started*: <https://benchmarkdotnet.org/articles/guides/getting-started.html>
- *"Performance improvements in .NET 9"* — Stephen Toub: <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
- *`MaxBy` / `MinBy` reference* (since .NET 6): <https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.maxby>

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes the setup commands, the BenchmarkDotNet table, the perf note, and 4 "things I learned" specific to LINQ / records / patterns / .NET 9.
3. Make sure `dotnet build`, `dotnet test`, `dotnet run --project src/CsvAnalyzer.App`, and `dotnet run -c Release --project src/CsvAnalyzer.Benchmark` all green on a fresh clone.
4. Post the repo URL in your cohort tracker. You shipped a real refactor; show it.
