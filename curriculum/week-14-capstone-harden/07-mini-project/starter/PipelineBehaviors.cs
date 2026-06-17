// Polyglot Workshop — MediatR pipeline behaviors (starter)
//
// The three behaviors that earn MediatR its keep in the workshop: validation,
// authorization, and transaction/outbox scoping. Registered in order:
//   validate -> authorize -> transaction -> handler.
// The handler bodies (in src/Workshop.Application/<feature>/) are business logic
// only; these behaviors carry the cross-cutting concerns.
//
// Citations:
//   Behaviors:        https://github.com/jbogard/MediatR/wiki/Behaviors
//   FluentValidation: https://docs.fluentvalidation.net/en/latest/
//   Outbox (week 8):  the transaction commits state + outbox row atomically.

#nullable enable
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Workshop.Application.Abstractions;     // ICommand, IAuthorizedRequest, app exceptions
using Workshop.Infrastructure;               // WorkshopDbContext

namespace Workshop.Application.Behaviors;

// ---- Validation --------------------------------------------------------------

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count != 0)
            {
                throw new ValidationException(failures);   // -> RFC 9457 at the boundary
            }
        }
        return await next();
    }
}

// ---- Authorization (resource-based, reusing the handlers in Authorization.cs) -

public sealed class AuthorizationBehavior<TRequest, TResponse>(
    IHttpContextAccessor http,
    IAuthorizationService authz,
    IServiceProvider services)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IAuthorizedRequest authorized)
        {
            var user = http.HttpContext?.User
                ?? throw new InvalidOperationException("No authenticated user on the request.");

            var resource = await authorized.LoadResourceAsync(services, ct);
            if (resource is null)
            {
                throw new NotFoundException();   // -> 404
            }

            var result = await authz.AuthorizeAsync(user, resource, authorized.Policy);
            if (!result.Succeeded)
            {
                // TODO(you): map to 404 for secret-existence objects, 403 for public ones.
                throw new ForbiddenException();
            }
        }
        return await next();
    }
}

// ---- Transaction + outbox (commands only) -----------------------------------

public sealed class TransactionBehavior<TRequest, TResponse>(WorkshopDbContext db)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand          // constraint: queries never construct this behavior
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await next();        // nested send; don't double-open
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var response = await next();    // handler adds the entity AND the outbox row
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);       // atomic: state change + event together
        return response;
    }
}

// ---- Registration ------------------------------------------------------------

public static class MediatrRegistration
{
    public static IServiceCollection AddWorkshopMediatr(
        this IServiceCollection services, System.Reflection.Assembly applicationAssembly)
    {
        services.AddHttpContextAccessor();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            // ORDER IS THE PIPELINE: validate -> authorize -> transaction -> handler.
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        });
        services.AddValidatorsFromAssembly(applicationAssembly);
        return services;
    }
}
