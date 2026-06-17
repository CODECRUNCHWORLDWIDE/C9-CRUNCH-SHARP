# Lecture 2 — Serilog, OpenTelemetry, and the Single Trace Through Three Protocols

## Why this lecture exists

The Week 11 lecture-1 promise was a "wire-bytes contract" — every realtime endpoint had a known, intentional wire format and you could see it in the browser's developer tools. The Week 12 promise is broader: every request, on every protocol, produces a **structured log line** with a **trace ID** that you can grep across every other log line the request touched, and a **distributed-trace span** that you can render in a viewer like Jaeger and see as a flame graph. The mechanism is two libraries — **Serilog** for the structured log lines and **OpenTelemetry .NET** for the spans — and one shared identifier: the **W3C Trace Context** `traceparent`. Microsoft's framework-level distributed-tracing API (`Activity` and `ActivitySource` in `System.Diagnostics`) wires the two together for free when you register them in the right order.

This lecture has three jobs. First, replace the default `Microsoft.Extensions.Logging` console provider with Serilog, configured to emit one JSON line per log call, enriched with the trace ID, machine name, environment, and any application-supplied properties. Second, register the OpenTelemetry .NET SDK with the four instrumentation packages — ASP.NET Core, HttpClient, gRPC client, Npgsql — and a console exporter, then read the resulting console output until the trace structure is muscle memory. Third, swap the console exporter for an OTLP exporter pointed at a Jaeger container and verify the trace renders as a flame graph in the Jaeger UI. By the end, a `curl -X POST /api/projects` that writes to Postgres and broadcasts to SignalR will produce one trace with at least four spans and one log line per span, all carrying the same trace ID.

## Serilog as the host-level logger

The default ASP.NET Core logger is fine for `dotnet run` output and useless for production. Its config knobs are flat (no enrichers, no template expressions), its output format is human-readable but machine-hostile (log aggregators have to regex the message string), and the structured-data extension points are missing. Serilog fixes all three.

Wiring Serilog is a two-step pattern: configure it **early** so registration errors are logged, then register `UseSerilog()` on the host so the framework's `ILogger<T>` calls route through Serilog. The "early" step uses a temporary bootstrap logger that captures startup failures; we replace it with the final configuration once `IConfiguration` is available.

Here is the production-shaped registration:

```csharp
namespace ProjectHub.Configuration;

using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

public static class LoggingHostConfiguration
{
    public static WebApplicationBuilder AddProjectHubLogging(this WebApplicationBuilder builder)
    {
        // Bootstrap logger — used during host construction so we can log
        // any config-load failure before the final logger is built.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(new CompactJsonFormatter())
            .Enrich.FromLogContext()
            .CreateBootstrapLogger();

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("Application", "projecthub")
                .WriteTo.Console(new CompactJsonFormatter())
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    path: "logs/projecthub-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
        });

        return builder;
    }
}
```

The configuration reads its log levels from `IConfiguration` via the `Serilog` configuration section (we saw it in lecture 1's `appsettings.json`). The `ReadFrom.Services(services)` line lets enrichers that need DI (the trace-ID enricher from OpenTelemetry, for example) participate. Citation: <https://github.com/serilog/serilog-aspnetcore>.

The format we chose is `Serilog.Formatting.Compact.CompactJsonFormatter`. One log call produces one JSON line. Here is what the output looks like when a REST handler calls `logger.LogInformation("Project {ProjectId} created in org {OrgId}", project.Id, orgId)`:

```json
{
  "@t": "2026-05-15T17:21:09.4112048Z",
  "@mt": "Project {ProjectId} created in org {OrgId}",
  "@l": "Information",
  "ProjectId": "3a8e0f1e-0e0e-4d22-b7a5-d2f3e5b8a1c9",
  "OrgId": "11111111-1111-1111-1111-111111111111",
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "SpanId": "00f067aa0ba902b7",
  "MachineName": "build-runner-7",
  "EnvironmentName": "Development",
  "Application": "projecthub",
  "SourceContext": "ProjectHub.Endpoints.ProjectEndpoints"
}
```

Read this until it is muscle memory. `@t` is the timestamp, `@mt` is the message template (the part with `{ProjectId}` braces, not the rendered string), `@l` is the level. Then the structured fields — `ProjectId`, `OrgId` — appear as top-level keys. `TraceId` and `SpanId` are populated by Serilog's `Enrich.FromLogContext()` together with the OpenTelemetry instrumentation that pushes the current activity into the context. The enrichers add `MachineName`, `EnvironmentName`, `Application`. `SourceContext` is the categorical class name from `ILogger<ProjectEndpoints>`. All of this is queryable in Elastic, Loki, Datadog, Splunk, or Seq with no extra parsing — the aggregator just indexes the JSON keys.

Three Serilog principles worth internalizing.

**One. The message template is the contract, not the rendered string.** If you write `logger.LogInformation($"Project {project.Id} created")`, you have built the string yourself and Serilog cannot extract the `ProjectId` as a structured field. The output will have `@mt` equal to the rendered string and no `ProjectId` key. The aggregator can index full-text but not query for "all logs where `ProjectId = X`." Use the template form: `logger.LogInformation("Project {ProjectId} created", project.Id)`. The braces are not interpolation — they are placeholder names that become JSON keys.

**Two. The log level is a filter, not a decoration.** `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, `LogCritical` — each has a cost (string serialization, JSON formatting, file I/O). The `MinimumLevel` filter applies *before* the message template is rendered, so a debug-level call that is below the threshold costs only the level comparison. Use `LogTrace` for "I want to see this when I have the level cranked up"; use `LogInformation` for "I want to see this on every production run."

**Three. Don't `try/catch` around `Log.Error`.** If Serilog throws — disk full, IO exception, recursive enricher — the framework's `SelfLog.Enable(Console.Error.WriteLine)` is the right escape hatch. Wrapping every log call in a `try/catch` couples application code to the logging library's internal behavior.

The full Serilog reference is at <https://github.com/serilog/serilog>. Read the message-template syntax docs once; they pay back the half-hour many times. The ASP.NET Core integration is at <https://github.com/serilog/serilog-aspnetcore>; the compact JSON formatter is at <https://github.com/serilog/serilog-formatting-compact>.

## The `UseSerilogRequestLogging` middleware

Serilog ships a middleware that produces **one log line per HTTP request**. It runs after the request completes and emits a structured line with method, path, status code, duration, and any properties the application enriched the log context with. Wire it via `app.UseSerilogRequestLogging()` in `Program.cs`.

The result is one log line per request, like this:

```json
{
  "@t": "2026-05-15T17:21:09.5891234Z",
  "@mt": "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms",
  "@l": "Information",
  "RequestMethod": "POST",
  "RequestPath": "/api/projects",
  "StatusCode": 201,
  "Elapsed": 17.6234,
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "SourceContext": "Serilog.AspNetCore.RequestLoggingMiddleware",
  "RequestId": "0HMV9G1KQDDFB:00000001",
  "ConnectionId": "0HMV9G1KQDDFB"
}
```

This is the "what happened" line for the entire HTTP request. Combined with the application-level lines emitted from inside the handler, the operator can reconstruct the full request from a single `TraceId` lookup. There is one of these for every REST request and every gRPC request (the gRPC pipeline is built on the same ASP.NET Core HTTP pipeline). SignalR's negotiate POST also produces one; the long-lived WebSocket connection does not produce a per-message line — those go through the framework's `Information`-level connection-lifecycle events instead.

You can enrich the request log with custom properties by setting `EnrichDiagnosticContext`:

```csharp
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            diagnosticContext.Set("UserId",
                httpContext.User.FindFirst("sub")?.Value);
            diagnosticContext.Set("OrgId",
                httpContext.User.FindFirst("org_id")?.Value);
        }
    };
});
```

Now every request log line carries `UserId` and `OrgId`. Log aggregator queries become `"OrgId" : "11111111-..."` instead of regex over message text.

## OpenTelemetry .NET — the model

OpenTelemetry's job is to give every observable thing in the service a vocabulary that does not depend on the vendor. A "trace" is a tree of "spans"; a "span" has a name, a start time, a duration, a parent span ID, an array of attributes, and an array of events; a span can also have "links" to other spans (useful for fan-out cases). The vocabulary is standardized as **semantic conventions** at <https://opentelemetry.io/docs/specs/semconv/>.

In .NET, the implementation rides on top of `System.Diagnostics.Activity` — a runtime-supplied class that has been carrying request-scoped state since `.NET Framework` and that OpenTelemetry adopted as its in-process span representation. An `Activity` has a `TraceId`, a `SpanId`, a `ParentSpanId`, and a bag of tags and baggage. Library authors create activities with `ActivitySource.StartActivity(name)`; the OpenTelemetry SDK subscribes to those `ActivitySource` events and forwards them to exporters. There is no application-level "span object" — the activity *is* the span.

This matters because **the framework instrumentation is already there**. ASP.NET Core's `HttpContext` pipeline already creates an activity for every inbound request (`Microsoft.AspNetCore.Hosting.HttpRequestIn`). The `HttpClient` already creates one for every outbound call. The Npgsql driver already creates one for every SQL command. The gRPC server already creates one for every RPC. All of them call `ActivitySource.StartActivity` against named sources. The job of `AddOpenTelemetry()` is to **subscribe** to those sources, decorate the activities with semantic-convention attributes, and export them. The library does no instrumentation work itself; it brokers between the activities the framework already produces and the exporter that ships them out.

Registration:

```csharp
namespace ProjectHub.Configuration;

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

public static class TelemetryServiceConfiguration
{
    public const string ActivitySourceName = "ProjectHub";

    public static IServiceCollection AddProjectHubTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("OpenTelemetry");
        var serviceName = section.GetValue<string>("ServiceName") ?? "projecthub";
        var serviceVersion = section.GetValue<string>("ServiceVersion") ?? "0.0.0";
        var exporter = section.GetValue<string>("Exporter") ?? "Console";
        var otlpEndpoint = section.GetValue<string>("OtlpEndpoint")
            ?? "http://localhost:4317";

        services.AddSingleton(new ActivitySource(ActivitySourceName));

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] =
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddNpgsql();

                if (exporter == "Console")
                    tracing.AddConsoleExporter();
                else if (exporter == "Otlp")
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("ProjectHub.*");

                if (exporter == "Console")
                    metrics.AddConsoleExporter();
                else if (exporter == "Otlp")
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}
```

Several things deserve attention.

**`ConfigureResource(...).AddService(name, version)`** sets the `service.name` and `service.version` resource attributes that every exported span carries. The semantic-convention name is `service.name`; setting it correctly is the first thing a Jaeger / Tempo / Honeycomb operator will check when traces aren't appearing under the expected service.

**`tracing.AddSource(ActivitySourceName)`** subscribes to activities the *application* produces. Without this line, the framework's activities still export but any custom `activitySource.StartActivity("MyOperation")` calls from the application code will be silently dropped.

**`AddAspNetCoreInstrumentation`**, **`AddHttpClientInstrumentation`**, **`AddGrpcClientInstrumentation`**, **`AddNpgsql`**. The four instrumentations cover the four ways the service touches a network or a database. AspNetCore covers inbound REST, gRPC server, and SignalR negotiate. HttpClient covers any outbound HTTP. GrpcClient covers outbound gRPC. Npgsql covers every SQL command EF Core issues. There is no instrumentation for SignalR's WebSocket frames after the upgrade — those are not request-scoped and OpenTelemetry's per-span model does not fit them well; we handle them with application-level `ActivitySource.StartActivity("SignalRBroadcast")` calls instead.

**The `Filter` on AspNetCoreInstrumentation** excludes `/health` from tracing. Health-check polls would otherwise dominate the trace volume; the cost is that you lose visibility into health-check timing, which is fine in 99% of cases.

The full SDK reference is <https://github.com/open-telemetry/opentelemetry-dotnet>. Read the `README.md`; the `examples/Console` and `examples/AspNetCore` folders are runnable demonstrations.

## Reading a console-exported trace

The `AddConsoleExporter()` exporter writes each span to standard output in a multi-line format that is verbose but readable. Run the host, issue `curl -X POST /api/projects -H "Authorization: Bearer <token>" -d '{"name":"trace demo"}'`, and the console will produce something like:

```
Activity.TraceId:            4bf92f3577b34da6a3ce929d0e0e4736
Activity.SpanId:             a3ce929d0e0e4736
Activity.TraceFlags:         Recorded
Activity.ActivitySourceName: Microsoft.AspNetCore
Activity.DisplayName:        POST /api/projects
Activity.Kind:               Server
Activity.StartTime:          2026-05-15T17:21:09.4045123Z
Activity.Duration:           00:00:00.0211456
Activity.Tags:
    http.request.method: POST
    url.path: /api/projects
    http.route: /api/projects
    network.protocol.version: 1.1
    user_agent.original: curl/8.4.0
    server.address: localhost
    server.port: 5001
    http.response.status_code: 201
StatusCode:                  Unset
Resource associated with Activity:
    service.name: projecthub
    service.version: 0.12.0
    deployment.environment: Development

Activity.TraceId:            4bf92f3577b34da6a3ce929d0e0e4736
Activity.SpanId:             d3ce929d0e0e4736
Activity.ParentSpanId:       a3ce929d0e0e4736
Activity.ActivitySourceName: Npgsql
Activity.DisplayName:        INSERT projects
Activity.Kind:               Client
Activity.StartTime:          2026-05-15T17:21:09.4123012Z
Activity.Duration:           00:00:00.0067891
Activity.Tags:
    db.system: postgresql
    db.name: projecthub
    db.statement: INSERT INTO projects (id, organization_id, name, created_at) VALUES ($1, $2, $3, $4)
StatusCode:                  Unset
```

Read it carefully. The two spans share a `TraceId` (`4bf92f35...`); the second span's `ParentSpanId` (`a3ce929d...`) equals the first span's `SpanId`. That parent-child relationship is what makes the flame graph in Jaeger render the SQL call *inside* the REST request bar. The `Activity.Kind` distinguishes "Server" (we received it) from "Client" (we issued it). The semantic-convention tags — `http.request.method`, `db.statement` — are exactly what the OpenTelemetry spec prescribes; Jaeger and Tempo and Honeycomb all know to render them in their respective UIs without per-vendor config.

Two more spans should appear from the same request: one from the SignalR broadcast and one from `ProjectEventsBroadcaster.BroadcastProjectCreatedAsync` itself (if you add an explicit `ActivitySource.StartActivity` call in that method, which we will do in the mini-project). All four spans carry the same `TraceId`.

## Custom spans for application logic

The framework instrumentation covers HTTP, gRPC, and SQL. Application logic — the broadcaster method, a long-running calculation, a multi-step orchestration — needs explicit spans. The API is `ActivitySource.StartActivity`:

```csharp
public class ProjectEventsBroadcaster
{
    private static readonly ActivitySource Source = new("ProjectHub");

    private readonly IHubContext<ProjectEventsHub> _hub;
    private readonly IDbContextFactory<ProjectHubDbContext> _dbFactory;
    private readonly ILogger<ProjectEventsBroadcaster> _logger;

    public ProjectEventsBroadcaster(
        IHubContext<ProjectEventsHub> hub,
        IDbContextFactory<ProjectHubDbContext> dbFactory,
        ILogger<ProjectEventsBroadcaster> logger)
    {
        _hub = hub;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task BroadcastProjectCreatedAsync(Project project)
    {
        using var activity = Source.StartActivity("ProjectCreatedBroadcast");
        activity?.SetTag("projecthub.project_id", project.Id);
        activity?.SetTag("projecthub.org_id", project.OrganizationId);

        await _hub.Clients
            .Group($"org-{project.OrganizationId}")
            .SendAsync("ProjectCreated", new
            {
                Id = project.Id,
                Name = project.Name
            });

        _logger.LogInformation(
            "Broadcast ProjectCreated for {ProjectId} to org-{OrgId}",
            project.Id, project.OrganizationId);
    }
}
```

The `Source` field is `static readonly` and uses the same name we registered in `AddProjectHubTelemetry` (`"ProjectHub"`). `StartActivity` returns the current activity if there is a parent (the REST handler's request span) or starts a new root if there isn't; either way the resulting `Activity` is part of the same trace tree. The `?.` operator handles the case where no listener is subscribed — `StartActivity` returns `null` in that case and we don't pay the tag-setting cost.

Custom tags follow the `projecthub.<lowercase_snake_case>` convention. The OpenTelemetry semantic conventions reserve unprefixed names (`http.*`, `db.*`, `rpc.*`); application-specific tags should use a vendor prefix to avoid collision. Citation: <https://opentelemetry.io/docs/specs/semconv/general/attribute-naming/>.

## Trace-ID-as-correlation-key — the contract

Here is the contract this lecture establishes:

> **Every log line emitted while an HTTP request is being processed carries the same `TraceId`, and the `TraceId` is the join key against the distributed-trace export.**

The mechanism: Serilog's `Enrich.FromLogContext` reads `System.Diagnostics.Activity.Current` and writes its `TraceId` and `SpanId` to the log line. OpenTelemetry's instrumentation creates that activity at the start of the request and disposes it at the end. The two libraries cooperate by both reading from the same runtime-supplied state; we wire them in the same `Program.cs` and the contract holds.

The test of the contract is this: take the `TraceId` from any single log line, search the log aggregator for that `TraceId`, and you should see every line the request produced — the request-logging line, every application `LogInformation` from inside the handler, every framework-emitted line at `Information` or above. Open the trace viewer (Jaeger) and search for the same `TraceId`, and you should see every span — the request span, the SQL spans, the SignalR broadcast span. The two views — logs and traces — show the same request from two angles, and the `TraceId` is the join.

When the contract breaks, it is usually because the application code did something async that lost the `Activity.Current`. The most common cause is `Task.Run(() => DoWork())` — the work runs on a thread-pool thread without the request's activity context. The fix is to capture `Activity.Current` before the `Task.Run` and start a new activity inside the lambda with the captured ID as the parent:

```csharp
var parent = Activity.Current;
_ = Task.Run(() =>
{
    using var activity = Source.StartActivity(
        "BackgroundWork", ActivityKind.Internal, parent?.Context ?? default);
    DoWork();
});
```

In practice, do not `Task.Run` from inside a request handler. Push the work to a `Channel<T>` consumed by a hosted service (the Week 13 pattern). The trace will then end at the request handler and a separate, independent trace will start at the consumer; that is the correct shape.

## Switching the exporter to OTLP and viewing in Jaeger

The console exporter is for development and reading-by-hand. Production sends spans to an OpenTelemetry Collector, which forwards them to whatever backend the team uses — Jaeger, Tempo, Honeycomb, Datadog, Lightstep, AWS X-Ray. The wire protocol is **OTLP** (OpenTelemetry Protocol) at <https://opentelemetry.io/docs/specs/otlp/>.

For local development, Jaeger has a single-container "all-in-one" image that accepts OTLP directly:

```yaml
# docker-compose snippet
jaeger:
  image: jaegertracing/all-in-one:1.54
  ports:
    - "16686:16686"  # Jaeger UI
    - "4317:4317"    # OTLP gRPC receiver
    - "4318:4318"    # OTLP HTTP receiver
  environment:
    - COLLECTOR_OTLP_ENABLED=true
```

Set `OpenTelemetry__Exporter=Otlp` and `OpenTelemetry__OtlpEndpoint=http://localhost:4317` in the environment, and the .NET SDK's OTLP exporter (which we registered conditionally above) will ship spans there. Open <http://localhost:16686/>, pick `projecthub` from the service dropdown, click "Find traces", and the flame graph renders.

The mini-project ships the full `docker-compose.yml`. Three services: `projecthub` (this service), `postgres` (the database), `jaeger` (the trace viewer). `docker compose up` brings the lot online; `curl -X POST` issues a request; Jaeger shows the trace within seconds.

Citation: Jaeger documentation at <https://www.jaegertracing.io/docs/>. The "Getting started with all-in-one" page is the right starting point.

## Metrics — a brief tour

OpenTelemetry's metrics API parallels its tracing API. `Meter` is to metrics what `ActivitySource` is to spans; `Counter<T>`, `Histogram<T>`, `ObservableGauge<T>` are the instrument types. The four framework instrumentations (`AddAspNetCoreInstrumentation`, `AddHttpClientInstrumentation`, `AddRuntimeInstrumentation`, plus our `AddMeter("ProjectHub.*")`) cover the basics: per-route latency histograms, per-status-code counters, GC heap size, thread-pool work item counts, custom application meters.

For ProjectHub we expose three custom meters:

```csharp
public class ProjectMetrics
{
    private readonly Counter<long> _projectsCreated;
    private readonly Counter<long> _projectsDeleted;
    private readonly Histogram<double> _projectListLatency;

    public ProjectMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("ProjectHub.Projects");
        _projectsCreated = meter.CreateCounter<long>("projecthub.projects.created");
        _projectsDeleted = meter.CreateCounter<long>("projecthub.projects.deleted");
        _projectListLatency = meter.CreateHistogram<double>(
            "projecthub.projects.list_latency_ms",
            unit: "ms");
    }

    public void RecordCreated(string orgId) =>
        _projectsCreated.Add(1, new KeyValuePair<string, object?>("org_id", orgId));

    public void RecordDeleted(string orgId) =>
        _projectsDeleted.Add(1, new KeyValuePair<string, object?>("org_id", orgId));

    public void RecordListLatency(double ms) =>
        _projectListLatency.Record(ms);
}
```

The `IMeterFactory` is registered automatically by `AddOpenTelemetry().WithMetrics(...)`. The meter name follows the same `ProjectHub.*` prefix we registered. The metrics show up in the console exporter as periodic snapshots; in the OTLP path they go to Prometheus / Tempo / whatever the team operates. Documentation: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>.

## What we built

By the end of Lecture 2, the host:

- Logs every operation through Serilog as one JSON line per call, with trace ID, span ID, machine name, environment, and application-supplied properties.
- Emits one request-summary log line per HTTP request via `UseSerilogRequestLogging`.
- Produces a distributed trace per request via the OpenTelemetry .NET SDK, with framework-supplied spans for HTTP, SQL, gRPC client, and HttpClient, plus application-supplied spans for the broadcaster and any other instrumented code path.
- Exports traces to the console in development and to OTLP / Jaeger in production via a config switch.
- Records three custom metrics — projects created, projects deleted, list latency — alongside the framework counters.

The contract: every log line and every span produced during one request shares one `TraceId`. The operator can pivot from a log line to a trace, from a trace to a log line, and back, by copying the `TraceId` across views.

Lecture 3 wraps this entire host in `WebApplicationFactory<Program>` and writes integration tests that exercise the REST surface, the gRPC surface, and the SignalR hub from inside the same test process — against a real PostgreSQL container started by Testcontainers per test class. The traces produced during a test run are captured to a test output sink so failing tests carry the same observability evidence as failing production requests.

The slogan: structured logs are queries waiting to happen; traces are flame graphs waiting to happen. Both pay for themselves the first incident. Write them in once, configured from `IConfiguration`, and they follow you for the lifetime of the service.
