# Week 14 Resources — Capstone Harden

This is the canonical reading list for Week 14. Every URL has been opened, every package is referenced by the lectures, exercises, challenges, or the harden milestone. Read what you need when you need it; the lecture notes will tell you which section of which document is load-bearing for the technique under discussion.

The list is grouped by the role the document plays in hardening the Polyglot Workshop — the OWASP catalogue, tenant-aware authorization and JWT, mass-assignment defense, MediatR, AutoMapper, Polly resilience, the outbox and background services, observability (OpenTelemetry + Grafana/Loki/Tempo/Prometheus), BenchmarkDotNet, integration testing, and adjacent reading. The "adjacent" section is the most valuable for the team member who wants to outgrow the lectures; do not skip it because it sits last on the page.

## The threat catalogue — OWASP API Security Top 10 (2023)

The single document the whole week is organized around. Read the top-level list, then the three entries the capstone fixes most directly.

- **OWASP API Security Top 10 (2023) — the list** — <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>. The taxonomy of API failures; read it as a checklist against your endpoints.
- **API1:2023 Broken Object Level Authorization** — <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>. The most damaging entry; the one the capstone closes with a tenant `Where` and an EF global query filter.
- **API2:2023 Broken Authentication** — <https://owasp.org/API-Security/editions/2023/en/0xa2-broken-authentication/>. Maps to the hardened JWT bearer registration.
- **API3:2023 Broken Object Property Level Authorization** — <https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>. Mass assignment and excessive data exposure; the request-DTO and `ToDto()` defense.
- **API4:2023 Unrestricted Resource Consumption** — <https://owasp.org/API-Security/editions/2023/en/0xa4-unrestricted-resource-consumption/>. Paging and rate limiting.
- **API5:2023 Broken Function Level Authorization** — <https://owasp.org/API-Security/editions/2023/en/0xa5-broken-function-level-authorization/>. Role policies on instructor-only paths.
- **API9:2023 Improper Inventory Management** — <https://owasp.org/API-Security/editions/2023/en/0xa9-improper-inventory-management/>. Why `/dev/mint-token` must be compiled out of production.

## Tenant-aware authorization and JWT

- **EF Core global query filters** — <https://learn.microsoft.com/en-us/ef/core/querying/filters>. The structural BOLA fix; read the caveats on `IgnoreQueryFilters()` and pooling, both of which the capstone relies on.
- **EF Core security guidance** — <https://learn.microsoft.com/en-us/ef/core/miscellaneous/security>. Multi-tenant pitfalls and the SQL-injection-safe parameterization the analytics query depends on.
- **Configure JWT bearer authentication** — <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>. Every `Validate*` flag and `ClockSkew`; the hardened registration in Lecture 1.
- **Policy-based authorization** — <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies>. `RequireRole`, `RequireClaim`, the `InstructorOnly` and `RequireTenant` policies.
- **gRPC authentication and authorization** — <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>. Why one auth model covers both REST and gRPC; the `authorization` metadata header.
- **Rate limiting middleware** — <https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit>. The fixed-window per-tenant limiter for API4.
- **Threat modeling (STRIDE)** — <https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>. The STRIDE-per-element framing for `THREAT-MODEL.md`.

## MediatR

- **MediatR repository** — <https://github.com/jbogard/MediatR>. The in-process mediator; `IRequest`, `IRequestHandler`, `Send`.
- **MediatR pipeline behaviors (wiki)** — <https://github.com/jbogard/MediatR/wiki/Behaviors>. The actual value proposition: the cross-cutting wrapper written once. The decisive read for "when does MediatR earn its keep."
- **FluentValidation** — <https://github.com/FluentValidation/FluentValidation>. The validator the `ValidationBehavior` runs; `AbstractValidator<T>`, `RuleFor`.
- **Problem Details (RFC 9457)** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling#problem-details>. The error-body contract a validation failure maps to at the edge.

## AutoMapper

- **AutoMapper repository** — <https://github.com/AutoMapper/AutoMapper>. The mapping library; profiles, `CreateMap`.
- **Configuration validation** — <https://docs.automapper.org/en/stable/Configuration-validation.html>. `AssertConfigurationIsValid()` — the only reason AutoMapper is defensible; a renamed property must fail the build, not a customer.
- **Projection (`ProjectTo`)** — <https://docs.automapper.org/en/stable/Queryable-Extensions.html>. The EF-projection feature, and why a hand-written `.Select(...)` is usually clearer.

## Resilience — Polly

- **Polly repository** — <https://github.com/App-vNext/Polly>. The resilience library; v8 resilience pipelines, strategies.
- **Building resilient HTTP apps (.NET)** — <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>. `AddResilienceHandler`, the `Microsoft.Extensions.Http.Resilience` integration that wraps Polly; the timeout → retry → circuit-breaker ordering.
- **Polly resilience strategies docs** — <https://www.pollydocs.org/strategies/>. Per-strategy reference: timeout, retry with jitter, circuit breaker, their options.

## The outbox and background services

- **Worker services in .NET** — <https://learn.microsoft.com/en-us/dotnet/core/extensions/workers>. `BackgroundService`, `ExecuteAsync`, the `OutboxDrainer` shape.
- **Background tasks with hosted services** — <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services>. The ASP.NET Core hosting integration for the drainer.
- **The transactional outbox pattern** — <https://microservices.io/patterns/data/transactional-outbox.html>. The canonical description of writing the domain row and the message row in one transaction.
- **EF Core saving data and transactions** — <https://learn.microsoft.com/en-us/ef/core/saving/transactions>. How the two inserts commit atomically.

## Observability — OpenTelemetry, Serilog, and the Grafana stack

- **OpenTelemetry observability primer** — <https://opentelemetry.io/docs/concepts/observability-primer/>. Why three pillars, and what each answers.
- **OpenTelemetry .NET SDK** — <https://github.com/open-telemetry/opentelemetry-dotnet>. `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`, the OTLP exporter.
- **OpenTelemetry .NET metrics & exemplars** — <https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md#exemplars>. How an exemplar is attached when you record inside a span.
- **Exemplar data model (spec)** — <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>. The wire-level definition of an exemplar.
- **.NET metrics instrumentation** — <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>. The `System.Diagnostics.Metrics.Meter` API; `Counter`, `Histogram`.
- **Distributed tracing in .NET** — <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>. `Activity`, `ActivitySource`, `Activity.Current`.
- **Serilog** — <https://github.com/serilog/serilog>. The structured logger; message templates as the contract.
- **Serilog OpenTelemetry sink** — <https://github.com/serilog/serilog-sinks-opentelemetry>. Routing Serilog log lines to the OTLP collector and on to Loki.
- **Serilog span enricher** — <https://github.com/RehanSaeed/Serilog.Enrichers.Span>. `Enrich.WithSpan()` — puts the trace id on every log line so trace-to-logs works.
- **OpenTelemetry sampling** — <https://opentelemetry.io/docs/concepts/sampling/>. Why you sample traces but keep all metrics, and why exemplars survive sampling.
- **Grafana exemplars** — <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>. The "click the spike, see the trace" feature.
- **Grafana Tempo datasource (trace-to-logs)** — <https://grafana.com/docs/grafana/latest/datasources/tempo/configure-tempo-data-source/>. The "logs for this span" jump to Loki.
- **Grafana Tempo** — <https://grafana.com/docs/tempo/latest/>. The trace backend.
- **Grafana Loki** — <https://grafana.com/docs/loki/latest/>. The log backend.
- **Grafana provisioning** — <https://grafana.com/docs/grafana/latest/administration/provisioning/>. Checked-in datasources and dashboards.

## Performance — BenchmarkDotNet and Dapper

- **BenchmarkDotNet** — <https://github.com/dotnet/BenchmarkDotNet>. The micro-benchmark library; `[MemoryDiagnoser]`, the regression gate.
- **BenchmarkDotNet docs** — <https://benchmarkdotnet.org/articles/overview.html>. Reading the summary table, statistical rigor, why you trust the mean.
- **Dapper** — <https://github.com/DapperLib/Dapper>. The micro-ORM behind the analytics hot path.

## Integration testing

- **Integration tests in ASP.NET Core** — <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. `WebApplicationFactory<Program>`, the in-memory `TestServer`, the `public partial class Program` requirement.
- **Testcontainers for .NET** — <https://github.com/testcontainers/testcontainers-dotnet>. Ephemeral PostgreSQL and Keycloak containers per test class.
- **Testcontainers .NET documentation** — <https://dotnet.testcontainers.org/>. The module catalogue and the `PostgreSqlContainer` / Keycloak module usage.
- **xUnit** — <https://xunit.net/>. The test framework; `IClassFixture`, `ICollectionFixture` for shared context.
- **FluentAssertions** — <https://fluentassertions.com/>. The `.Should().Be(...)` assertion style used in the test examples.

## Adjacent reading — for the team member who wants to outgrow the lectures

- **PostgreSQL row-level security** — <https://www.postgresql.org/docs/current/ddl-rowsecurity.html>. Defense-in-depth tenant isolation below the EF filter (Challenge 1 stretch).
- **Npgsql EF Core provider** — <https://github.com/npgsql/efcore.pg>. The provider behind the transactional store; the OpenTelemetry hook the analytics span uses.
- **W3C Trace Context** — <https://www.w3.org/TR/trace-context/>. The `traceparent` header that propagates the trace across REST and gRPC.
- **Keycloak documentation** — <https://www.keycloak.org/documentation>. The OIDC provider issuing the capstone's tokens; realms, clients, and the `tenant_id` claim mapper.
- **Grafana alerting** — <https://grafana.com/docs/grafana/latest/alerting/>. Trace-linked alerts (Challenge 2 stretch).
- **.NET 9 release notes** — <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/overview>. What changed from .NET 8: `Guid.CreateVersion7`, performance, the resilience integration. The capstone targets `9.0.x`.
