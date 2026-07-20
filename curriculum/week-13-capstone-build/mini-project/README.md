# Capstone Milestone 1 — Polyglot Workshop: The Integration Baseline (Build)

> **Time:** 13 hours across Thursday–Sunday. **Prerequisites:** Exercises 1–4 and (ideally) both challenges. **Citations:** every Microsoft Learn URL referenced in the three lecture notes, the Protocol Buffers guide, the gRPC for .NET docs, the EF Core 9 Npgsql provider, Testcontainers for .NET, the Serilog and OpenTelemetry repositories, and the GitHub Actions .NET guide.

This is **Milestone 1 of 3** toward the graded capstone. You are building the foundation of the **Polyglot Workshop** — *one deployable system, three clients, one contract* — on a single repository you will keep and extend for the next two weeks. Week 14 (**harden**) adds OIDC/Keycloak auth, SignalR presence, an outbox, Polly resilience, Dapper analytics, and BenchmarkDotNet to this *same repo*. Week 15 (**deploy + present**) containerizes it, ships a GitHub Actions deploy pipeline to Azure Container Apps (or Fly.io), and adds a `RUNBOOK.md`. So the value of this milestone is not just "it passes" — it is that the repo is clean, the contract is right, and the baseline is green, because the next two weeks are tractable only on a solid base.

The milestone is deliberately **narrow**: define the contract, scaffold the service and the EF Core data layer, get the **first client** compiling against the contract, and prove the **Testcontainers integration baseline green in CI**. Auth is a stub. The MAUI and Blazor clients are scaffolded references, not finished apps. Presence, the outbox, Polly, Dapper, and BenchmarkDotNet are explicitly **out of scope** this week and belong to Week 14. Cutting scope to a demonstrable baseline — and writing down what you cut — is part of the grade.

## The spec

```
                          +----------------------------------+
                          |     Workshop.Contracts           |
                          |  protos/workshop/v1/workshop.proto|  <-- THE ONE CONTRACT
                          |  (Grpc.Tools -> generated C#)     |
                          +-----------------+----------------+
                                            |  <ProjectReference> (no copies)
        +----------------------+------------+------------+----------------------------+
        v                      v                         v                            v
 +----------------+   +-----------------+      +------------------+        +------------------------+
 | Workshop.Api   |   | Workshop.Mobile |      | Workshop.Admin   |        | Workshop.IntegrationTests|
 | ASP.NET Core 9 |   | MAUI (scaffold, |      | Blazor (scaffold,|        | FIRST honest client    |
 | gRPC server    |   |  Week 14-15)    |      |  Week 15)        |        | THIS week              |
 |  + EF Core 9   |   +-----------------+      +------------------+        +-----------+------------+
 |  + Serilog/OTel|                                                                   |
 +-------+--------+                                                                   |
         |                                                                            |
         v  (Database.MigrateAsync on startup, dev)                                   |
 +----------------+                                                                   |
 |  PostgreSQL 16 |<------------ Testcontainers spins an ephemeral copy --------------+
 |  (lessons,     |              for the integration baseline (real DB, every run)
 |   enrollments) |
 +----------------+
                                  + GitHub Actions (ubuntu-latest): restore -> build -> test  ==> GREEN
```

One ASP.NET Core 9 process serves the generated gRPC `Workshop` service, persists to PostgreSQL through EF Core 9, and logs/traces from the first commit. One `.proto` is the source of truth; the service and the test client both reference the *generated* contract. The integration baseline boots the real host against a real PostgreSQL container and proves the enroll round trip — green in CI.

## Milestone requirements

### M1 — The contract is defined

- A `Workshop.Contracts` project with `protos/workshop/v1/workshop.proto` (proto3, `package workshop.v1`, `csharp_namespace = "Workshop.Contracts.V1"`).
- Messages: `Lesson`, `Enrollment`, `Exercise`, `Submission`, `Review`; the `LessonStatus` enum with a zero `UNSPECIFIED`; `google.protobuf.Timestamp` for instants; request/response envelopes for each RPC.
- A single `Workshop` service with at minimum `CreateLesson`, `ListLessons`, `GetLesson`, `Enroll`, `ListEnrollments`, and `WhoAmI`. `SubmitExercise` / `ReviewSubmission` may be declared but left `Unimplemented` this week.
- `Grpc.Tools` with `GrpcServices="Both"` and `PrivateAssets="All"`; generated code is **not** checked in. No hand-written parallel DTOs anywhere.

### M2 — The service + EF Core 9 data layer

- `Workshop.Api` (ASP.NET Core 9) serves the gRPC service via `MapGrpcService<WorkshopService>()`, with `public partial class Program { }` for the test factory.
- `WorkshopDbContext` (EF Core 9, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.x), `AddDbContextPool`, snake_case tables, a unique `(lesson_id, learner_id)` index, a lesson→enrollment cascade.
- Entities are **separate** from the contract messages; a single `ToContract`-style boundary maps between them.
- A checked-in `InitialCreate` migration; `Database.MigrateAsync()` on startup in Development.
- Serilog (compact JSON, `Enrich.FromLogContext`) and OpenTelemetry tracing (`Workshop.Api` source + AspNetCore + Npgsql, console exporter) wired from the first commit.

### M3 — The first client compiles against the contract

- `Workshop.IntegrationTests` references `Workshop.Contracts` and constructs the generated `Workshop.WorkshopClient`. This is the milestone's "first client compiles against the contract" deliverable.
- `Workshop.Mobile` (MAUI) and `Workshop.Admin` (Blazor) exist as projects with a `<ProjectReference>` to `Workshop.Contracts` and at least a one-line use of a generated type, proving the shared-contract wiring — but they are **not** finished apps this week.

### M4 — The Testcontainers integration baseline, green in CI

- `WorkshopFactory : WebApplicationFactory<Program>, IAsyncLifetime` owning a `PostgreSqlContainer`, redirecting the connection string, applying migrations, and handing out a `WorkshopClient` over the in-memory `TestServer`.
- At least three tests on the enroll slice: the happy path, the `NotFound` invariant, and the idempotency branch — against a **real** PostgreSQL via Testcontainers (no SQLite-in-memory).
- A `.github/workflows/ci.yml` on `ubuntu-latest` (`actions/checkout`, `actions/setup-dotnet` pinned `9.0.x`, `dotnet restore/build/test`) that runs the baseline **green** on a real runner.

### M5 — Scope discipline

- A `SCOPE.md` listing what is **in** Milestone 1, what is **deferred** to Week 14 (harden) and Week 15 (deploy), and a one-line reason for each cut. The auth stub, the unfinished MAUI/Blazor clients, and the absent outbox/Polly/Dapper/Benchmark are all explicitly named as deliberate cuts.

### M6 — The slice is genuinely vertical

- The enroll path is implemented *all the way through* — `EnrollRequest` at the gRPC frame → input validation → lesson-exists invariant → read-first idempotency → `Enrollment` row in PostgreSQL → `ToContract` mapping → `Enrollment` message on the wire — with a structured Serilog line and one OpenTelemetry trace following it the whole way. No layer is stubbed on the enroll path.
- The *other* RPCs may be shallow: `CreateLesson`/`ListLessons`/`GetLesson`/`ListEnrollments` need only enough to support the slice and the tests; `SubmitExercise`/`ReviewSubmission`/`GetLessonProgress`/`ListSubmissions` may throw `Unimplemented`. Depth on one slice beats breadth across eight half-built ones — that is the milestone's whole thesis.

### M7 — Observability from commit one

- Serilog and OpenTelemetry are wired in the *first* commit that boots the host, not bolted on at the end. The grader checks `git log` for this: a repo where logging appears only in the final commit signals it was an afterthought, which is the habit the capstone is built to break.
- A single `Enroll` call produces exactly one `TraceId` shared by the `Enroll` application span, the Npgsql `SELECT` (the lesson-exists check), the Npgsql `INSERT`, and every log line the call emits. This is the cross-layer correlation from Week 12, now inside one process and one slice.

## Non-functional requirements

### NF1 — Build and run

- `dotnet build` is clean: 0 warnings, 0 errors.
- `dotnet run --project src/Workshop.Api` serves the gRPC service; `grpcurl -plaintext localhost:5080 list workshop.v1.Workshop` lists the RPCs.
- `dotnet test` passes locally with Docker running, in under ~90 seconds including container start.

### NF2 — Code quality

- Nullable reference types enabled; file-scoped namespaces; C# 13 idioms where natural (collection expressions, primary constructors).
- gRPC methods surface failures as `RpcException` with the correct `StatusCode`, never a bare `Exception`.
- No singleton captures a scoped service; DI validation passes on startup in Development.

### NF3 — Citations

- Every non-trivial implementation choice carries a citation comment pointing at Microsoft Learn or the relevant source repo.
- The top-level `README.md` lists every external dependency with version and license.

## Suggested repo layout

```
PolyglotWorkshop/
├── .github/workflows/ci.yml          <-- restore + build + test on ubuntu-latest
├── SCOPE.md                          <-- in / deferred / why (M5)
├── CONTRACT-WALKTHROUGH.md           <-- the contract write-up (see below)
├── README.md                         <-- top-level: build, run, dependency table
├── PolyglotWorkshop.sln
├── src/
│   ├── Workshop.Contracts/
│   │   ├── Workshop.Contracts.csproj  <-- Grpc.Tools, GrpcServices="Both"
│   │   └── protos/workshop/v1/workshop.proto   <-- THE source of truth
│   ├── Workshop.Api/
│   │   ├── Workshop.Api.csproj
│   │   ├── Program.cs                 <-- gRPC + EF Core + Serilog + OTel; partial Program
│   │   ├── Services/WorkshopService.cs
│   │   ├── Data/WorkshopDbContext.cs
│   │   ├── Data/Lesson.cs
│   │   ├── Data/Enrollment.cs
│   │   ├── Migrations/                <-- InitialCreate, checked in
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── Workshop.Mobile/               <-- MAUI scaffold, references Contracts (Week 14-15)
│   └── Workshop.Admin/                <-- Blazor scaffold, references Contracts (Week 15)
└── tests/
    └── Workshop.IntegrationTests/
        ├── Workshop.IntegrationTests.csproj
        ├── WorkshopFactory.cs         <-- WebApplicationFactory<Program> + Testcontainers
        └── EnrollSliceTests.cs        <-- happy / NotFound / idempotent
```

## The contract walkthrough (`CONTRACT-WALKTHROUGH.md`)

Treat the write-up as part of the deliverable, not an afterthought. Capture each item.

### W1 — The proto, annotated

Paste `workshop.proto` and annotate three decisions: why each enum has a zero `UNSPECIFIED`, why IDs are strings, and why timestamps use the well-known type. Cite <https://protobuf.dev/programming-guides/proto3/>.

### W2 — Generation proof

Show that `Workshop.Contracts` generates `Workshop.WorkshopClient` and `Workshop.WorkshopBase` — paste the `dotnet build` line and one referenced generated type from the service. Confirm generated code is gitignored.

### W3 — The enroll slice, traced

Run the service with the console exporter, issue one `Enroll`, and paste the trace: the `Enroll` span parenting the Npgsql `SELECT` and `INSERT`, all sharing one `TraceId`, plus the one structured log line. Confirm idempotency returns the same enrollment id on a repeat call.

### W4 — The migration SQL

Paste the `dotnet ef migrations script` output for `lessons`, `enrollments`, the cascade FK, and the unique index. Confirm you read it before applying.

### W5 — The baseline, local

Paste the `dotnet test` summary: three tests, the timing breakdown (container start vs host boot vs test bodies).

### W6 — The baseline, green in CI

Link or screenshot the green Actions run. This is the milestone's pass condition.

### W7 — The scope cuts

Summarize `SCOPE.md`: the three biggest things you cut (auth, the finished MAUI/Blazor clients, the outbox) and the one-line reason each is correct to defer to Weeks 14–15.

## Acceptance criteria / definition of done

1. One `workshop.proto` is the only declaration of the domain shape; the service and the test client reference the *generated* types, with no hand-rolled parallel DTOs.
2. `Workshop.Api` serves the gRPC service and persists to PostgreSQL through a checked-in EF Core 9 migration; Serilog + OpenTelemetry are wired from the first commit.
3. The enroll vertical slice works end to end: `EnrollRequest` → entity → `INSERT` → `Enrollment`, with validation, the lesson-exists invariant, and idempotency.
4. The first client (`Workshop.IntegrationTests`) compiles against the contract and exercises the slice; MAUI and Blazor scaffolds reference the contract.
5. The Testcontainers integration baseline (three enroll tests against a real PostgreSQL) is **green in CI** on `ubuntu-latest`.
6. `SCOPE.md` and `CONTRACT-WALKTHROUGH.md` are complete.

## Grading rubric

- **30 points: the contract.** One `workshop.proto` is the source of truth, generation works, no hand-rolled DTOs, sensible field-number/enum/timestamp choices (W1, W2).
- **25 points: service + data layer.** `Workshop.Api` serves the gRPC service; EF Core 9 entities separate from the contract; checked-in migration with correct SQL; Serilog + OTel from commit one (M2, W3, W4).
- **20 points: the integration baseline green in CI.** Three enroll tests against a real Testcontainers PostgreSQL, green on a GitHub Actions runner (M4, W5, W6).
- **10 points: the first client compiles.** The test client constructs the generated `WorkshopClient`; MAUI/Blazor scaffolds reference the contract (M3).
- **10 points: scope discipline.** `SCOPE.md` names what was cut and why; the cuts are the right ones (M5, W7).
- **5 points: code quality.** Clean build (0 warnings), `RpcException` for failures, no scoped-from-singleton captures, citations present (NF2, NF3).

## Stretch goals

1. **A second slice.** Implement `CreateLesson` and `ListLessons` to the same depth as `Enroll` (validation, entity, mapping, three integration tests). This is the breadth you earn *after* the first slice is green. Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>.
2. **gRPC reflection + a thin REST gateway.** Add `AddGrpcReflection()` (dev only) so `grpcurl` works without the `.proto`, and add one minimal-API REST endpoint that calls the same service method, proving REST and gRPC over one contract. Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/test-tools>.
3. **Buf breaking-change gate.** Add the `buf` CLI to CI with `buf breaking --against main`, so a wire-incompatible `.proto` change fails the PR automatically (the Challenge-1 lesson, automated). Cite <https://buf.build/docs/breaking/overview>.
4. **Respawn the test DB.** Switch the fixture to `ICollectionFixture<T>` with `Respawn` truncating between tests so one container serves the whole suite; report the run-time delta. Cite <https://github.com/jbogard/Respawn>.
5. **Coverage in CI.** Add Coverlet and upload a coverage report as an Actions artifact; set a floor for the slice. Cite <https://github.com/coverlet-coverage/coverlet>.

## Submission

Push the project on a branch named `week13-mini-project/<your-handle>` and open a PR against the C9 curriculum repository. The PR description must link to `SCOPE.md` and `CONTRACT-WALKTHROUGH.md` and include the green Actions run from W6 and the `dotnet test` summary from W5.

The teaching staff reviews milestone PRs within 7 business days. Reviews focus on (a) whether the contract is the single source of truth, (b) whether the enroll slice is genuinely end to end through a real database, (c) whether the baseline is green in CI and not just locally, and (d) whether the scope cuts are deliberate and correct. Because this is Milestone 1 of 3 on a repo you keep, reviewers also note anything that will make Weeks 14–15 harder — a leaky contract↔entity boundary, a missing `public partial class Program`, generated code committed by accident.

Cited Microsoft Learn pages: every page referenced in the three lecture notes plus <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore> and <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>. External: the Protocol Buffers guide at <https://protobuf.dev/programming-guides/proto3/>, Testcontainers for .NET at <https://dotnet.testcontainers.org/>, the Serilog org at <https://github.com/serilog>, the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, and the GitHub Actions .NET guide at <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>.
