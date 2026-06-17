# Lecture 2 — Loading Strategies, the N+1 Problem, and Cartesian Explosion

> **Time:** 2 hours. The lecture has three measured sections — eager, explicit, split — and one bonus section on lazy loading. **Prerequisites:** Lecture 1 of this week (`DbContext`, the SQL log, the change tracker). **Citations:** Microsoft Learn's loading-related-data chapter at <https://learn.microsoft.com/en-us/ef/core/querying/related-data/>, the single/split-queries chapter at <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries>, and the EF Core perf guide at <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying>.

## 1. The setup: a parent-child schema and one slow endpoint

Throughout this lecture the example is the same two-table schema. Read it once and keep the picture in mind:

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
    public List<Address> Addresses { get; set; } = new();
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTime PlacedAt { get; set; }
    public decimal Total { get; set; }
}

public sealed class Address
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Line1 { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
}
```

The endpoint we will fix three times is "list every customer and how much they have spent." The naive implementation:

```csharp
public async Task<IReadOnlyList<CustomerSummary>> GetSummariesNaive()
{
    var customers = await db.Customers.AsNoTracking().ToListAsync();
    var result = new List<CustomerSummary>();
    foreach (var c in customers)
    {
        // BAD: c.Orders is empty here unless lazy loading is on; if it is,
        // touching it issues a fresh SELECT every iteration.
        var spent = c.Orders.Sum(o => o.Total);
        result.Add(new CustomerSummary(c.Id, c.Name, spent));
    }
    return result;
}

public record CustomerSummary(int Id, string Name, decimal TotalSpent);
```

This endpoint has a bug. With lazy loading off (the EF Core default), `c.Orders` is an empty list every iteration and the sum is always zero. With lazy loading on, every iteration of the loop issues `SELECT * FROM Orders WHERE CustomerId = @p0`, so 100 customers becomes 101 queries (the 1 for the parent list plus 100 for the children). This is the N+1 problem. Both bugs are silently wrong in different ways; the lazy-loading variant is wrong about performance and right about output, the lazy-loading-off variant is right about performance and wrong about output. We will fix it three ways.

The dataset for the measurement: 100 customers, each with 10 orders, on a local Postgres 16 instance. The measurements come from the EF Core team's perf-guide methodology, repeated for this lecture. Numbers are wall-clock from the .NET process, not server-reported.

## 2. Eager loading: `Include`

The first cure is **eager loading**. You tell EF, at query-definition time, "when you load these customers, also load their orders." The way you say so is `Include`:

```csharp
public async Task<IReadOnlyList<CustomerSummary>> GetSummariesEager()
{
    var customers = await db.Customers
        .AsNoTracking()
        .Include(c => c.Orders)
        .ToListAsync();

    return customers
        .Select(c => new CustomerSummary(c.Id, c.Name, c.Orders.Sum(o => o.Total)))
        .ToList();
}
```

The SQL EF emits is **one** query with a `LEFT JOIN`:

```sql
SELECT c."Id", c."Email", c."Name",
       o."Id", o."CustomerId", o."PlacedAt", o."Total"
FROM "Customers" AS c
LEFT JOIN "Orders" AS o ON c."Id" = o."CustomerId"
ORDER BY c."Id", o."Id"
```

The materializer reads the joined row stream, groups by `Customer.Id` (which is why the `ORDER BY c."Id"` is mandatory), and stitches the `Orders` collection onto each customer. One round trip; the database does the work in one shot; the wire carries 1,000 rows (100 customers times 10 orders, minus zero because every customer has at least one).

Measured numbers on the test dataset:

| Strategy                     | Round-trips | Wire rows | .NET wall time |
|------------------------------|------------:|----------:|---------------:|
| Naive (lazy loading on)      |        101  |     1,000 |        ~248 ms |
| `Include(c => c.Orders)`     |          1  |     1,000 |         ~14 ms |

The eager variant is 17x faster, and the gap grows with network latency: every round-trip in the naive variant pays the full round-trip cost, while the eager variant pays it once. On a 1ms-latency LAN the gap is 17x; on a 50ms-latency cross-region link, the gap is 100x.

### 2.1 `ThenInclude` for multi-level navigations

When the navigation is more than one level deep — customer to orders to line items — you chain `ThenInclude`:

```csharp
var customers = await db.Customers
    .AsNoTracking()
    .Include(c => c.Orders)
        .ThenInclude(o => o.Lines)
    .ToListAsync();
```

The pattern reads top-down. `Include` selects a navigation on the current entity; `ThenInclude` selects a navigation on the entity-type *of the last `Include`*. After `Include(c => c.Orders)`, the "current type" is `Order`, so `ThenInclude(o => o.Lines)` is valid. After a second `Include(c => c.Addresses)`, the current type is back to `Customer` (the next-sibling rule).

### 2.2 Filtered `Include`

EF Core 5 added filtered `Include` — you can apply `Where`, `OrderBy`, `Take`, `Skip` inside the `Include`:

```csharp
var customers = await db.Customers
    .AsNoTracking()
    .Include(c => c.Orders.Where(o => o.PlacedAt >= DateTime.UtcNow.AddDays(-30))
                          .OrderByDescending(o => o.PlacedAt)
                          .Take(5))
    .ToListAsync();
```

The filter is applied **server-side**, inside the `LEFT JOIN`. The collection on each customer contains only the last 30 days' orders, top 5 by date. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager#filtered-include>.

Filtered `Include` is one of the most useful constructs in EF Core 8 and one of the least-known. Reach for it any time the endpoint wants "the top N children" rather than "all the children."

## 3. The cartesian-explosion problem

The eager-loading cure has a sharp edge. The moment you `Include` **two** sibling collections, the join becomes a cross product:

```csharp
var customers = await db.Customers
    .AsNoTracking()
    .Include(c => c.Orders)
    .Include(c => c.Addresses)   // a second collection — danger
    .ToListAsync();
```

The SQL EF emits is:

```sql
SELECT c."Id", c."Email", c."Name",
       o."Id", o."CustomerId", o."PlacedAt", o."Total",
       a."Id", a."CustomerId", a."Line1", a."City", a."PostalCode"
FROM "Customers" AS c
LEFT JOIN "Orders"    AS o ON c."Id" = o."CustomerId"
LEFT JOIN "Addresses" AS a ON c."Id" = a."CustomerId"
ORDER BY c."Id", o."Id", a."Id"
```

For a customer with 10 orders and 3 addresses, the join produces **30** rows. For 100 such customers, the wire carries **3,000** rows — three times more than the actual data, with every order joined to every address joined to its customer. This is **cartesian explosion**. The wire bandwidth, the materialization cost, and the memory footprint all grow multiplicatively.

For 10 orders and 3 addresses the factor is 3x. For 100 line items and 50 tags it would be 5,000x. The cure exists, and EF Core has a built-in warning for it: at runtime, when EF detects that a single query is producing more rows than the cardinality of the parent table multiplied by a "reasonable" factor, it emits the warning `RelationalEventId.MultipleCollectionIncludeWarning`. Read the warning. Always.

## 4. Query splitting: `AsSplitQuery`

The cure for cartesian explosion is **query splitting**. EF emits **one query per collection** and stitches the results in C# memory:

```csharp
var customers = await db.Customers
    .AsNoTracking()
    .Include(c => c.Orders)
    .Include(c => c.Addresses)
    .AsSplitQuery()
    .ToListAsync();
```

The SQL EF now emits is **three** queries:

```sql
-- 1. The customers.
SELECT c."Id", c."Email", c."Name"
FROM "Customers" AS c
ORDER BY c."Id";

-- 2. The orders, keyed by the customer IDs from query 1.
SELECT o."Id", o."CustomerId", o."PlacedAt", o."Total", t."Id"
FROM "Customers" AS t
INNER JOIN "Orders" AS o ON t."Id" = o."CustomerId"
ORDER BY t."Id";

-- 3. The addresses, keyed similarly.
SELECT a."Id", a."CustomerId", a."Line1", a."City", a."PostalCode", t."Id"
FROM "Customers" AS t
INNER JOIN "Addresses" AS a ON t."Id" = a."CustomerId"
ORDER BY t."Id";
```

Three round trips instead of one, but each round trip carries only its own data: 100 customer rows, 1,000 order rows, 300 address rows — 1,400 wire rows total instead of 3,000.

Measured numbers on the test dataset (100 customers, 10 orders each, 3 addresses each):

| Strategy                              | Round-trips | Wire rows | .NET wall time |
|---------------------------------------|------------:|----------:|---------------:|
| `Include + Include` (single query)    |          1  |     3,000 |         ~42 ms |
| `Include + Include + AsSplitQuery`    |          3  |     1,400 |         ~17 ms |

The trade-off is intentional: split queries spend more round-trips to save on wire bytes and materialization work. On a high-latency network, single queries can still win if the cartesian factor is small (`Include` of two reference navigations, not collections — the cartesian factor is 1 and a single query is right). On a typical LAN with collection includes that have any cardinality at all, split queries are the default to reach for.

The Microsoft Learn chapter at <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries> has the canonical decision matrix. Read it. The rule of thumb you should leave the lecture with:

- One `Include` of a reference (single-row navigation): single query (`Include` alone is fine).
- One `Include` of a collection: single query (cartesian factor is 1, JOIN is efficient).
- Two `Include`s where at least one is a collection: **split**, almost always.
- Three or more `Include`s, any combination: **split**, always.

You can also set the splitting behavior at the model level:

```csharp
builder.Services.AddDbContext<CatalogDb>(options =>
{
    options.UseNpgsql(connStr, npgsql =>
        npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
});
```

With this set, every query in the context uses split queries by default; you opt **out** with `AsSingleQuery()` on the rare query that you want as a join. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries#configuring-the-query-splitting-behavior>.

## 5. Explicit loading: `Entry(...).Collection(...).LoadAsync`

Sometimes the navigation is **conditionally** needed — the endpoint loads orders only if a flag is set, only if the customer has a recent purchase, only if the user is an admin. Eager-loading them is wasteful when the flag is off; lazy-loading them risks the N+1 trap. The third tool is **explicit loading**:

```csharp
public async Task<CustomerDetail> GetCustomerDetail(int id, bool includeOrders)
{
    var customer = await db.Customers.FindAsync(id);
    if (customer is null) throw new NotFoundException();

    if (includeOrders)
    {
        await db.Entry(customer)
                .Collection(c => c.Orders)
                .LoadAsync();
    }

    return new CustomerDetail(
        customer.Id,
        customer.Name,
        includeOrders ? customer.Orders.ToList() : null);
}
```

`Entry(customer).Collection(c => c.Orders).LoadAsync()` issues exactly one `SELECT` for that customer's orders. It is **the manual override**: you decide, at runtime, whether to issue the second query.

The cost is that explicit loading **requires tracking**. `Entry(...)` only works for tracked entities; `AsNoTracking` is incompatible with this pattern. For a write path where the customer is tracked anyway, this is free. For a read-only list endpoint where you wanted `AsNoTracking`, you have to choose: drop `AsNoTracking` and use explicit loading, or stick with `AsNoTracking` and use eager loading (with the cost of always loading the orders, even when not needed).

The variant for a reference navigation is `.Reference(...)`:

```csharp
await db.Entry(order)
        .Reference(o => o.Customer)
        .LoadAsync();
```

And the variant that loads only entities matching a predicate, leveraging the `Query()` builder:

```csharp
await db.Entry(customer)
        .Collection(c => c.Orders)
        .Query()
        .Where(o => o.PlacedAt >= DateTime.UtcNow.AddDays(-30))
        .LoadAsync();
```

The `Query()` form is the most flexible. It exposes the underlying `IQueryable<T>` for the navigation, lets you filter and order on the server, and only loads the rows you want. Cite <https://learn.microsoft.com/en-us/ef/core/querying/related-data/explicit#querying-related-entities>.

Measured numbers on the test dataset, for the "load the customer and conditionally load orders" pattern:

| Strategy                              | Round-trips | Wire rows | .NET wall time |
|---------------------------------------|------------:|----------:|---------------:|
| `Include` (always)                    |          1  |        10 |          ~5 ms |
| `Entry().Collection().LoadAsync()`    |          2  |        11 |          ~7 ms |

For a single customer the gap is in the noise. The reason to choose explicit loading is *correctness* (don't load what you don't need) more than *performance*; the performance argument arises only at high call rates where 1 unused order load times 10,000 calls per second is real wire traffic.

## 6. Lazy loading: how it works and why it is rarely the right answer

Lazy loading is the pattern where touching a navigation property issues a `SELECT` on demand. It is **off by default** in EF Core 8, and turning it on requires two steps:

1. Install the `Microsoft.EntityFrameworkCore.Proxies` package.
2. Configure with `optionsBuilder.UseLazyLoadingProxies()`.
3. Mark every navigation `virtual`.

When enabled, EF wraps every entity in a proxy at materialization time. Each navigation property's getter is overridden to check "has this collection been loaded?" — if not, it synchronously issues a `SELECT`, materializes the rows, populates the collection, and returns. Subsequent accesses hit the populated collection.

The reason lazy loading is rarely the right answer is the N+1 problem from Section 1. Lazy loading does not solve N+1; it *causes* N+1. In a loop that touches a navigation, lazy loading silently issues one query per iteration, and the only way to find out is to read the SQL log. The framework will not warn you, the type system will not complain, and the unit tests will pass (a unit test usually exercises one customer, not a list).

A second strike against lazy loading: it is **synchronous**. The proxy's overridden getter is a property getter, which by .NET convention is not allowed to be `async`. A lazy-loaded navigation blocks the calling thread on the database round trip. On an ASP.NET Core request thread this is exactly the sync-over-async pattern Week 8 told you not to do. There is no async lazy loading. There never will be — the language does not support async property getters.

The third strike: lazy loading **requires tracking**. `AsNoTracking` is incompatible with the proxy machinery. So you give up the read-path lever from Lecture 1.

The standing C9 advice: **do not enable lazy loading**. Use eager loading by default, explicit loading when the navigation is conditional, raw SQL when the query is genuinely out of LINQ's reach. If you inherit a codebase with lazy loading on, treat its removal as a planned tech-debt task. Cite the warning at <https://learn.microsoft.com/en-us/ef/core/querying/related-data/lazy#performance-considerations>.

## 7. The three cures, side by side

Here is the same "list customers and their orders" endpoint, four times: the broken naive version, and the three cures. Read all four together; the differences are the point of the lecture.

### 7.1 Naive (broken)

```csharp
public async Task<IReadOnlyList<CustomerSummary>> GetSummariesNaive()
{
    var customers = await db.Customers.AsNoTracking().ToListAsync();
    return customers
        .Select(c => new CustomerSummary(c.Id, c.Name, c.Orders.Sum(o => o.Total)))
        .ToList();
}
// With lazy loading off: silently wrong (every total is 0).
// With lazy loading on:  N+1 — 101 queries, ~248 ms.
```

### 7.2 Cure 1: `Include`

```csharp
public async Task<IReadOnlyList<CustomerSummary>> GetSummariesEager()
{
    var customers = await db.Customers
        .AsNoTracking()
        .Include(c => c.Orders)
        .ToListAsync();
    return customers
        .Select(c => new CustomerSummary(c.Id, c.Name, c.Orders.Sum(o => o.Total)))
        .ToList();
}
// 1 query, 1,000 wire rows, ~14 ms.
// Cartesian risk: low — only one collection included.
```

### 7.3 Cure 2: projection (often the best answer of all)

The version of the endpoint that does not even need the entities. The endpoint returns a summary, not the orders themselves; let the database do the sum:

```csharp
public async Task<IReadOnlyList<CustomerSummary>> GetSummariesProjected()
{
    return await db.Customers
        .AsNoTracking()
        .Select(c => new CustomerSummary(
            c.Id,
            c.Name,
            c.Orders.Sum(o => o.Total)))
        .ToListAsync();
}
```

The SQL is:

```sql
SELECT c."Id", c."Name",
       (SELECT COALESCE(SUM(o."Total"), 0)
        FROM "Orders" AS o
        WHERE c."Id" = o."CustomerId") AS "TotalSpent"
FROM "Customers" AS c
```

One query. **100 wire rows** (one per customer, no orders on the wire). **~6 ms.**

The projection variant outperforms `Include` because it does not transport the order rows over the wire at all — the server computes the sum, returns 100 scalars, and the .NET side does no aggregation. The performance lesson is **let the database do the work it is good at, and bring back only what the endpoint actually returns**. Citation: <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#project-only-properties-you-need>.

### 7.4 Cure 3: split query (for the multi-collection case)

```csharp
public async Task<IReadOnlyList<CustomerDetail>> GetCustomerDetails()
{
    var customers = await db.Customers
        .AsNoTracking()
        .Include(c => c.Orders)
        .Include(c => c.Addresses)
        .AsSplitQuery()
        .ToListAsync();

    return customers.Select(c => new CustomerDetail(c.Id, c.Name,
        c.Orders.ToList(),
        c.Addresses.ToList())).ToList();
}
// 3 queries, 1,400 wire rows, ~17 ms.
// No cartesian explosion.
```

### 7.5 Cure 4: explicit (for the conditional case)

```csharp
public async Task<CustomerDetail> GetCustomerDetail(int id, bool includeOrders)
{
    var customer = await db.Customers.FindAsync(id);
    if (customer is null) throw new NotFoundException();

    if (includeOrders)
    {
        await db.Entry(customer).Collection(c => c.Orders).LoadAsync();
    }
    return new CustomerDetail(customer.Id, customer.Name, /* ... */);
}
// 1 or 2 queries depending on includeOrders.
```

## 8. Reading the SQL log to spot N+1 in the wild

The skill you need most after this lecture is *recognising N+1 in a real SQL log*. The signature is:

```
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) ... SELECT * FROM "Customers" ...
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) ... SELECT * FROM "Orders" WHERE "CustomerId" = @p0 ... [@p0=1]
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) ... SELECT * FROM "Orders" WHERE "CustomerId" = @p0 ... [@p0=2]
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) ... SELECT * FROM "Orders" WHERE "CustomerId" = @p0 ... [@p0=3]
...
```

The fingerprint: one outer query followed by N inner queries with **the same SQL** and only the parameter changing. Once you see it, the cure is one of: `Include` (if the navigation is always needed), `AsSplitQuery` (if there are multiple collections), projection (if the endpoint does not need the entities themselves), explicit `LoadAsync` (if the navigation is sometimes-needed). All four are in your toolbox now.

There is also a **runtime warning** EF Core 8 emits when it detects "the same query has executed N times in this DbContext lifetime, you might have N+1 here." Enable it explicitly with:

```csharp
options.LogTo(Console.WriteLine, new[] { CoreEventId.NavigationLazyLoading })
       .ConfigureWarnings(w => w.Log(CoreEventId.NavigationLazyLoading));
```

Citation: <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/extensions-logging#filter-log-messages>. The warning is off by default; turn it on in your test suite and treat it as a build break.

## 9. A note on `ExecuteUpdate` and `ExecuteDelete` — bulk operations that skip the tracker

EF Core 7 added, and EF Core 8 polished, two methods that issue an `UPDATE` or `DELETE` statement directly without involving the change tracker:

```csharp
// Bulk update: set Price = Price * 1.1 for all expired products.
await db.Products
    .Where(p => p.ExpiresAt < DateTime.UtcNow)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 1.1m));

// Bulk delete: remove cancelled orders.
await db.Orders
    .Where(o => o.Status == OrderStatus.Cancelled)
    .ExecuteDeleteAsync();
```

These methods are the **right answer** when you would otherwise load thousands of entities, mutate them in a loop, and call `SaveChangesAsync`. They emit a single `UPDATE` (or `DELETE`) with a `WHERE` clause and skip the tracker entirely. The cost is that they do not run any `OnUpdating` / `OnDeleting` interceptors and they do not fire change-tracking events. For business-logic-bearing operations where you have invariants enforced in code, this can be the wrong tool. For pure data-shape operations — set every price up 10%, delete every record older than 90 days — it is the right tool.

Citation: <https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete>. Lecture 3 returns to these as part of the "escape hatches" discussion.

## 10. Lecture-2 summary checklist

After this lecture you should be able to, without notes:

1. Diagnose an N+1 from a SQL log: identify the outer query and the repeated inner queries with only the parameter changing.
2. Apply `Include(c => c.Orders)` to fold the children into the parent query.
3. Chain `Include().ThenInclude()` for multi-level navigations.
4. Recognise cartesian explosion when two collections are joined — the multiplicative row growth — and reach for `AsSplitQuery`.
5. Choose between `Include` (always-needed navigation), `AsSplitQuery` (multi-collection), explicit `Entry().Collection().LoadAsync` (conditional navigation), and projection (when the endpoint does not need the entities themselves).
6. Explain why lazy loading is rarely the right answer, citing the N+1 trap and the sync-over-async cost.
7. Apply filtered `Include` for "top N children" use cases.
8. Set the model-wide query-splitting behavior with `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)`.
9. Use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` for bulk operations that skip the tracker.
10. Read measured numbers from the lecture's `PERF.md` and explain the trade-off matrix (single query vs split query) in your own words.

Lecture 3 covers the escape hatches: raw SQL with `FromSqlInterpolated`, value converters, owned types, compiled queries, and the observability story.
