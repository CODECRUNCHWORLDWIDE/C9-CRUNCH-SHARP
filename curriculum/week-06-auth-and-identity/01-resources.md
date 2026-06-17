# Week 6 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `dotnet/aspnetcore` source is MIT-licensed and public on GitHub. The OWASP cheat sheets are CC-BY-SA. The RFCs are free to read at `datatracker.ietf.org`. No paywalled books are linked.

## Required reading (work it into your week)

- **Overview of ASP.NET Core authentication** — the canonical Microsoft Learn entry point:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/>
- **Use cookie authentication without ASP.NET Core Identity** — the minimal cookie-auth article:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie>
- **Configure JWT bearer authentication in ASP.NET Core** — the JWT bearer reference:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- **Introduction to authorization in ASP.NET Core** — the authorization overview:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction>
- **Policy-based authorization in ASP.NET Core** — the canonical reference for policies, requirements, and handlers:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies>
- **Custom authorization policy providers** — when one policy is not enough and you need a `IAuthorizationPolicyProvider`:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/iauthorizationpolicyprovider>
- **Resource-based authorization in ASP.NET Core** — `IAuthorizationService.AuthorizeAsync` with a resource argument:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>
- **Role-based authorization in ASP.NET Core** — when `Roles = "..."` is appropriate:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles>
- **Claims-based authorization in ASP.NET Core** — when `ClaimTypes` carry the policy decision:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/claims>
- **Introduction to ASP.NET Core Identity** — the identity system overview:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity>
- **Configure ASP.NET Core Identity** — the configuration reference:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration>
- **Identity model customization** — extending `IdentityUser` and `IdentityRole`:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model>
- **Integration tests in ASP.NET Core** — `WebApplicationFactory<T>` and overriding auth in tests:
  <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>
- **Mock authentication in ASP.NET Core integration tests** — the `TestAuthHandler` pattern:
  <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests#mock-authentication>
- **.NET 9 — what's new in ASP.NET Core 9** — the official changelog page:
  <https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0>

## Authoritative deep dives

- **Andrew Lock — "ASP.NET Core in Action" companion blog** — the most thorough running source of ASP.NET Core auth deep dives anywhere. The "Custom authorization policies and requirements" series is the canonical walk-through:
  <https://andrewlock.net/category/series/authorization/>
- **Dominick Baier — Duende IdentityServer blog** — Dominick wrote large parts of the cookie and JWT auth handlers in ASP.NET Core. The "Flowing identity through your APIs" series is essential:
  <https://leastprivilege.com/>
- **Steve Smith ("Ardalis") — "ASP.NET Core authentication and authorization"** — clean, opinionated walk-throughs of the same surface:
  <https://ardalis.com/category/aspnet-core/>
- **Damien Bowden — "Securing Microsoft Identity Platform"** — the cross-cutting series on locally-issued JWTs, OIDC, and the bridge between them:
  <https://damienbod.com/category/asp-net-core/>
- **Khalid Abuhakmeh — JetBrains' .NET advocate** — pragmatic security posts, including the JWT signing-key-rotation walkthroughs:
  <https://khalidabuhakmeh.com/blog>
- **OWASP — JSON Web Token cheat sheet** — language-agnostic but every recommendation maps to .NET 9:
  <https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html>
- **OWASP — Authentication cheat sheet**:
  <https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html>
- **OWASP — Session management cheat sheet**:
  <https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html>

## Official .NET docs

- **`AuthenticationOptions` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.authenticationoptions>
- **`CookieAuthenticationOptions` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.cookieauthenticationoptions>
- **`JwtBearerOptions` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions>
- **`TokenValidationParameters` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters>
- **`AuthorizationOptions` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationoptions>
- **`AuthorizationPolicy` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationpolicy>
- **`AuthorizationPolicyBuilder` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationpolicybuilder>
- **`IAuthorizationRequirement` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationrequirement>
- **`AuthorizationHandler<TRequirement>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationhandler-1>
- **`AuthorizationHandlerContext` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationhandlercontext>
- **`IAuthorizationService` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationservice>
- **`ClaimsPrincipal` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal>
- **`ClaimsIdentity` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsidentity>
- **`Claim` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claim>
- **`IdentityUser` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identityuser>
- **`UserManager<TUser>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1>
- **`SignInManager<TUser>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.signinmanager-1>
- **`JwtSecurityTokenHandler` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.identitymodel.tokens.jwt.jwtsecuritytokenhandler>
- **`SecurityTokenDescriptor` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.securitytokendescriptor>

## Open-source projects to read this week

You learn more from one hour reading the actual auth handlers in `dotnet/aspnetcore` than from three hours of tutorials.

- **`dotnet/aspnetcore` — Cookie authentication handler** — the canonical implementation, ~600 lines, MIT-licensed:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authentication/Cookies>
- **`dotnet/aspnetcore` — JWT bearer authentication handler**:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authentication/JwtBearer>
- **`dotnet/aspnetcore` — Core authentication abstractions** — the base classes (`AuthenticationHandler<T>`, `AuthenticationScheme`, etc.):
  <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authentication/Core>
- **`dotnet/aspnetcore` — Authorization core** — `AuthorizationPolicy`, `AuthorizationHandler<T>`, the policy provider:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authorization/Core>
- **`dotnet/aspnetcore` — Authorization policy** — the policy evaluator that decides 200 vs 403:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Security/Authorization/Policy>
- **`dotnet/aspnetcore` — Identity** — the full user-store implementation:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Identity>
- **`AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet`** — the `Microsoft.IdentityModel.*` libraries that ship inside `System.IdentityModel.Tokens.Jwt`. MIT-licensed:
  <https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet>
- **`DuendeSoftware/Samples`** — the canonical worked examples of cookie + JWT in the same ASP.NET Core app (the team that maintains IdentityServer):
  <https://github.com/DuendeSoftware/Samples>
- **`dotnet/AspNetCore.Docs.Samples`** — the official sample repo for every Microsoft Learn auth article:
  <https://github.com/dotnet/AspNetCore.Docs.Samples>
- **`thomhurst/EnumerableAsyncProcessor`** — unrelated to auth but referenced from the integration-tests section because the integration-test runner uses this pattern for parallel tests.

## Community deep-dives

- **Andrew Lock — full ASP.NET Core auth series** — every post is excellent: <https://andrewlock.net/category/csharp/>
- **Dominick Baier — "Flowing claims through your APIs"** (deep-dive on `ClaimsPrincipal` propagation): <https://leastprivilege.com/>
- **Damien Bowden — "ASP.NET Core JWT auth"** (locally-issued JWT recipes): <https://damienbod.com/>
- **Nick Chapsas — "ASP.NET Core authentication" YouTube playlist** (community, very clear): <https://www.youtube.com/@nickchapsas>
- **IAmTimCorey — "ASP.NET Core Identity tutorial"** (community, beginner-to-intermediate): <https://www.youtube.com/@IAmTimCorey>
- **Milan Jovanovic — "JWT authentication in .NET"** (community, focused screencasts): <https://www.milanjovanovic.tech/blog>
- **Steve Smith — "Authorization handler patterns"**: <https://ardalis.com/>

## Libraries we touch this week

- **`Microsoft.AspNetCore.Authentication.Cookies`** — the cookie auth handler. Ships in `Microsoft.AspNetCore.App` (no NuGet add needed for the framework reference); the NuGet package matters when targeting class libraries: <https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.Cookies>
- **`Microsoft.AspNetCore.Authentication.JwtBearer`** — the JWT bearer auth handler. NuGet: <https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer>
- **`Microsoft.AspNetCore.Authorization`** — the authorization framework. Part of `Microsoft.AspNetCore.App`; the NuGet package is for class libraries: <https://www.nuget.org/packages/Microsoft.AspNetCore.Authorization>
- **`Microsoft.AspNetCore.Identity.EntityFrameworkCore`** — the EF Core integration for ASP.NET Core Identity. NuGet: <https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore>
- **`System.IdentityModel.Tokens.Jwt`** — the `JwtSecurityTokenHandler` for issuing and validating JWTs. NuGet: <https://www.nuget.org/packages/System.IdentityModel.Tokens.Jwt>
- **`Microsoft.IdentityModel.Tokens`** — `TokenValidationParameters`, `SymmetricSecurityKey`, `SigningCredentials`. NuGet (transitively included by `JwtBearer` and `System.IdentityModel.Tokens.Jwt`): <https://www.nuget.org/packages/Microsoft.IdentityModel.Tokens>
- **`Microsoft.AspNetCore.Mvc.Testing`** — `WebApplicationFactory<T>` for integration tests. NuGet: <https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing>

## Editors

Unchanged from Weeks 1–5.

- **VS Code + C# Dev Kit** (primary): <https://code.visualstudio.com/docs/csharp/get-started>
- **JetBrains Rider Community** (secondary, free for non-commercial): <https://www.jetbrains.com/rider/>
- The new bit this week: **the `[Authorize]` quick-info popup**. Hover any `[Authorize]` attribute and read the popup. The IDE tells you the scheme (or its absence — a red flag), the policy (or its absence — another red flag), and the roles. If the popup is empty, your `[Authorize]` is doing nothing useful — read Lecture 2 again.

## Free books and chapters

- **"ASP.NET Core in Action, Third Edition" by Andrew Lock** — chapters 14–17 cover auth and authorization end-to-end. The book is paywalled but Lock's blog covers ~80% of the material free.
- **"Pro ASP.NET Core 9" by Adam Freeman** — chapters 38–41 cover Identity and authorization. Apress's free sample chapters surface the cookie-auth chapter most years.
- **"OAuth 2 in Action" by Justin Richer and Antonio Sanso** — paywalled but the OAuth 2.0 specs themselves (RFCs 6749, 6750, 7519) are free to read at `datatracker.ietf.org` and cover the same ground.

## RFCs — when you need to be exact

- **RFC 6265 — HTTP State Management Mechanism (Cookies)**: <https://datatracker.ietf.org/doc/html/rfc6265>
- **RFC 6749 — The OAuth 2.0 Authorization Framework** (read sections 1, 2, 3, 4.1): <https://datatracker.ietf.org/doc/html/rfc6749>
- **RFC 6750 — The OAuth 2.0 Authorization Framework: Bearer Token Usage**: <https://datatracker.ietf.org/doc/html/rfc6750>
- **RFC 7519 — JSON Web Token (JWT)**: <https://datatracker.ietf.org/doc/html/rfc7519>
- **RFC 7515 — JSON Web Signature (JWS)**: <https://datatracker.ietf.org/doc/html/rfc7515>
- **RFC 7518 — JSON Web Algorithms (JWA)**: <https://datatracker.ietf.org/doc/html/rfc7518>
- **RFC 8725 — JSON Web Token Best Current Practices**: <https://datatracker.ietf.org/doc/html/rfc8725>
- **RFC 6819 — OAuth 2.0 Threat Model and Security Considerations**: <https://datatracker.ietf.org/doc/html/rfc6819>

## Videos (free, no signup)

- **"ASP.NET Core security" — the .NET YouTube channel's playlist** — short, focused videos: <https://www.youtube.com/@dotnet>
- **".NET Conf 2024 — security sessions"** — the .NET 9 release; includes "What's new in ASP.NET Core 9 security": <https://www.youtube.com/playlist?list=PL1rZQsJPBU2StolNg0aqvQswETPcYnNKL>
- **Nick Chapsas — "Authentication and authorization in ASP.NET Core"** (community): <https://www.youtube.com/@nickchapsas>
- **Milan Jovanovic — "JWT auth in .NET"** (community): <https://www.youtube.com/@MilanJovanovicTech>
- **Raw Coding — "ASP.NET Core authentication deep dive"** (community, very detailed): <https://www.youtube.com/@RawCoding>

## Tools you'll use this week

- **`dotnet` CLI** — same as before.
- **`dotnet user-secrets`** — store the JWT signing key out of source control during development. Same machine, encrypted store: <https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets>
- **`jwt.io`** — paste a JWT in the browser and inspect the header, payload, and signature. Do **not** paste production tokens; the tool is client-side but the habit is bad. Use scratch tokens or your dev-issuer's tokens: <https://jwt.io/>
- **`curl`** or **`HTTPie`** — send `Authorization: Bearer ...` headers from the terminal. Either is fine; we use `curl` in examples.
- **`Bruno`** (open-source Postman alternative) — collections of authenticated requests; saves you typing the same `Authorization` header forty times: <https://www.usebruno.com/>
- **Browser DevTools** — Application → Cookies tab to inspect the cookie set by `SignInAsync`. Network tab to watch the `Set-Cookie` header roundtrip. The DevTools cookie editor lets you tamper a cookie to test invalid-credential paths.
- **`openssl`** — generate a random 256-bit signing key for HS256: `openssl rand -base64 32`. Available on every Unix and on Windows via WSL or Git Bash.

## The spec — when you need to be exact

- **ASP.NET Core 9 source layout for security** — start here, branch into the area:
  <https://github.com/dotnet/aspnetcore/tree/main/src/Security>
- **.NET 9 changelog — security improvements**:
  <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/>
- **ASP.NET Core 9 release notes — security section**:
  <https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0#security>

## Glossary cheat sheet

Keep this open in a tab.

| Term | Plain English |
|------|---------------|
| **Authentication (`authn`)** | "Who is this caller?" Reads a credential off the request, attaches a `ClaimsPrincipal` to `HttpContext.User`. Never refuses a request. |
| **Authorization (`authz`)** | "Given who they are, what may they do?" Reads `HttpContext.User`, looks at endpoint metadata, returns `200`, `401`, or `403`. |
| **`AuthenticationScheme`** | A named bundle of handler + options registered with `AddAuthentication(...)`. `Cookies`, `Bearer`, `Identity.Application` are the three you will use. |
| **`AuthenticationHandler<TOptions>`** | The class that knows how to read a credential off a request. ASP.NET Core ships one per scheme. |
| **`ClaimsPrincipal`** | The "user" abstraction in ASP.NET Core. Holds one or more `ClaimsIdentity` instances. `HttpContext.User` is always a `ClaimsPrincipal`. |
| **`ClaimsIdentity`** | One identity within a principal. Has an `AuthenticationType` (the scheme that produced it) and a list of `Claim` objects. |
| **`Claim`** | A typed key-value pair: `(Type, Value, ValueType, Issuer, OriginalIssuer)`. The four canonical types are `ClaimTypes.Name`, `ClaimTypes.NameIdentifier`, `ClaimTypes.Email`, `ClaimTypes.Role`. |
| **Cookie authentication** | A scheme that reads a credential out of a `Set-Cookie`-issued cookie. `Microsoft.AspNetCore.Authentication.Cookies`. The browser's primary credential format. |
| **JWT bearer authentication** | A scheme that reads a credential out of the `Authorization: Bearer <token>` header. `Microsoft.AspNetCore.Authentication.JwtBearer`. The machine's primary credential format. |
| **JWT (JSON Web Token)** | A compact, signed JSON document. `header.payload.signature` base64url-encoded, joined by dots. RFC 7519. |
| **`JwtSecurityTokenHandler`** | The class that issues and parses JWTs in `System.IdentityModel.Tokens.Jwt`. |
| **`SigningCredentials`** | The algorithm + key pair used to sign a JWT. `HS256` (symmetric, `SymmetricSecurityKey`) for Week 6; `RS256` (asymmetric, `RsaSecurityKey`) for Week 7. |
| **`TokenValidationParameters`** | The configuration that says how to validate an incoming JWT: which issuer, which audience, which key, what clock skew. |
| **ASP.NET Core Identity** | The user-store + sign-in-manager + password-hasher + token-providers framework. Sits on top of cookie auth; it is *not* itself a scheme. |
| **`IdentityUser`** | The default user entity. Has `Id`, `UserName`, `NormalizedUserName`, `PasswordHash`, `Email`, `SecurityStamp`, plus a few more. |
| **`UserManager<TUser>`** | The CRUD-ish API for users: create, find, update, delete, change password, generate tokens. |
| **`SignInManager<TUser>`** | The "sign this user in" API: `PasswordSignInAsync`, `SignInAsync`, `SignOutAsync`. Lays cookies via `HttpContext.SignInAsync`. |
| **`IPasswordHasher<TUser>`** | The salting + hashing API. Default implementation uses PBKDF2 with HMAC-SHA512. Pluggable. |
| **`[Authorize]`** | An attribute or endpoint metadata that says "this endpoint requires authentication." Without arguments: any authenticated user. With `Policy = "..."`: that policy. |
| **`[Authorize(Policy = "Owner")]`** | The endpoint requires policy "Owner." The policy is defined in `AddAuthorization(...)`. |
| **`[Authorize(Roles = "Admin")]`** | Shortcut for a single-role policy. The form `[Authorize(Policy = "...")]` is the production form. |
| **`[AllowAnonymous]`** | An attribute that overrides `[Authorize]` on an inner endpoint. Useful when a whole group is `[Authorize]` but one endpoint must be public. |
| **`AuthorizationPolicy`** | A list of `IAuthorizationRequirement` instances. The policy passes if every requirement is satisfied. |
| **`IAuthorizationRequirement`** | A marker interface for the data half of a policy. Has no method; it just identifies the requirement. |
| **`AuthorizationHandler<TRequirement>`** | The logic half. Override `HandleRequirementAsync(context, requirement)`; call `context.Succeed(requirement)` if the rule is satisfied; let the method return otherwise. |
| **`AuthorizationHandlerContext`** | The argument to `HandleRequirementAsync`. Exposes `User`, `Resource`, `PendingRequirements`, and the result-mutation methods. |
| **`IAuthorizationService`** | The service for *programmatic* (resource-based) authorization. Call `AuthorizeAsync(user, resource, "policy")` from inside an endpoint when the policy depends on the resource. |
| **`RequireAuthenticatedUser()`** | The simplest policy: "the user must be authenticated." |
| **`RequireRole(string role)`** | Built-in helper: the user must have a `ClaimTypes.Role` claim equal to `role`. |
| **`RequireClaim(string type, string value)`** | Built-in helper: the user must have a claim of type `type` with value `value`. |
| **`RequireAssertion(Func<AuthorizationHandlerContext, bool>)`** | Built-in helper: the user must satisfy this predicate. The escape hatch for "I just want one if-statement." |
| **CSRF (Cross-Site Request Forgery)** | An attack where a malicious site causes the victim's browser to submit a request to your site that uses the victim's cookies. ASP.NET Core's `[ValidateAntiForgeryToken]` and the default `SameSite=Lax` cookie attribute prevent the common shapes. |
| **XSS (Cross-Site Scripting)** | An attack where a malicious script in your site reads cookies or tokens from the user's browser. `HttpOnly=true` on the cookie prevents the cookie variant; output-encoding prevents the script-injection variant. |
| **`SameSite=Lax`** | A cookie attribute that prevents the cookie from being sent on cross-site `POST` requests but allows it on top-level `GET` navigations. The default for ASP.NET Core's auth cookies. |
| **`HttpOnly=true`** | A cookie attribute that prevents JavaScript (`document.cookie`) from reading the cookie. The default for ASP.NET Core's auth cookies. |
| **`Secure=Always`** | A cookie attribute that requires HTTPS. The default for ASP.NET Core's auth cookies in production. |

---

*If a link 404s, please open an issue so we can replace it.*
