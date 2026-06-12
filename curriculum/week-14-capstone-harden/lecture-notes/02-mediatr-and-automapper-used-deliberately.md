# Lecture 2 — MediatR and AutoMapper, Used Deliberately: Pipeline Behaviors That Earn Their Keep, and Projection-Only Mapping

> **Time:** 2 hours. Take MediatR in one sitting and AutoMapper in a second. **Prerequisites:** Lecture 1 (resource-based authorization — we move the check into a pipeline behavior here), Week 4 (dependency injection, the options pattern), Week 6 (EF Core and `IQueryable`). **Citations:** the MediatR behaviors wiki at <https://github.com/jbogard/MediatR/wiki/Behaviors>, Jimmy Bogard's "you probably don't need MediatR" at <https://www.jimmybogard.com/you-probably-dont-need-mediatr/>, and the AutoMapper queryable-extensions docs at <https://docs.automapper.org/en/stable/Queryable-Extensions.html>.

## 1. Two tools, one discipline: use them where they remove code, not where they add it

MediatR and AutoMapper are the two most over-used libraries in the .NET ecosystem, and they are over-used for the same reason: a tutorial showed them wrapping *everything*, and "wrap everything" is easy to apply without thinking. This lecture is about thinking. The discipline is identical for both: **introduce the tool exactly where it removes duplication, and refuse it everywhere else.** For MediatR, "where it removes duplication" is cross-cutting concerns expressed as pipeline behaviors. For AutoMapper, it is mechanical, name-matched DTO projection. Outside those two cases, both tools *add* indirection that a reviewer has to chase, and the harden theme — hardening is editing — says we do not pay indirection we did not earn.

## 2. MediatR — what it actually is, and what it is not

MediatR is a small library: you send an `IRequest<TResponse>` to an `IMediator`, and it finds the one `IRequestHandler<TRequest, TResponse>` registered for that request type and invokes it. That is the "mediator" part, and on its own it is nearly worthless — it replaces a direct method call with a reflection-based dispatch and an extra type. Jimmy Bogard, MediatR's author, says exactly this in "you probably don't need MediatR" (<https://www.jimmybogard.com/you-probably-dont-need-mediatr/>): if all you want is to call a handler, call the handler.

The value is the **pipeline**. Every request that flows through `IMediator.Send` passes through an ordered chain of `IPipelineBehavior<TRequest, TResponse>` implementations before reaching the handler, and back out after. A behavior is a piece of middleware *for your application logic* — it runs for every request, in order, and it is the place a cross-cutting concern lives once instead of being copy-pasted into every handler. The signature (<https://github.com/jbogard/MediatR/wiki/Behaviors>):

```csharp
public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
```

`next()` calls the next behavior in the chain (or the handler, at the end). Behaviors run outside-in on the way down and inside-out on the way back, exactly like ASP.NET Core middleware. **That** is what earns its keep: validation, authorization, logging, and transaction scoping become four behaviors that run once for every request, and the per-feature handler shrinks to the business logic alone.

### 2.1 The decision rule, stated plainly

> Introduce a MediatR request/handler pair for a feature **only if** that feature benefits from at least one pipeline behavior you have. If the feature is a one-line endpoint with no cross-cutting concern, a Minimal API handler calling a service is simpler — keep it.

In the Polyglot Workshop the behaviors that earn MediatR are exactly three: **validation**, **authorization**, and **transaction/outbox scoping**. Every command that writes data benefits from all three. Every query benefits from validation and authorization. That is the entire write surface and most of the read surface of the workshop — so MediatR pays off here. A toy CRUD app with no validation, no per-object authorization, and no outbox would *not* benefit, and wrapping it in MediatR would be the over-use the author warns against.

## 3. The three behaviors that earn their keep

### 3.1 ValidationBehavior — FluentValidation, once

Without a behavior, every handler starts with `if (string.IsNullOrWhiteSpace(...)) return BadRequest(...)`. With a behavior, validation runs before the handler, for every request, and the handler assumes valid input. FluentValidation (<https://docs.fluentvalidation.net/en/latest/aspnet.html#manual-validation>) provides the validators; the behavior runs all registered validators for the request type:

```csharp
#nullable enable
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
            var failures = (await Task.WhenAll(
                    validators.Select(v => v.ValidateAsync(context, cancellationToken))))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
            {
                // Thrown once here; translated to RFC 9457 ProblemDetails by a single
                // exception handler at the boundary. No handler repeats this.
                throw new ValidationException(failures);
            }
        }

        return await next();
    }
}
```

The validator for a command is a plain `AbstractValidator<T>`:

```csharp
public sealed class SubmitExerciseValidator : AbstractValidator<SubmitExerciseCommand>
{
    public SubmitExerciseValidator()
    {
        RuleFor(c => c.LessonId).NotEmpty();
        RuleFor(c => c.Content).NotEmpty().MaximumLength(50_000);
    }
}
```

The `ValidationException` is translated once, at the boundary, into an RFC 9457 ProblemDetails response (<https://www.rfc-editor.org/rfc/rfc9457.html>):

```csharp
app.UseExceptionHandler(eh => eh.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is ValidationException ve)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new HttpValidationProblemDetails(
            ve.Errors.GroupBy(e => e.PropertyName)
                     .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        });
    }
}));
```

That is the *deletion* the theme promises: every `BadRequest(...)` scattered through the handlers is gone, replaced by one behavior and one translator.

### 3.2 AuthorizationBehavior — the resource-based check from Lecture 1, once

Lecture 1 put `IAuthorizationService.AuthorizeAsync(user, resource, policy)` inside each endpoint. With MediatR you can express authorization as part of the request and run it in a behavior — so a command that touches a `Submission` declares the requirement and the behavior enforces it before the handler runs:

```csharp
// A request can opt into resource-based authorization by carrying the resource
// loader and the policy. The behavior runs the check; the handler never sees an
// unauthorized request.
public interface IAuthorizedRequest
{
    string Policy { get; }
    Task<object?> LoadResourceAsync(IServiceProvider sp, CancellationToken ct);
}

public sealed class AuthorizationBehavior<TRequest, TResponse>(
    IHttpContextAccessor http,
    IAuthorizationService authz,
    IServiceProvider sp)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IAuthorizedRequest authorized)
        {
            var user = http.HttpContext?.User
                ?? throw new InvalidOperationException("No user on the request.");
            var resource = await authorized.LoadResourceAsync(sp, cancellationToken);
            if (resource is null)
            {
                throw new NotFoundException();   // -> 404 at the boundary
            }

            var result = await authz.AuthorizeAsync(user, resource, authorized.Policy);
            if (!result.Succeeded)
            {
                throw new ForbiddenException();   // -> 404 or 403 per Lecture 1's rule
            }
        }

        return await next();
    }
}
```

The reward is the re-use Lecture 1 promised: the *same* `SubmissionOwnerHandler` enforces the *same* policy whether the request arrives over HTTP, over gRPC, or through a background job, because the check lives in the authorization service and the behavior calls it once.

### 3.3 TransactionBehavior — open, run, enqueue the outbox, commit

The workshop uses the outbox pattern (Week 8): a write and its side-effect event commit in the same database transaction, and a background worker publishes the event afterward. Without a behavior, every command handler opens a transaction, does its work, writes the outbox row, and commits — four lines of identical ceremony per handler. With a behavior, the handler just does its work and the behavior owns the transaction:

```csharp
public sealed class TransactionBehavior<TRequest, TResponse>(
    WorkshopDbContext db)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand   // marker: only commands are transactional; queries skip this
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await next();   // already in a transaction (nested send); don't double-open
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var response = await next();              // handler does its work, adds outbox rows
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);  // atomic: state change + outbox event together
        return response;
    }
}
```

The `where TRequest : ICommand` constraint is the load-bearing detail: MediatR will only construct this behavior for requests that implement `ICommand`, so queries flow straight through without a pointless transaction. That is the kind of precision the deliberate-MediatR posture demands — a behavior that runs for *exactly* the requests that need it, not for all of them.

### 3.4 Registration and ordering

Behaviors run in registration order, outside-in. The order matters: validate *before* you authorize (no point checking ownership of a malformed id), authorize *before* you transact (no point opening a transaction for a request you will reject):

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(SubmitExerciseCommand).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(SubmitExerciseValidator).Assembly);
```

A request now flows: **validate → authorize → transaction → handler → back out**. The handler is pure business logic. That is the whole argument for MediatR in three behaviors, and the harden diff that introduces it *removes* the validation, authorization, and transaction lines from every handler it touches.

### 3.5 Testing a behavior in isolation

A behavior is just a class with a `Handle` method, so you unit-test it without a web host. The test that matters most for the harden milestone is the one proving `TransactionBehavior` rolls back *both* the entity and the outbox row when the handler throws — because that atomicity is the whole reason the outbox pattern is correct:

```csharp
[Fact]
public async Task Transaction_behavior_rolls_back_entity_and_outbox_on_handler_failure()
{
    await using var db = NewSqliteContext();          // a real, in-memory transactional store
    var behavior = new TransactionBehavior<FailingCommand, Unit>(db);

    RequestHandlerDelegate<Unit> failingHandler = _ =>
    {
        db.Submissions.Add(new Submission { Id = Guid.NewGuid() });
        db.OutboxMessages.Add(OutboxMessage.For(new SubmissionReceived(/* ... */)));
        throw new InvalidOperationException("boom after the adds, before commit");
    };

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        behavior.Handle(new FailingCommand(), failingHandler, default));

    // Neither row survived: the transaction never committed.
    (await db.Submissions.CountAsync()).Should().Be(0);
    (await db.OutboxMessages.CountAsync()).Should().Be(0);
}
```

The same shape tests `ValidationBehavior` (assert it throws `ValidationException` and never calls `next`) and `AuthorizationBehavior` (assert it throws `ForbiddenException` for a non-owner). These are fast, host-free unit tests; the *integration* tests in Exercise 1 prove the behaviors are actually wired into the running pipeline. You want both: the unit test catches a logic bug in the behavior; the integration test catches a registration bug (the behavior that was written but never added to the pipeline).

### 3.6 A note on notifications — the place MediatR also earns its keep

Commands and queries are the request/response side of MediatR. The other side is `INotification` — a fire-and-forget event published to *zero or more* `INotificationHandler<T>`s. This is the natural shape for the outbox's downstream effects: when the `TransactionBehavior` commits a `SubmissionReceived` outbox row, the background worker (Week 8) reads it and publishes a `SubmissionReceivedNotification`, and *several* independent handlers react — one updates the analytics projection, one notifies the instructor via SignalR, one re-indexes the submission for search. None of them know about each other, and adding a fourth reaction is adding one handler class, not editing a god-method. That decoupling is a second, legitimate MediatR win — but note the discipline still applies: a notification with exactly one handler that you will never add a second to is just a method call wearing a costume. Use notifications when the *fan-out* is real. Citation: <https://github.com/jbogard/MediatR/wiki#notifications>.

## 4. The endpoint shrinks to a one-liner

With the pipeline in place, the Minimal API endpoint is a thin adapter that builds the request and sends it:

```csharp
app.MapPost("/api/lessons/{lessonId:guid}/submissions",
    async (Guid lessonId, SubmitExerciseRequest body, IMediator mediator) =>
{
    var id = await mediator.Send(new SubmitExerciseCommand(lessonId, body.Content));
    return Results.Created($"/api/submissions/{id}", new { id });
})
.RequireAuthorization();
```

No validation block, no authorization call, no transaction management — those are in the pipeline. The three near-identical "submit / resubmit / submit-on-behalf" endpoints that used to copy-paste all of that collapse to three thin adapters over three commands sharing the same three behaviors. That collapse is Exercise 2.

## 5. AutoMapper — the honest case and the dishonest case

AutoMapper maps one type to another by matching property names. The **honest case** is a name-matched DTO projection: an entity and a DTO whose properties are a name-matched subset, mapped the same way in many places. The **dishonest case** is anything with logic — conditional mapping, flattening that needs a join, security-sensitive field stripping, or any mapping a reviewer cannot predict from the type shapes. The rule:

> **AutoMapper where the mapping is obvious from the names; a hand-written method where it is not.**

### 5.1 The honest case — `ProjectTo` pushes projection into SQL

The killer feature is `ProjectTo<TDto>` (<https://docs.automapper.org/en/stable/Queryable-Extensions.html>). Given an `IQueryable<Submission>`, it builds an `IQueryable<SubmissionDto>` whose `SELECT` lists *only the DTO's columns* — so the database never reads `InternalNotes` or `LearnerEmail` off disk:

```csharp
public sealed class WorkshopMappingProfile : Profile
{
    public WorkshopMappingProfile()
    {
        // Name-matched, logic-free. A reviewer sees the DTO shape and knows the output.
        CreateMap<Submission, SubmissionDto>();
        CreateMap<Lesson, LessonSummaryDto>();
        CreateMap<Enrollment, EnrollmentDto>();
    }
}

// In the query:
var page = await mapper.ProjectTo<SubmissionDto>(
        db.Submissions.Where(s => s.LessonId == lessonId).OrderBy(s => s.SubmittedAtUtc))
    .Skip(offset).Take(pageSize)
    .ToListAsync(ct);
```

The generated SQL is `SELECT s."Id", s."LessonId", s."Grade", s."SubmittedAtUtc" FROM ...` — the four DTO columns, nothing more. That is a BOPLA mitigation (Lecture 1) *and* a performance win in one call, and it replaces the fifteen hand-written `new SubmissionDto { ... }` constructions scattered through the read endpoints. That deletion is Exercise 3.

### 5.2 Validate the configuration in a test

The risk with AutoMapper is a silent partial mapping — a new DTO property nobody mapped, defaulting to `null`. Close it with `AssertConfigurationIsValid()` in a test (<https://docs.automapper.org/en/stable/Configuration-validation.html>):

```csharp
[Fact]
public void Mapping_configuration_is_valid()
{
    var config = new MapperConfiguration(c => c.AddProfile<WorkshopMappingProfile>());
    config.AssertConfigurationIsValid();   // fails the build if any DTO property is unmapped
}
```

This test is the price of admission for using AutoMapper at all. Without it, AutoMapper trades compile-time safety for runtime surprises; with it, an unmapped property fails CI.

### 5.3 The three mappings the workshop must NOT give AutoMapper

Three mappings in the workshop carry logic, and all three stay hand-written:

1. **`Submission` → `SubmissionGradeReportDto`.** The report flattens the submission, its lesson title (a join), and a *computed* `PercentOfClassMedian` field. AutoMapper can be coerced into this with custom resolvers, but the resolver is harder to read than the method, and the computation is testable in isolation only if it is a method. Hand-write it.
2. **`User` → `ProfileDto` with role-conditional fields.** An instructor viewing their own profile sees `EmailVerifiedAtUtc`; a learner viewing another user's profile sees only `DisplayName`. The output depends on *who is asking* — that is authorization logic masquerading as mapping, and it must be explicit. Hand-write it, and unit-test both branches.
3. **The inbound `CreateLessonRequest` → `Lesson` entity.** This sets `TenantId` from the *caller's claim*, not from the request body (mass-assignment defense, Lecture 1). A mapper that reads `TenantId` from the source would be a security hole. Hand-write it, and let the absence of a `CreateMap` for this direction be the thing that stops anyone from binding it accidentally.

The pattern: if a reviewer cannot look at the source and target types and predict the output, it does not belong in AutoMapper. Exercise 3 has you find these three in the workshop and confirm they stay hand-written, while everything mechanical moves to `ProjectTo`.

## 6. The harden diff is net-negative — that is the point

Tally the lecture's changes to the workshop. **Added:** four small behavior classes, a handful of `AbstractValidator<T>`s, one `Profile` with name-matched maps, one config-validation test. **Removed:** the validation blocks from every handler, the authorization calls from every endpoint, the transaction ceremony from every command, the fifteen hand-written DTO constructions, and the three duplicated submit endpoints collapsed to one command each. The net is fewer lines, less duplication, and a single place to change each cross-cutting concern. That is what "hardening is editing" means in practice: you did not add MediatR and AutoMapper to *do more*; you added them to *write the same thing once*, and then you deleted the copies.

What is still missing is the ability to *see* a request flow through this pipeline in production — which behavior ran, how long the handler took, what the database did. That is Lecture 3: the three observability signals, the OpenTelemetry SDK, and the local Grafana + Loki + Tempo stack that lets you watch a single request cross every one of these layers.

Citations for this lecture: MediatR behaviors at <https://github.com/jbogard/MediatR/wiki/Behaviors>; "you probably don't need MediatR" at <https://www.jimmybogard.com/you-probably-dont-need-mediatr/>; FluentValidation at <https://docs.fluentvalidation.net/en/latest/>; RFC 9457 ProblemDetails at <https://www.rfc-editor.org/rfc/rfc9457.html>; AutoMapper queryable extensions at <https://docs.automapper.org/en/stable/Queryable-Extensions.html>; and AutoMapper configuration validation at <https://docs.automapper.org/en/stable/Configuration-validation.html>.
