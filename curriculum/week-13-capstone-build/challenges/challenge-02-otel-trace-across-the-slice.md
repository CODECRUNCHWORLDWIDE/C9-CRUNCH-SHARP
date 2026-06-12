# Challenge 2 — One Request, One Trace: Make a Single Call Span Blazor → gRPC-Web → Service → EF Core → PostgreSQL, and Read It in the Collector

> **Time:** 2.5 hours. **Prerequisites:** Exercises 1–3, Lecture 3 §7 (the OpenTelemetry wiring). **Citations:** OpenTelemetry .NET getting started at <https://opentelemetry.io/docs/languages/net/getting-started/>, .NET distributed tracing at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>, the OTLP exporter at <https://github.com/open-telemetry/opentelemetry-dotnet/tree/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol>, and the OpenTelemetry Collector at <https://opentelemetry.io/docs/collector/>.

## The premise

Observability is only worth wiring if the spans actually *connect*. A trace where the gRPC span and the EF Core span are two unrelated entries is no better than two log lines; the value is the *tree* — the gRPC call as the parent, the database query as its child, with one shared trace id stitching them. This challenge proves the stitching: you make one `CreateLesson` call from the Blazor admin and show that it produces **one** trace, with spans for the inbound gRPC-Web request, the domain `CreateLesson` activity, and the EF Core `INSERT`, all under one trace id — and you read that trace in a local OpenTelemetry Collector.

By the end you will have a screenshot of a single trace with the full span tree, and you will be able to explain how the trace id propagated from the browser to the database.

## Why this is the trace contract from the README

The README's trace contract says: *every request through the running system leaves a structured Serilog event and an OpenTelemetry trace behind it.* This challenge is where you verify that contract for the slice's marquee call. If you cannot produce one trace for one `CreateLesson`, you have not met the contract — you have spans that do not compose, which is the observability equivalent of three clients that do not agree on a contract.

## Part 1 — Start the collector

The collector receives OTLP and prints traces so you can read them without a backend. Start it with a minimal config that logs received spans:

```yaml
# otel-collector-config.yaml
receivers:
  otlp:
    protocols:
      grpc:  { endpoint: 0.0.0.0:4317 }
      http:  { endpoint: 0.0.0.0:4318 }

exporters:
  debug:
    verbosity: detailed

service:
  pipelines:
    traces:
      receivers:  [otlp]
      exporters:  [debug]
    metrics:
      receivers:  [otlp]
      exporters:  [debug]
```

```sh
docker run --rm -p 4317:4317 -p 4318:4318 \
  -v "$(pwd)/otel-collector-config.yaml":/etc/otelcol/config.yaml \
  otel/opentelemetry-collector:latest --config /etc/otelcol/config.yaml
```

The `debug` exporter writes every received span to the container's stdout, so `docker logs` (or the foreground output) is your trace viewer for this challenge. For a nicer UI, swap the exporter for one that ships to Jaeger or Tempo — but the debug exporter is enough to *prove* the spans connect, which is the assignment.

## Part 2 — Confirm the backend emits and exports

The backend's `Program.cs` already has the OpenTelemetry wiring from Lecture 3 §7. Confirm three things are present:

1. `.AddSource(WorkshopTelemetry.ActivitySourceName)` — so your domain `CreateLesson` activity is collected.
2. `.AddAspNetCoreInstrumentation()` and `.AddEntityFrameworkCoreInstrumentation()` — so the inbound request and the EF Core query auto-emit spans.
3. `.AddOtlpExporter()` pointed at the collector. Set the endpoint via the standard env var so no code change is needed:

```sh
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_SERVICE_NAME=workshop-api
```

In the `CreateLesson` service method, the domain span and the tag are already there (Lecture 3 §7):

```csharp
using var activity = WorkshopTelemetry.Activity.StartActivity("CreateLesson");
// ... domain work, db.SaveChangesAsync ...
activity?.SetTag("workshop.tenant_id", tenantId);
WorkshopTelemetry.LessonsCreated.Add(1);
```

## Part 3 — Propagate the trace from the browser

The harder, more interesting half: making the trace start at the *browser*, not the backend. By default the backend's ASP.NET Core instrumentation starts a fresh trace for each request. To make the Blazor admin's `createLesson()` the root of the same trace, the browser must send the W3C `traceparent` header and the backend must continue it (which `AddAspNetCoreInstrumentation` does automatically when the header is present).

In the gRPC-Web client, inject a `traceparent`. The W3C Trace Context format is `00-{trace-id}-{span-id}-01`:

```typescript
function newTraceparent(): string {
  const hex = (bytes: number) =>
    Array.from(crypto.getRandomValues(new Uint8Array(bytes)))
      .map((b) => b.toString(16).padStart(2, "0")).join("");
  return `00-${hex(16)}-${hex(8)}-01`;   // version-traceid-spanid-sampled
}

// add it to the call metadata alongside the Authorization header:
const metadata = { authorization: `Bearer ${token}`, traceparent: newTraceparent() };
```

Now the backend's ASP.NET Core instrumentation reads the inbound `traceparent`, makes its request span a *child* of the browser-supplied span, and the EF Core span is in turn a child of the domain activity — one trace, four spans, one trace id from the browser to the database. (Reference: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>.)

## Part 4 — Read the trace and prove it connects

Make one `CreateLesson` call (from the Blazor admin, or with `grpcurl` carrying a `traceparent` metadata entry). In the collector's debug output, find the spans for the request and confirm:

1. There is **one** `Trace ID` shared by every span of the call.
2. The span tree is: the inbound ASP.NET Core request span → the `CreateLesson` domain activity → the EF Core `INSERT` span. (The EF span's name is the SQL operation; the `db.statement` attribute carries the parameterized SQL.)
3. The `workshop.tenant_id` tag is present on the `CreateLesson` span.
4. The Serilog log line for the same request carries the **same** `TraceId` in its properties — because the OpenTelemetry integration stamps `TraceId`/`SpanId` onto the log context. Open the console Serilog output and the collector output side by side; the trace id matches. *That is the log-and-trace correlation that lets you pivot from "this log line is interesting" to "show me the whole trace."*

## Deliverables

1. The `otel-collector-config.yaml` and the `docker run` command, working.
2. A capture (screenshot or saved log) of **one** trace for **one** `CreateLesson` call, showing the request → domain → EF Core span tree under one trace id.
3. The browser `traceparent` injection, with a capture proving the trace id in the collector matches the one the browser sent.
4. A side-by-side capture of a Serilog log line and the collector span sharing the same trace id.
5. A short `CHALLENGE-02.md` answering: (a) what would the trace look like *without* the browser `traceparent` (where would the root be); (b) why is the EF Core span a child of the domain activity and not a sibling; (c) one thing this trace would tell you instantly that a log line alone would not.

## Stretch goals

- **Span the gRPC client hop too.** Add `.AddGrpcClientInstrumentation()` and make a call where the backend calls *itself* (or a second service) over gRPC; show the client-side gRPC span nested in the same trace, proving the trace crosses a network boundary, not just a process.
- **A metric, not just a trace.** Point the collector at a Prometheus exporter, scrape `workshop.lessons.created`, and show the counter incrementing per call. Confirm the metric and the trace are the same event measured two ways.
- **Sampling.** Configure a parent-based sampler so that only browser-sampled traces (the `-01` flag) are exported, and prove that flipping the browser's flag to `-00` drops the trace from the collector. Explain why head-sampling at the browser is the right place to decide for a user-initiated action. Cite <https://opentelemetry.io/docs/concepts/sampling/>.
