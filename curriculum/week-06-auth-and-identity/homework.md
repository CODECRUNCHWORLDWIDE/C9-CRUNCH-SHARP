# Week 6 Homework

Six practice problems that revisit the week's topics. The full set should take about **6 hours**. Work in your Week 6 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — Read the cookie auth source

**Problem statement.** Open the ASP.NET Core 9 source for the cookie authentication handler: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs>. Read the `HandleAuthenticateAsync` method end-to-end. Save a 200-word note at `notes/cookie-handler.md` explaining:

1. How the handler reads the cookie value from the request.
2. How it deserializes the value into a `ClaimsPrincipal` (the `IDataProtector` step).
3. What it does when the cookie is absent — does it `Fail`, `NoResult`, or something else? Why?

Then add a code snippet in the note showing the equivalent shape in your own pseudocode (~10 lines).

**Acceptance criteria.**

- `notes/cookie-handler.md` exists, is 180–220 words, and references at least three method names from the file (e.g. `HandleAuthenticateAsync`, `ReadCookieTicket`, `ApplyHeaders`).
- The note correctly identifies that the handler returns `AuthenticateResult.NoResult()` (not `Fail`) when no cookie is present — and explains why (`NoResult` lets the request continue as anonymous; `Fail` would short-circuit the pipeline).
- File is committed.

**Hint.** The `ReadCookieTicket` private method is the deserialization step. The `IDataProtector` is resolved from `Options.DataProtectionProvider` and applies the data protection key ring.

**Estimated time.** 30 minutes.

---

## Problem 2 — JWT signing key rotation

**Problem statement.** Configure your Week 6 ledger API to accept JWTs signed with *either* of two keys: `Jwt:Key:Current` and `Jwt:Key:Previous`. Tokens minted by your own sign-in endpoint use `Current`. Tokens signed with `Previous` are still accepted on inbound validation (for users whose tokens were issued before a key rotation). Write a test that:

1. Mints a token with the previous key directly (not through the sign-in endpoint).
2. Sends it to a `[Authorize]` endpoint.
3. Asserts `200 OK`.
4. Mints a token with a *third* key (the "next" key, not configured) and asserts `401 Unauthorized`.

**Acceptance criteria.**

- Both keys load from `dotnet user-secrets` (or configuration); neither is hardcoded.
- The JWT bearer handler uses `IssuerSigningKeys` (plural) with both keys, **or** uses `IssuerSigningKeyResolver` to return both.
- The four-step test (Current accepted, Previous accepted, Next rejected, missing rejected) all pass.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** `TokenValidationParameters.IssuerSigningKeys` is `IEnumerable<SecurityKey>`. Set it to `[currentKey, previousKey]`. The handler will try each in turn. The cleanest production form uses `IssuerSigningKeyResolver`, which is a callback that returns the matching key by `kid` (key id) — but for this homework the array form is acceptable.

**Estimated time.** 1 hour.

---

## Problem 3 — A custom `IAuthorizationPolicyProvider`

**Problem statement.** Build a custom `IAuthorizationPolicyProvider` that generates policies on the fly for `[Authorize(Policy = "MinAge:30")]`, `[Authorize(Policy = "MinAge:90")]`, etc. The provider parses the policy name, extracts the number, and returns a policy that includes a `MinimumAccountAgeRequirement(N)`. Fall back to the default provider for any other policy name. Cover the cache so repeated lookups for the same name return the same `AuthorizationPolicy` instance.

```csharp
public sealed class MinAgePolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string name)
    {
        if (name.StartsWith("MinAge:") &&
            int.TryParse(name["MinAge:".Length..], out var days))
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new MinimumAccountAgeRequirement(days))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return base.GetPolicyAsync(name);
    }
}
```

Register it: `builder.Services.AddSingleton<IAuthorizationPolicyProvider, MinAgePolicyProvider>();`.

Write 5 tests: `MinAge:30` against a 31-day account passes; `MinAge:30` against a 5-day account fails; `MinAge:90` against a 31-day account fails; `MinAge:abc` falls back to the default provider (and fails because no such policy exists); `AdminsOnly` (a normal named policy) still works.

**Acceptance criteria.**

- The provider extends `DefaultAuthorizationPolicyProvider` and overrides `GetPolicyAsync`.
- Both the parameterized policies and the conventional named policies work.
- 5 tests pass.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** The default provider reads from `IOptions<AuthorizationOptions>` and supports the `AddPolicy(name, ...)` registrations from `AddAuthorization(...)`. By calling `base.GetPolicyAsync(name)` on a miss, you preserve that behavior. Read <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/iauthorizationpolicyprovider> for the canonical example.

**Estimated time.** 1 hour 15 minutes.

---

## Problem 4 — Refactor a real endpoint

**Problem statement.** Pick *one* endpoint from your Week 5 mini-project (the ledger API) and add authorization to it. The endpoint must:

- Have been previously unauthenticated.
- Have a clear "who can do this?" answer (you, or your team, or admins, or owners).
- Be testable with `WebApplicationFactory<T>`.

Add:

1. A `.RequireAuthorization("...")` (or `[Authorize(Policy = "...")]`) on the endpoint.
2. The named policy registration in `AddAuthorization(...)`.
3. Three integration tests: `200` for an allowed caller, `403` for a disallowed caller, `401` for an anonymous caller.
4. A 5-sentence comment at the top of the endpoint explaining the threat model — what does the policy protect, and what does it not?

Commit both forms — the original (in a branch tag or a `before/` folder) and the new (on the main branch), so a reviewer can diff them.

**Acceptance criteria.**

- The endpoint now requires authentication and authorization.
- The named policy is registered and named with intent (e.g. `OwnerOnly`, not `Policy1`).
- All three new tests pass.
- The 5-sentence threat model comment is honest about what the policy does and does not protect (e.g. "this policy ensures the caller is the owner, but it does not prevent denial-of-service via repeated forbidden requests").
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** If your Week 5 mini-project doesn't have a candidate endpoint, scaffold a small `/api/notes/{id}` GET endpoint backed by an in-memory list and add the policy to that. The win is the refactor reflex, not the specific endpoint.

**Estimated time.** 1 hour 30 minutes.

---

## Problem 5 — The `WhoAmI` debugging endpoint

**Problem statement.** Build a small `WhoAmI` endpoint at `GET /api/whoami` that returns every claim on `HttpContext.User` as JSON. Useful as a debugging tool during integration tests and in dev environments. The endpoint must:

- Be `[Authorize]` (so it returns `401` for anonymous callers — proves the auth pipeline is wired).
- Return `{ scheme, name, claims: [{ type, value }, ...] }` as JSON.
- Be enabled only in `Development` or `Staging` environments (not Production). Use `app.MapWhen(...)` or an env-conditional `if (app.Environment.IsDevelopment() || app.Environment.IsStaging())`.
- Have one test that asserts the response contains a `ClaimTypes.NameIdentifier` claim with the expected value when authenticated as `"ada"`.

```csharp
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapGet("/api/whoami", (HttpContext ctx) =>
    {
        var user   = ctx.User;
        var scheme = user.Identity?.AuthenticationType;
        var name   = user.Identity?.Name;
        var claims = user.Claims.Select(c => new { type = c.Type, value = c.Value }).ToList();
        return Results.Ok(new { scheme, name, claims });
    })
    .RequireAuthorization();
}
```

**Acceptance criteria.**

- The endpoint exists, returns the full claim set, and is gated behind the env check.
- The test passes against a Development-configured `WebApplicationFactory<T>`.
- The endpoint returns `401` for anonymous, `200` for authenticated.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** This endpoint will be the single most-used debugging tool in your Week 6 mini-project. When a test fails with "expected 200, got 403," hit `/api/whoami` and check what claims your `TestAuthHandler` actually stamped. The bug is usually a missing claim, not a missing policy.

**Estimated time.** 45 minutes.

---

## Problem 6 — Mini reflection essay

**Problem statement.** Write a 300–400 word reflection at `notes/week-06-reflection.md` answering:

1. The lecture argues that "authentication answers who; authorization answers what." After a week of wiring cookie auth, JWT bearer auth, and policy-based authorization, do you agree the two layers are *meaningfully* different? Cite one concrete example from your homework where conflating them would have caused a bug.
2. Where does ASP.NET Core 9's auth surface feel familiar vs different compared to similar systems you've used (Django's `@login_required`, Flask-Login, Spring Security, Express's `passport.js`, Rails' Devise)? Pick one familiar and one different, with concrete examples.
3. Which of cookies vs JWTs would you reach for in *your* next side project? Be honest about whether you would actually wire both. (Most production apps ship both. Most side projects ship one.)
4. What's one thing you'd want to learn next that this week didn't cover? (OIDC? Refresh token rotation? mTLS? API keys?) Week 7 covers OIDC; pick one of the others.

**Acceptance criteria.**

- File exists, 300–400 words.
- Each numbered question is addressed in its own paragraph.
- File is committed.

**Hint.** This is for *you*, not for a grade. Be honest. Future-you reading it after Week 7 (when you swap the local JWT issuer for Keycloak) will be grateful for the honesty about which auth concepts you internalized vs which you copy-pasted.

**Estimated time.** 30 minutes.

---

## Time budget recap

| Problem | Estimated time |
|--------:|--------------:|
| 1 | 30 min |
| 2 | 1 h 0 min |
| 3 | 1 h 15 min |
| 4 | 1 h 30 min |
| 5 | 45 min |
| 6 | 30 min |
| **Total** | **~5 h 30 min** |

When you've finished all six, push your repo and open the [mini-project](./mini-project/README.md). The mini-project takes everything you've practiced this week and adds full cookie + JWT authentication plus policy-based authorization to the Week-5 ledger API — with five named policies, three custom handlers, and a regression-test suite that proves every policy decision.
