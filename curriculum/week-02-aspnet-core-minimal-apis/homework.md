# Week 2 Homework

Six practice problems that revisit the week's topics. The full set should take about **6 hours**. Work in your Week 2 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — Reading the OpenAPI output

**Problem statement.** Start with the project you built in Exercise 1 (or scaffold a fresh `dotnet new web`). Add three endpoints with intentionally different return types:

1. `GET /a` returning `string` (plain text).
2. `GET /b` returning `Greeting` (a record — defaults to JSON).
3. `GET /c` returning `Results<Ok<Greeting>, NotFound>` via `TypedResults`.

Run `dotnet run`, fetch `/openapi/v1.json`, and inspect the three response entries. Write a short note in `notes/openapi-comparison.md` that, for each of the three endpoints, records:

- The `content` block in the OpenAPI response (media type and schema).
- Whether the response is documented with a schema or as raw text/object.
- A one-sentence explanation of why endpoint C's documentation is more precise than B's, which is more precise than A's.

**Acceptance criteria.**

- File `notes/openapi-comparison.md` exists with three labeled sections.
- The OpenAPI JSON snippets are quoted directly (use `jq -r '.paths."/c".get.responses'`).
- Committed.

**Hint.** `curl -s http://localhost:5099/openapi/v1.json | jq '.paths."/c"'`.

**Estimated time.** 25 minutes.

---

## Problem 2 — Parameter binding from four sources

**Problem statement.** Build a single endpoint at `GET /homework/p2/{id}` that binds from **every** standard source in one signature:

```csharp
app.MapGet("/homework/p2/{id:int}", (
    int id,                                     // route
    [FromQuery] string? mode,                   // query
    [FromHeader(Name = "X-Locale")] string? locale, // header
    IClock clock,                               // services
    CancellationToken ct) => /* ... */);
```

The handler returns a JSON record echoing every value back, with the current UTC timestamp from `IClock`. If `mode` is `"fail"`, return `TypedResults.BadRequest<string>("forced failure")` to exercise the second result type.

Wire it up so the return type is `Results<Ok<EchoEverythingResponse>, BadRequest<string>>`.

**Acceptance criteria.**

- A buildable web project with the endpoint mapped.
- `dotnet build`: 0 warnings, 0 errors.
- Hitting the URL with `mode=fail` returns 400 with the message; without it returns 200 with the echo.
- The OpenAPI document documents both response shapes.
- Committed.

**Hint.** Register `IClock` as a singleton. Read X-Locale from `[FromHeader]`. Use `clock.GetUtcNow()` for the timestamp.

**Estimated time.** 45 minutes.

---

## Problem 3 — Data-annotation validation with problem details

**Problem statement.** Create `POST /homework/p3/signup` that accepts a `SignupRequest`:

```csharp
public sealed record SignupRequest(
    [Required, StringLength(80, MinimumLength = 2)] string Name,
    [Required, EmailAddress] string Email,
    [Required, Range(18, 120)] int Age,
    [StringLength(2000)] string? Bio);
```

The endpoint:

- Returns `Created<SignupResponse>` on success, where `SignupResponse` is `(Guid Id, string Name, string Email)`.
- Returns `ValidationProblem` on data-annotation failure (handled automatically by `.WithParameterValidation()` from `MinimalApis.Extensions`).
- Returns `Conflict<string>` if the email is `"taken@example.com"` — a stub for "already in use."

Write three tests via `WebApplicationFactory<Program>`:

1. Valid request → 201 Created.
2. Missing email → 400 problem details, with an `Email` key in the `errors` map.
3. `"taken@example.com"` → 409 Conflict.

**Acceptance criteria.**

- All three tests pass.
- The OpenAPI document records all three response shapes (`Created<SignupResponse>`, `ValidationProblem`, `Conflict<string>`).
- `dotnet build`: 0 warnings, 0 errors.
- Committed.

**Hint.** `Results<Created<SignupResponse>, ValidationProblem, Conflict<string>>` is the return type. Use `TypedResults.Conflict("taken@example.com is already registered.")` for the conflict path. The taken email is hard-coded; in Week 6 it becomes a DB lookup.

**Estimated time.** 1 hour.

---

## Problem 4 — Endpoint class with constructor injection

**Problem statement.** Refactor the Todo API from this week's challenge so that the five endpoint handlers are methods on a `TodoEndpoints` class, with `ITodoStore` and `IClock` injected via primary constructor. Wire the class as a scoped service.

The `Program.cs` route block should look roughly like:

```csharp
var todos = app.MapGroup("/api/v1/todos").WithTags("Todos");
todos.MapGet   ("/",         (TodoEndpoints e, CancellationToken ct) => e.GetAll(ct));
todos.MapGet   ("/{id:int}",    (TodoEndpoints e, int id, CancellationToken ct) => e.GetById(id, ct));
todos.MapPost  ("/",         (TodoEndpoints e, CreateTodoRequest body, CancellationToken ct) => e.Create(body, ct))
     .WithParameterValidation();
todos.MapPut   ("/{id:int}",    (TodoEndpoints e, int id, UpdateTodoRequest body, CancellationToken ct) => e.Update(id, body, ct))
     .WithParameterValidation();
todos.MapDelete("/{id:int}",    (TodoEndpoints e, int id, CancellationToken ct) => e.Remove(id, ct));
```

**Acceptance criteria.**

- `TodoEndpoints` is a `sealed class` with a primary constructor taking `ITodoStore` and `IClock`.
- All five handler methods are instance methods on `TodoEndpoints` with the right `TypedResults` return type.
- Registered as `AddScoped<TodoEndpoints>()` — **not** singleton.
- Existing tests from the challenge still pass.
- Committed.

**Hint.** Why scoped and not singleton? Because `TodoEndpoints` depends on `ITodoStore`, which might be scoped (it usually is in real apps). Registering the endpoint class as singleton would create a captive-dependency bug; `ValidateOnBuild` would catch it at startup.

**Estimated time.** 1 hour.

---

## Problem 5 — Five `[Theory]` tests for typed routes

**Problem statement.** Take the Todo API from this week's challenge and add five **`[Theory]`** tests using `WebApplicationFactory<Program>`. Each `[Theory]` covers multiple cases via `[InlineData]`.

Cover at minimum:

1. `GetById` — three cases: a real id (200), id `0` or `-1` (404), id `999999` (404).
2. `Create` — three cases: a valid title (201), an empty title (400), a 200-char title (400 because of `MinimumLength` is fine but `MaximumLength = 120`).
3. `Update` — three cases: existing id (200), unknown id (404), invalid title (400).
4. `Delete` — two cases: existing id (204), unknown id (404).
5. Path patterns — three cases: `/api/v1/todos`, `/api/v1/todos/1`, `/api/v1/todos/1/extra` (the last must 404 from routing, not 500).

**Acceptance criteria.**

- At least five `[Theory]` tests, each with `[InlineData]` for at least three rows (or two rows for the Delete case).
- Each test method has one assertion. No "test that does five things."
- `dotnet test` shows all of them passing.
- Committed.

**Hint.** Theories that test "an integer in the path" can use `[InlineData(1, 200)] [InlineData(0, 404)] [InlineData(-1, 404)]` and a single assertion on the response status code.

**Estimated time.** 1 hour.

---

## Problem 6 — Mini reflection essay

**Problem statement.** Write a 300–400 word reflection at `notes/week-02-reflection.md` answering:

1. Which felt more natural this week — Minimal APIs or dependency injection? Which felt more foreign? Why?
2. If you have written REST APIs in another language before (Flask, FastAPI, Express, Spring), where does ASP.NET Core 9 feel familiar, and where does it feel different? Cite one concrete example of each.
3. The lecture argues that "a Minimal API is not a stripped-down controller; it is a different programming model." Do you agree, after a week of writing them? If not, what would change your mind?
4. What's one thing you'd want to learn next that this week didn't cover?

**Acceptance criteria.**

- File exists, 300–400 words.
- Each numbered question is addressed in its own paragraph.
- File is committed.

**Hint.** This is for *you*, not for a grade. Be honest. Future-you reading it after Week 8 will be grateful.

**Estimated time.** 30 minutes.

---

## Time budget recap

| Problem | Estimated time |
|--------:|--------------:|
| 1 | 25 min |
| 2 | 45 min |
| 3 | 1 h 0 min |
| 4 | 1 h 0 min |
| 5 | 1 h 0 min |
| 6 | 30 min |
| **Total** | **~4 h 40 min** |

When you've finished all six, push your repo and open the [mini-project](./mini-project/README.md). The mini-project takes Week 1's Ledger CLI and puts it behind a typed REST surface — every concept from this week, applied end-to-end on a domain you already know.
