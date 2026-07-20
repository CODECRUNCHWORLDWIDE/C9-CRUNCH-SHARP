# Challenge 1 — Stitch One Trace ID Through REST, EF Core, and SignalR, and Render It in Jaeger

> **Time:** 2 hours. **Prerequisites:** Exercises 1, 2, 3. **Citations:** the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, the W3C Trace Context spec at <https://www.w3.org/TR/trace-context/>, the OpenTelemetry semantic conventions at <https://opentelemetry.io/docs/specs/semconv/>, the Jaeger docs at <https://www.jaegertracing.io/docs/>, and `ActivitySource` at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>.

## The premise

You have the composed ProjectHub host from Exercises 1–3: REST, gRPC, and SignalR behind one JWT scheme, EF Core against PostgreSQL, Serilog and OpenTelemetry wired with the console exporter. This challenge proves the trace-ID contract from Lecture 2 end to end: a single `POST /api/projects` writes a row through EF Core and broadcasts a `ProjectCreated` event through SignalR, and **one trace ID** appears in the inbound REST span, the Npgsql `INSERT` span, the application broadcaster span, and every log line all four produced. You will then swap the console exporter for OTLP, point it at a Jaeger all-in-one container, and capture the flame graph.

By the end you will have produced: (a) the raw console-exporter dump showing four spans with one shared `TraceId` and correct parent/child links, (b) a Jaeger screenshot of the same trace as a flame graph, and (c) a written explanation of what breaks the correlation and how you would detect it.

## Setup

A `docker-compose.yml` at the project root with three services:

```yaml
services:
  postgres:
    image: postgres:16
    container_name: pg-week12-ch1
    environment:
      - POSTGRES_DB=projecthub
      - POSTGRES_USER=projecthub
      - POSTGRES_PASSWORD=devpass
    ports: [ "5432:5432" ]

  jaeger:
    image: jaegertracing/all-in-one:1.54
    container_name: jaeger-week12-ch1
    environment:
      - COLLECTOR_OTLP_ENABLED=true
    ports:
      - "16686:16686"   # Jaeger UI
      - "4317:4317"     # OTLP gRPC receiver
      - "4318:4318"     # OTLP HTTP receiver

  projecthub:
    build: .
    container_name: projecthub-ch1
    depends_on: [ postgres, jaeger ]
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://0.0.0.0:8080
      - ConnectionStrings__ProjectHub=Host=postgres;Port=5432;Database=projecthub;Username=projecthub;Password=devpass
      - Jwt__SigningKey=DevelopmentKeyDoNotUseInProductionMustBeAtLeastSixtyFourCharactersLong
      - OpenTelemetry__Exporter=Otlp
      - OpenTelemetry__OtlpEndpoint=http://jaeger:4317
    ports: [ "8080:8080" ]
```

For the console-exporter half of the challenge, run the host with `OpenTelemetry__Exporter=Console` and read stdout directly; for the Jaeger half, flip it to `Otlp` as shown above.

## Server changes from Exercise 3

There is almost no new server code. The broadcaster from Lecture 2 already starts an explicit span; confirm it is wired into the create path. The relevant pieces:

```csharp
// ProjectEventsBroadcaster — the explicit application span.
public async Task BroadcastProjectCreatedAsync(Project project)
{
    using var activity = Source.StartActivity("ProjectCreatedBroadcast");
    activity?.SetTag("projecthub.project_id", project.Id);
    activity?.SetTag("projecthub.org_id", project.OrganizationId);

    await _hub.Clients
        .Group($"org-{project.OrganizationId}")
        .SendAsync("ProjectCreated", new { project.Id, project.Name });

    _logger.LogInformation(
        "Broadcast ProjectCreated for {ProjectId} to org-{OrgId}",
        project.Id, project.OrganizationId);
}
```

The `Source` field is `private static readonly ActivitySource Source = new("ProjectHub");` and the name `"ProjectHub"` must match the `AddSource("ProjectHub")` call in `AddProjectHubTelemetry`. If they disagree, the span is dropped silently and you have just reproduced the most common OpenTelemetry bug in the wild — confirm the strings are identical before you go hunting for anything subtler.

The REST create handler calls the broadcaster after `SaveChangesAsync`:

```csharp
db.Projects.Add(project);
await db.SaveChangesAsync(cancellationToken);     // produces the Npgsql INSERT span
await broadcaster.BroadcastProjectCreatedAsync(project); // produces the broadcast span
```

That is the whole code surface. The challenge is reading what the instrumentation produces, not writing new instrumentation.

## The measurement plan

### Measurement 1 — the four spans, console exporter

Run with `OpenTelemetry__Exporter=Console`. Mint a dev token, then:

```bash
curl -X POST http://localhost:8080/api/projects \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"trace demo"}'
```

Capture stdout. You are looking for four `Activity.*` blocks that share one `Activity.TraceId`:

| Span | `ActivitySourceName` | `Kind` | `DisplayName` (approx) | Parent |
|------|----------------------|--------|------------------------|--------|
| 1 | `Microsoft.AspNetCore` | `Server` | `POST /api/projects` | (root) |
| 2 | `Npgsql` | `Client` | `INSERT projects` | span 1 |
| 3 | `ProjectHub` | `Internal` | `ProjectCreatedBroadcast` | span 1 |
| 4 | `Microsoft.AspNetCore.SignalR` *(if surfaced)* | `Internal` | hub dispatch | span 3 or 1 |

Span 4 is the soft one — the in-box SignalR instrumentation does not always emit a dispatch span for a server-initiated `SendAsync` (there is no inbound invocation), so it is acceptable for the broadcast to surface only as span 3, the explicit `ProjectCreatedBroadcast` you started. Document which spans you actually observed and which were absent; "I expected five and saw three, and here is why" is a passing answer.

Verify by hand: every block's `Activity.TraceId` is identical, and span 2's and span 3's `Activity.ParentSpanId` equals span 1's `Activity.SpanId`.

### Measurement 2 — the log lines carry the same trace ID

The Serilog console output (also on stdout, interleaved with the spans) must show the same `TraceId` value in:

- the application `LogInformation` from the REST handler (`Project {ProjectId} created in org {OrgId}`),
- the `BroadcastProjectCreated` line from the broadcaster,
- the `UseSerilogRequestLogging` summary line (`HTTP POST /api/projects responded 201 in ...`).

Pipe the output through `jq` to extract just the trace IDs and confirm they collapse to one value:

```bash
docker logs projecthub-ch1 2>&1 | grep '"@mt"' | jq -r '.TraceId' | sort -u
```

A correct run prints exactly one trace ID for the request (plus separate IDs for any unrelated requests). If you see log lines with a `null` or absent `TraceId`, that is the `Enrich.FromLogContext()` failure mode from Lecture 2 — find it and fix it.

### Measurement 3 — render it in Jaeger

Flip to `OpenTelemetry__Exporter=Otlp`, `OpenTelemetry__OtlpEndpoint=http://jaeger:4317`, restart, re-issue the POST. Open <http://localhost:16686/>, pick `projecthub` from the **Service** dropdown, click **Find Traces**, and open the most recent one.

You should see a flame graph with the `POST /api/projects` bar at the top and the `INSERT projects` and `ProjectCreatedBroadcast` bars nested under it, each shorter and offset to the right by its start time. Hover a span to read its tags; confirm `db.statement` on the Npgsql span and `projecthub.project_id` on the broadcast span. Screenshot it.

## Acceptance criteria

1. `docker compose up` brings up Postgres, Jaeger, and ProjectHub; `curl http://localhost:8080/health` returns 200.
2. A single `POST /api/projects` produces a console-exporter dump with at least three spans (REST, Npgsql INSERT, broadcast) sharing one `TraceId`, with correct `ParentSpanId` links. The dump is captured in `TRACE.md`.
3. `docker logs ... | jq -r '.TraceId' | sort -u` confirms the request's log lines collapse to a single trace ID, and that ID equals the one in the spans.
4. The Jaeger UI renders the same trace as a flame graph; a screenshot is in `TRACE.md`, and the `db.statement` and `projecthub.project_id` tags are visible.
5. `TRACE.md` includes a 200-word section "what breaks the correlation," covering at least: a mismatched `ActivitySource` name, a missing `Enrich.FromLogContext()`, and an off-context `Task.Run` (Lecture 2's failure modes), with the symptom each produces.

## Stretch goals

1. **Fan into gRPC.** Add an outbound gRPC call from the create handler to the host's own `Projects.List` (a self-call through `GrpcChannel`), so the trace gains a `GrpcClient` span and a second inbound `Server` span. Verify the `traceparent` header propagates and the child request adopts the parent trace ID. Capture the now-six-span trace. Cite <https://www.w3.org/TR/trace-context/>.
2. **Break it on purpose.** Wrap the broadcaster call in `_ = Task.Run(() => broadcaster.BroadcastProjectCreatedAsync(project));`. Observe that the broadcast span is now a separate root trace (its `TraceId` differs from the REST request's) because `Activity.Current` did not flow across the `Task.Run`. Then fix it by capturing `Activity.Current` and passing its `Context` as the parent. Document both traces side by side. This is the single most common real-world trace-correlation bug; reproduce it once and you will recognize it forever.
3. **Sample at 10%.** Configure a `TraceIdRatioBasedSampler(0.1)` and fire 100 requests. Confirm roughly 10 traces reach Jaeger and that, for any sampled request, *all* of its spans are present (sampling is per-trace, not per-span). Explain why head-based sampling must be consistent across spans. Cite <https://opentelemetry.io/docs/concepts/sampling/>.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>, <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>. Source-link references: `ActivitySource` and `Activity` in `dotnet/runtime`; `HttpRequestIn` instrumentation in `dotnet/aspnetcore`. External: the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, the OTLP spec at <https://opentelemetry.io/docs/specs/otlp/>, the W3C Trace Context spec at <https://www.w3.org/TR/trace-context/>, the Jaeger docs at <https://www.jaegertracing.io/docs/>.
