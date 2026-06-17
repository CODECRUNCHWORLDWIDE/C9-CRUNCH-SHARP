// Exercise 3 — Policy-Based Authorization with Custom Handlers
//
// Goal: Write a custom IAuthorizationRequirement + AuthorizationHandler<T>
//       pair that enforces "the user's account must be at least N days old."
//       Wire it as a named policy. Write three integration tests with
//       WebApplicationFactory<T> and a TestAuthHandler that prove (a) a
//       new account is denied, (b) an old account is allowed, (c) an
//       anonymous caller is rejected before the policy runs.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh solution with two projects: the web API and the test
//    project.
//
//      mkdir PolicyHandlers && cd PolicyHandlers
//      dotnet new sln -n PolicyHandlers
//      dotnet new web   -n PolicyHandlers.Api   -o src/PolicyHandlers.Api
//      dotnet new xunit -n PolicyHandlers.Tests -o tests/PolicyHandlers.Tests
//      dotnet sln add src/PolicyHandlers.Api tests/PolicyHandlers.Tests
//      dotnet add tests/PolicyHandlers.Tests reference src/PolicyHandlers.Api
//      dotnet add tests/PolicyHandlers.Tests package Microsoft.AspNetCore.Mvc.Testing
//
//    Replace src/PolicyHandlers.Api/Program.cs with the contents of THIS
//    FILE up to and including the `app.Run();` line. Put the test class
//    (the bottom half of this file) at tests/PolicyHandlers.Tests/Tests.cs.
//
// 2. Fill in the four TODOs below.
//
// 3. Run the tests:
//
//      dotnet test
//
//    All three tests should pass.
//
// ACCEPTANCE CRITERIA
//
//   [ ] MinimumAccountAgeRequirement is an `IAuthorizationRequirement` with
//       a `MinDays` property set via the constructor.
//   [ ] MinimumAccountAgeHandler is an `AuthorizationHandler<MinimumAccountAgeRequirement>`
//       that reads the `account_created_at` claim, compares to clock.UtcNow,
//       and calls `context.Succeed(requirement)` ONLY when the age is sufficient.
//   [ ] The handler does NOT throw on missing or malformed claims — it
//       silently fails (returns without calling Succeed).
//   [ ] The handler takes IClock through its constructor (so tests can inject
//       a deterministic time).
//   [ ] AddAuthorization registers a policy "MinAge30" that adds the
//       requirement with MinDays = 30.
//   [ ] The /verified endpoint is `[Authorize(Policy = "MinAge30")]`.
//   [ ] The test project uses a custom AuthenticationHandler ("Test") that
//       reads X-Test-User and X-Test-AccountAgeDays headers.
//   [ ] Three tests pass: OldAccount→200, NewAccount→403, Anonymous→401.
//   [ ] dotnet build: 0 Warning(s), 0 Error(s).
//
// SMOKE OUTPUT (target)
//
//   $ dotnet test
//   Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints at the bottom of the file.

// === src/PolicyHandlers.Api/Program.cs ===

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// TODO 1 — Define MinimumAccountAgeRequirement and the IClock abstraction.
//
// public sealed class MinimumAccountAgeRequirement(int minDays) : IAuthorizationRequirement
// {
//     public int MinDays { get; } = minDays;
// }
//
// public interface IClock { DateTime UtcNow { get; } }
// public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
// ---------------------------------------------------------------------------

// YOUR CODE HERE (see Hint 1 — both class declarations go at the bottom of
// the file, AFTER `app.Run();` and the LoginRequest record.)

// ---------------------------------------------------------------------------
// TODO 2 — Implement MinimumAccountAgeHandler.
//
// public sealed class MinimumAccountAgeHandler(IClock clock)
//     : AuthorizationHandler<MinimumAccountAgeRequirement>
// {
//     protected override Task HandleRequirementAsync(
//         AuthorizationHandlerContext context,
//         MinimumAccountAgeRequirement requirement)
//     {
//         var claim = context.User.FindFirst("account_created_at");
//         if (claim is null) return Task.CompletedTask;
//         if (!DateTime.TryParse(claim.Value, null,
//                 System.Globalization.DateTimeStyles.RoundtripKind,
//                 out var createdAt))
//             return Task.CompletedTask;
//
//         var age = clock.UtcNow - createdAt;
//         if (age.TotalDays >= requirement.MinDays)
//             context.Succeed(requirement);
//
//         return Task.CompletedTask;
//     }
// }
// ---------------------------------------------------------------------------

// YOUR CODE HERE (also at the bottom of the file.)

// ---------------------------------------------------------------------------
// TODO 3 — Register the services and the policy.
//
// builder.Services.AddSingleton<IClock, SystemClock>();
// builder.Services.AddSingleton<IAuthorizationHandler, MinimumAccountAgeHandler>();
//
// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
//     .AddCookie();
//
// builder.Services.AddAuthorization(options =>
// {
//     options.AddPolicy("MinAge30", policy =>
//         policy.AddRequirements(new MinimumAccountAgeRequirement(30)));
// });
// ---------------------------------------------------------------------------

// YOUR CODE HERE

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// TODO 4 — Map /verified, protected by the policy. Map /health for sanity.
//
// app.MapGet("/health", () => Results.Ok("ok"));
//
// app.MapGet("/verified", (HttpContext ctx) =>
// {
//     var name = ctx.User.Identity?.Name ?? "anonymous";
//     return Results.Ok(new { name, message = "your account is old enough" });
// })
// .RequireAuthorization("MinAge30");
// ---------------------------------------------------------------------------

// YOUR CODE HERE

app.Run();

// Make the Program class accessible to WebApplicationFactory<Program>.
public partial class Program;

// ============================================================================
// === tests/PolicyHandlers.Tests/Tests.cs ===
// ============================================================================
//
// using System.Net;
// using System.Security.Claims;
// using System.Text.Encodings.Web;
// using Microsoft.AspNetCore.Authentication;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Hosting;
// using Microsoft.AspNetCore.Mvc.Testing;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
//
// public sealed class FixedClock(DateTime utcNow) : IClock
// {
//     public DateTime UtcNow { get; } = utcNow;
// }
//
// public sealed class TestAuthHandler(
//     IOptionsMonitor<AuthenticationSchemeOptions> options,
//     ILoggerFactory logger,
//     UrlEncoder encoder)
//     : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
// {
//     protected override Task<AuthenticateResult> HandleAuthenticateAsync()
//     {
//         if (!Request.Headers.TryGetValue("X-Test-User", out var userValues))
//             return Task.FromResult(AuthenticateResult.NoResult());
//
//         var claims = new List<Claim>
//         {
//             new(ClaimTypes.NameIdentifier, userValues.ToString()),
//             new(ClaimTypes.Name,           userValues.ToString()),
//         };
//
//         if (Request.Headers.TryGetValue("X-Test-AccountCreatedAt", out var createdValues))
//             claims.Add(new Claim("account_created_at", createdValues.ToString()));
//
//         var identity  = new ClaimsIdentity(claims, Scheme.Name);
//         var principal = new ClaimsPrincipal(identity);
//         var ticket    = new AuthenticationTicket(principal, Scheme.Name);
//         return Task.FromResult(AuthenticateResult.Success(ticket));
//     }
// }
//
// public sealed class PolicyHandlerFactory : WebApplicationFactory<Program>
// {
//     public DateTime NowUtc { get; set; } = new(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);
//
//     protected override void ConfigureWebHost(IWebHostBuilder builder)
//     {
//         builder.ConfigureTestServices(services =>
//         {
//             // Replace the real IClock with a deterministic one for tests.
//             services.AddSingleton<IClock>(_ => new FixedClock(NowUtc));
//
//             services.AddAuthentication("Test")
//                 .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
//             services.Configure<AuthorizationOptions>(options =>
//             {
//                 options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
//                     .RequireAuthenticatedUser()
//                     .Build();
//                 // Re-add the named policy with the Test scheme so it picks up the test identity.
//                 options.AddPolicy("MinAge30", policy =>
//                     policy.AuthenticationSchemes.Add("Test"));
//                 var existing = options.GetPolicy("MinAge30")!;
//                 options.AddPolicy("MinAge30", policy =>
//                 {
//                     policy.AuthenticationSchemes.Add("Test");
//                     policy.AddRequirements(new MinimumAccountAgeRequirement(30));
//                 });
//             });
//         });
//     }
// }
//
// public sealed class VerifiedEndpointTests(PolicyHandlerFactory factory)
//     : IClassFixture<PolicyHandlerFactory>
// {
//     [Fact]
//     public async Task OldAccount_Returns200()
//     {
//         var client = factory.CreateClient();
//         client.DefaultRequestHeaders.Add("X-Test-User", "ada");
//         client.DefaultRequestHeaders.Add(
//             "X-Test-AccountCreatedAt",
//             factory.NowUtc.AddDays(-31).ToString("o"));
//
//         var response = await client.GetAsync("/verified");
//
//         Assert.Equal(HttpStatusCode.OK, response.StatusCode);
//     }
//
//     [Fact]
//     public async Task NewAccount_Returns403()
//     {
//         var client = factory.CreateClient();
//         client.DefaultRequestHeaders.Add("X-Test-User", "fresh");
//         client.DefaultRequestHeaders.Add(
//             "X-Test-AccountCreatedAt",
//             factory.NowUtc.AddDays(-5).ToString("o"));
//
//         var response = await client.GetAsync("/verified");
//
//         Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
//     }
//
//     [Fact]
//     public async Task Anonymous_Returns401()
//     {
//         var client = factory.CreateClient();
//
//         var response = await client.GetAsync("/verified");
//
//         Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
//     }
// }
//
// ============================================================================
// HINTS
// ============================================================================
//
// 1) The Requirement + Handler + IClock classes all go at the BOTTOM of
//    Program.cs (after `app.Run();` and after the `public partial class Program;`
//    line). C# 13 file-scoped top-level statements treat everything after
//    the first using directive as either statements or type declarations;
//    type declarations always come last.
//
// 2) The handler MUST NOT throw on a missing or malformed `account_created_at`
//    claim. Returning without calling `context.Succeed(...)` is how you
//    convey "this requirement is not satisfied." If you throw, the test
//    that asserts 403 will fail with a 500 instead.
//
// 3) The DateTime parse uses `DateTimeStyles.RoundtripKind` because we ship
//    the test value as ISO 8601 with `"o"` formatting. Without
//    RoundtripKind the parser will normalize to local time, which makes
//    the comparison flaky in CI runs on machines in non-UTC timezones.
//
// 4) The test factory replaces the real IClock with a FixedClock so the
//    test is deterministic. The TestAuthHandler reads the
//    X-Test-AccountCreatedAt header and stamps it as the
//    `account_created_at` claim. The handler sees the same claim shape as
//    in production.
//
// 5) The MinAge30 policy is re-added in the test factory because the
//    ConfigureTestServices overlay would otherwise lose the requirement
//    when we change the authentication scheme. The two-step `existing`
//    + re-add pattern is a workaround for the AuthorizationOptions
//    builder's add-only API; the cleaner production form is to register
//    the policy with `AuthenticationSchemes.Add(...)` from the start.
//
// 6) `WebApplicationFactory<Program>` requires `public partial class Program;`
//    at the bottom of Program.cs because top-level statements compile to
//    a `Program` class with `internal` visibility by default. The partial
//    declaration changes it to `public` so the test project can reference
//    it.
//
// 7) The three tests are the canonical authorization-test triplet:
//    happy path (200), policy denial (403), anonymous (401). Every
//    [Authorize(Policy = "...")] endpoint deserves the same three tests
//    in production code.
