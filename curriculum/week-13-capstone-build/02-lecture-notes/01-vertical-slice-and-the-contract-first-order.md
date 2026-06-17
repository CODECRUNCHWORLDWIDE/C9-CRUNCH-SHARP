# Lecture 1 — Ship a Vertical Slice on Day One: The Contract-First Build Order

> **Time:** 2 hours. Take the vertical-slice philosophy in one sitting and the build-order mechanics in a second. **Prerequisites:** all of Phase 1–3 of C9 — this is a capstone lecture and it assumes you can already build a Minimal-API host, an EF Core model, a gRPC service, a MAUI client, and a Blazor app in isolation. **Citations:** the vertical-slice articulation at <https://www.jimmybogard.com/vertical-slice-architecture/>, the walking-skeleton idea at <https://wiki.c2.com/?WalkingSkeleton>, and the Minimal-APIs overview at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview>.

## 1. The capstone, and why the order you build it in is the whole lesson

The Polyglot Workshop is one system with three clients and one contract. The backend is an ASP.NET Core 9 service that exposes both a REST surface (Minimal APIs) and a gRPC surface mirroring the same domain, persists to PostgreSQL through EF Core with Dapper for analytics, authenticates against Keycloak, and emits structured logs and traces. The MAUI client is a learner's phone app that signs in via OIDC, consumes the gRPC contract, and works offline against SQLite. The Blazor admin is an instructor/moderator dashboard that consumes the *same* contract over gRPC-Web. The domain is a classroom: instructors create lessons, learners enroll and submit, an analytics surface aggregates progress.

You already know how to build each of those pieces — you built smaller versions of every one across Weeks 5 through 12. The capstone does not ask you to learn a new framework. It asks you to do the one thing the topic weeks never forced: make three independently-built clients agree on one contract, against one real database, with one set of tests that prove the agreement, running in CI on every push. That is a different skill from "build a gRPC service," and the difference is **order**. The capstone is won or lost in the first two days, by the sequence in which you assemble the pieces — not by the pieces themselves.

There are two orders you could build in, and only one of them works.

The order that fails is **UI-first, breadth-first**. You build the learner's lesson list screen in MAUI, hard-coding the data; then you build the instructor's lesson-create form in Blazor, hard-coding *its* data; then, with two pretty screens, you "wire up the backend." Now you discover that the MAUI screen assumed a lesson has a `Title` and a `Duration`, the Blazor form produces a `Name` and a `Length`, and the backend you wrote third has `lesson_title` and `minutes`. You have three vocabularies for one concept and no forcing function that made them agree. You spend the back half of the week in reconciliation meetings with yourself. This is the most common way a capstone produces a Friday demo and a Monday rewrite.

The order that works is **contract-first, depth-first**: write the contract, generate the clients from it, and build *one thin path all the way through* before you build any breadth. That is the subject of this lecture.

## 2. The walking skeleton: a thin path through every layer on day one

The term comes from Alistair Cockburn (<https://wiki.c2.com/?WalkingSkeleton>): a *walking skeleton* is "a tiny implementation of the system that performs a small end-to-end function. It need not use the final architecture, but it should link together the main architectural components." Freeman and Pryce, in *Growing Object-Oriented Software, Guided by Tests*, make it the first move of any project: get something — anything — running end to end, including the deployment and the test harness, before you build a single feature. The feature is trivial; the *path* is everything.

For the Polyglot Workshop, the walking skeleton is one sentence: **an instructor creates a lesson, a learner enrolls in it and submits work, and the submission appears in the admin moderation queue.** That sentence is the vertical slice. It is "vertical" because it cuts down through every horizontal layer of the system rather than building one layer wide:

```
   instructor (Blazor admin)              learner (MAUI)
            │                                  │
            ▼  CreateLesson (gRPC-Web)         ▼  Enroll, Submit (native gRPC)
   ┌────────────────────────────────────────────────────┐
   │   ASP.NET Core 9 backend                            │
   │   ├─ gRPC service (mirrors the domain)              │
   │   ├─ Minimal API (REST mirror of the same ops)      │
   │   ├─ domain model + EF Core mapping                 │
   │   └─ Serilog event + OTel span per call             │
   └────────────────────────────────────────────────────┘
            │
            ▼  EF Core / Npgsql
   ┌────────────────────┐
   │   PostgreSQL        │  (Testcontainers in tests)
   └────────────────────┘
            ▲
            │  Keycloak-issued bearer token validated on every call
```

The slice is deliberately thin. It does not paginate. It does not handle the offline-sync conflict case. It does not draw a chart. It moves *one* lesson and *one* submission through *every* component — the proto, the generated server stub, the generated client stubs in both MAUI and Blazor, the EF mapping, the database, the auth token, the log line, the trace. If that path is green end to end on Monday, then on Tuesday you are *adding* — a second endpoint, a second screen — to a skeleton that already stands. If that path is not green, you have no business adding anything, because you do not yet have a system; you have three programs that compile.

The discipline to internalize: **breadth is cheap once depth exists; depth is impossible to retrofit onto breadth.** Build the one path to the floor first.

## 3. Why the contract comes before the clients

In a single-client system you can get away with writing the server and the client together, adjusting the shapes as you go. In a *three*-client system you cannot, because there is no "together" — the MAUI team-of-you and the Blazor team-of-you and the backend team-of-you are three contexts that will drift the instant they are allowed to. The only thing that stops drift is a single artifact that all three depend on and none of them can quietly edit: **the contract.**

For the Polyglot Workshop the contract is `workshop.proto`. It is the source of truth in the strongest possible sense: it is the *only* place the domain's wire shape is written down, and every client gets its types by *generating* from it, not by hand-writing them. The mechanics are Lecture 2's subject; the *principle* is this lecture's:

> A hand-written DTO that duplicates a `.proto` message is a code-review reject, not a convenience. It creates a second source of truth, and two sources of truth are zero sources of truth.

When you add a `difficulty` field to the `Lesson` message, you regenerate, and both the MAUI client and the Blazor admin either compile against the new field or fail to compile. There is no third state in which one client "missed the memo." That binary — compiles or breaks — is the entire value of contract-first. It converts a coordination problem (did everyone update their DTO?) into a build problem (does it compile?), and a build problem is one that CI can answer for you on every push.

Here is the proto for the walking slice. It is small on purpose — this is day one:

```proto
syntax = "proto3";

option csharp_namespace = "Workshop.Contract";

package workshop.v1;

import "google/protobuf/timestamp.proto";

// The classroom domain, version 1. This file is the single source of truth.
// Both the MAUI client and the Blazor admin generate their types from it.
service Workshop {
  rpc CreateLesson(CreateLessonRequest) returns (Lesson);
  rpc Enroll(EnrollRequest) returns (Enrollment);
  rpc Submit(SubmitRequest) returns (Submission);
  rpc ListPendingSubmissions(ListPendingSubmissionsRequest)
      returns (ListPendingSubmissionsResponse);
}

message Lesson {
  string id = 1;
  string tenant_id = 2;
  string title = 3;
  string body = 4;
  google.protobuf.Timestamp created_at = 5;
}

message CreateLessonRequest {
  string title = 1;
  string body = 2;
}

message Enrollment {
  string id = 1;
  string lesson_id = 2;
  string learner_id = 3;
  google.protobuf.Timestamp enrolled_at = 4;
}

message EnrollRequest {
  string lesson_id = 1;
}

message Submission {
  string id = 1;
  string lesson_id = 2;
  string learner_id = 3;
  string content = 4;
  SubmissionStatus status = 5;
  google.protobuf.Timestamp submitted_at = 6;
}

enum SubmissionStatus {
  SUBMISSION_STATUS_UNSPECIFIED = 0;
  SUBMISSION_STATUS_PENDING = 1;
  SUBMISSION_STATUS_APPROVED = 2;
  SUBMISSION_STATUS_REJECTED = 3;
}

message SubmitRequest {
  string lesson_id = 1;
  string content = 2;
}

message ListPendingSubmissionsRequest {
  int32 page_size = 1;
  string page_token = 2;
}

message ListPendingSubmissionsResponse {
  repeated Submission submissions = 1;
  string next_page_token = 2;
}
```

Notice what is *not* in the proto: there is no `instructor_id` on `CreateLessonRequest` and no `learner_id` on `EnrollRequest` or `SubmitRequest`. The caller's identity is **not** a wire field — it comes from the validated bearer token on the call, server-side, via `ServerCallContext.GetHttpContext().User`. Putting identity in the request body is one of the most common security mistakes in a gRPC contract; a client could claim to be anyone. The token is the identity; the proto carries only what the caller is *entitled* to assert. This is the kind of decision the contract-first order forces you to make on Monday, in the proto review, rather than discover on Thursday when the MAUI client starts sending whatever `learner_id` it likes.

## 4. The domain model behind the contract

The proto is the wire shape. It is **not** the domain model, and conflating the two is a trap. The proto is generated, allocation-heavy, and exists to cross the network; the domain model is yours, hand-written, persisted by EF Core, and exists to hold invariants. Keeping them separate is what lets you change the wire shape without rewriting the database, and change the database without breaking the wire.

The domain entities for the slice, in idiomatic C# 13:

```csharp
#nullable enable
namespace Workshop.Domain;

public sealed class Lesson
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public required string TenantId { get; init; }
    public required string InstructorId { get; init; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    // EF Core needs a parameterless ctor for materialization; private keeps it
    // off the public surface so application code goes through the factory.
    private Lesson() { TenantId = ""; InstructorId = ""; Title = ""; Body = ""; }

    public static Lesson Create(string tenantId, string instructorId, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A lesson must have a title.", nameof(title));
        return new Lesson
        {
            TenantId = tenantId,
            InstructorId = instructorId,
            Title = title.Trim(),
            Body = body,
        };
    }
}

public sealed class Submission
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public required Guid LessonId { get; init; }
    public required string LearnerId { get; init; }
    public required string Content { get; set; }
    public SubmissionStatus Status { get; private set; } = SubmissionStatus.Pending;
    public DateTimeOffset SubmittedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void Approve() => Status = SubmissionStatus.Approved;
    public void Reject() => Status = SubmissionStatus.Rejected;
}

public enum SubmissionStatus { Pending = 1, Approved = 2, Rejected = 3 }
```

Three things are worth naming. First, `Guid.CreateVersion7()` — new in .NET 9 — gives time-ordered UUIDs, which index far better in PostgreSQL than the random `Guid.NewGuid()` because they cluster by insertion time and do not fragment the B-tree. For a system whose whole story is "it persists to a real database," using the right id type from commit one is the cheapest performance decision you will make. Second, the `required` modifier plus `init` accessors means the compiler enforces that `TenantId`, `Title`, and `Body` are set at construction — there is no "valid-but-half-built" `Lesson` that can exist. Third, the domain `SubmissionStatus` enum is *not* the proto `SubmissionStatus` enum; they happen to share a name across namespaces and they map to each other in the mapping layer (Lecture 2), but the domain one starts at `1` for its own reasons and the proto one has the mandatory `_UNSPECIFIED = 0`. Keeping them distinct is the discipline; mapping them is one switch expression.

## 5. The vertical slice as one endpoint, both surfaces

The slice's first behavior is "create a lesson." The contract-first order says: it exists on the gRPC surface (because the proto declares it) and it is mirrored on the REST surface (because the syllabus requires both). The *same* domain call backs both. Here is the gRPC service method:

```csharp
#nullable enable
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Workshop.Contract;   // generated from workshop.proto
using Workshop.Domain;

namespace Workshop.Api.Grpc;

public sealed class WorkshopService(WorkshopDbContext db, ILogger<WorkshopService> log)
    : Workshop.Contract.Workshop.WorkshopBase
{
    public override async Task<Lesson> CreateLesson(
        CreateLessonRequest request, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        var instructorId = http.User.FindFirst("sub")?.Value
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "No subject claim."));
        var tenantId = http.User.FindFirst("tenant")?.Value ?? "default";

        var lesson = Domain.Lesson.Create(tenantId, instructorId, request.Title, request.Body);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(context.CancellationToken);

        log.LogInformation("Lesson {LessonId} created by {InstructorId} in tenant {TenantId}",
            lesson.Id, instructorId, tenantId);

        return lesson.ToProto();   // mapping extension, Lecture 2
    }
}
```

And the REST mirror, as a Minimal API endpoint, calling the *same* domain factory and the *same* DbContext:

```csharp
app.MapPost("/api/lessons", async (
    CreateLessonDto dto,
    WorkshopDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var instructorId = http.User.FindFirst("sub")?.Value;
    if (instructorId is null) return Results.Unauthorized();
    var tenantId = http.User.FindFirst("tenant")?.Value ?? "default";

    var lesson = Lesson.Create(tenantId, instructorId, dto.Title, dto.Body);
    db.Lessons.Add(lesson);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/api/lessons/{lesson.Id}", lesson.ToProto());
})
.RequireAuthorization();

public sealed record CreateLessonDto(string Title, string Body);
```

The two surfaces are not duplicating *logic* — both call `Lesson.Create` and `db.SaveChangesAsync`. They are duplicating only the thin adapter that turns a transport request into a domain call. That is acceptable and intentional: REST and gRPC are two doors into one house. The day either door grows its own business logic is the day the system has split in two, and the code review's job is to catch it. (Week 14 introduces MediatR precisely so both doors call one handler; this week we keep it explicit so the duplication is *visible* and you can feel why MediatR will earn its keep.)

## 6. Scope cuts: the discipline of building less

A capstone has a fixed amount of time and an unbounded amount of possible scope. The walking-skeleton method gives you a *tool* for deciding what to build: anything that is not on the one vertical path does not get built this week. Everything else is a scope cut, written down and deferred — not abandoned, deferred — to a later week or a backlog.

For the integration-baseline week, the cut list is roughly:

- **Deferred to Week 14 (harden):** MediatR handlers, AutoMapper, the OWASP threat-model pass, the Grafana/Loki/Tempo stack, pagination correctness edge cases, optimistic-concurrency conflict handling, the full moderation workflow (approve/reject with audit).
- **Deferred to Week 15 (deploy):** the multi-stage Dockerfile, the GitHub Actions *deploy* job (this week's Actions job only *tests*), Azure Container Apps, the MAUI sideload build, the RUNBOOK.
- **Cut from the slice entirely this week:** the offline-sync conflict resolution in MAUI (the slice assumes online), the analytics charts in Blazor (the slice shows a list, not a chart), the SignalR presence feature (not on the create→enroll→submit path).

Writing the cut list down is not bureaucracy; it is the artifact that lets you say "no, not this week" without anxiety, because the thing you are saying no to has a home. The Sunday retrospective on the schedule is exactly this: review the cut list, confirm the slice is green, and confirm that what you deferred is genuinely Week-14 or Week-15 work and not something the baseline secretly needs.

The honest measure of a scope cut: **if the vertical slice is still green end to end without it, it was a correct cut.** A chart is a correct cut — the submission still reaches the admin queue without it. Auth is *not* a correct cut — the slice cannot run a real token without it, so auth is in the baseline. Use the green slice as the arbiter, not your sense of what is "important."

## 7. The order, restated as a checklist

Put concretely, the build order this lecture prescribes — the order the exercises and the mini-project follow — is:

1. **Write `workshop.proto`** for the slice (create, enroll, submit, list-pending). Review it for the identity-in-token rule. *This is the contract; nothing is built before it.*
2. **Generate the server stub and both client stubs** from it (Lecture 2). Confirm all three projects build against the generated types — empty implementations are fine. *Now the contract is load-bearing: a drift is a build break.*
3. **Build the domain model and EF mapping** for the slice's entities, plus the proto↔entity mapping (Lecture 2).
4. **Implement the gRPC service and its REST mirror** for `CreateLesson` — one endpoint, both doors — backed by EF Core against PostgreSQL.
5. **Wire Serilog and OpenTelemetry** in `Program.cs` (Lecture 3) so step 4's call already emits a structured event and a span. *Observability is wired before the test, not after.*
6. **Write the integration test** with `WebApplicationFactory<T>` over Testcontainers PostgreSQL + Keycloak (Lecture 3): mint a token, call `CreateLesson` over the in-memory gRPC channel, assert the row exists and the response matches. *This is what "green" means.*
7. **Run the slice end to end through the real clients:** Blazor admin creates the lesson over gRPC-Web, MAUI enrolls and submits over native gRPC, the submission appears in the admin's pending list.
8. **Make CI run steps 2, 6, and 7** on every push (Lecture 3). *Now "it works" is a fact CI verifies, not a claim you make.*

Steps 1–3 are Monday and Tuesday morning. Steps 4–6 are Tuesday and Wednesday. Steps 7–8 are Thursday and the mini-project. By Friday you are *adding the second endpoint* to a skeleton that already walks — which is the whole point of building the skeleton first.

## 8. The definition of done for the integration baseline

A milestone needs a definition of done that is binary — true or false, not "mostly." For the integration baseline, the definition is five facts, each of which is mechanically checkable, so there is no room to argue you are "almost there":

1. **All three projects build against the one contract.** `dotnet build Workshop.sln` is green for the backend and the Blazor admin, and `dotnet build -f net9.0-android` is green for the MAUI client. Checkable: the build exit code.
2. **The vertical slice passes as an integration test.** One `[Fact]` drives create → enroll → submit → list over real gRPC with a real token against a real, migrated PostgreSQL. Checkable: the test result.
3. **The integration test uses real infrastructure, not mocks.** The factory overrides only addresses; the database is Testcontainers PostgreSQL, the auth is Testcontainers Keycloak. Checkable: grep the test project for `UseInMemoryDatabase` and fake auth schemes — there must be none.
4. **The same green runs in CI.** A GitHub Actions run builds all three and runs the integration suite with Testcontainers in the runner, gating the merge. Checkable: the Actions run status on the PR.
5. **Every request emits a structured log and a trace.** A `CreateLesson` call produces a Serilog event with `LessonId` as a field and an OpenTelemetry trace with the gRPC and EF Core spans. Checkable: read the console log and the collector output.

Notice that none of the five mentions a screen, a chart, a color, or a layout. The integration baseline is graded on the *spine* of the system being real and verifiable, not on its skin. This is deliberate and it is the syllabus's intent: "graded on contract integrity, test coverage of meaningful paths, the quality of the deploy pipeline, and the runbook — not on visual polish." A team that spends Week 13 making the Blazor grid pretty and skips the integration test has not reached the baseline, no matter how good the grid looks. A team with a plain list and all five facts green *has* reached it.

The reason to make the definition this strict is that the baseline is the foundation the next two weeks build on, and a foundation you cannot verify is a foundation you cannot trust. Week 14's hardening assumes the slice is green so it can *edit* — delete code, add MediatR where it earns its keep, wire the observability stack — without re-establishing that the system works. Week 15's deploy assumes the tests are green so the pipeline has something trustworthy to ship. If the baseline's "done" were fuzzy, every subsequent week would inherit the fuzz. The strict, checkable definition is what lets the capstone compound instead of accumulate debt.

## 9. Why the day-by-day order is what it is

The weekly schedule in the README is not arbitrary; it is the contract-first order spread across the days, and it is worth understanding *why* each day is where it is, because the order is the part students most often get wrong under time pressure.

**Monday is planning and the proto, with no code beyond the contract.** The temptation on Monday is to "get started" by writing a service method or a screen. Resist it. Monday's deliverable is a reviewed `workshop.proto` and a written vertical-slice statement plus scope-cut ledger. That feels slow — a whole day and "nothing runs" — but it is the cheapest day of the week to change your mind about the contract, because nothing depends on it yet. A field you rename on Monday costs a regeneration; the same rename on Thursday costs a sweep through three clients and a test suite.

**Tuesday wires the contract through generation and builds the service.** With the proto fixed, Tuesday makes it load-bearing: generate the three sides, confirm all three projects compile against the generated types (even with empty bodies), then implement the service and the mapping. By Tuesday night you have a backend that compiles and a slice that *could* work — the skeleton's bones, not yet walking.

**Wednesday is the integration substrate.** This is the day that rewards patience and punishes shortcuts. `WebApplicationFactory<T>`, Testcontainers, the migration application, the first green integration test. A flaky test "fixed" with a `Thread.Sleep` on Wednesday is a flaky CI run you will fight for the rest of the capstone. Get Wednesday right and the rest of the week stands on solid ground.

**Thursday wires observability and CI, and tackles the challenges.** Serilog and OpenTelemetry go in *before* Friday's mini-project so the mini-project's first run already emits. The CI workflow goes in so Friday's work is green-where-it-counts as it lands, not validated retroactively.

**Friday and Saturday assemble and prove.** The mini-project is assembly, not discovery — every piece was built earlier in the week, and the slice walks end to end. Saturday is CI green and the baseline write-up.

**Sunday is the retrospective.** Review the scope-cut ledger; confirm the slice is green; confirm what you deferred is genuinely Week-14 or Week-15 work. The retrospective is where you catch a cut that was actually a hole — something the baseline secretly needed that you talked yourself out of building.

The through-line: **contract Monday, generation and service Tuesday, substrate Wednesday, observability and CI Thursday, assembly Friday–Saturday, retrospective Sunday.** Depth before breadth, every day.

## 10. What you now know, and what Lecture 2 builds on it

You have the philosophy and the order: a thin path through every layer on day one, a contract that every client generates from and none can quietly edit, identity that lives in the token and not the wire, a domain model kept distinct from the wire shape, two transport doors into one set of domain calls, and a scope-cut discipline arbitrated by the green slice. None of that is code you have written yet — it is the *plan* the code will follow.

Lecture 2 makes the contract real: it walks the `Grpc.Tools` MSBuild wiring that turns `workshop.proto` into a server stub and two client stubs, the `GrpcServices` attribute that decides which side each project generates, the proto↔entity mapping layer, and the gRPC-Web configuration that lets the Blazor admin reach the *same* service the MAUI client reaches over native gRPC. That is "keeping three clients honest against one contract," in mechanism rather than principle. Lecture 3 then makes "green" mean something: `WebApplicationFactory<T>` over Testcontainers, migrations in tests, and the Serilog + OpenTelemetry wiring that makes a passing test a trustworthy fact about a real database with real auth.

Read the proto in this lecture one more time before you move on. Everything in the next two lectures — every generated type, every mapping, every assertion — is downstream of those nine messages and four RPCs. Get the contract right on Monday and the rest of the week is filling it in. Get it wrong on Monday and the rest of the week is reconciliation. The order is the lesson.
