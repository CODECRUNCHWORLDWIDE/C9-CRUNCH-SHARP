# Challenge 2 — Debug an Injected Incident Using Only Grafana, and Write the Post-Incident Note

> **Time:** 2 hours. **Prerequisites:** Lecture 3, Exercise 4 (the stack is up and the three signals flow). **Citations:** the .NET observability guide at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>, Grafana Tempo at <https://grafana.com/docs/tempo/latest/>, and Grafana Loki at <https://grafana.com/docs/loki/latest/>.

## The premise

The observability target for the week is binary and behavioral: **handed an incident, you diagnose it from Grafana alone** — no SSH, no debugger, no redeploy-with-extra-logging. This challenge proves you have that capability by injecting a fault you did not write the diagnosis for, then making you find it cold, using only the trace, the logs (by `TraceId`), and the metric that alarmed. The rule for the whole exercise: **if you reach for the source code before you have a `TraceId` from the dashboard, you have not exercised the capability — start over.**

## Setup — three faults, injected by a peer

Have a teammate (or the provided `fault-injector` script) enable **one** of the following faults without telling you which, by flipping an environment variable the app reads at startup. Each is a realistic production failure mode:

- **`FAULT=nplus1`** — the grading read path loads each submission's lesson in a loop instead of a join, producing an N+1 query storm. Symptom: p99 latency on `GET /api/lessons/{id}/submissions` climbs with the number of submissions.
- **`FAULT=poolexhaust`** — a code path opens a `DbContext` and forgets to dispose it under load, exhausting the Npgsql connection pool. Symptom: requests hang, then fail with a pool-timeout, and the error rate metric spikes while throughput collapses.
- **`FAULT=tenantleak`** — a new (deliberately broken) analytics endpoint forgets the tenant filter, so a tenant-1 user occasionally sees tenant-2 counts. Symptom: no latency change, no error-rate change — only a correctness anomaly visible in the logs and a trace whose `db.statement` lacks the tenant predicate.

You do not know which one is active. Your job is to name it from Grafana.

## The diagnosis — follow the thread, do not guess

Work the signals in this order (the order is the lesson):

1. **Start at the metrics (the alarm).** In Grafana Explore → Prometheus, graph the RED trio for the suspect routes:
   - Rate: `sum(rate(http_server_request_duration_seconds_count[1m])) by (http_route)`
   - Errors: `sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[1m])) by (http_route)`
   - Duration p99: `histogram_quantile(0.99, sum(rate(http_server_request_duration_seconds_bucket[1m])) by (le, http_route))`
   Which signal moved? Latency up but errors flat → likely `nplus1`. Errors up and throughput down → likely `poolexhaust`. Nothing moved → likely `tenantleak` (correctness, not performance).

2. **Jump to a trace via the exemplar.** Click an exemplar diamond on the panel that moved. For `tenantleak`, where no panel moves, instead open Tempo's search and find a trace for the analytics route. Read the span tree.

3. **Read the slow / wrong span.** For `nplus1`, the trace shows dozens of sibling `Npgsql command` spans, each a tiny `SELECT ... FROM Lessons WHERE Id = @p` — the N+1 fingerprint. For `poolexhaust`, the trace shows a long gap *before* the first DB span (the request is waiting on the pool), and the span has an exception event. For `tenantleak`, the `db.statement` tag on the analytics span lacks the `TenantId = @tenant` predicate that every other tenant-scoped query has.

4. **Confirm in the logs by `TraceId`.** Click the `TraceId` on the span to open Loki filtered to that trace. For `nplus1`, the log shows the submission count that triggered the storm. For `poolexhaust`, the log shows the Npgsql pool-timeout exception with the same `TraceId`. For `tenantleak`, the log shows the tenant on the request and the (wrong) tenant in the result.

5. **Name the cause.** You should now have: the route, the trace id, the offending SQL (from `db.statement`), and the corroborating log line — all from the dashboard. Write the one-sentence root cause.

## The post-incident note — the deliverable

Write a real post-incident note (the kind you would attach to the runbook). It must contain, and only contain, what the dashboard told you:

```markdown
# Post-incident: <route> latency/error/correctness anomaly

- Detected: <which RED signal moved, with the PromQL and the value>
- Trace: <trace id>, reached via <exemplar | Tempo search>
- Root cause: <one sentence, naming the span and the db.statement evidence>
- Corroborating log: <the Loki line, with its TraceId>
- Fix: <the one-line code change that resolves it>
- Guardrail: <the test or alert that would have caught it earlier>
```

The **Guardrail** line is graded hardest. For `nplus1`, it is a BenchmarkDotNet regression test on the read path (you have one from week 12) plus a trace-based alert on span count. For `poolexhaust`, it is a metric alert on `db.client.connections.usage` approaching the pool max, plus a test that asserts the path disposes its context. For `tenantleak`, it is the cross-tenant integration test from Challenge 1 (`BolaTests`/tenant) that should have failed — and the EF Core global query filter (Lecture 1) that would have made the leak impossible regardless of the missing predicate.

## Acceptance criteria

1. **You named the active fault correctly from Grafana**, and your note's "Detected" and "Trace" lines were obtained before you opened the source.
2. **The note contains a real trace id, a real PromQL query, and a real Loki line** from your run — not generic placeholders.
3. **The Guardrail you propose would actually have caught the fault** — and for `tenantleak`, you note that the global query filter is the *structural* fix that beats any test.
4. **You did the walk for at least two of the three faults** (have your peer flip a second one).

## Deliverable

`challenges/02-incident/`: two post-incident notes (one per fault you diagnosed), each with the captured trace id, PromQL, and Loki line, plus a screenshot of the Tempo flame graph that named the cause. Add a 200-word reflection on which signal was the *entry point* for each fault and why the RED metrics alone were insufficient to name the root cause (they tell you *that* something is wrong and *where*; only the trace tells you *what*). Cite the observability guide and the Tempo/Loki docs for the correlation mechanism you used.
