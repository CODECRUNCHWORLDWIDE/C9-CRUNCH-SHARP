# Mini-Project — ProjectHub: One Deployable Serving REST, gRPC, and SignalR Behind JWT, with PostgreSQL, Serilog, OpenTelemetry, and an Integration Test Suite

> **Time:** 8.5 hours across Thursday–Saturday–Sunday. **Prerequisites:** Exercises 1–4 and (ideally) both challenges. **Citations:** every Microsoft Learn URL referenced in the three lecture notes, the Serilog GitHub, the OpenTelemetry .NET SDK, xUnit, Testcontainers for .NET, and the Npgsql EF Core provider.

## The spec

You are building **ProjectHub**, a small but plausible multi-tenant project-management backend. It is the worked example from the lecture notes, assembled end to end. One process serves three protocol surfaces over the same auth, logging, tracing, and persistence pipeline. The runtime topology:

```
                  +-----------+
                  |  client   |  (curl / grpcurl / HubConnection)
                  +-----+-----+
                        |
                        v
                  +-----------+
                  | ProjectHub|   (.NET 8: REST + gRPC + SignalR, one Program.cs)
                  +-----+-----+
                        |
            +-----------+-----------+
            v                       v
      +-----------+           +-----------+
      | postgres  |           |  jaeger   |
      | (EF Core) |           | (OTLP in) |
      +-----------+           +-----------+
```

Everything runs via `docker compose up`. The service listens on `https://localhost:5080`; Postgres is the migration-controlled store for `projects` and `tasks`; Jaeger receives OTLP spans so the cross-protocol trace from Challenge 1 is queryable in a UI. Structured Serilog JSON logs roll to a file and to stdout.

This is **the same service** Week 13 extends with a background `IHostedService` that drains a Channel the hub pushes onto. Build it so that future-you, three weeks from now, can extend it without a rewrite.

## Functional requirements

### F1 — Authentication

- One `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` registration covers REST, gRPC, and SignalR.
- The `JwtBearerEvents.OnMessageReceived` hook lifts the `access_token` query-string token into `context.Token` **only** for paths starting with `/hubs/`.
- An authorization policy `RequireOrg` requires an `org_id` claim. It is applied to every protected REST endpoint, the gRPC service, and the hub.
- A development-only `POST /dev/token?user=<name>&orgId=<guid>` endpoint mints a short-lived JWT. It is removed (or `#if DEBUG`-gated) in production.

### F2 — REST surface (minimal APIs)

- `GET  /api/projects` — list the caller's org's projects.
- `POST /api/projects` — create a project scoped to the token's `org_id`.
- `GET  /api/projects/{id}` — fetch one project (404 if not in the caller's org — never 403; do not leak existence across tenants).
- `POST /api/projects/{id}/tasks` — add a task to a project.
- `POST /api/projects/{id}/tasks/{taskId}/status` — change a task's status; this is the endpoint that writes via EF Core **and** broadcasts to SignalR (the cross-protocol path).
- All of the above are behind `.RequireAuthorization("RequireOrg")` via a route group.

### F3 — gRPC surface

- A `ProjectsGrpcService` mirrors the read side for internal consumers: `ListProjects` and `GetProject`, returning the same data the REST surface serves, on the same `RequireOrg` policy.
- The service is registered with `app.MapGrpcService<ProjectsGrpcService>()` and carries `[Authorize(Policy = "RequireOrg")]` at the class level.
- The `.proto` lives in `Protos/projecthub.proto`; the `.csproj` declares it as a `<Protobuf GrpcServices="Server">` item.

### F4 — SignalR surface

- A `ProjectEventsHub` mapped at `/hubs/events`, carrying `[Authorize(Policy = "RequireOrg")]`.
- `OnConnectedAsync` adds the connection to the `org-{orgId}` group.
- The status-change REST endpoint broadcasts `TaskStatusChanged` to the connection's org group via a `ProjectEventsBroadcaster` helper that resolves the `IHubContext` safely (it is a singleton, so it must **not** capture a scoped `DbContext`).

### F5 — Persistence

- `ProjectHubDbContext` with `Project` and `ProjectTask` entities (Guid keys, `OrganizationId` for tenancy, snake_case tables, an index on `OrganizationId`).
- Registered via `AddDbContextPool` for REST/gRPC, and `AddDbContextFactory` for the broadcaster's non-request-scoped writes.
- An initial migration (`InitialCreate`) is checked in. In development the app applies pending migrations on startup; in production migrations are applied by an explicit step, never silently on boot.

### F6 — Structured logging (Serilog)

- Serilog is the global logger via `builder.Host.UseSerilog(...)`, configured with `RenderedCompactJsonFormatter` to stdout and a rolling file sink.
- Enrichers attach machine name, environment, and the trace id to every log line (`Enrich.FromLogContext()` plus the OpenTelemetry trace-id enrichment).
- Every hub method, REST handler, and gRPC method logs its dispatch with structured properties (`{ProjectId}`, `{OrgId}`, `{Status}`), never string interpolation.

### F7 — Tracing (OpenTelemetry)

- `AddOpenTelemetry().ConfigureResource(...).WithTracing(...).WithMetrics(...)` with the ASP.NET Core, HttpClient, gRPC client, and Npgsql instrumentations and a custom `ActivitySource("ProjectHub")`.
- Console exporter in development; OTLP exporter to Jaeger when `Otel:OtlpEndpoint` is set.
- The cross-protocol path (F2's status-change) produces one trace spanning the inbound HTTP span, the `UpdateTaskStatus` application span, the Npgsql `UPDATE` span, and the `BroadcastStatusChanged` span — verifiable in the Jaeger UI.

### F8 — Integration tests

- An xUnit project using `WebApplicationFactory<Program>` and a Testcontainers `PostgreSqlContainer`.
- A `CustomWebApplicationFactory` overrides the `DbContext` connection string to point at the ephemeral container.
- At least one test per F2 endpoint plus one gRPC test and one SignalR test, asserting on real responses.
- The suite runs in CI in under a minute (Docker daemon must be running).

## Non-functional requirements

### NF1 — Build and run

- `docker compose up` brings up ProjectHub + Postgres + Jaeger in under 60 seconds on commodity hardware.
- `dotnet build` is clean: 0 warnings, 0 errors. Nullable reference types are enabled.
- `dotnet test` passes against a freshly-spun container.

### NF2 — Code quality

- File-scoped namespaces, nullable enabled, `async`/`await` all the way (no `.Result`/`.Wait()`).
- Every endpoint and hub method validates input and returns a presentable error (`Results.ValidationProblem` for REST, `HubException` for the hub, an appropriate gRPC `Status` for gRPC).
- Cross-cutting registration lives in one `ServiceConfiguration` static class, not scattered through `Program.cs`.

### NF3 — Observability and citations

- A `/health` endpoint returns `200` when the `DbContext` can reach Postgres; `503` otherwise.
- The cross-protocol trace is reproducible and the README documents the exact `curl` that produces it.
- Every non-trivial implementation choice has a citation comment pointing at Microsoft Learn, the Serilog/OpenTelemetry GitHub, or the Npgsql provider.

## Suggested project layout

```
ProjectHub/
├── docker-compose.yml
├── Dockerfile
├── README.md                       <-- top-level description, build, run, the cross-protocol curl
├── src/
│   └── ProjectHub/
│       ├── ProjectHub.csproj
│       ├── Program.cs              <-- four ServiceConfiguration calls + route mapping
│       ├── ServiceConfiguration.cs <-- AddProjectHubAuth/Logging/Telemetry/Persistence (see starter)
│       ├── ProjectHubDbContext.cs  <-- EF Core context + entities (see starter)
│       ├── ProjectEndpoints.cs     <-- the minimal-API route group (see starter)
│       ├── ProjectsGrpcService.cs  <-- the gRPC mirror service
│       ├── ProjectEventsHub.cs     <-- the SignalR hub + broadcaster (see starter)
│       ├── DevTokenIssuer.cs       <-- dev-only token mint
│       ├── Protos/projecthub.proto
│       ├── Migrations/             <-- dotnet ef migrations add InitialCreate
│       ├── appsettings.json
│       └── appsettings.Development.json
└── tests/
    └── ProjectHub.IntegrationTests/
        ├── ProjectHub.IntegrationTests.csproj
        ├── CustomWebApplicationFactory.cs
        └── ProjectHubTests.cs
```

## Starter files

A starter scaffold is in `mini-project/starter/`. Copy it as your starting point:

- `ServiceConfiguration.cs` — the four extension methods, with auth fully wired and the telemetry/persistence/logging bodies stubbed with `TODO`s and citations.
- `ProjectHubDbContext.cs` — the context and the `Project` / `ProjectTask` entities, complete (this is the part you proved in Exercise 3, so it is given).
- `ProjectEndpoints.cs` — the minimal-API route group with the list/create endpoints done and the cross-protocol status-change endpoint stubbed for you to complete.
- `ProjectEventsHub.cs` — the hub plus the singleton `ProjectEventsBroadcaster`, showing the safe `IDbContextFactory`/`IHubContext` pattern; the broadcast body is yours to finish.

The starter compiles once you add the missing NuGet packages and the `.proto`, but it does not run end to end. Your work is to fill in the stubbed bodies, author the migration, write the `Dockerfile` and `docker-compose.yml`, and write the integration tests.

## The trace write-up (`TRACE.md`)

Treat this as part of the deliverable. Run the service against Jaeger and capture:

### M1 — Cold start
`docker compose up` from clean; time to first successful `GET /api/projects`. Target: under 60 seconds.

### M2 — The cross-protocol trace
Fire the status-change `curl` (documented in your top-level README). Open <http://localhost:16686>, select service `ProjectHub`, open the most recent trace. Confirm it has four spans in a parent-child waterfall (inbound HTTP, `UpdateTaskStatus`, Npgsql `UPDATE`, `BroadcastStatusChanged`) and capture a screenshot.

### M3 — One trace id, everywhere
Grep the Serilog JSON log for the request and confirm every line carries the same `traceId` as the Jaeger trace. Report the matching id.

### M4 — Tenant isolation
With two tokens for two different `org_id`s, confirm org A cannot read org B's project: `GET /api/projects/{B-project-id}` with token A returns `404`, not `403` and not the data.

### M5 — The scoping discipline holds under load
Fire 50 concurrent status-change requests. Confirm no `InvalidOperationException: A second operation was started on this context...` appears in the log — proving the broadcaster's `IDbContextFactory` usage is correct and no scoped context leaked into the singleton.

### M6 — Migration on boot vs explicit
Show the development startup log line that applies pending migrations, and confirm the production configuration does **not** apply them on boot (it logs "pending migrations: N; not applying automatically in Production").

### M7 — Health under failure
Stop the Postgres container while the service runs. Confirm `/health` flips to `503`. Restart Postgres; confirm `/health` returns to `200`.

## Grading rubric

- **40 points: functional correctness.** Every functional requirement (F1–F8) is implemented and demonstrable: the three surfaces serve, one token authenticates all three, the cross-protocol path writes and broadcasts, migrations are checked in.
- **20 points: non-functional quality.** Build is clean (0 warnings); code is idiomatic (file-scoped namespaces, nullable, no `.Result`); cross-cutting registration lives in one `ServiceConfiguration`.
- **15 points: the trace write-up.** All seven measurements (M1–M7) are reported with captured numbers/screenshots and a one-sentence interpretation each. The Jaeger four-span waterfall (M2) is the centerpiece.
- **10 points: integration tests.** `dotnet test` passes against a Testcontainers Postgres; at least one test per F2 endpoint plus the gRPC and SignalR tests; tests assert on real responses, not mocks.
- **10 points: scoping discipline.** M5 produces no second-operation exception under concurrency; the broadcaster uses `IDbContextFactory`/`IServiceScopeFactory` correctly; a reviewer can point at the line that proves the singleton never captures a scoped context.
- **5 points: source-link citations.** At least 10 distinct citation comments in the source pointing at Microsoft Learn, the Serilog/OpenTelemetry GitHub, or the Npgsql provider.

## Stretch goals

1. **Downstream gRPC into the trace.** Have the status-change handler call an internal gRPC method (point it at the same host's gRPC endpoint for the demo), attaching the JWT via `CallCredentials`. Confirm the gRPC client instrumentation joins the same trace — the waterfall is now HTTP → EF → SignalR → gRPC across a process boundary carried by the W3C `traceparent` header. Cite <https://www.w3.org/TR/trace-context/>.
2. **Sample at 10%.** Switch the sampler to `TraceIdRatioBased(0.1)`. Fire 100 requests; confirm Jaeger shows ~10 complete traces (sampling is per-trace, never half a trace). Explain why `ParentBased(TraceIdRatioBased(...))` is the right default for a service that receives `traceparent` from upstream.
3. **Strongly-typed hub.** Replace `Hub` with `Hub<IProjectClient>` and the matching interface; verify the compile error if you typo a client method name. Discuss the wire shape (the method name is still a string on the wire; the check is C#-only).
4. **Metrics, not just traces.** Add a custom `Meter("ProjectHub")` counter `projecthub.tasks.status_changed` incremented in the status-change handler, exported via the OTLP metrics pipeline. Point a Prometheus scrape at it and graph the rate. Cite <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics>.
5. **Outbox pattern preview.** Instead of broadcasting inside the request, write a `TaskStatusChanged` row to an `outbox` table in the same EF Core transaction as the status update, and have a background reader (the Week 13 hook) drain it to SignalR. Discuss the at-least-once delivery guarantee this buys versus the fire-and-forget broadcast. This is the literal seam Week 13 picks up.

## Submission

Push the project on a branch named `week12-mini-project/<your-handle>` and open a PR against the C9 curriculum repository. The PR description must link to `TRACE.md` and include the Jaeger four-span screenshot (M2) and the matching `traceId` from the Serilog log (M3).

The teaching staff reviews mini-project PRs within 7 business days. Reviews focus on (a) whether the eight functional requirements are met, (b) whether the trace write-up has real captured spans and a real screenshot, (c) whether the code reads like the editorial code style of the lecture-note examples, and (d) whether the scoping discipline holds under the M5 concurrency test.

Cited Microsoft Learn pages: every page referenced in the three lecture notes plus <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images> for the Dockerfile. Source-link references: `JwtBearerEvents.cs`, `HubConnectionHandler.cs`, the EF Core `DbContextFactory`, all in `dotnet/aspnetcore` and `dotnet/efcore`. External: the Serilog GitHub at <https://github.com/serilog/serilog>, the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, xUnit at <https://xunit.net/>, Testcontainers for .NET at <https://github.com/testcontainers/testcontainers-dotnet>, and the Npgsql EF Core provider at <https://www.npgsql.org/efcore/>.
