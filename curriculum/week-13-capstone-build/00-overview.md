# Week 13 — Capstone Build: The Integration Baseline. One `.proto` Contract, Three Clients That Compile Against It, Integration Tests Green in CI on Ephemeral PostgreSQL and Keycloak, with Serilog and OpenTelemetry Wired From the First Commit

Welcome to **C9 · Crunch Sharp**, Week 13 — the first of the three capstone weeks. For twelve weeks the work was scoped: a topic, a lecture, a mini-project, a quiz. The framing was "learn this one thing well." Starting this week the framing inverts. There is no new topic to learn in isolation; there is one system to build, and the system is the sum of everything the prior twelve weeks taught — Minimal APIs (Week 5), EF Core and Dapper (Week 6), Identity and OIDC (Week 7), SignalR and background workers (Week 8), the gRPC contract (Week 9), MAUI (Week 10), Blazor (Week 11), and the performance discipline (Week 12). The system is the **Polyglot Workshop**: an ASP.NET Core 9 backend, a .NET MAUI mobile client, and a Blazor admin dashboard, all sharing **one** typed gRPC contract. The domain is a workshop/classroom platform — instructors create lessons, learners enroll and submit, an analytics surface aggregates progress.

This week's milestone is the **integration baseline**, and the order in which you reach it is the entire lesson. The temptation — the one that has sunk more capstones than any bug — is to start at the UI, get one screen working end to end, and then "wire up the rest." That order produces a demo on Friday and a contract argument on Monday, because the three clients were never forced to agree on anything. The discipline this week installs is the opposite: **the contract is written first, the contract generates the clients, and a client that does not compile against the contract is a build break, not a conversation.** The slogan, lifted directly from the syllabus, is "ship a vertical slice on day one" — one thin path (create a lesson, enroll, submit, see it in the admin queue) that travels through every layer of every client on Monday, so that by Tuesday the integration surface area is *known* and the rest of the week is filling it in, not discovering it.

The second pillar of the week is **integration testing as the default substrate, not an afterthought.** A unit test that mocks the database tells you your C# compiles; it does not tell you your EF Core query translates, your migration applies, your Npgsql connection string parses, or your Keycloak token validates. The honest test for a system whose whole value is *three clients agreeing on one contract against one database* is an integration test that spins a real PostgreSQL and a real Keycloak, applies the real migrations, mints a real token, and exercises the real gRPC and REST surfaces through `WebApplicationFactory<T>`. The tool that makes this cheap enough to run on every push is **Testcontainers for .NET**: it starts a throwaway PostgreSQL container and a throwaway Keycloak container per test collection, hands you the dynamically-assigned connection string and issuer URL, and tears both down when the collection finishes. The container is ephemeral; the assertion is real. By Wednesday your CI is red or green on the strength of a test that, if it passes, means the contract is honored on a real database with real auth — and that is the only kind of green that means anything for this system.

The third pillar is **observability from the first commit, not the last.** A capstone that you cannot read from its logs is a capstone you debug by guessing. This week you wire **Serilog** for structured logging (every log line a typed event with a request id, a tenant id, a trace id — not a `string.Format`) and **OpenTelemetry** for traces and metrics (every gRPC call, every EF Core query, every outbound HTTP hop a span on a trace you can follow end to end). You do not build the dashboards this week — that is Week 14's hardening work with Grafana, Loki, and Tempo. This week you wire the *emission*: the `ActivitySource`, the `Meter`, the Serilog sink to console in the OpenTelemetry-compatible format, and the OTLP exporter pointed at a local collector you start with one `docker run`. The reason to do it now, in the build week, is that retrofitting tracing onto a system that was not built to emit it is a rewrite; emitting from the first commit costs ten lines in `Program.cs` and pays for itself the first time an integration test fails and you read the trace instead of adding `Console.WriteLine`.

The fourth pillar — the one that ties the other three together — is **keeping three clients honest against one contract.** The MAUI client and the Blazor admin do not share a hand-written DTO; they share generated code from the same `workshop.proto`. When you add a field to a message, you regenerate, and both clients either compile or break — there is no third state where one client "forgot." gRPC-Web is how the Blazor admin reaches the same service the MAUI client reaches over native gRPC; the contract is identical, only the transport differs. Lecture 1 is the vertical-slice discipline; Lecture 2 is the contract-first mechanics that make the three clients compile against one source of truth; Lecture 3 is Testcontainers as the integration substrate and the Serilog + OpenTelemetry wiring that makes the green build trustworthy. The exercises and the mini-project are not toy problems — they *are* the capstone milestone, broken into the order you should build it.

By the end of the week you will have a repository where `dotnet build` succeeds across the backend, the MAUI client, and the Blazor admin; where `dotnet test` is green against a real PostgreSQL and a real Keycloak started by Testcontainers; where the same green runs in GitHub Actions on every push; and where every request you make against the running system leaves a structured Serilog event and an OpenTelemetry trace behind it. That is the integration baseline. It is not glamorous and it does not demo well, and it is the single most important week of the capstone, because every week after it is editing — and you cannot edit what does not yet stand up.

## Learning objectives

By the end of this week, you will be able to:

- **Plan** a vertical slice for a multi-client system: pick the one path (create lesson → enroll → submit → appears in admin queue) that touches every layer of every client, and build *that* end to end on day one before building breadth. Justify why this order beats UI-first.
- **Author** a single `workshop.proto` as the source of truth for the domain, generate server stubs (`Grpc.AspNetCore`) and client stubs (`Grpc.Net.Client`, `Grpc.Net.Client.Web`) from it, and consume the generated types from both the MAUI client and the Blazor admin without hand-writing a DTO.
- **Mirror** the REST surface (Minimal APIs) and the gRPC surface against the same domain model, and explain which calls belong on which surface and why a learner phone uses native gRPC while a browser admin uses gRPC-Web.
- **Stand up** an integration test with `WebApplicationFactory<TEntryPoint>` that overrides the database and the OIDC authority to point at Testcontainers-managed PostgreSQL and Keycloak, applies migrations against the ephemeral database, and exercises both the REST and gRPC surfaces with a real bearer token.
- **Configure** Testcontainers for .NET: a `PostgreSqlContainer` and a `KeycloakContainer` started once per test collection via an `IAsyncLifetime` fixture, with the dynamic connection string and issuer URL flowed into the `WebApplicationFactory<T>` via `IConfiguration` overrides. Cite <https://dotnet.testcontainers.org/>.
- **Wire** Serilog as the logging provider with structured enrichment (request id, tenant id, trace id) and a console sink, replacing the default `Microsoft.Extensions.Logging` console output. Cite <https://github.com/serilog/serilog-aspnetcore>.
- **Wire** OpenTelemetry traces and metrics: an `ActivitySource` and a `Meter` for the domain, the ASP.NET Core / HttpClient / EF Core / gRPC instrumentations, and an OTLP exporter to a local collector. Cite <https://opentelemetry.io/docs/languages/net/>.
- **Build** a GitHub Actions workflow that restores, builds all three clients, and runs the integration test suite — with Testcontainers starting PostgreSQL and Keycloak inside the runner — and gates the merge on a green result. Cite <https://docs.github.com/actions>.
- **Distinguish** an integration test (real Postgres, real Keycloak, real migrations, via `WebApplicationFactory<T>`) from a unit test (mocked dependencies), and decide which to write for a given assertion.
- **Defend** the "contract is the source of truth" rule in a code review: reject a PR that adds a hand-written DTO duplicating a `.proto` message, and explain the maintenance cost it would have created.

## Prerequisites

This is a capstone week. It assumes the whole track, not a single prior week. Specifically:

- **Week 5 of C9 complete.** You can build a Minimal-API host, register services, return `Results<T1,T2>` typed responses, and produce OpenAPI. The capstone's REST surface is Minimal APIs.
- **Week 6 of C9 complete.** You can model a schema in EF Core, write and apply migrations with `dotnet ef`, and drop to Dapper for a hot analytics query. The capstone persists to PostgreSQL.
- **Week 7 of C9 complete.** You can configure JWT bearer auth and OIDC against Keycloak, and reason about the `ClaimsPrincipal`. The capstone authenticates against a Keycloak realm.
- **Week 9 of C9 complete.** You have authored a `.proto`, generated server and client stubs, and crossed a network boundary with gRPC and gRPC-Web. The capstone's contract *is* a `.proto`.
- **Weeks 10 and 11 of C9 complete.** You have a MAUI client and a Blazor Auto app that each consumed the Week 9 contract. The capstone reuses both as the two non-backend clients.
- **A working `dotnet --version` of `9.0.x`.** This capstone targets **.NET 9** and **C# 13** across every project.
- **Docker (or Colima / Podman) running.** Testcontainers needs a container runtime reachable via the Docker socket. The integration tests will not run without it. Verify with `docker info`.
- **The MAUI workloads installed.** `dotnet workload install maui` (or at minimum `maui-android` plus one desktop head). The MAUI client must at least compile in CI even if it is not exercised by the integration tests.

## Topics covered

- **Vertical-slice planning.** Picking the thinnest path that touches every layer of every client; the scope-cut discipline ("what is the smallest thing that proves the integration?"); building depth before breadth.
- **The contract as source of truth.** One `workshop.proto`; `Grpc.Tools` generating server and client; the `<Protobuf>` MSBuild item; `GrpcServices="Server"` vs `"Client"` vs `"Both"`; why a hand-written DTO duplicating a message is a code-review reject.
- **REST and gRPC mirroring one domain.** Minimal APIs for the REST surface, a gRPC service mirroring the same operations, the shared domain model behind both, the mapping layer between proto messages and EF entities.
- **gRPC-Web for the browser.** Why a browser cannot speak native gRPC (HTTP/2 trailers), the gRPC-Web framing, `Grpc.AspNetCore.Web` on the server, `GrpcWebHandler` on the Blazor client, the CORS implications.
- **`WebApplicationFactory<TEntryPoint>`.** The in-memory test host, `ConfigureWebHost` overrides, replacing the `DbContext` registration and the OIDC authority, getting both an `HttpClient` and a gRPC channel against the test server.
- **Testcontainers for .NET.** `PostgreSqlContainer`, `KeycloakContainer`, the `IAsyncLifetime` collection fixture, dynamic ports and connection strings, container reuse, the Ryuk resource reaper, why ephemeral beats a shared dev database.
- **Migrations in tests.** Applying `context.Database.MigrateAsync()` against the Testcontainers database before the first assertion; the difference from `EnsureCreated`; respawn/cleanup between tests.
- **Keycloak as a test dependency.** Importing a realm JSON at container start, the client and the test user, minting a token against the container's token endpoint, flowing the issuer into the factory's JWT validation.
- **Serilog.** `UseSerilog`, the `LoggerConfiguration`, structured properties, `Enrich.FromLogContext`, the request-logging middleware, the console sink, why structured logs beat formatted strings.
- **OpenTelemetry.** `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`, the ASP.NET Core / HttpClient / EF Core / gRPC instrumentations, a domain `ActivitySource` and `Meter`, the OTLP exporter, the local collector via `docker run`, trace-log correlation.
- **CI for the integration baseline.** A GitHub Actions job that restores, builds all three projects, and runs `dotnet test` with Testcontainers inside the runner; caching the NuGet packages; the Docker-in-runner requirement; gating the merge.
- **The worked milestone: the integration baseline.** The full repository skeleton — `workshop.proto`, the backend with REST + gRPC + EF Core + Serilog + OTel, the MAUI and Blazor clients compiling against generated stubs, the Testcontainers integration suite, and the green CI workflow — assembled in the order the lectures and exercises prescribe.

## Weekly schedule

The schedule adds up to approximately **36 hours**. Capstone weeks are heavier than the topic weeks because the mini-project *is* a milestone, not a warm-up. Treat the schedule as a target. The integration substrate (Testcontainers + CI) rewards an unhurried Wednesday; a flaky integration test you "fixed" by adding a `Thread.Sleep` is a bug you shipped to your future self.

| Day       | Focus                                                                    | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | Vertical-slice planning, the contract-first order, scope cuts            |    2h    |    2h     |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     6h      |
| Tuesday   | One `.proto`, three clients; REST + gRPC mirroring; gRPC-Web for Blazor  |    2h    |    2h     |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     6h      |
| Wednesday | `WebApplicationFactory<T>`, Testcontainers (Postgres + Keycloak), migrations |  2h   |    2h     |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     6h      |
| Thursday  | Serilog + OpenTelemetry wiring, the CI workflow, the two challenges       |    0.5h  |    0h     |     2.5h   |    0.5h   |   1h     |     2h       |    0.5h    |     7h      |
| Friday    | Mini-project — assemble the integration baseline, vertical slice green   |    0h    |    0h     |     0h     |    0.5h   |   1h     |     3.5h     |    0.5h    |     5.5h    |
| Saturday  | Mini-project — CI green, trace walkthrough, baseline write-up            |    0h    |    0h     |     0h     |    0h     |   0h     |     3h       |    0h      |     3h      |
| Sunday    | Quiz, review, scope-cut retrospective ("what did we defer to Week 14")   |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                                          | **8.5h** | **6h**    | **4.5h**   | **4h**    | **5h**   | **12h**      | **3h**     | **36h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./00-overview.md) | This overview (you are here) |
| [resources.md](./01-resources.md) | The Testcontainers for .NET docs, the `WebApplicationFactory<T>` reference, the Serilog and OpenTelemetry .NET docs, the gRPC / gRPC-Web references, the GitHub Actions docs, and the Keycloak realm-import reference |
| [lecture-notes/01-vertical-slice-and-the-contract-first-order.md](./02-lecture-notes/01-vertical-slice-and-the-contract-first-order.md) | Ship a vertical slice on day one; the contract-first build order; scope cuts; why UI-first sinks capstones |
| [lecture-notes/02-one-contract-three-clients.md](./02-lecture-notes/02-one-contract-three-clients.md) | One `workshop.proto`; generating server and client stubs; REST + gRPC mirroring one domain; gRPC-Web for the Blazor admin; keeping three clients honest |
| [lecture-notes/03-testcontainers-and-observability-from-commit-one.md](./02-lecture-notes/03-testcontainers-and-observability-from-commit-one.md) | `WebApplicationFactory<T>` + Testcontainers (PostgreSQL + Keycloak); migrations in tests; Serilog structured logging; OpenTelemetry traces and metrics; the CI workflow |
| [exercises/exercise-01-vertical-slice-plan.cs](./03-exercises/exercise-01-vertical-slice-plan.cs) | Define the slice as code: the domain entities, the `workshop.proto` messages, and the one path that proves the integration |
| [exercises/exercise-02-contract-and-mapping.cs](./03-exercises/exercise-02-contract-and-mapping.cs) | Implement the gRPC service against the proto, mirror one REST endpoint, write the proto↔entity mapping, prove both surfaces agree |
| [exercises/exercise-03-testcontainers-integration.cs](./03-exercises/exercise-03-testcontainers-integration.cs) | A `WebApplicationFactory<T>` integration test over a Testcontainers PostgreSQL + Keycloak; apply migrations; assert the vertical slice end to end |
| [exercises/exercise-04-blazor-grpc-web-client.ts](./03-exercises/exercise-04-blazor-grpc-web-client.ts) | The browser/admin side: configure the gRPC-Web channel, call the same service the MAUI client calls, handle the auth token |
| [exercises/SOLUTIONS.md](./03-exercises/SOLUTIONS.md) | Annotated solutions for the four exercises, with the expected build output and the green test run |
| [challenges/challenge-01-keycloak-realm-and-token-minting.md](./04-challenges/challenge-01-keycloak-realm-and-token-minting.md) | Stand up the Keycloak Testcontainer with an imported realm, mint a real token in-test, and validate it against the running backend |
| [challenges/challenge-02-otel-trace-across-the-slice.md](./04-challenges/challenge-02-otel-trace-across-the-slice.md) | Make one request produce one trace that spans Blazor → gRPC-Web → service → EF Core → Postgres, and read it in the collector |
| [quiz.md](./05-quiz.md) | 10 multiple-choice questions on vertical slices, the contract-first order, Testcontainers, `WebApplicationFactory<T>`, Serilog, and OpenTelemetry |
| [homework.md](./06-homework.md) | Six practice problems that consolidate the integration baseline |
| [mini-project/README.md](./07-mini-project/00-overview.md) | Full spec for the **Integration Baseline** capstone milestone — the assembled repository, the green test, the green CI |

## The "build succeeded" promise — restated for the capstone, and a new "green CI" promise

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 1.2 s
```

For the capstone we extend it. The build-succeeded contract now spans **three** projects, not one: the backend, the MAUI client, and the Blazor admin must all build against the same generated contract, in the same solution, on the same machine and in CI. A green build on the backend that leaves the MAUI client broken is not a green build — it is a broken capstone with one project that happens to compile.

We add a **green-CI contract**: *the integration baseline is not "done on my machine" — it is "green in CI."* The merge gate is a GitHub Actions run that builds all three projects and runs the integration suite against a real PostgreSQL and a real Keycloak started by Testcontainers inside the runner. A milestone that is green locally and red in CI is red. The whole point of the integration baseline is that "it works" is a fact CI can verify on every push, not a claim you make on Friday.

And we add a **trace contract**, carried forward into Weeks 14 and 15: *every request through the running system leaves a structured Serilog event and an OpenTelemetry trace behind it.* A code path you cannot find in the logs is a code path you will debug by guessing. This week wires the emission; the dashboards come in Week 14; the discipline starts now.

> **Note on packages and targets.** Every project targets **.NET 9** (`net9.0`, and `net9.0-android` / `net9.0-maccatalyst` / `net9.0-windows` for the MAUI heads). The contract uses `Grpc.AspNetCore` (server) and `Grpc.Net.Client` + `Grpc.Net.Client.Web` (clients), with `Grpc.Tools` generating from `workshop.proto`. Persistence uses `Microsoft.EntityFrameworkCore` 9 with `Npgsql.EntityFrameworkCore.PostgreSQL` and `Dapper` for analytics. Tests use `xunit`, `Microsoft.AspNetCore.Mvc.Testing` (for `WebApplicationFactory<T>`), `Testcontainers.PostgreSql`, and `Testcontainers.Keycloak`. Observability uses `Serilog.AspNetCore` and the `OpenTelemetry.Extensions.Hosting` / `OpenTelemetry.Instrumentation.*` / `OpenTelemetry.Exporter.OpenTelemetryProtocol` package family. All free, all open source.
