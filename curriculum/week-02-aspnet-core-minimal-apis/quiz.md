# Week 2 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 3. Answer key at the bottom — don't peek.

---

**Q1.** Which statement best describes the relationship between Minimal APIs and MVC controllers in ASP.NET Core 9?

- A) Minimal APIs are MVC controllers without filters; the two compile to the same thing.
- B) They are two distinct routing models that can coexist in the same project; Minimal APIs route requests to delegates, MVC routes them to methods on classes.
- C) Minimal APIs replaced MVC in .NET 6; MVC is no longer supported.
- D) MVC is for HTTP and Minimal APIs are for gRPC; they target different protocols.

---

**Q2.** Given:

```csharp
app.MapGet("/orders/{id}", (int id, string? status, IOrderRepo repo, CancellationToken ct) => /* ... */);
```

What is the default binding source of each parameter?

- A) All four bind from the query string.
- B) `id` from route, `status` from query, `repo` from services, `ct` from the framework.
- C) `id` from route, `status` from headers, `repo` from the body, `ct` from the query.
- D) Minimal APIs cannot infer binding sources; every parameter requires a `[From*]` attribute.

---

**Q3.** Which handler signature gives the OpenAPI generator the most precise response metadata?

- A) `static IResult Get(int id, IStore s) { ... }`
- B) `static object Get(int id, IStore s) { ... }`
- C) `static async Task<Results<Ok<Order>, NotFound>> Get(int id, IStore s, CancellationToken ct) { ... }`
- D) `static dynamic Get(int id, IStore s) { ... }`

---

**Q4.** You register a service like this:

```csharp
builder.Services.AddScoped<ITodoRepository, EfTodoRepository>();
```

How many `EfTodoRepository` instances does the container construct across two consecutive HTTP requests, each of which resolves `ITodoRepository` twice?

- A) 1 — singleton-like reuse across requests.
- B) 2 — one per request, reused within the request.
- C) 4 — fresh every resolution.
- D) 0 — scoped services are lazy and only construct on first iteration.

---

**Q5.** Which of the following is a **captive dependency** bug?

- A) A singleton service whose constructor takes another singleton service.
- B) A scoped service whose constructor takes another scoped service.
- C) A singleton service whose constructor takes a scoped service.
- D) A transient service whose constructor takes a scoped service.

---

**Q6.** What does this endpoint return when the request body is `{"title":""}`?

```csharp
todos.MapPost("/", (CreateTodoRequest body, ITodoStore store, CancellationToken ct) =>
    TypedResults.Created($"/api/v1/todos/{1}", new Todo(1, body.Title, null, null, false)))
.WithParameterValidation();

public sealed record CreateTodoRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title);
```

- A) 201 Created with a `Todo` whose Title is `""`.
- B) 400 Bad Request with an RFC 7807 problem-details body containing the `Title` error.
- C) 422 Unprocessable Entity with the same body.
- D) The handler throws a `ValidationException` at runtime.

---

**Q7.** Why does C9 recommend turning on `ValidateOnBuild = true` in `Program.cs`?

- A) It speeds up DI resolution at request time.
- B) It eagerly validates every registered service at `builder.Build()` time, so captive dependencies and missing registrations crash the app at startup instead of at first request.
- C) It enables JIT compilation of all endpoint handlers ahead of time.
- D) It is required for `TypedResults` to work.

---

**Q8.** Which return type from a Minimal API handler produces a 201 status code with a `Location` header?

- A) `Ok<T>` — `TypedResults.Ok(value)`
- B) `Created<T>` — `TypedResults.Created("/path", value)`
- C) `NoContent` — `TypedResults.NoContent()`
- D) `Accepted<T>` — `TypedResults.Accepted("/path", value)`

---

**Q9.** Inside an `IHostedService` background worker that runs every minute, you need to use a scoped `ITodoStore`. Which is the correct way to resolve it?

- A) Inject `ITodoStore` directly into the worker's constructor.
- B) Inject `IServiceProvider` and call `GetRequiredService<ITodoStore>()` once at startup.
- C) Inject `IServiceScopeFactory`, create a scope per cycle with `CreateAsyncScope`, resolve `ITodoStore` from the scope's `ServiceProvider`, and dispose the scope at the end of the cycle.
- D) Register `ITodoStore` as a singleton in `Program.cs` so it can be injected directly.

---

**Q10.** In ASP.NET Core 9, which package generates the OpenAPI document by default, and which one renders Swagger UI?

- A) `Swashbuckle.AspNetCore` generates the document and renders the UI; one package does both.
- B) `Microsoft.AspNetCore.OpenApi` generates the document; `Swashbuckle.AspNetCore.SwaggerUI` (or NSwag) renders the UI.
- C) `Newtonsoft.OpenApi` generates the document; ASP.NET Core renders the UI built-in.
- D) The framework generates and renders both built-in; no extra package is needed for either.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **B** — Minimal APIs and MVC are two routing models that share Kestrel, the DI container, and the OpenAPI generator. The difference is whether the route target is a delegate (Minimal) or a method on a class (MVC). They coexist freely in the same project.
2. **B** — The framework matches parameter names against route values first, then falls back to query for simple types, then to services for registered types. `CancellationToken` is always the request's cancellation token. No `[From*]` attribute is needed for any of these — the inference is reliable.
3. **C** — `Results<Ok<Order>, NotFound>` is a typed union of possible results. The compiler enforces that every return path is one of the listed cases, and the OpenAPI generator records every case with its schema. A, B, and D all collapse to `application/json` of unknown shape.
4. **B** — Scoped means "one instance per `IServiceScope`." In ASP.NET Core, the framework creates one scope per HTTP request. Within a single request, the same instance is returned every time `ITodoRepository` is resolved. Two requests = two scopes = two instances.
5. **C** — A captive dependency is a longer-lived service holding a reference to a shorter-lived one. Singleton-holding-scoped is the textbook case: the scoped service ends up living for the lifetime of the singleton (forever), defeating its purpose and often leaking the underlying resource (e.g. a `DbContext`).
6. **B** — `.WithParameterValidation()` from `MinimalApis.Extensions` reads the data-annotation attributes, runs validation before the handler, and short-circuits with `TypedResults.ValidationProblem` (400 + problem-details JSON) on failure. The handler never runs.
7. **B** — `ValidateOnBuild` validates every registration eagerly at startup. The cost is a few extra milliseconds at boot; the benefit is that registration mistakes (missing services, captive dependencies, circular deps) fail at startup instead of on the first request. C9 turns it on in every environment from Week 2 onward.
8. **B** — `TypedResults.Created("/path", value)` produces HTTP 201 Created with the `Location` header set to `"/path"` and `value` serialized as the body. It is the standard response for a successful resource creation in REST.
9. **C** — `IServiceScopeFactory` is the pattern for resolving scoped services from long-lived components. Injecting `ITodoStore` directly into a singleton/hosted service is a captive dependency (the container will block this at startup with `ValidateScopes` on). Widening `ITodoStore` to singleton solves the symptom but breaks the lifetime model.
10. **B** — Starting with .NET 9, `Microsoft.AspNetCore.OpenApi` is the default OpenAPI 3.1 generator built into ASP.NET Core. Swashbuckle.AspNetCore.SwaggerUI is the in-browser viewer; it does not generate the document any more. The two libraries do separate jobs.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
