# Capstone Milestone 2 — Harden the Polyglot Workshop: Auth Proven by Tests, Observability You Can Operate, MediatR and AutoMapper Used Deliberately, Resilience and the Outbox, and One Performance Regression Gate

> **Time:** 18.5 hours across Thursday-Sunday. **Prerequisites:** Milestone 1 (Week 13) complete — the contract, the service, the EF data layer, the first client, and the Testcontainers baseline must already build and pass. This milestone **extends the same `PolyglotWorkshop` repository**; you do not start a new project. It feeds Milestone 3 (Week 15 — deploy: Docker multi-stage, GitHub Actions CI/CD, Azure Container Apps, demo and portfolio). **Citations:** every URL in the three lecture notes, the OWASP API Top 10, the Polly, MediatR, AutoMapper, OpenTelemetry, Grafana, and BenchmarkDotNet references.

## The spec

You built the Polyglot Workshop last week: one deployable system, three clients, one contract. ASP.NET Core 9 backend (`Workshop.Api`) + .NET MAUI mobile (`Workshop.Mobile`) + Blazor admin (`Workshop.Admin`), sharing the single typed gRPC contract (`Workshop.Contracts`, `workshop.v1`). The domain is a workshop/classroom platform — instructors create lessons, learners enroll, both submit and review exercises, and an analytics surface aggregates progress. The backend uses EF Core (PostgreSQL) for the transactional store and Dapper for the analytics query, ASP.NET Identity + OIDC via Keycloak, SignalR live presence, background workers, Serilog + OpenTelemetry.

Milestone 1 made it *exist*. Milestone 2 makes it *production-polished*: hardened against the OWASP API Top 10, observable from a local Grafana stack, resilient to downstream failure, and proven correct by a test suite that fails the moment a tenant boundary leaks. The theme is the week's slogan — **hardening is editing; we delete more than we add.** Most of your work this milestone removes a trust assumption or a reflexive abstraction, not adds a subsystem.

The observability topology you will stand up:

```
   Workshop.Api (.NET 9)
        | OTLP (gRPC :4317)
        v
   +------------------+
   |  otel-collector  |   (one stream in, fan out)
   +---+----+-----+---+
       |    |     |
   logs| metrics| traces
       v    v     v
   +------+ +----------+ +-------+
   | Loki | |Prometheus| | Tempo |
   +---+--+ +----+-----+ +---+---+
       |         |           |
       +----+----+-----+-----+
            v          v
         +--------------------+
         |     Grafana        |   <-- exemplars (Prometheus -> Tempo),
         |  (one pane of glass)|       trace-to-logs (Tempo -> Loki)
         +--------------------+
                  ^
                  |  metric spike -> click exemplar -> trace -> "logs for span"
               you, on call
```

Everything runs via `docker compose -f docker-compose.observability.yml up`. One OTLP stream from the app fans out to three backends; Grafana correlates all three so a latency spike links in one click to the trace that caused it and from there to the logs.

## Milestone requirements

### M1 — Threat model, written

- A `THREAT-MODEL.md` with the trust-boundary diagram for `Workshop.Api` and a STRIDE-per-element table for every endpoint group, where each row names a mitigation **and** the integration test that proves it. Cite <https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>.

### M2 — Auth fully covered by integration tests

- Every privileged path (REST and gRPC) has an integration test proving the **unauthorized** case is rejected: no token → `401`/`Unauthenticated`, wrong role → `403`/`PermissionDenied`.
- Every tenant-owned read has a test proving the **cross-tenant** case returns `404`/`NOT_FOUND` and never another tenant's data (OWASP API1). The tenant isolation is structural — an EF Core global query filter on `Submission`, `Review`, `Enrollment` — not per-handler-only.
- Tests run against `WebApplicationFactory<Program>` with `Testcontainers.PostgreSql` and `Testcontainers.Keycloak`. Cite <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests> and <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.

### M3 — Logs, metrics, and traces to a local Grafana stack

- `docker-compose.observability.yml` runs Grafana, Loki, Tempo, Prometheus, and an OTel collector. Prometheus runs with `--enable-feature=exemplar-storage`.
- `Workshop.Api` exports logs (Serilog → OTLP → Loki, enriched with the trace id), metrics (`Meter` API → OTLP → Prometheus), and traces (→ OTLP → Tempo) over one OTLP stream.
- A provisioned, checked-in Grafana dashboard with request-rate, error-rate, the analytics latency histogram **with exemplars**, a Loki logs panel, and a circuit-breaker state panel. Cite <https://opentelemetry.io/docs/concepts/observability-primer/> and <https://grafana.com/docs/grafana/latest/administration/provisioning/>.

### M4 — The exemplar

- The analytics-latency histogram records inside an active span so each data point carries an exemplar; clicking a spike in Grafana opens the offending trace in Tempo and the "logs for this span" jump reaches Loki. Cite <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>.

### M5 — MediatR where it earns its keep

- Commands and heavy queries route through MediatR with a `ValidationBehavior` (FluentValidation) and an `ObservabilityBehavior`; commands also get a transaction behavior.
- Probes, trivial lookups, and gRPC pass-throughs are **direct calls** — no MediatR types. The PR description names which paths use the mediator and why, and which were deliberately left direct. Cite <https://github.com/jbogard/MediatR/wiki/Behaviors>.

### M6 — AutoMapper only where warranted

- Exactly one AutoMapper profile (the wide, symmetric `Lesson -> LessonDto`), validated at startup with `AssertConfigurationIsValid()`. Every other DTO mapping is a hand-written `ToDto()` or an EF `Select` projection. The PR explains the one map kept and the rest deleted. Cite <https://docs.automapper.org/en/stable/Configuration-validation.html>.

### M7 — Resilience with Polly

- The outbound `NotificationClient` is wrapped in a Polly v8 resilience pipeline (timeout → retry with jitter → circuit breaker). A fault-injection test proves retries recover from transient `503`s and the breaker fails fast when the downstream is down. Cite <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>.

### M8 — The transactional outbox

- The `SubmissionCreated` broadcast is written to an `outbox_messages` row in the same transaction as the domain row; an `OutboxDrainer` `BackgroundService` drains it (using `IgnoreQueryFilters()`), Polly-wrapped. A test proves the submission succeeds even when the downstream is down. Cite <https://learn.microsoft.com/en-us/dotnet/core/extensions/workers>.

### M9 — One BenchmarkDotNet regression gate

- A `Workshop.Benchmarks` project benchmarks the Dapper analytics hot path; a CI step runs it and fails if the mean exceeds a committed threshold (a regression gate, not just a report). Cite <https://github.com/dotnet/BenchmarkDotNet>.

## Suggested layout (additions to the Milestone 1 repo)

```
PolyglotWorkshop/                       <-- the SAME repo from Milestone 1
├── docker-compose.observability.yml    <-- NEW: grafana, loki, tempo, prometheus, otel-collector
├── observability/
│   ├── otel/config.yaml
│   ├── tempo/tempo.yaml
│   ├── prometheus/prometheus.yml
│   └── grafana/provisioning/{datasources,dashboards}/
├── THREAT-MODEL.md                     <-- NEW (M1)
├── src/
│   ├── Workshop.Api/
│   │   ├── Authorization/{TenantContext.cs,Policies.cs}      <-- API1/API5
│   │   ├── Mediator/{Behaviors,Commands,Queries}/           <-- M5
│   │   ├── Mapping/LessonMappingProfile.cs                  <-- M6 (the one profile)
│   │   ├── Resilience/NotificationClient.cs                 <-- M7
│   │   ├── Outbox/{OutboxMessage.cs,OutboxDrainer.cs}       <-- M8
│   │   ├── Observability/WorkshopMetrics.cs                 <-- M3/M4
│   │   └── Data/ (Submission, Review, Enrollment + query filters)
│   ├── Workshop.Contracts/  (workshop.v1, unchanged)
│   ├── Workshop.Mobile/     (MAUI, unchanged this milestone)
│   └── Workshop.Admin/      (Blazor, unchanged this milestone)
├── tests/
│   └── Workshop.IntegrationTests/      <-- M2 auth + cross-tenant tests added
└── benchmarks/
    └── Workshop.Benchmarks/            <-- NEW (M9)
```

## The harden write-up (`HARDEN.md`)

Treat this as part of the deliverable, not an afterthought. Capture:

### H1 — The cross-tenant proof
The failing-then-passing cross-tenant test for `GetSubmission` (REST and gRPC), with output.

### H2 — The single trace, the single log, the single metric
One `POST /api/submissions`: the Tempo trace, the Loki log lines sharing its trace id, and the Prometheus counter increment. Paste the trace id appearing in all three.

### H3 — The exemplar click-through
The Grafana screenshot: spike → exemplar → Tempo trace → "logs for span" → Loki. (Challenge 2's artifact, in the milestone.)

### H4 — Resilience under fault injection
The fault-injection test output: retries recovering from `503`, then the breaker opening and failing fast.

### H5 — The outbox decoupling
Proof that a submission returns `201` while the notification downstream is down, the outbox row sits `processed_at = NULL`, and the drainer completes it once the downstream returns — on a *separate* trace.

### H6 — The benchmark gate
The BenchmarkDotNet summary for the analytics query, the committed threshold, and the CI step that fails on regression.

### H7 — What we deleted
A short list of what hardening *removed*: the per-handler tenant checks the global filter replaced, the MediatR wrappers deleted from probes, the AutoMapper profiles deleted in favor of `ToDto()`, the inline broadcast moved to the outbox. The slogan, demonstrated.

## Acceptance criteria / definition of done

- `dotnet build` is clean (0 warnings, 0 errors) and Milestone 1's tests still pass.
- Every privileged REST and gRPC path has a `401`/`Unauthenticated` (no token) and `403`/`PermissionDenied` (wrong role) test.
- Every tenant-owned read has a cross-tenant `404`/`NOT_FOUND` test that fails on the un-hardened branch and passes on the hardened one.
- `docker compose -f docker-compose.observability.yml up` brings up the full stack; one request produces one trace id across Tempo, Loki, and Prometheus.
- Clicking a latency-histogram spike in Grafana reaches the trace and the logs.
- MediatR is used only where a behavior applies; AutoMapper is one profile; both choices are justified in the PR.
- The Polly pipeline and the outbox are tested under fault injection.
- The BenchmarkDotNet gate runs in CI and fails on a deliberate regression.

## Grading rubric (100 points)

- **25 points — auth coverage (M1, M2).** Threat model written; every privileged path and every tenant-owned read has the rejection/cross-tenant test; isolation is structural.
- **20 points — observability (M3, M4).** Stack runs; one request, one trace id across three signals; the exemplar click-through works end to end.
- **15 points — MediatR and AutoMapper, deliberately (M5, M6).** Behaviors where they earn their keep; one justified map; the deletions documented.
- **15 points — resilience and the outbox (M7, M8).** Polly pipeline ordered correctly and tested; outbox atomic and drained; broadcast decoupled.
- **10 points — the benchmark gate (M9).** Benchmark on the hot path; CI fails on regression.
- **10 points — the harden write-up (HARDEN.md).** All seven sections (H1-H7) with captured evidence.
- **5 points — code quality.** Nullable enabled, file-scoped namespaces, no scoped-from-singleton captures, citations on non-trivial choices.

## Stretch goals

1. **Row-level security.** Add PostgreSQL RLS keyed on a per-request session variable so even a raw `SELECT` cannot cross tenants — defense in depth under the EF filter. Cite <https://www.postgresql.org/docs/current/ddl-rowsecurity.html>.
2. **Trace-based alerting.** A Prometheus alert on analytics p99 whose annotation links straight to the exemplar trace. Cite <https://grafana.com/docs/grafana/latest/alerting/>.
3. **Second auth scheme for service-to-service.** A dedicated scheme for internal callers on one privileged gRPC method, with a test proving a learner token is rejected there. This previews Milestone 3's deploy-time service identity. Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>.
4. **Benchmark the mapping choice.** Use BenchmarkDotNet to compare the one AutoMapper profile against a hand-written `ToDto()` and report the allocation/throughput delta — evidence for the editing thesis. Cite <https://github.com/dotnet/BenchmarkDotNet>.

## Submission

Push the hardened work on a branch named `week14-capstone-harden/<your-handle>` against the `PolyglotWorkshop` repository and open a PR. The PR description must link to `THREAT-MODEL.md` and `HARDEN.md`, include the exemplar click-through screenshot (H3), the cross-tenant failing-then-passing output (H1), and the BenchmarkDotNet gate summary (H6), and name explicitly which paths use MediatR and which AutoMapper profile was kept.

The teaching staff reviews milestone PRs within 7 business days. Reviews focus on (a) whether every privileged path has a rejection test and every tenant read a cross-tenant test, (b) whether one request produces one trace id across three signals with a working exemplar, (c) whether MediatR and AutoMapper were used deliberately and the deletions documented, and (d) whether the code reads like the editorial style of the lecture-note examples. Remember the harden contract: a privileged path without a test that proves the unauthorized case is rejected is not hardened — it is hoping. This milestone hands a deployable, observable, defensible service to Milestone 3, where Week 15 takes it to Azure Container Apps.

Cited Microsoft Learn pages: the integration-test chapter at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, EF Core query filters at <https://learn.microsoft.com/en-us/ef/core/querying/filters>, JWT bearer at <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>, HTTP resilience at <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>, and worker services at <https://learn.microsoft.com/en-us/dotnet/core/extensions/workers>. External: OWASP API Top 10 at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>, MediatR at <https://github.com/jbogard/MediatR>, AutoMapper at <https://github.com/AutoMapper/AutoMapper>, Polly at <https://github.com/App-vNext/Polly>, OpenTelemetry .NET at <https://github.com/open-telemetry/opentelemetry-dotnet>, BenchmarkDotNet at <https://github.com/dotnet/BenchmarkDotNet>, and Testcontainers for .NET at <https://github.com/testcontainers/testcontainers-dotnet>.
