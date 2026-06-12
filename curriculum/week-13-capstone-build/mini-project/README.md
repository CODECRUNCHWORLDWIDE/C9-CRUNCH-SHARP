# Mini-Project — The Integration Baseline: One `.proto`, Three Clients Compiling Against It, Integration Tests Green in CI on Ephemeral PostgreSQL and Keycloak, with Serilog and OpenTelemetry Wired

> **Time:** ~12 hours across Thursday–Saturday. **Prerequisites:** Exercises 1–4 and both challenges. **Citations:** every Microsoft Learn URL in the three lecture notes, the Testcontainers for .NET docs, the Serilog and OpenTelemetry repositories, the `grpc/grpc-dotnet` source, and the GitHub Actions docs.

## The milestone

This is not a new build — it is the **assembly** of everything the week's exercises and challenges produced into one repository that meets the integration baseline. By the SYLLABUS, Week 13's capstone milestone is the **integration baseline**: *all three clients (API, MAUI, Blazor) compile against the shared gRPC contract, with integration tests green in CI.* This mini-project is that milestone, made concrete and verifiable.

The baseline is reached when, on a clean checkout, the following are all true:

- `dotnet build Workshop.sln` succeeds across the backend, the Blazor admin, and the MAUI client — all against the one generated `workshop.proto` contract.
- `dotnet test` is green, with the integration suite running against a **real** PostgreSQL and a **real** Keycloak started by Testcontainers, migrations applied, the vertical slice asserted end to end.
- The same green runs in **GitHub Actions** on every push — Testcontainers starting the containers inside the runner — and gates the merge.
- Every request through the running system leaves a **structured Serilog event** and an **OpenTelemetry trace** behind it.

This week is graded on the baseline being real and green-in-CI, not on visual polish. A pretty screen with a broken contract fails; a plain list with a green integration suite passes.

## The repository you assemble

```
workshop-capstone/
├── Workshop.sln
├── .github/
│   └── workflows/
│       └── ci.yml                       # build all three + integration tests
├── src/
│   ├── Workshop.Contract/
│   │   ├── Workshop.Contract.csproj     # GrpcServices="Both"; the source of truth
│   │   └── Protos/workshop.proto        # the one contract
│   ├── Workshop.Domain/
│   │   ├── Lesson.cs  Enrollment.cs  Submission.cs
│   │   └── SubmissionStatus.cs
│   ├── Workshop.Api/                     # ASP.NET Core 9 backend
│   │   ├── Program.cs                    # Minimal API + gRPC + Serilog + OTel + JWT
│   │   ├── WorkshopDbContext.cs
│   │   ├── Migrations/                   # dotnet ef migrations add InitialCreate
│   │   ├── Grpc/WorkshopService.cs
│   │   ├── Mapping/ProtoMappings.cs
│   │   ├── Observability/WorkshopTelemetry.cs
│   │   └── Analytics/ProgressQueries.cs  # Dapper — the analytics escape hatch
│   ├── Workshop.Maui/                    # learner client (compiles; native gRPC)
│   │   └── Services/WorkshopApi.cs
│   └── Workshop.Admin/                   # Blazor Auto admin (gRPC-Web)
│       └── Services/AdminApi.cs
└── tests/
    ├── Workshop.UnitTests/               # domain + mapping, no I/O
    │   └── MappingTests.cs
    └── Workshop.IntegrationTests/        # WebApplicationFactory<Program> + Testcontainers
        ├── WorkshopFixture.cs
        ├── WorkshopAppFactory.cs
        ├── SliceHarness.cs
        ├── VerticalSliceTests.cs
        └── Realms/workshop-realm.json
```

The `mini-project/starter/` folder ships the scaffolding for the load-bearing files — the `Program.cs` with the full wiring, the Dapper analytics query, and the CI workflow — with the integration-specific pieces (the proto, the service body, the harness) carried in from your exercises.

## Functional requirements

### F1 — One contract, three compiling clients

- `workshop.proto` is the single source of truth, in `Workshop.Contract` with `GrpcServices="Both"`. No hand-written DTO duplicates any message.
- `Workshop.Api` overrides the generated `WorkshopBase` and implements all four RPCs.
- `Workshop.Admin` (Blazor) consumes the generated `WorkshopClient` over gRPC-Web (`GrpcWebHandler`).
- `Workshop.Maui` consumes the generated `WorkshopClient` over native gRPC.
- `dotnet build Workshop.sln` builds the backend and Blazor; a separate step builds the MAUI `net9.0-android` head. All green.

### F2 — Both surfaces over one domain

- The backend exposes `CreateLesson`, `Enroll`, `Submit`, `ListPendingSubmissions` over gRPC.
- It mirrors `CreateLesson` (at minimum) on the REST surface as `POST /api/lessons`.
- Both surfaces call the same domain factories and the same `WorkshopDbContext` — no duplicated business logic.
- Caller identity comes from the validated token's `sub` claim on every operation; no request message carries an identity field.

### F3 — Persistence and analytics

- EF Core (Npgsql) persists lessons, enrollments, and submissions to PostgreSQL.
- Migrations exist (`Migrations/InitialCreate`) and are applied via `MigrateAsync` (in tests) and at startup (in the running app).
- At least one analytics read uses **Dapper** — e.g. "submissions per lesson, last 7 days" — proving the escape hatch from EF Core for an aggregate query (the `Analytics/ProgressQueries.cs` starter).

### F4 — Auth via Keycloak

- JWT bearer auth validates tokens against a Keycloak realm (the `Oidc:Authority`/`Oidc:Audience` config).
- In tests, the authority is the Testcontainers Keycloak; in dev, a locally-run Keycloak; the realm JSON is `Realms/workshop-realm.json` (challenge 1).

### F5 — Observability wired from commit one

- Serilog is the logging provider: structured events, `Enrich.FromLogContext`, `UseSerilogRequestLogging`, a console sink.
- OpenTelemetry emits traces (ASP.NET Core, HttpClient, EF Core, gRPC client, and a domain `ActivitySource`) and metrics (a domain `Meter` with `lessons.created` / `submissions.received` counters) to an OTLP exporter.
- A request produces a connected trace (challenge 2) and a structured log line carrying the same trace id.

### F6 — The integration baseline test

- A `WebApplicationFactory<Program>` integration suite over Testcontainers PostgreSQL + Keycloak.
- The factory overrides only the connection string and the OIDC authority — no in-memory database, no stubbed auth.
- The vertical slice (`create → enroll → submit → appears in pending queue`) is asserted green with real tokens, plus at least one negative path (unknown lesson → `NotFound`, forged issuer → `Unauthenticated`).

### F7 — Green in CI

- `.github/workflows/ci.yml` restores, builds backend + Blazor, builds the MAUI android head, and runs the integration suite — Testcontainers starting the containers in the runner.
- The workflow runs on `push` and `pull_request` and gates the merge.

## Non-functional requirements

- **Targets.** Every project targets `net9.0` (MAUI also `net9.0-android` / a desktop head). C# 13.
- **Zero warnings.** `dotnet build` is 0 warnings, 0 errors. Treat warnings as the build contract.
- **No leaked containers.** After `dotnet test`, `docker ps` shows nothing lingering (Ryuk reaps them).
- **No secrets in the repo.** The realm's `test-secret` is a *test* secret in the test project, clearly labeled; no production secret is committed.

## The order to build it (and the scope cuts)

Follow the lecture order — it is the order that produces a walking skeleton on Friday rather than a reconciliation on Sunday:

1. **Contract + domain** (Exercise 1). Confirm all three projects build against the generated types with empty implementations.
2. **Service + mapping + REST mirror** (Exercise 2).
3. **Serilog + OpenTelemetry in `Program.cs`** (starter `Program.cs`) — wired *before* the test, so the first test already emits.
4. **Migrations** — `dotnet ef migrations add InitialCreate`.
5. **Integration test over Testcontainers + Keycloak** (Exercise 3 + Challenge 1). Green locally.
6. **Connected trace** (Challenge 2). One request, one trace.
7. **CI workflow** (starter `ci.yml`). Green in Actions.

**Scope cuts — explicitly deferred, written down (Lecture 1 §6):** MediatR and AutoMapper (Week 14), the Grafana/Loki/Tempo dashboards (Week 14), the OWASP threat-model pass (Week 14), the multi-stage Dockerfile and the deploy job (Week 15), the MAUI offline-sync conflict resolution (Week 15/portfolio), the Blazor charts and the SignalR presence feature (not on the slice path). The arbiter: if the vertical slice is still green without it, it was a correct cut.

## Deliverables

1. The assembled `workshop-capstone/` repository meeting F1–F7.
2. A green `dotnet test` run (paste the summary line) and a green Actions run (link the run).
3. A `BASELINE.md` at the repo root: the vertical-slice statement, the scope-cut list, the green-test summary, and a one-paragraph "what the baseline proves" write-up.
4. A short trace capture (from Challenge 2) showing one request → one connected trace.

## Grading rubric (100 points)

| Criterion | Points | What earns full marks |
|----------|-------:|------------------------|
| One contract, three compiling clients (F1) | 20 | All three build against the generated proto; no duplicated DTO; MAUI head builds in CI |
| Both surfaces over one domain (F2) | 12 | gRPC + REST share the domain; identity from the token, never the body |
| Persistence + Dapper analytics (F3) | 12 | EF Core + migrations applied; one Dapper aggregate read working |
| Auth via Keycloak (F4) | 12 | Real token validated against the real realm; negative (forged) path rejected |
| Observability wired (F5) | 12 | Serilog structured events + OTel traces/metrics emitting; trace-log correlation shown |
| Integration baseline test (F6) | 18 | `WebApplicationFactory<Program>` over Testcontainers; no mocked DB/auth; slice + negative paths green |
| Green in CI (F7) | 14 | Actions builds all three and runs the integration suite green on push |

Partial credit is given per criterion. **The integration test and CI (F6 + F7, 32 points) cannot be earned by a "works on my machine" claim** — they require a linked green Actions run. A baseline that is green locally and red in CI scores zero on F7, because the entire point of the baseline is that "it works" is a fact CI verifies on every push.

## A note on what this milestone is for

The integration baseline is the least glamorous week of the capstone and the most important. It draws no charts and demos no screens; it produces a repository that *stands up* — contract honored, database real, auth real, tests real, build green where it counts. Week 14 hardens what stands (threat model, MediatR/AutoMapper where they earn their keep, the observability stack); Week 15 deploys it (Dockerfile, Actions deploy, Azure Container Apps, the runbook). Neither is possible on a system that does not yet stand up. Get the baseline green this week and every following week is editing — which is exactly how it should be.
