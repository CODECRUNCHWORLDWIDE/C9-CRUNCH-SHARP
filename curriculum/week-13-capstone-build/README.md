# Week 13 — Capstone Build Week: One Deployable System, Three Clients, One Contract — Defining the gRPC `.proto`, Scaffolding the ASP.NET Core 9 Service on EF Core 9 + PostgreSQL, and Proving an Integration Baseline Green in CI with Testcontainers

Welcome to **C9 · Crunch Sharp**, Week 13. Week 12 taught composition: a single ASP.NET Core host serving REST, gRPC, and SignalR behind one JWT scheme, one Serilog pipeline, one OpenTelemetry trace, one EF Core context — and an integration suite that booted the whole thing against an ephemeral PostgreSQL container. That week was the dress rehearsal. This week the curtain rises on the **capstone**: a three-week arc (Weeks 13–15) building **one coherent system on one repository** — the **Polyglot Workshop**. The tagline from the C9 syllabus is exact and worth memorizing: *"One deployable system. Three clients. One contract."* An ASP.NET Core 9 backend, a .NET MAUI mobile client, and a Blazor admin dashboard, all generated from and consuming a single typed gRPC contract. We step up the framework deliberately here: the capstone targets **.NET 9, ASP.NET Core 9, EF Core 9, C# 13**. Confirm `dotnet --version` prints `9.0.x` before you begin. Week 13 is the **build milestone** — define the contract, scaffold the service and data layer, stand up the first client, and get integration tests green in CI. Week 14 *hardens* it (auth, resilience, observability under load); Week 15 *deploys and presents* it. We reference those forward but we do not do their work today.

The first thing to internalize is that **the contract is the source of truth, not the UI**. Most teams build a backend, then build a UI against whatever the backend happened to expose, then build a second UI by copying the first UI's HTTP-call code. By the time the third client exists, three subtly different notions of "what a `Lesson` is" have congealed in three codebases, and a field renamed in the database silently breaks one of them. The Polyglot Workshop inverts that. A single `.proto` file — `workshop/v1/workshop.proto` — is written first and is the only place the shape of `Lesson`, `Enrollment`, `Exercise`, `Submission`, and `Review` is declared. The `Grpc.Tools` MSBuild integration generates strongly-typed C# from it at build time; that generated code is referenced by the service, by the MAUI mobile client, and by the Blazor admin client. When you rename a field in the `.proto`, every client that consumes it fails to **compile** until it is updated — which is exactly what you want. The canonical reference is the Protocol Buffers language guide at <https://protobuf.dev/programming-guides/proto3/> and the gRPC for .NET documentation at <https://learn.microsoft.com/en-us/aspnet/core/grpc/>.

The second thing to internalize is **vertical-slice delivery: ship a working end-to-end slice on day one, not a complete layer**. The wrong way to build a system this size is horizontally — finish all the entities, then all the repositories, then all the endpoints, then start the first client three weeks in and discover the contract was wrong. The right way is one **vertical slice**: pick the single thinnest path that touches every layer — *"a learner enrolls in a lesson"* — and build it through the `.proto` message, the EF Core entity and migration, the gRPC service method, and one client call that proves the round trip. When that slice is green in CI, you have de-risked the entire architecture; everything after is repetition of a proven shape. The discipline is documented well in Jimmy Bogard's vertical-slice writing at <https://www.jimmybogard.com/vertical-slice-architecture/>; we apply it to a multi-client system rather than a single app.

The third thing to internalize is **scope cuts are a design skill, not an admission of failure**. The full Polyglot Workshop spec lists Keycloak OIDC, SignalR presence, an outbox, Polly, Dapper analytics, BenchmarkDotNet, three clients, and a deploy pipeline. You will not build all of it in Week 13, and trying to will leave you with eight half-finished features and nothing green. The build-milestone deliverable is narrow and explicit: the contract is defined, the service + EF Core data layer exists, **the first client compiles against the contract**, and the Testcontainers integration baseline is green in CI. Auth is a stub, the second and third clients are scaffolds, presence and the outbox are Week-14 work. Cutting scope to a demonstrable baseline — and writing down what you cut and why — is the senior move. The reference for milestone-driven planning is the "tracer bullet" idea from *The Pragmatic Programmer*; the canonical .NET-flavored version is the minimal-API tutorial at <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>, which builds exactly one slice at a time.

The fourth thing to internalize is **Testcontainers as the default integration-test substrate**. Week 12 introduced `WebApplicationFactory<T>` + Testcontainers for PostgreSQL; the capstone makes it the *default*. Every integration test in the Workshop boots the real service in-process and points it at a real PostgreSQL 16 container spun up for the test run — no SQLite-in-memory shortcut that hides provider-specific bugs, no shared dev database that makes tests order-dependent. The "integration baseline" you ship this week is a small suite that proves the gRPC contract, the EF Core migration, and at least one read/write round trip all compose against a real database, and that runs green on a GitHub Actions runner. The references are Testcontainers for .NET at <https://dotnet.testcontainers.org/> and the ASP.NET Core integration-test docs at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.

The fifth thing to internalize is **keeping three clients honest against one contract**. A MAUI app, a Blazor app, and an integration-test project are three independent compilation units. The thing that keeps them from drifting is that all three reference the *same* generated contract assembly (`Workshop.Contracts`) — the MAUI and Blazor clients use gRPC and gRPC-Web respectively, but both call the same generated `Workshop.WorkshopClient`. This week you only need the *first* client (the integration-test client and a thin console or REST consumer) to compile and exercise the contract; Weeks 14–15 flesh out MAUI and Blazor. But you set up the shared-contract project now so that adding the next client is a `<ProjectReference>`, not a copy-paste. The gRPC-Web reference for the browser-side Blazor consumer is at <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>.

The sixth thing to internalize is **Serilog + OpenTelemetry from the first commit**. We do not "add observability later." The scaffold ships with Serilog as the global logger and OpenTelemetry tracing wired to the console exporter, so that the very first enroll-a-learner request produces a structured log line carrying a trace ID and a span that crosses the gRPC frame into the EF Core `INSERT`. This is the same wiring from Week 12, brought forward unchanged; the point of repeating it on day one of the capstone is that observability is cheapest when it is present from the start and most expensive when retrofitted. The references are Serilog's ASP.NET Core package at <https://github.com/serilog/serilog-aspnetcore> and the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>.

By the end of this week you will be the person on your team who can drive an end-to-end build *from a contract* rather than from a screen — define a `.proto`, generate types, scaffold a service and a migration, stand up a client, and prove the whole thing in CI with Testcontainers — and who knows how to cut scope to a demonstrable baseline and write down what was cut. That is the skill the capstone exists to certify.

## Learning objectives

By the end of this week, you will be able to:

- **Author** a `workshop.proto` (proto3, package `workshop.v1`) declaring the domain messages — `Lesson`, `Enrollment`, `Exercise`, `Submission`, `Review` — and a `Workshop` service with the RPCs the build milestone needs. **Cite** <https://protobuf.dev/programming-guides/proto3/>.
- **Generate** strongly-typed C# from the `.proto` via `Grpc.Tools` and the `<Protobuf>` MSBuild item, in a shared `Workshop.Contracts` project consumed by the service and every client. **Cite** <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>.
- **Scaffold** an ASP.NET Core 9 host (`Workshop.Api`) that serves the generated gRPC service, with Serilog and OpenTelemetry wired to the console exporter from the first commit. **Cite** <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>.
- **Model** the domain in EF Core 9 against PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`; author and check in an `InitialCreate` migration; apply it on startup in Development. **Cite** <https://learn.microsoft.com/en-us/ef/core/providers/npgsql>.
- **Deliver** one vertical slice — *a learner enrolls in a lesson* — through the contract message, the entity, the gRPC method, and a client round trip, before building any breadth. **Cite** <https://www.jimmybogard.com/vertical-slice-architecture/>.
- **Stand up** the first client: an integration-test client (and a thin console/REST consumer) that compiles against `Workshop.Contracts` and calls the generated `WorkshopClient`. **Cite** <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>.
- **Build** the Testcontainers integration baseline: `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql`, with at least one test that exercises the enroll slice end to end against a real database. **Cite** <https://dotnet.testcontainers.org/>.
- **Run** the baseline green in CI with a GitHub Actions workflow that restores, builds, and tests on a Linux runner with Docker available. **Cite** <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>.
- **Cut** scope deliberately: produce a `SCOPE.md` listing what is in the milestone, what was deferred to Weeks 14–15, and the one-line reason for each cut. **Cite** the minimal-API tutorial at <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>.
- **Reason** about the difference between a unit test (calls a method) and an integration test (boots the host against a real database), and which the milestone requires. **Cite** <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.
- **Cite** the Protocol Buffers language guide, the gRPC for .NET docs, the EF Core Npgsql provider, Testcontainers for .NET, the Serilog and OpenTelemetry repositories, and the GitHub Actions .NET workflow guide for each technique covered.

## Standards this week meets

| Bar | What this week is measured against |
| --- | --- |
| University | `EECS 280` — Complete a substantial multi-file program of the learner's own design, built against a specification written before the code. |
| Industry | Take a contract to a working vertical slice that is green in continuous integration from a clean checkout, on a machine that is not yours and that you cannot log into. |
| Beyond the bar | Renaming one field in the `.proto` and watching every consumer break — the contract proven load-bearing rather than described as such — `challenges/challenge-01-rename-a-field-watch-it-break.md` |

## Prerequisites

- **Weeks 1–12 of C9 complete.** This is the first capstone week; it assumes minimal APIs (Week 2), EF Core (Weeks 3, 10), gRPC (Week 9), SignalR (Week 11), and the composition + integration-testing discipline of Week 12. Where a concept is reused we cite the lecture that introduced it.
- **A working `dotnet --version` of `9.0.x`.** The capstone steps the course up to .NET 9 / ASP.NET Core 9 / EF Core 9 / C# 13. If you are still on 8, install the .NET 9 SDK from <https://dotnet.microsoft.com/download/dotnet/9.0> first.
- **Docker, running.** Required for the Testcontainers integration baseline (a real PostgreSQL 16 container per test class) and for any local Postgres. The Docker daemon must be up before `dotnet test`.
- **PostgreSQL 16, reachable.** Easiest via Docker: `docker run --name pg-capstone -e POSTGRES_PASSWORD=devpass -p 5432:5432 -d postgres:16`. The integration tests spin their own container; you only need a local one for hands-on exercises.
- **The EF Core 9 CLI.** `dotnet tool install --global dotnet-ef --version 9.0.0`. Verify with `dotnet ef --version` printing `9.0.x`.
- **`grpcurl`.** For poking the gRPC surface by hand. `brew install grpcurl` on macOS, or releases at <https://github.com/fullstorydev/grpcurl/releases>.
- **A GitHub repository you can push to,** so the integration baseline runs on a real Actions runner — the "green in CI" deliverable is not satisfiable on your laptop alone.

## Topics covered

- **The contract first.** Writing `workshop.proto` (proto3, `package workshop.v1`), choosing message shapes, field numbers, `well_known_types` (`google.protobuf.Timestamp`), and the `Workshop` service RPCs. Why the contract is written before the database.
- **Code generation.** The `Grpc.Tools` package, the `<Protobuf Include="..." GrpcServices="..." />` MSBuild item, `Server` vs `Client` vs `Both` generation, and the shared `Workshop.Contracts` project that every consumer references.
- **Scaffolding `Workshop.Api`.** `WebApplication.CreateBuilder`, `AddGrpc()`, `MapGrpcService<WorkshopService>()`, the `appsettings.json`/`appsettings.Development.json` split, Serilog + OpenTelemetry from commit one.
- **EF Core 9 + PostgreSQL.** `WorkshopDbContext`, the `Lesson`/`Enrollment`/`Exercise`/`Submission`/`Review` entities, snake_case mapping, the `InitialCreate` migration, `Database.MigrateAsync()` on startup, the contract-DTO ↔ entity mapping boundary.
- **The vertical slice.** *Enroll a learner in a lesson* threaded through proto → entity → service method → client call → integration test. The discipline of finishing one slice before starting breadth.
- **The first client.** A gRPC client over `GrpcChannel`, the generated `WorkshopClient`, and why the integration-test project counts as the first honest client.
- **Integration baseline.** `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql`, the `WorkshopFactory` fixture, the enroll round-trip test, the auth-stub seam.
- **CI green.** A GitHub Actions workflow (`actions/checkout`, `actions/setup-dotnet`, `dotnet restore/build/test`) on `ubuntu-latest` with Docker available so Testcontainers works on the runner.
- **Scope discipline.** The `SCOPE.md` deliverable; what is in Milestone 1 and what is explicitly deferred to the harden (Week 14) and deploy (Week 15) milestones.
- **The worked system: Polyglot Workshop.** A classroom platform — instructors create lessons, learners enroll, both submit and review exercises, analytics aggregate progress — built once and extended across three weeks on one repo.

## Weekly schedule

The schedule adds up to approximately **34 hours**. Treat it as a target, not a contract. The contract-design and vertical-slice material reward an unhurried mind; resist the urge to scaffold all five entities before the first slice is green.

| Day       | Focus                                                                       | Lectures | Exercises | Challenges | Quiz/Read | Capstone | Self-Study | Daily Total |
|-----------|-----------------------------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-----------:|------------:|
| Monday    | The contract first: `workshop.proto`, code generation, the shared project   |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |    0.5h    |     5.5h    |
| Tuesday   | Scaffold `Workshop.Api` + EF Core 9 data layer; the enroll vertical slice   |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |    0.5h    |     5.5h    |
| Wednesday | Integration baseline with `WebApplicationFactory` + Testcontainers, CI      |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |    0.5h    |     5.5h    |
| Thursday  | Challenges, scope-cutting workshop, the first-client round trip             |    0.5h  |    0h     |     2h     |    0.5h   |   2h     |    0.5h    |     5.5h    |
| Friday    | Capstone milestone — build the Workshop baseline end to end                 |    0h    |    0h     |     1h     |    0.5h   |   3h     |    0.5h    |     5h      |
| Saturday  | Milestone polish, CI run, `SCOPE.md`, contract-walkthrough write-up         |    0h    |    0h     |     0h     |    0h     |   4h     |    0h      |     4h      |
| Sunday    | Quiz, review, design exercise: "what is the next slice"                     |    0h    |    0h     |     0h     |    1h     |   1h     |    0h      |     2h      |
| **Total** |                                                                             | **6.5h** | **4.5h**  | **3h**     | **3.5h**  | **13h**  | **2.5h**   | **34h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | The Protocol Buffers guide, gRPC for .NET, EF Core 9 Npgsql provider, Testcontainers, Serilog, OpenTelemetry, the GitHub Actions .NET guide, and adjacent reading |
| [lecture-notes/01-the-contract-is-the-source-of-truth.md](./lecture-notes/01-the-contract-is-the-source-of-truth.md) | Writing `workshop.proto`, code generation with `Grpc.Tools`, the shared `Workshop.Contracts` project, keeping three clients honest |
| [lecture-notes/02-scaffolding-the-service-and-the-vertical-slice.md](./lecture-notes/02-scaffolding-the-service-and-the-vertical-slice.md) | The `Workshop.Api` host, EF Core 9 + PostgreSQL data layer, the contract↔entity boundary, the enroll vertical slice end to end |
| [lecture-notes/03-the-integration-baseline-and-ci.md](./lecture-notes/03-the-integration-baseline-and-ci.md) | `WebApplicationFactory<Program>` + Testcontainers, the enroll round-trip test, the GitHub Actions workflow that runs it green |
| [exercises/exercise-01-author-the-contract.cs](./exercises/exercise-01-author-the-contract.cs) | Write `workshop.proto` and the `Workshop.Contracts.csproj`; confirm generation by referencing a generated type |
| [exercises/exercise-02-scaffold-service-and-migration.cs](./exercises/exercise-02-scaffold-service-and-migration.cs) | Scaffold `Workshop.Api`, the `WorkshopDbContext`, the entities, and the `InitialCreate` migration; serve the gRPC service |
| [exercises/exercise-03-the-enroll-slice.cs](./exercises/exercise-03-the-enroll-slice.cs) | Implement `Enroll` end to end: proto message → entity → service method → client call; verify with `grpcurl` |
| [exercises/exercise-04-integration-baseline.cs](./exercises/exercise-04-integration-baseline.cs) | Author the `WorkshopFactory` and one `WebApplicationFactory<Program>` + Testcontainers test of the enroll round trip |
| [exercises/SOLUTIONS.md](./exercises/SOLUTIONS.md) | Worked solutions for the four exercises, with generated-code excerpts, migration SQL, and the green-test output you should reproduce |
| [challenges/challenge-01-rename-a-field-watch-it-break.md](./challenges/challenge-01-rename-a-field-watch-it-break.md) | Prove the contract is load-bearing: rename a `.proto` field and watch every consumer fail to compile until updated |
| [challenges/challenge-02-green-in-ci-from-clean.md](./challenges/challenge-02-green-in-ci-from-clean.md) | Get the integration baseline green on a GitHub Actions runner from a clean checkout; diagnose the Docker-on-runner gotchas |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on the contract, code generation, EF Core 9, the vertical slice, and the integration baseline |
| [homework.md](./homework.md) | Six practice problems for the build week |
| [mini-project/README.md](./mini-project/README.md) | **Capstone Milestone 1 (Build)** — the full brief: contract, service + EF Core data layer, first client compiles, Testcontainers baseline green in CI |

## The "build succeeded" promise — restated, and a capstone contract

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 612 ms
```

For the capstone we add a **contract-is-the-source-of-truth** promise: **there is exactly one declaration of the domain shape — `workshop.proto` — and the service and every client consume the *generated* types, never a hand-rolled parallel DTO.** A pull request that hand-writes a `Lesson` class in a client instead of referencing `Workshop.Contracts` is, by definition, a pull request that has broken the single-source rule. Three clients, one `.proto`.

We add a **green-in-CI** contract too: **the integration baseline — the enroll round trip through `WebApplicationFactory<Program>` and a real Testcontainers PostgreSQL — passes on a GitHub Actions runner, not just on your laptop.** "It worked locally" is not the deliverable; a green checkmark on the Actions tab is. This is Milestone 1 of 3 toward the graded capstone; Weeks 14 (harden) and 15 (deploy + present) build on the *same* repository, so a clean, green baseline now is what makes the next two weeks tractable.

> **Note on packages.** Contract: `Grpc.Tools` 2.66+ (build-time, `PrivateAssets="All"`), `Google.Protobuf` 3.27+, `Grpc.Net.Client` 2.66+ (clients), `Grpc.AspNetCore` 2.66+ (server). Service: `Microsoft.AspNetCore.App` (framework reference; no install). EF Core 9 + Postgres: `Microsoft.EntityFrameworkCore` 9.0.x, `Microsoft.EntityFrameworkCore.Design` 9.0.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.x. Observability: `Serilog.AspNetCore` latest, `Serilog.Sinks.Console`, `Serilog.Formatting.Compact`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.GrpcNetClient`, `Npgsql.OpenTelemetry`, `OpenTelemetry.Exporter.Console`. Tests: `xunit` 2.9+, `Microsoft.AspNetCore.Mvc.Testing` 9.0.x, `Testcontainers.PostgreSql` 3.10+. All free, all open source, all source-linkable to the listed repositories.
