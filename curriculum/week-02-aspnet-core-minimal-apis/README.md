# Week 2 — ASP.NET Core Minimal APIs

Welcome back to **C9 · Crunch Sharp**. Week 1 made the language and the toolchain feel ordinary. Week 2 puts that language behind an HTTP port. By Friday you should be able to scaffold an ASP.NET Core 9 Minimal API from a blank folder, map five typed endpoints, register three services with the right lifetime each, generate an OpenAPI document, and serve the whole thing on `http://localhost:5000` without ever opening Visual Studio.

We are still in Phase 1 of the syllabus — the language phase — but this week starts the bridge to the backend phase that anchors weeks 5 through 8. The reason we cover Minimal APIs and dependency injection now is simple: every mini-project from Week 3 onward needs an HTTP surface, and every real .NET app you will ever see uses the same DI container. Learning these two things deeply, early, makes the rest of the course faster.

The first thing to internalize is that **a Minimal API is not a stripped-down MVC controller**. It is a different programming model. MVC routes a request to a method on a class; a Minimal API routes a request to a *delegate*. That delegate can be a lambda, a static method, or a method group. The framework reads its parameters and binds them from the request automatically — route values, query string, body, headers, dependency-injected services — using the same conventions everywhere. Master the binding rules and you have mastered most of the framework.

The second thing to internalize is that **dependency injection in .NET is a library, not a framework**. `Microsoft.Extensions.DependencyInjection` is one NuGet package, ~50 KB, and it is the same container used by ASP.NET Core, MAUI, Worker Services, Azure Functions, and Blazor. If you understand `AddSingleton`, `AddScoped`, `AddTransient`, and `IServiceProvider`, you understand DI everywhere in modern .NET. We will spend Lecture 2 making sure you do.

## Learning objectives

By the end of this week, you will be able to:

- **Scaffold** an ASP.NET Core 9 Minimal API from `dotnet new web` and explain what every line of the generated `Program.cs` does.
- **Map** `GET`, `POST`, `PUT`, and `DELETE` endpoints with `MapGet`, `MapPost`, `MapPut`, `MapDelete` — and choose between inline lambdas and named handler methods with reasons.
- **Bind** parameters from the route, the query string, the body, and headers; explain how the framework decides which source to use; and override the defaults with `[FromRoute]`, `[FromQuery]`, `[FromBody]`, `[FromHeader]`, `[FromServices]`.
- **Return** typed results with `TypedResults` rather than `IResult` — for both correct status codes and accurate OpenAPI metadata.
- **Validate** request payloads with `System.ComponentModel.DataAnnotations` and the `MinimalApis.Extensions` validation pattern; return RFC 7807 problem details on failure.
- **Generate** an OpenAPI 3.1 document with `Microsoft.AspNetCore.OpenApi` and serve a Swagger UI for local exploration.
- **Register** services with `AddSingleton`, `AddScoped`, and `AddTransient`, and pick the right lifetime for the job — not by reflex.
- **Inject** services into endpoint handlers via parameters, into typed handler classes via constructors, and into background work via `IServiceScopeFactory`.
- **Recognize** the three classic DI pitfalls — captive dependencies, scope leakage in singletons, and circular dependencies — and know how to fix each.

## Prerequisites

- **Week 1** of C9 complete: you can scaffold a solution from the `dotnet` CLI, you can read records and pattern matching, and your `dotnet build` reflexively prints zero warnings.
- HTTP fluency at the level of "I have used `curl` and I know what a 404 means." We do not teach REST conventions from scratch; if you've shipped a Flask or FastAPI service, you are exactly the audience.
- Nothing else. We start from `dotnet new web` and end at a working REST service for the Week 1 Ledger domain.

## Topics covered

- The minimal-API model: `WebApplicationBuilder`, `WebApplication`, the `Map*` family, route groups, and route handler delegates.
- MVC controllers, briefly — what they are and the three reasons to reach for them instead of Minimal APIs (filters, content negotiation, complex model binding).
- Parameter binding: routes, query string, body, headers, services, `BindAsync`, custom binders.
- `TypedResults` vs `IResult` vs raw `Results.Ok(...)` — why typed matters for both correctness and OpenAPI.
- The OpenAPI generator built into `Microsoft.AspNetCore.OpenApi` (replacing Swashbuckle as the default in .NET 9).
- Swagger UI via `Swashbuckle.AspNetCore.SwaggerUI` — purely a viewer, no generation responsibility.
- Validation: `System.ComponentModel.DataAnnotations` attributes, `Results.ValidationProblem`, the `MinimalApis.Extensions` filter pattern, and FluentValidation as the third-party alternative.
- `Microsoft.Extensions.DependencyInjection`: registration, three lifetimes, `IServiceProvider`, `IServiceScopeFactory`.
- Constructor injection, parameter injection, `[FromServices]`, the `[FromKeyedServices]` attribute (since .NET 8).
- Captive dependencies, why a singleton must never hold a scoped service, and how the container catches some — but not all — of these at startup.

## Weekly schedule

The schedule adds up to approximately **36 hours**. Treat it as a target, not a contract.

| Day       | Focus                                            | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | Minimal APIs, routing, binding, TypedResults     |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | OpenAPI, Swagger UI, validation, problem details |    1h    |    2h     |     1h     |    0.5h   |   1h     |     0h       |    0.5h    |     6h      |
| Wednesday | Dependency injection, three lifetimes, services  |    2h    |    2h     |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     6h      |
| Thursday  | DI pitfalls, `IServiceScopeFactory`, testing     |    1h    |    1h     |     0h     |    0.5h   |   1h     |     2h       |    0.5h    |     6h      |
| Friday    | Mini-project work — Ledger REST                  |    0h    |    1h     |     0h     |    0.5h   |   1h     |     3h       |    0.5h    |     6h      |
| Saturday  | Mini-project deep work                           |    0h    |    0h     |     0h     |    0h     |   1h     |     3h       |    0h      |     4h      |
| Sunday    | Quiz, review, polish                             |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                  | **6h**   | **7.5h**  | **1h**     | **3.5h**  | **6h**   | **8.5h**     | **2.5h**   | **35h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | Curated Microsoft Learn, ASP.NET Core source, and open-source links |
| [lecture-notes/01-minimal-apis-the-defaults.md](./lecture-notes/01-minimal-apis-the-defaults.md) | Anatomy of a `WebApplication`; `Map*`; binding; `TypedResults`; OpenAPI; validation |
| [lecture-notes/02-dependency-injection-and-services.md](./lecture-notes/02-dependency-injection-and-services.md) | Why DI; lifetimes; registration; injection; `IServiceProvider`; pitfalls |
| [exercises/README.md](./exercises/README.md) | Index of short coding exercises |
| [exercises/exercise-01-hello-api.md](./exercises/exercise-01-hello-api.md) | `dotnet new web`, map four endpoints, hit them with `curl` |
| [exercises/exercise-02-typed-routes.cs](./exercises/exercise-02-typed-routes.cs) | Fill-in-the-TODO endpoint binding and `TypedResults` drill |
| [exercises/exercise-03-injecting-services.cs](./exercises/exercise-03-injecting-services.cs) | Register three lifetimes; observe what each one means at request time |
| [challenges/README.md](./challenges/README.md) | Index of weekly challenges |
| [challenges/challenge-01-the-five-endpoint-todo-api.md](./challenges/challenge-01-the-five-endpoint-todo-api.md) | Build a five-endpoint typed Todo API end-to-end |
| [quiz.md](./quiz.md) | 10 multiple-choice questions |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for the "Ledger REST" mini-project — Week 1's Ledger, served by ASP.NET Core 9 |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

A nullable-reference warning is a bug. A "missing XML doc comment" warning we can let slide if your `.csproj` doesn't ask for it; everything else is a bug. By the end of Week 2 you will have an ASP.NET Core 9 service that compiles clean, runs on Kestrel, generates an OpenAPI 3.1 document, and returns RFC 7807 problem details on validation failure — all from ~200 lines of C# you wrote yourself.

## A note on what's not here

Week 2 introduces ASP.NET Core, but it does **not** introduce:

- **EF Core.** No database this week. Storage is an in-memory `ConcurrentDictionary` or a JSON file at most. EF Core lands in Week 6.
- **Authentication.** No JWT, no cookies, no Identity. Endpoints are anonymous. Auth is Week 7.
- **MVC controllers as a default.** We cover them as an alternative but every example is a Minimal API. MVC and Razor Pages get a fuller treatment in Week 5 once the language is fully internalized.
- **Background workers.** No `IHostedService`. That is Week 8.

The point of Week 2 is a sharp, narrow tool: typed HTTP endpoints with dependency-injected services, and nothing else.

## Stretch goals

If you finish the regular work early and want to push further:

- Read the official **"Tutorial: Create a minimal API with ASP.NET Core"** end to end: <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>.
- Skim **David Fowler's MinimalAPIs design notes** (he is one of the engineers who designed the model): <https://github.com/davidfowl/CommunityStandUpMinimalAPI>.
- Read the **`Microsoft.AspNetCore.OpenApi` source** to see how the document is generated: <https://github.com/dotnet/aspnetcore/tree/main/src/OpenApi>.
- Watch a recent **.NET Conf** talk on Minimal APIs or DI; the official channel reposts every talk: <https://www.youtube.com/@dotnet>.
- Read the **`ServiceProvider` source** — it is one of the most readable bits of the BCL: <https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.DependencyInjection>.

## Up next

Continue to **Week 3 — async/await, Tasks, and Channels** once you have pushed the mini-project to your GitHub. The mini-project's CSV loader from Week 1 becomes the persistence layer this week; in Week 3 it becomes the async, cancellable, streaming pipeline behind a long-running endpoint.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
