# Week 12 — Homework

Six practice problems that consolidate the week's material. They are sized to ~45 minutes each. Do them after the lectures and the exercises; do them before the mini-project. Cite the URLs you used while solving each one in the commit message of your homework branch.

## Problem 1 — The `Program.cs` composition audit

Take your Exercise 1–3 host (REST + gRPC + SignalR, JWT, Serilog, OpenTelemetry, EF Core). Refactor every cross-cutting registration out of the body of `Program.cs` and into a single `ServiceConfiguration` static class exposing four extension methods: `AddProjectHubAuth(this IServiceCollection, IConfiguration)`, `AddProjectHubLogging(this WebApplicationBuilder)`, `AddProjectHubTelemetry(this IServiceCollection, IConfiguration)`, and `AddProjectHubPersistence(this IServiceCollection, IConfiguration)`. The body of `Program.cs` should read as a list of four calls plus the route mapping.

Then write a 200-word note explaining:

1. Why "configure once, register everywhere" is an integration discipline and not just a style preference (hint: enumerate two concrete bugs the discipline prevents — e.g. two different JSON serializer settings, a `DbContext` scoped on one path and singleton on another).
2. Which of the four registrations is order-sensitive relative to the others, and which are not.

Cite the host-configuration chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host> and the middleware-ordering chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/#middleware-order>.

Deliverable: `homework/01-composition-audit.md` plus the refactored `ServiceConfiguration.cs` and `Program.cs`.

## Problem 2 — One token, three surfaces

Mint a single JWT with an `org_id` claim. Using that one token, authenticate against all three protocol surfaces and capture the evidence:

1. **REST:** `curl -k -H "authorization: bearer $TOKEN" https://localhost:5001/api/whoami` → `200` with the claims.
2. **gRPC:** `grpcurl -insecure -H "authorization: bearer $TOKEN" -d '{}' localhost:5001 projecthub.Projects/WhoAmI` → the org id echoed back.
3. **SignalR:** open a `HubConnection` (the .NET `Microsoft.AspNetCore.SignalR.Client` package) with `o.AccessTokenProvider = () => Task.FromResult(token)`, invoke `BroadcastTest`, and capture the echoed event.

Then write 250 words on the three different *transports* the same token rode on: the `Authorization` header (REST), gRPC metadata (which becomes the `authorization` HTTP/2 header on the wire), and the `access_token` query string (SignalR's WebSocket upgrade). Explain why one `AddAuthentication("Bearer").AddJwtBearer(...)` registration covers all three despite the token arriving in three different places.

Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz> and <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>.

Deliverable: `homework/02-one-token-three-surfaces.md` with the three captures and the write-up.

## Problem 3 — Read the trace by hand

Run your host with the OpenTelemetry console exporter (Exercise 2). Fire the cross-protocol request — a REST `POST` that writes via EF Core and broadcasts to SignalR. Capture the full console-exporter output for the request (it will be several spans).

For each span, extract and tabulate: `TraceId`, `SpanId`, `ParentSpanId`, `OperationName`, `Duration`. Then:

1. Draw the parent-child tree from the `ParentSpanId` links. Identify the root span (the one whose `ParentSpanId` is all zeros or absent).
2. Confirm every span shares one `TraceId`.
3. Identify which span owns the most wall-clock time (the critical-path span).
4. Explain in 150 words why a flat log stream — even one with a request id in every line — could not give you the parent-child latency attribution that the trace gives you for free.

Cite <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing> and the semantic conventions at <https://opentelemetry.io/docs/specs/semconv/>.

Deliverable: `homework/03-read-the-trace.md` with the raw export, the span table, the hand-drawn tree, and the write-up.

## Problem 4 — Reproduce and fix the DbContext scoping trap

Register a singleton service `StatusReportSweeper` that, on a timer, queries the `tasks` table and logs a count. Wire it the **wrong** way first — inject `ProjectHubDbContext` directly into the singleton's constructor. Boot the app and trigger two near-simultaneous sweeps (or run a sweep concurrently with an inbound REST request that touches the same context).

1. Capture the exact exception. (Expected: `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.`)
2. Explain why a singleton capturing a scoped `DbContext` is a captive-dependency bug, and why `DbContext`'s lack of thread-safety makes it surface as that specific exception.
3. Fix it two ways: (a) inject `IServiceScopeFactory` and create a fresh scope per sweep, and (b) register and inject `IDbContextFactory<ProjectHubDbContext>` and create a context per sweep. Show both diffs.
4. Write 150 words on when you reach for `IServiceScopeFactory` vs `IDbContextFactory<T>` (hint: the factory is the cleaner choice when the *only* scoped dependency you need is the context; the scope factory when you need several scoped services together).

Cite <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues> and <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#scoped-service-as-singleton>.

Deliverable: `homework/04-dbcontext-scoping.md` with the captured exception and both fix diffs.

## Problem 5 — Configuration layering, four ways

Take the PostgreSQL connection string, the JWT signing key, and the OTLP exporter endpoint. Demonstrate that each can be supplied — and overridden — from four configuration sources without rebuilding the binary:

1. A baseline in `appsettings.json`.
2. An environment-specific override in `appsettings.Development.json`.
3. A user-secrets override (`dotnet user-secrets set "Jwt:SigningKey" "..."`).
4. An environment-variable override (`ConnectionStrings__ProjectHub=...` / `Jwt__SigningKey=...`).

For each of the three settings, set conflicting values in all four sources, boot the app, and log the resolved value (add a one-time startup log line that prints the *effective* connection string host, the JWT key length, and the OTLP endpoint — never the secrets themselves). Confirm the precedence order: env var > user secrets > `appsettings.{Environment}.json` > `appsettings.json`.

Write 200 words on why secrets never belong in `appsettings.json` (it is checked into source control) and what the production answer is (environment variables injected by the orchestrator, or a secrets manager — Azure Key Vault, AWS Secrets Manager, SOPS).

Cite <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/> and <https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets>.

Deliverable: `homework/05-config-layering.md` with the four-source table and the resolved values.

## Problem 6 — An integration test per endpoint

Using `WebApplicationFactory<Program>` and a Testcontainers `PostgreSqlContainer` (Exercise 4), write one xUnit test per endpoint you have shipped so far:

1. `GET /api/ping` returns `200` anonymously.
2. `GET /api/whoami` returns `401` without a token and `200` with a token carrying `org_id`.
3. `POST /api/projects` creates a project scoped to the token's `org_id` and the row is visible in the container's database.
4. A gRPC `WhoAmI` call with the token echoes the org id.
5. A `HubConnection` against the in-test server connects with a query-string token and receives a broadcast.

Each test must boot the app against a *fresh* container (use `IClassFixture` to share one container per test class, or one per test if you want full isolation). Assert on real `HttpResponseMessage` / gRPC response / hub event, not mocks.

Then write 150 words on why these are "deployable invariants" and not "unit tests": they prove the REST contract, the gRPC contract, the SignalR contract, the EF Core migration, and the JWT shape all *compose* without surprise — the thing a unit test can never prove because a unit test never boots the host.

Cite <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, <https://xunit.net/>, and <https://github.com/testcontainers/testcontainers-dotnet>.

Deliverable: `homework/06-integration-tests/` with the test project and a short note on which endpoints are covered.

## Submission

Push the six deliverables on a branch named `week12-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link to each of the six files and include a 100-word summary of what you learned.

## Grading rubric

Each problem is worth a share of 100 points. The teaching staff reviews homework PRs within 5 business days.

- **Problem 1 — composition audit (15 pts).** `ServiceConfiguration` exists with all four extension methods; `Program.cs` body reads as four calls plus routing; the 200-word note names two concrete bugs the discipline prevents and correctly identifies which registration is order-sensitive. (10 pts) build is clean / 0 warnings; (5 pts) the note is correct, not hand-wavy.
- **Problem 2 — one token, three surfaces (15 pts).** All three captures present and passing (9 pts, 3 each); the write-up correctly explains the three transports and why one registration covers them (6 pts).
- **Problem 3 — read the trace (20 pts).** Raw export captured (4 pts); span table complete with all five fields (6 pts); parent-child tree correct and root span identified (5 pts); the 150-word "flat logs can't do this" argument is correct (5 pts).
- **Problem 4 — scoping trap (20 pts).** The exact exception reproduced and captured (6 pts); both fixes (`IServiceScopeFactory` and `IDbContextFactory<T>`) shown as diffs and both compile and run clean (10 pts); the when-to-use-which note is correct (4 pts).
- **Problem 5 — config layering (15 pts).** All four sources demonstrated for all three settings (8 pts); precedence order verified by logged effective values, with secrets never printed (4 pts); the "secrets out of appsettings" write-up names a real production answer (3 pts).
- **Problem 6 — integration tests (15 pts).** At least four of the five endpoint tests present and green against a real container (10 pts); the "deployable invariant" note correctly distinguishes integration from unit tests (5 pts).

Point deductions that cut across every problem: a missing citation on a non-trivial claim (-2 each, the single most common review comment), a build that produces warnings (-3), and any secret value printed in a log or committed to the branch (-10 and a request to rotate the key). Preempt the citation deduction by linking the Microsoft Learn URL or GitHub source for every non-trivial assertion.

Cited Microsoft Learn pages this homework draws from: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>, <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>, <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/>, <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. External: the Serilog wiki, the OpenTelemetry .NET SDK, xUnit, and Testcontainers for .NET.
