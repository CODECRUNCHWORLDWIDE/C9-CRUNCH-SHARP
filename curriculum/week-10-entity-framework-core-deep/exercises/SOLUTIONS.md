# Exercise Solutions — Week 10

> The exercises ship as runnable skeletons (the `.cs` files in this directory) plus the SQL log you should reproduce. The solutions here are annotated walk-throughs of the four exercises with the expected output, the most common mistakes, and a brief commentary on each. **Cite-back rule:** every assertion below has a Microsoft Learn URL or a `dotnet/efcore` source link.

## Exercise 1 — Migrations and the SQL Log

### Expected sequence of files produced

After `dotnet ef migrations add InitialCreate`:

```
Migrations/
  20260514120000_InitialCreate.cs
  20260514120000_InitialCreate.Designer.cs
  CatalogDbModelSnapshot.cs
```

After `dotnet ef migrations add AddProductSkuColumn` (with the `Sku` property added to `Product`):

```
Migrations/
  20260514120000_InitialCreate.cs
  20260514120000_InitialCreate.Designer.cs
  20260514130000_AddProductSkuColumn.cs
  20260514130000_AddProductSkuColumn.Designer.cs
  CatalogDbModelSnapshot.cs        (updated)
```

### Expected SQL log shape, step by step

For the seeded INSERT (one Category, then one Product):

```
Executed DbCommand (1ms) ...
INSERT INTO "Categories" ("Name") VALUES (@p0) RETURNING "Id";   -- @p0='Tools'

Executed DbCommand (1ms) ...
INSERT INTO "Products" ("CategoryId", "Name", "Price", "Sku")
VALUES (@p0, @p1, @p2, @p3) RETURNING "Id";                       -- @p3=NULL
```

For the read-by-key (`FirstAsync`):

```
SELECT "p"."Id", "p"."CategoryId", "p"."Name", "p"."Price", "p"."Sku"
FROM "Products" AS "p"
LIMIT 1
```

For the UPDATE (price changed by 10%):

```
UPDATE "Products" SET "Price" = @p0
WHERE "Id" = @p1
RETURNING 1;                                                       -- @p0=9.35, @p1=1
```

The single-column `SET` is the change tracker computing the minimum diff from the snapshot. If the SET clause includes columns whose values did not change, your `OnConfiguring` is somehow forcing a full update (rare).

For the DELETE:

```
DELETE FROM "Products" WHERE "Id" = @p0 RETURNING 1;
```

### Common mistakes

- **Forgetting to install `dotnet-ef`.** The error message is `Could not execute because the specified command or file was not found.` Run `dotnet tool install --global dotnet-ef --version 8.0.0` once per machine.
- **Editing a migration after applying it.** The next `dotnet ef migrations add` will produce a no-op or, worse, an inconsistent diff. Citation: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing#editing-migrations>.
- **Running migrations against the wrong connection string.** The `dotnet ef` CLI uses the connection from the `OnConfiguring` method or the `DbContextOptionsBuilder` registered at design-time via `IDesignTimeDbContextFactory<T>`. If you have a `appsettings.Production.json` that overrides the connection, set `ASPNETCORE_ENVIRONMENT=Development` before running the CLI.

### Expected idempotent-script shape

The script EF emits with `--idempotent`:

```sql
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;

-- For each migration:
SELECT NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260514120000_InitialCreate'
);
-- if 1, run the migration body. The Migrations Bundle wraps these checks in
-- conditional emission; the rendered script is one CREATE TABLE / ALTER TABLE
-- block per migration, each prefixed with a "skip if already applied" guard.

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260514120000_InitialCreate', '8.0.0');

COMMIT;
```

Citation: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#sql-scripts>.

---

## Exercise 2 — Tracking vs No-Tracking Benchmark

### Expected numbers on a 10,000-row SQLite read

The absolute numbers vary; the ratios are stable across machines. On a 2023 M2 Pro with the lecture's seed data:

```
|                           Method |       Mean | Ratio | Allocated | Alloc Ratio |
|--------------------------------- |-----------:|------:|----------:|------------:|
|                         Tracking |   19.40 ms |  1.00 |   5.92 MB |        1.00 |
|                       NoTracking |   13.15 ms |  0.68 |   3.55 MB |        0.60 |
| NoTrackingWithIdentityResolution |   14.20 ms |  0.73 |   3.86 MB |        0.65 |
```

### Why each switch behaves the way it does

- **Tracking** (baseline) does three things per row: (a) check the identity dictionary for a duplicate, (b) insert into the dictionary if new, (c) snapshot every property into the `_originalValues` array. Items (b) and (c) drive the allocation; item (a) is a hash lookup, cheap individually but 10,000 times means measurable.
- **NoTracking** skips (a), (b), and (c). The materializer reads rows from the data reader, news up the entity, sets the properties, and returns. No dictionary, no snapshot. The 32-40% allocation reduction is real; the 30-32% time reduction follows from less work + less GC pressure.
- **NoTrackingWithIdentityResolution** keeps (a) — a per-query dictionary that is freed when the query completes. Slightly slower than `NoTracking` because the dictionary lookup costs something; faster than `Tracking` because the dictionary does not survive the call.

Source link: <https://github.com/dotnet/efcore/blob/main/src/EFCore/Extensions/EntityFrameworkQueryableExtensions.cs> — search for `AsNoTracking` and `AsNoTrackingWithIdentityResolution`.

### Common mistakes

- **Running in Debug.** BenchmarkDotNet will refuse and print a warning. Use `dotnet run -c Release`.
- **Forgetting that the GlobalSetup runs once.** If you change the seed parameters, delete `ex02.db` so the new seed runs.
- **Asserting absolute timings.** The exercise checks the ratio, not the wall-clock number. A slow machine that takes 100ms on Tracking should still show NoTracking at 65-70% of that.

---

## Exercise 3 — Diagnose and Fix N+1

### Approach 1 — Naive (the silent-bug case)

With lazy loading off (the EF Core 8 default), the naive endpoint emits **one** SQL statement and returns 100 summaries with `OrderCount=0` and `TotalSpent=0`. The endpoint compiles, runs, and lies — the output is silently wrong because `c.Orders` is an empty list, not a loaded collection.

The exercise calls this "the silent failure mode of forgetting to Include." Real codebases sometimes have this bug for months, hidden behind a misleading dashboard.

### Approach 2 — Include (eager loading)

One SQL statement, a `LEFT JOIN Orders` on the customer ID, ordered by customer ID and order ID. The materializer reads 1,000 joined rows, groups by customer, and produces 100 summaries with the right counts and sums.

Expected SQL:

```sql
SELECT "c"."Id", "c"."Name",
       "o"."Id", "o"."CustomerId", "o"."PlacedAt", "o"."Total"
FROM "Customers" AS "c"
LEFT JOIN "Orders" AS "o" ON "c"."Id" = "o"."CustomerId"
ORDER BY "c"."Id", "o"."Id"
```

Approximately 38ms wall time on the test machine.

### Approach 3 — Projection (the best of the four)

One SQL statement, no joined rows on the wire. The count and sum are computed server-side via correlated subqueries:

```sql
SELECT "c"."Id", "c"."Name",
       (SELECT COUNT(*) FROM "Orders" AS "o" WHERE "c"."Id" = "o"."CustomerId") AS "OrderCount",
       (SELECT COALESCE(SUM("o"."Total"), 0) FROM "Orders" AS "o" WHERE "c"."Id" = "o"."CustomerId") AS "TotalSpent"
FROM "Customers" AS "c"
```

100 rows on the wire (one per customer); the orders never leave the server. Approximately 17ms wall time — faster than `Include` because the wire is 10x smaller.

This is the **right answer for any endpoint whose output does not include the orders themselves**. The "load the entities, sum in C#" pattern is wasteful whenever the endpoint discards the entities. Project to what the endpoint returns.

### Approach 4 — Explicit (the conditional-loading case)

Two SQL statements. The first loads 100 customers (tracked, no `AsNoTracking` because explicit loading requires tracking — though in this exercise we use a manual `Where ... IN (...)` batched load, which sidesteps the tracking requirement and lets us keep `AsNoTracking` for both halves):

```sql
SELECT "c"."Id", "c"."Name" FROM "Customers" AS "c"

SELECT "o"."Id", "o"."CustomerId", "o"."PlacedAt", "o"."Total"
FROM "Orders" AS "o"
WHERE "o"."CustomerId" IN (@p0, @p1, ..., @p99)
```

Approximately 22ms wall time. The right answer when the navigation is conditional — the endpoint decides at runtime whether to load orders based on a flag, a permission, or a downstream API request.

### The trade-off matrix

| Approach     | Round-trips | Wire rows | Correctness | When to use                                              |
|--------------|------------:|----------:|-------------|----------------------------------------------------------|
| Naive        |          1  |       100 | Wrong       | Never                                                    |
| Include      |          1  |     1,000 | Right       | Children always needed and the endpoint returns them     |
| Projection   |          1  |       100 | Right       | Endpoint returns summaries, not the children themselves  |
| Explicit     |          2  |       100 | Right       | Children conditionally needed                            |

Citation for the matrix: <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#use-eager-loading-when-appropriate>.

---

## Exercise 4 — Raw SQL with `FromSqlInterpolated` + a `Money` Value Converter

### Expected SQL log — the safety property

The legitimate search:

```sql
SELECT * FROM Products WHERE Name LIKE @p0     -- @p0='%wrench%'
```

The malicious search:

```sql
SELECT * FROM Products WHERE Name LIKE @p0     -- @p0='%''; DROP TABLE Products; --%'
```

Both queries return their respective row counts. The malicious one returns zero rows because no product's name contains the literal string `'; DROP TABLE Products; --`. The table is intact afterwards, which the subsequent list-all step confirms.

### The crucial source-code observation

`FromSqlInterpolated` takes a `FormattableString` parameter. The C# compiler converts an interpolated string `$"... {x}"` into `FormattableString.Create("... {0}", x)` — the template and the arguments arrive at EF as two separate things. EF then asks the provider to bind each `{i}` to a parameter, never substituting the value into the template. This is **structurally** safe from injection. Source: <https://github.com/dotnet/efcore/blob/main/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs> — search for `FromSqlInterpolated`.

`FromSqlRaw` takes a `string` and a `params object[]`. Used correctly (with `{0}` placeholders), it is equally safe — EF parameterizes the placeholders identically. The danger is the developer who reaches for `FromSqlRaw` and concatenates: `FromSqlRaw("SELECT ... = '" + input + "'")` is the classic SQL injection vector. The remedy is the social rule: **prefer `FromSqlInterpolated` always; reach for `FromSqlRaw` only when the template needs to vary at runtime; never put user input into the template, only into the parameters**.

### The composed-query SQL shape

When LINQ is layered on top of a raw query, EF wraps the raw query in a subquery:

```sql
SELECT "p"."Id", "p"."Name", "p"."PriceAmount", "p"."PriceCurrency"
FROM (
    SELECT * FROM Products WHERE PriceCurrency = @p0
) AS "p"
WHERE "p"."PriceAmount" > 10
ORDER BY "p"."PriceAmount" DESC
```

The wrapping only works if the inner query is a single composable `SELECT`. If the raw query had a trailing `ORDER BY` or `LIMIT`, the wrapping would produce malformed SQL on most providers. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#composing-with-linq>.

### Value-converter shape

`ComplexProperty` (EF Core 8) and `OwnsOne` (EF Core 7 and earlier) both produce the same two-column shape:

```sql
CREATE TABLE "Products" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "PriceAmount" decimal(19,4) NOT NULL,
    "PriceCurrency" char(3) NOT NULL
);
```

The difference is in the modelling semantics: `OwnsOne` treats the `Money` as an "owned entity" with a hidden identity; `ComplexProperty` treats it as a true value object with no identity at all. For EF Core 8 new code, prefer `ComplexProperty`. Cite <https://learn.microsoft.com/en-us/ef/core/modeling/complex-types>.

### Common mistakes

- **`OwnsOne` returns null for a never-set value object.** EF tracks the owned entity separately; if the parent row's owned columns are all `NULL`, the navigation is `null`. The lecture-3 advice is to default-initialize the value object in the property declaration (`public Money Price { get; set; } = default;`).
- **Mixing the EF 7 and EF 8 syntaxes.** The exercise shows both for reference; use one. The compiler will accept both, but the runtime model will be inconsistent if both are active for the same property.
- **Forgetting `.IsFixedLength()` on `Currency`.** Without it, the column is `varchar(3)` instead of `char(3)`. Functionally identical on SQLite; on PostgreSQL and SQL Server the difference matters for index usage.

---

## How to grade your own work

For each exercise, you should be able to:

1. Produce the expected SQL log on first attempt without editing the exercise.
2. Explain, in one sentence, why the EF team made each design decision (tracker presence, parameterization, wrapping subquery, two-column owned-type mapping).
3. Cite the Microsoft Learn URL for each technique you used.
4. Spot a wrong answer in a code review: "this endpoint has lazy loading on and a loop touching `.Orders`" / "this raw query concatenates user input" / "this write path uses `Update` without verifying the DTO is total."

If any of those four is shaky, re-read the corresponding lecture section before moving to the challenges.
