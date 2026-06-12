// Exercise 1 — Close a BOLA hole and prove it with an integration test.
//
// Goal: the workshop's GET /api/submissions/{id} endpoint currently returns any
// submission to any authenticated learner (OWASP API1, Broken Object Level
// Authorization). You will:
//   (a) add a SubmissionOwnerRequirement + SubmissionOwnerHandler (resource-based authz),
//   (b) wire the endpoint to check ownership before returning,
//   (c) write an integration test (WebApplicationFactory + Testcontainers Keycloak)
//       that proves BOTH paths: alice CAN read her own submission (allow),
//       and alice CANNOT read bob's submission (deny -> 404, not the object).
//
// This is the heart of the week's milestone: every object-by-id endpoint gets a
// resource-based check AND a deny-path test. Citations:
//   BOLA:           https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/
//   Resource authz: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased
//   Integration:    https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
//   Testcontainers Keycloak: https://dotnet.testcontainers.org/modules/keycloak/
//
// Project layout (extends your week-13 capstone solution):
//
//   src/Workshop.Api/
//     Authorization/SubmissionOwnerRequirement.cs   <-- PART 1 (this file)
//     Authorization/SubmissionOwnerHandler.cs        <-- PART 2 (this file)
//     Endpoints/SubmissionEndpoints.cs               <-- PART 3 (this file)
//   tests/Workshop.IntegrationTests/
//     WorkshopApiFactory.cs                          <-- PART 4 (given; from week 13)
//     SubmissionBolaTests.cs                         <-- PART 5 (this file) <- YOU WRITE THIS

#nullable enable

// ============================================================================
// PART 1 — SubmissionOwnerRequirement.cs
// ============================================================================

using Microsoft.AspNetCore.Authorization;

namespace Workshop.Api.Authorization;

// A marker requirement carries no data; the handler holds the logic.
public sealed class SubmissionOwnerRequirement : IAuthorizationRequirement;

// ============================================================================
// PART 2 — SubmissionOwnerHandler.cs
// ============================================================================
//
// using System.Security.Claims;
// using Microsoft.AspNetCore.Authorization;
// using Workshop.Domain;
//
// namespace Workshop.Api.Authorization;
//
// public sealed class SubmissionOwnerHandler
//     : AuthorizationHandler<SubmissionOwnerRequirement, Submission>
// {
//     protected override Task HandleRequirementAsync(
//         AuthorizationHandlerContext context,
//         SubmissionOwnerRequirement requirement,
//         Submission resource)
//     {
//         string? userId  = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
//         string? tenant  = context.User.FindFirstValue("tenant");
//         bool isInstructor = context.User.IsInRole("instructor");
//
//         bool owns      = userId is not null && resource.LearnerId == userId;
//         bool canModerate = isInstructor && resource.TenantId == tenant;
//
//         if (owns || canModerate)
//         {
//             context.Succeed(requirement);
//         }
//         // No context.Fail(): a soft miss lets other handlers (if any) still succeed.
//         return Task.CompletedTask;
//     }
// }

// ============================================================================
// PART 3 — SubmissionEndpoints.cs (the fixed endpoint)
// ============================================================================
//
// using System.Security.Claims;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.EntityFrameworkCore;
// using Workshop.Api.Authorization;
// using Workshop.Domain;
// using Workshop.Infrastructure;
//
// namespace Workshop.Api.Endpoints;
//
// public static class SubmissionEndpoints
// {
//     public static void MapSubmissionEndpoints(this IEndpointRouteBuilder app)
//     {
//         app.MapGet("/api/submissions/{id:guid}", GetSubmission)
//            .RequireAuthorization();   // authenticated; ownership checked in the handler
//     }
//
//     private static async Task<IResult> GetSubmission(
//         Guid id,
//         WorkshopDbContext db,
//         IAuthorizationService authz,
//         ClaimsPrincipal user,
//         CancellationToken ct)
//     {
//         var submission = await db.Submissions
//             .AsNoTracking()
//             .FirstOrDefaultAsync(s => s.Id == id, ct);
//
//         if (submission is null)
//         {
//             return Results.NotFound();
//         }
//
//         var result = await authz.AuthorizeAsync(user, submission, "SubmissionOwner");
//         if (!result.Succeeded)
//         {
//             // 404, not 403: do not confirm the object exists to a caller who must
//             // not even know its id is valid. (OWASP BOLA guidance.)
//             return Results.NotFound();
//         }
//
//         return Results.Ok(submission.ToDto());   // DTO allow-list, not the entity (BOPLA)
//     }
// }
//
// // Registration in Program.cs:
// //   builder.Services.AddScoped<IAuthorizationHandler, SubmissionOwnerHandler>();
// //   builder.Services.AddAuthorizationBuilder()
// //       .AddPolicy("SubmissionOwner", p => p.AddRequirements(new SubmissionOwnerRequirement()));

// ============================================================================
// PART 4 — WorkshopApiFactory.cs (GIVEN — carried from week 13; shown for context)
// ============================================================================
//
// using DotNet.Testcontainers.Builders;
// using Microsoft.AspNetCore.Mvc.Testing;
// using Microsoft.Extensions.DependencyInjection;
// using Testcontainers.Keycloak;
// using Testcontainers.PostgreSql;
//
// public sealed class WorkshopApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
// {
//     private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
//         .WithImage("postgres:16").Build();
//     private readonly KeycloakContainer _kc = new KeycloakBuilder()
//         .WithImage("quay.io/keycloak/keycloak:25.0").Build();
//
//     public async Task InitializeAsync()
//     {
//         await _pg.StartAsync();
//         await _kc.StartAsync();
//         await KeycloakSeeder.SeedRealmAsync(_kc.GetBaseAddress());  // realm, clients, users
//     }
//
//     protected override void ConfigureWebHost(IWebHostBuilder builder)
//     {
//         builder.UseSetting("ConnectionStrings:Workshop", _pg.GetConnectionString());
//         builder.UseSetting("Oidc:Authority", $"{_kc.GetBaseAddress()}/realms/workshop");
//     }
//
//     // Mints a real access token from the Testcontainers Keycloak via the
//     // resource-owner-password flow (dev realm only). Returns the bearer string.
//     public Task<string> TokenForAsync(string username, string password = "pw") =>
//         KeycloakSeeder.PasswordGrantAsync(_kc.GetBaseAddress(), username, password);
//
//     public new async Task DisposeAsync()
//     {
//         await _pg.DisposeAsync();
//         await _kc.DisposeAsync();
//     }
// }

// ============================================================================
// PART 5 — SubmissionBolaTests.cs   <-- YOU WRITE THIS
// ============================================================================
//
// The shape is given; fill in the asserts. Three principals are seeded in the
// Keycloak realm: "alice" and "bob" (learners), "carol" (instructor). The
// database is seeded with one submission owned by bob in lesson L1.

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Workshop.IntegrationTests;

public sealed class SubmissionBolaTests : IClassFixture<WorkshopApiFactory>
{
    private readonly WorkshopApiFactory _factory;

    public SubmissionBolaTests(WorkshopApiFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientFor(string user)
    {
        var client = _factory.CreateClient();
        var token = await _factory.TokenForAsync(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ALLOW PATH: bob reads his own submission -> 200.
    [Fact]
    public async Task Owner_can_read_own_submission()
    {
        var bob = await ClientFor("bob");
        var bobsSubmissionId = await SeedHelper.GetBobsSubmissionIdAsync(_factory);

        var response = await bob.GetAsync($"/api/submissions/{bobsSubmissionId}");

        // TODO(you): assert 200 OK and that the body is the SubmissionDto (no
        //            InternalNotes / LearnerEmail fields present).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("InternalNotes");
        json.Should().NotContain("LearnerEmail");
    }

    // DENY PATH (BOLA): alice tries to read bob's submission -> 404, NOT the object.
    [Fact]
    public async Task Non_owner_cannot_read_others_submission_and_gets_404_not_the_object()
    {
        var alice = await ClientFor("alice");
        var bobsSubmissionId = await SeedHelper.GetBobsSubmissionIdAsync(_factory);

        var response = await alice.GetAsync($"/api/submissions/{bobsSubmissionId}");

        // TODO(you): assert 404 (not 403, not 200). A 403 would confirm the id is
        //            valid; the OWASP BOLA guidance prefers 404 for object denials.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("InternalNotes");   // the object must not leak in the deny body
    }

    // DENY PATH (auth): an anonymous caller -> 401 on the authenticated endpoint.
    [Fact]
    public async Task Anonymous_caller_is_rejected_with_401()
    {
        var anon = _factory.CreateClient();   // no Authorization header
        var bobsSubmissionId = await SeedHelper.GetBobsSubmissionIdAsync(_factory);

        var response = await anon.GetAsync($"/api/submissions/{bobsSubmissionId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ALLOW PATH (moderation): carol, an instructor in the same tenant, may read it.
    [Fact]
    public async Task Instructor_in_same_tenant_can_moderate_a_submission()
    {
        var carol = await ClientFor("carol");
        var bobsSubmissionId = await SeedHelper.GetBobsSubmissionIdAsync(_factory);

        var response = await carol.GetAsync($"/api/submissions/{bobsSubmissionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

// ============================================================================
// COMMANDS — what you actually run
// ============================================================================
//
//   # Run only the BOLA tests (spins up Testcontainers Postgres + Keycloak):
//   dotnet test tests/Workshop.IntegrationTests \
//       --filter "FullyQualifiedName~SubmissionBolaTests"
//
//   # Expected: 4 passed. If "Non_owner_cannot_read..." FAILS with 200 OK, the
//   # resource-based check is not wired — re-check the AddPolicy name and the
//   # AddScoped<IAuthorizationHandler, SubmissionOwnerHandler>() registration.

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] Owner_can_read_own_submission                          -> 200, no leaked fields.
//   [ ] Non_owner_cannot_read_others_submission_..._404        -> 404 (NOT 403, NOT 200).
//   [ ] Anonymous_caller_is_rejected_with_401                  -> 401.
//   [ ] Instructor_in_same_tenant_can_moderate_a_submission    -> 200.
//   [ ] You added a row to THREATMODEL.md mapping the deny test to OWASP API1.
//
// Stretch (counted toward Exercise 1 if you finish the above with time left):
//   1. Add a cross-TENANT instructor (dave, instructor in tenant-2) and assert he
//      gets 404 on bob's (tenant-1) submission — instructor role is not a global pass.
//   2. Add the SAME resource-based check to the gRPC GetSubmission method and write
//      a gRPC deny-path test asserting StatusCode.NotFound for alice-reads-bob.
