# Week 13 — Homework

Six practice problems that consolidate the build-week material. They are sized to ~45 minutes each. Do them after the lectures and the exercises; do them before (or alongside) the capstone milestone — several feed directly into it. Cite the URLs you used while solving each one in the commit message of your homework branch.

## Problem 1 — The contract design review

Take the `workshop.proto` from Lecture 1 and write a one-page design review of it as if a teammate had submitted it for PR. For each of these, give a verdict (good / change it) and a one-line reason:

1. Every enum starts at `0` with an `UNSPECIFIED` member.
2. IDs are `string`, not a custom type.
3. Timestamps use `google.protobuf.Timestamp`.
4. The whole domain is one `Workshop` service rather than several.
5. The request/response envelopes (`EnrollRequest`/`Enrollment`) rather than bare scalars.

Then propose one improvement you would make for Week 14 (hint: think about what `Enroll` needs once auth is real — does the request still omit the learner id?).

Cite the proto3 guide at <https://protobuf.dev/programming-guides/proto3/> and the gRPC versioning guidance at <https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning>.

Deliverable: `homework/01-contract-review.md`.

## Problem 2 — Generation, inspected

Build `Workshop.Contracts` and find the generated code in `obj/`. Without copying it wholesale, answer:

1. What two files does `protoc` + the gRPC plugin emit, and what is in each?
2. What does `GrpcServices="Both"` produce that `"Server"` alone would not?
3. Show the generated signature of the `WorkshopClient.EnrollAsync` method and explain what `AsyncUnaryCall<Enrollment>` is and why you `await` it.
4. Confirm the generated files are gitignored and explain why committing them is a mistake.

Cite the gRPC .NET basics at <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics> and the `Grpc.Tools` integration at <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics#generated-c-assets>.

Deliverable: `homework/02-generation.md`.

## Problem 3 — Read the migration SQL

Generate the `InitialCreate` migration for the `Lesson` + `Enrollment` model and run `dotnet ef migrations script`. Then answer, citing the SQL:

1. What column type did `DateTimeOffset` map to under Npgsql, and why does `timestamptz` matter for a globally-distributed classroom?
2. What does the `(lesson_id, learner_id)` unique index enforce as a business rule, and what happens at the database if two concurrent requests both try to enroll the same learner?
3. What does `ON DELETE CASCADE` on the FK do, and name one case where you would *not* want a cascade.
4. Why is the migration checked into git rather than treated as a build artifact?

Cite the Npgsql provider docs at <https://learn.microsoft.com/en-us/ef/core/providers/npgsql> and the migrations docs at <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>.

Deliverable: `homework/03-migration-sql.md` with the captured SQL and the four answers.

## Problem 4 — The contract↔entity boundary

The lecture keeps the generated contract messages strictly separate from the EF Core entities and maps between them in one place. Reproduce and justify this:

1. List three concrete differences between the contract `Enrollment` (wire) and the entity `Enrollment` (persistence) — types, nullability, navigation properties.
2. Write the `ToContract(Enrollment entity)` method and a `FromContract` if you need one for `CreateLesson`.
3. Describe the failure mode if you instead persisted the generated message directly (think: enum-as-int, string GUIDs, no navigation properties, `Timestamp` vs `timestamptz`).
4. Give the one-sentence rule for where mapping is allowed to happen.

Cite the EF Core modeling docs at <https://learn.microsoft.com/en-us/ef/core/modeling/> and the well-known-types reference at <https://protobuf.dev/reference/protobuf/google.protobuf/#timestamp>.

Deliverable: `homework/04-mapping-boundary.md`.

## Problem 5 — Unit vs integration, and what the milestone needs

Write both kinds of test for the enroll slice and contrast them:

1. A **unit test** of `ToContract` (or of a small pure helper you extract) — no database, no host.
2. An **integration test** of `Enroll` via `WebApplicationFactory<Program>` + Testcontainers — the real host, a real PostgreSQL.

Then answer: which one catches a wrong `timestamptz` mapping? Which catches a unique-index violation? Which runs in milliseconds and which in seconds, and why? Which kind does the build milestone *require*, and why is that the right call for de-risking the architecture this week?

Cite the integration-test docs at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, Testcontainers for .NET at <https://dotnet.testcontainers.org/>, and the xUnit getting-started at <https://xunit.net/>.

Deliverable: `homework/05-unit-vs-integration.md` with both test bodies and the contrast.

## Problem 6 — The scope-cut memo

You are the tech lead. The full Polyglot Workshop spec lists Keycloak OIDC, SignalR presence, an outbox, Polly, Dapper analytics, BenchmarkDotNet, three finished clients, and a deploy pipeline. The build milestone ships only the contract, the service + data layer, the first client, and a green baseline. Write the memo that justifies the cut:

1. List everything in the full spec.
2. Mark each item **in Milestone 1**, **deferred to Week 14 (harden)**, or **deferred to Week 15 (deploy)**.
3. For each deferral, give the one-line reason it is safe to defer (what does the baseline *not* need it to be true?).
4. State the single risk the build milestone exists to retire, and why shipping the enroll slice green retires it.

Cite the minimal-API incremental tutorial at <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api> and the vertical-slice writing at <https://www.jimmybogard.com/vertical-slice-architecture/>.

Deliverable: `homework/06-scope-memo.md` (this is the seed of your milestone `SCOPE.md`).

## Submission

Push the six deliverables on a branch named `week13-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link to each of the six files and include a 100-word summary of what you learned about driving a build from a contract rather than from a UI.

The teaching staff reviews homework PRs within 5 business days. Reviews focus on whether you have read the citations and whether your reasoning holds together, not on perfect grammar. The single most common review comment is "where is your citation for this claim" — preempt it by linking the Microsoft Learn URL or source repo for every non-trivial assertion.

Cited Microsoft Learn pages this homework draws from: <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>, <https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning>, <https://learn.microsoft.com/en-us/ef/core/providers/npgsql>, <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>, <https://learn.microsoft.com/en-us/ef/core/modeling/>, <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, <https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api>. External: the Protocol Buffers guide at <https://protobuf.dev/programming-guides/proto3/>, Testcontainers for .NET at <https://dotnet.testcontainers.org/>, and the vertical-slice writing at <https://www.jimmybogard.com/vertical-slice-architecture/>.
