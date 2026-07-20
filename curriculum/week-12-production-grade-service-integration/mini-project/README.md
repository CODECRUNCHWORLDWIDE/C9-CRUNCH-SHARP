# Mini-Project — ProjectHub: One Deployable Serving REST, gRPC, and SignalR Behind One JWT Scheme, with PostgreSQL, Serilog, OpenTelemetry, and Integration Tests

> **Time:** 8.5 hours across Thursday-Saturday-Sunday. **Prerequisites:** Exercises 1-4 and (ideally) both challenges. **Citations:** every Microsoft Learn URL referenced in the three lecture notes, the Serilog and OpenTelemetry repositories, the Testcontainers .NET project, the Npgsql EF Core provider.

## The spec

You are building **ProjectHub**, a small multi-tenant project-management backend that demonstrates every concept from Week 12 composed into one deployable process. It is the same service the lectures and exercises built piecemeal; the mini-project assembles the pieces into a service that boots, serves three protocols, logs structurally, traces end to end, persists to PostgreSQL, and ships with an integration test suite and a `docker-compose.yml` that runs the lot. The runtime topology:

```
                  +-------------------+
                  |  curl / grpcurl   |
                  |  / wscat client   |
                  +---------+---------+
                            |
                            v
              +-----------------------------+
              |        ProjectHub host       |   (.NET 8, port 8080)
              |  +-----------------------+   |
              |  | REST  (minimal APIs)  |   |
              |  | gRPC  (Projects svc)  |   |---- one JWT bearer scheme
              |  | SignalR (events hub)  |   |---- one Serilog logger
              |  +-----------+-----------+   |---- one OpenTelemetry tracer
              |              |               |
              |   ProjectHubDbContext (pool) |
              +--------------+--------------+
                             |
              +--------------+--------------+
              v                             v
        +-----------+                 +-----------+
        | postgres  |   (port 5432)   |  jaeger   |   (UI 16686, OTLP 4317)
        +-----------+                 +-----------+
```

Everything runs via `docker compose up`. One process serves REST CRUD on projects and tasks, a gRPC mirror service for internal consumers, and a SignalR hub that broadcasts status changes. All three are behind one JWT bearer scheme; all three share one `ProjectHubDbContext`; every request produces structured logs and one distributed trace.

## Functional requirements

### F1 — Composition

- One `Program.cs` registers the three protocol surfaces via the `ServiceConfiguration` extension pattern: `AddProjectHubAuth`, `AddProjectHubLogging`, `AddProjectHubTelemetry`, `AddProjectHubPersistence`, plus `AddProjectHubRest`, `AddProjectHubGrpc`, `AddProjectHubSignalR`.
- The middleware order is fixed and documented inline: `UseSerilogRequestLogging` → `UseRouting` → `UseAuthentication` → `UseAuthorization` → endpoint mapping (`MapGroup`, `MapGrpcService`, `MapHub`).
- A `RequireOrg` authorization policy requires the `org_id` claim be present; every protected endpoint, method, and the hub use it.

### F2 — Authentication

- A single `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` registration covers REST, gRPC, and SignalR.
- The JWT middleware's `OnMessageReceived` lifts the `access_token` query-string parameter into `context.Token` for paths starting with `/hubs`.
- A dev-only `POST /dev/mint-token` endpoint mints a 10-minute JWT with `sub`, `org_id`, and `jti` claims. In production this endpoint is compiled out (or gated behind `app.Environment.IsDevelopment()`); tokens come from the real provider.

### F3 — REST surface

- `POST /api/projects`, `GET /api/projects` (paginated), `GET /api/projects/{id}`, `PATCH /api/projects/{id}`, `DELETE /api/projects/{id}` — all scoped to the caller's `org_id`.
- `POST /api/projects/{id}/tasks`, `GET /api/projects/{id}/tasks`, `PATCH /api/tasks/{id}` (status change) — also org-scoped.
- All responses use `Results<...>` typed results and `Results.Problem(...)` (RFC 7807) for failures. OpenAPI is exposed in Development only.

### F4 — gRPC surface

- A `projecthub.Projects` service mirrors the REST read surface: `WhoAmI`, `List(ListProjectsRequest)`, `Get(GetProjectRequest)`. It serves the same org-scoped data on the same authorization model.
- The `.proto` lives in a shared project and is compiled via `Grpc.Tools`; the contract types are referenced by the integration-test project too.

### F5 — SignalR surface

- A `ProjectEventsHub` at `/hubs/events`, `[Authorize]` with the `RequireOrg` policy. On connect, the connection joins the group `org-{org_id}`.
- A REST `POST /api/projects` and a `PATCH /api/tasks/{id}` (status change) trigger broadcasts (`ProjectCreated`, `TaskStatusChanged`) to the org group via a singleton `ProjectEventsBroadcaster` that resolves a fresh `DbContext` per broadcast through `IDbContextFactory<T>`.

### F6 — Persistence

- `ProjectHubDbContext` against PostgreSQL 16 via `Npgsql.EntityFrameworkCore.PostgreSQL`, registered with `AddDbContextPool`.
- `Project` and `Task` entities, snake_case table names, an index on `organization_id` and on `project_id`, a cascade delete from `projects` to `tasks`.
- One checked-in `InitialCreate` migration. Applied on startup via `Database.MigrateAsync()` in Development; documented as a manual `dotnet ef database update` step in production.

### F7 — Observability

- Serilog as the global logger via `UseSerilog()`, compact JSON formatter, enrichers (machine name, environment, `Application=projecthub`), and a 7-day rolling file sink.
- `UseSerilogRequestLogging` with `EnrichDiagnosticContext` adding `UserId` and `OrgId` to every request line.
- OpenTelemetry traces with the four instrumentations (ASP.NET Core, HttpClient, gRPC client, Npgsql) plus the `ProjectHub` `ActivitySource`; metrics with the runtime instrumentation plus three custom meters (`projects.created`, `projects.deleted`, `projects.list_latency_ms`).
- Exporter switches on `OpenTelemetry__Exporter`: `Console` in dev, `Otlp` (to Jaeger) under docker-compose.
- A `/health` endpoint returns 200 when PostgreSQL is reachable; it is excluded from tracing via the AspNetCore instrumentation `Filter`.

### F8 — Integration tests

- An xUnit test project using `WebApplicationFactory<Program>` and `Testcontainers.PostgreSql`.
- At least one test per endpoint shipped (the integration-test contract): REST CRUD, gRPC `List`/`Get`, the SignalR cross-protocol broadcast, and the auth-rejection paths (401 without a token on each surface).
- A `TestTokenIssuer` mints JWTs with the test signing key the factory injects.

## Non-functional requirements

### NF1 — Build and run

- `docker compose up` brings up Postgres, Jaeger, and ProjectHub in under 60 seconds on commodity hardware.
- `curl http://localhost:8080/health` returns 200 once the stack is up.
- `dotnet test` passes against a Testcontainers PostgreSQL in under 90 seconds, with the Docker daemon running.

### NF2 — Code quality

- C# uses nullable references enabled and file-scoped namespaces.
- Every endpoint and hub method has explicit input validation; failures return Problem Details (REST), a gRPC status code (gRPC), or a `HubException` (SignalR).
- No singleton captures a scoped service; the DI validation passes on startup in Development.

### NF3 — Citations

- Every non-trivial implementation choice carries a citation comment pointing at Microsoft Learn or the relevant GitHub source.
- `README.md` lists every external dependency with version and license.

## Suggested project layout

```
ProjectHub/
├── docker-compose.yml
├── Dockerfile               <-- multi-stage build (sdk:8.0 → aspnet:8.0)
├── TRACE.md                 <-- the trace write-up (see below)
├── README.md                <-- top-level description, build, run, runbook stub
├── src/
│   ├── ProjectHub/
│   │   ├── ProjectHub.csproj
│   │   ├── Program.cs
│   │   ├── Configuration/
│   │   │   ├── AuthServiceConfiguration.cs
│   │   │   ├── LoggingHostConfiguration.cs
│   │   │   ├── TelemetryServiceConfiguration.cs
│   │   │   ├── PersistenceServiceConfiguration.cs
│   │   │   ├── RestServiceConfiguration.cs
│   │   │   ├── GrpcServiceConfiguration.cs
│   │   │   └── SignalRServiceConfiguration.cs
│   │   ├── Endpoints/ProjectEndpoints.cs
│   │   ├── Grpc/ProjectsGrpcService.cs
│   │   ├── Hubs/ProjectEventsHub.cs
│   │   ├── Hubs/ProjectEventsBroadcaster.cs
│   │   ├── Data/ProjectHubDbContext.cs
│   │   ├── Data/Project.cs
│   │   ├── Data/ProjectTask.cs
│   │   ├── Migrations/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   └── ProjectHub.Contracts/
│       ├── ProjectHub.Contracts.csproj
│       └── projects.proto
└── tests/
    └── ProjectHub.IntegrationTests/
        ├── ProjectHub.IntegrationTests.csproj
        ├── ProjectHubFactory.cs
        ├── TestTokenIssuer.cs
        ├── ProjectsRestTests.cs
        ├── ProjectsGrpcTests.cs
        └── ProjectEventsHubTests.cs
```

## Starter files

A small starter scaffold is provided in `mini-project/starter/`. Copy it as your starting point:

- `Program.cs` — the composition skeleton calling the seven `Add*`/`Map*` extensions, with stubs.
- The seven `*Configuration.cs` files — signatures present, bodies stubbed from the lecture examples.
- `projects.proto` — the gRPC contract, complete.
- `ProjectHubFactory.cs` — the `WebApplicationFactory<Program>` + Testcontainers fixture, complete.
- `appsettings.Development.json` — config with placeholder JWT key, connection string, and OTLP settings.

The starter compiles but does not run end to end. Your work is to fill in the stubbed configuration bodies, the endpoints, the gRPC service, the hub and broadcaster, the entities and migration, and the integration tests, then write the Dockerfile, the `docker-compose.yml`, and the trace write-up.

## The trace write-up (`TRACE.md`)

Run the application and capture these measurements. Treat the trace write-up as part of the deliverable, not an afterthought.

### M1 — Cold start

`docker compose up` from clean; how long until `curl /health` returns 200? Target: under 60 seconds on commodity hardware. Report the breakdown (image pulls vs container starts vs migration apply).

### M2 — The single trace, console exporter

Issue one `POST /api/projects` with a minted token under `OpenTelemetry__Exporter=Console`. Capture the stdout span dump. Confirm at least three spans (REST `Server`, Npgsql `INSERT`, the `ProjectCreatedBroadcast` `Internal`) share one `TraceId`, with the latter two's `ParentSpanId` equal to the REST span's `SpanId`. Paste the dump.

### M3 — Logs carry the same trace ID

From the same request, confirm via `docker logs projecthub 2>&1 | grep '"@mt"' | jq -r '.TraceId' | sort -u` that the request's log lines collapse to one trace ID and it equals the span trace ID. Paste the result.

### M4 — Render in Jaeger

Flip to `OpenTelemetry__Exporter=Otlp`, re-issue the POST, open <http://localhost:16686/>, find the trace, and screenshot the flame graph. Confirm the `db.statement` tag on the Npgsql span and the `projecthub.project_id` tag on the broadcast span are visible.

### M5 — Cross-protocol broadcast

Connect a `wscat` (or browser) SignalR client for an org, issue a REST `POST /api/projects` for the same org, and confirm the `ProjectCreated` event arrives. Confirm a connection for a *different* org does not receive it. Report the path and the trace.

### M6 — Auth across three surfaces

Exercise each surface with and without a token: REST `GET /api/whoami` (200 / 401), gRPC `WhoAmI` (OK / Unauthenticated), SignalR negotiate (200 / 401). Tabulate the six results.

### M7 — The integration suite

Run `dotnet test` and report: the number of tests, the total run time, and the breakdown (container start, host boot, migration apply, test bodies). Confirm every shipped endpoint has at least one test (the integration-test contract).

## Grading rubric

- **40 points: functional correctness.** Every functional requirement (F1-F8) is implemented and demonstrable.
- **20 points: non-functional quality.** Build is clean (0 warnings); code is idiomatic; no scoped-from-singleton captures; citations present.
- **15 points: the trace write-up.** All seven measurements (M1-M7) are reported with captured output and a one-sentence interpretation each.
- **10 points: cross-protocol observability.** A single `POST /api/projects` produces one trace ID across the REST span, the SQL span, and the broadcast span, and the same ID in every log line — captured in both console and Jaeger form.
- **10 points: integration tests.** Every shipped endpoint has at least one `WebApplicationFactory<Program>` + Testcontainers test, including the 401 paths on all three surfaces.
- **5 points: deployability.** The multi-stage `Dockerfile` builds; `docker compose up` brings up the full stack and `curl /health` returns 200.

## Stretch goals

1. **Self-call gRPC fan-out.** Have the REST create handler issue an outbound gRPC `List` call to the host's own service so the trace gains a `GrpcClient` span and a second inbound `Server` span. Verify the `traceparent` header propagates. Cite <https://www.w3.org/TR/trace-context/>.
2. **Second auth scheme.** Add the `InternalRpc` named scheme (different key/issuer/audience) and put it on one gRPC method that only internal services may call. Add a test proving a default-scheme token is rejected on that method and an `InternalRpc` token is accepted. This is the Week 14 service-to-service auth foundation.
3. **Prometheus metrics endpoint.** Add the `OpenTelemetry.Exporter.Prometheus.AspNetCore` exporter and a `/metrics` scrape endpoint; confirm the three custom meters and the runtime counters appear. Cite the OpenTelemetry metrics docs at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>.
4. **Respawn between tests.** Replace the per-test `orgId = Guid.NewGuid()` isolation with `Respawn` truncating the `public` schema between tests; switch the fixture to `ICollectionFixture<T>` so one container serves the whole suite. Report the run-time delta. Cite <https://github.com/jbogard/Respawn>.
5. **Outbox preview.** Replace the in-handler broadcaster call with an `outbox` table write and a `BackgroundService` that drains it and broadcasts. Discuss why this decouples the request trace from the broadcast trace and why that is the correct shape (the Week 13 pattern). Cite the background-services docs at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services>.

## Submission

Push the project on a branch named `week12-mini-project/<your-handle>` and open a PR against the C9 curriculum repository. The PR description must link to `TRACE.md` and include the Jaeger flame-graph screenshot from M4 and the `dotnet test` summary from M7.

The teaching staff reviews mini-project PRs within 7 business days. Reviews focus on (a) whether the eight functional requirements are met, (b) whether one request produces one trace across three surfaces, (c) whether every endpoint has an integration test, and (d) whether the code reads like the editorial code style of the lecture-note examples.

Cited Microsoft Learn pages: every page referenced in the three lecture notes plus <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container> for the multi-stage Dockerfile. Source-link references: `JwtBearerHandler.cs`, `EndpointMiddleware.cs`, `Hub.cs` in `dotnet/aspnetcore`. External: the Serilog org at <https://github.com/serilog>, the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, Testcontainers for .NET at <https://github.com/testcontainers/testcontainers-dotnet>, and the Npgsql EF Core provider at <https://github.com/npgsql/efcore.pg>.
