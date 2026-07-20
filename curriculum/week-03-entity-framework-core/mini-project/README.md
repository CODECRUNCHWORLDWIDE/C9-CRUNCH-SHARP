# Mini-Project — Ledger Persistence

> Take the **Ledger REST API** from Week 2's mini-project and replace its in-memory `ConcurrentDictionary` with a real EF Core 9 `DbContext` against a SQLite file. Same five transaction endpoints, same four report endpoints, same admin endpoint. New: a `LedgerDbContext`, three entity types with one-to-many and many-to-many relationships, real migrations committed to the repo, applied on startup, and a full integration-test suite that runs against an in-memory SQLite database per test. By the end you have a service whose schema is versioned in Git and whose tests stand up a real database in milliseconds.

This mini-project is deliberately built on Week 2's deliverable so you spend your hours on the new ideas — `DbContext`, modeling, migrations, `IQueryable<T>` translation, integration testing — rather than re-modeling a domain. If you skipped Week 2's mini-project, you can copy its `Transaction`, `CreateTransactionRequest`, and endpoint classes verbatim; the point of *this* mini-project is the EF Core 9 layer underneath.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

An ASP.NET Core 9 service called `Ledger.Api` that exposes the Ledger domain over typed REST endpoints, now backed by SQLite via EF Core 9:

```
GET    /api/v1/transactions
GET    /api/v1/transactions/{id:int}
POST   /api/v1/transactions
PUT    /api/v1/transactions/{id:int}
DELETE /api/v1/transactions/{id:int}

GET    /api/v1/accounts
GET    /api/v1/categories
GET    /api/v1/tags

GET    /api/v1/reports/summary
GET    /api/v1/reports/daily
GET    /api/v1/reports/categories
GET    /api/v1/reports/top?kind=debit&count=5

POST   /api/v1/admin/load-csv   (bulk-inserts a CSV file into the database)
GET    /health
```

Persistence is a `LedgerDbContext : DbContext` with three `DbSet<T>` — `Transactions`, `Accounts`, `Categories`, `Tags` — and a join table for the `Transaction <-> Tag` many-to-many. The service applies pending migrations on startup with `Database.MigrateAsync`. The connection string lives in `appsettings.json` and points at `Data Source=ledger.db` by default; in tests it points at `Data Source=:memory:` against an explicitly-opened `SqliteConnection` so the schema is recreated per test fixture.

The service must:

- Apply pending migrations on startup before serving traffic.
- Use `AsNoTracking()` on every GET endpoint, projection on every report endpoint, and tracked reads only on PUT/DELETE handlers that need them.
- Generate an accurate OpenAPI 3.1 document at `/openapi/v1.json`.
- Use `TypedResults<...>` everywhere — every endpoint has typed success and typed error responses.
- Validate `POST` and `PUT` bodies via data annotations + `.WithParameterValidation()`.
- Return RFC 7807 problem-details JSON on every error path.
- Use `ValidateScopes = true` and `ValidateOnBuild = true` from startup.
- Be coverable by an integration-test suite running `WebApplicationFactory<Program>` end-to-end against an in-memory SQLite database — at least **40 passing tests**.

By the end you'll have a public GitHub repo of ~700 lines of C# (excluding tests) that compiles clean, runs on Kestrel, applies its migrations on startup, returns OpenAPI 3.1 for every endpoint, and ships with at least 40 passing integration tests against a real (in-memory) SQL database.

---

## Rules

- **You may** read Microsoft Learn, the EF Core source, lecture notes, and the source of the libraries listed below.
- **You may NOT** depend on any third-party NuGet package other than:
  - `Microsoft.EntityFrameworkCore.Sqlite` (the provider).
  - `Microsoft.EntityFrameworkCore.Design` (for `dotnet ef migrations`).
  - `MinimalApis.Extensions` (for `.WithParameterValidation()`).
  - `Swashbuckle.AspNetCore.SwaggerUI` (for the in-browser viewer).
  - `Microsoft.AspNetCore.Mvc.Testing` (in the test project).
  - `Microsoft.Data.Sqlite` (for the in-memory connection in tests).
  - `xUnit` and `Microsoft.NET.Test.Sdk`.
- **No Dapper. No Newtonsoft.Json. No FluentValidation. No AutoMapper. No repository wrapper.** EF Core handles every query. `System.Text.Json` handles every payload. Hand-written DTOs are fine; AutoMapper is not.
- Target framework: `net9.0`. C# language version: the default for the SDK (`13.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.

---

## Acceptance criteria

- [ ] A new public GitHub repo named `c9-week-03-ledger-persistence-<yourhandle>`.
- [ ] Solution layout:
  ```
  Ledger.Api/
  ├── Ledger.Api.sln
  ├── .gitignore
  ├── Directory.Build.props
  ├── samples/
  │   └── sample.csv
  ├── src/
  │   ├── Ledger.Core/
  │   │   ├── Ledger.Core.csproj
  │   │   ├── Entities/
  │   │   │   ├── Transaction.cs
  │   │   │   ├── Account.cs
  │   │   │   ├── Category.cs
  │   │   │   └── Tag.cs
  │   │   ├── Configurations/
  │   │   │   ├── TransactionConfiguration.cs
  │   │   │   ├── AccountConfiguration.cs
  │   │   │   ├── CategoryConfiguration.cs
  │   │   │   └── TagConfiguration.cs
  │   │   ├── LedgerDbContext.cs
  │   │   └── Migrations/
  │   │       ├── 20260513_InitialCreate.cs
  │   │       ├── 20260514_AddTransactionIndex.cs
  │   │       └── LedgerDbContextModelSnapshot.cs
  │   └── Ledger.Api/
  │       ├── Ledger.Api.csproj
  │       ├── Program.cs
  │       ├── appsettings.json
  │       ├── Ledger.Api.http
  │       ├── Endpoints/
  │       │   ├── TransactionEndpoints.cs
  │       │   ├── ReportEndpoints.cs
  │       │   ├── LookupEndpoints.cs
  │       │   └── AdminEndpoints.cs
  │       └── Contracts/
  │           ├── CreateTransactionRequest.cs
  │           ├── UpdateTransactionRequest.cs
  │           └── ReportDtos.cs
  └── tests/
      └── Ledger.Api.Tests/
          ├── Ledger.Api.Tests.csproj
          ├── LedgerWebApplicationFactory.cs
          ├── TransactionEndpointsTests.cs
          ├── ReportEndpointsTests.cs
          ├── LookupEndpointsTests.cs
          └── AdminEndpointsTests.cs
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports **at least 40** passing tests covering every endpoint and every status code in the contract.
- [ ] `dotnet ef database update --project src/Ledger.Core --startup-project src/Ledger.Api` applies the migrations cleanly against a fresh `ledger.db`.
- [ ] `dotnet run --project src/Ledger.Api` boots Kestrel, applies any pending migrations, seeds three default accounts and ten default categories if the database is empty, and serves the `.http` file's smoke requests with the expected status codes.
- [ ] `GET /api/v1/transactions` returns the seeded data when the app was started against a database with rows.
- [ ] `POST /api/v1/transactions` with an empty `memo` returns 400 + RFC 7807 problem details with the `Memo` error.
- [ ] `GET /openapi/v1.json` returns a valid OpenAPI 3.1 document describing every endpoint.
- [ ] `Swashbuckle.AspNetCore.SwaggerUI` is mounted at `/swagger` in `Development` only.
- [ ] `LedgerDbContext` is registered with `AddDbContext<LedgerDbContext>` (scoped). Endpoint classes are scoped. The DI graph passes `ValidateOnBuild`.
- [ ] **Zero `!` (null-forgiving) operators** outside of EF navigation properties (which require `null!` for the unfortunate compromise between EF's instantiation pattern and nullable reference types).
- [ ] No N+1 queries in any endpoint — verified by enabling `LogTo` in a debug run and confirming each endpoint emits a single SQL query (or, for endpoints that include collections, a small number of `AsSplitQuery` round trips).
- [ ] `dotnet ef migrations script 0 --idempotent --output schema.sql` produces a clean, non-empty file. `schema.sql` is committed.
- [ ] `README.md` in the repo root includes:
  - One paragraph describing the project.
  - The exact commands to clone, build, migrate, test, and run.
  - The full `curl` smoke suite (mirror what's in `Ledger.Api.http`).
  - A "Things I learned" section with at least 4 specific items about EF Core 9.

---

## Suggested order of operations

You'll find it easier if you build incrementally rather than trying to write the whole thing at once. The phases below mirror the suggested-schedule split: Thursday on the data layer, Friday on the API, Saturday on tests and polish.

### Phase 1 — Skeleton + EF Core wiring (~60 min)

```bash
mkdir Ledger.Api && cd Ledger.Api
dotnet new sln -n Ledger.Api
dotnet new gitignore && git init

dotnet new classlib -n Ledger.Core      -o src/Ledger.Core
dotnet new web      -n Ledger.Api       -o src/Ledger.Api
dotnet new xunit    -n Ledger.Api.Tests -o tests/Ledger.Api.Tests

dotnet sln add src/Ledger.Core/Ledger.Core.csproj
dotnet sln add src/Ledger.Api/Ledger.Api.csproj
dotnet sln add tests/Ledger.Api.Tests/Ledger.Api.Tests.csproj

dotnet add src/Ledger.Core package Microsoft.EntityFrameworkCore
dotnet add src/Ledger.Core package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/Ledger.Core package Microsoft.EntityFrameworkCore.Design
dotnet add src/Ledger.Api  reference src/Ledger.Core/Ledger.Core.csproj
dotnet add src/Ledger.Api  package MinimalApis.Extensions
dotnet add src/Ledger.Api  package Swashbuckle.AspNetCore.SwaggerUI

dotnet add tests/Ledger.Api.Tests reference src/Ledger.Api/Ledger.Api.csproj
dotnet add tests/Ledger.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Ledger.Api.Tests package Microsoft.EntityFrameworkCore.Sqlite
dotnet add tests/Ledger.Api.Tests package Microsoft.Data.Sqlite
```

Add the standard `Directory.Build.props` (treat warnings as errors, nullable on, implicit usings on, latest C#).

Commit: `Initial Ledger.Api solution skeleton with EF Core packages`.

### Phase 2 — Entities + configurations (~60 min)

Model four entities in `Ledger.Core/Entities/`:

```csharp
public sealed class Transaction
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Memo { get; set; } = "";

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public List<Tag> Tags { get; set; } = [];
}

public sealed class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public List<Transaction> Transactions { get; set; } = [];
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Transaction> Transactions { get; set; } = [];
}

public sealed class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Transaction> Transactions { get; set; } = [];
}
```

Write one `IEntityTypeConfiguration<T>` per entity in `Ledger.Core/Configurations/`. Each should set max lengths, required flags, the relationship configuration (cascade or restrict — for a financial domain `OnDelete(DeleteBehavior.Restrict)` is the right choice on `Transaction.Account` and `Transaction.Category`), and the indexes the queries will need: `Transactions.Date`, `Transactions.(AccountId, Date)`, `Tags.Name UNIQUE`, `Accounts.Name UNIQUE`, `Categories.Name UNIQUE`.

Write `LedgerDbContext` with the four `DbSet<T>` and an `ApplyConfigurationsFromAssembly` in `OnModelCreating`.

Commit: `Ledger.Core: entities + fluent configurations + LedgerDbContext`.

### Phase 3 — First migration + apply (~30 min)

```bash
cd src/Ledger.Core
dotnet ef migrations add InitialCreate --startup-project ../Ledger.Api
cd ../..
```

Open the generated migration and read it. Verify it creates `Transactions`, `Accounts`, `Categories`, `Tags`, and the `TagTransaction` join table; that the foreign keys are non-nullable; that the indexes you configured appear as `CreateIndex` calls.

Configure `Ledger.Api/Program.cs` to apply migrations on startup:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    await db.Database.MigrateAsync();
}
```

Apply against a development `ledger.db`:

```bash
dotnet ef database update --project src/Ledger.Core --startup-project src/Ledger.Api
sqlite3 src/Ledger.Api/ledger.db ".tables"
```

Commit: `Initial migration; auto-apply on startup`.

### Phase 4 — Contracts and endpoint classes (~90 min)

Reuse the Week 2 contracts (`CreateTransactionRequest`, `UpdateTransactionRequest`) and add new ones for the lookups and reports.

Rewrite the four endpoint classes against `LedgerDbContext` rather than `ITransactionStore`:

```csharp
public sealed class TransactionEndpoints(LedgerDbContext db)
{
    public async Task<Ok<IReadOnlyList<TransactionDto>>> GetAll(CancellationToken ct)
    {
        var rows = await db.Transactions
            .AsNoTracking()
            .OrderByDescending(t => t.Date)
            .Select(t => new TransactionDto(
                t.Id, t.Date, t.Amount, t.Memo,
                t.Account.Name, t.Category.Name,
                t.Tags.Select(tag => tag.Name).ToList()))
            .ToListAsync(ct);
        return TypedResults.Ok((IReadOnlyList<TransactionDto>)rows);
    }

    public async Task<Results<Ok<TransactionDto>, NotFound>> GetById(int id, CancellationToken ct)
    {
        var row = await db.Transactions
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TransactionDto(
                t.Id, t.Date, t.Amount, t.Memo,
                t.Account.Name, t.Category.Name,
                t.Tags.Select(tag => tag.Name).ToList()))
            .FirstOrDefaultAsync(ct);
        return row is null ? TypedResults.NotFound() : TypedResults.Ok(row);
    }

    // ... Create / Update / Remove with appropriate TypedResults ...
}
```

Do the same for `ReportEndpoints` (every method is a projection-only LINQ pipeline that translates to a single SQL query), `LookupEndpoints` (three read-only endpoints for the lookup tables), and `AdminEndpoints` (the CSV-load endpoint, now using `db.Transactions.AddRange` plus a single `SaveChangesAsync`).

The key discipline: **every read uses `AsNoTracking()` and projects into a DTO**. Entities are for write paths; DTOs are for HTTP responses.

Commit: `Endpoint classes against LedgerDbContext`.

### Phase 5 — `Program.cs` wiring (~45 min)

Aim for ~100 lines of `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider((_, o) => { o.ValidateScopes = true; o.ValidateOnBuild = true; });

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<LedgerDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Ledger") ?? "Data Source=ledger.db"));
builder.Services.AddScoped<TransactionEndpoints>();
builder.Services.AddScoped<ReportEndpoints>();
builder.Services.AddScoped<LookupEndpoints>();
builder.Services.AddScoped<AdminEndpoints>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
    await db.Database.MigrateAsync();
    await Seeder.SeedDefaultsAsync(db);
}

// ...endpoint mapping (mirroring Week 2's MapGroup pattern)...

app.Run();

public partial class Program;
```

The `Seeder.SeedDefaultsAsync` is idempotent — it checks `db.Accounts.AnyAsync()` first; if any exist it returns immediately. Otherwise it inserts three default accounts ("Checking", "Savings", "Credit Card") and ten default categories ("food", "rent", "utilities", "entertainment", "income", "transfer", "tax", "health", "transportation", "other").

Commit: `Program.cs wiring with DbContext registration + startup seed`.

### Phase 6 — Integration tests against in-memory SQLite (~120 min)

The trick for fast, isolated EF Core tests: open a single `SqliteConnection` against `Data Source=:memory:`, keep it open for the test class lifetime, and pass it explicitly to `UseSqlite(...)`. The schema lives only as long as the connection. Each test class fixture gets a fresh schema.

`LedgerWebApplicationFactory.cs`:

```csharp
public sealed class LedgerWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync() => await _connection.OpenAsync();
    public new async Task DisposeAsync() => await _connection.CloseAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LedgerDbContext>>();
            services.AddDbContext<LedgerDbContext>(o => o.UseSqlite(_connection));

            using var scope = services.BuildServiceProvider().CreateScope();
            scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.EnsureCreated();
        });
    }
}
```

Write at least **40 tests** across the four test files. Suggested coverage:

- `TransactionEndpointsTests`: 12 tests — list (empty, populated), get by id (found, 404), create (valid, invalid memo, invalid category, unknown account), update (valid, 404), delete (valid, 404), round-trip create-then-update-then-delete.
- `ReportEndpointsTests`: 10 tests — summary on empty store, summary on populated store, daily totals (one day, multiple days), category totals, top debits, top credits, malformed `kind` parameter, paging defaults, paging custom, empty fixture for all four reports.
- `LookupEndpointsTests`: 6 tests — list accounts (seeded defaults), list categories (seeded defaults), list tags (empty initially, populated after a tag is added via the transaction endpoint), each with both happy and edge cases.
- `AdminEndpointsTests`: 6 tests — load valid CSV, load CSV with malformed rows, load CSV with unknown account/category (decide your behaviour: reject or auto-create — document the decision), idempotent re-load behaviour, etc.
- A `SchemaTests` file with 6 tests — every entity has the expected columns, every index exists, the foreign keys are wired correctly. Use `db.Model.FindEntityType(typeof(Transaction))` to inspect the model at runtime.

Commit per file as you write each cluster.

### Phase 7 — Smoke file + polish (~45 min)

Update `Ledger.Api.http` from Week 2 to include the new lookup endpoints. Run `dotnet format`. Run `dotnet publish` to confirm the publish path still works.

Generate the idempotent script and commit it:

```bash
dotnet ef migrations script 0 --idempotent --output schema.sql \
    --project src/Ledger.Core --startup-project src/Ledger.Api
```

Update the root `README.md` with the four "things I learned" items. Push.

Commit: `Polish: schema.sql, updated .http, README with takeaways`.

---

## Example expected responses

```http
GET /api/v1/reports/summary

200 OK
{ "net": 187.51, "daysObserved": 3, "categories": 3 }
```

```http
POST /api/v1/transactions  Content-Type: application/json
{ "date": "2026-05-13", "amount": 10.00, "memo": "", "category": "food", "accountId": 1 }

400 Bad Request
Content-Type: application/problem+json
{
  "type":   "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title":  "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Memo": ["The Memo field is required."] }
}
```

---

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 10% | `dotnet build`, `dotnet test`, `dotnet ef database update`, and the `.http` smoke all clean on a fresh clone |
| Schema correctness | 15% | Migration generates the expected tables, columns, indexes, and FKs; `schema.sql` round-trips |
| Endpoint correctness | 15% | All 12+ endpoints hit every documented status code |
| LINQ/SQL quality | 20% | Every read uses `AsNoTracking` + projection; no N+1; no accidental client evaluation; the SQL log shows tight queries |
| DI hygiene | 10% | `ValidateScopes` and `ValidateOnBuild` on; scoped DbContext; scoped endpoint classes |
| Tests | 20% | At least 40 tests via `WebApplicationFactory<Program>` covering happy/error paths, against in-memory SQLite |
| README + smoke file | 10% | A new developer can clone, migrate, test, and exercise the API in under 10 minutes |

---

## Stretch (optional)

- Add a `[Timestamp]`-style concurrency token to `Transaction` (use a shadow `Version` int + an `ISaveChangesInterceptor` that bumps it). Write a test that demonstrates `DbUpdateConcurrencyException` on a concurrent PUT.
- Add an `ExecuteUpdateAsync`-powered `POST /api/v1/admin/archive?olderThan=YYYY-MM-DD` that archives every transaction older than the cutoff in one SQL statement. Bench it against a 100,000-row dataset.
- Wire the `Npgsql.EntityFrameworkCore.PostgreSQL` provider in a second `appsettings.Production.json` profile so the same migrations apply against a real PostgreSQL container. Document the SQL differences in `notes/sqlite-vs-postgres.md`.
- Add a `GET /api/v1/transactions/search?q=...` that uses `EF.Functions.Like` for memo substring search, with paging via a `Pagination` parameter object.
- Add a `BackgroundService` that re-computes a cached "month-to-date balance" every minute via `IServiceScopeFactory`. (Background work is Week 8 material — try it now as a stretch.)

---

## What this prepares you for

- **Week 4** introduces async deeply. Your `LedgerDbContext` is already async-only at the public API; Week 4 will replace synchronous helper paths with `IAsyncEnumerable<Transaction>` streaming straight off `DbSet<Transaction>`.
- **Week 5** introduces the OO and DI patterns that production EF Core codebases lean on (repositories — sparingly — domain services, change interceptors).
- **Week 6** (per the canonical syllabus) revisits EF Core against PostgreSQL with Dapper alongside for hot queries. Your `LedgerDbContext` survives the provider swap with one line of `.csproj` change.

---

## Resources

- *EF Core overview*: <https://learn.microsoft.com/en-us/ef/core/>
- *EF Core migrations*: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>
- *Querying data with EF Core*: <https://learn.microsoft.com/en-us/ef/core/querying/>
- *EF Core performance*: <https://learn.microsoft.com/en-us/ef/core/performance/>
- *Integration tests with `WebApplicationFactory<T>`*: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>
- *SQLite in-memory testing pattern*: <https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database>

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes the setup commands, the `curl` smoke suite, and the example output for the summary and top reports.
3. Make sure `dotnet build`, `dotnet test`, `dotnet ef database update`, and `dotnet run --project src/Ledger.Api` all green on a fresh clone.
4. Post the repo URL in your cohort tracker. You shipped a real persistence-backed REST service; show it.
