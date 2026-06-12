// Polyglot Workshop — Authorization scaffolding (starter)
//
// This file ships the requirement/handler shapes and the policy registration so
// you spend your hours on the deny-path TESTS and the THREATMODEL, not on the
// ceremony. The handler BODIES contain the real logic; fill in the TODOs where a
// judgment call is yours to make (the tenant-moderation rule, the 403-vs-404 rule).
//
// Citations:
//   Resource-based authz: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased
//   Policies:             https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies

#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Workshop.Domain;

namespace Workshop.Api.Authorization;

// ---- Requirements (marker types; the logic is in the handlers) --------------

public sealed class SubmissionOwnerRequirement : IAuthorizationRequirement;
public sealed class LessonInstructorRequirement : IAuthorizationRequirement;

// ---- Handlers ---------------------------------------------------------------

public sealed class SubmissionOwnerHandler
    : AuthorizationHandler<SubmissionOwnerRequirement, Submission>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SubmissionOwnerRequirement requirement,
        Submission resource)
    {
        string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? tenant = context.User.FindFirstValue("tenant");
        bool isInstructor = context.User.IsInRole("instructor");

        bool owns = userId is not null && resource.LearnerId == userId;

        // TODO(you): an instructor may moderate submissions in their OWN tenant only.
        // A cross-tenant instructor must NOT be able to read another tenant's data.
        bool canModerate = isInstructor && resource.TenantId == tenant;

        if (owns || canModerate)
        {
            context.Succeed(requirement);
        }

        // Deliberately NO context.Fail(): a soft miss lets other handlers succeed.
        return Task.CompletedTask;
    }
}

public sealed class LessonInstructorHandler
    : AuthorizationHandler<LessonInstructorRequirement, Lesson>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        LessonInstructorRequirement requirement,
        Lesson resource)
    {
        string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? tenant = context.User.FindFirstValue("tenant");

        if (context.User.IsInRole("instructor") &&
            resource.TenantId == tenant &&
            resource.InstructorId == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// ---- Registration extension --------------------------------------------------

public static class AuthorizationRegistration
{
    public static IServiceCollection AddWorkshopAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, SubmissionOwnerHandler>();
        services.AddScoped<IAuthorizationHandler, LessonInstructorHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy("SubmissionOwner",
                p => p.AddRequirements(new SubmissionOwnerRequirement()))
            .AddPolicy("LessonInstructor",
                p => p.AddRequirements(new LessonInstructorRequirement()))
            .AddPolicy("InstructorOnly",
                p => p.RequireRole("instructor"))
            // Deny-by-default: an endpoint with no explicit policy still needs auth.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
