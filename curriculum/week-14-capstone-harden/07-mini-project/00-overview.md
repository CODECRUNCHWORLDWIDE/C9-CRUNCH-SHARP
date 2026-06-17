# Capstone Milestone — Production Polish: Hardening the Polyglot Workshop

> **Time:** 7.5 hours across Thursday-Friday-Saturday-Sunday. **Prerequisites:** the Week 13 integration baseline (green), all four exercises, and ideally both challenges. **Citations:** every URL referenced in the three lecture notes — the OWASP API Top 10, the ASP.NET Core security/authorization/rate-limiting docs, the MediatR and AutoMapper docs, the OpenTelemetry .NET docs, and the Grafana/Loki/Tempo docs.

This is **not** a toy mini-project. It is the **Week 14 capstone milestone**: you take the Polyglot Workshop you built to "it works" in Week 13 and drive it to "it is trustworthy and operable." The deliverable is the same system, hardened — and the milestone gate is binary. Do not start a new project; harden the one you have.

## The system (recap)

The **Polyglot Workshop** is one deployable system with three clients over one contract: an **ASP.NET Core 9 backend** (Minimal APIs + a gRPC service mirroring the domain, EF Core/PostgreSQL, Dapper analytics, ASP.NET Identity + OIDC via Keycloak, SignalR presence, background workers with an outbox, Polly, Serilog + OpenTelemetry); a **.NET MAUI client**; and a **Blazor admin** (Auto render mode, MudBlazor, gRPC-Web). The domain is a workshop/classroom platform: instructors create lessons, learners enroll and submit, analytics aggregate progress.

This week you do not add a client or a feature. You harden the backend boundary that all three clients depend on.

## The milestone gate — five binary checks

The milestone is **Production Polish**, and it is done or it is not:

### 1. The auth surface is fully covered by integration tests

Every authenticated boundary — every HTTP endpoint, every gRPC method, the SignalR hub — has an integration test that proves **both** the allow path and the deny path, against a **real Testcontainers Keycloak**:

- Anonymous → 401 (HTTP) / `Unauthenticated` (gRPC).
- Wrong role → 403 (BFLA).
- Cross-owner / cross-tenant object access → 404 or 403 per the Lecture 1 rule, never the object (BOLA).

There is no "I tested the handler." The test goes through the wire, the policy attribute, the real token.

### 2. THREATMODEL.md exists and indexes every deny-path test

A repo-resident `THREATMODEL.md` enumerates the three boundaries with STRIDE and maps every OWASP API Top 10 item to its mitigation and its test (Challenge 1's structure). A boundary without a row, or an OWASP item without a test or an explicit N/A justification, fails the gate.

### 3. MediatR is present only as pipeline behaviors that earn their keep

`ValidationBehavior`, `AuthorizationBehavior`, and `TransactionBehavior` run once per request; the handlers contain business logic only; and the harden diff that introduced MediatR is **net-negative in lines** (`git diff --stat` proves it). No `IRequest`/`IRequestHandler` pair exists for a feature that has no cross-cutting concern.

### 4. AutoMapper is scoped to name-matched projection

One `Profile` of logic-free maps; reads use `ProjectTo` (proven by the EF Core SQL log selecting only DTO columns); `AssertConfigurationIsValid()` passes in a test; and the three logic-bearing mappings are hand-written and unit-tested. There is no `CreateMap` for any inbound entity that carries a `TenantId`.

### 5. All three signals flow to the local Grafana + Loki + Tempo stack

`docker compose -f observability/docker-compose.yml up -d` brings up the stack; the backend exports OTLP; and you can demonstrate the **correlated-incident walkthrough** live: a metric exemplar → the trace in Tempo → its logs in Loki by `TraceId` → the metric in Prometheus. No token or PII appears in any span tag or log (the collector's redaction processor is proven).

## Suggested order of work

1. **Thursday (telemetry first, so you can see the rest).** Wire the OpenTelemetry SDK (Exercise 4), bring up the stack, confirm one request produces a correlated trace/log/metric. You will lean on this while doing the security work.
2. **Friday morning (the pipeline).** Introduce the three MediatR behaviors (Exercise 2), collapse the duplicated write endpoints, confirm the diff is net-negative. Move the read endpoints to `ProjectTo` (Exercise 3).
3. **Friday afternoon (close the boundary).** Add resource-based authz to every object-by-id endpoint (HTTP and gRPC), the deny-by-default fallback policy, rate limiting, the pagination caps, the SSRF host allow-list. Write the integration tests as you go (Exercise 1, Challenge 1).
4. **Saturday (the artifacts).** Write `THREATMODEL.md`, index every test, run the full suite green in CI, and rehearse the correlated-incident walkthrough (Challenge 2) so you can perform it in the demo.
5. **Sunday (review).** The quiz, and the design exercise: "what would you harden next, and why."

## Project layout (added/changed this week)

```
src/Workshop.Api/
  Authorization/                 SubmissionOwnerRequirement.cs, SubmissionOwnerHandler.cs,
                                 TenantRequirement.cs, TenantHandler.cs
  Mapping/                       WorkshopMappingProfile.cs, HandWrittenMappings.cs
  Telemetry/                     WorkshopActivity.cs, WorkshopMetrics.cs
  Security/                      RateLimiting.cs, SecurityHeaders.cs, SsrfGuard.cs
src/Workshop.Application/
  Behaviors/                     ValidationBehavior.cs, AuthorizationBehavior.cs, TransactionBehavior.cs
  Submissions/                   SubmitExerciseCommand.cs + Handler + Validator
tests/Workshop.IntegrationTests/
  BolaTests.cs, BflaTests.cs, BoplaTests.cs, AuthenticationTests.cs,
  ResourceConsumptionTests.cs, SsrfTests.cs, MisconfigurationTests.cs
tests/Workshop.UnitTests/
  MappingConfigurationTests.cs, HandWrittenMappingTests.cs, TransactionBehaviorTests.cs
mini-project/observability/
  docker-compose.yml, otel-collector.yaml, loki.yaml, tempo.yaml,
  prometheus.yml, grafana-datasources.yaml, dashboards/workshop-red.json
THREATMODEL.md
```

The `starter/` folder ships the scaffolding for the pieces that are pure ceremony (the behavior base classes, the OTel registration, the rate-limiter config) so you spend your hours on the parts that require judgment: the resource-based handlers, the deny-path tests, and the threat model.

## Functional requirements

### F1 — Resource-based authorization everywhere an object is named by id

- Every HTTP endpoint and gRPC method that takes an object id resolves the object, then calls `IAuthorizationService.AuthorizeAsync(user, resource, policy)` before returning or mutating it.
- The same `AuthorizationHandler` is reused across HTTP and gRPC (authorization is a domain concern, not a transport concern).
- The EF Core global query filter enforces tenant isolation as defense-in-depth.

### F2 — Deny-by-default and function-level authz

- A fallback authorization policy requires an authenticated user for any endpoint without an explicit one.
- Instructor-only functions carry an `InstructorOnly` policy; the gRPC service carries `[Authorize]`.

### F3 — DTO allow-lists in and out

- No endpoint returns or binds a domain entity. Reads project to DTOs via `ProjectTo`; writes bind to request DTOs and map deliberately.

### F4 — Resource-consumption controls

- Per-user rate limiting on the analytics surface; `pageSize` clamped to 100; request-body size limited; outbound fetches (lesson import) host-allow-listed (SSRF).

### F5 — The MediatR pipeline

- Validation, authorization, and transaction/outbox concerns live in three pipeline behaviors; handlers are business logic only.

### F6 — Observability

- OTLP export of traces, metrics, and logs to the collector; the RED metrics plus domain metrics; manual spans on the grading path; `TraceId`/`SpanId` on every log; exemplars on the duration histogram; the collector redacts `access_token`.

### F7 — The artifacts

- `THREATMODEL.md`, the full security test suite green in CI, and a recorded (or live) correlated-incident walkthrough.

## What "done" looks like

```
$ dotnet build
Build succeeded · 0 warnings · 0 errors

$ dotnet test
Passed!  - Failed: 0, Passed: 71, Skipped: 0
  (incl. BolaTests, BflaTests, BoplaTests, AuthenticationTests,
   ResourceConsumptionTests, SsrfTests, MisconfigurationTests,
   MappingConfigurationTests, TransactionBehaviorTests)

$ git diff --stat capstone/week-13..capstone/week-14 -- src/Workshop.Application
  ... net negative on the submissions feature ...

$ docker compose -f mini-project/observability/docker-compose.yml up -d
  grafana, loki, tempo, prometheus, otel-collector ... started
  # one POST /api/submissions -> correlated trace + logs + metric in Grafana
```

## Grading

This milestone is graded on **boundary integrity** (is every authenticated door closed and tested), **the threat model** (is it complete and does it index real tests), **the deliberateness of MediatR/AutoMapper** (do they remove duplication rather than add ceremony), and **observability** (can you debug a request from the dashboard alone) — not on visual polish. The single most consequential review question is: *show me the deny-path integration test for this endpoint.* If it does not exist, the endpoint is not done, regardless of how the allow path looks. The theme, one last time: **hardening is editing** — a milestone that is *larger* than the baseline has probably added features instead of hardening them.
