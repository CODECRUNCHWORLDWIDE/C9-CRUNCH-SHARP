# Week 12 Exercise Solutions

These are the worked solutions to the four exercises. Each solution shows the canonical implementation, the verification output the grader looks for, and the most common ways the exercise gets done wrong. Read your own solution first; check it against the canonical one second. The point of this file is not to be copied — the point is to surface the patterns and the failure modes so you recognize them when they show up in your own code later.

---

## Exercise 01 — Compose REST, gRPC, and SignalR

The canonical solution is structurally identical to the scaffold in the exercise file. The eight tasks all collapse into a single `Program.cs` that registers the bearer scheme, the authorization policy, the SignalR and gRPC services, and the three protocol-mapping calls.

### Verification output

Running through the eight verification commands in order should produce:

1. `GET /api/ping` returns `200` with `{ "ok": true, "ts": "..." }`. No bearer token needed.
2. `POST /dev/mint-token` returns `200` with `{ "access_token": "eyJhbGc..." }`.
3. `GET /api/whoami` with bearer header returns `200` and a claims array including `org_id`, `sub`, `jti`, `iat`, `nbf`, `exp`, `iss`, `aud`.
4. `GET /api/whoami` without bearer returns `401 Unauthorized` with a `WWW-Authenticate: Bearer` header.
5. `grpcurl projecthub.Projects/WhoAmI` with bearer returns `{ "subject": "dev-user", "orgId": "11111111-..." }`.
6. `grpcurl projecthub.Projects/WhoAmI` without bearer returns `Code: Unauthenticated (16)`.
7. `POST /hubs/events/negotiate` without `access_token` returns `401`.
8. `POST /hubs/events/negotiate?access_token=<token>` returns `200` with a JSON body containing `connectionId`, `availableTransports`, and `negotiateVersion`.

If step 8 returns `401`, the `OnMessageReceived` event is missing or its path predicate is wrong. Print the path inside the hook (`Console.WriteLine(context.HttpContext.Request.Path)`) and verify it matches `/hubs/events/negotiate`. The `StartsWithSegments("/hubs")` check is case-sensitive only when the segment count differs; if the path is `/Hubs/events/negotiate`, the check still passes because case is ignored by default for segments. The most common cause of step 8 returning 401 is the configuration loading a stale token from a previous test (token cache), not a routing problem.

### Common stumbles documented

The "common stumbles" block at the bottom of the exercise file covers the four that students hit most. One more worth calling out: if you see a console log line that reads `Failed to validate the token. IDX10503: Signature validation failed.`, the signing key your token used is not what the host is validating against. The `/dev/mint-token` endpoint must use the same `signingKey` variable the bearer registration uses; in the exercise scaffold both come from the same `Jwt:SigningKey` config entry, so the most likely cause is two different `appsettings.json` files producing two different keys. Verify with `jq -r .Jwt.SigningKey appsettings.Development.json` and compare to whatever `dotnet user-secrets list` shows.

The "stretch goal" — a second named scheme called `InternalRpc` — is the foundation of the Week 14 service-to-service auth pattern. The implementation looks like:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(/* primary scheme */)
    .AddJwtBearer("InternalRpc", options =>
    {
        // Different signing key, different issuer, different audience.
        options.TokenValidationParameters = new TokenValidationParameters { ... };
    });
```

The gRPC service then carries `[Authorize(AuthenticationSchemes = "InternalRpc")]` on the method that should only accept service-to-service calls. The REST endpoints continue to authorize against the default scheme. Two parallel pipelines, one host, no cross-talk.

---

## Exercise 02 — Serilog and OpenTelemetry

The canonical solution wires Serilog as the bootstrap logger first, replaces it with the host-configured logger via `UseSerilog`, registers OpenTelemetry with the four instrumentations and the console exporter, and exposes two endpoints (`POST /work` and `GET /fetch-self`) that demonstrate application-level spans and HTTP-client instrumentation.

### What the console output should look like

A single `curl -X POST "https://localhost:5001/work?steps=3"` should produce roughly this sequence on stdout (formatted for readability — in practice the log lines are one-per-line JSON):

```
{"@t":"2026-05-15T17:21:09.4112048Z","@mt":"DoWork starting with {Steps} steps","@l":"Information","Steps":3,"TraceId":"4bf92f3577b34da6a3ce929d0e0e4736","SpanId":"00f067aa0ba902b7","SourceContext":"ProjectHub.Exercise02.Program","MachineName":"laptop","EnvironmentName":"Development","Application":"projecthub-ex02"}
{"@t":"2026-05-15T17:21:09.4267934Z","@mt":"Step {StepIndex} completed at {ElapsedMs}ms","StepIndex":1,"ElapsedMs":15.0432,"TraceId":"4bf92f3577b34da6a3ce929d0e0e4736",...}
{"@t":"2026-05-15T17:21:09.4423812Z","@mt":"Step {StepIndex} completed at {ElapsedMs}ms","StepIndex":2,"ElapsedMs":15.0421,"TraceId":"4bf92f3577b34da6a3ce929d0e0e4736",...}
{"@t":"2026-05-15T17:21:09.4579697Z","@mt":"Step {StepIndex} completed at {ElapsedMs}ms","StepIndex":3,"ElapsedMs":15.0432,"TraceId":"4bf92f3577b34da6a3ce929d0e0e4736",...}
{"@t":"2026-05-15T17:21:09.4581234Z","@mt":"DoWork finished","TraceId":"4bf92f3577b34da6a3ce929d0e0e4736",...}
{"@t":"2026-05-15T17:21:09.4612345Z","@mt":"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms","RequestMethod":"POST","RequestPath":"/work","StatusCode":200,"Elapsed":51.2345,"TraceId":"4bf92f3577b34da6a3ce929d0e0e4736",...}

Activity.TraceId:            4bf92f3577b34da6a3ce929d0e0e4736
Activity.SpanId:             a3ce929d0e0e4736
Activity.TraceFlags:         Recorded
Activity.ActivitySourceName: ProjectHub.Exercise02
Activity.DisplayName:        DoStep-1
Activity.Kind:               Internal
Activity.StartTime:          2026-05-15T17:21:09.4112064Z
Activity.Duration:           00:00:00.0155123
Activity.Tags:
    projecthub.step_index: 1

Activity.TraceId:            4bf92f3577b34da6a3ce929d0e0e4736
Activity.SpanId:             d3ce929d0e0e4736
Activity.ParentSpanId:       a3ce929d0e0e4736
Activity.DisplayName:        DoStep-2
...

Activity.TraceId:            4bf92f3577b34da6a3ce929d0e0e4736
Activity.SpanId:             f3ce929d0e0e4736
Activity.DisplayName:        DoWork
...

Activity.TraceId:            4bf92f3577b34da6a3ce929d0e0e4736
Activity.SpanId:             10967aa0ba902b7
Activity.DisplayName:        POST /work
Activity.Kind:               Server
Activity.Tags:
    http.request.method: POST
    url.path: /work
    http.response.status_code: 200
```

All log lines and all spans share the same `TraceId` (`4bf92f35...`). The `DoStep-N` spans are children of `DoWork` which is a child of `POST /work`. The framework instrumentation produced the `POST /work` span; the application code produced the four custom spans.

### Why the TraceId-correlation works

The lecture-2 explanation is worth restating in solution-context: Serilog's `Enrich.FromLogContext()` reads `System.Diagnostics.Activity.Current` and writes `TraceId` and `SpanId` to every log line. OpenTelemetry's `AddAspNetCoreInstrumentation()` creates the activity at the start of the request and disposes it at the end. The two libraries do not know about each other — they cooperate by both reading from the runtime-supplied `Activity.Current`. This is why the correlation is free once both are wired correctly, and why it silently disappears if either is misconfigured.

The most common failure mode is `Enrich.FromLogContext()` missing from the Serilog config. The log lines will be valid JSON but will have no `TraceId` key. If your verification shows logs without a `TraceId`, that is almost always the cause.

### HttpClient instrumentation — the propagation contract

The `/fetch-self` endpoint issues an outbound HttpClient call. With `AddHttpClientInstrumentation()` registered, the SDK injects the W3C `traceparent` header on the outbound request:

```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-a3ce929d0e0e4736-01
```

The receiving end (the same host, on the inbound side) reads this header via the ASP.NET Core instrumentation and adopts the trace ID as the parent of the new request span. The result is one trace ID spanning two HTTP requests with the parent-child relationship intact. This is the W3C Trace Context standard at <https://www.w3.org/TR/trace-context/>; both ends of a service-to-service call honor it for free in .NET because the framework integrates with `Activity` natively.

### HttpClient factory wiring — the cleanup the exercise hinted at

The exercise file noted that the HttpClient factory registration was placed lazily; the cleaner pattern moves it up to the service-registration block:

```csharp
builder.Services.AddHttpClient();
// ... rest of services ...
var app = builder.Build();
```

The `AddHttpClient()` call registers `IHttpClientFactory` and the supporting types. Inject the factory into any handler; do not new up `HttpClient` directly — the framework's factory manages the underlying `HttpMessageHandler` lifetime and avoids socket-exhaustion bugs that plague long-running services that allocate a fresh `HttpClient` per call.

---

## Exercise 03 — EF Core, PostgreSQL, and migrations

The canonical solution implements the two entities, the context, the migration, and the singleton-service factory pattern. The verification block at the bottom of the exercise file is the runbook; following it produces a working CRUD surface against a real PostgreSQL.

### The migration that gets generated

After `dotnet ef migrations add InitialCreate`, the `Migrations/20260515_InitialCreate.cs` file contains roughly this (truncated for readability):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "projects",
        columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            organization_id = table.Column<Guid>(type: "uuid", nullable: false),
            name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        },
        constraints: table => { table.PrimaryKey("PK_projects", x => x.id); });

    migrationBuilder.CreateTable(
        name: "tasks",
        columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            project_id = table.Column<Guid>(type: "uuid", nullable: false),
            title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            status = table.Column<int>(type: "integer", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_tasks", x => x.id);
            table.ForeignKey(
                name: "FK_tasks_projects_project_id",
                column: x => x.project_id,
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        });

    migrationBuilder.CreateIndex(
        name: "IX_projects_organization_id",
        table: "projects",
        column: "organization_id");

    migrationBuilder.CreateIndex(
        name: "IX_tasks_project_id",
        table: "tasks",
        column: "project_id");
}
```

Two observations. First, the `uuid` type maps from `Guid`; `timestamp with time zone` maps from `DateTime` (the Npgsql provider uses `timestamptz` by default for `DateTime` values that carry kind information). Second, the snake_case naming convention emerged from the `ToTable("projects")` / `ToTable("tasks")` calls in `OnModelCreating`; without them, EF Core would have used the PascalCase entity names and you would have ended up with `Projects` and `Tasks` tables. Snake_case is the PostgreSQL convention and worth honoring; it lets you query the tables from `psql` without quoting.

### The scoping trap — reproducing it

To see the `InvalidOperationException`, comment out `AddDbContextFactory` and change the constructor of `ProjectStatsService`:

```csharp
public ProjectStatsService(ProjectHubDbContext db, ILogger<...> logger)
{
    _db = db;
    _logger = logger;
}
```

Run `dotnet run`. The host will start. The first request to `/api/stats` will fail with:

```
System.InvalidOperationException: Cannot consume scoped service
'ProjectHub.Exercise03.ProjectHubDbContext' from singleton
'ProjectHub.Exercise03.ProjectStatsService'.
```

The framework's DI validation catches this at the first resolution. In development, the validation runs on startup (the host fails to boot if any singleton captures a scoped service); in production, it runs lazily (the host boots but the first request that triggers the resolution fails). The dev-time check is one of the better reasons to keep your CI running the dev-environment startup as a smoke test.

### The factory pattern — what it does

`IDbContextFactory<T>.CreateDbContextAsync()` returns a brand-new context per call. The factory itself is a singleton (we registered it with `ServiceLifetime.Singleton`); the contexts it produces are owned by the caller via `await using`. There is no shared mutable state. The pattern is the canonical answer to "I am a singleton service that needs a database context"; it is the same pattern Blazor Server uses, which is why the Microsoft Learn doc page (<https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor>) mentions Blazor in the title.

A consequence to internalize: contexts produced by the factory are NOT pooled. Each call to `CreateDbContextAsync` allocates a fresh instance. If your singleton service is called at request rate, you are paying allocator pressure for every call. For background services that fire every few seconds the cost is irrelevant; for hot paths it matters and the answer is to make the consumer scoped rather than singleton when possible.

### Application Name in the connection string

The stretch goal asked to add `Application Name=projecthub-ex03;` to the connection string. The effect: every Npgsql connection identifies itself in `pg_stat_activity` under the chosen name. Run:

```
psql -U postgres -d projecthub_ex03 -c \
  "SELECT pid, application_name, state, query FROM pg_stat_activity WHERE datname='projecthub_ex03';"
```

You should see one or more `projecthub-ex03` rows. This is how operators identify connections in a database shared across many apps. The pooling behavior is visible too — the pool reuses connections, so the same pid persists across requests until idle eviction kicks in.

---

## Exercise 04 — Integration tests

The canonical solution is the test file as written. Six tests across two classes (`ProjectsRestTests` and `DirectDatabaseTests`), one factory class that owns the PostgreSQL container, no test doubles.

### Per-class vs per-collection fixtures

The exercise uses `IClassFixture<ProjectHubFactory>`. Each test class gets its own factory, which means its own PostgreSQL container, which means full isolation between classes at the cost of one container start (~3 seconds) per class. For two classes, that is six seconds of container overhead per test run.

If your test suite grows to twenty classes, the container overhead becomes the bottleneck. The fix is `ICollectionFixture<T>` and `[CollectionDefinition]`:

```csharp
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<ProjectHubFactory> { }

[Collection("Database")]
public class ProjectsRestTests
{
    public ProjectsRestTests(ProjectHubFactory factory) { ... }
}

[Collection("Database")]
public class DirectDatabaseTests
{
    public DirectDatabaseTests(ProjectHubFactory factory) { ... }
}
```

Now both classes share one factory and one PostgreSQL container. The cost is that tests must clean up state — Respawn or a per-test transaction. The win is the container starts once for the suite.

### Why we set unique OrgIds per test

The default per-class fixture model leaves state behind across tests within a class. `ProjectsRestTests` has four tests; each generates a fresh `Guid` for `orgId` and only operates on rows tagged with that org. The `List_returns_only_org_scoped_projects` test, in particular, would fail if a previous test had seeded data with the same org id.

The cleaner pattern, as the exercise stretch goal suggests, is Respawn between tests. The pattern looks like this:

```csharp
public class ProjectsRestTests : IClassFixture<ProjectHubFactory>, IAsyncLifetime
{
    private readonly ProjectHubFactory _factory;
    private Respawner _respawner = null!;

    public ProjectsRestTests(ProjectHubFactory factory) { _factory = factory; }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public" }
        });
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }
}
```

`IAsyncLifetime` on the test class fires `InitializeAsync` before each test method and `DisposeAsync` after each. The Respawner truncates all tables in the `public` schema between tests. Tests become order-independent without the manual `orgId = Guid.NewGuid()` discipline.

### Capturing Serilog output in failing tests

When a CI test fails, you want every log line the host produced. xUnit's `ITestOutputHelper` is the per-test stream. Wire Serilog to it:

```csharp
public class FailingTest : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;
    private readonly ITestOutputHelper _output;

    public FailingTest(ProjectHubFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;

        // Redirect Serilog to this test's output.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(_output, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
    }
}
```

Add `dotnet add package Serilog.Sinks.XUnit` to the test project. When the test fails, CI's test report will include the captured log lines, one per call, JSON-formatted, with trace IDs that you can correlate against any telemetry exported during the test run.

### Why the test suite takes ~35 seconds

Roughly:
- 3 seconds: PostgreSQL container start per class. Two classes = 6 seconds.
- 5 seconds: host boot per class (`WebApplicationFactory` constructs the host once per fixture instance).
- 1 second: migration apply (once per class).
- ~20 seconds: the test bodies themselves.
- 3 seconds: PostgreSQL container disposal per class.

The container disposal is non-trivial because Testcontainers tears the docker network down too. If you `docker ps` while the suite runs, you will see the container appear, exist for ~15 seconds, and vanish. That is the visible footprint of the test isolation.

### Common stumbles documented elsewhere

The "common stumbles" block in the exercise file covers the four most likely issues. One more: if you see `Could not find type ProjectHub.Exercise03.Program: type is not public`, top-level statements in Program.cs need a `public partial class Program { }` declaration after them. The implicit `Program` class generated for top-level statements is `internal`; the test project cannot reference an `internal` type from another assembly without an `InternalsVisibleTo` declaration. The cleaner fix is the `public partial class Program { }` line.

---

## Synthesis — how the four exercises connect

The four exercises form a sequence:

- **Exercise 01** gave you the **composition skeleton**: one Program.cs registering three protocol surfaces behind one authentication scheme.
- **Exercise 02** added the **observability layer**: every request produces structured logs and distributed traces with one shared trace ID.
- **Exercise 03** added the **persistence layer**: EF Core against PostgreSQL with proper scoping for non-request consumers.
- **Exercise 04** added the **integration-test safety net**: the entire host booted in-process against an ephemeral PostgreSQL.

The mini-project pulls all four into one service called ProjectHub. When you start the mini-project, you will not be writing new patterns — you will be assembling the patterns from these four exercises into a single, deployable service with a Dockerfile, a CI workflow, and a docker-compose that runs the host, PostgreSQL, and Jaeger together. The exercises are the cookbook; the mini-project is the meal.

Read the patterns. Reproduce them. Then move on.
