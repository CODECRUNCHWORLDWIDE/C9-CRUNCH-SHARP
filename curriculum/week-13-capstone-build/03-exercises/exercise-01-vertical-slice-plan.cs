// Exercise 1 — The Vertical Slice, as Code: Domain Entities, the Contract, and
// the One Path That Proves the Integration.
//
// This is a capstone exercise, not a toy problem. By the end you will have the
// `workshop.proto` for the walking slice and the domain model behind it, and a
// written-down statement of the one end-to-end path you will make green this
// week. Steps 2 and 3 build on this; the mini-project assembles it.
//
// Goal: lock the contract and the domain BEFORE building any client. The order
// is the lesson (Lecture 1): contract first, depth before breadth.
//
// Project layout (you create this — there is no template to copy from):
//
//   Workshop.sln
//   src/Workshop.Contract/
//     Workshop.Contract.csproj      <-- holds Protos/workshop.proto, GrpcServices="Both"
//     Protos/workshop.proto         <-- PART 1 below
//   src/Workshop.Domain/
//     Workshop.Domain.csproj        <-- classlib, net9.0
//     Lesson.cs, Enrollment.cs, Submission.cs   <-- PART 2 below
//
// Commands you run (in this order):
//
//   dotnet new sln -n Workshop
//   dotnet new classlib -n Workshop.Contract -o src/Workshop.Contract -f net9.0
//   dotnet new classlib -n Workshop.Domain   -o src/Workshop.Domain   -f net9.0
//   dotnet sln add src/Workshop.Contract src/Workshop.Domain
//   # add the Grpc.Tools / Grpc.Net.Client / Google.Protobuf packages to the
//   # Contract project (see Lecture 2 §2 for the exact .csproj), paste the
//   # proto into Protos/workshop.proto, then:
//   dotnet build
//
// Acceptance criteria:
//   1. `dotnet build` succeeds with 0 warnings, 0 errors across both projects.
//   2. The generated namespace Workshop.Contract contains Lesson,
//      CreateLessonRequest, Enrollment, Submission, SubmissionStatus, and the
//      Workshop.WorkshopBase / Workshop.WorkshopClient types.
//   3. No request message carries a caller-identity field (no instructor_id on
//      CreateLessonRequest, no learner_id on EnrollRequest/SubmitRequest).
//   4. The domain entities use Guid.CreateVersion7() for ids and `required`
//      members; there is no public parameterless ctor that leaves an entity
//      half-built.
//   5. You can state the one vertical path in a sentence (see PART 3).

// ============================================================================
// PART 1 — Protos/workshop.proto (lives in Workshop.Contract; the source of
// truth). Author it; do NOT hand-write any C# message type that mirrors these.
// ============================================================================
//
//   syntax = "proto3";
//   option csharp_namespace = "Workshop.Contract";
//   package workshop.v1;
//   import "google/protobuf/timestamp.proto";
//
//   service Workshop {
//     rpc CreateLesson(CreateLessonRequest) returns (Lesson);
//     rpc Enroll(EnrollRequest) returns (Enrollment);
//     rpc Submit(SubmitRequest) returns (Submission);
//     rpc ListPendingSubmissions(ListPendingSubmissionsRequest)
//         returns (ListPendingSubmissionsResponse);
//   }
//
//   message Lesson {
//     string id = 1; string tenant_id = 2; string title = 3; string body = 4;
//     google.protobuf.Timestamp created_at = 5;
//   }
//   message CreateLessonRequest { string title = 1; string body = 2; }
//   message Enrollment {
//     string id = 1; string lesson_id = 2; string learner_id = 3;
//     google.protobuf.Timestamp enrolled_at = 4;
//   }
//   message EnrollRequest { string lesson_id = 1; }
//   message Submission {
//     string id = 1; string lesson_id = 2; string learner_id = 3;
//     string content = 4; SubmissionStatus status = 5;
//     google.protobuf.Timestamp submitted_at = 6;
//   }
//   enum SubmissionStatus {
//     SUBMISSION_STATUS_UNSPECIFIED = 0;
//     SUBMISSION_STATUS_PENDING = 1;
//     SUBMISSION_STATUS_APPROVED = 2;
//     SUBMISSION_STATUS_REJECTED = 3;
//   }
//   message SubmitRequest { string lesson_id = 1; string content = 2; }
//   message ListPendingSubmissionsRequest { int32 page_size = 1; string page_token = 2; }
//   message ListPendingSubmissionsResponse {
//     repeated Submission submissions = 1; string next_page_token = 2;
//   }

// ============================================================================
// PART 2 — the domain entities (Workshop.Domain). These are NOT the proto
// types; they are your hand-written model with invariants. The mapping between
// them is exercise 2.
//
// TODO(you): complete Enrollment.Create and the Submission factory following
// the same shape as Lesson.Create. The compiler will tell you what is missing.
// ============================================================================

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

    private Lesson() { TenantId = ""; InstructorId = ""; Title = ""; Body = ""; }

    public static Lesson Create(string tenantId, string instructorId, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(instructorId))
            throw new ArgumentException("Instructor is required.", nameof(instructorId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A lesson must have a title.", nameof(title));

        return new Lesson
        {
            TenantId = tenantId,
            InstructorId = instructorId,
            Title = title.Trim(),
            Body = body ?? "",
        };
    }
}

public sealed class Enrollment
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public required Guid LessonId { get; init; }
    public required string LearnerId { get; init; }
    public DateTimeOffset EnrolledAt { get; private set; } = DateTimeOffset.UtcNow;

    private Enrollment() { LearnerId = ""; }

    // TODO(you): validate lessonId is non-empty and learnerId is non-blank,
    // then return a new Enrollment. Mirror Lesson.Create's shape.
    public static Enrollment Create(Guid lessonId, string learnerId)
    {
        // your code here
        throw new NotImplementedException();
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

    private Submission() { LearnerId = ""; Content = ""; }

    // TODO(you): a learner submits content for a lesson they are enrolled in.
    // Validate content is non-empty. Status starts Pending. Return the entity.
    public static Submission Create(Guid lessonId, string learnerId, string content)
    {
        // your code here
        throw new NotImplementedException();
    }

    public void Approve() => Status = SubmissionStatus.Approved;
    public void Reject() => Status = SubmissionStatus.Rejected;
}

public enum SubmissionStatus { Pending = 1, Approved = 2, Rejected = 3 }

// ============================================================================
// PART 3 — write the slice down. Fill in the const string below with the ONE
// path you will make green this week. It must touch every layer of every
// client. (The reference answer is in SOLUTIONS.md; write yours first.)
//
//   "An instructor creates a lesson; a learner enrolls and submits; the
//    submission appears in the instructor's pending moderation queue."
//
// Then list THREE scope cuts — things explicitly NOT in this week's slice that
// you are deferring to Week 14 or Week 15. The green slice is the arbiter: if
// the slice is still green without it, it was a correct cut.
// ============================================================================

namespace Workshop.Planning;

public static class Slice
{
    public const string VerticalPath =
        "TODO(you): one sentence; the path that touches every layer of every client.";

    public static readonly string[] ScopeCuts =
    [
        "TODO(you): a scope cut deferred to Week 14 or 15",
        "TODO(you): a second scope cut",
        "TODO(you): a third scope cut",
    ];
}
