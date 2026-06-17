# Mini-Project — Crunch Catalog: a Production-Shaped Product Catalog API with EF Core 8

> Build a small product-catalog API in C# and .NET 8 with PostgreSQL (or SQLite) as the store. Implement migrations, value converters, owned types, three loading-strategy variants for the same endpoint with measured timings, a `FromSqlInterpolated` search endpoint, an interceptor that logs slow queries, and an idempotent SQL script for production deploy. By the end you have a small, schema-driven, observable web service that an operator at a real company would be willing to put behind a load balancer.

This is the canonical "build a small EF Core 8 service end-to-end" exercise. The shape is genuinely production-shaped: migrations checked into source control with an idempotent deploy script, every endpoint has an intentional SQL signature you can read from `LogTo`, the change tracker is used correctly on the write path and bypassed on the read path, raw SQL is parameterized, slow queries are logged. Every senior .NET engineer who has shipped an EF Core service has built something with this exact skeleton. The mini-project is that experience in microcosm.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A solution called `CrunchCatalog` with three projects:

- `src/CrunchCatalog.Api/` — an ASP.NET Core minimal-API host (`net8.0`) with seven endpoints.
- `src/CrunchCatalog.Data/` — the `DbContext`, the entities, the value converters, the migrations.
- `tests/CrunchCatalog.Tests/` — xUnit tests asserting correctness, the SQL signature of key queries (via `ToQueryString`), interceptor wiring, and the idempotent-script behaviour.

You ship one solution. The three projects each have their own `.csproj`. The mini-project is gradable from `dotnet test` plus a one-paragraph `PERF.md` write-up.

---

## The domain

A catalog of products, organized by category, with prices in `Money`:

```csharp
public sealed class Category
{
    public CategoryId Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = new();
}

public sealed class Product
{
    public ProductId Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public Money Price { get; set; }
    public CategoryId CategoryId { get; set; }
    public Category? Category { get; set; }
    public List<Review> Reviews { get; set; } = new();
}

public sealed class Review
{
    public int Id { get; set; }
    public ProductId ProductId { get; set; }
    public Product? Product { get; set; }
    public int Stars { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; }
}

public readonly record struct CategoryId(int Value);
public readonly record struct ProductId(int Value);
public readonly record struct Money(decimal Amount, string Currency);
```

The shape is intentionally simple. The interest is in **how** the entities are modelled and queried, not in the domain itself.

### Modelling requirements

- `CategoryId` and `ProductId` are **strongly-typed IDs**. Apply value converters so they persist as plain `int` columns. The C# API refuses to confuse a `CategoryId` with a `ProductId`.
- `Money` is a **value object on the same row**. Apply `ComplexProperty` (or `OwnsOne` if you prefer the legacy syntax) so it persists as two columns: `PriceAmount decimal(19,4)` and `PriceCurrency char(3)`.
- `Review.Stars` has a database-level `CHECK` constraint of `1 <= Stars <= 5`. Apply this in `OnModelCreating` via `HasCheckConstraint`.
- `Product.Sku` has a **unique index**.
- `Category.Name` has a **unique index** with case-insensitive collation on Postgres or a `COLLATE NOCASE` clause on SQLite.

Cite <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions>, <https://learn.microsoft.com/en-us/ef/core/modeling/complex-types>, and <https://learn.microsoft.com/en-us/ef/core/modeling/indexes>.

---

## The seven endpoints

The API exposes the following minimal-API endpoints. Each has a specified SQL signature; the tests assert the signature via `ToQueryString` or by reading the captured log.

### 1. `GET /api/categories` — list all categories with their product counts

```http
GET /api/categories
```

Returns `CategoryListItem[] { CategoryId Id, string Name, int ProductCount }`. **Required SQL shape:** a single `SELECT` with a correlated subquery for the count. No `Include`, no extra round-trip.

### 2. `GET /api/categories/{id}/products` — list products in a category

```http
GET /api/categories/42/products?page=1&pageSize=20
```

Returns `ProductListItem[]` paginated. **Required SQL shape:** a single `SELECT` from `Products` with `WHERE CategoryId = @p0 ORDER BY Name LIMIT @p1 OFFSET @p2`. Use `.AsNoTracking()`.

### 3. `GET /api/products/{id}` — get a product detail with category and reviews

```http
GET /api/products/7
```

Returns `ProductDetail { ProductId Id, string Name, string Sku, Money Price, CategoryId CategoryId, string CategoryName, ReviewItem[] Reviews }`. **Required SQL shape:** an `Include(p => p.Category)` and an `Include(p => p.Reviews)` with `AsSplitQuery()` — two collections potentially, so split is the right answer. Use `.AsNoTracking()`.

### 4. `GET /api/products/search?q=...` — search by name with raw SQL

```http
GET /api/products/search?q=wrench
```

Returns `SearchHit[]`. **Required SQL shape:** `FromSqlInterpolated($"... WHERE Name ILIKE {pattern}")` (Postgres) or `LIKE` with `COLLATE NOCASE` (SQLite). The interpolated value is a parameter, not a substring. Confirm with a SQL log capture in `PERF.md`.

### 5. `POST /api/products` — create a product

```http
POST /api/products
Content-Type: application/json
{ "name": "...", "sku": "...", "price": { "amount": 9.99, "currency": "USD" }, "categoryId": 3 }
```

Returns `201 Created` with the created `ProductDetail`. **Required behaviour:** wraps the insert in a transaction; rejects duplicates (`Sku` unique violation maps to `409 Conflict`); the change tracker shows the entity in state `Added` before `SaveChangesAsync` and `Unchanged` after.

### 6. `PATCH /api/products/{id}/price` — update a product's price

```http
PATCH /api/products/7/price
{ "amount": 12.50, "currency": "USD" }
```

Returns `204 No Content`. **Required SQL shape:** an `UPDATE` with only the `PriceAmount` and `PriceCurrency` columns in the `SET` clause (the change tracker computes the diff). Confirm in the SQL log.

### 7. `POST /api/products/{id}/reviews` — add a review

```http
POST /api/products/7/reviews
{ "stars": 5, "body": "Solid." }
```

Returns `201 Created`. **Required behaviour:** validates `1 <= stars <= 5` in C#; the DB `CHECK` constraint catches any race-condition violation; foreign-key violation if the product does not exist maps to `404 Not Found`.

---

## The performance-comparison endpoint

Add one **extra** internal endpoint that runs the same logical query in **three** ways and reports the timing of each:

```http
GET /api/_perf/customer-summaries
```

This endpoint is a copy of the lecture's "list customers with order counts" demo, but on the catalog domain: "list categories with product counts and average review star rating per product." Implement it three ways:

1. **Naive (lazy off, no Include).** Reports the wrong answer; demonstrates the silent-failure mode.
2. **Eager with `Include + AsSplitQuery`.**
3. **Pure projection** (server-side aggregation).

The endpoint returns:

```json
{
  "naive":      { "ms": 12, "sqlCount": 1, "rowsReturned": 5, "correctness": "WRONG (zeros)" },
  "include":    { "ms": 38, "sqlCount": 2, "rowsReturned": 5, "correctness": "OK" },
  "projection": { "ms": 17, "sqlCount": 1, "rowsReturned": 5, "correctness": "OK" }
}
```

The tests assert that the three approaches return the same row count and that the `include` and `projection` approaches return the same data (the naive one is allowed to be wrong, as documented).

The write-up in `PERF.md` discusses the result, citing your specific numbers.

---

## The interceptor

Implement a `SlowQueryInterceptor` that derives from `DbCommandInterceptor` and logs to `ILogger<SlowQueryInterceptor>` every command that takes more than 100ms:

```csharp
public sealed class SlowQueryInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SlowQueryInterceptor> _log;
    public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> log) => _log = log;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Duration > TimeSpan.FromMilliseconds(100))
        {
            _log.LogWarning("Slow query ({Duration} ms): {Sql}",
                eventData.Duration.TotalMilliseconds, command.CommandText);
        }
        return new ValueTask<DbDataReader>(result);
    }
}
```

Register it on the `DbContext`:

```csharp
builder.Services.AddDbContext<CatalogDb>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Catalog"));
    options.AddInterceptors(sp.GetRequiredService<SlowQueryInterceptor>());
});
builder.Services.AddSingleton<SlowQueryInterceptor>();
```

The test suite asserts that a query intentionally made slow (e.g. `Thread.Sleep(150)` inside a benchmark-only endpoint, or a query that selects 1M rows from a seeded large table) triggers the interceptor and the log captures the SQL text.

Cite <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors>.

---

## The migrations

You ship the schema as three migrations:

1. `20260514120000_InitialCreate` — create `Categories`, `Products`, `Reviews`, with the value-converter mappings and the unique indexes.
2. `20260514130000_AddProductSku` — add the `Sku` column with the unique index. (This deliberately ships **after** the initial migration to demonstrate the workflow.)
3. `20260514140000_AddReviewCheckConstraint` — add the `CHECK (Stars BETWEEN 1 AND 5)` constraint.

Each migration is checked in. Each is applied to the dev database with `dotnet ef database update`. For production deploy, you run:

```bash
dotnet ef migrations script --idempotent --output deploy.sql
```

The committed `deploy.sql` is part of the deliverable. The test suite asserts that re-running the script against a fully-migrated database is a no-op.

---

## The tests

Place tests under `tests/CrunchCatalog.Tests/` using xUnit. Required tests:

### Modelling tests

- `Cannot_confuse_CategoryId_with_ProductId` — a compile-time test that asserts swapping the argument order in a method that takes both is a compiler error. (The test is "this file does not compile if the IDs are swapped"; comment-out an intentional swap and confirm.)
- `Money_persists_as_two_columns` — assert via `ToQueryString` or by inspecting the DDL that `PriceAmount` and `PriceCurrency` exist.

### Query-shape tests

- `Category_list_emits_correlated_subquery` — call the endpoint, capture the SQL log, assert it contains exactly one `SELECT` against `Categories` with a `(SELECT COUNT(*) ...)` subquery.
- `Product_detail_uses_split_query` — assert the SQL log contains three `SELECT` statements (one product, one category, one reviews).
- `Product_search_parameterizes_input` — pass a malicious input (`'; DROP TABLE Products; --`); assert the table is intact and 0 rows are returned.

### Write-path tests

- `Create_product_emits_INSERT` — POST a product, capture the log, assert the SQL is an `INSERT` with parameters for every column.
- `Update_price_emits_minimal_UPDATE` — PATCH a price, capture the log, assert the `SET` clause contains only `PriceAmount` and `PriceCurrency` (not `Name`, not `CategoryId`).
- `Duplicate_sku_returns_409` — POST a product with an existing SKU, assert the response is `409 Conflict`.

### Performance test

- `Three_strategies_return_consistent_data` — call `/api/_perf/customer-summaries`, assert the `include` and `projection` strategies return the same data; assert the `naive` strategy returns zeros (documented wrong behaviour).

### Idempotent-script test

- `Idempotent_script_is_noop_when_migrations_applied` — apply all migrations, then run the idempotent script, then assert that no rows were affected by the script (the `IF NOT EXISTS` guards stopped all migration bodies from running).

---

## The PERF.md write-up

Submit `mini-project/PERF.md` with:

- Your machine's specs (CPU, RAM, OS, database — Postgres in Docker or SQLite).
- Measured timings for the three loading strategies on the seeded dataset.
- A discussion of the inflection points: when does the projection beat `Include`? When does `AsSplitQuery` matter?
- Captured SQL log fragments for the most interesting queries (product detail split query, product search interpolated query, price patch minimal UPDATE).
- A recommendation paragraph: which loading strategies you would use for which kinds of endpoints in this codebase, and why.

The write-up is approximately one page. It is the deliverable an operator or a senior engineer would read at code review time.

---

## What's in the `starter/` directory

To get you off the blank-page problem, `starter/` contains:

- `CatalogDb.cs` — a stub `DbContext` with the three entities and stubbed `OnModelCreating`.
- `Program.cs` — a stub minimal-API host with the seven endpoints declared but unimplemented.
- `PerfMeasurer.cs` — a helper that runs an async block, captures the elapsed time, and counts SQL statements via a `LogTo` callback.
- `appsettings.Development.json` — the connection string template for Postgres and SQLite.

You may use the starter or write the project from scratch. The grading does not penalize either choice.

---

## Grading rubric

| Area                           | Weight | Criteria                                                                                  |
|--------------------------------|-------:|-------------------------------------------------------------------------------------------|
| Migrations                     |    15% | Three migrations checked in; idempotent script produces; re-applying it is a no-op.       |
| Modelling                      |    15% | Strongly-typed IDs apply; `Money` persists as two columns; unique indexes and check constraints in place. |
| Endpoints                      |    25% | All seven endpoints work; SQL signatures match the spec; tests pass.                      |
| The performance comparison     |    15% | Three loading strategies implemented; the perf endpoint reports consistent data; PERF.md is correct. |
| Raw SQL safety                 |    10% | Search endpoint parameterizes; injection attempt does not damage the table.               |
| Interceptor                    |     5% | Slow-query interceptor wired and tested.                                                  |
| Tests                          |    10% | All required tests present and passing.                                                   |
| Write-up                       |     5% | `PERF.md` is a clear one-page report with measured numbers and a defensible recommendation. |

A passing grade is 70%. An exceptional grade is 90%+; that level represents work that, in a real codebase, would pass a senior code review on the first round.
