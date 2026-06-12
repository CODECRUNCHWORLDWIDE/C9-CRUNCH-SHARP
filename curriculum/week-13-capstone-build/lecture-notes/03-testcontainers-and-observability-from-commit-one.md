# Lecture 3 — Testcontainers as the Integration Substrate, Serilog and OpenTelemetry From the First Commit, and the CI That Makes Green Mean Something

> **Time:** 2 hours. Take the `WebApplicationFactory<T>` + Testcontainers material first, the Serilog + OpenTelemetry wiring second, and the CI workflow last. **Prerequisites:** Lectures 1 and 2 (the contract, the service, the mapping), Week 6 (EF Core migrations), Week 7 (JWT/OIDC against Keycloak). **Citations:** integration tests at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, Testcontainers for .NET at <https://dotnet.testcontainers.org/>, Serilog for ASP.NET Core at <https://github.com/serilog/serilog-aspnetcore>, OpenTelemetry .NET at <https://opentelemetry.io/docs/languages/net/getting-started/>, and GitHub Actions at <https://docs.github.com/actions>.

## 1. What "green" has to mean for this system

A test suite is only as honest as the thing it exercises. For a system whose entire value proposition is "three clients agree on one contract against one real database with one real auth provider," a test that mocks the database and stubs the token tells you almost nothing that matters. It tells you your C# compiles and your method calls line up. It does not tell you that your EF Core query *translates* to SQL Postgres accepts, that your migration *applies*, that your `Guid.CreateVersion7()` ids *round-trip* through `uuid` columns, that your Keycloak-issued token *validates* against your configured authority, or that the proto↔entity mapping *survives* a real serialization. Every one of those is a place the slice can be broken while every unit test stays green.

So for this week, the load-bearing test is an **integration test** with three real ingredients: the real backend (hosted in-memory by `WebApplicationFactory<T>`), a real PostgreSQL (started by Testcontainers, migrated by your real migrations), and a real Keycloak (started by Testcontainers, issuing a real token your real JWT middleware validates). When *that* test is green, "the contract works on a real database with real auth" is a fact, not a hope. That is the only kind of green the integration baseline accepts.

## 2. `WebApplicationFactory<TEntryPoint>`: the real host, in-memory

`WebApplicationFactory<TEntryPoint>` (from `Microsoft.AspNetCore.Mvc.Testing`) boots your *actual* application — your real `Program.cs`, your real middleware pipeline, your real DI registrations — on an in-memory `TestServer` instead of a real socket. You get back an `HttpClient` (for REST) and, with a little wiring, a `GrpcChannel` (for gRPC) that talk to that in-memory server with no network involved. Because it is your real `Program`, the test exercises the real wiring; because it is in-memory, it is fast and needs no port.

The `TEntryPoint` is your `Program` class. Minimal-API `Program.cs` files are top-level statements, which the compiler turns into an implicit `internal Program` class — to reference it from the test project you add `public partial class Program { }` at the bottom of `Program.cs` (or expose internals via `InternalsVisibleTo`). The factory's job is to *override* the parts of the app that must point at the test infrastructure: the database connection string and the OIDC authority.

```csharp
#nullable enable
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Workshop.IntegrationTests;

public sealed class WorkshopAppFactory(string connectionString, string oidcAuthority)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Point the real app at the Testcontainers Postgres and Keycloak.
                ["ConnectionStrings:Workshop"] = connectionString,
                ["Oidc:Authority"] = oidcAuthority,
                ["Oidc:Audience"] = "workshop-api",
            });
        });
    }
}
```

The crucial design point: the factory does **not** swap the `DbContext` for an in-memory provider, and it does **not** disable auth. Those are the two most common ways teams accidentally make their integration test prove nothing — an `UseInMemoryDatabase` test does not catch a Postgres-specific query failure, and an `AddAuthentication(_ => _.DefaultScheme = "Test")` stub does not catch a token-validation bug. The factory overrides *only the addresses* — where the database is, where the OIDC authority is — and lets the real provider and the real JWT middleware do their real jobs against the real containers. (Reference: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.)

## 3. Testcontainers: real PostgreSQL and real Keycloak, ephemeral and per-collection

Testcontainers for .NET (<https://dotnet.testcontainers.org/>) starts Docker containers from your test code, hands you their dynamically-assigned connection details, and disposes them when you are done. It uses a resource reaper (Ryuk) so a crashed test run does not leak containers. The PostgreSQL and Keycloak modules wrap the common configuration so you do not hand-roll the `docker run`.

Starting a container per *test* would be slow; starting one per *test collection* is the right granularity. An `IAsyncLifetime` fixture starts both containers once, and an xUnit collection shares them across the tests in it:

```csharp
#nullable enable
using DotNet.Testcontainers.Builders;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace Workshop.IntegrationTests;

public sealed class WorkshopFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("workshop")
        .WithUsername("workshop")
        .WithPassword("workshop")
        .Build();

    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder()
        .WithImage("quay.io/keycloak/keycloak:25.0")
        // Import a realm with the workshop-api client and a test user (the
        // realm JSON ships in the test project; see challenge 1).
        .WithResourceMapping(
            new FileInfo("Realms/workshop-realm.json"),
            "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .Build();

    public async ValueTask InitializeAsync()
    {
        // Start both in parallel; neither depends on the other.
        await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Keycloak.DisposeAsync();
    }
}

[CollectionDefinition(nameof(WorkshopCollection))]
public sealed class WorkshopCollection : ICollectionFixture<WorkshopFixture>;
```

`Postgres.GetConnectionString()` returns the connection string with the dynamically-mapped port; `Keycloak.GetBaseAddress()` returns the base URL from which the realm's issuer URL is derived. Those two values are what you flow into `WorkshopAppFactory`. Because the ports are dynamic, two test runs on the same machine never collide, and CI — where the runner is ephemeral anyway — gets the same isolation for free.

The "ephemeral beats shared" argument is worth stating plainly: a shared dev database accumulates state, drifts from the migrations, and turns "the test passed" into "the test passed against whatever happened to be in the database today." An ephemeral container is *created from the migrations every run*, so a passing test is a statement about *the migrations*, not about the database's history. That is the property that makes the green trustworthy.

## 4. Migrations in the test, not `EnsureCreated`

When the fixture has a running Postgres, the test must put the schema in it. There are two ways, and only one is honest. `context.Database.EnsureCreated()` builds the schema from the model directly, *skipping the migrations* — which means a migration bug (a missing index, a wrong column type, a migration that does not apply cleanly) sails right past the test. `context.Database.MigrateAsync()` runs your *actual migration files* against the container, which is exactly what will run against production. For an integration baseline, you apply migrations:

```csharp
public async Task<WorkshopAppFactory> CreateFactoryAsync(WorkshopFixture fixture)
{
    var factory = new WorkshopAppFactory(
        fixture.Postgres.GetConnectionString(),
        $"{fixture.Keycloak.GetBaseAddress()}realms/workshop");

    // Apply the real migrations against the ephemeral database before any
    // assertion. This is what makes "the test passed" mean "the migrations
    // apply and the schema is what the model expects".
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
    await db.Database.MigrateAsync();

    return factory;
}
```

Between tests within a collection, you may need to reset row state without paying for a container restart. The common tool is Respawn (`Respawn.Respawn`), which deletes rows in dependency order while leaving the schema intact — far cheaper than re-migrating. For the baseline, a simpler approach is acceptable: write each test to use its own tenant id or its own ids, so tests do not interfere. The mini-project uses tenant isolation; the homework explores Respawn.

## 5. The integration test itself: the vertical slice, asserted

With the factory, the containers, and the migrations, the test is the vertical slice from Lecture 1 — create, enroll, submit, list — asserted end to end over the *real* surfaces with a *real* token:

```csharp
[Collection(nameof(WorkshopCollection))]
public sealed class VerticalSliceTests(WorkshopFixture fixture)
{
    [Fact]
    public async Task Create_enroll_submit_appears_in_pending_queue()
    {
        await using var factory = await new SliceHarness(fixture).BuildAsync();

        // A real token minted against the Testcontainers Keycloak (challenge 1
        // covers the minting; here we use the harness helper).
        var instructor = await factory.TokenForAsync("instructor-1", role: "instructor");
        var learner    = await factory.TokenForAsync("learner-1",    role: "learner");

        var adminClient = factory.GrpcClient(instructor);
        var lesson = await adminClient.CreateLessonAsync(
            new CreateLessonRequest { Title = "Records 101", Body = "Value semantics." });
        Assert.False(string.IsNullOrEmpty(lesson.Id));

        var learnerClient = factory.GrpcClient(learner);
        await learnerClient.EnrollAsync(new EnrollRequest { LessonId = lesson.Id });
        var submission = await learnerClient.SubmitAsync(
            new SubmitRequest { LessonId = lesson.Id, Content = "public sealed record Point(...)" });
        Assert.Equal(SubmissionStatus.Pending, submission.Status);

        // The instructor's moderation queue shows the learner's submission.
        var pending = await adminClient.ListPendingSubmissionsAsync(
            new ListPendingSubmissionsRequest { PageSize = 50 });
        Assert.Contains(pending.Submissions, s => s.Id == submission.Id);
    }
}
```

This single `[Fact]` exercises: the proto (every message), the generated server stub (every RPC), the EF mapping (the round-trip), PostgreSQL (the migrated schema, the `uuid` columns, the inserts and the query), Keycloak (two real tokens, validated by the real JWT middleware), and the identity-in-token rule (the instructor and learner ids come from the tokens, never the request bodies). When it is green, the baseline is real. When it is red, the failure points at exactly which layer broke — and because Serilog and OpenTelemetry are already wired (next), you read the failure from the log and the trace, not from added `Console.WriteLine`.

## 6. Serilog: structured logging from `Program.cs`

The default `Microsoft.Extensions.Logging` console writes formatted strings — readable to a human, opaque to a query. Serilog writes *structured events*: every log call is a message template plus a typed property bag, so `log.LogInformation("Lesson {LessonId} created by {InstructorId}", id, sub)` records `LessonId` and `InstructorId` as queryable fields, not as text spliced into a sentence. For a system you must be able to debug from logs alone (Week 14's promise, started this week), that difference is everything.

Wiring is in `Program.cs`, before the rest of the host is built so even startup logs are structured:

```csharp
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()                       // pick up LogContext.PushProperty
    .Enrich.WithProperty("Service", "workshop-api")
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

// ... after building:
app.UseSerilogRequestLogging();   // one structured event per HTTP request
```

`Enrich.FromLogContext()` is what lets you push a property — a tenant id, a correlation id — onto an ambient scope so every log line within that scope carries it:

```csharp
using (Serilog.Context.LogContext.PushProperty("TenantId", tenantId))
{
    log.LogInformation("Lesson {LessonId} created.", lesson.Id);
    // every log line in this scope now also carries TenantId
}
```

`UseSerilogRequestLogging()` collapses the default framework's noisy per-request log lines into one summary event per request with the method, path, status code, and elapsed milliseconds — the single most useful log line you will read, and the one that makes "which request was slow" a query instead of a hunt. (Reference: <https://github.com/serilog/serilog-aspnetcore>.)

## 7. OpenTelemetry: traces and metrics, also from `Program.cs`

Serilog tells you *what happened*; OpenTelemetry tells you *how it flowed*. A trace is a tree of spans across the request — the gRPC call, the EF Core query it triggered, the outbound HTTP hop to Keycloak's discovery endpoint — each with a duration and a parent, so you can see where a 200ms request spent its 200ms. .NET's `ActivitySource` is the span emitter; OpenTelemetry's SDK collects the activities and exports them. The instrumentation packages auto-emit spans for ASP.NET Core, HttpClient, EF Core, and the gRPC client; you add a domain `ActivitySource` and `Meter` for spans and metrics the framework does not know about.

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string ServiceName = "workshop-api";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(WorkshopTelemetry.ActivitySourceName)   // our domain spans
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter())                                 // -> local collector
    .WithMetrics(metrics => metrics
        .AddMeter(WorkshopTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

The domain telemetry surface is a tiny static holder so the `ActivitySource` and `Meter` are created once and shared:

```csharp
#nullable enable
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Workshop.Api.Observability;

public static class WorkshopTelemetry
{
    public const string ActivitySourceName = "Workshop.Api";
    public const string MeterName = "Workshop.Api";

    public static readonly ActivitySource Activity = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> LessonsCreated =
        Meter.CreateCounter<long>("workshop.lessons.created");
    public static readonly Counter<long> SubmissionsReceived =
        Meter.CreateCounter<long>("workshop.submissions.received");
}
```

In the service, you start a domain span and bump a counter:

```csharp
public override async Task<Lesson> CreateLesson(CreateLessonRequest request, ServerCallContext context)
{
    using var activity = WorkshopTelemetry.Activity.StartActivity("CreateLesson");
    // ... domain work ...
    activity?.SetTag("workshop.tenant_id", tenantId);
    WorkshopTelemetry.LessonsCreated.Add(1);
    return lesson.ToProto();
}
```

`AddOtlpExporter` ships spans and metrics to a local OpenTelemetry Collector you start with one `docker run` (`otel/opentelemetry-collector` on ports `4317`/`4318`). You do *not* build dashboards this week — that is Week 14, with Grafana, Loki, and Tempo. This week you wire the *emission*, because retrofitting tracing onto a system not built to emit it is a rewrite, while emitting from commit one is the ten lines above. Crucially, because the EF Core and gRPC instrumentations share the same `Activity` context, the trace already correlates: the gRPC `CreateLesson` span is the parent of the EF Core `INSERT` span, and Serilog's `TraceId`/`SpanId` enrichment (added by the OTel integration) stamps every log line with the trace id — so a log line and a span are the same event seen two ways. (References: <https://opentelemetry.io/docs/languages/net/getting-started/>, <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>.)

## 8. The CI workflow: green in CI, not "on my machine"

The integration baseline is not done until it is green in CI. GitHub-hosted `ubuntu-latest` runners ship with Docker preinstalled, which is exactly what Testcontainers needs — the same `PostgreSqlContainer` and `KeycloakContainer` start inside the runner, so the same real integration test runs on every push. The workflow restores, builds all three projects, and runs the test suite:

```yaml
name: ci

on:
  push:
    branches: [ main ]
  pull_request:

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore
        run: dotnet restore Workshop.sln

      # All three clients must build against the generated contract. A backend
      # that builds while MAUI is broken is not a green build.
      - name: Build (backend + Blazor)
        run: dotnet build Workshop.sln -c Release --no-restore

      - name: Build MAUI (android head)
        run: dotnet build src/Workshop.Maui/Workshop.Maui.csproj -c Release -f net9.0-android

      # Testcontainers starts Postgres and Keycloak inside the runner; Docker
      # is preinstalled on ubuntu-latest. This is the load-bearing step.
      - name: Integration tests
        run: dotnet test tests/Workshop.IntegrationTests -c Release --no-build --logger "trx;LogFileName=integration.trx"
```

Two points make this honest. First, the MAUI head is built explicitly — the solution build does not necessarily build every MAUI target framework, so the workflow forces the `net9.0-android` build so that a contract change that breaks the phone client *fails CI*, not "Friday." Second, the integration step has no special database service block, no `services: postgres:` in the workflow — because Testcontainers starts its own containers from the test code. That is the payoff of the substrate: the same test that ran on your laptop runs in CI with zero CI-specific infrastructure config. The merge gate is this job; a milestone red here is red.

## 9. The integration baseline, assembled

You now have the full machinery of a trustworthy green. `WebApplicationFactory<Program>` hosts your real app in-memory; Testcontainers gives it a real, ephemeral PostgreSQL and Keycloak; `MigrateAsync` applies your real migrations so the schema is the migrations' schema; the integration test drives the vertical slice over real gRPC with real tokens, asserting the contract works end to end; Serilog makes every log line a queryable structured event; OpenTelemetry makes every request a readable trace correlated to those logs; and GitHub Actions runs all of it on every push so "it works" is a fact CI verifies, not a claim you make.

That is the integration baseline — the milestone for Week 13. It is not the prettiest week of the capstone; it draws no charts and demos no screens. It is the week that makes every following week *editing* instead of *discovery*, because the system stands up, the contract holds, the database is real, the auth is real, the tests are real, and the build is green where it counts. Week 14 hardens what stands; Week 15 deploys it. Neither is possible without the baseline this week installs.

Start with the contract (Lecture 1), make it load-bearing (Lecture 2), make green mean something (this lecture). Then go build the slice — the exercises and the mini-project are that build, in the order these three lectures prescribed.
