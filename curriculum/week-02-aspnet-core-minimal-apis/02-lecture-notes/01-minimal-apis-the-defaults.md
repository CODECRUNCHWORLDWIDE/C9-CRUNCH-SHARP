# Lecture 1 — Minimal APIs: The Defaults

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can scaffold a `dotnet new web` project on .NET 9, map five typed endpoints, bind parameters from every standard source, return strongly-typed results, generate an OpenAPI 3.1 document, and explain every line of `Program.cs` without referring to a tutorial.

If you only remember one thing from this lecture, remember this:

> **A Minimal API is not a stripped-down controller. It is a different programming model.** MVC routes a request to a method on a class; a Minimal API routes a request to a *delegate*. The framework reads that delegate's parameter list and binds each parameter from the request — route values, query string, body, headers, the DI container — using the same conventions everywhere. Master the binding rules and you have mastered most of the framework.

---

## 1. The two programming models

ASP.NET Core in 2026 ships **two** routing models in the same framework:

| Model | Introduced | Surface | Best for |
|-------|------------|---------|----------|
| **MVC controllers** | ASP.NET MVC, 2009 | `[ApiController] public class FooController : ControllerBase { [HttpGet("...")] public ... }` | Large APIs, complex content negotiation, filter-heavy endpoints |
| **Razor Pages** | ASP.NET Core 2.0, 2017 | `Foo.cshtml` + `Foo.cshtml.cs` | Server-rendered pages with one URL per page |
| **Minimal APIs** | ASP.NET Core 6, 2021 | `app.MapGet("/foo", () => ...)` | Small-to-medium HTTP APIs, function-first style |

The three coexist. You can have a single ASP.NET Core project that mixes Minimal API endpoints, MVC controllers, and Razor Pages — they all run on the same Kestrel server, share the same DI container, and emit the same OpenAPI document. The choice between them is not technical; it is stylistic.

In C9 we use **Minimal APIs as the default** for three reasons:

1. **They are smaller.** A "Hello, World" REST endpoint is one line in Minimal API form and a six-line class in MVC form. For learning, smaller wins.
2. **They map cleanly to functions.** A function takes inputs and returns an output. A Minimal API handler is a delegate that takes inputs (parameters) and returns an output (`IResult`). The abstraction matches the mental model.
3. **The .NET team writes new framework features against the Minimal API surface first.** Endpoint filters, output caching, native rate limiting, `TypedResults` — all designed Minimal-first. MVC then gets the same feature, usually a release later.

We will cover MVC controllers in Week 5 when you have enough Minimal API experience to compare them honestly. Until then: Minimal.

---

## 2. The smallest possible ASP.NET Core 9 app

Scaffold a fresh project:

```bash
mkdir HelloApi && cd HelloApi
dotnet new web -n HelloApi -o src/HelloApi
cd src/HelloApi
```

Open `Program.cs`. In .NET 9 it is exactly four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
```

Run it:

```bash
dotnet run
```

You should see something close to:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5099
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

Hit it:

```bash
curl http://localhost:5099/
# Hello World!
```

That is a complete ASP.NET Core 9 service. Four lines of source. No `Startup.cs`. No `IConfiguration` ceremony. No `IHostBuilder` chain. The `WebApplicationBuilder` / `WebApplication` pair replaced all of that in ASP.NET Core 6, and the templates have been getting smaller ever since.

Read those four lines carefully. They are the entire shape of every ASP.NET Core app you will write this year:

1. **`WebApplication.CreateBuilder(args)`** — read configuration (env vars, `appsettings.json`, command-line args), set up logging, set up DI. Returns the builder.
2. **`builder.Build()`** — freeze the configuration, freeze the service collection, instantiate the root `IServiceProvider`, and return the `WebApplication`.
3. **`app.MapGet(...)`** — register a route handler. (We will spend the rest of this lecture on this line.)
4. **`app.Run()`** — start Kestrel listening on the configured ports and block until the host is told to stop.

That ordering matters. **Everything between `CreateBuilder` and `Build` configures the application; everything between `Build` and `Run` defines its behavior.** Adding services after `Build()` is a runtime error. Adding routes before `Build()` is a compile error. The shape of the file enforces the shape of the lifecycle.

---

## 3. The `Map*` family

ASP.NET Core 9 ships these route-handler verbs out of the box:

```csharp
app.MapGet   ("/path", handler);   // HTTP GET
app.MapPost  ("/path", handler);   // HTTP POST
app.MapPut   ("/path", handler);   // HTTP PUT
app.MapPatch ("/path", handler);   // HTTP PATCH
app.MapDelete("/path", handler);   // HTTP DELETE
app.Map      ("/path", handler);   // any method (rarely the right call)
```

Each one returns an `IEndpointConventionBuilder` — a fluent value you can chain metadata onto:

```csharp
app.MapGet("/todos/{id}", (int id) => /* ... */)
   .WithName("GetTodoById")
   .WithSummary("Look up a single todo by id.")
   .WithTags("Todos")
   .Produces<Todo>(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status404NotFound);
```

The `WithName`, `WithSummary`, `WithTags`, and `Produces` calls are metadata: they end up in the OpenAPI document and in the routing table. They have **no** effect on runtime behavior. The runtime behavior is entirely in the handler delegate.

### Route groups

For an API with five endpoints on a common prefix, you do **not** type `/api/v1/todos` five times. You group:

```csharp
var todos = app.MapGroup("/api/v1/todos")
               .WithTags("Todos");

todos.MapGet   ("/",      GetAll);
todos.MapGet   ("/{id}",  GetById);
todos.MapPost  ("/",      Create);
todos.MapPut   ("/{id}",  Update);
todos.MapDelete("/{id}",  Remove);
```

The group's prefix and metadata are inherited by every endpoint inside it. You can nest groups arbitrarily (`/api`, `/api/v1`, `/api/v1/todos`). In C9 the default is one group per resource.

### Inline lambdas vs. named handlers

Inline lambdas are fine for tiny endpoints:

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
```

For anything non-trivial, extract a named static method or a method group:

```csharp
todos.MapGet("/{id}", GetTodoById);

static async Task<Results<Ok<Todo>, NotFound>> GetTodoById(int id, ITodoStore store, CancellationToken ct)
{
    var todo = await store.FindAsync(id, ct);
    return todo is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(todo);
}
```

Named methods are easier to test, easier to read, and produce better OpenAPI metadata because the compiler can see the full parameter list. The C9 convention is: **inline lambda only if it fits on one line and has zero parameters or one parameter you'd recognize in your sleep.** Otherwise, extract.

---

## 4. Parameter binding — the heart of the framework

Given a handler signature like:

```csharp
todos.MapGet("/{id}", async (int id, [FromQuery] bool includeArchived, ITodoStore store, CancellationToken ct) => /* ... */);
```

the framework has to figure out, for each parameter, *where* to read its value from. The rules are deterministic, and once you know them you will reach for the `[From*]` attributes only occasionally.

### The default binding order

For each parameter, the framework asks in this order:

1. **Is it a special framework type?** (`HttpContext`, `HttpRequest`, `HttpResponse`, `ClaimsPrincipal`, `CancellationToken`, `IFormFile`, `IFormCollection`, `Stream`, `PipeReader`.) If yes, bind from the request directly.
2. **Is its name in the route template?** (e.g. `int id` and the route is `/todos/{id}`.) If yes, bind from the route values.
3. **Is the type a simple, parseable type?** (`int`, `string`, `Guid`, `DateOnly`, `bool`, anything with a `TryParse` static, anything with `IParsable<T>` implemented.) If yes, bind from the query string.
4. **Is the parameter type registered in the DI container?** If yes, bind from `IServiceProvider`.
5. **Otherwise**, assume it is a complex type and bind from the request **body** as JSON.

That is the default. You almost never need to override it; you just need to be aware of it.

### A worked example

```csharp
todos.MapPost("/{id}/comments",
    async (
        int id,                                  // route — name matches "{id}"
        [FromQuery] string? language,            // explicit query
        [FromHeader(Name = "X-Tenant-Id")] string tenant, // header
        CreateCommentRequest body,               // body — complex type, no route/services match
        ITodoStore store,                        // DI — store is registered
        CancellationToken ct) =>                 // framework type
{
    /* ... */
});
```

This handler takes five distinct binding sources in one call:

| Parameter | Source | Why |
|-----------|--------|-----|
| `id` | Route | The parameter name is in `{id}`. |
| `language` | Query | `[FromQuery]` is explicit; would default to query anyway for a `string?`. |
| `tenant` | Header | `[FromHeader]` is **required** for headers — they are never the default. |
| `body` | Body | Complex type, not registered in DI, not in route. |
| `store` | DI | Registered service. |
| `ct` | Framework | `CancellationToken` is always the request's cancellation token. |

### The `[From*]` attributes

Five attributes you should recognize on sight:

| Attribute | Source |
|-----------|--------|
| `[FromRoute]` | Route value. Usually inferred from the route template, occasionally needed to override the name. |
| `[FromQuery]` | Query string. Default for simple types; redundant in many cases but harmless. |
| `[FromBody]` | Request body. Default for complex types; redundant. Useful when you want a *simple* type from the body. |
| `[FromHeader]` | Header. **Never** inferred; always required for header binding. |
| `[FromServices]` | DI container. Usually inferred; you only need it for ambiguous cases. |

Plus, since .NET 8:

```csharp
public record Pagination([FromQuery] int Page = 1, [FromQuery] int Size = 20);

todos.MapGet("/", (Pagination paging, ITodoStore store) => /* ... */);
```

That `Pagination` record is bound from the query string **as a unit** because its constructor parameters all carry `[FromQuery]`. This is the "parameter object" pattern; use it when an endpoint has four or more query parameters that belong together.

### Custom binding — `BindAsync` and `TryParse`

Two escape hatches for binding things the framework does not natively understand:

```csharp
// 1) Make the type IParsable<T> — works for query/route binding.
public readonly record struct TenantId(string Value) : IParsable<TenantId>
{
    public static TenantId Parse(string s, IFormatProvider? p) => new(s);
    public static bool TryParse(string? s, IFormatProvider? p, out TenantId result)
    {
        result = s is null ? default : new(s);
        return s is not null;
    }
}

// 2) Implement static BindAsync — works for complex binding from HttpContext.
public sealed record AcceptHeader(string Value)
{
    public static ValueTask<AcceptHeader?> BindAsync(HttpContext ctx, ParameterInfo _)
        => ValueTask.FromResult<AcceptHeader?>(new(ctx.Request.Headers.Accept.ToString()));
}
```

You will rarely need either in this course; mentioning them now so you recognize the pattern when you meet it in `dotnet/aspnetcore` source.

---

## 5. Return types — `TypedResults` is the default

A handler can return:

| Return type | Meaning |
|-------------|---------|
| `string` | 200 OK with `text/plain` body. |
| `object` (anything else) | 200 OK with the object serialized as JSON. |
| `IResult` | The handler decides the status, headers, and body. |
| `Task<...>` / `ValueTask<...>` | Async variants of any of the above. |
| `Results<TResult1, TResult2, ...>` | A typed union of possible results; the OpenAPI generator sees all of them. |

The C9 default is **`TypedResults` returning `Results<...>`**. Here is why.

### The wrong way (and why)

Compare:

```csharp
// 1. Untyped — works, but the OpenAPI document has no idea what comes back.
todos.MapGet("/{id}", async (int id, ITodoStore store) =>
{
    var todo = await store.FindAsync(id, default);
    if (todo is null) return Results.NotFound();
    return Results.Ok(todo);
});
```

That endpoint works. It returns 200 with the todo, 404 without. But its declared return type is `object?`, so the OpenAPI generator infers `Produces: application/json` with an *unknown* schema. The compiler can't help you because `Results.Ok(todo)` and `Results.NotFound()` both return the same `IResult`.

### The right way

```csharp
todos.MapGet("/{id}", GetTodoById);

static async Task<Results<Ok<Todo>, NotFound>> GetTodoById(int id, ITodoStore store, CancellationToken ct)
{
    var todo = await store.FindAsync(id, ct);
    return todo is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(todo);
}
```

Now the return type is `Results<Ok<Todo>, NotFound>`. The compiler enforces that *every* return path is one of those two cases. The OpenAPI generator produces:

```yaml
responses:
  '200':
    description: OK
    content:
      application/json:
        schema:
          $ref: '#/components/schemas/Todo'
  '404':
    description: Not Found
```

…automatically, from the type alone. **No `Produces` attribute. No `[ProducesResponseType]`. The type is the documentation.**

### The full `TypedResults` palette

The ones you'll use weekly:

```csharp
TypedResults.Ok();                         // 200, no body
TypedResults.Ok(value);                    // 200, JSON body
TypedResults.Created("/path", value);      // 201, with Location header
TypedResults.NoContent();                  // 204
TypedResults.BadRequest(value);            // 400
TypedResults.Unauthorized();               // 401
TypedResults.Forbid();                     // 403
TypedResults.NotFound();                   // 404
TypedResults.Conflict(value);              // 409
TypedResults.UnprocessableEntity(value);   // 422
TypedResults.Problem(detail, statusCode);  // RFC 7807 problem details
TypedResults.ValidationProblem(errors);    // 400 problem details, with field errors
TypedResults.Stream(stream, "video/mp4");  // streaming response
TypedResults.Redirect("/elsewhere");       // 302
```

Use these. Reach for the lowercase `Results.*` only when you genuinely have a runtime-determined status code that `TypedResults` can't express (rare).

---

## 6. OpenAPI generation — built-in in .NET 9

Before .NET 9 the default was the third-party **Swashbuckle** package. Starting with .NET 9, ASP.NET Core ships its own OpenAPI generator: `Microsoft.AspNetCore.OpenApi`. It produces OpenAPI 3.1 documents (Swashbuckle is still on 3.0).

Wire it up in two lines:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();          // <-- register the generator

var app = builder.Build();

app.MapOpenApi();                        // <-- expose /openapi/v1.json

app.MapGet("/", () => "Hello!");
app.Run();
```

Hit `http://localhost:5099/openapi/v1.json` and you get a complete OpenAPI 3.1 document for every mapped endpoint. No further configuration needed.

### Adding Swagger UI

The new OpenAPI package only *generates* the document — it does not render a UI. If you want the in-browser explorer, add `Swashbuckle.AspNetCore.SwaggerUI`:

```bash
dotnet add package Swashbuckle.AspNetCore.SwaggerUI
```

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "HelloApi v1");
    });
}
```

Now `http://localhost:5099/swagger` opens the familiar Swagger UI, pointed at the document the new generator emits. Two packages, one responsibility each. We will keep this pattern through Week 8.

### What the generator picks up automatically

- Route templates and HTTP methods (from `MapGet` etc.).
- Parameter binding sources (route, query, header, body) from the binding inference rules above.
- Response shapes, from your `Results<...>` return type.
- Tags, summaries, and descriptions from `WithTags`, `WithSummary`, `WithDescription`.
- XML documentation comments **if** you turn on `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in the `.csproj`. We do this from Week 2 onward.

---

## 7. Validation with data annotations

ASP.NET Core has shipped declarative validation since 2009 via `System.ComponentModel.DataAnnotations`. It is unglamorous and it works.

```csharp
public sealed record CreateTodoRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title,
    [StringLength(2000)] string? Notes,
    DateOnly? DueDate);
```

The attributes you'll meet weekly:

| Attribute | What it checks |
|-----------|----------------|
| `[Required]` | The property must be present and non-null. |
| `[StringLength(max, MinimumLength = min)]` | A string's length is in `[min, max]`. |
| `[MinLength(n)]` / `[MaxLength(n)]` | Lower / upper bound only. |
| `[Range(min, max)]` | A numeric value is in `[min, max]`. |
| `[RegularExpression(@"...")]` | A string matches a regex. |
| `[EmailAddress]` | The string is a syntactically valid email. |
| `[Url]` | The string is a syntactically valid URL. |
| `[Compare(nameof(Other))]` | This property equals another property on the same type. |

### Wiring validation into a Minimal API endpoint

Unlike MVC controllers, Minimal APIs do **not** run data-annotation validation automatically. You either call it yourself or add an endpoint filter that does. The cleanest option in .NET 9 is the community filter from `MinimalApis.Extensions`:

```bash
dotnet add package MinimalApis.Extensions
```

```csharp
todos.MapPost("/", CreateTodo)
     .WithParameterValidation();

static async Task<Results<Created<Todo>, ValidationProblem>> CreateTodo(
    CreateTodoRequest body, ITodoStore store, CancellationToken ct)
{
    var created = await store.AddAsync(body, ct);
    return TypedResults.Created($"/api/v1/todos/{created.Id}", created);
}
```

`WithParameterValidation()` reads the `[Required]`, `[StringLength]`, etc. attributes on every parameter and, if any fail, short-circuits the handler with a 400 problem-details response containing per-field errors. Without it, your handler runs on garbage input.

If you prefer not to take the dependency, write the filter yourself in 20 lines:

```csharp
static EndpointFilterDelegate ValidationFilter(EndpointFilterFactoryContext _, EndpointFilterDelegate next) =>
    async ctx =>
    {
        foreach (var arg in ctx.Arguments)
        {
            if (arg is null) continue;
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(arg, new ValidationContext(arg), results, validateAllProperties: true))
            {
                var errors = results
                    .SelectMany(r => r.MemberNames.Select(m => (Member: m, r.ErrorMessage)))
                    .GroupBy(x => x.Member)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid").ToArray());
                return TypedResults.ValidationProblem(errors);
            }
        }
        return await next(ctx);
    };
```

That `ValidationFilter` can be attached to any endpoint with `.AddEndpointFilterFactory(ValidationFilter)`. We will use the off-the-shelf one in C9 but you should be able to read this when you see it.

### FluentValidation as the alternative

For complex rules (cross-field comparisons, conditional requirements, async checks against a database), **FluentValidation** is the standard third-party choice:

```csharp
public sealed class CreateTodoRequestValidator : AbstractValidator<CreateTodoRequest>
{
    public CreateTodoRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.DueDate).GreaterThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.DueDate is not null);
    }
}
```

Mention it now; cover it deeper in Week 5.

---

## 8. Problem details (RFC 7807)

When a Minimal API returns an error, the JSON shape should follow **RFC 7807 — Problem Details for HTTP APIs**. Every modern .NET API does this; clients can rely on it.

```json
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Title": ["The Title field is required."]
  }
}
```

You produce this with two helpers:

```csharp
return TypedResults.Problem(
    detail: "User must be at least 18 years old.",
    statusCode: StatusCodes.Status422UnprocessableEntity,
    title: "Age requirement not met");

return TypedResults.ValidationProblem(new Dictionary<string, string[]>
{
    ["Email"] = ["Already in use."]
});
```

And you opt the global error handler in to problem details with one line in `Program.cs`:

```csharp
builder.Services.AddProblemDetails();   // any unhandled exception → problem-details JSON
```

With that line, a thrown `KeyNotFoundException` becomes a clean 500 problem-details response instead of a stack trace leaking to the client. We turn it on in every project from Week 2 onward.

---

## 9. A complete five-endpoint example

Here is a 60-line `Program.cs` that exercises every idea above. It is the warm-up for this week's challenge and mini-project.

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using MinimalApis.Extensions.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ITodoStore, InMemoryTodoStore>();

var app = builder.Build();

app.MapOpenApi();

var todos = app.MapGroup("/api/v1/todos").WithTags("Todos");

todos.MapGet   ("/",      GetAll);
todos.MapGet   ("/{id}",  GetById).WithName("GetTodoById");
todos.MapPost  ("/",      Create).WithParameterValidation();
todos.MapPut   ("/{id}",  Update).WithParameterValidation();
todos.MapDelete("/{id}",  Remove);

app.Run();

static async Task<Ok<IReadOnlyList<Todo>>> GetAll(ITodoStore store, CancellationToken ct) =>
    TypedResults.Ok(await store.ListAsync(ct));

static async Task<Results<Ok<Todo>, NotFound>> GetById(int id, ITodoStore store, CancellationToken ct)
{
    var todo = await store.FindAsync(id, ct);
    return todo is null ? TypedResults.NotFound() : TypedResults.Ok(todo);
}

static async Task<Created<Todo>> Create(CreateTodoRequest body, ITodoStore store, CancellationToken ct)
{
    var created = await store.AddAsync(body, ct);
    return TypedResults.Created($"/api/v1/todos/{created.Id}", created);
}

static async Task<Results<Ok<Todo>, NotFound>> Update(int id, UpdateTodoRequest body, ITodoStore store, CancellationToken ct)
{
    var updated = await store.UpdateAsync(id, body, ct);
    return updated is null ? TypedResults.NotFound() : TypedResults.Ok(updated);
}

static async Task<Results<NoContent, NotFound>> Remove(int id, ITodoStore store, CancellationToken ct) =>
    await store.RemoveAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

public sealed record Todo(int Id, string Title, string? Notes, DateOnly? DueDate, bool Done);

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

Run it:

```bash
dotnet run
curl -s http://localhost:5099/api/v1/todos | jq .
```

Read it slowly. There are five endpoints in twenty-two lines (excluding the records and `using`s). Every endpoint:

- Uses `TypedResults` so the OpenAPI document is precise.
- Accepts a `CancellationToken` as its last parameter.
- Takes its store via parameter injection (we will revisit DI in Lecture 2).
- Either succeeds (200/201/204) or fails (404) with no third option.

`POST` and `PUT` both run data-annotation validation via `.WithParameterValidation()`; invalid inputs return a 400 problem-details JSON automatically. The `Create` endpoint returns 201 with a `Location` header pointing at the newly created resource.

This is the C9 baseline. The mini-project this week extends this exact pattern with persistence and a real domain.

---

## 10. Recap — the build-succeeded checklist

After this lecture you should be able to:

- State the three programming models (Minimal APIs, MVC controllers, Razor Pages) and pick between them with reasons.
- Scaffold a Minimal API with `dotnet new web` and explain every line of `Program.cs`.
- Map five endpoints with `MapGroup` and the `Map*` verbs.
- Predict the binding source of every parameter in a handler without running the code.
- Return `Results<TResult1, ..., TResultN>` from a handler and explain how the OpenAPI generator uses that information.
- Wire `AddOpenApi`, `MapOpenApi`, and Swagger UI in three lines.
- Attach data-annotation validation to a `POST` endpoint with `.WithParameterValidation()`.
- Explain RFC 7807 in one sentence and use `TypedResults.Problem` / `TypedResults.ValidationProblem`.

Lecture 2 turns to **dependency injection** — the other half of every ASP.NET Core endpoint you will ever write.

---

## References

- *Minimal APIs overview* — Microsoft Learn: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview>
- *Parameter binding* — Microsoft Learn: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding>
- *Responses in Minimal APIs* — Microsoft Learn: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses>
- *`TypedResults` reference*: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.typedresults>
- *Routing in ASP.NET Core*: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing>
- *`Microsoft.AspNetCore.OpenApi` overview*: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview>
- *Problem details (RFC 7807) in ASP.NET Core*: <https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors>
- *Validation with data annotations*: <https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation>
- *RFC 7807 — Problem Details for HTTP APIs*: <https://www.rfc-editor.org/rfc/rfc7807>
- *David Fowler — Minimal API design notes*: <https://github.com/davidfowl/CommunityStandUpMinimalAPI>
- *Andrew Lock — Exploring ASP.NET Core*: <https://andrewlock.net/series/exploring-asp-net-core/>
