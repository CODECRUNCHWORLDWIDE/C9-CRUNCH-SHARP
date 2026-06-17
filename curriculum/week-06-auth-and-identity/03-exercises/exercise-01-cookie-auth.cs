// Exercise 1 — Cookie Authentication in ASP.NET Core 9
//
// Goal: Wire cookie authentication into a small ASP.NET Core 9 minimal API.
//       Add a sign-in endpoint that produces a cookie, a sign-out endpoint
//       that removes it, an authenticated /me endpoint that reads the
//       principal, and a [Authorize]-protected /admin endpoint. By the end
//       you should be able to demonstrate, with curl, that the cookie is
//       set on /auth/login, sent on subsequent requests, and refused
//       (with 401) when missing.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh web project:
//
//      mkdir CookieAuth && cd CookieAuth
//      dotnet new web -n CookieAuth -o src/CookieAuth
//
//    Replace src/CookieAuth/Program.cs with the contents of THIS FILE.
//    No NuGet packages needed — Microsoft.AspNetCore.Authentication.Cookies
//    is in Microsoft.AspNetCore.App (already referenced by `dotnet new web`).
//
// 2. Fill in the four TODOs below.
//
// 3. Run:
//
//      dotnet run --project src/CookieAuth
//
//    Use curl in another terminal:
//
//      # 1) Hit /me without auth — should be 401.
//      curl -i http://localhost:5000/me
//
//      # 2) Sign in — should set a cookie.
//      curl -i -c cookies.txt -X POST http://localhost:5000/auth/login \
//           -H "Content-Type: application/json" \
//           -d '{"username":"ada","password":"correct"}'
//
//      # 3) Hit /me with the cookie — should be 200 and return the name.
//      curl -i -b cookies.txt http://localhost:5000/me
//
//      # 4) Hit /admin with the cookie — should be 200 (we sign ada in as admin).
//      curl -i -b cookies.txt http://localhost:5000/admin
//
//      # 5) Sign out — should clear the cookie.
//      curl -i -b cookies.txt -c cookies.txt -X POST http://localhost:5000/auth/logout
//
//      # 6) Hit /me again — should be 401.
//      curl -i -b cookies.txt http://localhost:5000/me
//
// ACCEPTANCE CRITERIA
//
//   [ ] /me returns 401 with no cookie.
//   [ ] /auth/login with correct credentials returns 200 + Set-Cookie: C9.Auth=...
//   [ ] /auth/login with wrong credentials returns 401.
//   [ ] /me returns 200 + JSON body with the username and email after sign-in.
//   [ ] /admin returns 200 for ada (admin role), 403 for any other authenticated user.
//   [ ] /auth/logout clears the cookie (Set-Cookie with past Expires).
//   [ ] After sign-out, /me returns 401 again.
//   [ ] dotnet build: 0 Warning(s), 0 Error(s).
//   [ ] No hardcoded passwords in production code — but for this exercise the
//       in-memory user dictionary at the top of the file is acceptable.
//
// SMOKE OUTPUT (target — abbreviated; curl headers will be verbose)
//
//   $ curl -i http://localhost:5000/me
//   HTTP/1.1 401 Unauthorized
//
//   $ curl -i -X POST .../auth/login -d '{"username":"ada","password":"correct"}'
//   HTTP/1.1 200 OK
//   Set-Cookie: C9.Auth=CfDJ...; expires=...; secure; samesite=lax; httponly
//
//   $ curl -i -b cookies.txt http://localhost:5000/me
//   HTTP/1.1 200 OK
//   {"name":"ada","email":"ada@example.com","role":"admin"}
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints at the bottom of the file.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// In-memory user store. Not production. But the shape of the data is right:
// a stable id (we use the username here for simplicity), an email, a role,
// and a password. The password is plaintext here ONLY because Exercise 3
// introduces ASP.NET Core Identity's password hasher; this exercise is
// about the cookie pipeline, not credential storage.
// ---------------------------------------------------------------------------

var users = new Dictionary<string, (string Email, string Role, string Password)>
{
    ["ada"]   = ("ada@example.com",   "admin", "correct"),
    ["linus"] = ("linus@example.com", "user",  "kernel-1991"),
    ["grace"] = ("grace@example.com", "user",  "compilers-rule"),
};

// ---------------------------------------------------------------------------
// TODO 1 — Register the cookie authentication scheme.
//
// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
//     .AddCookie(options =>
//     {
//         options.Cookie.Name         = "C9.Auth";
//         options.Cookie.HttpOnly     = true;
//         options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
//         options.Cookie.SameSite     = SameSiteMode.Lax;
//         options.ExpireTimeSpan      = TimeSpan.FromHours(8);
//         options.SlidingExpiration   = true;
//
//         // For an API, override the redirect-to-login behavior so unauthenticated
//         // requests get 401 instead of 302.
//         options.Events = new CookieAuthenticationEvents
//         {
//             OnRedirectToLogin = ctx =>
//             {
//                 ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
//                 return Task.CompletedTask;
//             },
//             OnRedirectToAccessDenied = ctx =>
//             {
//                 ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
//                 return Task.CompletedTask;
//             },
//         };
//     });
// ---------------------------------------------------------------------------

// YOUR CODE HERE

// ---------------------------------------------------------------------------
// TODO 2 — Register authorization with one named policy "AdminsOnly".
//
// builder.Services.AddAuthorization(options =>
// {
//     options.AddPolicy("AdminsOnly", policy => policy.RequireRole("admin"));
// });
// ---------------------------------------------------------------------------

// YOUR CODE HERE

var app = builder.Build();

// ---------------------------------------------------------------------------
// TODO 3 — Add the two middleware calls. Authentication BEFORE authorization.
//
// app.UseAuthentication();
// app.UseAuthorization();
// ---------------------------------------------------------------------------

// YOUR CODE HERE

// ---------------------------------------------------------------------------
// TODO 4 — Implement the four endpoints.
//
// app.MapPost("/auth/login", async (LoginRequest req, HttpContext ctx) =>
// {
//     if (!users.TryGetValue(req.Username, out var u) || u.Password != req.Password)
//         return Results.Unauthorized();
//
//     var claims = new List<Claim>
//     {
//         new(ClaimTypes.NameIdentifier, req.Username),
//         new(ClaimTypes.Name,           req.Username),
//         new(ClaimTypes.Email,          u.Email),
//         new(ClaimTypes.Role,           u.Role),
//     };
//
//     var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
//     var principal = new ClaimsPrincipal(identity);
//
//     await ctx.SignInAsync(
//         CookieAuthenticationDefaults.AuthenticationScheme,
//         principal);
//
//     return Results.Ok(new { user = req.Username });
// });
//
// app.MapPost("/auth/logout", async (HttpContext ctx) =>
// {
//     await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
//     return Results.Ok();
// });
//
// app.MapGet("/me", (HttpContext ctx) =>
// {
//     var user = ctx.User;
//     return Results.Ok(new
//     {
//         name  = user.Identity?.Name,
//         email = user.FindFirst(ClaimTypes.Email)?.Value,
//         role  = user.FindFirst(ClaimTypes.Role)?.Value,
//     });
// })
// .RequireAuthorization();
//
// app.MapGet("/admin", () => Results.Ok(new { message = "welcome, admin" }))
//    .RequireAuthorization("AdminsOnly");
// ---------------------------------------------------------------------------

// YOUR CODE HERE

app.Run();

public sealed record LoginRequest(string Username, string Password);

// ---------------------------------------------------------------------------
// HINTS
// ---------------------------------------------------------------------------
//
// 1) The order of UseAuthentication() and UseAuthorization() is fixed:
//    authentication first, authorization second. Reversing them is a silent
//    bug — every endpoint reads HttpContext.User as anonymous.
//
// 2) The cookie payload contains the ENTIRE principal, encrypted with the
//    data protection key ring. Restarting the app rotates a dev key by
//    default, which logs everyone out. That is expected — `dotnet user-secrets`
//    + DataProtection key persistence is a Week 13 hardening topic.
//
// 3) `ctx.User.Identity?.Name` returns the value of the `ClaimTypes.Name`
//    claim. If you forget to add that claim at sign-in, `Identity.Name` is
//    null even when the user is authenticated.
//
// 4) `Results.Unauthorized()` returns 401. `Results.Forbid()` returns 403.
//    The framework's authorization middleware also returns 401 when the
//    principal is anonymous and 403 when the principal is authenticated but
//    the policy denies. You should rarely call `Results.Forbid()` yourself
//    on a `.RequireAuthorization(...)` endpoint.
//
// 5) Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to your
//    csproj. Every nullable-reference warning is a real bug; you want them
//    surfaced.
//
// 6) `RequireRole("admin")` is shorthand for
//    `RequireClaim(ClaimTypes.Role, "admin")`. The role form looks nicer
//    when the policy is genuinely role-based; switch to the claim form when
//    you start carrying more interesting metadata.
//
// 7) The smoke test sequence (1) anonymous /me → 401, (2) login → 200 with
//    Set-Cookie, (3) authenticated /me → 200 with body, (4) /admin as
//    non-admin → 403, (5) logout → 200, (6) /me → 401 again. If any step
//    deviates, the bug is almost always either a missing UseAuthentication()
//    call or a missing claim at sign-in.
