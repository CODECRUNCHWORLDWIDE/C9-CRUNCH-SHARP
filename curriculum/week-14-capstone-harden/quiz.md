# Week 14 — Quiz

Ten multiple-choice questions covering threat modeling, the OWASP API Security Top 10 in .NET, resource-based and function-level authorization, MediatR pipeline behaviors, AutoMapper projection, and the three observability signals. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — The dominant OWASP API category

Looking at the OWASP API Security Top 10 (2023), what do API1 (BOLA), API3 (BOPLA), and API5 (BFLA) have in common, and what is the primary .NET mitigation for the group?

- (A) They are all input-validation bugs; the mitigation is FluentValidation.
- (B) They are all authorization bugs; the mitigation is consistent, resource-based and policy-based authorization applied at the boundary before the handler touches data.
- (C) They are all transport-encryption bugs; the mitigation is HTTPS redirection.
- (D) They are all rate-limiting bugs; the mitigation is the rate-limiting middleware.

## Question 2 — BOLA and the 403-vs-404 choice

An endpoint `GET /api/submissions/{id}` resolves a submission that exists but belongs to a different learner than the caller. For a resource whose very *existence* should be secret from this caller, the idiomatic response is:

- (A) 403 Forbidden, always — it is the most honest status.
- (B) 200 OK with the object redacted, so the caller learns nothing.
- (C) 404 Not Found — a 403 would confirm that the id is valid and belongs to someone, which is itself an information disclosure.
- (D) 500 Internal Server Error, to avoid revealing anything about the request.

## Question 3 — Why the authorization handler does not call `Fail()`

`SubmissionOwnerHandler` calls `context.Succeed(requirement)` on the allow path and just returns on the deny path. Why does it deliberately avoid `context.Fail()`?

- (A) `context.Fail()` does not exist on `AuthorizationHandlerContext`.
- (B) Calling `Fail()` vetoes the requirement regardless of other handlers; leaving it unmet is a soft failure that lets another handler (e.g. instructor moderation) still succeed.
- (C) `Fail()` throws an exception that crashes the request pipeline.
- (D) `Fail()` is only valid in policy providers, not in handlers.

## Question 4 — The defense-in-depth layer for tenant isolation

In addition to the resource-based check, the workshop adds an EF Core global query filter `HasQueryFilter(s => s.TenantId == _tenantId)` on tenant-scoped entities. Its role is:

- (A) The primary authorization control; the resource-based check is redundant.
- (B) Defense in depth: if an endpoint *forgets* the resource-based check, the filter still prevents cross-tenant data from being returned, bounding the blast radius to within-tenant.
- (C) A performance optimization only; it has no security value.
- (D) A replacement for `WHERE` clauses, so queries can omit all filtering.

## Question 5 — When MediatR earns its keep

According to Lecture 2's decision rule, you should introduce a MediatR request/handler pair for a feature:

- (A) Always — every endpoint should be a MediatR request for consistency.
- (B) Only if the feature benefits from at least one pipeline behavior you have (validation, authorization, transaction/outbox); otherwise a Minimal API handler calling a service is simpler.
- (C) Never — MediatR is deprecated in .NET 9.
- (D) Only for queries, never for commands.

## Question 6 — Pipeline behavior ordering

The workshop registers `ValidationBehavior`, `AuthorizationBehavior`, and `TransactionBehavior`. Why is this order (and not authorize → validate → transaction) correct?

- (A) The order does not matter; MediatR runs behaviors in parallel.
- (B) Validate first (reject malformed input before checking ownership of an object that cannot exist), authorize second (reject unauthorized requests before opening a transaction), transaction last (only for requests you will actually run).
- (C) Transaction must run first so the database is locked before validation.
- (D) Authorization must run first because it is the most expensive.

## Question 7 — The `where TRequest : ICommand` constraint on the transaction behavior

`TransactionBehavior<TRequest, TResponse>` is constrained `where TRequest : ICommand`. The effect is:

- (A) The behavior throws if a query is sent through the mediator.
- (B) MediatR only constructs the behavior for requests implementing `ICommand`, so queries flow through without opening a (pointless) transaction.
- (C) All requests, including queries, are wrapped in a transaction.
- (D) The constraint is cosmetic and has no runtime effect.

## Question 8 — AutoMapper `ProjectTo`

What does `mapper.ProjectTo<SubmissionDto>(db.Submissions.Where(...))` do that `mapper.Map<List<SubmissionDto>>(db.Submissions.Where(...).ToList())` does not?

- (A) Nothing; the two are equivalent.
- (B) `ProjectTo` pushes the projection into the SQL `SELECT`, so the database only reads the DTO's columns; the `Map`-after-`ToList` version materializes the full entity (including `InternalNotes`) first, then drops columns in memory.
- (C) `ProjectTo` runs on a background thread.
- (D) `ProjectTo` validates the mapping configuration at runtime; `Map` does not.

## Question 9 — Choosing the right observability signal

You need to know the p99 latency of `POST /api/submissions` over the last hour, across all requests. Which signal answers this, and which signal is the wrong tool?

- (A) Logs answer it; traces are the wrong tool.
- (B) A metric (histogram) answers it; grepping logs to compute a p99 is the wrong tool.
- (C) A trace answers it; metrics are the wrong tool.
- (D) All three answer it equally well.

## Question 10 — Exemplars

In the Grafana stack, an exemplar lets you click a point on a latency *metric* panel and jump to the *trace* that caused it. Which of these is NOT part of the chain that makes this work?

- (A) The OpenTelemetry SDK attaches the `TraceId` to a histogram data point while an `Activity` is current.
- (B) The collector's Prometheus exporter has `enable_open_metrics: true` so exemplars survive export.
- (C) Prometheus runs with `--enable-feature=exemplar-storage`.
- (D) The application manually writes the `TraceId` into the metric name string before recording it.

---

## Answer key

- **Q1: (B).** Four of the top five OWASP API items (API1 BOLA, API2 Broken Auth, API3 BOPLA, API5 BFLA) are authorization/authentication problems — "the caller asked for something they should not have, and you gave it to them." For a .NET engineer the group mitigation is consistent authorization applied at the boundary: resource-based checks for object access (BOLA), DTO allow-lists for property exposure (BOPLA), policy gates for functions (BFLA). Citation: <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>.
- **Q2: (C).** A 403 confirms the id is valid and belongs to someone — an attacker iterating ids learns which ids exist without ever reading an object. For resources whose existence is itself sensitive, the OWASP BOLA guidance prefers 404. Use 403 only when the resource's existence is public. Citation: <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.
- **Q3: (B).** Authorization handlers compose with OR-semantics: a requirement is satisfied if *any* handler succeeds. `context.Fail()` vetoes the requirement regardless of other handlers — correct for an explicit ban, wrong for an ownership check where an instructor-moderation handler should still be able to grant access. Leaving the requirement unmet is the soft, composable default. Citation: <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies#why-would-i-want-multiple-handlers-for-a-requirement>.
- **Q4: (B).** The resource-based check is the primary control; the global query filter is defense in depth. When an engineer adds an endpoint and forgets the check, the filter ensures the blast radius is "leaks within a tenant," not "leaks across tenants." Both layers, not one. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/filters>.
- **Q5: (B).** MediatR's value is the pipeline, not the dispatch. Introduce a request/handler pair only when the feature benefits from a behavior you have. A one-line endpoint with no cross-cutting concern is simpler as a Minimal API handler. The author makes this point himself. Citation: <https://www.jimmybogard.com/you-probably-dont-need-mediatr/>.
- **Q6: (B).** Validate → authorize → transaction. Reject garbage before checking ownership; check permission before opening a transaction; open the transaction only for requests you will run. MediatR runs behaviors in registration order, so registration order *is* pipeline order. Citation: <https://github.com/jbogard/MediatR/wiki/Behaviors>.
- **Q7: (B).** MediatR only constructs a behavior for requests satisfying its generic constraints. `where TRequest : ICommand` means the transaction behavior is built only for commands; queries (which do not implement `ICommand`) flow straight through and never open a transaction. That precision is the point — a behavior runs for exactly the requests that need it.
- **Q8: (B).** `ProjectTo` builds an `IQueryable<TDto>` whose SQL `SELECT` lists only the DTO's columns, so the database never reads the dropped columns (a BOPLA mitigation *and* a perf win). `Map`-after-`ToList` materializes the full entity first — every column off disk — then drops columns in memory. The fix when you see extra columns in the SQL is to project on the `IQueryable`, before `ToListAsync`. Citation: <https://docs.automapper.org/en/stable/Queryable-Extensions.html>.
- **Q9: (B).** A p99 over a window across all requests is an aggregate — a metric (a duration histogram) answers it directly. Grepping logs to compute a percentile is the classic wrong-tool mistake. Conversely, to understand why *one specific* request was slow, you use a trace, not the metrics dashboard. Citation: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>.
- **Q10: (D).** The `TraceId` is never written into the metric *name*. It is attached as an **exemplar** — a separate annotation on a histogram bucket — automatically by the SDK when an `Activity` is current. The real chain is (A) SDK attaches it → (B) collector exports with open-metrics → (C) Prometheus stores it with exemplar-storage → Grafana renders the clickable diamond. (D) describes a corruption of the metric, not how exemplars work. Citation: <https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars>.

## Self-assessment

- 9-10: you can ship this week's capstone milestone without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact source.
- 5-6: re-read the lecture notes end to end and redo the exercises.
- 0-4: rewind to Lecture 1. The milestone's auth surface will not close without the threat-modeling and authorization foundation.
