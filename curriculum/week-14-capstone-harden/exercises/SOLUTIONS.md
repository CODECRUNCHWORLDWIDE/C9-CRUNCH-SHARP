# Week 14 Exercise Solutions

These are the worked solutions to the four exercises. Each shows the canonical implementation, the verification output the grader looks for, and the most common ways the exercise gets done wrong. Read your own solution first; check it against the canonical one second. The point of this file is not to be copied — it is to surface the patterns and failure modes so you recognize them in your own `PolyglotWorkshop` code. Every exercise edits the same repo from Milestone 1; none of them ask you to start a new project.

---

## Exercise 01 — Close the BOLA leak

`exercises/exercise-01-close-the-bola-leak.cs` starts from the Week 13 `GET /api/submissions/{id}` handler that loads by id with no ownership check. Task one is to *prove* the leak (a learner in tenant A reads tenant B's submission); task two is to close it with a tenant `Where` clause; task three is the structural fix — an EF Core global query filter so the next endpoint cannot reintroduce it.

The canonical fix at the handler:

```csharp
app.MapGet("/api/submissions/{id:guid}", async (
    Guid id, WorkshopDbContext db, ITenantContext tenant, CancellationToken ct) =>
{
    var submission = await db.Submissions
        .Where(s => s.Id == id && s.TenantId == tenant.TenantId)
        .FirstOrDefaultAsync(ct);
    return submission is null ? Results.NotFound() : Results.Ok(submission.ToDto());
}).RequireAuthorization();
```

And the structural fix in `OnModelCreating`:

```csharp
modelBuilder.Entity<Submission>().HasQueryFilter(s => s.TenantId == _tenantProvider.TenantId);
```

### Verification output

1. **Before the fix:** with tenant A's token, `GET /api/submissions/{B's id}` returns `200` and tenant B's content. This is the leak; capture it.
2. **After the `Where` fix:** the same request returns `404` (not `403` — we do not confirm the row exists to a caller who may not see it; `404` leaks nothing).
3. **After the global filter:** revert the per-handler `Where`, leaving only `db.Submissions.FindAsync(id)`. The cross-tenant read *still* returns `404`, because the filter is applied automatically. This is the proof the filter, not the handler, is now load-bearing.
4. The within-tenant read (tenant A reading tenant A's own submission) returns `200` in all three states.

### Common stumbles

- **Returning `403` instead of `404`.** A `403` confirms the row exists, which is itself an information leak (the attacker now knows id `X` is a real submission in some tenant). Return `404` for "not yours."
- **Filtering in memory.** `db.Submissions.ToList().Where(s => s.TenantId == ...)` pulls every tenant's rows into the app and then filters — the leak is closed at the API but the database still shipped every row over the wire, and a large tenant times out. Keep the `Where` in the `IQueryable` so EF translates it to a SQL `WHERE`.
- **Forgetting the filter makes the context non-poolable.** A query filter that closes over an injected `_tenantProvider` means the `DbContext` cannot be pooled with a captured singleton tenant. Use the supported per-request tenant pattern (`IDbContextFactory` + a scoped tenant accessor) the EF docs describe, or you will get the same tenant's filter applied to the wrong request under load — a *worse* leak than the one you closed.
- **Not testing the gRPC path.** The leak exists on `workshop.v1` `GetSubmission` too. The global query filter closes both because both share the `DbContext`; prove it with a `grpcurl` call carrying tenant A's token against tenant B's id.

### The non-poolable-context trap, reproduced

The most dangerous wrong answer *passes* the single-request test and leaks under load. If you register the context with `AddDbContextPool` and capture a singleton tenant accessor in the filter, the pooled context retains the *previous* request's tenant. Reproduce it by hammering two tenants concurrently:

```bash
# two tenants, 200 interleaved reads each; without the scoped accessor, some return the wrong tenant's row
seq 1 200 | xargs -P16 -I{} curl -s -H "Authorization: Bearer $TOKEN_A" \
  http://localhost:8080/api/submissions/$SUB_A | jq -r .id | sort | uniq -c
```

If any line shows an id you did not create under tenant A, the pooled filter leaked. The fix is the scoped `ITenantProvider` from Lecture 1 (read once from `HttpContext.User`), with `AddDbContext` rather than the pool when the filter depends on per-request state. This is the single failure that turns "I closed the BOLA leak" into "I introduced an intermittent, load-dependent BOLA leak" — strictly worse, because it only appears in production under concurrency.

### Why 404 and not 403, restated in code

The status code is a deliberate information-disclosure decision. A tenant-filtered query that finds nothing is *indistinguishable* from a non-existent id — and that is the point:

```csharp
var submission = await db.Submissions          // filter already applied: other tenants' rows are invisible
    .FirstOrDefaultAsync(s => s.Id == id, ct);
return submission is null ? Results.NotFound() : Results.Ok(submission.ToDto());  // 404, never 403
```

A `403` would confirm the row exists in *some* tenant, handing the attacker a working id-enumeration oracle. The filter + `404` reveals nothing.

---

## Exercise 02 — MediatR pipeline behavior

`exercises/exercise-02-mediatr-pipeline-behavior.cs` routes `CreateSubmission` through MediatR with a `ValidationBehavior` and an `ObservabilityBehavior`, and — deliberately — leaves the `/health/db` probe a direct minimal-API call. The point is to feel the difference between a path that earns the mediator and one that does not.

The command, handler, and validator:

```csharp
public sealed record CreateSubmissionCommand(Guid ExerciseId, string Content) : IRequest<Guid>;

public sealed class CreateSubmissionValidator : AbstractValidator<CreateSubmissionCommand>
{
    public CreateSubmissionValidator()
    {
        RuleFor(x => x.ExerciseId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(20_000);
    }
}

public sealed class CreateSubmissionHandler(WorkshopDbContext db, ITenantContext tenant, ClaimsPrincipal user)
    : IRequestHandler<CreateSubmissionCommand, Guid>
{
    public async Task<Guid> Handle(CreateSubmissionCommand cmd, CancellationToken ct)
    {
        var submission = new Submission
        {
            Id = Guid.CreateVersion7(), ExerciseId = cmd.ExerciseId, Content = cmd.Content,
            LearnerId = user.GetSubjectId(), TenantId = tenant.TenantId, CreatedAt = DateTimeOffset.UtcNow
        };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync(ct);
        return submission.Id;
    }
}
```

### Verification output

1. `POST /api/submissions` with valid body and token returns `201` and the new id; the `ObservabilityBehavior` logs `Handled CreateSubmissionCommand in 7.2ms` with the request's trace id.
2. `POST /api/submissions` with an empty `Content` returns `400` with an RFC 9457 `ProblemDetails` body listing the validation failure — produced by the `ValidationBehavior` throwing `ValidationException`, mapped at the edge, **not** by code inside the handler.
3. The `/health/db` probe returns `200`/`503` directly with no MediatR types in the call path. Grepping the build for a `HealthQuery` returns nothing — that is the correct answer to "should this be a mediator request?": no.

### Common stumbles

- **Putting validation inside the handler.** If the handler starts with `if (string.IsNullOrEmpty(cmd.Content)) return ...`, you have not used the behavior; you have duplicated the check the behavior exists to centralize. Delete it; let the pipeline validate.
- **Registering behaviors in the wrong order.** `ObservabilityBehavior` should wrap `ValidationBehavior` so a validation failure is still traced. Reversed, a rejected request produces no span.
- **Routing everything through MediatR.** The most common over-correction: a learner converts the health probe, the OpenAPI doc endpoint, and a trivial `GET /api/lessons/{id}` into commands. Each gains four artifacts and zero behavior. Delete them back to direct calls.

### What the ProblemDetails body looks like

The grader checks that the validation failure is RFC 9457, produced by the behavior, not hand-rolled in the handler. The `400` body for an empty `Content` should read:

```json
{
  "type": "https://tools.ietf.org/html/rfc9457",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Content": ["'Content' must not be empty."] },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-a3ce929d0e0e4736-01"
}
```

Note the `traceId` — the same one the `ObservabilityBehavior` logged. A client that reports a rejected request can quote the `traceId` and you can pull the exact trace; that is the API3/observability seam paying off. The mapping from `ValidationException` to this body happens once, at the edge (`app.UseExceptionHandler` + a problem-details writer), not in twenty handlers.

### The artifact count, made concrete

The exercise asks you to *feel* the editing thesis. Count the artifacts for the health probe done both ways:

```
Direct call:   1 artifact  (the MapGet lambda)
Via MediatR:   4 artifacts (HealthQuery record, HealthQueryHandler, AddMediatR registration, Send call)
               ...for zero behaviors that apply. Net cost: +3 artifacts, +0 value. Delete.
```

For `CreateSubmissionCommand` the same count is +3 artifacts for +3 behaviors that *do* apply (validation, observability, transaction). The artifact count is identical; the *value* is what differs, and that is the whole test.

---

## Exercise 03 — Polly and the outbox

`exercises/exercise-03-polly-and-the-outbox.cs` has two parts: wrap the `NotificationClient` in a Polly v8 resilience pipeline, and move the `SubmissionCreated` broadcast from inline-in-the-handler to a transactional outbox drained by a `BackgroundService`.

The resilience registration (timeout innermost, retry, breaker outermost) and the outbox write in the same transaction as the domain row are shown in Lecture 2; the canonical solution matches them.

### Verification output

1. **Resilience.** Point `NotificationClient` at a fault-injecting stub that returns `503` on the first two calls and `200` on the third. With the retry strategy, the publish succeeds; the trace shows three `GrpcClient`/`HttpClient` child spans, two with `error` status. Without the strategy it fails on the first `503`.
2. **Circuit breaker.** Make the stub return `503` for 20 consecutive calls. After the failure ratio is exceeded within the sampling window, further calls throw `BrokenCircuitException` *immediately* (sub-millisecond) instead of waiting for the timeout — visible in the latency histogram as a cliff, not a plateau.
3. **Outbox atomicity.** `POST /api/submissions` returns `201` even when the notification stub is down. Query the `outbox_messages` table: the row exists with `processed_at = NULL`. Bring the stub up; within a second the drainer sets `processed_at` and the notification arrives.
4. **Decoupled trace.** The request trace ends at `SaveChangesAsync`. The drainer's publish is a *separate* trace with a *different* trace id — by design. Capture both and confirm they do not share an id.

### Common stumbles

- **Doing the broadcast inside the request transaction over the network.** Defeats the whole point — the user's `201` now waits on the downstream again. The outbox row write is local; the network call happens later, in the drainer.
- **Wrong Polly strategy order.** Putting the circuit breaker *innermost* means each retry trips the breaker faster than intended; timeout *outermost* bounds the whole retry sequence instead of each attempt. The order is timeout → retry → breaker, inner to outer.
- **Drainer respects the tenant query filter.** If the drainer does not call `IgnoreQueryFilters()`, it only ever sees the (empty) current request's tenant and silently processes nothing. This is the single most common "the outbox never drains" bug.
- **No backoff jitter.** Without `UseJitter = true`, every replica retries on the same schedule and re-DDoSes the recovering downstream — the thundering herd.

### Reading the resilience in the trace

The fault-injection run should produce a trace you can read like a story. For the "first two `503`s, third `200`" case, the outbound span tree is:

```
publish-notification (1.9s total)
 ├─ attempt 1  -> 503  (status: error)   ~80ms
 ├─ [retry delay ~200ms +jitter]
 ├─ attempt 2  -> 503  (status: error)   ~80ms
 ├─ [retry delay ~400ms +jitter]
 └─ attempt 3  -> 200  (status: ok)      ~75ms
```

The three child spans are the three HTTP attempts; the gaps between them are the jittered backoff. When the breaker is *open*, by contrast, there is no child span at all — the call returns `BrokenCircuitException` before any HTTP attempt is made, which is exactly the "fail fast" you want to *see* in the trace as a single sub-millisecond span with an error status, not a 2-second timeout.

### Proving the outbox decoupling with two trace ids

The grader wants proof the broadcast is on a *separate* trace. Capture both:

```bash
# the request trace ends at SaveChangesAsync — grab its id from the response log line
REQ_TRACE=$(curl -s -D - http://localhost:8080/api/submissions ... | grep -i trace | cut -d= -f2)
# the drainer publishes ~1s later on its own root trace — grab it from the drainer's log line
DRAIN_TRACE=$(docker logs workshop-api 2>&1 | grep "Drained outbox" | tail -1 | jq -r .TraceId)
# they must differ:
[ "$REQ_TRACE" != "$DRAIN_TRACE" ] && echo "decoupled, as designed"
```

If the two ids are equal, the broadcast is still running inside the request (the bug the outbox exists to fix) — the network call has not actually been moved off the hot path.

---

## Exercise 04 — Exemplar to trace

`exercises/exercise-04-exemplar-to-trace.cs` records the `workshop.analytics.query.duration` histogram *inside* the analytics span and walks the Grafana click-through. The histogram `Record` call must sit inside `using var activity = Source.StartActivity(...)` — that is what attaches the trace context as an exemplar.

### Verification output

1. Run the observability stack: `docker compose -f docker-compose.observability.yml up`. Confirm Grafana is at `http://localhost:3000`, Prometheus started with `--enable-feature=exemplar-storage`.
2. Drive load against `GET /api/analytics/progress` (a `for` loop of `curl`, a few of them artificially slow by querying a large tenant).
3. In Grafana, open the latency-histogram panel for `workshop.analytics.query.duration`. The p99 bucket shows **exemplar dots**.
4. Click an exemplar dot on the slow bucket. Grafana jumps to the Tempo datasource and renders that exact trace as a flame graph, showing ~800ms in the `analytics.progress` span.
5. On that span, click **"Logs for this span."** Grafana queries Loki `{service_name="workshop-api"} | trace_id="<id>"` and shows the structured log lines for that request.

The deliverable is the screenshot of the spike → trace → logs path. If clicking the exemplar does nothing, the three usual causes are below.

### Common stumbles

- **Recording the histogram outside the span.** `metrics.AnalyticsQueryDuration.Record(ms)` after the `using` block has disposed the activity attaches no exemplar — `Activity.Current` is `null` by then. The `Record` must be inside the `using`.
- **Prometheus dropped the exemplars.** Without `--enable-feature=exemplar-storage`, Prometheus accepts exemplars on the wire and silently discards them; the dots never appear. Check the compose command line.
- **The datasources are not correlated.** Exemplar dots appear but clicking them does nothing if the Grafana Prometheus datasource has no exemplar trace-id mapping to the Tempo datasource, and trace-to-logs does nothing if the Tempo datasource has no Loki correlation. These are provisioning-file settings, not app code; check `grafana/provisioning/datasources/`.
- **The log line has no trace id.** If `Enrich.WithSpan()` is missing from the Serilog config, the Loki line carries no `trace_id` and the "Logs for this span" jump returns nothing. The enricher reads `Activity.Current`; without it the correlation key is absent.

### The three switches that must all be on

The "click does nothing" failures almost always trace to one of three exemplar switches being off. The grader checks all three:

```
1. app side       -> Record() called INSIDE  using var activity = Source.StartActivity(...)
2. collector side -> prometheusremotewrite exporter has  send_exemplars: true
3. prometheus     -> started with  --enable-feature=exemplar-storage
```

Each is silent when missing — the exemplar is dropped without an error at that stage — so debugging is a matter of checking all three in order, app-side first because it is the most common miss. A quick app-side proof: hit the Prometheus `/api/v1/query_exemplars` endpoint directly; if it returns an empty `data` array while requests are flowing, the exemplar never reached storage and you bisect the three switches from there.

### Driving a visible bimodal distribution

The histogram only shows a clean spike-to-trace story if the data is bimodal — a dense fast cluster and a sparse slow tail. Seed one heavy tenant and drive mixed load:

```bash
# 19 of every 20 requests are fast (small tenants), 1 is slow (the heavy tenant) -> a visible high tail
for i in $(seq 1 200); do
  T=$([ $((i % 20)) -eq 0 ] && echo bigcohort || echo small$((i % 5)))
  curl -s -H "Authorization: Bearer $(./scripts/mint-token.sh --tenant $T)" \
       http://localhost:8080/api/analytics/progress >/dev/null
done
```

A flat distribution (every request the same speed) has no spike to click; the exercise depends on the heavy-tenant sequential scan producing the ~800ms tail described in Lecture 3.

---

## Synthesis — how the four exercises connect

The four exercises are the harden milestone in miniature, in the order you should build it:

- **Exercise 01** closed the **authorization leak** (BOLA / API1) structurally — the EF Core global query filter that makes a cross-tenant read a `404` even when a handler forgets the `Where`. This is the "delete a trust assumption" half of the editing thesis.
- **Exercise 02** introduced **MediatR where it earns its keep** — the validation and observability behaviors written once, applied to every command — and the discipline to *not* route the health probe through it. This is the "delete the reflexive abstraction" half.
- **Exercise 03** added **resilience and the outbox** — Polly so a slow downstream cannot exhaust the thread pool, and the transactional outbox so a submission's success does not depend on a broadcast. The request gets faster *and* more reliable, and the broadcast moves to its own trace.
- **Exercise 04** wired the **exemplar** — the link from a metric spike to the trace to the logs, so the whole system is debuggable from the dashboard at 3:14am.

Read together, they answer the harden contract from four angles: Exercise 01 proves the boundary holds (a test that fails on the un-hardened branch), Exercise 02 proves the cross-cutting checks cannot be forgotten, Exercise 03 proves the service degrades gracefully instead of cascading, and Exercise 04 proves you can *operate* what you built. The milestone is not new patterns — it is these four assembled into one `PolyglotWorkshop` repo with the threat model, the test suite, the observability stack, and the benchmark gate. The exercises are the cookbook; Milestone 2 is the meal.

Read the patterns. Reproduce the failure modes. Then harden your own repo until every privileged path has a test that proves the unauthorized case is rejected.
