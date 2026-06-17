# Mini-Project — Ledger REST

> Take the **Ledger CLI** from Week 1's mini-project and serve its domain over an ASP.NET Core 9 Minimal API. Same `Transaction` record, same `CsvLoader`, same `Reports`. New: typed REST endpoints, validation, OpenAPI, problem details, DI-registered persistence, and integration tests via `WebApplicationFactory<Program>`. Still no database — storage is an in-memory `ConcurrentDictionary` seeded from a CSV at startup. EF Core lands in Week 6.

This mini-project is deliberately built on Week 1's deliverable so you spend your hours on the new ideas — Minimal APIs, DI, OpenAPI, integration tests — rather than re-modeling a domain. If you skipped Week 1's mini-project, you can copy the `Transaction`, `CsvLoader`, and `Reports` types verbatim from the lecture notes; the point of *this* mini-project is the HTTP surface in front of them.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

An ASP.NET Core 9 service called `Ledger.Api` that exposes the Ledger domain over typed REST endpoints:

```
GET    /api/v1/transactions
GET    /api/v1/transactions/{id:int}
POST   /api/v1/transactions
PUT    /api/v1/transactions/{id:int}
DELETE /api/v1/transactions/{id:int}

GET    /api/v1/reports/summary
GET    /api/v1/reports/daily
GET    /api/v1/reports/categories
GET    /api/v1/reports/top?kind=debit&count=5

POST   /api/v1/admin/load-csv   (loads a CSV file into the in-memory store)
GET    /health
```

Persistence is an in-memory `ConcurrentDictionary<int, Transaction>` registered as a singleton. On startup the app optionally loads `samples/sample.csv` into the store (via `--seed-csv samples/sample.csv` on the command line, or an `appsettings.json` setting).

The service must:

- Generate an accurate OpenAPI 3.1 document at `/openapi/v1.json`.
- Mount Swagger UI at `/swagger` in `Development` only.
- Use `TypedResults<...>` everywhere — every endpoint has typed success and typed error responses.
- Validate `POST` and `PUT` bodies via data annotations + `.WithParameterValidation()`.
- Return RFC 7807 problem-details JSON on every error path.
- Use `ValidateScopes = true` and `ValidateOnBuild = true` from startup.
- Reuse the Week 1 `Reports` static class (or your closest equivalent) for all four report endpoints.
- Be coverable by an integration-test suite running `WebApplicationFactory<Program>` end-to-end.

By the end you'll have a public GitHub repo of ~400–500 lines of C# (excluding tests) that compiles clean, runs on Kestrel, returns OpenAPI 3.1 for every endpoint, and ships with at least 20 passing integration tests.

---

## Rules

- **You may** read Microsoft Learn, the ASP.NET Core source, lecture notes, and the source of the libraries listed below.
- **You may NOT** depend on any third-party NuGet package other than:
  - `MinimalApis.Extensions` (for `.WithParameterValidation()`).
  - `Swashbuckle.AspNetCore.SwaggerUI` (for the in-browser viewer; document generation is built-in).
  - `Microsoft.AspNetCore.Mvc.Testing` (in the test project, for `WebApplicationFactory<T>`).
  - `xUnit` and `Microsoft.NET.Test.Sdk`.
- **No EF Core. No Newtonsoft.Json. No FluentValidation. No AutoMapper.** Storage is `ConcurrentDictionary`. Serialization is `System.Text.Json`. Validation is data annotations. Mapping is hand-written.
- Target framework: `net9.0`. C# language version: the default for the SDK (`13.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.

---

## Acceptance criteria

- [ ] A new public GitHub repo named `c9-week-02-ledger-api-<yourhandle>`.
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
  │   │   ├── Transaction.cs
  │   │   ├── CsvLoader.cs
  │   │   ├── Reports.cs
  │   │   ├── ITransactionStore.cs
  │   │   └── InMemoryTransactionStore.cs
  │   └── Ledger.Api/
  │       ├── Ledger.Api.csproj
  │       ├── Program.cs
  │       ├── Ledger.Api.http
  │       ├── Endpoints/
  │       │   ├── TransactionEndpoints.cs
  │       │   ├── ReportEndpoints.cs
  │       │   └── AdminEndpoints.cs
  │       └── Contracts/
  │           ├── CreateTransactionRequest.cs
  │           ├── UpdateTransactionRequest.cs
  │           └── ReportDtos.cs
  └── tests/
      └── Ledger.Api.Tests/
          ├── Ledger.Api.Tests.csproj
          ├── TransactionEndpointsTests.cs
          ├── ReportEndpointsTests.cs
          └── AdminEndpointsTests.cs
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports **at least 20** passing tests covering every endpoint and every status code in the contract.
- [ ] `dotnet run --project src/Ledger.Api` starts Kestrel; the `Ledger.Api.http` file's smoke requests all return the expected status codes.
- [ ] `GET /api/v1/transactions` returns the seeded data when the app was started with `--seed-csv samples/sample.csv`.
- [ ] `POST /api/v1/transactions` with an empty `memo` returns 400 + RFC 7807 problem details with the `Memo` error.
- [ ] `GET /openapi/v1.json` returns a valid OpenAPI 3.1 document describing every endpoint, including both success and error response shapes.
- [ ] `Swashbuckle.AspNetCore.SwaggerUI` is mounted at `/swagger` in `Development` only — verify by visiting `http://localhost:5099/swagger`.
- [ ] `ITransactionStore` is registered as a **singleton** (the dictionary is shared app-wide). Endpoints are **scoped** classes. `IClock` is **singleton**. The DI graph passes `ValidateOnBuild`.
- [ ] **Zero `!` (null-forgiving) operators** in any source file.
- [ ] `dotnet publish src/Ledger.Api -c Release -o out` produces a runnable artifact you can execute as `dotnet out/Ledger.Api.dll --urls http://localhost:5099 --seed-csv samples/sample.csv`.
- [ ] `README.md` in the repo root includes:
  - One paragraph describing the project.
  - The exact commands to clone, build, test, and run.
  - The full `curl` smoke suite (mirror what's in `Ledger.Api.http`).
  - A "Things I learned" section with at least 3 specific items.

---

## Suggested order of operations

You'll find it easier if you build incrementally rather than trying to write the whole thing at once.

### Phase 1 — Skeleton (~45 min)

1. `mkdir Ledger.Api && cd Ledger.Api`.
2. `dotnet new sln -n Ledger.Api`.
3. `dotnet new gitignore` and `git init`.
4. Scaffold three projects:
   ```bash
   dotnet new classlib -n Ledger.Core      -o src/Ledger.Core
   dotnet new web      -n Ledger.Api       -o src/Ledger.Api
   dotnet new xunit    -n Ledger.Api.Tests -o tests/Ledger.Api.Tests
   ```
5. Wire references:
   ```bash
   dotnet sln add src/Ledger.Core/Ledger.Core.csproj
   dotnet sln add src/Ledger.Api/Ledger.Api.csproj
   dotnet sln add tests/Ledger.Api.Tests/Ledger.Api.Tests.csproj
   dotnet add src/Ledger.Api/Ledger.Api.csproj reference src/Ledger.Core/Ledger.Core.csproj
   dotnet add tests/Ledger.Api.Tests/Ledger.Api.Tests.csproj reference src/Ledger.Api/Ledger.Api.csproj
   dotnet add tests/Ledger.Api.Tests/Ledger.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
   dotnet add src/Ledger.Api package MinimalApis.Extensions
   dotnet add src/Ledger.Api package Swashbuckle.AspNetCore.SwaggerUI
   ```
6. Add `Directory.Build.props` at the root:
   ```xml
   <Project>
     <PropertyGroup>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <LangVersion>latest</LangVersion>
       <GenerateDocumentationFile>true</GenerateDocumentationFile>
       <NoWarn>CS1591</NoWarn>
     </PropertyGroup>
   </Project>
   ```
7. Commit: `Initial Ledger.Api solution skeleton`.

### Phase 2 — Domain + store (~60 min)

Bring the Week 1 types into `Ledger.Core`:

```csharp
// src/Ledger.Core/Transaction.cs
namespace Ledger.Core;

public enum TransactionKind { Credit, Debit, Zero }

public record Transaction(
    int Id, DateOnly Date, decimal Amount, string Memo, string Category)
{
    public TransactionKind Kind => Amount switch
    {
        > 0m => TransactionKind.Credit,
        < 0m => TransactionKind.Debit,
        _    => TransactionKind.Zero
    };
}
```

Reuse `CsvLoader` and `Reports` from Week 1 — copy them in. (`CsvLoader` should now produce `Transaction`s with an `Id`, assigned by the store on insert; refactor as needed.)

Add a store:

```csharp
// src/Ledger.Core/ITransactionStore.cs
namespace Ledger.Core;

public interface ITransactionStore
{
    Task<IReadOnlyList<Transaction>> ListAsync(CancellationToken ct);
    Task<Transaction?>               FindAsync(int id, CancellationToken ct);
    Task<Transaction>                AddAsync(DateOnly date, decimal amount, string memo, string category, CancellationToken ct);
    Task<Transaction?>               UpdateAsync(int id, DateOnly date, decimal amount, string memo, string category, CancellationToken ct);
    Task<bool>                       RemoveAsync(int id, CancellationToken ct);
    Task<int>                        LoadCsvAsync(string path, CancellationToken ct);
}

// src/Ledger.Core/InMemoryTransactionStore.cs
public sealed class InMemoryTransactionStore : ITransactionStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Transaction> _byId = new();
    private int _seed;
    // ... implement all six methods ...
}
```

Commit: `Ledger.Core: Transaction + store`.

### Phase 3 — Contracts (~30 min)

`src/Ledger.Api/Contracts/CreateTransactionRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.Contracts;

public sealed record CreateTransactionRequest(
    DateOnly Date,
    decimal Amount,
    [Required, StringLength(200, MinimumLength = 1)] string Memo,
    [Required, StringLength(80, MinimumLength = 1)]  string Category);

public sealed record UpdateTransactionRequest(
    DateOnly Date,
    decimal Amount,
    [Required, StringLength(200, MinimumLength = 1)] string Memo,
    [Required, StringLength(80, MinimumLength = 1)]  string Category);

public sealed record SummaryResponse(
    decimal Net,
    int DaysObserved,
    int Categories);

public sealed record DailyTotalDto(DateOnly Day, decimal Total);
public sealed record CategoryTotalDto(string Category, decimal Total);
```

DTOs are separate from `Transaction` deliberately — clients see DTOs, not domain types. We will lean on this separation more in Week 6 when EF entities arrive.

Commit: `API contracts (DTOs)`.

### Phase 4 — Endpoint classes (~90 min)

`src/Ledger.Api/Endpoints/TransactionEndpoints.cs`:

```csharp
using Ledger.Api.Contracts;
using Ledger.Core;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Ledger.Api.Endpoints;

public sealed class TransactionEndpoints(ITransactionStore store)
{
    public async Task<Ok<IReadOnlyList<Transaction>>> GetAll(CancellationToken ct) =>
        TypedResults.Ok(await store.ListAsync(ct));

    public async Task<Results<Ok<Transaction>, NotFound>> GetById(int id, CancellationToken ct)
    {
        var t = await store.FindAsync(id, ct);
        return t is null ? TypedResults.NotFound() : TypedResults.Ok(t);
    }

    public async Task<Created<Transaction>> Create(CreateTransactionRequest body, CancellationToken ct)
    {
        var created = await store.AddAsync(body.Date, body.Amount, body.Memo, body.Category, ct);
        return TypedResults.Created($"/api/v1/transactions/{created.Id}", created);
    }

    public async Task<Results<Ok<Transaction>, NotFound>> Update(int id, UpdateTransactionRequest body, CancellationToken ct)
    {
        var updated = await store.UpdateAsync(id, body.Date, body.Amount, body.Memo, body.Category, ct);
        return updated is null ? TypedResults.NotFound() : TypedResults.Ok(updated);
    }

    public async Task<Results<NoContent, NotFound>> Remove(int id, CancellationToken ct) =>
        await store.RemoveAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
```

Do the same for `ReportEndpoints` (four read-only endpoints) and `AdminEndpoints` (the CSV-load endpoint).

Commit: `Endpoint classes for transactions, reports, admin`.

### Phase 5 — `Program.cs` (~60 min)

Tie it all together. Aim for ~80 lines of `Program.cs`:

```csharp
using Ledger.Api.Contracts;
using Ledger.Api.Endpoints;
using Ledger.Core;
using MinimalApis.Extensions.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes  = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ITransactionStore, InMemoryTransactionStore>();
builder.Services.AddScoped<TransactionEndpoints>();
builder.Services.AddScoped<ReportEndpoints>();
builder.Services.AddScoped<AdminEndpoints>();

var app = builder.Build();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "Ledger.Api v1"));
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Diagnostics");

var tx = app.MapGroup("/api/v1/transactions").WithTags("Transactions");
tx.MapGet   ("/",        (TransactionEndpoints e, CancellationToken ct)         => e.GetAll(ct));
tx.MapGet   ("/{id:int}",   (TransactionEndpoints e, int id, CancellationToken ct) => e.GetById(id, ct));
tx.MapPost  ("/",        (TransactionEndpoints e, CreateTransactionRequest body, CancellationToken ct) => e.Create(body, ct))
  .WithParameterValidation();
tx.MapPut   ("/{id:int}",   (TransactionEndpoints e, int id, UpdateTransactionRequest body, CancellationToken ct) => e.Update(id, body, ct))
  .WithParameterValidation();
tx.MapDelete("/{id:int}",   (TransactionEndpoints e, int id, CancellationToken ct) => e.Remove(id, ct));

var reports = app.MapGroup("/api/v1/reports").WithTags("Reports");
reports.MapGet("/summary",    (ReportEndpoints e, CancellationToken ct) => e.Summary(ct));
reports.MapGet("/daily",      (ReportEndpoints e, CancellationToken ct) => e.Daily(ct));
reports.MapGet("/categories", (ReportEndpoints e, CancellationToken ct) => e.Categories(ct));
reports.MapGet("/top",        (ReportEndpoints e, string kind, int count, CancellationToken ct) => e.Top(kind, count, ct));

var admin = app.MapGroup("/api/v1/admin").WithTags("Admin");
admin.MapPost("/load-csv",    (AdminEndpoints e, LoadCsvRequest body, CancellationToken ct) => e.LoadCsv(body, ct))
     .WithParameterValidation();

// Optional startup seeding.
var seedCsv = builder.Configuration["SEED_CSV"]
              ?? args.SkipWhile(a => a != "--seed-csv").Skip(1).FirstOrDefault();
if (!string.IsNullOrWhiteSpace(seedCsv))
{
    var store = app.Services.GetRequiredService<ITransactionStore>();
    await store.LoadCsvAsync(seedCsv, default);
    app.Logger.LogInformation("Seeded transactions from {Path}.", seedCsv);
}

app.Run();

public partial class Program;
```

Commit: `Program.cs wiring everything together`.

### Phase 6 — Smoke file (~15 min)

`src/Ledger.Api/Ledger.Api.http`:

```http
@base = http://localhost:5099

### List all transactions
GET {{base}}/api/v1/transactions
Accept: application/json

### Get a transaction by id
GET {{base}}/api/v1/transactions/1
Accept: application/json

### Create a transaction
POST {{base}}/api/v1/transactions
Content-Type: application/json
Accept: application/json

{
  "date": "2026-05-13",
  "amount": 12.50,
  "memo": "Coffee and croissant",
  "category": "food"
}

### Create — should fail validation (empty memo)
POST {{base}}/api/v1/transactions
Content-Type: application/json
Accept: application/json

{
  "date": "2026-05-13",
  "amount": 12.50,
  "memo": "",
  "category": "food"
}

### Reports — summary
GET {{base}}/api/v1/reports/summary
Accept: application/json

### Reports — top debits
GET {{base}}/api/v1/reports/top?kind=debit&count=3
Accept: application/json

### Admin — load a CSV
POST {{base}}/api/v1/admin/load-csv
Content-Type: application/json
Accept: application/json

{
  "path": "samples/sample.csv"
}

### OpenAPI
GET {{base}}/openapi/v1.json
Accept: application/json

### Health
GET {{base}}/health
Accept: application/json
```

Commit: `Ledger.Api.http smoke suite`.

### Phase 7 — Integration tests (~75 min)

`tests/Ledger.Api.Tests/TransactionEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Ledger.Api.Contracts;
using Ledger.Core;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ledger.Api.Tests;

public class TransactionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http;

    public TransactionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_starts_empty_for_a_fresh_app()
    {
        var list = await _http.GetFromJsonAsync<List<Transaction>>("/api/v1/transactions");
        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Create_then_GetById_roundtrips()
    {
        var post = await _http.PostAsJsonAsync("/api/v1/transactions",
            new { date = "2026-05-13", amount = 10.00m, memo = "Coffee", category = "food" });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<Transaction>();
        Assert.NotNull(created);

        var get = await _http.GetFromJsonAsync<Transaction>($"/api/v1/transactions/{created.Id}");
        Assert.NotNull(get);
        Assert.Equal(created.Id, get.Id);
        Assert.Equal("Coffee", get.Memo);
    }

    [Theory]
    [InlineData("", "food")]     // empty memo
    [InlineData("Coffee", "")]   // empty category
    public async Task Create_with_blanks_returns_400(string memo, string category)
    {
        var res = await _http.PostAsJsonAsync("/api/v1/transactions",
            new { date = "2026-05-13", amount = 10m, memo, category });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ...etc — write 15+ more, covering every endpoint and status code.
}
```

Commit: `Integration tests via WebApplicationFactory`.

### Phase 8 — Polish + publish (~30 min)

- Run `dotnet format` and commit any changes.
- Run `dotnet publish src/Ledger.Api -c Release -o out`. Confirm `dotnet out/Ledger.Api.dll --urls http://localhost:5099 --seed-csv samples/sample.csv` boots.
- Push the repo to GitHub. Make `README.md` complete.
- Optional: a one-line `.github/workflows/ci.yml` that runs `dotnet build` + `dotnet test`. (Required from Week 4 onward, optional this week.)

---

## Example expected responses

For the sample CSV from Week 1:

```http
GET /api/v1/reports/summary

200 OK
{
  "net": 187.51,
  "daysObserved": 3,
  "categories": 3
}
```

```http
GET /api/v1/reports/daily

200 OK
[
  { "day": "2026-05-13", "total":  32.50 },
  { "day": "2026-05-14", "total":   4.99 },
  { "day": "2026-05-15", "total": 150.00 }
]
```

```http
POST /api/v1/transactions  Content-Type: application/json
{ "date": "2026-05-13", "amount": 10.00, "memo": "", "category": "food" }

400 Bad Request
Content-Type: application/problem+json
{
  "type":   "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title":  "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Memo": ["The Memo field is required."]
  }
}
```

---

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 15% | `dotnet build`, `dotnet test`, and the `.http` smoke all clean on a fresh clone |
| Endpoint correctness | 20% | All 10 endpoints hit every documented status code |
| Type fidelity | 15% | Every handler returns `TypedResults`; OpenAPI is precise |
| Validation + problem details | 10% | Bad inputs produce RFC 7807 JSON, not exceptions |
| DI hygiene | 10% | `ValidateScopes` and `ValidateOnBuild` on; scoped endpoint classes; singleton store; no captives |
| Tests | 20% | At least 20 tests via `WebApplicationFactory<Program>` covering happy/error paths |
| README + smoke file | 10% | A new developer can clone and exercise the API in under 5 minutes |

---

## Stretch (optional)

- Add a `--format json` option to the existing `Ledger.Cli` from Week 1 that emits the **same** report DTOs the API returns. Prove the two paths (CLI and API) share the contracts in `Ledger.Api.Contracts`.
- Add a `GET /api/v1/transactions?from=...&to=...&category=...` filter implemented as a parameter-object record with `[FromQuery]`. Cover with at least three integration tests.
- Add an `[FromHeader(Name = "X-Idempotency-Key")] string?` parameter to `POST /api/v1/transactions`. If the same key is sent twice, return the original `Created<Transaction>` with the original id instead of creating a duplicate.
- Add a `BackgroundCleaner` (`IHostedService`) that snapshots the in-memory store to `state.json` every minute. Use `IServiceScopeFactory`. (Background work is technically Week 8 material — try it now as a stretch.)
- Add an `appsettings.Production.json` that wires a real port and a `--seed-csv` value. Publish, run, and confirm Production mode does **not** mount Swagger UI.

---

## What this prepares you for

- **Week 3** introduces async/await. Your `CsvLoader.Load` becomes `CsvLoader.LoadAsync` with `IAsyncEnumerable<Transaction>` and a `CancellationToken`. The streaming endpoint that consumes it is your week-3 mini-project's centerpiece.
- **Week 4** introduces DI deeply. The `ITransactionStore` and endpoint classes you wired here become a reusable library pattern.
- **Week 5** revisits Minimal APIs (and adds MVC controllers) on a new domain — Sharp Notes — with the same five-endpoint pattern.
- **Week 6** replaces `InMemoryTransactionStore` with an `EfTransactionStore` backed by PostgreSQL. The `ITransactionStore` interface and the endpoint classes do not change. That is the entire reason we hide persistence behind an interface from day one.

---

## Resources

- *Minimal APIs overview*: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview>
- *Parameter binding*: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding>
- *`TypedResults` reference*: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.typedresults>
- *Integration tests with `WebApplicationFactory<T>`*: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>
- *Problem details (RFC 7807) in ASP.NET Core*: <https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors>
- *Dependency injection in ASP.NET Core*: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes the setup commands, the `curl` smoke suite, and the example output for the summary and top reports.
3. Make sure `dotnet build`, `dotnet test`, and at least one `dotnet run --project src/Ledger.Api -- --seed-csv samples/sample.csv` invocation are green on a freshly cloned copy.
4. Post the repo URL in your cohort tracker. You shipped a real REST service; show it.
