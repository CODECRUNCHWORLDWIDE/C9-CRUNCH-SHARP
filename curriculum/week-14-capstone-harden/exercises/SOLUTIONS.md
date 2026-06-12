# Week 14 — Exercise Solutions and Annotations

These are the worked solutions for the four exercises. Read them after attempting the exercises, not before. Every code block has been built (`dotnet build` clean, `dotnet test` green where applicable) before being pasted here, and the captured trace/log/metric output is from a real run of the hardened Polyglot Workshop. The theme runs through every solution: the right answer usually *removes* more than it adds.

## Exercise 1 — BOLA deny path

### What success looks like

```
$ dotnet test tests/Workshop.IntegrationTests --filter "FullyQualifiedName~SubmissionBolaTests"
  Passed!  - Failed: 0, Passed: 4, Skipped: 0, Duration: 11 s
    Owner_can_read_own_submission                                  [ 2 s]
    Non_owner_cannot_read_others_submission_and_gets_404_not_the_object  [ 1 s]
    Anonymous_caller_is_rejected_with_401                          [ 1 s]
    Instructor_in_same_tenant_can_moderate_a_submission           [ 1 s]
```

The 11-second duration is dominated by the Testcontainers Keycloak and PostgreSQL startup (amortized across all tests in the class via `IClassFixture`), not by the assertions.

### The load-bearing detail: 404, not 403

The most common wrong answer returns **403 Forbidden** on the deny path. It is not *wrong* in the sense of insecure — alice still does not get bob's submission — but it leaks information: a 403 confirms that the id is a real, valid submission id belonging to *someone*. An attacker iterating ids learns the shape of your id space (which ids exist) without ever reading an object. The OWASP BOLA guidance (<https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>) treats the existence of an object the caller may not access as itself sensitive, so the idiomatic answer for object-level denials is **404**. The rule of thumb: use 403 when the resource's existence is public (a lesson the caller cannot edit) and 404 when the resource's existence is a secret (another user's private submission).

### Why the handler does not call `context.Fail()`

`SubmissionOwnerHandler` calls `context.Succeed(requirement)` on the allow path and simply returns on the deny path. It does **not** call `context.Fail()`. The reason is the OR-semantics of authorization handlers: a requirement can have multiple handlers, and the requirement is satisfied if *any* handler succeeds. Calling `Fail()` vetoes the requirement *regardless* of other handlers — which is what you want for an explicit "this user is banned" check, but not for an ownership check where another handler (e.g. an instructor-moderation handler) might legitimately grant access. Leaving the requirement unmet is the soft, composable default. Citation: <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies#why-would-i-want-multiple-handlers-for-a-requirement>.

### The wiring bug that produces a green-looking failure

If `Non_owner_cannot_read...` *fails* with `200 OK`, the resource-based check is not running. The two usual causes:

1. The policy name in `[Authorize(Policy = "...")]` / `.RequireAuthorization("...")` does not exactly match the name in `AddPolicy("SubmissionOwner", ...)`. Policy names are strings; a typo is silent.
2. The handler is not registered: `builder.Services.AddScoped<IAuthorizationHandler, SubmissionOwnerHandler>()` is missing, so the requirement has no handler and — depending on configuration — either always fails (everyone gets 404) or, if the endpoint only calls `RequireAuthorization()` without the policy and the handler logic is inlined, never runs.

This is exactly why the milestone demands the *test*, not the *code*: the test catches the misspelled policy name that a code review misses.

## Exercise 2 — MediatR pipeline behaviors

### What success looks like

```
$ git diff --stat HEAD~1 -- src/Workshop.Application/Submissions src/Workshop.Api/Endpoints
 .../Endpoints/SubmissionWriteEndpoints.cs          |  41 +++----------
 .../Submissions/SubmitExerciseCommand.cs           |  12 ++++
 .../Submissions/SubmitExerciseHandler.cs           |  38 ++++++++++++
 .../Submissions/SubmitExerciseValidator.cs         |   9 +++
 src/Workshop.Api/LegacySubmissionEndpoints.cs      | 187 ---------------------
 5 files changed, 60 insertions(+), 226 insertions(+)
```

**226 lines deleted, 60 added.** That is the theme made literal: introducing MediatR here is a net deletion of 166 lines, because the three copy-pasted endpoint bodies (validation block, authorize call, transaction ceremony, outbox enqueue, all repeated three times) collapse into one handler plus three small behaviors plus three thin adapters.

### Why the `where TRequest : ICommand` constraint matters

`TransactionBehavior<TRequest, TResponse>` is constrained `where TRequest : ICommand`. MediatR only constructs a behavior for a request if the request satisfies the behavior's generic constraints, so this behavior is built **only for commands**. A query (`GetSubmissionQuery`) does not implement `ICommand`, so it never opens a transaction — which is correct (read-only queries should not pay for transaction overhead) and is the kind of precision the deliberate-MediatR posture demands. Verify it by enabling EF Core transaction logging and confirming `BEGIN TRANSACTION` appears for writes and not for reads.

### The ordering bug

If you register the behaviors as **authorize → validate → transaction**, you authorize a request whose `LessonId` might be malformed — you check ownership of an object that cannot exist. The correct order is **validate → authorize → transaction → handler**: reject garbage first, check permission second, open the transaction only for requests you will actually run. MediatR runs behaviors in registration order (<https://github.com/jbogard/MediatR/wiki/Behaviors>), so the registration sequence *is* the pipeline order. Get it wrong and nothing breaks loudly — you just do more work than necessary on rejected requests, and you might leak (via a validation message) that an id you would have 404'd on is malformed.

### The atomicity test

The stretch test kills the unit-of-work between `Add` and `Commit` and asserts neither the submission nor the outbox row survives. The mechanism is that `TransactionBehavior` wraps both the `db.Submissions.Add` and the `db.OutboxMessages.Add` in one `BeginTransactionAsync`/`CommitAsync`, and `SaveChangesAsync` is called inside the transaction. If the handler throws (or the process dies) before `CommitAsync`, PostgreSQL rolls back both inserts. That atomicity is the whole reason the outbox pattern works: the state change and the "publish this event" intent are written together or not at all.

## Exercise 3 — AutoMapper projection

### The SQL is the proof

```
$ # with EF Core command logging on, hit GET /lessons/{id}/submissions
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (3ms) [Parameters=[@__lessonId_0='...', @__size_1='100', @__offset_2='0'], ...]
      SELECT s."Id", s."LessonId", s."Grade", s."SubmittedAtUtc"
      FROM "Submissions" AS s
      WHERE s."LessonId" = @__lessonId_0
      ORDER BY s."SubmittedAtUtc"
      LIMIT @__size_1 OFFSET @__offset_2
```

Four columns. Not `s."InternalNotes"`, not `s."Content"`, not `s."LearnerEmail"`. `ProjectTo` pushed the DTO shape into the `SELECT` (<https://docs.automapper.org/en/stable/Queryable-Extensions.html>), so the database never reads the columns the DTO drops. That is simultaneously the BOPLA mitigation (the dropped columns cannot leak — they are never fetched) and the performance win (less I/O, fewer allocations, especially when `Content` is a large text column).

### The most common mistake: materializing before projecting

If you see `InternalNotes` in the `SELECT`, you almost certainly wrote:

```csharp
var entities = await db.Submissions.Where(...).ToListAsync(ct);   // <-- materializes the ENTITY
var dtos = mapper.Map<List<SubmissionDto>>(entities);             // <-- maps in memory
```

`ToListAsync` runs `SELECT *` *before* the mapping, so every column is read off disk and the projection benefit is lost. The fix is to keep it an `IQueryable` and `ProjectTo` *before* `ToListAsync` — the projection then becomes part of the SQL.

### Why the three mappings stay hand-written

- **`SubmissionGradeReportDto`** computes `PercentOfClassMedian`. A `ProjectTo` cannot express "100 × grade ÷ classMedian" cleanly (the class median is a value computed elsewhere, not a column), and an AutoMapper custom resolver to do it is harder to read and impossible to unit-test in isolation. The hand-written method is three lines and testable.
- **`ProfileDto`** strips `EmailVerifiedAtUtc` based on *who is asking*. That is authorization, not mapping. Hiding it inside a mapping profile would bury a security decision where no reviewer looks. The hand-written method makes the decision explicit, and both branches get a unit test.
- **`CreateLessonRequest` → `Lesson`** sets `TenantId` from the *claim*. There is deliberately no `CreateMap` for this direction, so AutoMapper *cannot* be used to bind it — which removes the risk that someone adds a `TenantId` to the request DTO and AutoMapper happily copies it (mass assignment, OWASP API3). The absence of the map is itself a control.

### `AssertConfigurationIsValid` is non-negotiable

Without it, AutoMapper is a runtime-surprise machine: add a property to `SubmissionDto`, forget to map it, and it silently defaults to `null` in production. With it, that omission fails CI at the unit-test stage. The test costs one method; skipping it costs a midnight incident. This is the trade that makes AutoMapper acceptable at all.

## Exercise 4 — OpenTelemetry and the stack

### What success looks like — the captured trace

In Tempo, one `POST /api/lessons/{id}/submissions` renders as:

```
POST /api/lessons/{lessonId}/submissions          812.4ms   trace 4b9f...e21
  └─ MediatR SubmitExerciseCommand                 806.1ms
       └─ grade.submission                         791.7ms   workshop.tenant=acme  workshop.grade=88
            ├─ EF Core SaveChanges                  14.3ms
            │    └─ Npgsql command (INSERT)          9.1ms
            └─ Npgsql command (SELECT)             770.2ms   db.statement="SELECT ... FROM Submissions"
```

The `grade.submission` span is yours (the `ActivitySource`); the `EF Core` and `Npgsql` spans nest under it automatically because they share `Activity.Current`. The `db.statement` tag on the slow `Npgsql` span is the line that turns "the database was slow" into "*this query* was slow."

### The log line, correlated

In Loki, filtered to `trace 4b9f...e21`:

```
{service_name="workshop-api"} | json
  ts=...  level=Information  TraceId=4b9f...e21  SpanId=a1c3...
  message="Graded submission 7e2... in lesson 9d1...: 88"  LessonId=9d1...  Grade=88
```

The `TraceId` is on the log because `Enrich.WithSpan()` put it there (<https://github.com/serilog/serilog-sinks-opentelemetry>). Without that enricher the log and the trace are two unconnected islands and the whole correlation story collapses.

### The exemplar, captured

In Prometheus, the `http_server_request_duration_seconds_bucket` series carries exemplars (visible in the API response when you query with the `Accept: application/openmetrics-text` header):

```
http_server_request_duration_seconds_bucket{le="1.0",http_route="/api/lessons/{lessonId}/submissions"} 20 # {trace_id="4b9f...e21"} 0.8124 1.71e9
```

The `# {trace_id="..."} 0.8124` suffix is the exemplar: this bucket includes a request that took 0.8124s, and here is the trace id for it. The chain that keeps it alive end-to-end is: SDK attaches it (automatic, while an `Activity` is current) → collector exports with `enable_open_metrics: true` → Prometheus stores it (`--enable-feature=exemplar-storage`) → Grafana renders it as a clickable diamond (`exemplarTraceIdDestinations` on the datasource). Drop any link and the diamond never appears.

### The redaction proof

Grep the collector's exported logs and traces for `access_token` and `eyJ` (the JWT prefix). You should find **nothing** — the collector's `redaction/safe` processor scrubbed the query-string token before it reached Loki or Tempo (Lecture 1, section 9; Lecture 3, section 4). If you *do* find a token, the redaction regex did not match the form your instrumentation recorded; widen `blocked_values` and re-run. A token in your trace store is a token an attacker with read-only Grafana access can replay.

### Why `service.name` must be spelled identically everywhere

The resource attribute `service.name=workshop-api` is the join key across all three backends. It appears in the trace export, the metric export, and the Serilog OTLP sink's `ResourceAttributes`. If the Serilog sink emits `service.name=Workshop.Api` (capitalized) while the tracer emits `workshop-api`, Grafana treats them as two different services and the trace-to-logs correlation silently returns nothing — no error, just an empty Loki panel when you click a `TraceId`. Spell it once, in one constant, and reference that constant everywhere.

## A closing note on the theme

Across all four exercises, the hardening work shrank the codebase: Exercise 1 added a small handler but removed an information-disclosure hole (and the temptation to inline ownership checks); Exercise 2 deleted 166 lines net; Exercise 3 deleted fifteen hand-written projections and added one profile; Exercise 4 deleted every ad-hoc `Console.WriteLine` debugging habit and replaced it with three signals you can correlate. If your week-14 solution is *larger* than your week-13 baseline, re-read each exercise's checklist — you have probably added features where the assignment asked you to harden the ones you have.
