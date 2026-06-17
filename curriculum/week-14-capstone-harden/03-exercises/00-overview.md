# Week 14 — Exercises

This is the capstone harden week, and these four exercises are where "it works" becomes "I can trust it and operate it." Each one takes the Polyglot Workshop you built in Week 13 and hardens one face of it: Exercise 1 closes a Broken Object Level Authorization hole and *proves* the deny path with an integration test (security); Exercise 2 collapses three copy-pasted write endpoints into one MediatR pipeline so validation, authorization, and transaction scoping run once in order (resilience, and a net-negative diff); Exercise 3 swaps hand-written DTO constructions for AutoMapper `ProjectTo` so the projection is pushed into the SQL `SELECT` (performance, plus a BOPLA win); and Exercise 4 wires the OpenTelemetry SDK and correlates one request across traces, logs, and metrics in a local Grafana + Loki + Tempo stack (observability). The theme that runs through all four is the lecture's: hardening is editing, so the right answer usually *removes* more than it adds.

## How to Run an Exercise

Each exercise is a single annotated `.cs` file that extends your Week 13 capstone solution. The file is organized into PART sections — some are reference (handlers, behaviors, profiles, `Program.cs` registration shown in comments), and one or two parts are the code you write (look for `<-- YOU WRITE THIS` and `TODO(you)` markers). The header comment of each file lists the exact project paths, the citations, and the commands. The shape is always:

1. Open the workshop solution from Week 13 and create the files the PART headers name (for example `src/Workshop.Api/Authorization/SubmissionOwnerHandler.cs` or `src/Workshop.Application/Behaviors/ValidationBehavior.cs`), pasting the reference parts and filling in the TODOs.

2. Wire the registration shown in the `Program.cs` PART (policy names, `AddScoped<IAuthorizationHandler, …>`, MediatR behavior order, `AddAutoMapper`, the OpenTelemetry builder), then build clean:

   ```bash
   dotnet build        # target: 0 warnings, 0 errors
   ```

3. Run the exercise's tests or walk, using the commands in the file's `COMMANDS` section. For example:

   ```bash
   # Exercise 1 — spins up Testcontainers Postgres + Keycloak
   dotnet test tests/Workshop.IntegrationTests \
       --filter "FullyQualifiedName~SubmissionBolaTests"
   ```

   Exercise 4 instead brings up the observability stack (`docker compose -f mini-project/observability/docker-compose.yml up -d`), runs the API against the collector, generates traffic, and has you do the correlated walk in Grafana. Docker must be running for Exercises 1 and 4.

4. Work the `CHECKLIST AFTER YOU RUN IT` block at the bottom of the file; the stretch goals there count toward the exercise if you finish the core work with time left.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|-----------:|----------:|
| 1 | [exercise-01-bola-deny-path-integration-test.cs](./exercise-01-bola-deny-path-integration-test.cs) | Closing a BOLA hole with resource-based authorization (a `SubmissionOwnerRequirement` + handler), wiring `IAuthorizationService.AuthorizeAsync` into the endpoint, and writing the `WebApplicationFactory` + Testcontainers Keycloak integration test that proves owner-allow, non-owner-deny (404 not 403), anonymous-401, and instructor-moderation paths | Intermediate+ | 90 min |
| 2 | [exercise-02-mediatr-pipeline-behaviors.cs](./exercise-02-mediatr-pipeline-behaviors.cs) | Collapsing three near-identical write endpoints into one `SubmitExerciseCommand` with `ValidationBehavior` / `AuthorizationBehavior` / `TransactionBehavior` pipeline behaviors, the `ICommand` constraint that keeps queries out of transactions, the validate→authorize→transaction order, and proving the diff is net-negative | Intermediate+ | 75 min |
| 3 | [exercise-03-automapper-projection.cs](./exercise-03-automapper-projection.cs) | Replacing hand-written DTO constructions with a logic-free AutoMapper `Profile` and `ProjectTo`, proving via the EF Core SQL log that only DTO columns are selected, keeping the three logic-bearing mappings hand-written, and adding `AssertConfigurationIsValid()` as a test | Intermediate | 60 min |
| 4 | [exercise-04-otel-and-the-stack.cs](./exercise-04-otel-and-the-stack.cs) | Wiring the OpenTelemetry SDK (`ActivitySource`, a RED `Meter`, the OTLP exporter, Serilog span enrichment), bringing up the Grafana + Loki + Tempo + Prometheus stack, and correlating one request from a metric exemplar to its trace to its logs without leaving Grafana | Advanced | 90 min |

## Checking Your Work

Annotated reference solutions, the captured trace/log/metric output, and the load-bearing details (why 404 not 403, why the `ICommand` constraint, why `ProjectTo` must run before `ToListAsync`, why `service.name` must be spelled identically everywhere) live in [SOLUTIONS.md](./SOLUTIONS.md). Attempt every TODO yourself before opening it — the learning is in the struggle. When you compare against the reference, check that:

- The deny paths actually deny: Exercise 1's `Non_owner_cannot_read…` returns **404, not 200** (a green 200 means the resource-based check is not wired — recheck the policy name and the handler registration), and the response body never leaks `InternalNotes` or `LearnerEmail`.
- The work is **net-negative**: Exercise 2's `git diff --stat` shows more lines deleted than added, and Exercise 3's EF Core SQL log shows **only the DTO columns** in the `SELECT` (no `InternalNotes`, no `Content`).
- `dotnet build` and `dotnet test` are **green with 0 warnings**, every checkbox in each file's `CHECKLIST` block is satisfied, and for Exercise 4 you can click metric exemplar → trace → logs end to end and no token or PII appears in any span tag or log property.
