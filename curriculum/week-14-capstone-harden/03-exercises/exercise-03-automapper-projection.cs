// Exercise 3 — Replace hand-written DTO constructions with AutoMapper ProjectTo,
//              prove the SQL projection, and keep the three logic-bearing mappings
//              hand-written.
//
// Goal: the workshop's read endpoints contain ~15 hand-written
//   new SubmissionDto { Id = s.Id, LessonId = s.LessonId, ... }
// constructions. You will:
//   (a) add a single AutoMapper Profile with name-matched, logic-free maps,
//   (b) replace the constructions with mapper.ProjectTo<TDto>(query) so the
//       projection is pushed into the SQL SELECT (a BOPLA win AND a perf win),
//   (c) prove via the EF Core SQL log that only the DTO columns are selected,
//   (d) identify the THREE mappings that must STAY hand-written and explain why,
//   (e) add AssertConfigurationIsValid() as a test so unmapped properties fail CI.
//
// Citations:
//   ProjectTo:          https://docs.automapper.org/en/stable/Queryable-Extensions.html
//   Config validation:  https://docs.automapper.org/en/stable/Configuration-validation.html
//   BOPLA:              https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/

#nullable enable

// ============================================================================
// PART 1 — WorkshopMappingProfile.cs (ONLY name-matched, logic-free maps)
// ============================================================================

using AutoMapper;
using Workshop.Domain;
using Workshop.Api.Contracts;

namespace Workshop.Api.Mapping;

public sealed class WorkshopMappingProfile : Profile
{
    public WorkshopMappingProfile()
    {
        // A reviewer can predict every one of these outputs from the type shapes.
        // Submission has InternalNotes and LearnerEmail; the DTO has neither, so
        // ProjectTo never even SELECTs those columns from the database.
        CreateMap<Submission, SubmissionDto>();
        CreateMap<Lesson, LessonSummaryDto>();
        CreateMap<Enrollment, EnrollmentDto>();

        // Do NOT add CreateMap<CreateLessonRequest, Lesson>() here — that inbound
        // mapping sets TenantId from the caller's CLAIM, not the body. It stays
        // hand-written (see PART 4) precisely so nobody can mass-assign TenantId.
    }
}

// ============================================================================
// PART 2 — The read endpoint, using ProjectTo
// ============================================================================
//
// using AutoMapper;
// using AutoMapper.QueryableExtensions;
// using Microsoft.EntityFrameworkCore;
//
// app.MapGet("/api/lessons/{lessonId:guid}/submissions",
//     async (Guid lessonId, int page, int pageSize,
//            WorkshopDbContext db, IMapper mapper, CancellationToken ct) =>
// {
//     const int MaxPageSize = 100;                       // API4: pagination cap
//     int size   = Math.Clamp(pageSize, 1, MaxPageSize);
//     int offset = Math.Max(0, page) * size;
//
//     // ProjectTo turns IQueryable<Submission> into IQueryable<SubmissionDto>;
//     // the generated SQL SELECTs only the DTO's four columns.
//     var items = await db.Submissions
//         .Where(s => s.LessonId == lessonId)
//         .OrderBy(s => s.SubmittedAtUtc)
//         .ProjectTo<SubmissionDto>(mapper.ConfigurationProvider)
//         .Skip(offset).Take(size)
//         .ToListAsync(ct);
//
//     return Results.Ok(items);
// })
// .RequireAuthorization()
// .RequireRateLimiting("per-user");
//
// // Registration (Program.cs):
// //   builder.Services.AddAutoMapper(typeof(WorkshopMappingProfile).Assembly);

// ============================================================================
// PART 3 — Prove the SQL projection (this is the point of ProjectTo)
// ============================================================================
//
// Turn on EF Core command logging and run the endpoint. The emitted SQL must be:
//
//   SELECT s."Id", s."LessonId", s."Grade", s."SubmittedAtUtc"
//   FROM "Submissions" AS s
//   WHERE s."LessonId" = @lessonId
//   ORDER BY s."SubmittedAtUtc"
//   LIMIT @size OFFSET @offset
//
// If you see s."InternalNotes" or s."Content" in the SELECT, the projection did
// NOT happen — you probably materialized the entity first (e.g. .ToList() before
// .ProjectTo, or mapped in memory). Fix the pipeline so ProjectTo runs on the
// IQueryable, not on an already-materialized list.

// ============================================================================
// PART 4 — The THREE mappings that STAY hand-written (and why)
// ============================================================================
//
// namespace Workshop.Api.Mapping;
//
// public static class HandWrittenMappings
// {
//     // (1) Carries a JOIN + a COMPUTED field. A reviewer cannot predict
//     //     PercentOfClassMedian from the type shapes; it is a calculation, not a
//     //     projection. Hand-written so the computation is unit-testable in isolation.
//     public static SubmissionGradeReportDto ToGradeReport(
//         Submission s, string lessonTitle, double classMedian) =>
//         new(
//             s.Id,
//             lessonTitle,
//             s.Grade,
//             classMedian == 0 ? 0 : Math.Round(100.0 * (s.Grade ?? 0) / classMedian, 1));
//
//     // (2) ROLE-CONDITIONAL output. What a caller sees depends on WHO is asking —
//     //     that is authorization logic, not mapping. Hand-written; both branches tested.
//     public static ProfileDto ToProfileDto(User u, bool viewerIsSelfOrInstructor) =>
//         viewerIsSelfOrInstructor
//             ? new ProfileDto(u.DisplayName, u.EmailVerifiedAtUtc)
//             : new ProfileDto(u.DisplayName, EmailVerifiedAtUtc: null);
//
//     // (3) INBOUND, sets TenantId from the CLAIM not the body (mass-assignment
//     //     defense, OWASP API3). A mapper that read TenantId from the source would
//     //     be a security hole. Hand-written so TenantId can ONLY come from the claim.
//     public static Lesson FromCreateRequest(
//         CreateLessonRequest req, string tenantIdFromClaim, string instructorId) =>
//         new()
//         {
//             Id          = Guid.NewGuid(),
//             Title       = req.Title,
//             Description = req.Description,
//             TenantId    = tenantIdFromClaim,    // NOT req.TenantId — there is no such field
//             InstructorId = instructorId
//         };
// }

// ============================================================================
// PART 5 — The configuration-validation test  <-- YOU WRITE THE ASSERT
// ============================================================================

using AutoMapper;
using Workshop.Api.Mapping;
using Xunit;

namespace Workshop.UnitTests;

public sealed class MappingConfigurationTests
{
    [Fact]
    public void Mapping_configuration_is_valid()
    {
        var config = new MapperConfiguration(c => c.AddProfile<WorkshopMappingProfile>());

        // Fails the build if any DTO property is unmapped. This is the price of
        // admission for using AutoMapper at all — it buys back the compile-time
        // safety AutoMapper otherwise trades away.
        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void ProfileDto_hides_email_from_non_self_non_instructor_viewers()
    {
        var user = new Workshop.Domain.User
        {
            DisplayName = "Bob",
            EmailVerifiedAtUtc = DateTimeOffset.UtcNow
        };

        var asStranger = HandWrittenMappings.ToProfileDto(user, viewerIsSelfOrInstructor: false);

        Assert.Null(asStranger.EmailVerifiedAtUtc);   // the role-conditional branch
        Assert.Equal("Bob", asStranger.DisplayName);
    }
}

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] The ~15 hand-written `new SubmissionDto { ... }` reads are gone, replaced
//      by ProjectTo on the IQueryable.
//   [ ] The EF Core SQL log shows ONLY the DTO columns in the SELECT (no
//      InternalNotes, no LearnerEmail, no Content).
//   [ ] AssertConfigurationIsValid() passes (and fails if you add an unmapped
//      DTO property — try it, then revert).
//   [ ] The three logic-bearing mappings are hand-written and unit-tested.
//   [ ] There is NO CreateMap<CreateLessonRequest, Lesson> anywhere.
//
// Stretch (counted toward Exercise 3 if you finish the above with time left):
//   1. Benchmark (BenchmarkDotNet) the ProjectTo read vs the old materialize-then-map
//      read on a table with a large InternalNotes column. Report the allocation and
//      time delta — the projection should win on both because it never reads the column.
//   2. Add a Roslyn analyzer rule (or a unit test) that fails CI if any endpoint
//      returns a domain ENTITY directly instead of a DTO (a BOPLA guard).
