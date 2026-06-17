# Lecture 3 — Raw SQL, Value Converters, Owned Types, Compiled Queries, and `dotnet-counters`

> **Time:** 2 hours. The lecture splits cleanly: raw SQL and SQL-injection safety in the first hour, modelling (converters and owned types) and compiled queries in the second. **Prerequisites:** Lectures 1 and 2 of this week. **Citations:** Microsoft Learn's SQL-queries page at <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries>, the value-conversions chapter at <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions>, and Stephen Toub's "Performance Improvements in .NET 8" post at <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/>.

## 1. The case for raw SQL — and against avoiding it

EF Core's LINQ surface is large. It handles `Where`, `Select`, `GroupBy`, `Join`, projection to anonymous types and records, server-side aggregation, window functions (via `EF.Functions`), JSON column queries on PostgreSQL and SQL Server, and a long list of provider-specific extensions. For 95% of working programmer time, LINQ is enough and is the more readable choice. The remaining 5% are queries that LINQ either cannot express, can express but translates poorly, or that you have a hand-tuned version of from your DBA that you want to use verbatim.

For those cases, EF Core gives you two escape hatches: `FromSqlInterpolated` for `SELECT` queries that return entities (or types EF can materialize), and `Database.ExecuteSqlInterpolatedAsync` for non-query statements (`UPDATE`/`INSERT`/`DELETE` you want to send straight, plus DDL during migrations). Both keep parameterization automatic; both let you compose further LINQ on top in the query case; neither requires you to abandon the rest of EF Core.

The most common mistake here is the **string-concatenation reflex**. A developer who has not internalized SQL injection writes:

```csharp
// CATASTROPHICALLY UNSAFE — DO NOT WRITE THIS, EVER.
var products = await db.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Name = '" + userInput + "'")
    .ToListAsync();
```

This is the classic SQL-injection vulnerability. If `userInput` is `x'; DROP TABLE Products; --` the database happily drops the table. The OWASP cheat sheet at <https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html> is the canonical write-up; read it before writing your first raw query and re-read it before every code review you do. The cure is parameterization. EF Core gives you two ways to parameterize, one accidental-safe and one accidental-unsafe, and you should reach for the accidental-safe one always.

## 2. `FromSqlInterpolated` — the accidental-safe form

`FromSqlInterpolated` takes a C# **interpolated string** (the kind with `$"..."`). Every interpolated value becomes a parameter automatically:

```csharp
var name = userInput;          // potentially malicious
var products = await db.Products
    .FromSqlInterpolated($"SELECT * FROM Products WHERE Name = {name}")
    .ToListAsync();
```

The SQL EF actually emits is:

```sql
SELECT * FROM Products WHERE Name = @p0   -- @p0 = userInput, bound as a string parameter
```

The key insight is that **the C# compiler captures the interpolation parts separately**. The format string template (`"SELECT ... = "`) and the value (`name`) are passed to EF as a `FormattableString`; EF takes the template literally and binds each `{...}` slot as a parameter. There is no string concatenation. `name` could be `'; DROP TABLE Products; --` and the database would still execute `SELECT * FROM Products WHERE Name = '''; DROP TABLE Products; --'`, which is a legitimate (if absurd) search for a product with that bizarre name. The injection is impossible.

This is the form you reach for **always** unless you have a very specific reason not to. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#passing-parameters>.

## 3. `FromSqlRaw` — the accidental-unsafe form

`FromSqlRaw` takes a plain string and a `params object[] parameters`. Used correctly, it is exactly as safe as `FromSqlInterpolated`:

```csharp
// Safe — the {0} is a parameter placeholder, not string formatting.
var products = await db.Products
    .FromSqlRaw("SELECT * FROM Products WHERE Name = {0}", name)
    .ToListAsync();
```

The `{0}` is **not** string formatting; it is a parameter placeholder. EF rewrites it to `@p0` and binds `name` to it. The output SQL is the same as the interpolated version.

The reason `FromSqlRaw` exists at all, given `FromSqlInterpolated` is strictly safer, is that occasionally you want to **build the format string dynamically** — different `WHERE` clauses depending on a runtime flag. The interpolated form cannot do this; the raw form can:

```csharp
var template = useExactMatch
    ? "SELECT * FROM Products WHERE Name = {0}"
    : "SELECT * FROM Products WHERE Name LIKE {0}";

var products = await db.Products
    .FromSqlRaw(template, name)
    .ToListAsync();
```

This is safe **as long as** the `template` string is constructed from compile-time literals only, never from user input. The moment you start concatenating user input into `template`, you have re-introduced the injection vulnerability. The standing C9 rule is **default to `FromSqlInterpolated`; reach for `FromSqlRaw` only when the template needs to vary; never put user input into the template, only into the parameters**.

## 4. Composing LINQ on top of a raw query

A `FromSql{Interpolated,Raw}` result is an `IQueryable<TEntity>`. You can compose further LINQ on top:

```csharp
var query = db.Products
    .FromSqlInterpolated($"SELECT * FROM Products WHERE CategoryId = {catId}")
    .Where(p => p.Price > 100m)
    .OrderBy(p => p.Name)
    .Take(20);
```

The SQL EF emits is a **wrapping** `SELECT` that uses the raw query as a subquery:

```sql
SELECT * FROM (
    SELECT * FROM Products WHERE CategoryId = @p0
) AS p
WHERE p."Price" > 100
ORDER BY p."Name"
LIMIT 20
```

This composes cleanly **only if the raw query is itself composable** — that is, it is a single `SELECT` statement with no trailing `ORDER BY`, no `LIMIT`, no semicolon, no statement terminator. If your raw query says `SELECT * FROM Products WHERE CategoryId = {0} ORDER BY Name LIMIT 5`, the wrapping `SELECT` will produce malformed SQL (a `LIMIT` inside a subquery on PostgreSQL, an `ORDER BY` inside a derived table on SQL Server — provider-dependent). Use `IgnoreQueryFilters()` if global filters are in play; do not try to put ordering inside the raw query. Cite <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#composing-with-linq>.

## 5. `ExecuteSqlInterpolatedAsync` — the non-query escape hatch

For statements that do not return rows — `INSERT`, `UPDATE`, `DELETE`, DDL — use `Database.ExecuteSqlInterpolatedAsync`:

```csharp
var rowsAffected = await db.Database
    .ExecuteSqlInterpolatedAsync($@"
        UPDATE Products
        SET Price = Price * 1.1
        WHERE CategoryId = {catId}");
```

The method returns the number of rows affected. The same parameterization rules apply: `{catId}` becomes `@p0`. Use this when `ExecuteUpdateAsync` (from Lecture 2) cannot express what you need — for example, a multi-table update, a `MERGE` statement, or an `INSERT ... SELECT`.

A word of caution: `ExecuteSqlInterpolatedAsync` runs **outside** the change tracker. If you `UPDATE` a row that EF has tracked, the in-memory entity will be stale. Either evict the entity first (`db.Entry(p).State = EntityState.Detached`), reload it explicitly (`await db.Entry(p).ReloadAsync()`), or do not mix the two patterns on the same data.

## 6. Value converters — mapping types EF does not natively support

EF Core's default type mappings cover the .NET primitives, `DateTime`, `Guid`, `decimal`, `string`, `byte[]`. For richer domain types — money, dates with timezones, strongly-typed IDs, enums-as-strings — you write a **value converter**.

The shape of a converter is a pair of expression-tree lambdas: one from the CLR type to the provider type, one from the provider type back. The simplest case is "enum stored as string":

```csharp
public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<Order>()
        .Property(o => o.Status)
        .HasConversion<string>();
}
```

`.HasConversion<string>()` is shorthand for `.HasConversion(new EnumToStringConverter<OrderStatus>())`. The wire type becomes `varchar(...)`, the column stores `"Pending"` rather than `0`, and the conversion runs on every read and write. The benefit is readability in the database (a `SELECT` against `Status` returns human-readable values); the cost is a few extra bytes per row and a slight perf cost on the conversion. Citation: <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#built-in-converters>.

### 6.1 A `Money` value converter — two designs

For a domain `Money` value object — `record struct Money(decimal Amount, string Currency)` — you have two reasonable designs:

**Design A: store as one column, encode as `"USD 19.99"`.** Simple, breaks aggregation in the database (a `SUM(price)` is meaningless on a string), and forces every comparison to round-trip through the CLR.

**Design B: store as two columns, one numeric and one varchar.** Native database aggregation works, indexes are useful, comparisons happen in the engine. This is the right answer for almost every real codebase.

The implementation of Design B uses **owned types**, covered in the next section. For Design A — which is the right answer when the currency is invariant or the field is rarely aggregated — the converter is:

```csharp
public readonly record struct Money(decimal Amount, string Currency)
{
    public string ToWire() => $"{Currency} {Amount:F2}";
    public static Money FromWire(string s)
    {
        var parts = s.Split(' ', 2);
        return new Money(decimal.Parse(parts[1]), parts[0]);
    }
}

protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<Product>()
        .Property(p => p.Price)
        .HasConversion(
            m => m.ToWire(),
            s => Money.FromWire(s));
}
```

This is the literal converter shape: two lambdas, one each direction. EF inlines them into the materializer at model-build time, so the runtime overhead is one method call per row per property.

### 6.2 Strongly-typed IDs — the most useful converter pattern

The most useful converter pattern in real codebases is the **strongly-typed ID**. The problem: every entity has an `int Id`, and they are interchangeable at the type-system level. `void TransferFunds(int fromAccountId, int toCustomerId)` is a bug factory; the type system does not stop you from swapping the arguments. The cure:

```csharp
public readonly record struct AccountId(int Value);
public readonly record struct CustomerId(int Value);

public sealed class Account
{
    public AccountId Id { get; set; }
    public string Owner { get; set; } = "";
}

public sealed class Customer
{
    public CustomerId Id { get; set; }
    public string Name { get; set; } = "";
}

protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<Account>()
        .Property(a => a.Id)
        .HasConversion(id => id.Value, value => new AccountId(value));

    builder.Entity<Customer>()
        .Property(c => c.Id)
        .HasConversion(id => id.Value, value => new CustomerId(value));
}
```

The function signature becomes `void TransferFunds(AccountId from, CustomerId to)` and the compiler now refuses to let you pass them in the wrong order. The cost is the converter (one method call per ID read/write); the benefit is that an entire class of bug — argument-order confusion across entity types — becomes impossible. The EF Core team's perf guide notes the cost is negligible, the bug-prevention value is large, and recommends this pattern broadly. Cite <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#bulk-configuring-a-value-conversion>.

## 7. Owned types — value objects inside another table's row

For a value object that has its own properties but does not deserve its own table — an `Address` belongs to a `Customer` and is meaningless without one — EF Core gives you the **owned type**:

```csharp
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Address ShippingAddress { get; set; } = new();
    public Address BillingAddress { get; set; } = new();
}

public sealed class Address
{
    public string Line1 { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
}

protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<Customer>(cb =>
    {
        cb.OwnsOne(c => c.ShippingAddress, ab =>
        {
            ab.Property(a => a.Line1).HasColumnName("ShippingLine1");
            ab.Property(a => a.City).HasColumnName("ShippingCity");
            ab.Property(a => a.PostalCode).HasColumnName("ShippingPostalCode");
        });
        cb.OwnsOne(c => c.BillingAddress, ab =>
        {
            ab.Property(a => a.Line1).HasColumnName("BillingLine1");
            ab.Property(a => a.City).HasColumnName("BillingCity");
            ab.Property(a => a.PostalCode).HasColumnName("BillingPostalCode");
        });
    });
}
```

The result is a single `Customers` table with six address columns (three for shipping, three for billing) plus the customer columns. The C# code uses `customer.ShippingAddress.City` naturally, EF maps each owned property to its column, and there is no `Addresses` table. The address has no independent identity; it is loaded with the customer, deleted with the customer, and never queried in isolation.

For the "list of addresses per customer" case — a customer can have N addresses — use `OwnsMany`:

```csharp
cb.OwnsMany(c => c.Addresses, ab =>
{
    ab.WithOwner().HasForeignKey("CustomerId");
    ab.Property<int>("Id");
    ab.HasKey("Id");
});
```

This produces an `Address` table with a foreign key back to `Customer`, but the type is still "owned" — its lifetime is tied to the customer's, deletes cascade, and EF does not let you query `Addresses` independently of a customer.

### 7.1 EF Core 8's `ComplexProperty` — the modern replacement for owned-type-on-the-same-row

For the value-object-on-the-same-row case (the `OwnsOne` example above), EF Core 8 introduced **complex properties**:

```csharp
cb.ComplexProperty(c => c.ShippingAddress);
cb.ComplexProperty(c => c.BillingAddress);
```

Complex properties have less of the "owned entity" machinery and behave more like the plain value objects they are: no implicit key, no implicit identity, no `IncludeXxx` calls, no surprises around cascading deletes. The standing advice from the EF team is **use `ComplexProperty` for new code; `OwnsOne` remains for existing code or for cases that need EF Core 7's behaviour**. Cite <https://learn.microsoft.com/en-us/ef/core/modeling/complex-types>.

## 8. Compiled queries — the hot-path optimisation

`EF.CompileAsyncQuery` caches the LINQ-to-SQL translation step. The translation step is, depending on query complexity, between 20us and 100us of CPU work per execution. For a query that runs once, that cost is invisible. For a query in a hot loop running 10,000 times per second, that cost is 20-100ms of CPU per second per executor — a lot.

The compiled-query shape:

```csharp
private static readonly Func<CatalogDb, int, IAsyncEnumerable<Product>> _byCategory =
    EF.CompileAsyncQuery((CatalogDb db, int categoryId) =>
        db.Products.AsNoTracking().Where(p => p.CategoryId == categoryId));

public async Task<IReadOnlyList<Product>> GetByCategory(int categoryId)
{
    var result = new List<Product>();
    await foreach (var p in _byCategory(db, categoryId))
    {
        result.Add(p);
    }
    return result;
}
```

Three details:

1. The compiled delegate is a `static` field. The whole point is that the translation runs **once at program startup** (when the static field is initialised), and every subsequent call reuses the translated SQL.
2. The delegate takes the `DbContext` as its first parameter. Different `DbContext` instances are fine; the compiled translation is shared across all of them.
3. The delegate returns `IAsyncEnumerable<T>` for streaming queries or `Task<T>` for `FirstOrDefaultAsync`-shaped queries. Match the shape of the query you intended.

Measured cost from the EF perf guide and confirmed in C9 testing:

| Query shape                          | Uncompiled (mean) | Compiled (mean) | Delta   |
|--------------------------------------|------------------:|----------------:|--------:|
| `Where + AsNoTracking + ToList`      |              94us |            70us |   -24us |
| `FirstOrDefault by PK`               |              68us |            48us |   -20us |
| Multi-join with `Include + ThenInclude` |          240us |           180us |   -60us |

The 20-30us savings are real. For a query running 10,000 times per second, that is 200-600ms of CPU per second back. For a query running 10 times a minute, the saving is invisible and the cost is the static-field plumbing.

Stephen Toub's "Performance Improvements in .NET 8" post at <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/> notes that the EF Core 8 translation pipeline got faster on its own — by approximately 20% on common LINQ shapes — which means **the case for compiled queries weakened slightly in .NET 8**. Measure your specific workload before reaching for them. The rule of thumb: compile queries that are hot enough to show up in a `dotnet-trace` profile; do not compile queries you have not measured.

## 9. `dotnet-counters` — the production observability primitive

Every modern .NET 8 process exposes a stream of counters through the `EventCounter` and `Meter` APIs. EF Core 8 publishes the source `Microsoft.EntityFrameworkCore` with these counters:

- `active-db-contexts` — number of `DbContext` instances currently alive.
- `total-queries` — cumulative query count since process start.
- `queries-per-second` — rolling rate.
- `total-save-changes` — cumulative `SaveChanges` calls.
- `save-changes-per-second` — rolling rate.
- `compiled-query-cache-hit-rate` — percentage of LINQ queries served from the translation cache.
- `total-execution-strategy-operation-failures` — count of retried operations under the resilience pipeline.

You read the counters with the `dotnet-counters` global tool:

```bash
dotnet-counters monitor --process-id <pid> Microsoft.EntityFrameworkCore
```

The output refreshes every second. For a steady-state web service, you want `queries-per-second` proportional to your request rate and `compiled-query-cache-hit-rate` above 90% — anything lower suggests you have queries being constructed at runtime that miss the cache. Citation: <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/event-counters>.

The `Microsoft.EntityFrameworkCore.Diagnostics` namespace also lets you subscribe to individual events programmatically. The interceptor pattern, for cross-cutting concerns like "log every query that took more than 100ms":

```csharp
public sealed class SlowQueryInterceptor : DbCommandInterceptor
{
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Duration > TimeSpan.FromMilliseconds(100))
        {
            Console.Error.WriteLine($"SLOW: {eventData.Duration.TotalMilliseconds:F0}ms — {command.CommandText}");
        }
        return ValueTask.FromResult(result);
    }
}

// Registration:
builder.Services.AddDbContext<CatalogDb>(o =>
    o.UseNpgsql(...).AddInterceptors(new SlowQueryInterceptor()));
```

Interceptors run inside the query pipeline, see every command, and can mutate or short-circuit. They are how you wire EF Core into your application's logging and tracing infrastructure. Cite <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors>.

## 10. Reading the generated SQL with `ToQueryString` — revisited

We mentioned `ToQueryString` in Lecture 1; it is worth a second look now that we have raw SQL and compiled queries in the mix.

`ToQueryString` is the no-execution inspection tool:

```csharp
var query = db.Products
    .AsNoTracking()
    .Where(p => p.Price > 100m)
    .Include(c => c.Category)
    .OrderByDescending(p => p.Price);

Console.WriteLine(query.ToQueryString());
```

Output:

```sql
SELECT p."Id", p."CategoryId", p."Name", p."Price",
       c."Id", c."Name"
FROM "Products" AS p
LEFT JOIN "Categories" AS c ON p."CategoryId" = c."Id"
WHERE p."Price" > 100.0
ORDER BY p."Price" DESC
```

This is invaluable when you want to:

- Assert in a unit test that a LINQ expression compiles to a specific SQL shape (catch refactoring regressions).
- Show a reviewer the literal SQL of a complex query without standing up a database.
- Verify that a `FromSqlInterpolated` composes correctly with downstream LINQ.

`ToQueryString` does **not** work on compiled queries (the compiled delegate skips the translation step that `ToQueryString` introspects). It also does not work on `ExecuteUpdate`/`ExecuteDelete` (those are not `IQueryable<T>`). For those, you have to read the SQL log.

## 11. The week's mental model, restated

The week has been one long answer to one question: **what SQL did EF emit, and is it the SQL you wanted**. The tools you now have:

- The **SQL log** (`LogTo + EnableSensitiveDataLogging` in development) — see everything.
- **`ToQueryString`** — see one query without executing it.
- **`dotnet-counters`** — see the rates and the cache hit rate in aggregate.
- **`DbCommandInterceptor`** — subscribe to every command for cross-cutting logging.

The decisions you now have to make on every query:

- Track or not (`AsNoTracking` for reads).
- Identity-resolve or not (`AsNoTrackingWithIdentityResolution` when shared references matter).
- Eager, explicit, or projected (`Include`, `Entry().Collection().LoadAsync`, or just `Select`).
- Single or split (`AsSplitQuery` for multi-collection includes).
- LINQ or raw (`FromSqlInterpolated` for hand-written, never raw concatenation).
- Compiled or not (`EF.CompileAsyncQuery` for hot paths after measurement).
- Owned or related (`OwnsOne`/`ComplexProperty` for value objects, foreign keys for related entities).

By the end of the week, these decisions should be in your fingers — the conscious choice should be rare, the right answer obvious from the shape of the endpoint. That fluency is the deliverable.

## 12. Lecture-3 summary checklist

After this lecture you should be able to, without notes:

1. Write a `FromSqlInterpolated` query, recognise that interpolated values are parameterized, and explain why this prevents SQL injection.
2. Use `FromSqlRaw` with a `{0}` placeholder and explain when it is appropriate vs `FromSqlInterpolated`.
3. Compose further LINQ on top of a raw query and recognise the wrapping `SELECT` EF emits.
4. Use `Database.ExecuteSqlInterpolatedAsync` for non-query statements and recognise that it bypasses the change tracker.
5. Write a value converter from the CLR type to the provider type and back, using `HasConversion(lambda, lambda)`.
6. Apply the strongly-typed-ID pattern with a `record struct` and a one-line converter per entity.
7. Configure an owned type with `OwnsOne` and recognise that it produces extra columns on the parent table, not a new table.
8. Distinguish `OwnsOne` (owned entity) from `ComplexProperty` (value object on the same row) and choose between them for new code.
9. Compile a hot query with `EF.CompileAsyncQuery`, place the delegate in a `static` field, and explain the 20-30us per-call saving.
10. Read `dotnet-counters monitor Microsoft.EntityFrameworkCore` output and identify the steady-state shape of a healthy service: `queries-per-second` proportional to request rate, `compiled-query-cache-hit-rate` above 90%.
11. Write a `DbCommandInterceptor` that logs slow queries (>100ms) and register it via `AddInterceptors`.

The mini-project applies every one of these decisions to a small product-catalog API. The exercises drill each one in isolation. The challenges push you past the obvious answers into the trade-off matrix.
