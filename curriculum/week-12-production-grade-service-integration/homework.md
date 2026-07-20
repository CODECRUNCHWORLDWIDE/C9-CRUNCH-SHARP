# Week 12 — Homework

Six practice problems that consolidate the week's material. They are sized to ~45 minutes each. Do them after the lectures and the exercises; do them before the mini-project. Cite the URLs you used while solving each one in the commit message of your homework branch.

## Problem 1 — The composition audit

Take the composed ProjectHub host from Exercises 1–3 and write a one-page audit of its cross-cutting wiring. For each of the four cross-cutting concerns — authentication, logging, telemetry, persistence — answer:

1. Where is it configured (which `Add*` extension, which file)?
2. Which protocol surfaces consume it (REST, gRPC, SignalR), and is the configuration shared or duplicated?
3. What would break if it were configured per-surface instead of once?

Then find one place in the host where a cross-cutting concern is *not* yet shared (or invent a plausible one — e.g. JSON serializer options) and describe how you would unify it.

Cite the host-configuration chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host> and the DI chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>.

Deliverable: `homework/01-composition-audit.md`.

## Problem 2 — Middleware order, broken four ways

Start from a working `Program.cs`. Produce four broken variants, each moving exactly one middleware call to the wrong place, and document the observable symptom of each:

- `UseAuthentication()` after the endpoint mapping.
- `UseAuthorization()` before `UseAuthentication()`.
- `UseRouting()` omitted entirely (or after `UseEndpoints`-style mapping).
- `UseSerilogRequestLogging()` after the endpoints (so it never sees the response).

For each, write the exact request you issued, the status code or log behavior you observed, and the one-line reason. Then restore the correct order and note why each line sits where it does.

Cite the middleware chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/> and the Serilog request-logging docs at <https://github.com/serilog/serilog-aspnetcore>.

Deliverable: `homework/02-middleware-order.md` with the four broken variants and their symptoms.

## Problem 3 — Structured-log query design

You are handed a directory of compact-JSON Serilog output from a day of ProjectHub traffic (generate it by running Exercise 2 under load, or use the sample in `homework/data/`). Using `jq` only — no grep over the rendered message — answer these five operational questions:

1. How many `POST /api/projects` requests returned `201` vs a `4xx`?
2. What is the p95 `Elapsed` (request duration) across all requests, from the `UseSerilogRequestLogging` lines?
3. Which `OrgId` produced the most requests?
4. List the distinct `TraceId`s that produced at least one `Error`-level line.
5. For one of those error traces, reconstruct the full request by extracting every line with that `TraceId` in timestamp order.

Write the `jq` expression for each and a one-sentence interpretation of the result.

Cite the compact-JSON format at <https://github.com/serilog/serilog-formatting-compact> and the message-template docs at <https://github.com/serilog/serilog>.

Deliverable: `homework/03-log-queries.md` with the five `jq` expressions and their outputs.

## Problem 4 — Read a trace by hand

Run Exercise 2 (or the mini-project) with `OpenTelemetry__Exporter=Console`. Issue one `POST /api/projects` and capture the full stdout span dump. Then, without using a viewer, draw the trace tree by hand from the `SpanId`/`ParentSpanId` links:

1. Identify the root span (the one with no `ParentSpanId`).
2. List every child and its parent, building the tree.
3. For each span, name its `ActivitySourceName`, `Kind`, and `Duration`.
4. Identify which spans came from framework instrumentation and which from application `ActivitySource.StartActivity` calls.
5. Compute the "self time" of the root span (its duration minus the time accounted for by its children) and explain what that self time represents.

Cite the `Activity`/`ActivitySource` walkthrough at <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs> and the semantic conventions at <https://opentelemetry.io/docs/specs/semconv/>.

Deliverable: `homework/04-trace-by-hand.md` with the captured dump, the hand-drawn ASCII tree, and the self-time calculation.

## Problem 5 — The scoping trap, reproduced and explained

Reproduce the `DbContext` scoping trap from Lecture 1 and Exercise 3 in three states, and document each:

1. **The crash.** Inject `ProjectHubDbContext` directly into a singleton service. Capture the exact `InvalidOperationException` and note whether it fired on startup (Development DI validation) or on the first request (Production lazy resolution). Explain the difference.
2. **The factory fix.** Switch to `IDbContextFactory<ProjectHubDbContext>` and `CreateDbContextAsync()`. Confirm the host boots and the path works. Note that factory-produced contexts are *not* pooled and explain the allocation consequence.
3. **The scope fix.** Use `IServiceScopeFactory.CreateScope()` and resolve the context from `scope.ServiceProvider`. Confirm it works and explain when you would prefer this over the factory (hint: when you need other scoped services alongside the context).

Cite the `DbContext` configuration chapter at <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/> and the DI service-lifetimes section at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection#service-lifetimes>.

Deliverable: `homework/05-scoping-trap.md` with the three states, their outputs, and the explanations.

## Problem 6 — Write one integration test, three ways

Pick one ProjectHub behavior — "a created project is visible to the owning org and invisible to a different org." Write three integration tests of it, escalating in fidelity:

1. **REST only.** `POST /api/projects` with org A's token, then `GET /api/projects` with org A's token (sees it) and org B's token (does not).
2. **REST + database assertion.** As above, plus a direct `ProjectHubDbContext` re-fetch via `_factory.Services.CreateScope()` confirming the row's `OrganizationId`.
3. **Cross-protocol.** `POST /api/projects` (REST) triggers a SignalR `ProjectCreated` event; a `HubConnection` for org A receives it and a `HubConnection` for org B does not (within a timeout).

All three run against a Testcontainers PostgreSQL via `WebApplicationFactory<Program>`. Report the run time of each and explain why fidelity costs time. Note which test you would keep if you could only keep one, and why.

Cite the integration-test docs at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, the Testcontainers .NET project at <https://github.com/testcontainers/testcontainers-dotnet>, and `xUnit`'s shared-context docs at <https://xunit.net/docs/shared-context>.

Deliverable: `homework/06-three-tests.md` with the three test bodies, their run times, and the "keep one" justification.

## Submission

Push the six deliverables on a branch named `week12-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link to each of the six files and include a 100-word summary of what you learned.

The teaching staff reviews homework PRs within 5 business days. Reviews focus on whether you have read the citations and whether your reasoning holds together, not on perfect grammar. The single most common review comment is "where is your citation for this claim" — preempt it by linking the Microsoft Learn URL or GitHub source for every non-trivial assertion.

Cited Microsoft Learn pages this homework draws from: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>, <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>, <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. External: the Serilog org at <https://github.com/serilog>, the OpenTelemetry .NET SDK at <https://github.com/open-telemetry/opentelemetry-dotnet>, and Testcontainers for .NET at <https://github.com/testcontainers/testcontainers-dotnet>.
