# Week 13 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The Testcontainers for .NET documentation is open. The Serilog and OpenTelemetry repositories are Apache-2.0 / MIT. The `dotnet/aspnetcore` and `grpc/grpc-dotnet` sources on GitHub are public. The Keycloak documentation is free. The GitHub Actions documentation is free. No paywalled material is linked.

## Required reading (work it into your week)

### Vertical-slice delivery and planning

- **Vertical slice architecture (Jimmy Bogard)** — the canonical articulation of building feature-complete thin slices instead of horizontal layers:
  <https://www.jimmybogard.com/vertical-slice-architecture/>
- **The walking skeleton (Cockburn)** — the original "thin end-to-end path on day one" idea the capstone is built on:
  <https://wiki.c2.com/?WalkingSkeleton>
- **ASP.NET Core Minimal APIs overview** — the REST surface of the capstone:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview>

### One contract, three clients (gRPC and gRPC-Web)

- **gRPC services with ASP.NET Core** — the server side, `AddGrpc`, `MapGrpcService<T>`, the `<Protobuf>` MSBuild item:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>
- **Create gRPC services and methods** — message types, service methods, the four call types:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/services>
- **Call gRPC services with the .NET client** — `GrpcChannel`, the generated client, used by the MAUI app:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>
- **gRPC-Web in ASP.NET Core** — why browsers cannot speak native gRPC (HTTP/2 trailers), the server middleware, the `GrpcWebHandler` on the client:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>
- **Code-first vs contract-first gRPC** — why C9 uses contract-first (`.proto` as source of truth):
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/code-first>
- **Protocol Buffers language guide (proto3)** — the syntax of the `workshop.proto` you author:
  <https://protobuf.dev/programming-guides/proto3/>
- **`grpc/grpc-dotnet` repository** — the source for `Grpc.AspNetCore`, `Grpc.Net.Client`, `Grpc.Net.Client.Web`, and the examples:
  <https://github.com/grpc/grpc-dotnet>

### Integration testing with `WebApplicationFactory<T>`

- **Integration tests in ASP.NET Core** — the canonical reference for `WebApplicationFactory<TEntryPoint>`, `ConfigureWebHost`, and `CustomWebApplicationFactory`:
  <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>
- **`WebApplicationFactory<TEntryPoint>` API reference** — the class, `WithWebHostBuilder`, `CreateClient`, the server lifetime:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1>
- **Test ASP.NET Core gRPC services** — calling a gRPC service through the in-memory test server:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/test-services>
- **`Microsoft.AspNetCore.Mvc.Testing` package** — the NuGet package that brings `WebApplicationFactory<T>`:
  <https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing>

### Testcontainers for .NET — the integration substrate of the week

- **Testcontainers for .NET — documentation root** — the philosophy, the `IContainer` API, the module list:
  <https://dotnet.testcontainers.org/>
- **The PostgreSQL module** — `PostgreSqlBuilder`, `GetConnectionString()`, the dynamic port:
  <https://dotnet.testcontainers.org/modules/postgres/>
- **The Keycloak module** — `KeycloakBuilder`, realm import, the base address and issuer:
  <https://dotnet.testcontainers.org/modules/keycloak/>
- **Resource reuse and the Ryuk reaper** — `WithReuse`, the resource-reaper container, the labels:
  <https://dotnet.testcontainers.org/api/resource_reuse/>
- **`testcontainers/testcontainers-dotnet` repository** — the source and the module packages on NuGet:
  <https://github.com/testcontainers/testcontainers-dotnet>
- **EF Core migrations at runtime** — `context.Database.MigrateAsync()` vs `EnsureCreated`, applied against the Testcontainers database before assertions:
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>

### Keycloak as a test dependency

- **Keycloak — securing applications (OIDC)** — the realm, the client, the token endpoint, the issuer:
  <https://www.keycloak.org/docs/latest/securing_apps/>
- **Keycloak — realm import/export** — the `realm.json` you import at container start:
  <https://www.keycloak.org/server/importExport>
- **JWT bearer authentication in ASP.NET Core** — validating the Keycloak-issued token in the backend, the `Authority` and `Audience`:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- **OIDC discovery document** — `/.well-known/openid-configuration` on the Keycloak realm, how the backend finds the signing keys:
  <https://openid.net/specs/openid-connect-discovery-1_0.html>

### Serilog — structured logging

- **`serilog/serilog-aspnetcore` repository** — `UseSerilog`, the request-logging middleware, the README is the canonical setup guide:
  <https://github.com/serilog/serilog-aspnetcore>
- **Serilog — structured data** — message templates vs string formatting, the property-bag model:
  <https://github.com/serilog/serilog/wiki/Structured-Data>
- **Serilog enrichers** — `Enrich.FromLogContext`, `LogContext.PushProperty`, adding request id / tenant id / trace id:
  <https://github.com/serilog/serilog/wiki/Enrichment>
- **`Serilog.Sinks.Console`** — the console sink and the output template used in dev:
  <https://github.com/serilog/serilog-sinks-console>

### OpenTelemetry for .NET — traces and metrics

- **OpenTelemetry .NET — getting started** — `AddOpenTelemetry`, `WithTracing`, `WithMetrics`, the SDK setup:
  <https://opentelemetry.io/docs/languages/net/getting-started/>
- **`.NET` distributed tracing** — `ActivitySource`, `Activity`, how it maps onto OTel spans:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>
- **`.NET` metrics with `System.Diagnostics.Metrics`** — `Meter`, `Counter<T>`, `Histogram<T>`, the OTel metrics bridge:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>
- **OpenTelemetry instrumentation packages** — ASP.NET Core, HttpClient, EF Core, gRPC client instrumentations:
  <https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/src>
- **The OTLP exporter** — `AddOtlpExporter`, the endpoint, gRPC vs HTTP/protobuf transport:
  <https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol>
- **The OpenTelemetry Collector** — the local collector you start with `docker run`; the receivers/exporters config:
  <https://opentelemetry.io/docs/collector/>
- **OpenTelemetry .NET on Microsoft Learn** — the ASP.NET Core observability walkthrough:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>

### CI for the integration baseline

- **GitHub Actions — workflow syntax** — `on`, `jobs`, `steps`, the `runs-on` runner:
  <https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions>
- **`actions/setup-dotnet`** — install the .NET 9 SDK on the runner:
  <https://github.com/actions/setup-dotnet>
- **Testcontainers on CI** — running Testcontainers inside GitHub-hosted runners (Docker is preinstalled on `ubuntu-latest`):
  <https://dotnet.testcontainers.org/api/create_docker_image/>
- **Caching NuGet in Actions** — `actions/cache` keyed on the lock file, to keep the run fast:
  <https://docs.github.com/en/actions/using-workflows/caching-dependencies-to-speed-up-workflows>

### The `dotnet/aspnetcore` and related GitHub source — source link these as you read

- **`dotnet/aspnetcore` repository root**:
  <https://github.com/dotnet/aspnetcore>
- **`WebApplicationFactory.cs`** — the test-host factory; read once to see how the in-memory server is constructed:
  <https://github.com/dotnet/aspnetcore/blob/main/src/Mvc/Mvc.Testing/src/WebApplicationFactory.cs>
- **`Grpc.AspNetCore` service registration** — `AddGrpc`, `MapGrpcService`:
  <https://github.com/grpc/grpc-dotnet/tree/master/src/Grpc.AspNetCore.Server>
- **`Grpc.Net.Client.Web` (`GrpcWebHandler`)** — the browser-side gRPC-Web framing handler:
  <https://github.com/grpc/grpc-dotnet/tree/master/src/Grpc.Net.Client.Web>

## Recommended reading (after the required set)

- **"Growing Object-Oriented Software, Guided by Tests" (Freeman & Pryce)** — the walking-skeleton-first methodology that underlies the whole week; the first three chapters are the relevant ones.
- **Andrew Lock — "Running async tasks on app startup"** — relevant to applying migrations at startup vs in the test fixture:
  <https://andrewlock.net/running-async-tasks-on-app-startup-in-asp-net-core-3/>
- **Martin Thwaites — OpenTelemetry in .NET (talks and posts)** — practical OTel wiring beyond the getting-started guide:
  <https://www.honeycomb.io/blog>
- **"Test Desiderata" (Kent Beck)** — the properties a good test has; the lens for deciding unit vs integration:
  <https://kentbeck.github.io/TestDesiderata/>
- **gRPC performance best practices** — channel reuse, `GrpcChannel` lifetime, relevant when the MAUI and Blazor clients hold a channel:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/performance>

## Tools you will install this week

- **The .NET 9 SDK** — verify with `dotnet --version` (expect `9.0.x`). The whole capstone targets `net9.0`.
- **Docker / Colima / Podman** — Testcontainers needs a reachable Docker socket. Verify with `docker info`. On macOS, Colima (`colima start`) is a fine free alternative to Docker Desktop.
- **`dotnet-ef`** (once per machine): `dotnet tool install --global dotnet-ef`. Verify with `dotnet ef --version`. Used to create and apply migrations.
- **The Testcontainers NuGet modules** (added per test project, not globally): `Testcontainers.PostgreSql` and `Testcontainers.Keycloak`.
- **The OpenTelemetry Collector** (run via Docker for the trace exercises): `docker run --rm -p 4317:4317 -p 4318:4318 otel/opentelemetry-collector:latest`. Verify it accepts OTLP on `4317` (gRPC) and `4318` (HTTP).
- **`grpcurl`** — a command-line gRPC client for poking the service directly: install per your platform from <https://github.com/fullstorydev/grpcurl>. Verify with `grpcurl --version`.
- **The MAUI workloads** — `dotnet workload install maui` so the MAUI client compiles in the solution build.

## Citations policy

This curriculum cites Microsoft Learn URLs, the Testcontainers for .NET documentation, the Serilog and OpenTelemetry repositories, the `dotnet/aspnetcore` and `grpc/grpc-dotnet` GitHub sources, the Keycloak documentation, and the GitHub Actions docs as the primary references. Every example in the lecture notes and exercises traces back to one of these. When a third-party reference (Jimmy Bogard on vertical slices, Andrew Lock on startup tasks, Kent Beck on test properties) is the clearer source, it is cited explicitly with a URL — never paraphrased without attribution. If a citation is missing from a section of these notes, treat it as a bug and open an issue against the C9 curriculum repository.
