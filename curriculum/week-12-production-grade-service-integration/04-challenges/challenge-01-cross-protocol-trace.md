# Challenge 1 — One Trace ID, Three Protocol Surfaces: Stitch a REST POST through EF Core and SignalR and Export it to Jaeger

> **Time:** 2 hours. **Prerequisites:** Exercises 1, 2, 3. **Citations:** the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, the OTLP exporter chapter at <https://opentelemetry.io/docs/languages/net/exporters/>, the ASP.NET Core distributed-tracing chapter at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>, and the Jaeger getting-started guide at <https://www.jaegertracing.io/docs/latest/getting-started/>.

## The premise

You have the ProjectHub host from the exercises: one process serving REST, gRPC, and SignalR, with Serilog and OpenTelemetry already wired and a console exporter printing spans. The console exporter is fine for proving the trace exists; it is useless for *reading* it, because the parent-child relationships are buried in `ParentSpanId` hex strings scattered across the terminal scrollback. This challenge swaps the console exporter for an **OTLP exporter pointed at a local Jaeger**, then drives the single most interesting request in the service — a REST `POST /api/projects/{id}/tasks/{taskId}/status` that updates a task, writes to Postgres via EF Core, and broadcasts `TaskStatusChanged` to the SignalR group `org-{orgId}` — and verifies that **one trace ID** spans the inbound HTTP span, the EF Core `UPDATE` span, the application span you add by hand, and the SignalR broadcast.

By the end you will have a Jaeger screenshot showing a single trace with at least four spans in a parent-child waterfall, and a written analysis of which span owns the latency.

## Setup — add Jaeger to the compose file

Add a Jaeger all-in-one container alongside the Postgres container in your `docker-compose.yml`:

```yaml
services:
  postgres:
    image: postgres:16
    container_name: pg-week12
    environment:
      - POSTGRES_PASSWORD=devpass
      - POSTGRES_DB=projecthub
    ports: [ "5432:5432" ]

  jaeger:
    image: jaegertracing/all-in-one:1.57
    container_name: jaeger-week12
    environment:
      - COLLECTOR_OTLP_ENABLED=true
    ports:
      - "16686:16686"   # Jaeger UI
      - "4317:4317"      # OTLP gRPC receiver
      - "4318:4318"      # OTLP HTTP receiver
```

Bring it up with `docker compose up -d`. The Jaeger UI is at <http://localhost:16686>.

## Server changes — swap the console exporter for OTLP

Add the OTLP exporter package:

```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.9.*
```

Change the tracing registration in `TelemetryServiceConfiguration.AddProjectHubTelemetry` so the exporter is environment-driven. The console exporter stays in development for tight-loop debugging; OTLP turns on when the `OTEL_EXPORTER_OTLP_ENDPOINT` config key is present:

```csharp
public static IServiceCollection AddProjectHubTelemetry(
    this IServiceCollection services, IConfiguration config)
{
    var otlpEndpoint = config["Otel:OtlpEndpoint"];   // e.g. http://localhost:4317

    services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(
            serviceName: "ProjectHub",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
        .WithTracing(tracing =>
        {
            tracing
                .AddSource("ProjectHub")                 // our ActivitySource
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddGrpcClientInstrumentation()
                .AddNpgsql();                            // from Npgsql.OpenTelemetry

            if (string.IsNullOrEmpty(otlpEndpoint))
                tracing.AddConsoleExporter();
            else
                tracing.AddOtlpExporter(o =>
                    o.Endpoint = new Uri(otlpEndpoint));
        });

    return services;
}
```

Set the endpoint in `appsettings.Development.json`:

```json
{ "Otel": { "OtlpEndpoint": "http://localhost:4317" } }
```

That is the entire exporter change. The instrumentation that produces the spans is unchanged; you are only redirecting where they go.

## The hand-written application span

The framework instrumentations give you the HTTP span and the EF Core span for free. The two interesting boundaries — "I decided to update this task" and "I broadcast the change" — are application concerns, so you add them with the `ActivitySource` you declared in Exercise 2. The status-change handler becomes:

```csharp
public static readonly ActivitySource AppActivity = new("ProjectHub");

group.MapPost("/{projectId:guid}/tasks/{taskId:guid}/status", async (
    Guid projectId, Guid taskId,
    UpdateStatusRequest body,
    ProjectHubDbContext db,
    ProjectEventsBroadcaster broadcaster,
    ClaimsPrincipal user) =>
{
    using var activity = AppActivity.StartActivity("UpdateTaskStatus");
    activity?.SetTag("project.id", projectId);
    activity?.SetTag("task.id", taskId);
    activity?.SetTag("task.status", body.Status.ToString());

    var orgId = Guid.Parse(user.FindFirstValue("org_id")!);

    var task = await db.Tasks
        .Where(t => t.Id == taskId && t.Project!.OrganizationId == orgId)
        .FirstOrDefaultAsync();
    if (task is null) return Results.NotFound();

    task.Status = body.Status;
    await db.SaveChangesAsync();              // <-- EF Core UPDATE span, same trace

    await broadcaster.BroadcastStatusChanged(orgId, taskId, body.Status);  // <-- SignalR span

    return Results.NoContent();
});
```

The `BroadcastStatusChanged` method on `ProjectEventsBroadcaster` wraps its `IHubContext` send in its own activity so the broadcast is a distinct, nameable span:

```csharp
public async Task BroadcastStatusChanged(Guid orgId, Guid taskId, TaskStatus status)
{
    using var activity = ProjectHubEndpoints.AppActivity.StartActivity("BroadcastStatusChanged");
    activity?.SetTag("org.id", orgId);
    await _hub.Clients.Group($"org-{orgId}")
        .SendAsync("TaskStatusChanged", new { TaskId = taskId, Status = status });
}
```

The key insight: `Activity.Current` flows through the `async`/`await` continuation chain via `AsyncLocal`. You do **not** have to thread a trace ID parameter through your method signatures. The handler's activity is the current activity when `SaveChangesAsync` runs, so the Npgsql instrumentation parents its `UPDATE` span under `UpdateTaskStatus` automatically. The same is true for the broadcast.

## The verification flow

1. `docker compose up -d` (Postgres + Jaeger).
2. `dotnet run` (the host, with the OTLP endpoint set).
3. Mint a token: `TOKEN=$(curl -sk -X POST "http://localhost:5080/dev/token?user=alice&orgId=11111111-1111-1111-1111-111111111111" | jq -r .token)`.
4. Create a project and a task (REST `POST`), capture the IDs.
5. Open a SignalR client to `/hubs/events?access_token=$TOKEN` so there is a subscriber in the `org-...` group (otherwise the broadcast goes to zero connections, which is still a valid span but less satisfying to watch).
6. Fire the status change:
   ```bash
   curl -sk -X POST \
     "http://localhost:5080/api/projects/$PID/tasks/$TID/status" \
     -H "authorization: Bearer $TOKEN" \
     -H "content-type: application/json" \
     -d '{"status":"InProgress"}'
   ```
7. Open <http://localhost:16686>, select service `ProjectHub`, find the most recent trace, and expand it.

## Acceptance criteria

1. The Jaeger UI shows **one trace** for the status-change request, not three disconnected traces.
2. That trace contains at least four spans in a parent-child waterfall: the ASP.NET Core inbound span (`POST /api/projects/{projectId}/tasks/{taskId}/status`), the `UpdateTaskStatus` application span, the Npgsql `UPDATE projecthub.tasks` span, and the `BroadcastStatusChanged` application span.
3. The `UpdateTaskStatus` span carries the `project.id`, `task.id`, and `task.status` tags you set.
4. Every Serilog log line emitted during the request carries the same `TraceId` (it is attached by the `Enrich.FromLogContext()` plus the OpenTelemetry log enricher). Grep the JSON log for the `traceId` and confirm it matches the Jaeger trace ID.
5. The same trace ID appears in the Postgres `application_name`-tagged session if you set `Application Name=ProjectHub` on the connection string and read `pg_stat_activity` mid-request (bonus, not required).

## Stretch goals

1. **Add a downstream gRPC call into the trace.** Have the status-change handler also call an internal gRPC method on a *second* ProjectHub-like service (you can point it at the same host's gRPC endpoint for the demo). Attach the JWT to the outbound gRPC call via `CallCredentials`, and verify the gRPC client instrumentation produces a span that joins the same trace — the trace now spans HTTP → EF → SignalR → gRPC. Document the W3C `traceparent` header that carries the context across the process boundary.
2. **Sample at 10% and prove it.** Production traces are expensive; switch the sampler to `TraceIdRatioBased(0.1)` via `tracing.SetSampler(new TraceIdRatioBasedSampler(0.1))`. Fire 100 requests and confirm Jaeger shows roughly 10 traces, all complete (sampling is per-trace, not per-span, so you never get a half-sampled trace). Explain why the parent-based sampler is the right default for a service that receives `traceparent` from upstream.
3. **Find the slow span on purpose.** Add an artificial `await Task.Delay(120)` inside `BroadcastStatusChanged`. Re-run and confirm the Jaeger waterfall attributes the 120ms to the broadcast span, not the EF Core span. Write 150 words on how this is the exact diagnostic the README's "the API was slow at 3:14am" operator needs — and why a flat log stream could never have given it to them.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>, <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>. External: the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, the semantic conventions at <https://opentelemetry.io/docs/specs/semconv/>, the Jaeger docs at <https://www.jaegertracing.io/docs/latest/>, and the W3C Trace Context spec at <https://www.w3.org/TR/trace-context/>.
