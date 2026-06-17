# Challenge 1 — The Five-Endpoint Todo API

**Time estimate:** ~2 hours.

## Problem statement

Build a complete typed CRUD API for a Todo domain using ASP.NET Core 9 Minimal APIs. Five endpoints, all typed with `TypedResults`, validated with data annotations, persisted via a DI-registered `ITodoStore`, documented with OpenAPI 3.1, and tested with xUnit and `WebApplicationFactory<T>`.

This is the "canonical Minimal API" the rest of the course returns to. Build it once, get it right, and every later mini-project starts from a familiar shape.

## The contract

```
GET    /api/v1/todos              → 200 Ok<IReadOnlyList<Todo>>
GET    /api/v1/todos/{id:int}     → 200 Ok<Todo>      | 404 NotFound
POST   /api/v1/todos              → 201 Created<Todo> | 400 ValidationProblem
PUT    /api/v1/todos/{id:int}     → 200 Ok<Todo>      | 404 NotFound | 400 ValidationProblem
DELETE /api/v1/todos/{id:int}     → 204 NoContent     | 404 NotFound
```

The `Todo` resource:

```csharp
public sealed record Todo(int Id, string Title, string? Notes, DateOnly? DueDate, bool Done);
```

The request DTOs:

```csharp
public sealed record CreateTodoRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Notes,
    DateOnly? DueDate);

public sealed record UpdateTodoRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Notes,
    DateOnly? DueDate,
    bool Done);
```

## Acceptance criteria

- [ ] A solution with one web project (`Todos.Api`) and one xUnit test project (`Todos.Api.Tests`).
- [ ] Solution layout matches the C9 standard:
  ```
  Todos/
  ├── Todos.sln
  ├── .gitignore
  ├── Directory.Build.props        (treats warnings as errors)
  ├── src/
  │   └── Todos.Api/
  │       ├── Todos.Api.csproj
  │       ├── Program.cs
  │       ├── Todos.Api.http       (smoke-request file)
  │       └── ITodoStore.cs        (interface + InMemoryTodoStore)
  └── tests/
      └── Todos.Api.Tests/
          ├── Todos.Api.Tests.csproj
          └── TodoApiTests.cs
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports at least **10 passing tests** covering every status code in the contract above.
- [ ] All five endpoints use `TypedResults` — no lowercase `Results.Ok(...)` anywhere.
- [ ] `POST` and `PUT` validate via `.WithParameterValidation()` and return RFC 7807 problem details on failure.
- [ ] `ITodoStore` is registered as a singleton with a `ConcurrentDictionary` backing.
- [ ] The OpenAPI document at `/openapi/v1.json` describes all five endpoints, with both success and error response shapes for every one.
- [ ] Swagger UI is mounted at `/swagger` in `Development` only.
- [ ] `ValidateScopes = true` and `ValidateOnBuild = true` are set in `Program.cs`.
- [ ] A `Todos.Api.http` file in the project includes one request per endpoint and at least one validation-failure example.
- [ ] A `README.md` in the repo root with one paragraph describing the API and the exact commands to clone, run, and test it.
- [ ] **Zero `!` (null-forgiving) operators** in the source.

## Suggested order of operations

### Phase 1 — Skeleton (~20 min)

```bash
mkdir Todos && cd Todos
dotnet new sln -n Todos
dotnet new gitignore
git init

dotnet new web    -n Todos.Api       -o src/Todos.Api
dotnet new xunit  -n Todos.Api.Tests -o tests/Todos.Api.Tests
dotnet sln add src/Todos.Api/Todos.Api.csproj
dotnet sln add tests/Todos.Api.Tests/Todos.Api.Tests.csproj
dotnet add tests/Todos.Api.Tests/Todos.Api.Tests.csproj reference src/Todos.Api/Todos.Api.csproj
dotnet add tests/Todos.Api.Tests/Todos.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add src/Todos.Api package MinimalApis.Extensions
dotnet add src/Todos.Api package Swashbuckle.AspNetCore.SwaggerUI
```

Add `Directory.Build.props` at the root:

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>CS1591</NoWarn> <!-- "missing XML doc" — turn on once docs are written -->
  </PropertyGroup>
</Project>
```

Commit: `Initial Todos solution`.

### Phase 2 — The domain and store (~25 min)

In `src/Todos.Api/ITodoStore.cs`:

```csharp
namespace Todos.Api;

public sealed record Todo(int Id, string Title, string? Notes, DateOnly? DueDate, bool Done);

public interface ITodoStore
{
    Task<IReadOnlyList<Todo>> ListAsync(CancellationToken ct);
    Task<Todo?>               FindAsync(int id, CancellationToken ct);
    Task<Todo>                AddAsync(string title, string? notes, DateOnly? dueDate, CancellationToken ct);
    Task<Todo?>               UpdateAsync(int id, string title, string? notes, DateOnly? dueDate, bool done, CancellationToken ct);
    Task<bool>                RemoveAsync(int id, CancellationToken ct);
}

public sealed class InMemoryTodoStore : ITodoStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Todo> _byId = new();
    private int _seed;

    public Task<IReadOnlyList<Todo>> ListAsync(CancellationToken _) =>
        Task.FromResult<IReadOnlyList<Todo>>(_byId.Values.OrderBy(t => t.Id).ToList());

    public Task<Todo?> FindAsync(int id, CancellationToken _) =>
        Task.FromResult(_byId.TryGetValue(id, out var t) ? t : null);

    public Task<Todo> AddAsync(string title, string? notes, DateOnly? dueDate, CancellationToken _)
    {
        var id = Interlocked.Increment(ref _seed);
        var todo = new Todo(id, title, notes, dueDate, Done: false);
        _byId[id] = todo;
        return Task.FromResult(todo);
    }

    public Task<Todo?> UpdateAsync(int id, string title, string? notes, DateOnly? dueDate, bool done, CancellationToken _)
    {
        if (!_byId.ContainsKey(id)) return Task.FromResult<Todo?>(null);
        var next = new Todo(id, title, notes, dueDate, done);
        _byId[id] = next;
        return Task.FromResult<Todo?>(next);
    }

    public Task<bool> RemoveAsync(int id, CancellationToken _) =>
        Task.FromResult(_byId.TryRemove(id, out _));
}
```

Commit: `Todo domain + in-memory store`.

### Phase 3 — `Program.cs` (~40 min)

The five endpoints, the registration, OpenAPI, Swagger UI, problem details, and scope validation — all in one file. Aim for ~80 lines of `Program.cs` plus the `ITodoStore.cs` from Phase 2. The shape:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using MinimalApis.Extensions.Filters;
using Todos.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes  = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ITodoStore, InMemoryTodoStore>();

var app = builder.Build();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "Todos v1"));
}

var todos = app.MapGroup("/api/v1/todos").WithTags("Todos");

todos.MapGet   ("/",       GetAll);
todos.MapGet   ("/{id:int}",  GetById).WithName("GetTodoById");
todos.MapPost  ("/",       Create).WithParameterValidation();
todos.MapPut   ("/{id:int}",  Update).WithParameterValidation();
todos.MapDelete("/{id:int}",  Remove);

app.Run();

static async Task<Ok<IReadOnlyList<Todo>>> GetAll(ITodoStore store, CancellationToken ct) => /* ... */;
// etc.

public sealed record CreateTodoRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Notes,
    DateOnly? DueDate);

public sealed record UpdateTodoRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Notes,
    DateOnly? DueDate,
    bool Done);

public partial class Program; // for WebApplicationFactory in tests
```

The trailing `public partial class Program;` is required so the test project's `WebApplicationFactory<Program>` can find an entry point. Top-level `Program.cs` generates an internal `Program` class by default; the partial promotes it to `public`.

Commit: `Five endpoints + validation + OpenAPI`.

### Phase 4 — `.http` smoke file (~10 min)

`src/Todos.Api/Todos.Api.http`:

```http
@base = http://localhost:5099

### List all todos
GET {{base}}/api/v1/todos
Accept: application/json

### Get a todo by id
GET {{base}}/api/v1/todos/1
Accept: application/json

### Create a todo
POST {{base}}/api/v1/todos
Content-Type: application/json
Accept: application/json

{
  "title": "Buy milk",
  "notes": "Whole milk, 2L",
  "dueDate": "2026-05-20"
}

### Create a todo — should fail validation (empty title)
POST {{base}}/api/v1/todos
Content-Type: application/json
Accept: application/json

{
  "title": "",
  "notes": null,
  "dueDate": null
}

### Update a todo
PUT {{base}}/api/v1/todos/1
Content-Type: application/json
Accept: application/json

{
  "title": "Buy milk",
  "notes": "2% milk after all",
  "dueDate": "2026-05-21",
  "done": true
}

### Delete a todo
DELETE {{base}}/api/v1/todos/1

### OpenAPI document
GET {{base}}/openapi/v1.json
Accept: application/json
```

Commit: `Todos.Api.http with smoke requests`.

### Phase 5 — Integration tests (~30 min)

In `tests/Todos.Api.Tests/TodoApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Todos.Api;

namespace Todos.Api.Tests;

public class TodoApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http;

    public TodoApiTests(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_starts_empty()
    {
        var todos = await _http.GetFromJsonAsync<List<Todo>>("/api/v1/todos");
        Assert.NotNull(todos);
        Assert.Empty(todos);
    }

    [Fact]
    public async Task Create_then_GetById_roundtrips()
    {
        var post = await _http.PostAsJsonAsync("/api/v1/todos", new { title = "Walk the dog" });
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<Todo>();
        Assert.NotNull(created);
        Assert.Equal("Walk the dog", created.Title);

        var get = await _http.GetFromJsonAsync<Todo>($"/api/v1/todos/{created.Id}");
        Assert.NotNull(get);
        Assert.Equal(created.Id, get.Id);
    }

    [Fact]
    public async Task GetById_unknown_id_returns_404()
    {
        var res = await _http.GetAsync("/api/v1/todos/99999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_with_empty_title_returns_400()
    {
        var res = await _http.PostAsJsonAsync("/api/v1/todos", new { title = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ...add five more covering Update happy/404, Delete happy/404, validation on PUT.
}
```

Run:

```bash
dotnet test
```

Commit: `Integration tests via WebApplicationFactory`.

### Phase 6 — Polish (~10 min)

- Run `dotnet format`.
- Confirm `dotnet build` is clean.
- Write `README.md` with the API description, the run/test commands, and the example requests from the `.http` file.
- Push the repo.

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 20% | `dotnet build`, `dotnet test`, and the `.http` smoke all clean on a fresh clone |
| Endpoint correctness | 25% | All five endpoints hit every status code in the contract |
| Type fidelity | 15% | Every handler returns `TypedResults`; the OpenAPI doc accurately reflects success and error shapes |
| Validation | 10% | `[Required]` and `[StringLength]` enforced, RFC 7807 body on failure |
| DI hygiene | 10% | `ValidateScopes` and `ValidateOnBuild` on; one singleton store; no captive deps |
| Tests | 15% | At least 10 tests via `WebApplicationFactory<Program>`, covering every status code |
| README quality | 5% | Someone unfamiliar can clone and run in under 5 minutes |

## Stretch

- Add a `GET /api/v1/todos?done=true&dueBefore=2026-06-01` filter implemented as a parameter-object record carrying `[FromQuery]`. Test both filter and no-filter.
- Add `[FromHeader(Name = "X-Idempotency-Key")] string?` to `POST` and short-circuit duplicates by returning the same `Created<Todo>` for the same key.
- Replace `InMemoryTodoStore` with a `JsonFileTodoStore` that persists to a `todos.json` file on disk. Keep the interface identical — only the registration changes.
- Add a `BackgroundCleaner` (`IHostedService`) that runs once a minute and removes todos older than 30 days. Use `IServiceScopeFactory`; do not widen any lifetime. (Hosted services are technically Week 8 material — try it now as a stretch.)

## Why this matters

The five-endpoint pattern is the literal template for every REST resource you will write through the rest of C9:

- **Week 5** — Sharp Notes API: same shape, with a Razor Pages admin form bolted on.
- **Week 6** — Sharp Notes Persistence: same shape, with EF Core swapped in for the in-memory store.
- **Week 7** — Sharp Notes Auth: same shape, with `[Authorize]` and a `RequireOwner` policy.
- **Week 13–15** — Capstone: same shape, but the resource is a `Lesson` or an `Exercise` or a `Submission`.

If you internalize "five typed endpoints + validation + a DI-registered store + integration tests" now, every later piece slots into a slot you have already practiced.
