# C9 · Crunch Sharp — Syllabus

**Length:** 15 weeks intensive · **~540 hours full-time** · **Language: C# 13 on .NET 9** · **License: GPL-3.0**

This is the structured plan for the C9 track. Every week follows the cadence described in `README.md`: lecture, lab, reading + quiz, mini-project, review. Phase 4 (capstone) replaces the weekly mini-project with capstone milestones but keeps the rest of the structure intact.

The toolchain assumed for every week: the **.NET 9 SDK**, **VS Code + C# Dev Kit** or **JetBrains Rider Community**, **`dotnet` CLI**, **Git**, **Docker** (or `colima` / `podman`). No paid tooling is required at any point in the curriculum.

---

## Phase 1 — Foundations (Weeks 1–4)

The goal of this phase is to make the language and the runtime feel ordinary. We start at the type system and end with a fully tested async data pipeline.

### Week 1 — The C# Type System and the .NET Runtime

- **Topics**
  - Value vs reference types; `struct` vs `class`; the heap, the stack, and box/unbox.
  - Primitive types, `string`, `DateTime`, `DateOnly`, `TimeOnly`, `Guid`.
  - Nullable reference types; the `?`, `!`, and `??` operators; nullable value types.
  - `record` and `record struct`; structural equality.
  - `dotnet new`, `dotnet build`, `dotnet run`, `dotnet test`; the SDK layout.
- **Lecture.** What "managed" actually means: the CLR, the JIT, garbage collection generations, and why a `string` is a reference type even though it acts like a value.
- **Hands-on mini-project — "Ledger CLI."** A double-entry accounting CLI that parses CSV transactions, normalizes them into immutable `record` types, and prints balances. No external libraries — just the BCL.
- **Skills earned**
  - Read and write modern C# without resorting to Java or Python idioms.
  - Use records and pattern matching as the default modeling tool.
  - Run, build, and test .NET projects entirely from the CLI.

### Week 2 — Generics, Collections, and LINQ

- **Topics**
  - Generic types and methods; `where` constraints; `in`/`out` variance.
  - `List<T>`, `Dictionary<TKey,TValue>`, `HashSet<T>`, `Queue<T>`, `Stack<T>`, `ImmutableArray<T>`, `FrozenDictionary<TKey,TValue>`.
  - LINQ to objects: deferred execution, `IEnumerable<T>` vs `IQueryable<T>`.
  - Query expressions vs method syntax; `Select`, `Where`, `GroupBy`, `Join`, `Aggregate`.
  - `IEnumerable<T>` iteration costs; when to materialize.
- **Lecture.** Deferred execution explained from the iterator pattern up: why a misplaced `.ToList()` can change correctness, not just performance.
- **Hands-on mini-project — "Repo Stats."** A CLI that reads a Git log via `libgit2sharp` and produces author-by-month commit statistics. Implemented entirely with LINQ pipelines, with at least one user-defined generic extension method.
- **Skills earned**
  - Pick the right collection for the job and justify it.
  - Write LINQ that another engineer can read three months later.

### Week 3 — async/await, Tasks, and Channels

- **Topics**
  - `Task`, `Task<T>`, `ValueTask<T>`; the state machine the compiler generates.
  - `ConfigureAwait`, synchronization context, why library code defaults to `false`.
  - `CancellationToken` as a first-class parameter; `IAsyncEnumerable<T>`.
  - `System.Threading.Channels` for producer/consumer pipelines.
  - Common deadlocks and how to avoid them (`.Result`, `.Wait`, `async void`).
- **Lecture.** The async state machine, decompiled. We open a small `await` example in ILSpy and walk the generated code so async stops being magic.
- **Hands-on mini-project — "Crawler."** A bounded, concurrent web crawler with a `Channel<Uri>` work queue, a configurable parallelism level, and graceful shutdown via `CancellationToken`.
- **Skills earned**
  - Reason about async code without relying on intuition.
  - Build producer/consumer pipelines with backpressure.

### Week 4 — OOP, SOLID, and Dependency Injection

- **Topics**
  - Classes, interfaces, sealed types, abstract types, primary constructors.
  - SOLID, briefly and honestly — what each letter actually buys you in C#.
  - `Microsoft.Extensions.DependencyInjection`: registration, lifetimes, scopes.
  - Options pattern (`IOptions<T>`, `IOptionsMonitor<T>`).
  - `Microsoft.Extensions.Logging` and structured logs.
- **Lecture.** "DI in C# is a library, not a framework." We trace a single call through the service provider.
- **Hands-on mini-project — "Notifier."** A reusable library with pluggable transports (email via SMTP, console, file) wired through MS.DI, options-driven configuration, and structured logging. Ships with an xUnit test suite that swaps the transport via the container.
- **Skills earned**
  - Design small types that are easy to swap and test.
  - Wire any .NET app — console, web, mobile — with the same DI primitives.

---

## Phase 2 — Backend & Data (Weeks 5–8)

We build a real ASP.NET Core 9 service. EF Core comes early because it shows up again in MAUI and Blazor.

### Week 5 — ASP.NET Core 9 Minimal APIs and MVC

- **Topics**
  - The `WebApplication` builder; `Map*` route handlers; route groups.
  - Model binding, validation, problem details, `Results<T1,T2>` for typed responses.
  - When to choose Minimal APIs vs MVC controllers vs Razor Pages.
  - OpenAPI generation via the built-in `Microsoft.AspNetCore.OpenApi`.
  - Middleware pipeline order and the `Use*` family.
- **Lecture.** Anatomy of a request: from Kestrel through middleware, routing, model binding, the endpoint, and back out.
- **Hands-on mini-project — "Sharp Notes API."** A note-taking JSON API with Minimal API endpoints, validation, OpenAPI docs, and a Razor Pages admin form for moderation.
- **Skills earned**
  - Design a clean HTTP surface in ASP.NET Core.
  - Decide between Minimal APIs and MVC with reasons.

### Week 6 — Entity Framework Core and Dapper

- **Topics**
  - DbContext, change tracking, `DbSet<T>`, fluent vs attribute configuration.
  - Migrations (`dotnet ef`), seeding, transactions, optimistic concurrency.
  - `IQueryable<T>` translation: which LINQ operators translate, which throw, which silently client-evaluate.
  - Performance escape hatch: **Dapper** for hot queries; mixing it with EF.
  - SQLite for local dev, PostgreSQL for production, with the same migration set.
- **Lecture.** "EF Core is a query builder you don't have to write." Plus the three queries you should never let EF generate.
- **Hands-on mini-project — "Sharp Notes Persistence."** Add EF Core (PostgreSQL) and Dapper to the week-5 API. Implement a paginated tag search with Dapper and a full audit log with EF Core change interceptors.
- **Skills earned**
  - Model a relational schema fluently in EF Core.
  - Know when to bypass EF for raw SQL — and how to do it safely.

### Week 7 — Authentication, Authorization, and Identity

- **Topics**
  - ASP.NET Core Identity vs custom user stores.
  - Cookie auth, JWT bearer auth, refresh tokens.
  - OAuth2 / OIDC with an external provider (e.g. **Keycloak** or **Auth0** free tier).
  - Authorization policies, requirements, handlers; resource-based authorization.
  - CSRF, CORS, HTTPS, the secure-cookie defaults.
- **Lecture.** "Authentication is who; authorization is what." Plus the three places people leak tokens.
- **Hands-on mini-project — "Sharp Notes Auth."** Add Identity-backed user accounts, JWT for the API surface, OIDC sign-in via a Keycloak container, and a `RequireOwner` policy that enforces resource-level access.
- **Skills earned**
  - Stand up production-grade auth without writing your own crypto.
  - Reason about JWT lifetime, refresh, and revocation.

### Week 8 — Real-time with SignalR + Background Work

- **Topics**
  - SignalR hubs, groups, JSON and MessagePack protocols.
  - Scaling SignalR (Redis backplane, Azure SignalR Service).
  - `IHostedService`, `BackgroundService`, queued background tasks.
  - Outbox pattern for reliable side effects.
  - **Polly** for resilience: retry, circuit breaker, timeout, bulkhead.
- **Lecture.** Why SignalR is still the right answer for browser-to-server real-time in .NET.
- **Hands-on mini-project — "Sharp Notes Live."** Add a SignalR hub for live collaboration on a note, a `BackgroundService` that emails on @-mentions through an outbox table, and Polly policies on every outbound HTTP call.
- **Skills earned**
  - Ship real-time features without losing data on reconnect.
  - Make resilience a habit, not a feature.

---

## Phase 3 — Cross-platform & Performance (Weeks 9–12)

We expand outward from the backend. The mobile, desktop, and admin surfaces all consume the same typed contract.

### Week 9 — gRPC, gRPC-Web, and Typed Contracts

- **Topics**
  - Protobuf, `.proto` files, code generation in `Grpc.AspNetCore`.
  - Unary, server-streaming, client-streaming, bidirectional streaming.
  - gRPC-Web for browser clients; interop with MAUI clients.
  - **Refit** for typed REST clients where gRPC is overkill.
  - Versioning a service contract without breaking existing clients.
- **Lecture.** "REST is a style; gRPC is a contract." When to pick which.
- **Hands-on mini-project — "Notes Contract."** Define a shared `.proto` contract for the Sharp Notes domain. Add a gRPC service to the backend and a generated client library that will be consumed in weeks 10 and 11.
- **Skills earned**
  - Author a clean protobuf contract.
  - Generate, version, and ship typed clients.

### Week 10 — .NET MAUI: Cross-platform Mobile and Desktop

- **Topics**
  - MAUI project structure; the single-project model; XAML and MVU.
  - **CommunityToolkit.Mvvm** for source-generated `ObservableObject`s and commands.
  - Navigation, dependency injection in MAUI, platform-specific code via partial classes.
  - Local persistence with `SQLite-net-pcl` and EF Core SQLite.
  - Consuming the week-9 gRPC contract from MAUI.
- **Lecture.** "MAUI is .NET on every screen." Honest tradeoffs vs Flutter and React Native.
- **Hands-on mini-project — "Sharp Notes Mobile."** A MAUI app that signs in via OIDC, syncs notes through the gRPC contract, works offline against a local SQLite store, and pushes changes when connectivity returns.
- **Skills earned**
  - Ship one codebase to iOS, Android, macOS, and Windows.
  - Design offline-first data flow with sync reconciliation.

### Week 11 — Blazor (Server + WASM) and MAUI Blazor Hybrid

- **Topics**
  - Blazor Server vs Blazor WebAssembly vs Blazor United / Auto in .NET 9.
  - Components, render modes, parameters, cascading values, JS interop.
  - **MudBlazor** as a component library; theming the amethyst Sharp accent.
  - MAUI Blazor Hybrid: rendering Blazor components inside the MAUI shell.
  - Authentication state in WASM clients.
- **Lecture.** "Pick your render mode on purpose." A decision tree for Server, WASM, and Auto.
- **Hands-on mini-project — "Sharp Notes Admin."** A Blazor Auto admin dashboard with MudBlazor, consuming the gRPC-Web contract, with charts for usage, tag clouds, and a moderation queue.
- **Skills earned**
  - Build a credible admin UI without leaving C#.
  - Decide between Blazor render modes with confidence.

### Week 12 — Performance, Source Generators, and Unity

- **Topics**
  - `Span<T>`, `Memory<T>`, `ReadOnlySpan<byte>`, stack allocation, `ref struct`.
  - `ArrayPool<T>`, `MemoryPool<T>`, pooled `StringBuilder`.
  - `BenchmarkDotNet` for honest measurement.
  - Native AOT: what it gives, what it costs, what it forbids.
  - Source generators and Roslyn analyzers (intro): `[GeneratedRegex]`, `System.Text.Json` source-gen, building a tiny custom generator.
  - **Unity gameplay scripting (intro):** `MonoBehaviour`, coroutines vs async, the C# version Unity actually supports, where C9 ends and **C11 · Crunch Arcade** begins.
- **Lecture.** "Allocations are the new cycles." Plus a guided tour through one `BenchmarkDotNet` report.
- **Hands-on mini-project — "Hot Path."** Rewrite the Sharp Notes tag-search Dapper query path to use `Span<T>` and pooled buffers, prove the win with `BenchmarkDotNet`, and ship a Native AOT publish of a small companion CLI. Plus a one-scene Unity sample that wires a `MonoBehaviour` to a local .NET 9 console via `NamedPipes`.
- **Skills earned**
  - Measure before optimizing; optimize when measurements demand it.
  - Recognize where Unity's C# differs from .NET 9's C#.

---

## Phase 4 — Capstone (Weeks 13–15)

Weeks 13–15 replace the weekly mini-project with capstone milestones. The lecture/lab/reading/review rhythm continues, focused on the capstone.

### Week 13 — Capstone build week

- **Topics**
  - Project planning, scope cuts, vertical-slice delivery.
  - Integration testing with `WebApplicationFactory<T>`; **Testcontainers for .NET** for ephemeral PostgreSQL and Keycloak.
  - **Serilog** structured logging, **OpenTelemetry** traces and metrics.
- **Lecture.** "Ship a vertical slice on day one." How to keep three clients honest against one contract.
- **Hands-on capstone milestone — Integration baseline.** All three clients (API, MAUI, Blazor) compile against the shared gRPC contract, with integration tests green in CI.
- **Skills earned**
  - Drive an end-to-end build from a contract, not from the UI.
  - Use Testcontainers as a default integration-test substrate.

### Week 14 — Capstone harden week

- **Topics**
  - Threat modeling at the API boundary; the OWASP API Top 10 in .NET.
  - **MediatR** for request/response handlers; **AutoMapper** for DTO mapping (and when to skip it).
  - Observability: structured logs, metrics, traces, exemplars.
- **Lecture.** "Hardening is editing." We delete more than we add.
- **Hands-on capstone milestone — Production polish.** Auth fully covered by integration tests, logs/metrics/traces flowing to a local Grafana + Loki + Tempo stack, MediatR introduced where it earns its keep, AutoMapper used only where DTOs warrant it.
- **Skills earned**
  - Operate a service you can debug from logs alone.
  - Use MediatR and AutoMapper deliberately, not reflexively.

### Week 15 — Capstone deploy and present

- **Topics**
  - Multi-stage Dockerfiles for ASP.NET Core and for Native AOT.
  - **GitHub Actions** pipelines: build, test, publish, deploy.
  - Deploy targets: **Azure Container Apps** free tier (primary), Fly.io, or any Linux container host (secondary).
  - Release notes, runbook, on-call basics.
- **Lecture.** "Deploy is a feature." The pipeline is part of the product.
- **Hands-on capstone milestone — Live demo + runbook.** The Polyglot Workshop is deployed, the MAUI client is sideloadable on an Android device, the Blazor admin is reachable on its public URL, the runbook is in the repo, and the recorded demo is in the portfolio.
- **Skills earned**
  - Take a .NET service from `dotnet new` to a live URL with one push.
  - Write the runbook your future self will thank you for.

---

## Assessment matrix

| Component | Weight | What it measures |
|----------|-------|------------------|
| Weekly mini-projects (weeks 1–12) | 40% | Mastery of the week's specific concept |
| Quizzes (weeks 1–12) | 10% | Conceptual fluency and reading retention |
| Peer code reviews (all weeks) | 10% | Ability to read and critique C# code |
| Capstone — Polyglot Workshop | 35% | End-to-end engineering: API, mobile, admin, contract, deploy |
| Career engineering pack | 5% | Interview readiness and portfolio completeness |

A passing grade is 70% overall and a passing capstone. The capstone cannot be carried by other components.

---

## Capstone — Polyglot Workshop

**One deployable system. Three clients. One contract.**

The capstone for C9 is the **Polyglot Workshop**: an ASP.NET Core 9 backend, a .NET MAUI mobile client, and a Blazor admin dashboard, all sharing a single typed gRPC contract.

**Domain (default).** A workshop / classroom platform: instructors create lessons, learners enroll, both submit and review exercises, an analytics surface aggregates progress. Learners may propose an alternative domain at the start of week 13 if it covers the same surface area.

**Required components.**

- **Backend (ASP.NET Core 9).** Minimal APIs for REST plus a gRPC service that mirrors the same domain. EF Core (PostgreSQL) for persistence, Dapper for the analytics queries. ASP.NET Identity plus OIDC via Keycloak. SignalR for live presence in a lesson. Background workers with an outbox. Polly on outbound calls. Serilog + OpenTelemetry observability.
- **MAUI client.** Signs in via OIDC, consumes the gRPC contract, works offline against a local SQLite store, syncs on reconnect, and runs on at least one of iOS and Android plus one of macOS and Windows.
- **Blazor admin.** Auto render mode, MudBlazor, consumes the gRPC-Web contract, includes a moderation queue, charts, and a tenant-aware authorization policy.
- **Shared contract.** A single `.proto` set is the source of truth. Generated client code is consumed by both MAUI and Blazor.
- **Tests.** xUnit unit tests for domain logic. Integration tests via `WebApplicationFactory<T>` and Testcontainers for PostgreSQL and Keycloak. At least one `BenchmarkDotNet` regression test for a hot path.
- **Deploy.** Multi-stage Dockerfile, GitHub Actions pipeline (build, test, publish, deploy), deployed to Azure Container Apps free tier (or equivalent). A runbook in `RUNBOOK.md`.

**Grading.** The capstone is graded on contract integrity, test coverage of meaningful paths, the quality of the deploy pipeline, and the runbook — not on visual polish.

---

## Career engineering pack

The last three weeks include parallel career engineering — small, dated artifacts you will hand to recruiters.

- **Interview prep for C#/.NET shops.** A focused review of the questions that actually show up: collection internals, async pitfalls, EF Core query translation, garbage collection generations, value-vs-reference semantics, lock-free primitives. Pairs naturally with **C13 · Hack the Interview** for the algorithm side.
- **System design dossier.** Two written designs in the C9 portfolio: one for a SignalR-backed real-time service, one for an EF-Core-backed multi-tenant API. Reviewers expect specifics.
- **Runbook.** The capstone's `RUNBOOK.md`: how to deploy, how to roll back, where logs live, how to rotate the OIDC client secret, what to do if the database fills up.
- **Portfolio surface.** A public GitHub profile linking the capstone, the weekly mini-projects, and a short writeup (one paragraph each) explaining what the project does and what it taught you. The portfolio is reviewed in week 15.

---

## License

This curriculum is published under **GPL-3.0**. See `LICENSE`. Contributions follow the org-level `CONTRIBUTING.md`.
