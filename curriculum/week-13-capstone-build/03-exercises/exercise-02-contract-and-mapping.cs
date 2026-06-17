// Exercise 2 — The Contract, Implemented: the gRPC Service, its REST Mirror,
// and the proto<->entity Mapping That Keeps Them Honest.
//
// Builds on exercise 1 (the proto and the domain entities). By the end the
// backend implements CreateLesson on BOTH surfaces (gRPC and REST), backed by
// the same domain factory and the same DbContext, with a hand-written mapping
// layer between the wire types and the entities.
//
// Goal: prove the domain is transport-agnostic. The same lesson created over
// REST must be readable over gRPC, because both doors open onto one house.
//
// Project layout (extends exercise 1):
//
//   src/Workshop.Api/
//     Workshop.Api.csproj           <-- Sdk.Web; references Contract + Domain
//     Program.cs                    <-- PART 3 (registration + REST mirror)
//     WorkshopDbContext.cs          <-- EF Core context (PART 1)
//     Grpc/WorkshopService.cs       <-- the gRPC service (PART 2)
//     Mapping/ProtoMappings.cs      <-- proto<->entity (PART 4)
//
// Packages on Workshop.Api:
//   Grpc.AspNetCore, Microsoft.EntityFrameworkCore,
//   Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.AspNetCore.Authentication.JwtBearer
//
// Acceptance criteria:
//   1. `dotnet build` succeeds, 0 warnings, 0 errors.
//   2. CreateLesson exists on the gRPC service (override of WorkshopBase) AND
//      as a Minimal-API POST /api/lessons; both call Lesson.Create + SaveChanges.
//   3. Neither surface reads the caller identity from the request body; both
//      read the "sub" claim from the validated token.
//   4. ProtoMappings.ToProto round-trips a Lesson and a Submission, converting
//      DateTimeOffset <-> google.protobuf.Timestamp and the status enum.
//   5. A unit test (no DB) proves Lesson.Create(...).ToProto() preserves Title,
//      Body, and CreatedAt. (The DB-backed test is exercise 3.)

// ============================================================================
// PART 1 — WorkshopDbContext.cs
// ============================================================================

#nullable enable
using Microsoft.EntityFrameworkCore;
using Workshop.Domain;

namespace Workshop.Api;

public sealed class WorkshopDbContext(DbContextOptions<WorkshopDbContext> options) : DbContext(options)
{
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Submission> Submissions => Set<Submission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var lesson = builder.Entity<Lesson>();
        lesson.HasKey(l => l.Id);
        lesson.Property(l => l.TenantId).HasMaxLength(64).IsRequired();
        lesson.Property(l => l.InstructorId).HasMaxLength(128).IsRequired();
        lesson.Property(l => l.Title).HasMaxLength(256).IsRequired();
        lesson.Property(l => l.Body).IsRequired();
        // (Tenant, Id) supports the tenant-scoped reads the admin makes.
        lesson.HasIndex(l => new { l.TenantId, l.Id }).HasDatabaseName("ix_lessons_tenant_id");

        var sub = builder.Entity<Submission>();
        sub.HasKey(s => s.Id);
        sub.Property(s => s.LearnerId).HasMaxLength(128).IsRequired();
        sub.Property(s => s.Content).IsRequired();
        // The moderation queue query: WHERE Status = Pending ORDER BY SubmittedAt.
        sub.HasIndex(s => new { s.Status, s.SubmittedAt }).HasDatabaseName("ix_submissions_status_time");

        builder.Entity<Enrollment>().HasKey(e => e.Id);
    }
}

// ============================================================================
// PART 2 — Grpc/WorkshopService.cs (override the generated base class)
//
// TODO(you): implement Submit and ListPendingSubmissions following the same
// shape. RequireSubject reads identity from the token, never the request.
// ============================================================================

using Grpc.Core;
using Workshop.Api.Mapping;
using Workshop.Contract;

namespace Workshop.Api.Grpc;

public sealed class WorkshopService(WorkshopDbContext db, ILogger<WorkshopService> log)
    : Workshop.Contract.Workshop.WorkshopBase
{
    public override async Task<Lesson> CreateLesson(CreateLessonRequest request, ServerCallContext context)
    {
        var instructorId = RequireSubject(context);
        var tenantId = TenantOf(context);

        var lesson = Domain.Lesson.Create(tenantId, instructorId, request.Title, request.Body);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync(context.CancellationToken);
        log.LogInformation("Lesson {LessonId} created by {InstructorId}", lesson.Id, instructorId);
        return lesson.ToProto();
    }

    public override async Task<Enrollment> Enroll(EnrollRequest request, ServerCallContext context)
    {
        var learnerId = RequireSubject(context);
        if (!Guid.TryParse(request.LessonId, out var lessonId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "lesson_id is not a GUID."));
        if (!await db.Lessons.AnyAsync(l => l.Id == lessonId, context.CancellationToken))
            throw new RpcException(new Status(StatusCode.NotFound, $"Lesson {lessonId} not found."));

        var enrollment = Domain.Enrollment.Create(lessonId, learnerId);
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync(context.CancellationToken);
        return enrollment.ToProto();
    }

    public override Task<Submission> Submit(SubmitRequest request, ServerCallContext context)
    {
        // TODO(you): mirror Enroll — read learner from token, validate lesson,
        // create the Submission (status Pending), persist, return ToProto().
        throw new NotImplementedException();
    }

    public override Task<ListPendingSubmissionsResponse> ListPendingSubmissions(
        ListPendingSubmissionsRequest request, ServerCallContext context)
    {
        // TODO(you): query Submissions WHERE Status == Pending, ordered by
        // SubmittedAt, take page_size, map each ToProto(), return the response.
        throw new NotImplementedException();
    }

    private static string RequireSubject(ServerCallContext context)
        => context.GetHttpContext().User.FindFirst("sub")?.Value
           ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "No subject claim."));

    private static string TenantOf(ServerCallContext context)
        => context.GetHttpContext().User.FindFirst("tenant")?.Value ?? "default";
}

// ============================================================================
// PART 3 — Program.cs (registration + the REST mirror of CreateLesson)
// ============================================================================
//
//   var builder = WebApplication.CreateBuilder(args);
//   builder.Services.AddDbContext<WorkshopDbContext>(o =>
//       o.UseNpgsql(builder.Configuration.GetConnectionString("Workshop")));
//   builder.Services.AddGrpc(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment());
//   builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", o =>
//   {
//       o.Authority = builder.Configuration["Oidc:Authority"];
//       o.Audience  = builder.Configuration["Oidc:Audience"];
//       o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
//   });
//   builder.Services.AddAuthorization();
//   var app = builder.Build();
//   app.UseAuthentication();
//   app.UseAuthorization();
//   app.MapGrpcService<Workshop.Api.Grpc.WorkshopService>();
//
//   app.MapPost("/api/lessons", async (CreateLessonDto dto, WorkshopDbContext db,
//       HttpContext http, CancellationToken ct) =>
//   {
//       var instructorId = http.User.FindFirst("sub")?.Value;
//       if (instructorId is null) return Results.Unauthorized();
//       var tenantId = http.User.FindFirst("tenant")?.Value ?? "default";
//       var lesson = Workshop.Domain.Lesson.Create(tenantId, instructorId, dto.Title, dto.Body);
//       db.Lessons.Add(lesson);
//       await db.SaveChangesAsync(ct);
//       return Results.Created($"/api/lessons/{lesson.Id}", lesson.ToProto());
//   }).RequireAuthorization();
//
//   app.Run();
//   public sealed record CreateLessonDto(string Title, string Body);
//   public partial class Program { }   // <-- so WebApplicationFactory<Program> works (exercise 3)

// ============================================================================
// PART 4 — Mapping/ProtoMappings.cs
//
// TODO(you): complete the Submission mapping including the status switch.
// ============================================================================

using Google.Protobuf.WellKnownTypes;
using DomainLesson = Workshop.Domain.Lesson;
using DomainSubmission = Workshop.Domain.Submission;
using DomainEnrollment = Workshop.Domain.Enrollment;

namespace Workshop.Api.Mapping;

public static class ProtoMappings
{
    public static Lesson ToProto(this DomainLesson l) => new()
    {
        Id = l.Id.ToString(),
        TenantId = l.TenantId,
        Title = l.Title,
        Body = l.Body,
        CreatedAt = Timestamp.FromDateTimeOffset(l.CreatedAt),
    };

    public static Enrollment ToProto(this DomainEnrollment e) => new()
    {
        Id = e.Id.ToString(),
        LessonId = e.LessonId.ToString(),
        LearnerId = e.LearnerId,
        EnrolledAt = Timestamp.FromDateTimeOffset(e.EnrolledAt),
    };

    public static Submission ToProto(this DomainSubmission s) => new()
    {
        Id = s.Id.ToString(),
        LessonId = s.LessonId.ToString(),
        LearnerId = s.LearnerId,
        Content = s.Content,
        // TODO(you): map the domain status to the proto SubmissionStatus with a
        // switch expression, defaulting unmapped values to Unspecified.
        Status = SubmissionStatus.Unspecified,
        SubmittedAt = Timestamp.FromDateTimeOffset(s.SubmittedAt),
    };
}
