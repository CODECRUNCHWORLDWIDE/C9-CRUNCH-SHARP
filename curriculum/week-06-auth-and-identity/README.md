# Week 6 — ASP.NET Core Authentication and Authorization

Welcome to **C9 · Crunch Sharp**, Week 6. Week 1 made the language ordinary. Week 2 put it behind an HTTP port. Week 3 put it on top of a real database. Week 4 made it concurrent. Week 5 made it declarative. This week you put a *door* in front of the HTTP port and a *bouncer* in the hallway. Authentication is the door — *who is this caller?* Authorization is the bouncer — *given who they are, what may they do?* They are different concerns, implemented by different middleware, configured by different builders, and broken by different bugs. By Friday you should be able to wire cookie authentication and JWT bearer authentication into the same ASP.NET Core 9 application without them stepping on each other, write policy-based authorization that compiles into testable handlers instead of `if` statements scattered through endpoints, sign a JWT with a symmetric key and validate it back, claim-check a resource with `IAuthorizationService` from inside a handler, and explain — to a code reviewer who has never written C# — why `[Authorize]` without a scheme is a smell and why `[Authorize(Policy = "...")]` is the production form.

This is the third week of Phase 2. The Sharp Notes ledger API you built in Week 5 currently accepts every request, trusts every caller, and exposes every endpoint. That is fine for a learning exercise. It is not fine for anything that touches money, identity, or a database with another user's row in it. ASP.NET Core ships a complete authentication and authorization stack — `Microsoft.AspNetCore.Authentication.Cookies`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Authorization`, and `Microsoft.AspNetCore.Identity` — and the Microsoft Learn "Overview of ASP.NET Core authentication" article (~80 pages across its subsections) is the canonical reference. We will read parts of it together. But the article does not prepare you for the question that actually matters in a code review: *given a request hits this endpoint with this set of cookies and this `Authorization: Bearer ...` header, which middleware reads which credential, which handler runs first, which `ClaimsPrincipal` lives on `HttpContext.User` when the endpoint executes, and which policy decides whether the response is `200 OK` or `403 Forbidden`?* This week gives you a defensible answer for every authenticated route you write from here on — and the muscle memory to write the policy before the endpoint.

The first thing to internalize is that **authentication and authorization are different layers of the middleware pipeline, run in a fixed order, and any code that conflates them will eventually leak**. `app.UseAuthentication()` reads the credential off the request and populates `HttpContext.User` with a `ClaimsPrincipal`. `app.UseAuthorization()` reads `HttpContext.User`, looks up the endpoint's authorization metadata (`[Authorize]`, `[AllowAnonymous]`, or a `RequireAuthorization(...)` call on a route group), and decides whether to short-circuit the pipeline with `401 Unauthorized` or `403 Forbidden`. Authentication never refuses a request. Authorization never reads a cookie or a JWT. The two are wired with two different `builder.Services.Add*` calls — `AddAuthentication(...)` and `AddAuthorization(...)` — and the two `app.Use*` calls must appear in the right order. Get the order wrong and the symptom is *every endpoint is anonymous*. By the end of this week you will know the order by reflex.

The second thing to internalize is that **cookies and JWTs are not "one is better than the other" — they answer different questions**. Cookies are ideal when the browser is the client and the same site is the server: HTTP-only, secure, SameSite=Lax cookies are the strongest credential a browser can hold. JWTs are ideal when the client is *not* the same site as the server, when the client is not a browser at all (a mobile app, a CLI, a service-to-service caller), and when the credential needs to be readable by something other than the issuer (a sidecar, an API gateway, a microservice). Most production ASP.NET Core 9 applications ship *both* — cookies for the human-facing surface, JWTs for the API surface, the same authorization policies on top. The lecture shows you how to configure both in the same `WebApplication`, distinguish them by scheme name, and route incoming requests to the right handler based on `Authorization` header presence vs cookie presence.

The third thing to internalize is that **policy-based authorization is how production ASP.NET Core scales beyond `[Authorize(Roles = "Admin")]`**. Roles are fine for small applications: a user is in a role, a role unlocks endpoints. Policies are how you encode *real* business rules: "the caller must be the owner of the resource," "the caller must be on the same tenant as the resource," "the caller's account must be older than 30 days," "the caller is allowed but only between 9am and 5pm UTC." Each policy is one `AddPolicy("...", policy => policy.RequireXxx(...))` call at startup. Complex policies graduate to `IAuthorizationRequirement` + `AuthorizationHandler<T>` — a pair of testable classes where the handler runs against an injected service container and can read `HttpContext`, your `DbContext`, an `IClock`, or anything else. Lecture 2 makes policies the default and `[Authorize(Roles = "...")]` the exception.

## Learning objectives

By the end of this week, you will be able to:

- **Read** any ASP.NET Core 9 authentication setup and predict, before running it, which middleware populates `HttpContext.User` for which kind of request — cookie, JWT, both, neither, anonymous.
- **Distinguish** authentication from authorization at every middleware call site. Name the three things `UseAuthentication()` does, the three things `UseAuthorization()` does, and the one thing neither of them does (validating the *contents* of a claim — that is policy work).
- **Wire** cookie authentication into an ASP.NET Core 9 minimal API in fewer than 20 lines of startup configuration: cookie name, expiration, sliding renewal, `SameSite=Lax`, `HttpOnly=true`, `Secure=Always`, and a sign-in endpoint that produces the cookie from a `ClaimsPrincipal`.
- **Wire** JWT bearer authentication into the same application using `Microsoft.AspNetCore.Authentication.JwtBearer`. Configure issuer, audience, signing key (symmetric for now; asymmetric in Week 7), clock skew, and validation parameters.
- **Issue** a JWT from a sign-in endpoint using `System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler`. Read every claim back from `HttpContext.User`.
- **Configure** ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`) for a real user store backed by EF Core. Use `UserManager<T>`, `SignInManager<T>`, password hashing, and the default `IdentityUser` schema.
- **Author** policy-based authorization with `AddAuthorization(options => options.AddPolicy(...))`. Use `RequireClaim`, `RequireRole`, `RequireAssertion`, and `RequireAuthenticatedUser` as the four building blocks.
- **Write** custom `IAuthorizationRequirement` + `AuthorizationHandler<T>` pairs for business rules that do not fit the built-in `Require*` helpers. Inject your `DbContext`, your `IClock`, your `ITenantContext` into the handler.
- **Apply** resource-based authorization with `IAuthorizationService.AuthorizeAsync(user, resource, "policy")` from inside an endpoint handler — the right pattern for "may this user edit this specific note?"
- **Test** every authorization decision with `WebApplicationFactory<T>` and a fake authentication scheme that issues a known `ClaimsPrincipal` per test. No production credentials in tests, ever.
- **Recognize** the three places people leak tokens — query strings, log files, client-side JavaScript — and the three CSRF / XSS / token-theft mistakes that ASP.NET Core's defaults already prevent if you do not override them.

## Prerequisites

- **Weeks 1, 2, 3, 4, and 5** of C9 complete: you can scaffold a multi-project solution from the `dotnet` CLI, you can write Minimal API endpoints with `TypedResults`, you can model an EF Core `DbContext`, you can compose `async`/`await` and `CancellationToken` through a `Channel<T>` pipeline, you can read a LINQ chain out loud as the question it answers, and your `dotnet build` reflexively prints `Build succeeded · 0 warnings · 0 errors`.
- **Comfort with HTTP cookies and headers.** You have seen `Set-Cookie`, you know what `HttpOnly` and `Secure` mean, you have looked at an `Authorization: Bearer ...` header in a browser DevTools network tab. We do not teach HTTP from scratch; we teach it as the substrate cookie auth and JWT auth share.
- **The ledger API from Week 5's mini-project** (or an equivalent ASP.NET Core 9 minimal API you can use as the host). The mini-project this week adds auth on top of it; if you skipped Week 5's mini-project, allow an extra ~2 hours at the start of the week to scaffold a small replacement.
- A working `dotnet --version` of `9.0.x` or later on your PATH. ASP.NET Core 9 is the runtime; `Microsoft.AspNetCore.Authentication.JwtBearer 9.0.x` and `Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.x` are the NuGet packages.
- Nothing else. We start from a clean `dotnet new web`, end at a fully authenticated, policy-authorized ledger API with a real user store, and never install a paid identity provider. Week 7 introduces OpenID Connect with Keycloak; Week 6 is local-account-and-JWT only.

## Topics covered

- **The authentication middleware.** What `app.UseAuthentication()` does on every request: read the configured scheme's credential off the request, deserialize it into a `ClaimsPrincipal`, attach the principal to `HttpContext.User`. Why this is *not* the same as "decide whether to allow the request."
- **The authorization middleware.** What `app.UseAuthorization()` does: read the endpoint's authorization metadata, walk the configured policies against `HttpContext.User`, short-circuit with `401` or `403` on failure. Why the order — authentication *before* authorization — matters at every endpoint.
- **Authentication schemes.** What a "scheme" is in ASP.NET Core: a named bundle of `AuthenticationHandler<T>`, `AuthenticationOptions`, and configuration. `Cookies`, `Bearer`, and `Identity.Application` are the three you will use this week.
- **Cookie authentication.** `AddCookie(options => ...)` from `Microsoft.AspNetCore.Authentication.Cookies`. The cookie's name, expiration, sliding renewal, `HttpOnly`, `SameSite`, `Secure` policy. The sign-in flow: `HttpContext.SignInAsync("Cookies", principal)`. The sign-out flow: `HttpContext.SignOutAsync("Cookies")`. The redirect-to-login behavior and how to disable it for API endpoints.
- **JWT bearer authentication.** `AddJwtBearer(options => ...)` from `Microsoft.AspNetCore.Authentication.JwtBearer`. `TokenValidationParameters`: `ValidIssuer`, `ValidAudience`, `IssuerSigningKey`, `ClockSkew`, `ValidateLifetime`. Why `ClockSkew` defaults to 5 minutes and why you should usually shrink it.
- **Issuing a JWT.** `JwtSecurityTokenHandler.WriteToken(...)`. The three claims every JWT carries (`sub`, `iat`, `exp`) plus the ones you add (`email`, `role`, anything domain-specific). The HMAC-SHA256 symmetric signing path for Week 6; the RS256 asymmetric path is Week 7.
- **`ClaimsPrincipal` and `ClaimsIdentity`.** What a claim *is* (a key-value pair from an issuer about a subject). How `HttpContext.User` exposes the current principal. How `user.FindFirst("sub")?.Value` reads a claim. How `user.Identity!.IsAuthenticated` works.
- **ASP.NET Core Identity.** What Identity *is* (a user store + password hashing + sign-in manager + token providers) and what it is *not* (an authentication scheme — it sits on top of cookie auth). `AddIdentityCore<TUser>()` vs `AddIdentity<TUser, TRole>()`. The `UserManager<TUser>`, `SignInManager<TUser>`, `IPasswordHasher<TUser>` services. The EF Core integration via `AddEntityFrameworkStores<TContext>()`.
- **Policy-based authorization.** `AddAuthorization(options => options.AddPolicy("name", policy => ...))`. The four built-in `Require*` helpers: `RequireAuthenticatedUser`, `RequireRole`, `RequireClaim`, `RequireAssertion`. When each is the right tool. Why `[Authorize(Roles = "...")]` is the lazy form of `[Authorize(Policy = "...")]`.
- **Custom requirements and handlers.** `IAuthorizationRequirement` (a marker type with the data) + `AuthorizationHandler<TRequirement>` (the logic). DI scope: handlers are registered as singletons by default; if your handler depends on a `DbContext` you must register it as scoped and use `IServiceScopeFactory`.
- **Resource-based authorization.** `IAuthorizationService.AuthorizeAsync(user, resource, "policy")`. The pattern for "may this user edit *this specific* row?" Why `[Authorize]` cannot express this and why it must live inside the endpoint handler.
- **Testing authentication and authorization.** `WebApplicationFactory<T>` + a custom `AuthenticationHandler<T>` that issues a known `ClaimsPrincipal` per test. The `TestAuthHandler` pattern. Why integration tests for `403 Forbidden` are the highest-ROI tests in the codebase.
- **Token storage and leakage.** Cookies: HttpOnly + Secure + SameSite, never accessible to JavaScript. JWTs: in-memory or in `localStorage` (with caveats) or in a secure cookie of their own. The three places tokens leak: query strings, server logs, browser history. The defaults ASP.NET Core gives you and what to leave alone.
- **CORS and authentication.** Why CORS preflight (`OPTIONS`) must not require authentication. The `app.UseCors()` placement relative to `app.UseAuthentication()`. The four headers a credential-bearing CORS response must send.

## Weekly schedule

The schedule adds up to approximately **34 hours**. Treat it as a target, not a contract.

| Day       | Focus                                                  | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | Cookies, sign-in/sign-out, the auth middleware         |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | JWT bearer, signing, validation, claims                |    1h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     4.5h    |
| Wednesday | ASP.NET Core Identity, `UserManager`, `SignInManager`  |    1.5h  |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5h      |
| Thursday  | Policy-based authz, requirements, handlers, resource   |    1.5h  |    1.5h   |     1h     |    0.5h   |   1h     |     2h       |    0.5h    |     8h      |
| Friday    | Mini-project — auth + authz on the Week-5 ledger API   |    0h    |    0h     |     0h     |    0.5h   |   1h     |     3h       |    0.5h    |     5h      |
| Saturday  | Mini-project deep work, integration tests              |    0h    |    0h     |     0h     |    0h     |   1h     |     3h       |    0h      |     4h      |
| Sunday    | Quiz, review, polish                                   |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                        | **6h**   | **6h**    | **1h**     | **3h**    | **6h**   | **8.5h**     | **2.5h**   | **33.5h**   |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | Curated Microsoft Learn ASP.NET Core authentication and authorization docs, the IdentityModel source, OWASP cheat sheets |
| [lecture-notes/01-cookies-jwt-and-the-identity-system.md](./lecture-notes/01-cookies-jwt-and-the-identity-system.md) | Cookie auth, JWT bearer auth, the middleware pipeline order, `ClaimsPrincipal` end-to-end, ASP.NET Core Identity, mixing schemes in one app |
| [lecture-notes/02-policy-based-authorization-and-claims.md](./lecture-notes/02-policy-based-authorization-and-claims.md) | The `[Authorize]` attribute, `RequireAuthorization`, policies, requirements, handlers, resource-based authorization, integration tests |
| [exercises/README.md](./exercises/README.md) | Index of short coding exercises |
| [exercises/exercise-01-cookie-auth.cs](./exercises/exercise-01-cookie-auth.cs) | Build a small ASP.NET Core 9 app with cookie authentication, a sign-in endpoint, a sign-out endpoint, and a `[Authorize]`-protected resource |
| [exercises/exercise-02-jwt-bearer.cs](./exercises/exercise-02-jwt-bearer.cs) | Same app, but with JWT bearer authentication instead of cookies. Issue a token, validate it, read its claims |
| [exercises/exercise-03-policy-handlers.cs](./exercises/exercise-03-policy-handlers.cs) | Write a `MinimumAccountAgeRequirement` + handler. Wire it as a policy. Test it with `WebApplicationFactory<T>` and a fake auth scheme |
| [challenges/README.md](./challenges/README.md) | Index of weekly challenges |
| [challenges/challenge-01-multi-tenant-authorization.md](./challenges/challenge-01-multi-tenant-authorization.md) | Build a multi-tenant authorization layer with a `TenantClaim`, a per-resource tenant check, and a regression test that proves tenant isolation |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on cookies, JWTs, Identity, and policies in ASP.NET Core 9 |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for "Sharp Notes Auth" — add cookie auth, JWT bearer auth, ASP.NET Core Identity, and policy-based authorization to the Week-5 ledger API |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

A nullable-reference warning is a bug. An `[Authorize]` without a scheme that silently does the wrong thing is a bug. A `AddAuthentication()` call without a `DefaultScheme` is — at minimum — a code smell and often the reason every endpoint in your app is `401 Unauthorized` in production. By the end of Week 6 you will have an ASP.NET Core 9 API that compiles clean, accepts both browser cookies and machine bearer tokens, applies the same set of policy-based authorization rules to both surfaces, and ships with integration tests that prove every policy decision — including the negative cases (`403 Forbidden` when the policy fails).

## A note on what's not here

Week 6 introduces local authentication (cookies, JWTs issued by your own app) and policy-based authorization, but it does **not** introduce:

- **OpenID Connect (OIDC) and external identity providers.** That is Week 7. We deliberately stay on locally-issued JWTs this week so you can see what a JWT actually contains, how the signing works, and how validation fails — without an external provider in the way. Week 7 swaps the local issuer for Keycloak and the symmetric HMAC key for an RS256 keypair, but the validation pipeline is the same.
- **OAuth 2.0 flows.** Authorization code, client credentials, device code, the refresh token rotation pattern — Week 7. Week 6 issues a JWT from a sign-in endpoint and validates it on every request; that is enough to get the rest of the auth surface honest.
- **Certificate authentication, Windows authentication, API key authentication.** Each is a documented `AuthenticationHandler<T>` and shows up briefly in resources; none is on the lecture path. Pick them up when you need them.
- **Custom token formats.** PASETO, Macaroons, opaque bearer tokens backed by a Redis store. All are valid; none is the default. JWTs are the lingua franca; we use them.
- **`Microsoft.AspNetCore.DataProtection` key management beyond the default.** The default file-system key ring is fine for a single-machine dev environment. Distributed key management is a Week 13 (capstone hardening) topic.
- **MFA, passwordless, WebAuthn.** ASP.NET Core Identity has scaffolding for all three; we mention them in resources and leave the hands-on for elective weeks.

The point of Week 6 is a sharp, narrow tool: cookie auth and JWT bearer auth wired into the same app, ASP.NET Core Identity providing the user store, and policy-based authorization expressing every business rule in testable, injectable code.

## Stretch goals

If you finish the regular work early and want to push further:

- Read **the `dotnet/aspnetcore` `Microsoft.AspNetCore.Authentication` source** for the cookie handler: <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authentication/Cookies>. The `CookieAuthenticationHandler` class is ~600 lines and the most-read file in the security stack.
- Skim the **`dotnet/aspnetcore` JWT bearer source**: <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authentication/JwtBearer>. Note how `TokenValidationParameters` is the same type used everywhere `System.IdentityModel.Tokens.Jwt` ships.
- Read **the OWASP "JSON Web Token Cheat Sheet"**: <https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html>. The header is Java-flavored but every recommendation maps cleanly to .NET 9.
- Watch **Dominick Baier — "API authentication and authorization in ASP.NET Core"** (the canonical Duende IdentityServer talk; their YouTube channel hosts the recorded sessions free).
- Implement **the refresh-token rotation pattern** as a custom endpoint pair (`POST /auth/refresh`). Note where the security analysis gets subtle: token reuse detection, server-side revocation lists, sliding lifetimes.
- Write a **custom `AuthenticationHandler<T>` for API keys**, registered as a third scheme alongside `Cookies` and `Bearer`. Note how the same `[Authorize(Policy = "...")]` works across all three.

## Up next

Continue to **Week 7 — OpenID Connect and External Identity Providers** once you have pushed the mini-project to your GitHub. Week 7 reintroduces every authentication concept from this week against an external Identity Provider (Keycloak in a Docker container) — and you will see, in real time, how OIDC's authorization code flow replaces your `POST /auth/login` endpoint, how RS256 replaces HS256, and how the same `[Authorize(Policy = "...")]` attributes work without changes. The reflex you build this week — *"which scheme is this request hitting and which policy does the endpoint require?"* — is the reflex Week 7 cashes in on.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
