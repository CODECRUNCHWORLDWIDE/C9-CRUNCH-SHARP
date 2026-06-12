# Week 13 — Exercise Solutions

Annotated solutions for the four exercises. Read these after you have attempted each exercise; the value is in the reasoning, not the answer. Every solution traces to a lecture section and a citation. The expected build/test output is given so you can confirm your own.

---

## Exercise 1 — The Vertical Slice, as Code

### The two `TODO` factories

`Enrollment.Create` and `Submission.Create` mirror `Lesson.Create` — validate, then construct with `required` members set:

```csharp
public static Enrollment Create(Guid lessonId, string learnerId)
{
    if (lessonId == Guid.Empty)
        throw new ArgumentException("Lesson id is required.", nameof(lessonId));
    if (string.IsNullOrWhiteSpace(learnerId))
        throw new ArgumentException("Learner is required.", nameof(learnerId));
    return new Enrollment { LessonId = lessonId, LearnerId = learnerId };
}

public static Submission Create(Guid lessonId, string learnerId, string content)
{
    if (lessonId == Guid.Empty)
        throw new ArgumentException("Lesson id is required.", nameof(lessonId));
    if (string.IsNullOrWhiteSpace(learnerId))
        throw new ArgumentException("Learner is required.", nameof(learnerId));
    if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("A submission must have content.", nameof(content));
    return new Submission { LessonId = lessonId, LearnerId = learnerId, Content = content };
}
```

The point of the validation is that an invalid entity *cannot exist* — there is no path that produces a `Submission` with empty content. That invariant is the domain's job; the proto cannot enforce it (proto3 has no required fields), which is exactly why the domain model is distinct from the wire model (Lecture 1 §4).

### The slice statement and the cuts

The reference vertical path:

> "An instructor creates a lesson; a learner enrolls and submits; the submission appears in the instructor's pending moderation queue."

It is correct because it touches every layer of every client: the proto (four RPCs), the generated server stub, both generated client stubs (Blazor for create, MAUI for enroll/submit), the EF mapping, PostgreSQL, the Keycloak token, the log line, the span. Three correct scope cuts:

- **Offline sync in MAUI** — deferred; the slice assumes the learner is online. (Week 14/portfolio.)
- **Analytics charts in Blazor** — cut from the slice; the admin shows a *list*, not a chart. The submission still reaches the queue without a chart, so it is a correct cut (the green slice is the arbiter).
- **The multi-stage Dockerfile and deploy** — deferred to Week 15; this week's CI only *tests*.

### Why no caller-identity field in the proto

`CreateLessonRequest` has only `title` and `body`; identity comes from the validated token's `sub` claim. A `learner_id` field on `SubmitRequest` would let any client claim to be any learner — the single most common gRPC contract security mistake (Lecture 1 §3). Cite the proto3 guide at <https://protobuf.dev/programming-guides/proto3/> and the auth chapter at <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>.

### Expected output

```
$ dotnet build
Build succeeded · 0 warnings · 0 errors · 1.1 s
```

The generated `obj/.../WorkshopGrpc.cs` should contain `Workshop.Contract.Workshop.WorkshopBase` and `...WorkshopClient`. If it does not, check that `<Protobuf Include="Protos/workshop.proto" GrpcServices="Both" />` is present and `Grpc.Tools` is referenced.

---

## Exercise 2 — The Contract, Implemented

### `Submit` and `ListPendingSubmissions`

```csharp
public override async Task<Submission> Submit(SubmitRequest request, ServerCallContext context)
{
    var learnerId = RequireSubject(context);
    if (!Guid.TryParse(request.LessonId, out var lessonId))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "lesson_id is not a GUID."));
    if (!await db.Lessons.AnyAsync(l => l.Id == lessonId, context.CancellationToken))
        throw new RpcException(new Status(StatusCode.NotFound, $"Lesson {lessonId} not found."));

    var submission = Domain.Submission.Create(lessonId, learnerId, request.Content);
    db.Submissions.Add(submission);
    await db.SaveChangesAsync(context.CancellationToken);
    log.LogInformation("Submission {Id} received for lesson {LessonId}", submission.Id, lessonId);
    return submission.ToProto();
}

public override async Task<ListPendingSubmissionsResponse> ListPendingSubmissions(
    ListPendingSubmissionsRequest request, ServerCallContext context)
{
    _ = RequireSubject(context);   // any authenticated instructor may read the queue
    var pageSize = request.PageSize is > 0 and <= 200 ? request.PageSize : 50;

    var rows = await db.Submissions
        .Where(s => s.Status == Domain.SubmissionStatus.Pending)
        .OrderBy(s => s.SubmittedAt)
        .Take(pageSize)
        .ToListAsync(context.CancellationToken);

    var response = new ListPendingSubmissionsResponse();
    response.Submissions.AddRange(rows.Select(r => r.ToProto()));
    return response;
}
```

The `ListPendingSubmissions` query is backed by the `ix_submissions_status_time` index from the `DbContext` — `WHERE Status = Pending ORDER BY SubmittedAt` is exactly its shape, so the read is an index scan, not a table scan. Choosing the index to match the query at modeling time is the EF Core discipline from Week 6 carried into the capstone.

### The status mapping

```csharp
Status = s.Status switch
{
    Workshop.Domain.SubmissionStatus.Pending  => SubmissionStatus.Pending,
    Workshop.Domain.SubmissionStatus.Approved => SubmissionStatus.Approved,
    Workshop.Domain.SubmissionStatus.Rejected => SubmissionStatus.Rejected,
    _ => SubmissionStatus.Unspecified,
},
```

The `_ => Unspecified` arm is deliberate forward-compatibility: a future domain status that has not yet been mapped serializes as `UNSPECIFIED` rather than throwing. Because the proto3 enum *requires* a zero value (`SUBMISSION_STATUS_UNSPECIFIED = 0`), that arm has a meaningful target. The domain enum starts at `1` so its values never collide with the proto's zero (Lecture 2 §4).

### The REST/gRPC mirror unit test (no DB)

```csharp
[Fact]
public void Created_lesson_maps_to_proto_preserving_fields()
{
    var lesson = Lesson.Create("default", "instr-1", "Records 101", "Value semantics.");
    var proto = lesson.ToProto();

    Assert.Equal(lesson.Id.ToString(), proto.Id);
    Assert.Equal("Records 101", proto.Title);
    Assert.Equal("Value semantics.", proto.Body);
    Assert.Equal(lesson.CreatedAt, proto.CreatedAt.ToDateTimeOffset());
}
```

`Timestamp.FromDateTimeOffset` and `.ToDateTimeOffset()` round-trip through UTC. If the assertion on `CreatedAt` fails by an offset, you stored a local `DateTime` somewhere instead of a `DateTimeOffset` in UTC — the bug Lecture 2 §4 warns about.

### Expected output

```
$ dotnet build
Build succeeded · 0 warnings · 0 errors · 1.4 s
$ dotnet test tests/Workshop.UnitTests
Passed! - Failed: 0, Passed: 4, Skipped: 0
```

---

## Exercise 3 — The Integration Baseline

### Why this is the milestone

This is the exercise that makes "green" mean something (Lecture 3 §1). The factory overrides **only** the connection string and the OIDC authority. The two anti-patterns to avoid:

- `UseInMemoryDatabase` — would skip Npgsql entirely, so a Postgres-specific query failure (or a migration bug) passes the test. We use the real `postgres:16-alpine` container.
- A stubbed `"Test"` auth scheme — would skip token validation, so an auth bug ships green. We mint a real token from the real Keycloak and let the real JWT middleware validate it.

### `MigrateAsync`, not `EnsureCreated`

The harness applies migrations against the ephemeral database:

```csharp
using var scope = _factory.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
await db.Database.MigrateAsync();
```

`EnsureCreated` would build the schema from the model and *bypass the migrations*, so a broken migration sails past. `MigrateAsync` runs the actual migration files — the same ones that run in production — so a passing test is a statement about the migrations (Lecture 3 §4). You generate the initial migration once with `dotnet ef migrations add InitialCreate -p src/Workshop.Api`.

### `TokenForAsync`

The token is minted against the Keycloak container's token endpoint (challenge 1 has the full walkthrough; the core is):

```csharp
public async Task<string> TokenForAsync(string subject, string role)
{
    using var http = new HttpClient();
    var form = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"]    = "password",
        ["client_id"]     = "workshop-api",
        ["client_secret"] = "test-secret",   // from the imported realm
        ["username"]      = subject,          // seeded user in workshop-realm.json
        ["password"]      = "test-password",
        ["scope"]         = "openid",
    });
    var resp = await http.PostAsync(
        $"{fixture.Issuer}/protocol/openid-connect/token", form);
    resp.EnsureSuccessStatusCode();
    using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("access_token").GetString()!;
}
```

The seeded users (`instructor-1`, `learner-1`) and the client live in `Realms/workshop-realm.json`, imported with `--import-realm` (challenge 1).

### Why the second test (NotFound)

`Submit_with_unknown_lesson_is_NotFound` proves the *failure* path is also part of the contract: the service returns `StatusCode.NotFound`, and the generated client surfaces it as an `RpcException` the caller can branch on. An integration baseline that only tests the happy path is half a baseline.

### Expected output

```
$ docker info        # must succeed; Testcontainers needs a Docker socket
$ dotnet test tests/Workshop.IntegrationTests
  Starting postgres:16-alpine ... done (2.1s)
  Starting quay.io/keycloak/keycloak:25.0 (--import-realm) ... done (6.4s)
Passed! - Failed: 0, Passed: 2, Skipped: 0  [12.8s]
$ docker ps          # nothing lingers; Ryuk reaped the containers
```

If the run hangs on Keycloak start, the realm JSON failed to import — check the container logs with `docker logs <id>` for the realm-import line.

---

## Exercise 4 — The Browser Side of One Contract

### Why a TypeScript client at all

The Blazor admin is C# and uses `GrpcWebHandler`. The TS client is the *proof* that the contract is genuinely language-neutral: a browser generated from the same `workshop.proto`, with the same status codes and the same identity-in-token rule, hits the same service. If a TS client, a Blazor client, and a MAUI client all work against one proto, "the contract is the source of truth" is demonstrated, not asserted.

### The most common bug: missing exposed headers

If `createLesson()` always fails with a "no status" / empty-status error even though the server log shows the call succeeded, the server's CORS policy is not exposing `Grpc-Status` and `Grpc-Message`. The browser's CORS layer strips response headers it was not told to expose, so the client never sees the gRPC status. The fix is on the *server* (Lecture 2 §6):

```csharp
.WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")
```

This is worth memorizing — it is the single most common gRPC-Web setup failure.

### The UNAUTHENTICATED check

Calling `createLesson()` with no token must throw `WorkshopAuthError` (gRPC status `16` / `UNAUTHENTICATED`), not a generic network error. That proves the backend enforces auth for the browser client exactly as for the MAUI client — there is no "the browser is special" carve-out. The same `[Authorize]`/`RequireAuthorization` that protects the native-gRPC path protects the gRPC-Web path, because it is the same service.

### Expected output

```
$ npm run build
tsc --noEmit    # 0 errors
```

In the Network tab, the requests are `content-type: application/grpc-web-text` (the base64 framing from `mode=grpcwebtext`) and the response carries a `grpc-status: 0` header on success. A `grpc-status: 16` with no Authorization header confirms the auth enforcement.

---

## A note on the assembled slice

By the end of these four exercises you have, in order: the contract and domain (E1), the service implementing both surfaces with mapping (E2), the integration test that proves it on real infrastructure (E3), and a third-language client proving the contract is neutral (E4). That *is* the integration baseline in miniature — the mini-project assembles it into the full repository with Serilog, OpenTelemetry, and CI. The order you did the exercises in is the order Lecture 1 prescribed: contract first, depth before breadth.
