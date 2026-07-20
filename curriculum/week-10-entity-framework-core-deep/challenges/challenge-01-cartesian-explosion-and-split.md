# Challenge 1 — Cartesian Explosion and the `AsSplitQuery` Cure

> **Estimated time:** 90 minutes. **Prerequisites:** Lecture 2 (loading strategies and the N+1 problem). **Deliverable:** a small console program plus a one-page write-up that reports the measured trade between single-query and split-query strategies on a schema with two sibling collections.

## Statement of the problem

You are given a `Customer` entity with two collection navigations: `Orders` and `Addresses`. A request comes in for "list every customer with all their orders and all their addresses." You write the obvious eager-loading query:

```csharp
var customers = await db.Customers
    .AsNoTracking()
    .Include(c => c.Orders)
    .Include(c => c.Addresses)
    .ToListAsync();
```

The endpoint works correctly, but a colleague's load test reports it is twenty times slower than the equivalent endpoint that loads only orders, even though the additional data per customer is small (3 addresses vs 10 orders). Your job is to (a) explain the slowdown, (b) measure it on the same dataset, (c) apply the `AsSplitQuery` cure, and (d) write up the trade-off with the measured numbers.

## What you will build

A console program with one `DbContext`, one seed routine, and three measured loading strategies. Project layout:

```
src/Challenge01.Cartesian/
  Challenge01.Cartesian.csproj
  Program.cs
  SalesDb.cs
```

The schema:

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
    public List<Address> Addresses { get; set; } = new();
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Total { get; set; }
    public DateTime PlacedAt { get; set; }
}

public sealed class Address
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Line1 { get; set; } = "";
    public string City { get; set; } = "";
}
```

The seed: 100 customers, each with 10 `Order` rows and 3 `Address` rows. That gives you a cartesian factor of `10 * 3 = 30` per customer if you use a single-query include for both collections.

## The three strategies to measure

### Strategy A — Single query (the wrong answer)

```csharp
public static async Task<int> StrategyA_SingleQuery(SalesDb db)
{
    var sw = Stopwatch.StartNew();
    var customers = await db.Customers
        .AsNoTracking()
        .Include(c => c.Orders)
        .Include(c => c.Addresses)
        .ToListAsync();
    sw.Stop();
    Console.WriteLine($"  StrategyA SingleQuery: {sw.ElapsedMilliseconds} ms, {customers.Count} customers");
    return customers.Sum(c => c.Orders.Count + c.Addresses.Count);
}
```

Expected behaviour: one SQL statement, a `LEFT JOIN` of `Customers` against both `Orders` and `Addresses`, **3,000 rows on the wire** (100 customers x 10 orders x 3 addresses), 30-50ms wall time. EF Core 8 also emits a runtime warning of type `RelationalEventId.MultipleCollectionIncludeWarning` — capture it.

### Strategy B — Split query

```csharp
public static async Task<int> StrategyB_SplitQuery(SalesDb db)
{
    var sw = Stopwatch.StartNew();
    var customers = await db.Customers
        .AsNoTracking()
        .Include(c => c.Orders)
        .Include(c => c.Addresses)
        .AsSplitQuery()
        .ToListAsync();
    sw.Stop();
    Console.WriteLine($"  StrategyB SplitQuery: {sw.ElapsedMilliseconds} ms, {customers.Count} customers");
    return customers.Sum(c => c.Orders.Count + c.Addresses.Count);
}
```

Expected behaviour: three SQL statements, 1,400 rows on the wire (100 customers + 1,000 orders + 300 addresses), 15-25ms wall time. No multiple-include warning.

### Strategy C — Projection (the often-best answer)

```csharp
public static async Task<int> StrategyC_Projection(SalesDb db)
{
    var sw = Stopwatch.StartNew();
    var summaries = await db.Customers
        .AsNoTracking()
        .Select(c => new
        {
            c.Id,
            c.Name,
            OrderCount = c.Orders.Count,
            AddressCount = c.Addresses.Count,
            TotalSpent = c.Orders.Sum(o => o.Total),
        })
        .ToListAsync();
    sw.Stop();
    Console.WriteLine($"  StrategyC Projection: {sw.ElapsedMilliseconds} ms, {summaries.Count} summaries");
    return summaries.Sum(s => s.OrderCount + s.AddressCount);
}
```

Expected behaviour: one SQL statement, 100 rows on the wire (only the projected fields), 5-10ms wall time. No entities materialized, no joins, no cartesian. This is the right answer when the endpoint returns summaries and not the entities themselves.

## Acceptance criteria

1. **The single-query strategy emits exactly one SQL statement with two `LEFT JOIN`s.** Confirm by reading the SQL log.
2. **The single-query strategy emits the `MultipleCollectionIncludeWarning`** to the configured logger. Capture it with `options.ConfigureWarnings(w => w.Log(RelationalEventId.MultipleCollectionIncludeWarning))` and prove the warning appears.
3. **The split-query strategy emits exactly three SQL statements.** Confirm by reading the log.
4. **The split-query strategy is at least 30% faster than the single-query strategy** on the seeded dataset. If your local Postgres is so fast the gap shrinks, scale the seed up to 500 customers and re-measure; the ratio should still hold.
5. **The projection strategy is at least 50% faster than the split-query strategy** because no entity rows cross the wire.
6. **Write-up:** one page of Markdown (`WRITEUP.md` alongside `Program.cs`) describing the trade-off, citing the Microsoft Learn page, and naming the conditions under which you would still use a single-query include (single reference navigation; single collection where the cartesian factor is 1).

## The measurement table you should produce

After your run, write up something like this:

| Strategy        | SQL stmts | Wire rows | Wall time | Allocation |
|-----------------|----------:|----------:|----------:|-----------:|
| Single Query    |        1  |     3,000 |     42 ms |     6.1 MB |
| Split Query     |        3  |     1,400 |     17 ms |     2.8 MB |
| Projection      |        1  |       100 |      6 ms |     0.2 MB |

The numbers vary by machine and provider. The ratios are stable.

## A note on the model-wide default

You can set `QuerySplittingBehavior.SplitQuery` as the model-wide default:

```csharp
options.UseNpgsql(connStr,
    npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
```

With this set, every multi-collection include is split by default; you opt back into single-query with `AsSingleQuery()`. The standing recommendation from the EF Core team is to **set this default at the application level** and let the rare single-query case be the explicit one. Cite <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries#configuring-the-query-splitting-behavior>.

Include in your write-up your recommendation: would you set the default at the model level for a fresh codebase, or would you leave the default as single-query and opt-in case by case? Defend the choice.

## Stretch: reproduce on PostgreSQL with a 50ms latency

The trade-off matrix changes on a high-latency link. Run a Postgres container with `pumba netem delay --duration 60s --time 50 <container>` (or, more simply, run Postgres on a different AWS region than your laptop) and re-measure all three strategies. The split-query strategy's three round-trips now cost 150ms of pure latency; the single-query strategy still costs only 50ms. At what cartesian factor does single-query win on a 50ms link? Document the inflection point.

This stretch is worth doing — it shows that "always split" is not a universal rule. The rule is **measure on the network you actually ship on**.

## Submission

Submit:

- `Program.cs` and `SalesDb.cs` that produce the measurement table when run.
- `WRITEUP.md` with your trade-off discussion, the measured numbers from your machine, and your recommendation on the model-wide default.
- A screenshot or pasted text of the SQL log proving (a) the single-query JOIN, (b) the three split queries, and (c) the projection's correlated subquery.

The rubric:

- (40%) The measurements are correct and reproducible on the seeded dataset.
- (30%) The write-up correctly explains *why* each strategy behaves as it does (cartesian explosion, round-trips, projection's wire savings).
- (20%) The recommendation on the model-wide default is defensible — either choice is acceptable if the reasoning is sound.
- (10%) Citations are present and correct.
