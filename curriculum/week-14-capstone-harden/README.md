# Week 14 — Capstone Harden: Threat-Modeling the API Boundary, the OWASP API Top 10 in .NET, MediatR and AutoMapper Where They Earn Their Keep, and Logs-Metrics-Traces Flowing to a Local Grafana Stack

Welcome to **C9 · Crunch Sharp**, Week 14. Last week — the capstone *build* milestone — you stood up the Polyglot Workshop: the shared gRPC contract (`Workshop.Contracts`, package `workshop.v1`) compiled, the backend (`Workshop.Api`) booted, the EF Core data layer migrated against PostgreSQL, the first client connected, and the Testcontainers baseline went green. The system *exists*. This week we make it *hold up*. The capstone is one coherent project across three weeks — build (Week 13), harden (this week), deploy (Week 15) — and Week 14 extends the **same `PolyglotWorkshop` repository** you already have. You will not invent a new project; you will edit the one you built, because the theme of the week is exactly that: **hardening is editing. We delete more than we add.**

The first thing to internalize is that **the most damaging API vulnerabilities are missing checks, not missing features**. The endpoint that lists submissions works; it just forgot to filter by the caller's tenant. The handler that fetches a review by id works; it just trusted the id in the URL. The OWASP API Security Top 10 (2023) is, read honestly, a taxonomy of skipped authorization checks, and we walk it against the capstone's real endpoints. The canonical catalogue is at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>.

The second thing to internalize is that **broken object-level authorization (API1) is the one that matters most, and its fix is a deletion, not an addition**. We close cross-tenant data leaks with a `Where` clause and, structurally, an EF Core **global query filter** that scopes every tenant-owned read — one filter that deletes a whole class of forgettable per-handler checks. The query-filter reference is <https://learn.microsoft.com/en-us/ef/core/querying/filters>, and the BOLA entry is <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.

The third thing to internalize is that **MediatR and AutoMapper are liabilities until they earn their place**. We add MediatR only where a pipeline behavior — validation, transaction, telemetry — applies to many handlers at once, and we delete it from probes and trivial lookups. We keep exactly one AutoMapper profile (a wide, symmetric `Lesson -> LessonDto`) and skip it everywhere a map carries logic or is a security boundary, preferring `ToDto()` and EF `Select`. MediatR is at <https://github.com/jbogard/MediatR>; AutoMapper at <https://github.com/AutoMapper/AutoMapper>.

The fourth thing to internalize is that **a service you can only debug with a debugger is a service you cannot operate**. We stand up a local Grafana + Loki + Tempo + Prometheus stack via docker-compose, push logs, metrics, and traces over a single OTLP stream, and wire an **exemplar** so a latency spike in a Grafana histogram links — in one click — to the exact trace in Tempo that caused it, and from there to the logs in Loki. The exemplar spec is at <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars> and Grafana exemplars at <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>.

The fifth thing to internalize is that **a hardened service does not trust its dependencies or couple a user's success to a downstream's uptime**. We wrap outbound calls in a Polly v8 resilience pipeline (timeout → retry → circuit breaker) and move the SignalR/notification broadcast behind a transactional **outbox** drained by a background worker, so a slow downstream degrades a feature instead of cascading into an outage. Polly is at <https://github.com/App-vNext/Polly> and the HTTP resilience integration at <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>.

The sixth thing to internalize is that **a privileged path without a test proving the unauthorized case is rejected is not hardened — it is hoping**. The milestone's auth surface must be fully covered by integration tests (Week 12's `WebApplicationFactory` + Testcontainers pattern), including the cross-tenant rejection: org A's token must get a `404`, never org B's row. We also add one BenchmarkDotNet regression test on a hot path (the Dapper analytics query) so a performance regression fails CI like any other bug. BenchmarkDotNet is at <https://github.com/dotnet/BenchmarkDotNet>; the test harness reference is <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.

By the end of the week you will be the person on your team who can take a service that "works" and make it one you can defend in a threat review, operate from a dashboard at 3am, and prove correct with a test suite that fails the moment a tenant boundary leaks.

## Learning objectives

By the end of this week, you will be able to:

- **Threat-model** the API boundary of `Workshop.Api` with a STRIDE-per-element pass, producing a written table that names a mitigation and a test for every arrow crossing into the process. Cite <https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>.
- **Close** broken object-level authorization (API1) with per-handler tenant scoping and an EF Core global query filter, proving a cross-tenant read returns `404`. Cite <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.
- **Harden** JWT bearer validation against Keycloak — every `Validate*` on, tightened `ClockSkew` — and apply role and tenant policies (API2, API5). Cite <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>.
- **Defend** against mass assignment and excessive data exposure (API3) with request DTOs and `ToDto()` projections that are the security boundary. Cite <https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>.
- **Introduce** MediatR pipeline behaviors (validation, observability, transaction) where they earn their keep, and **delete** the mediator from paths where no behavior applies. Cite <https://github.com/jbogard/MediatR/wiki/Behaviors>.
- **Decide** when AutoMapper pays (wide symmetric maps, `AssertConfigurationIsValid`) and when to skip it (logic, security boundaries, EF projection). Cite <https://docs.automapper.org/en/stable/Configuration-validation.html>.
- **Wrap** outbound calls in a Polly v8 resilience pipeline and move broadcasts behind a transactional outbox drained by a `BackgroundService`. Cite <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>.
- **Instrument** the service with OpenTelemetry logs, metrics, and traces over OTLP to a local Grafana + Loki + Tempo + Prometheus stack. Cite <https://opentelemetry.io/docs/concepts/observability-primer/>.
- **Wire** an exemplar by recording a histogram inside an active span, then click from a Grafana metric spike to the Tempo trace to the Loki logs. Cite <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>.
- **Benchmark** a hot path with BenchmarkDotNet and turn it into a regression gate that fails CI on a slowdown. Cite <https://github.com/dotnet/BenchmarkDotNet>.
- **Cover** every privileged path with an integration test that proves the unauthorized and cross-tenant cases are rejected. Cite <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.

## Standards this week meets

| Bar | What this week is measured against |
| --- | --- |
| University | `COP 3337` — Past the outcome set: a second programming course grades whether the program works, not whether it holds when somebody attacks it. What transfers is the invariant discipline applied here — state the rule, then make the type system and the tests enforce it. |
| Industry | Prove a boundary leaks, close it at the structural layer so it cannot be reintroduced by the next feature, and leave behind the test that catches the regression. |
| Beyond the bar | A latency spike on a dashboard that links straight to the one trace that caused it, through an OpenTelemetry exemplar — `challenges/challenge-02-exemplar-spike-to-trace.md` |

## Prerequisites

- **Weeks 6, 12, and 13 of C9 complete.** Week 6 (auth and Identity) is the foundation for the OIDC/JWT work; Week 12 (production-grade integration) is the `WebApplicationFactory` + Testcontainers + OpenTelemetry foundation; Week 13 (the capstone *build* milestone) is the literal repository you extend. The other weeks (2-5, 7-11) are assumed.
- **A working `dotnet --version` of `9.0.x`.** The capstone is the deliberate step up from the .NET 8 LTS of Weeks 11-12: ASP.NET Core 9, EF Core 9, C# 13. Verify with `dotnet --version` printing `9.0.x`.
- **Docker.** Required for the Testcontainers integration tests, the Keycloak OIDC provider, and the `docker-compose.observability.yml` stack (Grafana, Loki, Tempo, Prometheus, OTel collector). The daemon must be running before `dotnet test`.
- **The `PolyglotWorkshop` repo from Milestone 1 (Week 13).** This week edits it in place. If your Milestone 1 is incomplete, finish it first — there is nothing to harden otherwise.
- **`grpcurl`, `jq`, and a browser.** `grpcurl` for the gRPC auth checks, `jq` for the structured log output, a browser for the Grafana UI at `http://localhost:3000`.
- **EF Core CLI 9.** `dotnet tool install --global dotnet-ef --version 9.0.0`; verify `dotnet ef --version` prints `9.0.x`.

## Topics covered

- **Threat modeling the boundary.** Drawing the trust boundary; STRIDE-per-element; turning each mitigation into a test you owe the milestone.
- **The OWASP API Security Top 10 (2023) in .NET.** API1 (BOLA) via EF global query filters; API2 (auth) via hardened JWT bearer; API3 (property-level) via request DTOs; API4 (resource consumption) via rate limiting and paging; API5 (function-level) via role policies; API8-API10 (misconfiguration, inventory, unsafe consumption) as the operational tail.
- **Tenant-aware authorization.** The three-layer model — token, `ITenantContext`, EF global query filter — and why it holds on both REST and gRPC.
- **MediatR where it earns its keep.** `IPipelineBehavior` for validation, observability, and transactions over many handlers; the test for when to *delete* the mediator.
- **AutoMapper deliberately.** The one wide symmetric map worth a profile; `AssertConfigurationIsValid`; the three reasons to skip it everywhere else.
- **Resilience with Polly v8.** Timeout, jittered retry, circuit breaker on the outbound typed `HttpClient`; failing fast instead of cascading.
- **The transactional outbox.** Writing the domain row and the outbox row in one transaction; a `BackgroundService` drainer; decoupling the broadcast trace from the request trace.
- **Observability: logs, metrics, traces.** The local Grafana + Loki + Tempo + Prometheus stack; one OTLP exporter for all three signals; the `Meter` API for custom metrics; the exemplar that links a metric spike to a trace.
- **Performance regression gating.** A BenchmarkDotNet benchmark on the Dapper analytics query, turned into a CI gate.
- **Auth coverage as a test contract.** Every privileged path proven to reject the unauthorized and cross-tenant cases via `WebApplicationFactory<Program>` + Testcontainers.

## Weekly schedule

The schedule adds up to approximately **34 hours**. Treat it as a target, not a contract. The threat-modeling and observability material rewards an unhurried mind; do not skim the exemplar workflow, walk it click by click.

| Day       | Focus                                                                  | Lectures | Exercises | Challenges | Quiz/Read | Capstone | Self-Study | Daily Total |
|-----------|------------------------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-----------:|------------:|
| Monday    | Threat modeling, OWASP API Top 10, BOLA, tenant isolation, query filters |    2h    |    1.5h   |     0h     |    0.5h   |    0h    |    0.5h    |     4.5h    |
| Tuesday   | MediatR pipeline behaviors, AutoMapper deliberately, Polly, the outbox  |    2h    |    1.5h   |     0h     |    0.5h   |    0h    |    0.5h    |     4.5h    |
| Wednesday | Observability — logs/metrics/traces to Grafana+Loki+Tempo, exemplars     |    2h    |    1.5h   |     0h     |    0.5h   |    0h    |    0.5h    |     4.5h    |
| Thursday  | Challenges — prove a cross-tenant leak; wire an exemplar to a trace      |    0h    |    0h     |     3h     |    0.5h   |    1h    |    0.5h    |     5h      |
| Friday    | Capstone harden milestone — auth tests, observability stack, MediatR    |    0h    |    0h     |     0h     |    0.5h   |    4h    |    0.5h    |     5h      |
| Saturday  | Milestone polish — outbox, Polly, BenchmarkDotNet regression gate        |    0h    |    0h     |     0h     |    0h     |    5h    |    0h      |     5h      |
| Sunday    | Quiz, threat-model write-up, "what would you observe next" review        |    0h    |    0h     |     0h     |    1h     |   4.5h   |    0h      |     5.5h    |
| **Total** |                                                                        | **6h**   | **4.5h**  | **6h**     | **4h**    | **18.5h**| **3h**     | **34h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | OWASP API Top 10, EF Core query filters, MediatR, AutoMapper, Polly, OpenTelemetry, Grafana/Loki/Tempo, BenchmarkDotNet, Testcontainers |
| [lecture-notes/01-threat-modeling-the-api-boundary.md](./lecture-notes/01-threat-modeling-the-api-boundary.md) | The trust boundary, STRIDE-per-element, the OWASP API Top 10 in .NET, BOLA and the global query filter, tenant-aware authorization |
| [lecture-notes/02-mediatr-automapper-when-they-earn-their-keep.md](./lecture-notes/02-mediatr-automapper-when-they-earn-their-keep.md) | MediatR pipeline behaviors and when to delete them, AutoMapper when it pays and when to skip it, Polly resilience, the transactional outbox |
| [lecture-notes/03-observability-logs-metrics-traces-exemplars.md](./lecture-notes/03-observability-logs-metrics-traces-exemplars.md) | The local Grafana+Loki+Tempo+Prometheus stack, one OTLP exporter, the `Meter` API, the exemplar that links a spike to a trace |
| [exercises/exercise-01-close-the-bola-leak.cs](./exercises/exercise-01-close-the-bola-leak.cs) | Reproduce a cross-tenant submission read, then close it with a tenant `Where` clause and an EF global query filter |
| [exercises/exercise-02-mediatr-pipeline-behavior.cs](./exercises/exercise-02-mediatr-pipeline-behavior.cs) | Route `CreateSubmission` through MediatR with validation + observability behaviors; leave the health probe a direct call |
| [exercises/exercise-03-polly-and-the-outbox.cs](./exercises/exercise-03-polly-and-the-outbox.cs) | Wrap the notification client in a Polly pipeline; move the broadcast behind a transactional outbox + drainer |
| [exercises/exercise-04-exemplar-to-trace.cs](./exercises/exercise-04-exemplar-to-trace.cs) | Record the analytics histogram inside an active span; click from a Grafana spike to the Tempo trace to the Loki logs |
| [exercises/SOLUTIONS.md](./exercises/SOLUTIONS.md) | Annotated solutions for the four exercises, with verification output and the common stumbles |
| [challenges/challenge-01-prove-and-close-the-cross-tenant-leak.md](./challenges/challenge-01-prove-and-close-the-cross-tenant-leak.md) | Demonstrate a BOLA cross-tenant data leak, close it, and add the integration test that catches it forever (OWASP API1) |
| [challenges/challenge-02-exemplar-spike-to-trace.md](./challenges/challenge-02-exemplar-spike-to-trace.md) | Make a latency-histogram spike in Grafana link directly to the offending trace in Tempo via an OpenTelemetry exemplar |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on the OWASP API Top 10, tenant isolation, MediatR, AutoMapper, Polly, the outbox, and exemplars |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | **Capstone Milestone 2 — Harden.** The production-polish brief: auth fully tested, observability stack, MediatR/AutoMapper deliberately, Polly, outbox, one BenchmarkDotNet regression gate |

## The "build succeeded" promise — restated, and a new harden contract

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 438 ms
```

Milestone 1's contract — the build compiles, the contract is shared, the data layer migrates, the baseline tests pass — still holds; this week may not regress it.

For Week 14 we add the **harden contract**: *every privileged path in the Polyglot Workshop has an integration test that proves the unauthorized case is rejected, and every tenant-owned read has a test that proves a cross-tenant read returns `404` and never another tenant's data.* A pull request that adds or changes a privileged path without the rejection test is, by definition, not hardened. "I checked it manually" is not a defense; CI runs the tests, and the tests prove the boundary holds.

We add an **observability contract** too: *a single request to the capstone produces one trace in Tempo, structured logs in Loki carrying the same trace id, and metrics in Prometheus whose latency histogram exposes an exemplar that links back to that trace.* If you cannot click from a metric spike to its cause without attaching a debugger, the milestone is incomplete.

> **Note on packages.** Server side: `Microsoft.AspNetCore.App` (framework reference; no install). Mediator: `MediatR` 12.4.x. Mapping (one profile only): `AutoMapper` 13.0.x, `AutoMapper.Extensions.Microsoft.DependencyInjection` 12.0.x. Validation: `FluentValidation` 11.9.x, `FluentValidation.DependencyInjectionExtensions` 11.9.x. Resilience: `Microsoft.Extensions.Http.Resilience` 9.0.x (wraps `Polly` 8.4.x). Data: `Microsoft.EntityFrameworkCore` 9.0.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.x, `Dapper` 2.1.x for the analytics query. Auth: `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.x. Logging: `Serilog.AspNetCore` latest, `Serilog.Sinks.OpenTelemetry`, `Serilog.Enrichers.Span`, `Serilog.Formatting.Compact`. Telemetry: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.GrpcNetClient`, `OpenTelemetry.Instrumentation.Runtime`, `Npgsql.OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Benchmarks: `BenchmarkDotNet` 0.14.x. Tests: `xunit` 2.9+, `Microsoft.AspNetCore.Mvc.Testing` 9.0.x, `Testcontainers.PostgreSql` 3.10+, `Testcontainers.Keycloak` 3.10+, `FluentAssertions` 6.12+. All free, all open source, all source-linkable to the listed repositories.
