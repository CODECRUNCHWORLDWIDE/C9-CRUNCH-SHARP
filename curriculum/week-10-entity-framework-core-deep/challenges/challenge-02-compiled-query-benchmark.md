# Challenge 2 — Compile a Hot Query and Measure the Saving

> **Estimated time:** 90 minutes. **Prerequisites:** Lecture 3 (compiled queries) and Exercise 2 (BenchmarkDotNet workflow). **Deliverable:** a `BenchmarkDotNet` project that measures the per-call cost of an uncompiled and compiled version of the same query, plus a one-page write-up of when the optimisation is and is not worth it.

## Statement of the problem

The EF Core team documents that compiled queries save 20-30 microseconds per call on common LINQ shapes. Microsoft Learn says so: <https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries>. Stephen Toub's "Performance Improvements in .NET 8" notes that the .NET 8 translation pipeline got 20% faster on its own, weakening (but not eliminating) the case for compilation. Your job is to reproduce the measurement on your own machine, plot the result, and write a recommendation.

The headline question: at what call rate does compilation become worth the static-field plumbing? The implicit answer is "thousands of calls per second"; the explicit answer comes from your numbers.

## What you will build

A `BenchmarkDotNet` project with two benchmarks measuring the same query — "get a customer by email, with their top 5 most recent orders included." One benchmark uses the LINQ-only form; the other uses `EF.CompileAsyncQuery`.

```
src/Challenge02.CompiledQuery/
  Challenge02.CompiledQuery.csproj
  Program.cs
  CatalogDb.cs
```

The .csproj additions:

```xml
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
```

## The schema and seed

A small `Customer` / `Order` schema, 1,000 customers, 20 orders each:

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
    public decimal Total { get; set; }
}
```

Seed in `[GlobalSetup]`. Use a deterministic random seed so the benchmark reruns are comparable.

## The two benchmark methods

```csharp
[MemoryDiagnoser]
public class CompiledQueryBenchmark
{
    private DbContextOptions<CatalogDb> _options = null!;
    private string _email = null!;

    // The compiled query lives in a static field — translated once at program
    // startup, reused on every call.
    private static readonly Func<CatalogDb, string, Task<Customer?>> _byEmailCompiled =
        EF.CompileAsyncQuery((CatalogDb db, string email) =>
            db.Customers
              .AsNoTracking()
              .Include(c => c.Orders.OrderByDescending(o => o.PlacedAt).Take(5))
              .FirstOrDefault(c => c.Email == email));

    [GlobalSetup]
    public async Task Setup() { /* seed, set _email to one that exists */ }

    [Benchmark(Baseline = true)]
    public async Task<Customer?> Uncompiled()
    {
        using var db = new CatalogDb(_options);
        return await db.Customers
            .AsNoTracking()
            .Include(c => c.Orders.OrderByDescending(o => o.PlacedAt).Take(5))
            .FirstOrDefaultAsync(c => c.Email == _email);
    }

    [Benchmark]
    public async Task<Customer?> Compiled()
    {
        using var db = new CatalogDb(_options);
        return await _byEmailCompiled(db, _email);
    }
}
```

## Acceptance criteria

1. **Both benchmarks return the same customer.** Add an `[IterationSetup]` or a simple sanity assert that the two methods produce equivalent results on the same input.
2. **The compiled benchmark is at least 15% faster than the uncompiled benchmark** on a `Where + Include + FirstOrDefault` shape on .NET 8. If your numbers do not show this, profile to confirm the translation step is the bottleneck (`dotnet-trace collect --providers Microsoft-Diagnostics-DiagnosticSource`).
3. **The compiled benchmark allocates less per call** than the uncompiled benchmark. The translation pipeline allocates `Expression` trees per call when uncompiled; the compiled form bypasses that.
4. **Write-up:** one page (`WRITEUP.md`) with:
   - Your measured numbers (mean, error, allocation) for both methods.
   - A computed "break-even call rate" — at what calls-per-second does the saving become 1ms of CPU per second.
   - Your recommendation: would you compile this specific query? Why or why not?
   - A second recommendation: name two query shapes you would *not* compile, and explain why (hint: queries with dynamically-varying `Where` clauses, queries that depend on captured-variable values that change semantics).

## The measurement table you should produce

| Method     | Mean       | Allocated | Notes                                      |
|------------|-----------:|----------:|--------------------------------------------|
| Uncompiled |    94.2 us |   12.4 KB | Includes the LINQ-to-SQL translation step  |
| Compiled   |    71.5 us |    8.8 KB | Translation cached in the static delegate  |
| Delta      |   -22.7 us |   -3.6 KB | ~24% time saving, ~29% allocation saving   |

The numbers vary; the ratio should be in the 15-30% range on a `Where + Include + FirstOrDefault` shape.

## A trap to watch for

Compiled queries are cached on the delegate identity, not on a query-string hash. If you re-write the LINQ slightly between runs, the cache miss is silent — you get a freshly-translated delegate and pay the translation cost again. The benchmark must call **the same** compiled delegate every iteration; do not put `EF.CompileAsyncQuery(...)` inside the benchmark method. The static-field pattern in the example above is the load-bearing detail.

## A second trap: captured variables that change

The compiled-query delegate takes the parameters EF compiled in. If your LINQ has a captured variable that is not a delegate parameter, the captured value is **baked into the translation** at compile time. Subsequent calls with a different value of the captured variable still use the old translation, which (depending on the variable's role) may produce wrong results.

The fix: every variable that varies per call must be a parameter to the `CompileAsyncQuery` lambda. The lecture-3 example shows this with `categoryId`; the challenge here shows it with `email`. Anything else captured in the lambda's scope is frozen.

## Stretch: a `dotnet-counters` measurement

While the benchmark runs, in a second terminal, run:

```bash
dotnet-counters monitor --process-id <pid> Microsoft.EntityFrameworkCore
```

Watch `compiled-query-cache-hit-rate`. With the compiled-query pattern, you should see this counter at 100%. With the uncompiled pattern, you should see it climb from 0% to 99% as the benchmark warm-up fills the translation cache (the EF Core 8 translation cache catches the second-and-subsequent translations of the same LINQ shape even without explicit `CompileAsyncQuery`).

The interesting observation: **EF Core 8 already caches translations**. The compiled-query advantage is partly a *first-call* advantage and partly a *cache-bypass* advantage on the hot path. Your write-up should note this and explain when the explicit compilation buys you something the implicit cache does not.

## Submission

Submit:

- The `BenchmarkDotNet` project, runnable with `dotnet run -c Release`.
- `WRITEUP.md` with your numbers, your recommendation, your two "do not compile" examples, and the `dotnet-counters` observations.
- A short comment block in `Program.cs` linking to Stephen Toub's "Performance Improvements in .NET 8" post and the Microsoft Learn compiled-queries page.

The rubric:

- (40%) Benchmark is correctly structured (static-field delegate, no per-iteration `CompileAsyncQuery`, sensible warm-up).
- (30%) Write-up correctly explains the 20-30us cost source (translation pipeline) and the EF Core 8 implicit cache nuance.
- (20%) The two "do not compile" examples are well-chosen and well-defended.
- (10%) Citations are present and correct.
