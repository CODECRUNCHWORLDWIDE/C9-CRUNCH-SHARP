# Week 13 — Homework

Six practice problems that consolidate the integration baseline. They are sized to ~45–60 minutes each. Do them after the lectures and the exercises; do them before (or alongside) the mini-project — several feed directly into it. Cite the URLs you used while solving each one in the commit message of your homework branch. The grading rubric is at the bottom.

## Problem 1 — The contract review

You are reviewing three pull requests against the capstone. For each, decide **accept** or **reject** and justify in two sentences, citing the relevant contract rule (Lecture 2 §8):

- **PR A** adds `public sealed class LessonViewModel { public string Title; public string Body; }` to the Blazor admin to bind the lesson grid.
- **PR B** adds `string moderator_id = 7;` to `SubmitRequest` so the admin can record who approved a submission.
- **PR C** moves the "hide rejected submissions from learners" rule into `ProtoMappings.ToProto(Submission)`.

Deliverable: `homework/01-contract-review.md` with the three decisions and justifications.

## Problem 2 — Unit vs integration: draw the line

For each of the following assertions about the capstone, decide whether it should be a **unit test** (mocked dependencies, no I/O) or an **integration test** (`WebApplicationFactory<T>` + Testcontainers), and justify in one sentence:

1. `Lesson.Create` throws `ArgumentException` for a blank title.
2. `ListPendingSubmissions` returns submissions ordered by `SubmittedAt`.
3. The `(Status, SubmittedAt)` index is actually used by the pending-queue query.
4. `ProtoMappings.ToProto(Lesson)` round-trips `CreatedAt` through `Timestamp`.
5. A token from the wrong issuer is rejected with `Unauthenticated`.
6. The `tenant` claim flows from the token into `WorkshopService.TenantOf`.

Deliverable: `homework/02-test-line.md` with the six classifications and one-sentence justifications. Cite <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests> and Kent Beck's Test Desiderata at <https://kentbeck.github.io/TestDesiderata/>.

## Problem 3 — The scope-cut ledger

Write the scope-cut ledger for your capstone (Lecture 1 §6). List at least **eight** things you are explicitly NOT building in Week 13, each tagged with where it goes (Week 14, Week 15, portfolio, or cut entirely) and a one-line reason. Then, for any three of them, state the test you would use to confirm the cut was correct — i.e., that the vertical slice is still green without it.

Deliverable: `homework/03-scope-cut-ledger.md`. The ledger feeds the mini-project's `BASELINE.md` and the Sunday retrospective.

## Problem 4 — Make `MigrateAsync` fail, then pass

Deliberately introduce a migration bug and prove the integration test catches what `EnsureCreated` would miss:

1. In a branch, hand-edit your `InitialCreate` migration to drop the `ix_submissions_status_time` index but leave the model expecting it.
2. Show that an `EnsureCreated`-based test stays green (because it builds from the model, not the migration).
3. Show that the `MigrateAsync`-based integration test — plus a query that *requires* the index for acceptable performance, or a model-vs-migration consistency check (`dotnet ef migrations has-pending-model-changes`) — catches the drift.
4. Revert.

Deliverable: `homework/04-migration-drift.md` with the two test outcomes and an explanation of why `MigrateAsync` is the honest choice (Lecture 3 §4). Cite <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>.

## Problem 5 — Trace one request, read it

Using the Challenge 2 setup (the OTLP collector with the debug exporter), capture **one** trace for one `CreateLesson` call and answer:

1. How many spans are in the trace, and what is the parent-child shape?
2. What is the shared trace id, and where does it appear in the matching Serilog log line?
3. What single fact does the trace tell you instantly that the log line alone does not?
4. If you remove the browser `traceparent`, where does the trace root move, and why?

Deliverable: `homework/05-trace-read.md` with the captured trace (debug-exporter output or a screenshot) and the four answers. Cite <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>.

## Problem 6 — Green in CI, for real

Push the capstone (or a minimal two-project slice of it) to a GitHub repo and make the Actions workflow green:

1. Adapt the starter `ci.yml` so it restores, builds, and runs the integration suite with Testcontainers inside the runner.
2. Get the run green. Link the run.
3. Then break it on purpose: add a field to `workshop.proto` that the MAUI client uses but the Blazor client does not update, and show the MAUI-head build step (or the Blazor build) failing — proving the contract is load-bearing across all three clients in CI.
4. Fix it and get green again.

Deliverable: `homework/06-green-ci.md` with the two run links (red and green) and a paragraph on what the red run proved about the contract being the source of truth. Cite <https://docs.github.com/actions> and <https://dotnet.testcontainers.org/>.

---

## Grading rubric (100 points)

| Problem | Points | What earns full marks |
|---------|-------:|------------------------|
| 1 — Contract review | 15 | All three decisions correct (reject A, reject B, reject C) with the right rule cited for each |
| 2 — Unit vs integration | 15 | All six correctly classified (1,4 unit; 2,3,5,6 integration) with sound one-line reasons |
| 3 — Scope-cut ledger | 15 | ≥8 cuts, each correctly placed (Week 14/15/portfolio/cut) with reasons; 3 confirmation tests stated |
| 4 — Migration drift | 18 | Both outcomes demonstrated; clear explanation of why `MigrateAsync` catches what `EnsureCreated` misses |
| 5 — Trace one request | 17 | A real captured trace; correct span tree; trace-id-in-log shown; the four questions answered with insight |
| 6 — Green in CI | 20 | A linked green run; the deliberate red run proving cross-client contract enforcement; green again after fix |

Partial credit per problem. **Problems 4, 5, and 6 require real artifacts** — a test outcome, a captured trace, a linked Actions run — not prose describing what would happen. A homework that asserts "the test would catch it" without the run earns no more than half the problem's points. The integration baseline is a milestone you *demonstrate*, not one you *describe* — the homework practices exactly that discipline.

> **Submission.** One branch, `homework/week-13/<your-handle>`, with the six markdown files under `homework/`, the captured artifacts (trace output, Actions links), and a commit message per problem citing the URLs you used. The peer reviewer checks that the contract-review decisions and the unit/integration classifications match the answer reasoning, and that Problems 4–6 carry real artifacts.
