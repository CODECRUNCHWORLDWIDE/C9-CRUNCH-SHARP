// Exercise 2 — Collapse three endpoints into one MediatR request + behaviors,
//              and delete the copy-paste.
//
// Goal: the workshop has three near-identical write endpoints —
//   POST /api/lessons/{id}/submissions            (learner submits)
//   PUT  /api/submissions/{id}                     (learner resubmits)
//   POST /api/lessons/{id}/submissions/on-behalf   (instructor submits for a learner)
// Each one repeats: validate input, authorize the actor, open a transaction,
// write the submission, enqueue an outbox event, commit. You will:
//   (a) introduce ValidationBehavior, AuthorizationBehavior, TransactionBehavior,
//   (b) express the three operations as commands whose handlers contain ONLY the
//       business logic (the cross-cutting concerns move to the pipeline),
//   (c) verify the harden diff is NET-NEGATIVE (you delete more than you add).
//
// Citations:
//   Behaviors:        https://github.com/jbogard/MediatR/wiki/Behaviors
//   "don't need it":  https://www.jimmybogard.com/you-probably-dont-need-mediatr/
//   FluentValidation: https://docs.fluentvalidation.net/en/latest/
//
// Project layout:
//   src/Workshop.Application/
//     Behaviors/ValidationBehavior.cs       <-- PART 1
//     Behaviors/TransactionBehavior.cs      <-- PART 2
//     Submissions/SubmitExerciseCommand.cs  <-- PART 3
//     Submissions/SubmitExerciseHandler.cs  <-- PART 3
//     Submissions/SubmitExerciseValidator.cs<-- PART 3
//   src/Workshop.Api/Endpoints/SubmissionEndpoints.cs  <-- PART 4 (the thin adapters)

#nullable enable

// ============================================================================
// PART 1 — ValidationBehavior.cs
// ============================================================================

using FluentValidation;
using MediatR;

namespace Workshop.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count != 0)
            {
                // Thrown once; translated to RFC 9457 ProblemDetails by the boundary handler.
                throw new ValidationException(failures);
            }
        }

        return await next();
    }
}

// ============================================================================
// PART 2 — TransactionBehavior.cs
// ============================================================================
//
// using MediatR;
// using Workshop.Infrastructure;
//
// namespace Workshop.Application.Behaviors;
//
// // Marker so ONLY commands are transactional; queries flow straight through.
// public interface ICommand;
// public interface ICommand<out TResponse> : IRequest<TResponse>, ICommand;
//
// public sealed class TransactionBehavior<TRequest, TResponse>(WorkshopDbContext db)
//     : IPipelineBehavior<TRequest, TResponse>
//     where TRequest : ICommand
// {
//     public async Task<TResponse> Handle(
//         TRequest request,
//         RequestHandlerDelegate<TResponse> next,
//         CancellationToken cancellationToken)
//     {
//         if (db.Database.CurrentTransaction is not null)
//         {
//             return await next();   // nested send; don't double-open
//         }
//
//         await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
//         var response = await next();                      // handler adds entity + outbox row
//         await db.SaveChangesAsync(cancellationToken);
//         await tx.CommitAsync(cancellationToken);          // state change + event, atomically
//         return response;
//     }
// }

// ============================================================================
// PART 3 — The command, its validator, and its handler (business logic ONLY)
// ============================================================================
//
// using MediatR;
// using Workshop.Application.Behaviors;
// using Workshop.Domain;
// using Workshop.Infrastructure;
//
// namespace Workshop.Application.Submissions;
//
// // One command covers submit and resubmit; ExistingSubmissionId distinguishes them.
// public sealed record SubmitExerciseCommand(
//     Guid LessonId,
//     string Content,
//     Guid? ExistingSubmissionId,   // null = new submission; set = resubmission
//     string? OnBehalfOfLearnerId   // null = self; set = instructor-on-behalf
// ) : ICommand<Guid>;
//
// public sealed class SubmitExerciseValidator : AbstractValidator<SubmitExerciseCommand>
// {
//     public SubmitExerciseValidator()
//     {
//         RuleFor(c => c.LessonId).NotEmpty();
//         RuleFor(c => c.Content).NotEmpty().MaximumLength(50_000);
//     }
// }
//
// public sealed class SubmitExerciseHandler(
//     WorkshopDbContext db,
//     ITenantContext tenant,
//     ICurrentUser currentUser,
//     TimeProvider clock)
//     : IRequestHandler<SubmitExerciseCommand, Guid>
// {
//     // NOTE: no validation block, no transaction, no authorize call — those are
//     // in the pipeline. This method is the business logic, and nothing else.
//     public async Task<Guid> Handle(SubmitExerciseCommand cmd, CancellationToken ct)
//     {
//         string learnerId = cmd.OnBehalfOfLearnerId ?? currentUser.UserId;
//
//         Submission submission;
//         if (cmd.ExistingSubmissionId is { } existingId)
//         {
//             submission = await db.Submissions.FirstAsync(s => s.Id == existingId, ct);
//             submission.Content      = cmd.Content;
//             submission.SubmittedAtUtc = clock.GetUtcNow();
//         }
//         else
//         {
//             submission = new Submission
//             {
//                 Id            = Guid.NewGuid(),
//                 LessonId      = cmd.LessonId,
//                 LearnerId     = learnerId,
//                 TenantId      = tenant.TenantId,
//                 Content       = cmd.Content,
//                 SubmittedAtUtc = clock.GetUtcNow()
//             };
//             db.Submissions.Add(submission);
//         }
//
//         // Outbox row in the SAME transaction (TransactionBehavior commits both).
//         db.OutboxMessages.Add(OutboxMessage.For(
//             new SubmissionReceived(submission.Id, submission.LessonId, learnerId)));
//
//         return submission.Id;
//     }
// }

// ============================================================================
// PART 4 — The endpoints shrink to thin adapters
// ============================================================================
//
// using MediatR;
// using Workshop.Application.Submissions;
//
// namespace Workshop.Api.Endpoints;
//
// public static class SubmissionWriteEndpoints
// {
//     public static void MapSubmissionWriteEndpoints(this IEndpointRouteBuilder app)
//     {
//         var group = app.MapGroup("/api").RequireAuthorization();
//
//         // submit (self)
//         group.MapPost("/lessons/{lessonId:guid}/submissions",
//             async (Guid lessonId, SubmitBody body, IMediator m) =>
//             {
//                 var id = await m.Send(new SubmitExerciseCommand(lessonId, body.Content, null, null));
//                 return Results.Created($"/api/submissions/{id}", new { id });
//             });
//
//         // resubmit (self)
//         group.MapPut("/submissions/{id:guid}",
//             async (Guid id, ResubmitBody body, IMediator m) =>
//             {
//                 await m.Send(new SubmitExerciseCommand(body.LessonId, body.Content, id, null));
//                 return Results.NoContent();
//             });
//
//         // submit on behalf (instructor) — same command, OnBehalfOfLearnerId set;
//         // the AuthorizationBehavior enforces the InstructorOnly policy for this shape.
//         group.MapPost("/lessons/{lessonId:guid}/submissions/on-behalf",
//             async (Guid lessonId, OnBehalfBody body, IMediator m) =>
//             {
//                 var id = await m.Send(new SubmitExerciseCommand(
//                     lessonId, body.Content, null, body.LearnerId));
//                 return Results.Created($"/api/submissions/{id}", new { id });
//             })
//             .RequireAuthorization("InstructorOnly");
//     }
// }
//
// public sealed record SubmitBody(string Content);
// public sealed record ResubmitBody(Guid LessonId, string Content);
// public sealed record OnBehalfBody(string Content, string LearnerId);

// ============================================================================
// PART 5 — Registration (Program.cs)
// ============================================================================
//
// builder.Services.AddMediatR(cfg =>
// {
//     cfg.RegisterServicesFromAssembly(typeof(SubmitExerciseCommand).Assembly);
//     // Order matters: validate -> authorize -> transaction -> handler.
//     cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
//     cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
//     cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
// });
// builder.Services.AddValidatorsFromAssembly(typeof(SubmitExerciseValidator).Assembly);

// ============================================================================
// COMMANDS — what you actually run
// ============================================================================
//
//   dotnet build                # must be 0 warnings, 0 errors
//   dotnet test --filter "FullyQualifiedName~SubmitExercise"
//
//   # Prove the validation behavior runs ONCE (not per-handler): submit an empty
//   # Content and expect a 400 ProblemDetails with the "Content" error key, for
//   # ALL THREE endpoints — without any handler containing a validation block.
//
//   # Measure the diff:
//   git diff --stat HEAD~1   # the "submissions" feature should show MORE lines
//                            # deleted than added (the copy-paste is gone).

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] The three handlers contain NO validation, NO transaction, NO authorize call.
//   [ ] Submitting empty Content returns 400 ProblemDetails on all three endpoints.
//   [ ] The submission and its outbox row commit atomically (kill the process
//      between Add and Commit in a test; neither row is present).
//   [ ] `git diff --stat` shows the feature is net-negative in lines.
//   [ ] Queries (e.g. GetSubmission) do NOT open a transaction (the ICommand
//      constraint excludes them) — verify with an EF Core log or a trace.
//
// Stretch (counted toward Exercise 2 if you finish the above with time left):
//   1. Add a LoggingBehavior that logs the request type, the elapsed ms, and the
//      TraceId for every Send — one place, every request. Verify it appears in Loki.
//   2. Write a unit test for TransactionBehavior using an in-memory Sqlite that
//      asserts a thrown handler exception rolls back BOTH the entity and the outbox row.
