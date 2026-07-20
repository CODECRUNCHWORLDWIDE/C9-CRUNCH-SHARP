# Week 6 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 7. Answer key at the bottom — don't peek.

---

**Q1.** You wire authentication and authorization into an ASP.NET Core 9 minimal API, but every authenticated endpoint returns `200 OK` for anonymous callers. The two middleware calls appear in this order:

```csharp
app.UseAuthorization();
app.UseAuthentication();
```

What is the bug?

- A) `UseAuthorization()` should not be called at all — `RequireAuthorization()` on each route is enough.
- B) The order is reversed. Authentication must run before authorization so `HttpContext.User` is populated when the authorization middleware reads it. With the calls in the order shown, authorization always sees an anonymous principal and lets the request through if the endpoint metadata happens not to fail-closed.
- C) `UseAuthentication()` is deprecated in ASP.NET Core 9; the framework wires it automatically when you call `AddAuthentication(...)`.
- D) The order is correct; the bug must be elsewhere (`Authentication` services not registered, for example).

---

**Q2.** A JWT signed by your `Jwt:Key` and bearing a valid `iss`, `aud`, `exp`, and `sub` is accepted by your API. An attacker takes the same JWT, base64-decodes it, modifies the `role` claim to `"admin"`, base64-encodes it again, and sends it back. Your API returns `200 OK` for an admin-only endpoint. What is the most likely root cause?

- A) The attacker also forged the signature with the leaked key.
- B) The `TokenValidationParameters` is missing `ValidateIssuerSigningKey = true` (or has it set to `false`), so the framework accepts the modified token without verifying the signature.
- C) JWTs cannot be tampered after signing; the API is correctly returning `200`.
- D) `ClockSkew` is too large and the framework is mis-handling the expiration.

---

**Q3.** You define a policy:

```csharp
options.AddPolicy("AdminsOnly", policy => policy.RequireRole("admin"));
```

What is the equivalent `RequireClaim` form?

- A) `policy.RequireClaim(ClaimTypes.Role, "admin")`
- B) `policy.RequireClaim("admin")`
- C) `policy.RequireClaim("role", "admin")` — using the literal string `"role"`.
- D) The two forms are not equivalent; `RequireRole` checks roles via `IUserRoleStore`, not claims.

---

**Q4.** You write a custom requirement and handler:

```csharp
public sealed class OwnerRequirement : IAuthorizationRequirement;

public sealed class OwnerHandler(AppDbContext db) : AuthorizationHandler<OwnerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement)
    {
        // ... uses db ...
        return Task.CompletedTask;
    }
}
```

You register the handler with:

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, OwnerHandler>();
```

What happens at runtime?

- A) The handler resolves correctly; `AppDbContext` is injected once and reused.
- B) The application throws at startup because `AppDbContext` is scoped and a singleton cannot depend on a scoped service. The fix is to register the handler as scoped (`AddScoped`) or to inject `IServiceScopeFactory` and open a scope inside the handler.
- C) The handler resolves but `AppDbContext` is `null` because singletons cannot resolve scoped services and the framework silently skips the injection.
- D) The handler resolves and the framework creates a fresh `AppDbContext` per request even though the handler is a singleton.

---

**Q5.** Consider:

```csharp
[Authorize(Policy = "OwnerOnly")]
public async Task<IResult> EditNote(int id, ...) { ... }
```

The "OwnerOnly" policy checks that the caller is the owner of the note. The note is loaded *inside* `EditNote` from the database — by the time `EditNote` runs, the authorization middleware has already executed. What is the problem with this design?

- A) None. The policy will read the note from the request body before `EditNote` runs.
- B) The policy runs *before* `EditNote`, so it has no access to the loaded note. The check it performs cannot be "is this user the owner of this specific note." The correct pattern is `IAuthorizationService.AuthorizeAsync(user, note, "OwnerOnly")` inside `EditNote`, after the note is loaded.
- C) The policy will throw an `InvalidOperationException` because resource-based policies require an `OperationAuthorizationRequirement` and the developer used a `IAuthorizationRequirement` instead.
- D) Roles always run before policies, so the policy will not run at all.

---

**Q6.** You set `ClockSkew = TimeSpan.FromMinutes(5)` (the default) in your `TokenValidationParameters`. A JWT's `exp` claim is `2026-05-13T10:00:00Z`. At wall-clock time `2026-05-13T10:03:00Z`, an attacker sends the token. What does the JWT bearer handler do?

- A) Returns `401 Unauthorized` — the token is expired (3 minutes past `exp`).
- B) Accepts the token — the clock skew tolerance of 5 minutes means tokens are still valid for 5 minutes past their `exp`.
- C) Returns `403 Forbidden` — the token is invalid, but the user is technically authenticated.
- D) The behavior is undefined; `ClockSkew` is deprecated in .NET 9.

---

**Q7.** You register cookie authentication and JWT bearer authentication on the same ASP.NET Core 9 app. You set:

```csharp
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(...)
    .AddJwtBearer(...);
```

An API endpoint is decorated with `[Authorize]` (no scheme specified). A request arrives with no cookie and an `Authorization: Bearer ...` header. What happens?

- A) The JWT bearer handler runs because the `Authorization` header is present.
- B) The cookie handler runs because `Cookies` is the default scheme set in `AddAuthentication(...)`. It finds no cookie and returns `NoResult`, leaving the principal anonymous. The endpoint returns `401`. The fix is either to set `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` on the endpoint, or to set `DefaultScheme = "Bearer"` (or write a policy whose `AuthenticationSchemes` lists both).
- C) Both handlers run in parallel; whichever returns first wins.
- D) The framework auto-detects the credential type; the default scheme is irrelevant when an `Authorization` header is present.

---

**Q8.** Which of the following is **not** done by `app.UseAuthentication()`?

- A) Reading the credential off the request (cookie, `Authorization` header, etc.).
- B) Constructing a `ClaimsPrincipal` from the validated credential.
- C) Attaching the principal to `HttpContext.User`.
- D) Refusing the request with `401 Unauthorized` when the credential is invalid.

---

**Q9.** You write an integration test for an `[Authorize(Policy = "AdminsOnly")]` endpoint using `WebApplicationFactory<Program>`:

```csharp
[Fact]
public async Task AdminEndpoint_AsAdmin_Returns200()
{
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add("X-Test-User",  "ada");
    client.DefaultRequestHeaders.Add("X-Test-Roles", "admin");
    var response = await client.GetAsync("/admin");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

Your `TestAuthHandler` is registered as scheme `"Test"` in the factory's `ConfigureTestServices`. The test fails with `401 Unauthorized`. What is the most likely fix?

- A) Add the password to the `X-Test-User` header.
- B) The named policy `AdminsOnly` was registered against the production scheme. In the test, the principal is issued by the `Test` scheme, but the policy's `AuthenticationSchemes` collection (or the policy's `DefaultPolicy`) does not include `Test`. Re-add the policy in the test factory with `policy.AuthenticationSchemes.Add("Test")`, or set `options.DefaultPolicy = new AuthorizationPolicyBuilder("Test").RequireAuthenticatedUser().Build();` in the test overlay.
- C) `WebApplicationFactory<Program>` cannot test `[Authorize]` endpoints — use `WebApplicationFactory<Startup>` instead.
- D) The test should use `client.GetAsync("/admin/")` (with a trailing slash).

---

**Q10.** A user submits a sign-up form with `{ "username": "ada", "password": "p", "tenant_id": "globex" }`. Your sign-in endpoint reads `tenant_id` from the request body and stamps it as a claim in the issued JWT. What is the security issue?

- A) None — the user provided their tenant id, so the policy will correctly enforce it.
- B) The tenant id is attacker-controlled. A user on tenant `acme` could send `tenant_id: "globex"` and the issued JWT would carry the `globex` claim, granting them access to `globex` data. Tenant assignment must come from a trusted source — the user's row in the database (set by an admin during onboarding) or the result of an authenticated OIDC sign-in flow whose IdP carries the tenant claim.
- C) The password is plaintext; that is the only issue.
- D) `tenant_id` is a reserved JWT claim name and the issuer will reject it.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **B** — `UseAuthentication()` populates `HttpContext.User`; `UseAuthorization()` reads it. If authorization runs first, it sees the default unauthenticated principal regardless of what credentials the caller actually sent. The fix is to swap the calls. Option C is wrong: ASP.NET Core 9 still requires both `Use*` calls, and the framework's minimal-API builder places them correctly relative to routing only if you actually invoke them. Option A is wrong: `UseAuthorization()` is required for `RequireAuthorization()` to take effect.

2. **B** — JWT tampering is detected at signature validation. `TokenValidationParameters.ValidateIssuerSigningKey` defaults to `true` for new code; if it is disabled (or `IssuerSigningKey` is unset), the framework accepts tokens with mismatched signatures. The fix is `ValidateIssuerSigningKey = true` and a non-null `IssuerSigningKey`. Option A is possible but rare in practice; option B is the common bug. Option C is wrong: JWTs are signed precisely to detect this kind of tampering.

3. **A** — `RequireRole(role)` is shorthand for `RequireClaim(ClaimTypes.Role, role)`. They are functionally identical; `ClaimTypes.Role` is the constant string `"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"`. Option C uses the unmapped JWT name `"role"`, which would *not* match a `ClaimsIdentity` whose role claim uses the `ClaimTypes.Role` URI — the JWT bearer handler maps the inbound `"role"` to `ClaimTypes.Role` by default. Option D is wrong: `RequireRole` checks claims, not `IUserRoleStore`.

4. **B** — Singleton services cannot depend on scoped services in ASP.NET Core's DI container. The `AppDbContext` is scoped (registered with `AddDbContext`). Registering `OwnerHandler` as a singleton causes a runtime exception at the first request (or at startup if validation is enabled). The two correct fixes are: (1) register the handler as scoped (`AddScoped<IAuthorizationHandler, OwnerHandler>()`); or (2) keep the handler singleton and inject `IServiceScopeFactory`, opening a scope inside `HandleRequirementAsync`. Both work; option (1) is cleaner.

5. **B** — `[Authorize(Policy = "OwnerOnly")]` runs the policy before the endpoint method body. The note has not yet been loaded; the policy has no way to know whether the caller owns the note. The correct pattern is resource-based authorization: leave `[Authorize]` on the endpoint to enforce "must be authenticated," then load the note inside the endpoint and call `IAuthorizationService.AuthorizeAsync(user, note, "OwnerOnly")`. The handler receives the `note` as its second generic parameter (`AuthorizationHandler<OwnerRequirement, Note>`) and can do the ownership check against it.

6. **B** — `ClockSkew` is a *tolerance* applied to lifetime validation. With `FromMinutes(5)`, a token with `exp = T` is still accepted at any wall-clock time `≤ T + 5min`. At 3 minutes past `exp`, the token is well within the tolerance and the handler reports `Success`. The default of 5 minutes is a relic from the era of poorly-synchronized servers; on modern NTP-synced .NET 9 infrastructure, reduce it to 30 seconds or even `TimeSpan.Zero` if your token lifetimes are short.

7. **B** — The framework dispatches to the default scheme when `[Authorize]` is unqualified. The default is `Cookies` here (set in the `AddAuthentication(...)` call). The cookie handler does not consult the `Authorization` header; it looks for the cookie, finds none, returns `NoResult`, and the request becomes anonymous. The endpoint returns `401`. There are three valid fixes: (a) specify the scheme on the endpoint (`[Authorize(AuthenticationSchemes = "Bearer")]`), (b) change the default scheme to `"Bearer"` for API routes (often by grouping API routes under a group whose policy specifies the scheme), or (c) write a policy whose `AuthenticationSchemes.Add("Cookies")` and `AuthenticationSchemes.Add("Bearer")` accept either credential — useful for endpoints that serve both browsers and APIs.

8. **D** — `UseAuthentication()` reads the credential, validates it, builds a `ClaimsPrincipal`, and attaches it to `HttpContext.User`. It does **not** refuse the request: if the credential is missing or invalid, it leaves `HttpContext.User` as an anonymous principal and lets the request continue. Refusal (`401` for anonymous, `403` for policy denial) is `UseAuthorization()`'s job. Conflating the two is the most common mental-model bug in ASP.NET Core auth, and it produces the "every endpoint is anonymous" symptom of Q1.

9. **B** — When you replace the production authentication scheme with `Test` in the test factory, any named policy that was registered against the production scheme stops accepting the test identity. The fix is to either re-register the policies in the test overlay with `policy.AuthenticationSchemes.Add("Test")`, or to set a `DefaultPolicy` that uses the `Test` scheme. The cleanest production form is to leave `AuthenticationSchemes` empty on policies (so they accept whichever scheme the framework dispatched to) and override only the *default* scheme in tests. Option C is wrong: `WebApplicationFactory<Program>` is the standard fixture for minimal-API integration tests.

10. **B** — Tenant assignment must come from a trusted source: the user's row in the database, set during onboarding by an admin or by an OIDC flow with a trusted IdP. Allowing the tenant id to flow from the request body is a privilege-escalation vector. The shape of the attack is "user on tenant A submits a sign-in (or sign-up, or refresh) form with `tenant_id = B`; the issuer trusts the input and stamps a JWT claiming `tenant = B`; the user now has access to tenant B's data because every downstream policy reads the claim, not the database." The fix: read the tenant from the user's row in your sign-in endpoint, *ignore* any `tenant_id` field in the body. Option D is wrong: `tenant_id` is not a reserved JWT claim name.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
