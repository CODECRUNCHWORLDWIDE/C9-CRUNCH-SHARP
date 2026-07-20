# Week 14 — Quiz

Ten multiple-choice questions covering the OWASP API Security Top 10 in .NET, tenant-aware authorization, MediatR pipeline behaviors, AutoMapper, Polly resilience, the transactional outbox, and observability with exemplars. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — The most damaging API vulnerability

`GET /api/submissions/{id}` in the capstone loads a submission by id and returns it; it has `.RequireAuthorization()`. A learner in tenant A reads tenant B's submission by supplying its id. This is:

- (A) Broken authentication (API2) — the token must be invalid.
- (B) Broken object-level authorization (API1, BOLA) — the endpoint authenticates the caller but never checks they own the specific object.
- (C) Security misconfiguration (API8) — the route is exposed.
- (D) Not a vulnerability; `RequireAuthorization()` covers it.

## Question 2 — The right status for "not yours"

A caller requests a row that exists but belongs to another tenant. The correct response is:

- (A) `403 Forbidden`, so the caller knows the row exists but is off-limits.
- (B) `404 Not Found`, because confirming the row exists is itself an information leak.
- (C) `200 OK` with an empty body.
- (D) `401 Unauthorized`.

## Question 3 — The structural BOLA fix

You closed the leak with a per-handler `Where(s => s.TenantId == tenant.TenantId)`. The *structural* fix that stops the next endpoint reintroducing it is:

- (A) Adding `[Authorize]` to every endpoint.
- (B) An EF Core global query filter (`HasQueryFilter`) on every tenant-owned entity, applied automatically to every query.
- (C) Renaming the route to include the tenant id.
- (D) Moving the check into a middleware that inspects the URL.

## Question 4 — Mass assignment (API3)

`POST /api/submissions` binds the request body straight to the `Submission` entity, which has a `Grade` and a `TenantId`. The risk and fix are:

- (A) No risk; EF ignores extra properties.
- (B) A learner can set `Grade` and `TenantId` from the body (mass assignment); the fix is a request DTO containing only client-settable fields, with the server owning `Grade` and `TenantId`.
- (C) The risk is SQL injection; the fix is parameterized queries.
- (D) The risk is over-fetching; the fix is paging.

## Question 5 — When MediatR earns its keep

The decisive test for whether a path belongs behind MediatR is:

- (A) Whether it touches the database.
- (B) Whether a pipeline behavior (validation, transaction, shared logging/telemetry) would ever apply to it; if none would, the mediator is indirection with no payoff and should be deleted.
- (C) Whether it returns more than one field.
- (D) Whether it is called from more than one client.

## Question 6 — When to SKIP AutoMapper

Which mapping is the *worst* candidate for an AutoMapper profile?

- (A) `Lesson -> LessonDto`, twelve same-named scalar fields, no logic.
- (B) `Submission -> SubmissionDto`, which must hide `TenantId` and `IsFlagged` and compute a `StatusLabel` from `Grade` — the map is a security boundary and carries logic.
- (C) A copy of a record with identical shape.
- (D) Any map where source and destination property names match exactly.

## Question 7 — Polly strategy order

A resilience pipeline wraps an outbound call with a timeout, a retry, and a circuit breaker. The correct nesting (inner to outer) is:

- (A) Retry → timeout → breaker.
- (B) Timeout → retry → circuit breaker — the timeout bounds each attempt, the retry re-tries the bounded attempt, the breaker stops retrying a downstream that is clearly dead.
- (C) Breaker → retry → timeout.
- (D) Order does not matter; Polly normalizes it.

## Question 8 — Why the outbox

The capstone moves the `SubmissionCreated` broadcast from inline-in-the-handler to a transactional outbox drained by a `BackgroundService`. The reason is:

- (A) To make the broadcast faster.
- (B) So a learner's submission succeeds (and commits) even when SignalR or the notification downstream is down — the user's success no longer depends on an unrelated subsystem's availability; the domain row and the outbox row commit in one transaction and the drainer retries the broadcast later.
- (C) To reduce database connections.
- (D) Because background services cannot use EF Core.

## Question 9 — Why the outbox drainer bypasses the query filter

The `OutboxDrainer` calls `IgnoreQueryFilters()` on its `outbox_messages` query. Without it:

- (A) The query throws a `NullReferenceException`.
- (B) The drainer would be scoped to the current request's tenant — but it has no request, so the tenant filter would hide every message and the outbox would silently never drain.
- (C) Nothing changes; the filter does not apply to background services.
- (D) The messages would be deleted instead of processed.

## Question 10 — What an exemplar buys you

You record the analytics-latency histogram inside an active span and enable exemplars in Grafana and Prometheus. The capability this unlocks is:

- (A) Faster queries.
- (B) Clicking a latency spike in the Grafana histogram jumps to the exact Tempo trace that contributed to it, and from the span to the Loki logs sharing its trace id — metric → trace → log in clicks, no debugger.
- (C) Automatic alerting on every request.
- (D) Lower telemetry cost.

---

## Answer key

- **Q1: (B).** This is the textbook BOLA (API1): the endpoint authenticates (valid token) but never authorizes the *object* (does the caller own this row). `RequireAuthorization()` proves identity, not ownership. It is the most common and most damaging API vulnerability. Citation: <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.
- **Q2: (B).** Return `404`. A `403` confirms the row exists, telling an attacker that id `X` is a real object in some tenant — an information leak. "Not yours" and "does not exist" should be indistinguishable to the caller. Citation: <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.
- **Q3: (B).** The EF Core global query filter (`HasQueryFilter`) is applied to *every* query against the entity, so a forgotten per-handler `Where` no longer leaks. One filter deletes a whole class of forgettable checks — the editing thesis. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/filters>.
- **Q4: (B).** Binding the body to the entity lets the client set server-owned fields (mass assignment, API3). The fix is a request DTO with only client-settable fields; the server sets `Grade`, `TenantId`, `CreatedAt` from the token and clock, never the body. Citation: <https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>.
- **Q5: (B).** MediatR's value is the pipeline behavior — a cross-cutting wrapper written once. If no behavior would apply (a health probe, a trivial lookup), the request/handler/registration/`Send` are four artifacts replacing one method call; delete the mediator. Citation: <https://github.com/jbogard/MediatR/wiki/Behaviors>.
- **Q6: (B).** A map that hides fields (a security boundary) and computes values (carries logic) is the worst AutoMapper candidate — convention *includes by default*, the wrong default for a DTO that must exclude `TenantId`, and logic-in-configuration is hard to debug. A hand-written `ToDto()` is clearer and auditable. (A) is the *good* candidate. Citation: <https://docs.automapper.org/en/stable/Configuration-validation.html>.
- **Q7: (B).** Timeout innermost bounds each attempt; retry wraps it to re-try the bounded attempt; the circuit breaker outermost stops hammering a dead downstream. Reversed, the timeout would bound the whole retry sequence and the breaker would trip per-attempt — wrong semantics. Citation: <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>.
- **Q8: (B).** The outbox decouples the user's success from a downstream's availability. The domain row and the outbox row commit in one transaction; the drainer broadcasts later, Polly-wrapped, retrying without touching the request. A slow downstream degrades a feature instead of failing the submission. Citation: <https://learn.microsoft.com/en-us/dotnet/core/extensions/workers>.
- **Q9: (B).** The global tenant query filter reads the current request's tenant; a background drainer has no request, so the filter would resolve to an empty/wrong tenant and hide every message — the outbox would silently never drain. `IgnoreQueryFilters()` is the deliberate, documented escape hatch for cross-tenant background work. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/filters>.
- **Q10: (B).** An exemplar attaches a sample `trace_id` to a metric data point (recorded automatically when the histogram `Record` runs inside an active span). In Grafana, clicking the spike jumps to that trace in Tempo, and trace-to-logs reaches Loki — the metric → trace → log path that lets you debug an incident without a debugger. Citation: <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars> and <https://grafana.com/docs/grafana/latest/fundamentals/exemplars/>.

## Self-assessment

- 9-10: you can ship the harden milestone without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact pages.
- 5-6: re-read all three lecture notes and redo the exercises, paying particular attention to the BOLA/query-filter and exemplar sections.
- 0-4: rewind to Lecture 1 and read all three lecture notes carefully. The milestone assembles every pattern the quiz tests; it will not make sense without the conceptual foundation — and remember the harden contract: a privileged path without a rejection test is not hardened.
