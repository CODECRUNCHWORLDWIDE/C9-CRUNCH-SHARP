# Week 2 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The ASP.NET Core source is MIT-licensed and public on GitHub. The Minimal APIs design notes are in the open. No paywalled books are linked.

## Required reading (work it into your week)

- **Minimal APIs overview** — the canonical Microsoft Learn entry point:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview>
- **Tutorial: Create a minimal API with ASP.NET Core** — the official end-to-end walkthrough; ~45 minutes:
  <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>
- **Parameter binding in Minimal APIs**:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding>
- **Responses in Minimal APIs** — `TypedResults`, `IResult`, problem details:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses>
- **Dependency injection in ASP.NET Core**:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>
- **`Microsoft.AspNetCore.OpenApi` overview** — the .NET 9 default OpenAPI generator:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview>

## Authoritative deep dives

- **Minimal APIs reference** — every method on the route handler:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis>
- **`IServiceCollection` reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.iservicecollection>
- **`IServiceProvider` reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.iserviceprovider>
- **Service lifetimes** (deep):
  <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#service-lifetimes>
- **Keyed services** (since .NET 8):
  <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#keyed-services>

## Official ASP.NET Core docs

- **ASP.NET Core 9 release notes**: <https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0>
- **Routing in ASP.NET Core**: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing>
- **Middleware fundamentals**: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>
- **Kestrel web server** — the default in-process HTTP server:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel>
- **Problem details (RFC 7807) in ASP.NET Core**:
  <https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors>
- **Validation with data annotations**:
  <https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation>

## Open-source projects to read this week

You learn more from one hour reading well-written C# than from three hours of tutorials.

- **`dotnet/aspnetcore`** — the full source of ASP.NET Core; readable, MIT-licensed:
  <https://github.com/dotnet/aspnetcore>
- **`Microsoft.AspNetCore.Routing`** — exactly how `MapGet` works:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Http/Routing>
- **`Microsoft.AspNetCore.OpenApi`** — the new OpenAPI generator (replaces Swashbuckle by default in .NET 9):
  <https://github.com/dotnet/aspnetcore/tree/main/src/OpenApi>
- **`Microsoft.Extensions.DependencyInjection`** — the entire DI container, surprisingly small:
  <https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.DependencyInjection>

## Community deep-dives

- **David Fowler's Minimal API design notes** (Fowler is one of the architects of ASP.NET Core):
  <https://github.com/davidfowl/CommunityStandUpMinimalAPI>
- **Andrew Lock — "Exploring ASP.NET Core"** series. The single best independent .NET writer:
  <https://andrewlock.net/series/exploring-asp-net-core/>
- **Maarten Balliauw — DI lifetime gotchas**:
  <https://blog.maartenballiauw.be/category/aspnetcore.html>

## Libraries we touch this week

- **`Microsoft.AspNetCore.OpenApi`** — generates the OpenAPI 3.1 document; ships in ASP.NET Core 9:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview>
- **`Swashbuckle.AspNetCore.SwaggerUI`** — pure UI viewer; no document generation responsibility:
  <https://github.com/domaindrivendev/Swashbuckle.AspNetCore>
- **`MinimalApis.Extensions`** — community filters and helpers; useful for endpoint validation:
  <https://github.com/DamianEdwards/MinimalApis.Extensions>
- **`FluentValidation`** — the third-party validation library; alternative to data annotations:
  <https://docs.fluentvalidation.net/>
- **`Microsoft.Extensions.DependencyInjection`** — the DI container; you already have it transitively via the web SDK.

## Editors

Unchanged from Week 1.

- **VS Code + C# Dev Kit** (primary): <https://code.visualstudio.com/docs/csharp/get-started>
- **JetBrains Rider Community** (secondary, free for non-commercial): <https://www.jetbrains.com/rider/>
- The new bit: Rider and VS Code both ship an HTTP-file format that lets you keep request scripts next to the API source. We will use `*.http` files starting this week.

## Free books and chapters

- **"Build web APIs with ASP.NET Core"** — free Microsoft Learn module path:
  <https://learn.microsoft.com/en-us/training/paths/build-web-api-aspnet-core/>
- **"Create a minimal API"** — the free Learn module that pairs with this week:
  <https://learn.microsoft.com/en-us/training/modules/build-web-api-minimal-api/>

## Videos (free, no signup)

- **"Minimal APIs in .NET 9"** — official, ~30 min: search the **dotnet** YouTube channel for the most recent ".NET 9 Minimal API" talk: <https://www.youtube.com/@dotnet>
- **Nick Chapsas — DI lifetimes** (community, very clear): <https://www.youtube.com/@nickchapsas>
- **David Fowler & Stephen Toub — ASP.NET Core architecture** appearances on the **On .NET** show: <https://learn.microsoft.com/en-us/shows/on-net/>

## Tools you'll use this week

- **`dotnet` CLI** — same as Week 1.
- **`curl`** — preinstalled on macOS and Linux. We use it heavily in the exercises.
- **`*.http` files** — a plain-text format both VS Code (with the REST Client extension or the C# Dev Kit's built-in support) and Rider can execute. Far better than a Postman collection because it lives in Git next to your code.
- **`Swagger UI`** — opens at `/swagger` when you wire it up in `Program.cs`. Useful for manual exploration, less useful for automation than `*.http` files.

## The spec — when you need to be exact

- **OpenAPI Specification 3.1.0** — what `Microsoft.AspNetCore.OpenApi` produces:
  <https://spec.openapis.org/oas/v3.1.0>
- **RFC 7807 — Problem Details for HTTP APIs** — what `Results.ValidationProblem` and `Results.Problem` return:
  <https://www.rfc-editor.org/rfc/rfc7807>
- **RFC 9110 — HTTP Semantics** — the current HTTP/1.1 + HTTP/2 specification, replaces the older RFC 7230–7235:
  <https://www.rfc-editor.org/rfc/rfc9110>

## Glossary cheat sheet

Keep this open in a tab.

| Term | Plain English |
|------|---------------|
| **Minimal API** | The route-handler-delegate model in ASP.NET Core. `app.MapGet("/", () => "hi")`. |
| **MVC controllers** | The class-based routing model. `[ApiController] public class FooController : ControllerBase { ... }`. |
| **`WebApplication`** | The configured app. Returned by `builder.Build()`. Used to add middleware and map routes. |
| **`WebApplicationBuilder`** | The pre-built configuration object. Used to add services and read configuration. |
| **`IResult`** | An ASP.NET Core return type that knows how to write itself to the response. |
| **`TypedResults`** | A static class of factories that return strongly-typed `IResult` implementations (e.g. `Ok<Todo>`). |
| **`IServiceCollection`** | The DI registration surface. You add to it during startup, then it freezes. |
| **`IServiceProvider`** | The DI resolution surface. You ask it for services at run time. |
| **`AddSingleton`** | One instance for the lifetime of the application. |
| **`AddScoped`** | One instance per HTTP request (or per `IServiceScope`). |
| **`AddTransient`** | A new instance every time you ask. |
| **Captive dependency** | A long-lived service holding a reference to a shorter-lived one. Bug. |
| **`[FromServices]`** | Tells the binder "this parameter comes from the DI container, not the request." Often optional in Minimal APIs in .NET 9. |
| **Problem details** | RFC 7807 — the standard JSON shape for error responses in HTTP APIs. |
| **OpenAPI** | The standard description format for HTTP APIs. Was called Swagger 2.0; renamed to OpenAPI in version 3.0. |
| **Swagger UI** | A browser viewer for an OpenAPI document. Separate from OpenAPI generation. |

---

*If a link 404s, please open an issue so we can replace it.*
