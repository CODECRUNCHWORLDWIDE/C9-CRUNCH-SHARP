# Week 12 Resources — Production-Grade Service Integration

This is the canonical reading list for Week 12. Every URL has been opened, every package has been installed, every section is referenced by the lectures, exercises, or mini-project. Read what you need when you need it; the lecture notes will tell you which section of which document is load-bearing for the technique under discussion.

The resource list is grouped by the role the document plays in the stack — host composition, REST surface, gRPC surface, SignalR surface, EF Core + PostgreSQL, JWT bearer, Serilog logging, OpenTelemetry tracing, integration testing, and adjacent reading. The "adjacent" section is the most valuable for the team member who wants to outgrow the lectures; do not skip it because it sits last on the page.

## Primary references — Microsoft Learn (ASP.NET Core 8)

The Microsoft Learn ASP.NET Core hub is the canonical documentation source for every framework feature we use this week. Bookmark the root and dig from there.

- **ASP.NET Core 8 overview** — <https://learn.microsoft.com/en-us/aspnet/core/introduction-to-aspnet-core>. The one-page summary of what ASP.NET Core 8 is, what it ships with, and what its LTS window looks like (it is supported through November 2026, which matters for the deployment week).
- **WebApplicationBuilder vs IHostBuilder** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>. The lifecycle of the host: configuration loaded, services registered, app built, middleware pipeline composed, server started. Read this before reading any lecture; the rest of the week assumes you have internalized "build, then run."
- **Configuration in ASP.NET Core** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/>. The precedence rules (environment variables override `appsettings.{Environment}.json` which overrides `appsettings.json`), the `IConfiguration` lookup model, the `IOptions<T>` pattern. The lecture-2 section on per-environment overrides relies on this chapter.
- **The dependency injection container** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>. Scoped vs singleton vs transient; what "scoped" means for a SignalR hub invocation; why a `DbContext` is scoped. The "service lifetimes" subsection is the part that resolves arguments about hub-vs-controller scoping.
- **Middleware fundamentals** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>. Order of `UseAuthentication()`, `UseAuthorization()`, `UseEndpoints()`, `MapHub<T>()`, `MapGrpcService<T>()`. The right answer to "does it matter what order I call these in?" is "yes" and this chapter is why.
- **Minimal APIs reference** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis>. The route-group helper `app.MapGroup("/api")`, the `.RequireAuthorization()` chain, the parameter binding rules.
- **Authentication with JWT bearer tokens** — <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn>. The `AddJwtBearer(...)` registration, the `TokenValidationParameters`, the `Events.OnMessageReceived` hook for SignalR query-string auth.
- **Authorization policies** — <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies>. `RequireAuthenticatedUser()`, `RequireRole(...)`, custom policy handlers; we will register one in the mini-project to require the `org_id` claim be present.

## REST surface — minimal APIs

- **Tutorial: minimal API to-do** — <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>. The canonical "REST CRUD in minimal APIs" walkthrough. ProjectHub's REST surface is shaped like this.
- **OpenAPI / Swagger with Swashbuckle** — <https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle>. We expose Swagger in dev only; the configuration is in the mini-project starter.
- **Route handlers and filters** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers>. Endpoint filters for validation, the `IEndpointFilter` interface, the order vs middleware question.
- **Problem details (RFC 7807)** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling#problem-details>. The error-body contract; we use `Results.Problem(...)` rather than ad-hoc JSON for failures.

## gRPC surface

- **gRPC for ASP.NET Core overview** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/>. The hub of the gRPC documentation tree.
- **Create a gRPC client and server** — <https://learn.microsoft.com/en-us/aspnet/core/tutorials/grpc/grpc-start>. The canonical "make it work" walkthrough; ProjectHub's gRPC service is shaped this way.
- **Authentication and authorization for gRPC** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>. `[Authorize]` on a `Grpc.Core.ServerCallContext`, `CallCredentials` on the client, the `authorization` metadata header.
- **gRPC interceptors** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors>. The cross-cutting hook for logging, authentication metadata, and tracing.
- **Compare gRPC services with HTTP APIs** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/comparison>. The "when do I pick REST vs gRPC" decision matrix; mini-project README references it.

## SignalR surface

(All revisited from Week 11; we list them again because the integration week references specific subsections.)

- **ASP.NET Core SignalR introduction** — <https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction>.
- **Hubs** — <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs>.
- **Authentication and authorization in SignalR** — <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>. The `OnMessageReceived` hook for the WebSocket upgrade.
- **Use ASP.NET Core SignalR with `IHubContext<THub>`** — <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext>. The "broadcast from non-hub code" pattern; load-bearing for the REST-handler-publishes-SignalR-event use case.
- **`IServiceScopeFactory` and SignalR** — <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#use-iservicescopefactory>. The official reference for the scope-resolution trap covered in lecture 1.

## EF Core + PostgreSQL

- **EF Core documentation hub** — <https://learn.microsoft.com/en-us/ef/core/>.
- **`DbContext` lifetime, configuration, and initialization** — <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>. The "use a context with dependency injection" subsection is what `AddDbContextPool` is described in.
- **`DbContext` pooling** — <https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#dbcontext-pooling>. The performance reason we use `AddDbContextPool` over `AddDbContext`.
- **`IDbContextFactory<T>`** — <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor>. The pattern for resolving a context outside of a DI scope; we use it from the hub's broadcast helper.
- **Migrations** — <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>. `dotnet ef migrations add`, `dotnet ef database update`, the apply-on-startup pattern via `Database.MigrateAsync()`.
- **The Npgsql EF Core provider** — <https://www.npgsql.org/efcore/>. The provider-specific documentation. Conventions, mapping types (`jsonb`, `timestamptz`, arrays), the JSON-by-default behavior.
- **Npgsql repository** — <https://github.com/npgsql/npgsql>. The ADO.NET-level driver; EF Core sits on top of this.
- **`Npgsql.EntityFrameworkCore.PostgreSQL` repository** — <https://github.com/npgsql/efcore.pg>. The provider; release notes, breaking-change announcements.

## JWT bearer authentication

- **JWT bearer authentication tutorial** — <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn>.
- **`Microsoft.IdentityModel.Tokens` GitHub** — <https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet>. The library that validates JWTs; the `TokenValidationParameters` record is from here.
- **`System.IdentityModel.Tokens.Jwt` reference** — <https://learn.microsoft.com/en-us/dotnet/api/system.identitymodel.tokens.jwt>. The legacy-named-but-still-current API for issuing and parsing JWTs.
- **JWT.io decoder** — <https://jwt.io/>. Paste a token; verify the claims and the signature. Useful while debugging.

## Serilog — structured logging

- **Serilog repository** — <https://github.com/serilog/serilog>. The main library. Read the README; the message-template syntax is non-obvious if you have only used `string.Format`.
- **Serilog for ASP.NET Core** — <https://github.com/serilog/serilog-aspnetcore>. The integration package. `UseSerilog()` host extension; the `RequestLoggingMiddleware` that produces structured per-request log entries.
- **`Serilog.Sinks.Console`** — <https://github.com/serilog/serilog-sinks-console>. The console writer; expression-based output templates.
- **`Serilog.Sinks.File`** — <https://github.com/serilog/serilog-sinks-file>. Rolling file sink; we use it in dev to keep the last 7 days of logs.
- **`Serilog.Formatting.Compact`** — <https://github.com/serilog/serilog-formatting-compact>. The JSON-per-line format that log aggregators index without an additional parser.
- **`Serilog.Enrichers.Environment`** — <https://github.com/serilog/serilog-enrichers-environment>. Adds machine name and environment to every log line.
- **`Serilog.Settings.Configuration`** — <https://github.com/serilog/serilog-settings-configuration>. Read Serilog configuration from `IConfiguration`; lets you keep log levels in `appsettings.json`.

## OpenTelemetry — distributed tracing and metrics

- **OpenTelemetry .NET SDK** — <https://github.com/open-telemetry/opentelemetry-dotnet>. The main repo. The README is the right starting point; the `examples/` folder has runnable hosts.
- **OpenTelemetry .NET instrumentation packages** — <https://github.com/open-telemetry/opentelemetry-dotnet-contrib>. The non-core instrumentations (Npgsql, MySql, others).
- **OpenTelemetry .NET getting-started for ASP.NET Core** — <https://opentelemetry.io/docs/languages/net/getting-started/>. The shortest path to a traced app.
- **Semantic conventions** — <https://opentelemetry.io/docs/specs/semconv/>. The shared vocabulary for span and metric names. `http.method`, `http.route`, `db.system`, `db.statement`, `rpc.system`, `rpc.service`, `rpc.method`. Honor these so your traces compose with the wider ecosystem.
- **OTLP — the OpenTelemetry Protocol** — <https://opentelemetry.io/docs/specs/otlp/>. The wire format the OTLP exporter speaks; useful when debugging a "the spans never reach Jaeger" problem.
- **Jaeger** — <https://www.jaegertracing.io/docs/>. The trace viewer the mini-project's docker-compose ships. Free, open source, runs in one container.
- **`Activity` and `ActivitySource` in .NET** — <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>. The framework-level API that OpenTelemetry's tracing instrumentation builds on. `Activity.Current` is how you access the current trace from application code.

## Integration testing — xUnit, WebApplicationFactory, Testcontainers

- **xUnit** — <https://xunit.net/>. The test framework. The "getting started" page lists the right `dotnet new xunit` command and the test-discovery model.
- **xUnit on GitHub** — <https://github.com/xunit/xunit>. The repo; release notes; the `[Theory]` vs `[Fact]` discussion.
- **`Microsoft.AspNetCore.Mvc.Testing` reference** — <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. The canonical doc for `WebApplicationFactory<TEntryPoint>`. Read the "customize WebApplicationFactory" subsection; the mini-project uses it to inject the Testcontainers connection string.
- **`WebApplicationFactory<TEntryPoint>` API** — <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1>. The reference page; the `ConfigureWebHost` override is the one you will call.
- **Testcontainers for .NET** — <https://dotnet.testcontainers.org/>. The documentation hub.
- **Testcontainers .NET on GitHub** — <https://github.com/testcontainers/testcontainers-dotnet>. The repo; modules; module discovery model.
- **`Testcontainers.PostgreSql` module** — <https://www.nuget.org/packages/Testcontainers.PostgreSql>. The PostgreSQL preset; we use it in every integration-test class fixture.
- **`FluentAssertions`** — <https://fluentassertions.com/>. Optional but recommended. The assertion library with the readable "value.Should().Be(...)" syntax.
- **Coverlet — code coverage for .NET** — <https://github.com/coverlet-coverage/coverlet>. The integration tests in the mini-project produce a coverage report via Coverlet.

## Docker, docker-compose, and the operational surface

- **`mcr.microsoft.com/dotnet/aspnet:8.0` image** — <https://hub.docker.com/_/microsoft-dotnet-aspnet>. The base image for the runtime stage of the Dockerfile.
- **`mcr.microsoft.com/dotnet/sdk:8.0` image** — <https://hub.docker.com/_/microsoft-dotnet-sdk>. The base image for the build stage.
- **`postgres:16` image** — <https://hub.docker.com/_/postgres>. The PostgreSQL base; `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` environment variables.
- **`jaegertracing/all-in-one`** — <https://hub.docker.com/r/jaegertracing/all-in-one>. The single-container Jaeger backend the mini-project's docker-compose uses to render traces.
- **Multi-stage Dockerfile tutorial** — <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>. The canonical "build then publish" Dockerfile pattern for ASP.NET Core.

## Adjacent reading — strongly recommended

The lectures cite these by name; reading them up front pays for itself.

- **Andrew Lock's blog — "Defining and creating custom configuration providers in .NET"** — <https://andrewlock.net/creating-a-custom-aspnetcore-configuration-provider/>. The deepest plain-language treatment of the `IConfiguration` extensibility model. Read it once and never feel uncertain about precedence again.
- **David Fowler's "Async guidance"** — <https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md>. A pattern catalog for `async` mistakes. Many of the EF Core scoping bugs we cover have an async root cause; this is the patient diagnosis text.
- **"Improvements to auth and identity in .NET 8"** — <https://devblogs.microsoft.com/dotnet/whats-new-with-identity-in-dotnet-8/>. What the framework added in .NET 8 that we are getting for free.
- **The `dotnet/aspnetcore` repository** — <https://github.com/dotnet/aspnetcore>. Source-link from any framework type. `Hub`, `JwtBearerHandler`, `EndpointMiddleware` — read the source when a behavior surprises you.
- **The OpenTelemetry specification** — <https://opentelemetry.io/docs/specs/otel/>. The cross-language spec; the .NET implementation honors it. Useful when correlating with a service written in Go or Python.
- **Phil Haack's "Web API design: the right way"** — <https://haacked.com/archive/2018/04/16/web-api-design/>. An older but still-accurate take on REST API design heuristics. ProjectHub's URLs and verbs follow this.
- **"Best practices for writing logs"** — <https://learn.microsoft.com/en-us/azure/azure-monitor/best-practices-logs>. The Azure-Monitor-specific page; the heuristics generalize to any log aggregator. "Make your logs queryable, not readable."

## Books — read after the week, not during

- **"Modern Software Engineering" — David Farley.** The chapter on observability is the most accurate three-page description of what we are doing this week. The rest of the book is also good.
- **"Distributed Tracing in Practice" — Austin Parker et al. (O'Reilly).** The trade-press treatment of OpenTelemetry's conceptual ancestry; what the spec is trying to standardize and why.
- **"Programming ASP.NET Core" — Dino Esposito (Microsoft Press).** Mostly the framework-team narrative; useful for the "why is the host built this way" question. Older than .NET 8 but the architecture has not changed in spirit.

## Bookmarks worth saving for the rest of C9

- The Microsoft Learn ASP.NET Core 8 hub.
- The Serilog GitHub org.
- The OpenTelemetry .NET repository.
- The Testcontainers for .NET site.
- The Jaeger documentation.
- The `dotnet/aspnetcore` repository (for source-link).

By Friday of this week you should have all six open in pinned browser tabs. Production-grade C# work means moving between three or four of these documents per hour; the time saved by not re-googling is real.
