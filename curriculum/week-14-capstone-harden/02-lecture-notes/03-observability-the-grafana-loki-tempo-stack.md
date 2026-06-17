# Lecture 3 — Observability: The Three Signals, the .NET 9 OpenTelemetry SDK, and a Local Grafana + Loki + Tempo Stack with Exemplars

> **Time:** 2 hours. Take the three-signals model and the SDK wiring in one sitting, the compose stack and the correlated-incident walkthrough in a second. **Prerequisites:** Lecture 2 (the MediatR pipeline — we trace a request through it), Week 8 (the background worker and outbox), Week 13 (Serilog and the OpenTelemetry baseline). **Citations:** the .NET observability with OpenTelemetry guide at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>, the OpenTelemetry .NET docs at <https://opentelemetry.io/docs/languages/net/>, and the Grafana Tempo docs at <https://grafana.com/docs/tempo/latest/>.

## 1. Operate a service you can debug from the dashboard alone

The target for this week's harden is a specific, testable capability: handed a production incident, you open Grafana, not SSH. You read the trace, find the slow span, jump to its logs by `TraceId`, and name the cause — from the dashboard, without attaching a debugger, without `Console.WriteLine`, without redeploying with extra logging. That capability has a name — **observability** — and it is built from three signals, each emitted by the application, each stored by a purpose-built backend, all correlated in one UI. This lecture wires all three from the .NET 9 OpenTelemetry SDK to a local **Grafana + Loki + Tempo + Prometheus** stack that runs from `docker compose`. The whole stack is free and open source; nothing here requires a SaaS vendor.

## 2. The three signals — and the discipline of using the right one

The single most common observability mistake is reaching for the wrong signal: grepping logs to compute a p99 (that is a metric), or staring at a metrics dashboard to understand why *one specific request* failed (that is a trace). Internalize what each one is *for* (<https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>):

| Signal | What it is | Stored in | The question it answers |
|--------|-----------|-----------|-------------------------|
| **Logs** | Discrete, structured events | **Loki** | "What happened, in detail, on this one request?" |
| **Metrics** | Aggregatable numbers over time | **Prometheus** | "What is the rate / error rate / p99 across all requests?" |
| **Traces** | Causally-linked spans across processes | **Tempo** | "Where did *this* request spend its time, across services?" |

The three are not redundant; they are complementary, and the magic is in the **correlation**. A metric tells you p99 latency spiked at 14:32. A trace — found via an exemplar on that metric — shows you *one* slow request from that spike: the API span took 800ms, of which 780ms was a single EF Core span running an N+1 query. The logs for that `TraceId` show the parameters that triggered it. You did not guess; you followed the thread. That is the whole game.

### 2.1 Logs are structured templates, never interpolated strings

The non-negotiable Serilog rule (Week 13, restated): a log message is a **template with named properties**, not an interpolated string.

```csharp
// WRONG: the structure is destroyed; you cannot query "all logs for lessonId X".
_log.LogInformation($"Submission {submissionId} accepted for lesson {lessonId}");

// RIGHT: lessonId and submissionId are queryable properties in Loki via LogQL.
_log.LogInformation("Submission {SubmissionId} accepted for lesson {LessonId}",
    submissionId, lessonId);
```

The right form lands in Loki as a structured event you can filter with `{app="workshop-api"} | json | LessonId="..."`. The wrong form lands as opaque text you can only substring-match. Citation: <https://github.com/serilog/serilog/wiki/Structured-Data>.

### 2.2 Metrics are the RED method

For a request-driven service, the three metrics worth emitting are **Rate, Errors, Duration** — the RED method (<https://grafana.com/blog/2018/08/02/the-red-method-how-to-instrument-your-services/>). ASP.NET Core 9 emits these automatically (`http.server.request.duration` is a histogram; rate and errors derive from it), and you add domain metrics with `System.Diagnostics.Metrics`:

```csharp
public sealed class WorkshopMetrics
{
    public const string MeterName = "Workshop.Api";
    private readonly Counter<long> _submissions;
    private readonly Histogram<double> _gradingDuration;

    public WorkshopMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(MeterName);
        _submissions = meter.CreateCounter<long>(
            "workshop.submissions.accepted", unit: "{submission}");
        _gradingDuration = meter.CreateHistogram<double>(
            "workshop.grading.duration", unit: "ms");
    }

    public void SubmissionAccepted(string tenant) =>
        _submissions.Add(1, new KeyValuePair<string, object?>("tenant", tenant));

    public void GradingCompleted(double ms) => _gradingDuration.Record(ms);
}
```

`Meter` and `Counter` cost essentially nothing when no exporter is listening — the .NET diagnostics primitives are designed to be near-free at rest (<https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>), so you instrument liberally. Citation: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>.

### 2.3 Traces are spans on an `ActivitySource`

A trace is a tree of spans sharing one `TraceId`. The framework's automatic instrumentation creates the big spans (the incoming HTTP request, the outgoing gRPC call, the EF Core command); you add domain spans with an `ActivitySource` (<https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>):

```csharp
public static class WorkshopActivity
{
    public static readonly ActivitySource Source = new("Workshop.Api", "1.0.0");
}

// Inside the grading handler:
using var activity = WorkshopActivity.Source.StartActivity("grade.submission");
activity?.SetTag("workshop.lesson_id", lessonId);
activity?.SetTag("workshop.tenant", tenant);
// ... do the grading; the EF Core span auto-nests under this one ...
activity?.SetTag("workshop.grade", grade);
```

The child EF Core and Npgsql spans nest under your `grade.submission` span automatically because they share the ambient `Activity.Current`. The result in Tempo is a flame graph: `HTTP POST /submissions` → `MediatR pipeline` → `grade.submission` → `EF Core SaveChanges` → `Npgsql command`, each with its own duration. **Never** put a token, password, or `access_token` in a span tag — span tags are visible to anyone with Grafana access. Citation: <https://opentelemetry.io/docs/languages/net/instrumentation/>.

## 3. Wiring the OpenTelemetry SDK in .NET 9

The SDK ties the three signals together and exports them over OTLP to the collector. The registration (<https://opentelemetry.io/docs/languages/net/getting-started/>):

```csharp
#nullable enable
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var otlpEndpoint = builder.Configuration["Otel:Endpoint"] ?? "http://localhost:4317";

var resource = ResourceBuilder.CreateDefault()
    .AddService(serviceName: "workshop-api", serviceVersion: "1.0.0")
    .AddAttributes(new KeyValuePair<string, object>[]
    {
        new("deployment.environment", builder.Environment.EnvironmentName)
    });

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("workshop-api", serviceVersion: "1.0.0"))
    .WithTracing(t => t
        .AddSource(WorkshopActivity.Source.Name)          // our domain spans
        .AddAspNetCoreInstrumentation(o => o.RecordException = true)
        .AddGrpcClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(m => m
        .AddMeter(WorkshopMetrics.MeterName)              // our domain metrics
        .AddAspNetCoreInstrumentation()                   // RED metrics, free
        .AddRuntimeInstrumentation()                      // GC, threadpool, etc.
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

// Logs: Serilog -> OTLP -> Loki, with TraceId/SpanId enrichment.
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.WithSpan()                                    // adds TraceId/SpanId to every log
    .WriteTo.OpenTelemetry(o =>
    {
        o.Endpoint = otlpEndpoint;
        o.ResourceAttributes.Add("service.name", "workshop-api");
    }));
```

Three things to name:

1. **`service.name` is the join key.** Every signal carries it as a resource attribute; Grafana uses it to know that a trace, a log, and a metric came from the same service. Spell it identically everywhere.
2. **`Enrich.WithSpan()` is the correlation enabler for logs.** It stamps every Serilog event with the ambient `TraceId` and `SpanId`, so Loki can answer "show me all logs for this trace." Without it, logs and traces are two disconnected islands. Citation: <https://github.com/serilog/serilog-sinks-opentelemetry>.
3. **`SetDbStatementForText = true` records the SQL on the EF Core span.** This is the line that turns "the database span was slow" into "*this query* was slow" — invaluable, and safe because parameter values are not included by default. Citation: <https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.EntityFrameworkCore>.

## 4. The stack — `docker compose` for Grafana, Loki, Tempo, Prometheus, and the collector

The application exports OTLP to an **OpenTelemetry Collector**, which fans the three signals out to the three backends, which Grafana reads. The topology:

```
                                       +----------------------------+
   workshop-api  --OTLP/gRPC:4317-->   |  OpenTelemetry Collector   |
   (traces, metrics, logs)             |  receivers: otlp           |
                                       |  processors: batch, redact |
                                       |  exporters:                |
                                       |    logs    -> loki         |---> [ Loki    ]
                                       |    traces  -> tempo (otlp) |---> [ Tempo   ]
                                       |    metrics -> prometheus   |---> [ Prometheus ]
                                       +----------------------------+          |
                                                                               v
                                                                         [ Grafana ]
                                                          (datasources: Loki, Tempo, Prometheus)
```

The `docker-compose.yml` (the full file ships in `mini-project/observability/`):

```yaml
services:
  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.111.0
    command: ["--config=/etc/otel/config.yaml"]
    volumes: [ "./otel-collector.yaml:/etc/otel/config.yaml:ro" ]
    ports: [ "4317:4317", "4318:4318" ]      # OTLP gRPC and HTTP
    depends_on: [ loki, tempo, prometheus ]

  loki:
    image: grafana/loki:3.2.0
    command: ["-config.file=/etc/loki/config.yaml"]
    volumes: [ "./loki.yaml:/etc/loki/config.yaml:ro" ]
    ports: [ "3100:3100" ]

  tempo:
    image: grafana/tempo:2.6.0
    command: ["-config.file=/etc/tempo/config.yaml"]
    volumes: [ "./tempo.yaml:/etc/tempo/config.yaml:ro" ]
    ports: [ "3200:3200" ]

  prometheus:
    image: prom/prometheus:v2.54.1
    command:
      - "--config.file=/etc/prometheus/prometheus.yml"
      - "--enable-feature=exemplar-storage"   # required for exemplars (section 5)
    volumes: [ "./prometheus.yml:/etc/prometheus/prometheus.yml:ro" ]
    ports: [ "9090:9090" ]

  grafana:
    image: grafana/grafana:11.2.0
    environment:
      - GF_AUTH_ANONYMOUS_ENABLED=true
      - GF_AUTH_ANONYMOUS_ORG_ROLE=Admin
    volumes: [ "./grafana-datasources.yaml:/etc/grafana/provisioning/datasources/ds.yaml:ro" ]
    ports: [ "3000:3000" ]
    depends_on: [ loki, tempo, prometheus ]
```

The collector pipeline (`otel-collector.yaml`) — note the `redaction` processor that scrubs the `access_token` (Lecture 1, section 9):

```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }

processors:
  batch: {}
  redaction/safe:
    allow_all_keys: true
    blocked_values: [ "access_token=[^&\\s]+" ]   # never let a token reach a backend

exporters:
  otlphttp/loki:
    endpoint: http://loki:3100/otlp
  otlp/tempo:
    endpoint: tempo:4317
    tls: { insecure: true }
  prometheus:
    endpoint: 0.0.0.0:8889
    enable_open_metrics: true                      # required to pass exemplars through

service:
  pipelines:
    traces:  { receivers: [otlp], processors: [batch, redaction/safe], exporters: [otlp/tempo] }
    metrics: { receivers: [otlp], processors: [batch], exporters: [prometheus] }
    logs:    { receivers: [otlp], processors: [batch, redaction/safe], exporters: [otlphttp/loki] }
```

Bring it up with `docker compose -f observability/docker-compose.yml up -d`, point the app's `Otel:Endpoint` at `http://localhost:4317`, generate some traffic, and open Grafana at `http://localhost:3000`.

## 5. Exemplars — the link from a metric spike to the trace that caused it

A metric tells you p99 latency spiked; it does not tell you *which request* was slow. An **exemplar** closes that gap: it is a metric data point tagged with the `TraceId` of an exemplary request (<https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>). When the OpenTelemetry SDK records a histogram value while an `Activity` is current, it attaches that activity's `TraceId` to the bucket as an exemplar — automatically, no code change. The chain that makes it visible end-to-end:

1. The app records `http.server.request.duration` while a trace is active → the SDK attaches the `TraceId` as an exemplar.
2. The collector's Prometheus exporter has `enable_open_metrics: true` → exemplars survive the export.
3. Prometheus runs with `--enable-feature=exemplar-storage` → exemplars are stored.
4. Grafana's Prometheus datasource has exemplars enabled and a `traceID` link to the Tempo datasource → a latency panel shows little diamonds; clicking one opens the trace in Tempo.

Now the operator's path is: see the p99 spike → click the exemplar diamond on the spike → land on the exact slow trace → read its spans → click the `TraceId` to jump to its Loki logs. That is the correlated-incident walkthrough, and reproducing it is the mini-project's last milestone and Challenge 2.

## 6. The Grafana correlation — trace to logs, logs to trace

Grafana stitches the three datasources together with **derived fields** (<https://grafana.com/docs/grafana/latest/datasources/tempo/configure-tempo-data-source/#trace-to-logs>). The provisioned datasource config:

```yaml
apiVersion: 1
datasources:
  - name: Tempo
    type: tempo
    url: http://tempo:3200
    jsonData:
      tracesToLogsV2:
        datasourceUid: loki
        filterByTraceID: true        # the Loki query is {service.name="..."} | TraceId="<id>"
      serviceMap: { datasourceUid: prometheus }
  - name: Loki
    type: loki
    url: http://loki:3100
    jsonData:
      derivedFields:
        - name: TraceID
          matcherRegex: '"TraceId":"(\w+)"'
          url: '$${__value.raw}'
          datasourceUid: tempo       # a TraceId in a log line becomes a link into Tempo
  - name: Prometheus
    type: prometheus
    url: http://prometheus:9090
    jsonData:
      exemplarTraceIdDestinations:
        - name: TraceID
          datasourceUid: tempo       # an exemplar's TraceId links into Tempo
```

This is the connective tissue. With it, every signal is one click from the others, and the "open Grafana, not SSH" capability is real, not aspirational.

## 7. The correlated-incident walkthrough, narrated

Here is the capability, exercised end to end against the workshop:

1. **The alert.** A Grafana panel shows `http.server.request.duration` p99 for `POST /api/submissions` jumped from 40ms to 900ms at 14:32.
2. **The exemplar.** You click the exemplar diamond on the spike. Grafana opens the trace in Tempo.
3. **The trace.** The flame graph shows the 900ms split: 20ms in the MediatR pipeline, 870ms in a single `Npgsql command` span. The span's `db.statement` tag shows a `SELECT` with no `WHERE` on an indexed column — a full table scan introduced by a bad query in the grading handler.
4. **The logs.** You click the `TraceId` on the span. Grafana opens Loki filtered to that trace. The logs show `Grading lesson {LessonId} with {SubmissionCount} submissions` where `SubmissionCount` is 240,000 — a tenant with a runaway import.
5. **The cause, named.** A query that should have filtered by `LessonId` and tenant was loading the whole table. You have the lesson id, the tenant, the SQL, and the trace — all from the dashboard. No SSH, no redeploy, no debugger.

That five-step walk is the deliverable. The exercise wires the stack; the mini-project proves you can perform the walk; Challenge 2 injects a fault and makes you do it cold.

## 8. Sampling, cardinality, and the cost of observability

Observability is not free, and the two costs that bite in production are **trace volume** and **metric cardinality**. Address both deliberately.

**Trace sampling.** Exporting every span of every request is fine in dev and ruinous at scale. The default in the SDK is `AlwaysOn` (sample everything), which is correct for local development and the demo. In production you switch to a **parent-based, ratio sampler** so that either the whole trace is kept or the whole trace is dropped (you never want half a trace), and you keep, say, 10% of traces plus 100% of error traces:

```csharp
.WithTracing(t => t
    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)))
    // ... instrumentation and exporter as before ...)
```

The `ParentBased` wrapper honors the sampling decision made upstream (so a trace sampled at the API edge stays sampled through the gRPC call), and the ratio sampler keeps a deterministic 10% by `TraceId`. The harden discipline here is "tail-based sampling for errors" — keep every trace that ended in an error or exceeded a latency threshold, sample the boring successes — which the collector can do with the `tail_sampling` processor when you outgrow head sampling. Citation: <https://opentelemetry.io/docs/concepts/sampling/>.

**Metric cardinality.** A metric's cost is the number of distinct *tag combinations* (time series) it produces, and the fastest way to blow up a Prometheus instance is to put a high-cardinality value in a tag. Putting `tenant` on `workshop.submissions.accepted` is fine — there are tens of tenants. Putting `submission_id` or `user_id` on a metric is a catastrophe — there are millions, and each becomes its own time series. The rule: **tag dimensions you will group or filter by (route, tenant, status code); never tag by an identity (submission id, user id, trace id)**. Identities belong on *traces and logs*, where each event is stored once, not on *metrics*, where each tag value multiplies storage. The N+1 of observability is a metric accidentally tagged by a request id; it will OOM your Prometheus before it tells you anything useful. Citation: <https://prometheus.io/docs/practices/naming/#labels>.

## 9. Logs at the right level, and the audit dividend

Lecture 1 noted that **R**epudiation in STRIDE is answered by audit logging. The observability stack pays that off: when alice deletes a submission, the structured log line `_log.LogInformation("Submission {SubmissionId} deleted by {UserId}", id, userId)` lands in Loki with alice's `sub`, the `TenantId`, and — via `Enrich.WithSpan()` — the `TraceId` of the deleting request. Now "did alice really delete it" is a LogQL query, not a shrug. Two disciplines make this trustworthy:

1. **Level the logs honestly.** `Information` for business events you will audit (submission accepted, lesson published, role granted), `Warning` for handled-but-notable conditions (a rate-limit rejection, a retry that succeeded), `Error` for unhandled failures, `Debug` for the firehose you only want when chasing a bug. Production runs at `Information`; the `Microsoft.*` framework categories are pinned to `Warning` so framework chatter does not drown your business events. Configure this in `appsettings.json`, not in code, so you can raise a category to `Debug` for one incident without a redeploy.
2. **Never log a secret or PII you would not put in a span tag.** The same rule from §2.3 applies: tokens, passwords, and unmasked PII do not go in log properties. The collector's redaction processor (§4) is a backstop, not a license to log carelessly — redaction is defense in depth, and the first line of defense is not logging the secret in the first place.

The audit dividend is the reason the observability work is part of the *security* milestone and not a separate concern: a service you can debug from logs alone is also a service whose actions you can reconstruct after the fact, which is exactly what repudiation-resistance requires. Citation: <https://github.com/serilog/serilog/wiki/Configuration-Basics>.

## 10. What you can do now

You can emit all three signals from a .NET 9 service with the OpenTelemetry SDK, fan them through a collector to Loki, Tempo, and Prometheus, correlate them in Grafana, and debug a request from the dashboard alone. Combined with Lecture 1's closed auth surface and Lecture 2's deliberate MediatR/AutoMapper, you have the three pillars of the week's milestone: **the auth boundary is closed and tested, the cross-cutting concerns live once, and the running system is observable.** The mini-project assembles all three into the Production Polish milestone, and the homework and challenges drive each pillar to a state you can prove.

Citations for this lecture: .NET observability with OpenTelemetry at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>; the OpenTelemetry .NET docs at <https://opentelemetry.io/docs/languages/net/>; the OpenTelemetry Collector at <https://opentelemetry.io/docs/collector/>; Grafana Tempo at <https://grafana.com/docs/tempo/latest/>; Grafana Loki at <https://grafana.com/docs/loki/latest/>; the exemplars data model at <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>; the RED method at <https://grafana.com/blog/2018/08/02/the-red-method-how-to-instrument-your-services/>; and `Serilog.Sinks.OpenTelemetry` at <https://github.com/serilog/serilog-sinks-opentelemetry>.
