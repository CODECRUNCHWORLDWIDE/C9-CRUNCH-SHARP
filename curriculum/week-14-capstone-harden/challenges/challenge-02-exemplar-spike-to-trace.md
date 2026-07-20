# Challenge 2 — Make a Latency Spike in Grafana Link Directly to the Offending Trace in Tempo via an OpenTelemetry Exemplar

> **Time:** 2 hours. **Prerequisites:** Exercises 3 and 4; Lecture 3; the observability stack running. **Citations:** the exemplar spec at <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>, OTel .NET metrics at <https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md#exemplars>, Grafana exemplars at <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>, and Tempo trace-to-logs at <https://grafana.com/docs/grafana/latest/datasources/tempo/configure-tempo-data-source/>.

## The premise

Your `PolyglotWorkshop` is instrumented (Week 12 foundation, extended in Lecture 3). Traces flow to Tempo, logs to Loki, metrics to Prometheus, all visible in Grafana. But the three signals are *islands*: when the analytics endpoint gets slow, you see a spike in the latency histogram, and then you have to *guess* which request caused it — there is no link from the metric to the trace. An on-call engineer can see "something is slow" but cannot see "this exact request was slow because of this exact query."

This challenge closes the gap. You will deliberately make the Dapper analytics query (`GET /api/analytics/progress`) slow for *one specific tenant* (a missing index on a large dataset), then wire an OpenTelemetry **exemplar** so that clicking the spike in the Grafana histogram jumps straight to the offending trace in Tempo, and from the trace to the logs in Loki. The skill is the operator workflow: spike → trace → log, three clicks, no debugger.

By the end you will have produced: (a) a reproducible latency spike isolated to one tenant, (b) the exemplar wiring that links the spike's histogram bucket to the trace, and (c) a captured click-through screenshot proving metric → trace → log works end to end.

## Setup

Run the observability stack and seed a "heavy" tenant with enough submissions that an un-indexed analytics query is visibly slow:

```bash
docker compose -f docker-compose.observability.yml up -d   # grafana, loki, tempo, prometheus, otel-collector
dotnet run --project src/Workshop.Api
./scripts/seed-heavy-tenant.sh --tenant bigcohort --submissions 200000
```

Confirm Prometheus was started with exemplar storage and Grafana is reachable:

```bash
docker inspect prometheus --format '{{.Args}}' | grep -- '--enable-feature=exemplar-storage'   # must print
open http://localhost:3000   # Grafana
```

The analytics query in `AnalyticsQuery.GetProgressAsync` runs over the `submissions` table. Drop the index on `(tenant_id, exercise_id)` (or never add it) so the heavy tenant's query does a sequential scan and takes ~800ms while every other tenant returns in single-digit milliseconds.

## The plan

### Step 1 — generate the spike

Drive mixed load: many fast requests from small tenants, a few slow ones from `bigcohort`:

```bash
for i in $(seq 1 200); do
  T=$([ $((i % 20)) -eq 0 ] && echo bigcohort || echo small$((i % 5)))
  curl -s -H "Authorization: Bearer $(./scripts/mint-token.sh --tenant $T)" \
       http://localhost:8080/api/analytics/progress > /dev/null
done
```

Open the `workshop.analytics.query.duration` histogram panel in Grafana. You should see a bimodal distribution: a dense cluster near 5ms and a sparse high tail near 800ms.

### Step 2 — confirm the exemplar is recorded inside the span

The exemplar is attached automatically *only* when the histogram `Record` happens inside an active `Activity`. Verify your `AnalyticsQuery` matches Lecture 3:

```csharp
using var activity = Source.StartActivity("analytics.progress");   // span is active...
var sw = Stopwatch.GetTimestamp();
var rows = await conn.QueryAsync<ProgressRow>(ProgressSql, new { TenantId = tenant.TenantId });
metrics.AnalyticsQueryDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);  // ...recorded here
```

If `Record` is outside the `using` block, `Activity.Current` is `null` at record time and no exemplar is attached — the histogram has no dots. This is the single most common failure; check it first.

### Step 3 — enable exemplars on the panel and the datasource

In the Grafana histogram panel options, toggle **"Exemplars"** on. In the Prometheus datasource config, ensure the exemplar trace-id internal link points at the Tempo datasource. These are provisioning settings; commit them in `grafana/provisioning/datasources/`:

```yaml
- name: Prometheus
  type: prometheus
  jsonData:
    exemplarTraceIdDestinations:
      - name: trace_id
        datasourceUid: tempo
```

### Step 4 — walk the click-through

1. On the histogram panel, the high-tail bucket now shows **exemplar dots**.
2. Click a dot on the ~800ms bucket. Grafana reads its `trace_id` and opens the Tempo trace.
3. The flame graph shows the time in the `analytics.progress` span; the span's `db.statement` tag shows the Dapper SQL and `workshop.row_count` shows the large count.
4. Click **"Logs for this span."** Grafana queries Loki `{service_name="workshop-api"} | trace_id="<id>"`; the structured log lines for that request appear, including any query-plan warning you logged.

Capture the screenshot of the dot → flame graph → logs sequence.

### Step 5 — fix the cause and watch the tail collapse

Add the index `CREATE INDEX ix_submissions_tenant_exercise ON submissions (tenant_id, exercise_id);` (via an EF migration). Re-run the load. The high tail disappears; the histogram collapses to the fast cluster. The exemplar workflow is what *told you* the index was missing without a profiler.

## Acceptance criteria

1. `EXEMPLAR.md` documents the reproduced spike (the bimodal histogram screenshot) and which tenant caused it.
2. The histogram `Record` call is demonstrably inside the active `Activity`; the exemplar dots appear on the panel.
3. The Prometheus datasource provisioning links exemplar `trace_id` to the Tempo datasource (config committed).
4. The click-through screenshot shows the full metric → trace → log path for one slow request.
5. The fix (the index migration) is applied and the high latency tail collapses; before/after histograms are captured.
6. A one-paragraph write-up explains why the exemplar found the cause faster than reading logs or staring at metrics alone.

## Stretch goals

1. **Circuit-breaker observability.** Wire a stat panel reading the Polly circuit-breaker state metric (Lecture 2) and reproduce an open breaker by pointing the notification client at a dead downstream. Capture the breaker opening in real time and correlate it with the failed-publish traces. Cite <https://github.com/App-vNext/Polly>.
2. **Trace-driven sampling.** Switch the trace sampler to 10% (`TraceIdRatioBased`) and confirm exemplars *still* link to traces — because each metric data point keeps a sampled trace id even when most traces are dropped. Explain why exemplars survive sampling. Cite <https://opentelemetry.io/docs/concepts/sampling/>.
3. **An alert with a trace link.** Add a Prometheus alert rule that fires when the analytics p99 exceeds 200ms, and configure the Grafana alert to include a link to the exemplar trace in its annotation. Discuss why an alert that links the on-call engineer straight to the trace cuts mean-time-to-resolution. Cite <https://grafana.com/docs/grafana/latest/alerting/>.

Cited pages: the exemplar spec at <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>, OTel .NET metrics/exemplars at <https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md#exemplars>, Grafana exemplars at <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>, Tempo at <https://grafana.com/docs/tempo/latest/>, and the .NET metrics-instrumentation guide at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation>.
