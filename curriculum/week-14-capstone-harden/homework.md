# Week 14 — Homework

Six practice problems that consolidate the week's material against your `PolyglotWorkshop` repo. They are sized to ~45 minutes each. Do them after the lectures and exercises; do them before (or alongside) the harden milestone. Cite the URLs you used while solving each one in the commit message of your homework branch.

## Problem 1 — The OWASP API Top 10 audit

Walk your `Workshop.Api` against the OWASP API Security Top 10 (2023). For each of the ten entries, write one of: (a) "mitigated, here is the line and the test," (b) "vulnerable, here is the proof," or (c) "not applicable, because." Be specific — "we use JWT" is not an answer to API2; the answer names which `Validate*` flags are on and what `ClockSkew` is set to.

Then pick the one entry you scored worst on and write the fix as a diff plus the test that proves it.

Cite the catalogue at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/> and the specific entry you fixed.

Deliverable: `homework/01-owasp-audit.md`.

## Problem 2 — The tenant-isolation proof

Add the EF Core global query filter to `Submission`, `Review`, and `Enrollment` if you have not already. Then prove, in three states, that it holds:

1. **Per-handler only.** With the `Where(s => s.TenantId == ...)` in the handler but no global filter, show a cross-tenant read returns `404`.
2. **Global filter only.** Remove the per-handler `Where`; show the cross-tenant read *still* returns `404` because the filter is now load-bearing.
3. **Filter bypassed.** Call `IgnoreQueryFilters()` and show the cross-tenant row *is* visible — documenting exactly what the filter protects and why only the outbox drainer is allowed to bypass it.

Cite the query-filter docs at <https://learn.microsoft.com/en-us/ef/core/querying/filters> and OWASP API1 at <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.

Deliverable: `homework/02-tenant-isolation.md` with the three states and their outputs.

## Problem 3 — MediatR: keep or delete

List every request path in `Workshop.Api`. For each, answer the test from Lecture 2: *would a pipeline behavior ever apply?* Classify each as "route through MediatR" (a behavior applies) or "direct call" (none does). Then, for two paths you currently route through MediatR that should not be, delete the command/handler/registration and replace them with direct calls; for one direct path that should be a command, add it. Report the artifact count before and after (request types, handlers, registrations, `Send` calls) to make the editing thesis concrete.

Cite the MediatR behaviors wiki at <https://github.com/jbogard/MediatR/wiki/Behaviors>.

Deliverable: `homework/03-mediatr-keep-or-delete.md` with the classification table and the before/after artifact count.

## Problem 4 — AutoMapper: the one you keep

Inventory every DTO mapping in the service. For each, decide: AutoMapper profile or hand-written `ToDto()`/EF `Select`. Justify each with one of the three skip-reasons from Lecture 2 (carries logic, is a security boundary, defeats EF projection) or the keep-reason (wide and mechanically symmetric). Keep exactly one profile, validate it with `AssertConfigurationIsValid()`, and convert the rest. For the converted `Submission -> SubmissionDto`, confirm `TenantId` and `IsFlagged` are absent from the output and write the test that asserts it.

Cite AutoMapper configuration validation at <https://docs.automapper.org/en/stable/Configuration-validation.html> and OWASP API3 at <https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>.

Deliverable: `homework/04-automapper-one-you-keep.md` with the inventory, the kept profile, and the exclusion test.

## Problem 5 — Polly under fault injection

Point the `NotificationClient` at a fault-injecting stub you control (return `503` on the first N calls, then `200`). Reproduce three behaviors and document each:

1. **Retry recovers.** With `503` on the first two calls and `200` on the third, the publish succeeds; the trace shows two failed child spans then a successful one.
2. **Breaker opens.** With `503` for 20 consecutive calls, confirm the breaker opens and subsequent calls throw `BrokenCircuitException` in sub-millisecond time (fail fast), visible as a latency cliff.
3. **Breaker half-opens and recovers.** After the break duration, the breaker probes; bring the stub up and confirm it closes again.

Then explain the strategy order (timeout → retry → breaker) and what breaks if you reverse it.

Cite the HTTP resilience docs at <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience> and Polly at <https://github.com/App-vNext/Polly>.

Deliverable: `homework/05-polly-fault-injection.md` with the three behaviors and the order explanation.

## Problem 6 — Benchmark the analytics hot path

Add a `Workshop.Benchmarks` project. Benchmark the Dapper analytics query (`GetProgressAsync`) against a seeded dataset in two states: with and without the `(tenant_id, exercise_id)` index. Report the `[MemoryDiagnoser]` output — mean, allocations, and the ratio between the two states. Then commit a threshold and a CI step that fails if the indexed mean regresses past it. Explain why a benchmark you do not *gate on* is a report nobody reads, and a benchmark you gate on is a test.

Cite BenchmarkDotNet at <https://github.com/dotnet/BenchmarkDotNet> and the Dapper repo at <https://github.com/DapperLib/Dapper>.

Deliverable: `homework/06-benchmark-gate.md` with the BenchmarkDotNet summary, the threshold, and the CI step.

## Submission

Push the six deliverables on a branch named `week14-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link to each of the six files and include a 100-word summary of what you learned about the difference between code that works and code that is hardened.

The teaching staff reviews homework PRs within 5 business days. Reviews focus on whether you have read the citations and whether your reasoning holds together, not on perfect grammar. The single most common review comment is "where is the test that proves this" — preempt it: every claim that a path is hardened should point at the integration test that proves the unauthorized case is rejected.

Cited Microsoft Learn pages this homework draws from: <https://learn.microsoft.com/en-us/ef/core/querying/filters>, <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>, <https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience>, <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. External: OWASP API Top 10 at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>, MediatR at <https://github.com/jbogard/MediatR>, AutoMapper at <https://github.com/AutoMapper/AutoMapper>, Polly at <https://github.com/App-vNext/Polly>, and BenchmarkDotNet at <https://github.com/dotnet/BenchmarkDotNet>.
