# Week 13 Resources — Capstone Build Week

This is the canonical reading list for Week 13, the build milestone of the Polyglot Workshop capstone. Every URL has been opened, every package has been installed, every section is referenced by the lectures, exercises, or the milestone brief. Read what you need when you need it; the lecture notes tell you which section of which document is load-bearing for the technique under discussion.

The list is grouped by the role the document plays in the build — the contract, code generation, the ASP.NET Core 9 gRPC service, EF Core 9 + PostgreSQL, the vertical-slice discipline, integration testing, CI, observability, and adjacent reading. The "adjacent" section is the most valuable for the team member who wants to drive the next two weeks confidently; do not skip it because it sits last.

> **Framework note.** The capstone targets **.NET 9 / ASP.NET Core 9 / EF Core 9 / C# 13**, a deliberate step up from the .NET 8 of Weeks 1–12. Confirm `dotnet --version` prints `9.0.x` and `dotnet ef --version` prints `9.0.x` before you begin.

## The contract — Protocol Buffers

- **Protocol Buffers proto3 language guide** — <https://protobuf.dev/programming-guides/proto3/>. The canonical reference for `message`, `enum`, `service`, scalar types, and `repeated`. Read the "Assigning field numbers" and "Enum" sections; they are the source of the field-number-is-forever and zero-`UNSPECIFIED` rules from Lecture 1.
- **Field-number assignment rules** — <https://protobuf.dev/programming-guides/proto3/#assigning>. Why numbers are the wire identity, why you never renumber, and what `reserved` is for. Challenge 1 lives here.
- **`reserved` fields and names** — <https://protobuf.dev/programming-guides/proto3/#fieldreserved>. How to retire a field without breaking the wire.
- **Well-known types — `Timestamp`** — <https://protobuf.dev/reference/protobuf/google.protobuf/#timestamp>. The standard UTC-instant type; round-trips to `DateTimeOffset` via the generated helpers.
- **Style guide** — <https://protobuf.dev/programming-guides/style/>. Naming conventions (`snake_case` fields, `PascalCase` messages, `SCREAMING_SNAKE` enum values) the generated C# remaps idiomatically.

## Code generation — gRPC for .NET

- **gRPC for .NET overview** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/>. The hub of the gRPC documentation tree; start here.
- **gRPC services with C# (the basics)** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>. The `<Protobuf>` MSBuild item, `GrpcServices` values, and what `Grpc.Tools` generates. The "Generated C# assets" subsection is the one Lecture 1 leans on.
- **Create a gRPC client and server** — <https://learn.microsoft.com/en-us/aspnet/core/tutorials/grpc/grpc-start>. The canonical "make it work" walkthrough; `Workshop.Api` is shaped this way.
- **gRPC services with ASP.NET Core** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>. `AddGrpc()`, `MapGrpcService<T>()`, the HTTP/2 requirement, Kestrel configuration.
- **Call gRPC services with the .NET client** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>. `GrpcChannel.ForAddress`, the generated client, `AsyncUnaryCall<T>`.
- **gRPC versioning** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning>. Additive vs breaking changes; the safe path Challenge 1 Part 3 demonstrates.
- **gRPC for ASP.NET Core on .NET (Web for the browser)** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>. How the Blazor admin client consumes the same generated client over gRPC-Web (Week 15).
- **gRPC and Protobuf repositories** — <https://github.com/grpc/grpc> and <https://github.com/protocolbuffers/protobuf>. The upstream sources for the runtime and the compiler.

## The ASP.NET Core 9 service

- **What's new in ASP.NET Core 9** — <https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0>. The step-up from 8; read it once so the version bump is not a surprise.
- **What's new in .NET 9** — <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/overview>. The runtime/SDK changes underneath ASP.NET Core 9.
- **What's new in C# 13** — <https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13>. Collection expressions, `params` collections, and the other idioms the lecture code uses.
- **WebApplicationBuilder host fundamentals** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>. Build, then run. The lifecycle the short `Program.cs` relies on.
- **Error handling in gRPC services** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/error-handling>. `RpcException`, `StatusCode`, and why a gRPC method surfaces `NotFound`/`InvalidArgument` instead of throwing a bare `Exception`.
- **Test gRPC services and gRPC tooling** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/test-tools>. `grpcurl`, gRPC reflection (`AddGrpcReflection()`), and the developer tooling the exercises use to poke the service by hand.

## EF Core 9 + PostgreSQL

- **EF Core documentation hub** — <https://learn.microsoft.com/en-us/ef/core/>.
- **What's new in EF Core 9** — <https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew>. The features and breaking changes the capstone gets on the step up to 9.
- **The Npgsql EF Core provider (Microsoft Learn)** — <https://learn.microsoft.com/en-us/ef/core/providers/npgsql>. `UseNpgsql`, type mapping (`timestamptz`, `jsonb`, arrays), the connection-string shape.
- **`Npgsql.EntityFrameworkCore.PostgreSQL` site** — <https://www.npgsql.org/efcore/>. The provider-specific documentation; conventions and mapping detail.
- **`Npgsql.EntityFrameworkCore.PostgreSQL` repository** — <https://github.com/npgsql/efcore.pg>. Release notes, breaking-change announcements, the 9.0 compatibility matrix.
- **`DbContext` lifetime, configuration, and initialization** — <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>. `AddDbContextPool` vs `AddDbContext`; the pooled form is the capstone default.
- **Indexes** — <https://learn.microsoft.com/en-us/ef/core/modeling/indexes>. The unique composite index that encodes "a learner enrolls once."
- **Migrations** — <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>. `dotnet ef migrations add`, `dotnet ef migrations script`, `Database.MigrateAsync()`, and why migrations are checked in.
- **EF Core tools reference (`dotnet ef`)** — <https://learn.microsoft.com/en-us/ef/core/cli/dotnet>. Install the 9.0.x global tool; the command surface for the migration workflow.

## The vertical-slice discipline and scope cutting

- **Jimmy Bogard — "Vertical Slice Architecture"** — <https://www.jimmybogard.com/vertical-slice-architecture/>. The canonical write-up of building one end-to-end slice rather than horizontal layers. Lecture 2's organizing idea.
- **Tutorial: create a minimal API (incremental)** — <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>. The .NET-flavored "build one slice at a time" walkthrough; the rhythm the milestone follows.
- **API design guidelines** — <https://learn.microsoft.com/en-us/azure/architecture/best-practices/api-design>. Heuristics for shaping the RPCs and the resources behind them.

## Integration testing — xUnit, WebApplicationFactory, Testcontainers

- **Integration tests in ASP.NET Core** — <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. The canonical doc for `WebApplicationFactory<TEntryPoint>`. Read "Customize WebApplicationFactory"; the milestone uses it to inject the Testcontainers connection string.
- **Test gRPC services with `WebApplicationFactory`** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/test-services>. The supported recipe for building a `GrpcChannel` over `Server.CreateHandler()` so the gRPC call routes through the in-memory `TestServer`.
- **`WebApplicationFactory<TEntryPoint>` API** — <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1>. The reference page; `ConfigureWebHost` is the override you call.
- **xUnit** — <https://xunit.net/>. The test framework; `dotnet new xunit`, the discovery model.
- **xUnit shared context** — <https://xunit.net/docs/shared-context>. `IClassFixture<T>`, `ICollectionFixture<T>`, and `IAsyncLifetime` — the fixture lifecycle the `WorkshopFactory` uses.
- **Testcontainers for .NET** — <https://dotnet.testcontainers.org/>. The documentation hub; the default integration-test substrate for the whole capstone.
- **Testcontainers PostgreSQL module** — <https://dotnet.testcontainers.org/modules/postgres/>. The `PostgreSqlBuilder` preset used in every integration-test fixture.
- **Testcontainers wait strategies** — <https://dotnet.testcontainers.org/api/wait_strategies/>. The cold-runner timeout fix from Challenge 2.
- **Testcontainers for .NET on GitHub** — <https://github.com/testcontainers/testcontainers-dotnet>. The repo; module list; the Docker-socket discovery model.
- **`FluentAssertions`** — <https://fluentassertions.com/>. The readable `value.Should().Be(...)` syntax the tests use.

## Continuous integration — GitHub Actions

- **Building and testing .NET** — <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>. The canonical .NET CI workflow; the Lecture-3 `ci.yml` is this shape.
- **`actions/setup-dotnet`** — <https://github.com/actions/setup-dotnet>. Pinning the SDK to `9.0.x`; the Break-1 lesson from Challenge 2.
- **About GitHub-hosted runners** — <https://docs.github.com/en/actions/using-github-hosted-runners/about-github-hosted-runners/about-github-hosted-runners>. What `ubuntu-latest` ships, including a running Docker daemon — why Testcontainers works on it unmodified.
- **`actions/cache`** — <https://github.com/actions/cache>. Caching the NuGet restore; a Challenge-2 stretch goal.
- **Using a matrix for your jobs** — <https://docs.github.com/en/actions/using-jobs/using-a-matrix-for-your-jobs>. Matrixing the substrate-portable unit tests while keeping the integration job ubuntu-only.

## Observability — Serilog + OpenTelemetry (carried from Week 12)

- **Serilog for ASP.NET Core** — <https://github.com/serilog/serilog-aspnetcore>. `UseSerilog()`, the request-logging middleware, the compact JSON formatter.
- **Serilog** — <https://github.com/serilog/serilog>. The message-template syntax; structured fields as a contract.
- **`Serilog.Formatting.Compact`** — <https://github.com/serilog/serilog-formatting-compact>. The JSON-per-line format aggregators index without a parser.
- **OpenTelemetry .NET SDK** — <https://github.com/open-telemetry/opentelemetry-dotnet>. `AddOpenTelemetry().WithTracing(...)`, `AddSource`, the console exporter.
- **OpenTelemetry .NET getting-started for ASP.NET Core** — <https://opentelemetry.io/docs/languages/net/getting-started/>. The shortest path to a traced app.
- **`Activity` and `ActivitySource` in .NET** — <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>. The framework API the `Workshop.Api` `ActivitySource` builds on; how the `Enroll` span parents the SQL span.

## Docker images the milestone uses

- **`mcr.microsoft.com/dotnet/aspnet:9.0` image** — <https://hub.docker.com/r/microsoft/dotnet-aspnet>. The runtime base for the Week-15 Dockerfile (referenced now, used then).
- **`mcr.microsoft.com/dotnet/sdk:9.0` image** — <https://hub.docker.com/r/microsoft/dotnet-sdk>. The build-stage base.
- **`postgres:16` image** — <https://hub.docker.com/_/postgres>. The database Testcontainers spins and the local dev container.

## Adjacent reading — strongly recommended

The lectures cite these by name; reading them up front pays for itself across all three capstone weeks.

- **The Buf breaking-change detector** — <https://buf.build/docs/breaking/overview>. Automated detection of wire-incompatible `.proto` changes — the Challenge-1 lesson, enforced in CI.
- **The `dotnet/aspnetcore` repository** — <https://github.com/dotnet/aspnetcore>. Source-link from any framework type; `Grpc.AspNetCore`, `WebApplicationFactory`, the gRPC middleware.
- **The `dotnet/efcore` repository** — <https://github.com/dotnet/efcore>. The EF Core source; read it when the SQL the provider emits surprises you.
- **W3C Trace Context** — <https://www.w3.org/TR/trace-context/>. The `traceparent` header standard that makes the trace cross the gRPC frame; the foundation for the multi-service tracing of Week 14.
- **David Fowler's "Async guidance"** — <https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md>. The async-mistake catalog; relevant the moment the slice fans out.

## Books — read after the week, not during

- **"The Pragmatic Programmer" — Hunt & Thomas.** The "tracer bullet" chapter is the most accurate description of the vertical-slice discipline this week is built on.
- **"Modern Software Engineering" — David Farley.** The chapters on incremental delivery and observability map directly onto the build-milestone philosophy.
- **"gRPC: Up and Running" — Kasun Indrasiri & Danesh Kuruppu (O'Reilly).** The trade-press treatment of contracts, codegen, and the four call types; deeper than the docs on the *why*.

## Bookmarks worth saving for the rest of the capstone

- The Protocol Buffers proto3 guide.
- The gRPC for .NET documentation tree.
- The Npgsql EF Core provider docs.
- Testcontainers for .NET.
- The GitHub Actions .NET guide.
- The `dotnet/aspnetcore` repository (for source-link).

By Friday you should have all six open in pinned tabs. Weeks 14 (harden) and 15 (deploy) extend the *same* repo against the *same* contract; the time saved by not re-googling these is real, and it compounds across three weeks.
