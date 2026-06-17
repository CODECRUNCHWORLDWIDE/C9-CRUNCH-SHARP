# Week 14 — Resources

Every resource on this page is **free**. The OWASP material is published under Creative Commons. Microsoft Learn is free without an account. The OpenTelemetry, Grafana, Loki, Tempo, and Prometheus documentation is free. MediatR, AutoMapper, FluentValidation, and Serilog are open-source projects with free documentation. The Docker images for the observability stack are public. No paywalled material is linked.

## Required reading (work it into your week)

### Threat modeling and the OWASP API Security Top 10

- **OWASP API Security Top 10 (2023) — the index** — the ten items, each with a description, an example, and a prevention checklist. This is the spine of the week:
  <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>
- **API1:2023 — Broken Object Level Authorization (BOLA)** — the most common and most damaging API vulnerability; the workshop's primary harden target:
  <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>
- **API2:2023 — Broken Authentication** — token-validation pitfalls, weak credential flows:
  <https://owasp.org/API-Security/editions/2023/en/0xa2-broken-authentication/>
- **API3:2023 — Broken Object Property Level Authorization (BOPLA)** — over-exposure of properties (mass assignment + excessive data exposure merged):
  <https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>
- **API4:2023 — Unrestricted Resource Consumption** — the rate-limiting and pagination-cap item:
  <https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/>
- **API5:2023 — Broken Function Level Authorization (BFLA)** — the role/function gate; "a learner called the instructor-only endpoint":
  <https://owasp.org/API-Security/editions/2023/en/0xa5-broken-function-level-authorization/>
- **OWASP Threat Modeling Cheat Sheet** — the lightweight, repeatable method we use to produce `THREATMODEL.md`:
  <https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html>
- **OWASP REST Security Cheat Sheet** — HTTPS, input validation, output encoding, security headers for the Minimal API surface:
  <https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html>
- **OWASP Authorization Cheat Sheet** — deny-by-default, least privilege, resource-based checks:
  <https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html>
- **STRIDE (Microsoft) — the threat categories** — the per-boundary enumeration we apply to the three doors:
  <https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>

### Authorization in ASP.NET Core

- **Resource-based authorization** — the canonical reference for `IAuthorizationService.AuthorizeAsync(user, resource, requirement)` and `AuthorizationHandler<TRequirement, TResource>`; the load-bearing pattern against BOLA:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>
- **Policy-based authorization** — requirements, handlers, registration; the gate for BFLA:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies>
- **Custom authorization policies and requirements** — multiple handlers per requirement, the OR/AND semantics:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/iauthorizationpolicyprovider>
- **Authorization in Minimal APIs** — `.RequireAuthorization("policy")` on endpoints and route groups:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/secure-data>
- **JWT bearer authentication configuration** — `TokenValidationParameters`, issuer/audience/lifetime validation, clock skew:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- **gRPC authentication and authorization** — `[Authorize]` on gRPC services, the `Unauthenticated` vs `PermissionDenied` status mapping:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>

### Rate limiting (OWASP API4)

- **Rate limiting middleware in ASP.NET Core** — fixed window, sliding window, token bucket, concurrency; partitioning; the `429` with `Retry-After`:
  <https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit>
- **`RateLimiterOptions` reference** — the global limiter, named policies, `OnRejected`:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.ratelimiting.ratelimiteroptions>
- **`System.Threading.RateLimiting` primitives** — the partitioned rate limiter the middleware builds on:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.ratelimiting>

### Integration testing the auth surface

- **Integration tests in ASP.NET Core** — `WebApplicationFactory<TEntryPoint>`, the in-memory test server, overriding services:
  <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>
- **Testcontainers for .NET** — ephemeral PostgreSQL and Keycloak containers for tests that touch real infrastructure:
  <https://dotnet.testcontainers.org/>
- **Testcontainers Keycloak module** — the prebuilt Keycloak container for minting real tokens in tests:
  <https://dotnet.testcontainers.org/modules/keycloak/>
- **Minimal APIs and `WebApplicationFactory`** — the `public partial class Program` convention that makes the entry point referenceable from the test project:
  <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests#basic-tests-with-the-default-webapplicationfactory>

### MediatR

- **MediatR repository and wiki** — `IRequest<T>`, `IRequestHandler<T>`, `INotification`, registration:
  <https://github.com/jbogard/MediatR>
- **MediatR behaviors (pipeline)** — the canonical reference for `IPipelineBehavior<TRequest, TResponse>` and the ordering rules; the heart of the deliberate-MediatR argument:
  <https://github.com/jbogard/MediatR/wiki/Behaviors>
- **Jimmy Bogard — "MediatR is not a mediator"** — the author's own caution against over-using it; read this before you wrap every endpoint:
  <https://www.jimmybogard.com/you-probably-dont-need-mediatr/>
- **Vertical slice architecture** — the design context in which MediatR pipeline behaviors pay off:
  <https://www.jimmybogard.com/vertical-slice-architecture/>

### AutoMapper

- **AutoMapper documentation root** — profiles, configuration, the dependency-injection package:
  <https://docs.automapper.org/en/stable/>
- **Queryable extensions (`ProjectTo`)** — pushing the DTO projection into the SQL `SELECT`; the only AutoMapper use we endorse for hot paths:
  <https://docs.automapper.org/en/stable/Queryable-Extensions.html>
- **Configuration validation (`AssertConfigurationIsValid`)** — the test that fails the build when a mapping is incomplete:
  <https://docs.automapper.org/en/stable/Configuration-validation.html>
- **AutoMapper "usage guidelines" — the author on when *not* to use it** — the explicit case against mapping with logic:
  <https://docs.automapper.org/en/stable/Understanding-your-mapping.html>

### FluentValidation

- **FluentValidation documentation** — `AbstractValidator<T>`, rule chains, the ASP.NET Core integration:
  <https://docs.fluentvalidation.net/en/latest/>
- **FluentValidation with a MediatR pipeline behavior** — the canonical `ValidationBehavior<TRequest, TResponse>` pattern:
  <https://docs.fluentvalidation.net/en/latest/aspnet.html#manual-validation>
- **Problem Details (RFC 9457)** — the standard error shape your `ValidationException` should translate to:
  <https://www.rfc-editor.org/rfc/rfc9457.html>
- **`Microsoft.AspNetCore.Http.ProblemDetails` and `IProblemDetailsService`** — the ASP.NET Core machinery for emitting RFC 9457:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/handle-errors>

### Observability: the .NET 9 OpenTelemetry SDK

- **.NET observability with OpenTelemetry** — the canonical Microsoft Learn overview of logs, metrics, and traces in .NET:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>
- **OpenTelemetry .NET documentation** — the SDK, the exporters, the instrumentation libraries:
  <https://opentelemetry.io/docs/languages/net/>
- **`System.Diagnostics.ActivitySource` and distributed tracing** — the framework type behind every span:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>
- **`System.Diagnostics.Metrics` (`Meter`, `Counter`, `Histogram`)** — the framework metrics API the RED metrics use:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>
- **OpenTelemetry for ASP.NET Core (instrumentation)** — automatic spans for incoming requests:
  <https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.AspNetCore>
- **OpenTelemetry instrumentation for EF Core / Npgsql** — database command spans on the same trace:
  <https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.EntityFrameworkCore>
- **The OTLP exporter** — exporting traces, metrics, and logs over OTLP/gRPC or OTLP/HTTP to the collector:
  <https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol>
- **Exemplars in the OpenTelemetry metrics data model** — the `TraceId`-carrying metric point:
  <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>

### Serilog

- **Serilog for ASP.NET Core** — the request-logging middleware, the JSON sinks, the enrichers:
  <https://github.com/serilog/serilog-aspnetcore>
- **`Serilog.Sinks.OpenTelemetry`** — emit Serilog events over OTLP so they land in Loki via the collector:
  <https://github.com/serilog/serilog-sinks-opentelemetry>
- **Serilog message templates** — why log messages are templates with named properties, never interpolated strings:
  <https://github.com/serilog/serilog/wiki/Structured-Data>

### The Grafana + Loki + Tempo + Prometheus stack

- **The OpenTelemetry Collector** — receivers, processors, exporters; the pipeline that fans the three signals to Loki/Tempo/Prometheus:
  <https://opentelemetry.io/docs/collector/>
- **Grafana documentation** — datasources, dashboards, explore, the trace-to-logs and logs-to-trace correlation:
  <https://grafana.com/docs/grafana/latest/>
- **Grafana Loki** — the log aggregation system; LogQL; the `traceID` label for correlation:
  <https://grafana.com/docs/loki/latest/>
- **Grafana Tempo** — the distributed tracing backend; TraceQL; the trace-to-logs derived field:
  <https://grafana.com/docs/tempo/latest/>
- **Prometheus** — the metrics scraper; PromQL; the `--enable-feature=exemplar-storage` flag for exemplar support:
  <https://prometheus.io/docs/introduction/overview/>
- **The RED method (Rate, Errors, Duration)** — the three metrics every request-driven service should emit:
  <https://grafana.com/blog/2018/08/02/the-red-method-how-to-instrument-your-services/>
- **Grafana's "trace to logs" correlation** — the derived field that turns a `TraceId` in a span into a Loki query link:
  <https://grafana.com/docs/grafana/latest/datasources/tempo/configure-tempo-data-source/#trace-to-logs>

## Recommended reading (after the required set)

- **OWASP API Security Top 10 — the full PDF** — the offline edition with the extended prevention guidance:
  <https://owasp.org/API-Security/editions/2023/en/0x00-header/>
- **Andrew Lock — "Adding rate limiting to ASP.NET Core"** — a clear walkthrough of the four algorithms in practice:
  <https://andrewlock.net/exploring-the-dotnet-8-preview-rate-limiting/>
- **Stephen Toub — performance of the .NET diagnostics primitives** — what `ActivitySource` and `Meter` cost when nobody is listening (answer: nearly nothing):
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
- **".NET Aspire" telemetry conventions** — even if you are not using Aspire, its OTLP conventions are a good template for the resource attributes:
  <https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/telemetry>
- **OWASP Cheat Sheet Series index** — the full catalog; the Logging, JWT, and Mass Assignment sheets are all relevant this week:
  <https://cheatsheetseries.owasp.org/>

## Tools you will install this week

- **Docker** (or `colima` / `podman`) — required for the observability stack and the Testcontainers integration tests. Verify with `docker info`.
- **The observability stack via `docker compose`** — `grafana/grafana`, `grafana/loki`, `grafana/tempo`, `prom/prometheus`, and `otel/opentelemetry-collector-contrib`. The `mini-project/observability/docker-compose.yml` ships the configuration; bring it up with `docker compose -f observability/docker-compose.yml up -d`.
- **`grpcurl`** (optional, for poking the gRPC boundary during threat modeling): `brew install grpcurl` or download from <https://github.com/fullstorydev/grpcurl/releases>. Verify with `grpcurl --version`.
- **The NuGet packages**, added per-project (not globally): `MediatR`, `AutoMapper`, `AutoMapper.Extensions.Microsoft.DependencyInjection`, `FluentValidation.AspNetCore`, `FluentValidation.DependencyInjectionExtensions`, `Serilog.AspNetCore`, `Serilog.Sinks.OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.GrpcNetClient`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Added via `dotnet add package <name>`.

## Citations policy

This curriculum cites the **OWASP API Security Top 10 (2023)** and the **OWASP Cheat Sheet Series** as the primary authorities on the security material, Microsoft Learn for the ASP.NET Core security, authorization, rate-limiting, and integration-testing machinery, the **OpenTelemetry .NET** documentation and the **Grafana / Loki / Tempo / Prometheus** documentation for the observability stack, and the project documentation for **MediatR**, **AutoMapper**, **FluentValidation**, and **Serilog**. Every example in the lecture notes and exercises traces back to one of these. When a third-party author (Jimmy Bogard on MediatR, Andrew Lock on rate limiting) is the clearer reference, it is cited explicitly with a URL — never paraphrased without attribution. If a citation is missing from a section of these notes, treat it as a bug and open an issue against the C9 curriculum repository.
