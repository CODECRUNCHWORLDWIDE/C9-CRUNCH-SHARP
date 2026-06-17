# Week 14 — Homework

Six practice problems that consolidate the week's hardening material. They are sized to ~45 minutes each. Do them after the lectures and the exercises; do them before (and in service of) the capstone milestone. Cite the URLs you used while solving each one in the commit message of your homework branch. Every deliverable is a small, dated artifact you could show a reviewer.

## Problem 1 — The threat-model walk

Write the STRIDE-per-boundary table for **one** of the workshop's three boundaries (Minimal API, gRPC, or SignalR hub) in full. For each of the six STRIDE categories, name the concrete threat at that boundary, the .NET mitigation, the OWASP API item it maps to (or "—" if none), and the test that proves it. Then write 150 words on which STRIDE category was hardest to reason about for your chosen boundary and why (for the SignalR hub it is usually **I**nformation disclosure, because the `access_token` rides in a URL that gets logged).

Cite the OWASP Threat Modeling cheat sheet at <https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html> and the OWASP API Top 10 at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>.

Deliverable: `homework/01-threat-model-walk.md`.

## Problem 2 — The BOLA inventory

Enumerate **every** endpoint and gRPC method in your workshop that names an object by id (the BOLA candidates). For each, state: the object type, whether a resource-based check exists today, the policy/handler that enforces it, and the deny-path test that proves it. This inventory *is* the work list for the milestone — a row without a test is an open hole.

Then pick the deepest object in your graph (in the default domain, a submission: submission → lesson → instructor → tenant) and explain in 150 words why deep object graphs make BOLA harder to close (every level is a place to leak, and "the caller owns the submission" is not the same as "the caller may see the lesson it belongs to").

Cite <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/> and the resource-based authz chapter at <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>.

Deliverable: `homework/02-bola-inventory.md` with the table and the essay.

## Problem 3 — MediatR: earns-its-keep or ceremony?

Take three features from your workshop. For each, decide whether it should be a MediatR request/handler pair or a plain Minimal API handler, using Lecture 2's decision rule (does it benefit from a pipeline behavior you have?). Write the decision and a one-line justification for each. Then take one feature you decided *should* use MediatR and show the before/after diff stat — prove the harden change is net-negative in lines.

Cite the behaviors wiki at <https://github.com/jbogard/MediatR/wiki/Behaviors> and "you probably don't need MediatR" at <https://www.jimmybogard.com/you-probably-dont-need-mediatr/>.

Deliverable: `homework/03-mediatr-decisions.md` with the three decisions and the diff stat.

## Problem 4 — AutoMapper: the three you must not give it

In your workshop, find three mappings that should *not* be AutoMapper (a computed/flattened one, a role-conditional one, and an inbound one that sets a claim-derived field like `TenantId`). For each, write the hand-written mapping method and a unit test, and explain in one sentence why AutoMapper is the wrong tool for it. Then add `AssertConfigurationIsValid()` for the maps that *do* belong to AutoMapper, and demonstrate it failing by adding an unmapped DTO property, then reverting.

Cite the queryable-extensions docs at <https://docs.automapper.org/en/stable/Queryable-Extensions.html> and the config-validation docs at <https://docs.automapper.org/en/stable/Configuration-validation.html>.

Deliverable: `homework/04-automapper-boundaries.md` with the three methods, their tests, and the failing-then-passing config-validation evidence.

## Problem 5 — The correlated-incident walkthrough, written up

With the Grafana stack up (Exercise 4) and traffic flowing, perform the five-step correlated walk from Lecture 3 §7 on a real (or deliberately introduced) slow path: metric spike → exemplar → trace → logs by `TraceId` → named cause. Capture the trace id, the PromQL that found the spike, the offending `db.statement` (if any), and the corroborating Loki line. Then write 200 words on why each signal alone was insufficient — what the metric told you, what only the trace added, and what only the log confirmed.

Cite the observability guide at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel> and the Tempo trace-to-logs docs at <https://grafana.com/docs/grafana/latest/datasources/tempo/configure-tempo-data-source/#trace-to-logs>.

Deliverable: `homework/05-correlated-walk.md` with the captured ids/queries/logs and the essay. Include a screenshot of the Tempo flame graph.

## Problem 6 — Rate-limit design for three surfaces

Design the rate-limiting policy for three surfaces of your workshop: (A) the read-heavy analytics endpoint, (B) the write endpoint that creates submissions, and (C) the "submit on behalf" sensitive business flow (OWASP API6). For each, pick an algorithm (fixed window / sliding window / token bucket / concurrency), a partition key (per-user / per-tenant / per-IP), and limits, and justify the choice. Then explain the **thundering-herd** problem when many clients retry after a 429 at once, and how `Retry-After` plus jitter mitigates it.

Cite the rate-limiting chapter at <https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit> and the API6 page at <https://owasp.org/API-Security/editions/2023/en/0xa6-unrestricted-access-to-sensitive-business-flows/>.

Deliverable: `homework/06-rate-limit-design.md` with the three designs and the thundering-herd analysis.

## Submission

Push the six deliverables on a branch named `week14-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link to each of the six files and include a 100-word summary of what you hardened and what surprised you.

## Grading rubric

Each problem is scored out of 5, for 30 points total. A passing homework is 21/30 (70%).

| Score | Meaning |
|------:|---------|
| 5 | Correct, complete, and every non-trivial claim is cited. The artifact would survive a real code review. For test-bearing problems, the tests run green and the deny paths actually fail when the control is removed. |
| 4 | Correct and complete, but a citation is missing or one edge case is unaddressed (e.g. the cross-tenant instructor in the BOLA inventory). |
| 3 | The core idea is right but the execution is thin — a table without the deny-path tests, a MediatR decision without the diff stat, a correlated walk without a real trace id. |
| 2 | Partially correct, with a conceptual error (e.g. returns 403 where the BOLA guidance wants 404, or computes a p99 from logs). |
| 1 | Attempted but largely incorrect or uncited. |
| 0 | Not submitted, or no evidence the work was actually run. |

The single most common review comment, as every week: **"where is your citation for this claim"** — preempt it by linking the OWASP page or Microsoft Learn URL for every non-trivial assertion. The second most common, this week specifically: **"show me the deny-path test"** — an authorization claim without a test that proves the deny path is not a closed control, it is a hope.

Cited sources this homework draws from: the OWASP API Security Top 10 (2023) and its per-item pages; the OWASP Threat Modeling and Authorization cheat sheets; the ASP.NET Core resource-based authorization, policy, and rate-limiting docs; the MediatR behaviors wiki and "you probably don't need MediatR"; the AutoMapper queryable-extensions and config-validation docs; and the .NET observability + Grafana Tempo/Loki documentation.
