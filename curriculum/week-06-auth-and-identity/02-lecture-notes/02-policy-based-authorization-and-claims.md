# Lecture 2 — Policy-Based Authorization and Claims

> **Duration:** ~1.5 hours of reading + hands-on.
> **Outcome:** You can define a named policy in `AddAuthorization(...)`, write a custom `IAuthorizationRequirement` + `AuthorizationHandler<T>` pair, use `IAuthorizationService` for resource-based decisions, override authentication in `WebApplicationFactory<T>` integration tests, and explain — to a code reviewer — why `[Authorize(Policy = "...")]` is the production form and `[Authorize(Roles = "...")]` is the lazy shortcut.

If you only remember one thing from this lecture, remember this:

> **A policy is a list of requirements; a requirement is data; a handler is logic.** `AddAuthorization(options => options.AddPolicy("name", policy => policy.RequireXxx(...)))` is configuration. `IAuthorizationRequirement` is a marker type with the data the policy needs. `AuthorizationHandler<TRequirement>` is the code that decides whether the requirement is satisfied — injectable, testable, single-responsibility. Policies + requirements + handlers replace every `if (user.IsInRole("Admin"))` you would have scattered through your endpoints.

---

## 1. The `[Authorize]` attribute, demystified

The simplest authorization metadata is `[Authorize]`. It is an attribute on an endpoint (or `.RequireAuthorization()` on a route), and the framework's authorization middleware reads it during the pipeline.

```csharp
app.MapGet("/me", (HttpContext ctx) => ctx.User.Identity?.Name)
   .RequireAuthorization();   // any authenticated user
```

That call is shorthand for:

```csharp
.RequireAuthorization(new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build());
```

`RequireAuthorization()` without arguments uses the **default policy** — the one configured at the top of `AddAuthorization(options => options.DefaultPolicy = ...)`. The default's default is `RequireAuthenticatedUser()`. If you only want "any authenticated user," that is enough.

When `[Authorize]` carries a `Policy = "..."`, the middleware looks up the named policy in `AuthorizationOptions.Policies`:

```csharp
[Authorize(Policy = "OwnerOnly")]
public async Task<IResult> EditNote(int id, ...) { ... }
```

There must be an `AddPolicy("OwnerOnly", ...)` registered in `AddAuthorization(...)`. If there is no such policy, the framework throws an `InvalidOperationException` at request time — a noisy failure that points you straight at the missing registration.

`[Authorize]` also accepts `Roles = "..."` and `AuthenticationSchemes = "..."`. The role form is shorthand for a one-line policy:

```csharp
[Authorize(Roles = "Admin")]
public ... // shorthand for [Authorize(Policy = "...")] where the policy is RequireRole("Admin")
```

The two forms are functionally identical. The policy form is preferred in production because the policy name carries *intent* — "OwnerOnly," "MustBeOver18," "InternalUsersOnly" — while a role name carries only the value of one claim.

### `[AllowAnonymous]`

`[AllowAnonymous]` is the override. It says "skip authorization on this endpoint, even if a parent group requires it." Used surgically when a `/health` or `/login` endpoint must be public on an otherwise-authenticated app:

```csharp
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/notes",          (...) => ...);                   // [Authorize] inherited
api.MapPost("/notes",         (...) => ...);                   // [Authorize] inherited
api.MapPost("/auth/login",    (...) => ...).AllowAnonymous();  // override: public
```

The presence of `[AllowAnonymous]` short-circuits the authorization middleware before any policy runs.

---

## 2. Defining a policy

Policies live in `AddAuthorization(...)`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("owner", "true"));

    options.AddPolicy("AdminsOnly", policy =>
        policy.RequireRole("admin"));

    options.AddPolicy("ConfirmedEmail", policy =>
        policy.RequireClaim(ClaimTypes.Email)
              .RequireAssertion(ctx => ctx.User.HasClaim("email_verified", "true")));

    options.AddPolicy("BusinessHoursOnly", policy =>
        policy.RequireAssertion(ctx =>
        {
            var hour = DateTime.UtcNow.Hour;
            return hour >= 9 && hour < 17;
        }));
});
```

Each policy is built with an `AuthorizationPolicyBuilder` that exposes four `Require*` methods:

- `RequireAuthenticatedUser()` — the principal's `Identity.IsAuthenticated` must be `true`. Anonymous fails.
- `RequireRole(string role)` — the principal must have a `ClaimTypes.Role` claim with that value. Multiple roles allowed: `RequireRole("admin", "moderator")` is OR.
- `RequireClaim(string type, params string[] values)` — the principal must have a claim of that type. If `values` is non-empty, the value must match one of them. `RequireClaim("tenant_id")` checks presence; `RequireClaim("tenant_id", "42")` checks a specific value.
- `RequireAssertion(Func<AuthorizationHandlerContext, bool>)` — a predicate over the context. The escape hatch when none of the above fits. **Asynchronous variant:** `RequireAssertion(Func<AuthorizationHandlerContext, Task<bool>>)`.

Policies are AND-composed. `RequireAuthenticatedUser().RequireRole("admin").RequireClaim("tenant_id", "42")` passes only if all three are satisfied.

### The default policy and the fallback policy

`AuthorizationOptions` has two top-level slots:

```csharp
options.DefaultPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();

options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

- `DefaultPolicy` is what `[Authorize]` without a `Policy = "..."` argument uses.
- `FallbackPolicy` is what runs on endpoints that have **no** authorization metadata at all. Setting `FallbackPolicy = RequireAuthenticatedUser()` makes "no `[Authorize]` and no `[AllowAnonymous]`" default to "requires authentication." This is the canonical "deny by default" production posture.

Production .NET 9 apps almost always set `FallbackPolicy = RequireAuthenticatedUser()`. The only reason not to is if your app is mostly public with a few authenticated endpoints — then leaving `FallbackPolicy = null` keeps the default-public posture and lets `[Authorize]` flag the exceptions.

---

## 3. Built-in `Require*` helpers, in detail

### `RequireAuthenticatedUser`

The simplest. Equivalent to:

```csharp
.RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true)
```

Used on its own when "any authenticated user is fine" is the policy.

### `RequireRole`

```csharp
.RequireRole("admin")                           // user must have role "admin"
.RequireRole("admin", "moderator")              // user must have role "admin" OR "moderator"
```

Internally checks `user.IsInRole(role)`, which checks for a `ClaimTypes.Role` claim with the given value. Multiple `RequireRole(...)` calls AND together; multiple values within one call OR together.

### `RequireClaim`

```csharp
.RequireClaim("tenant_id")                      // user must have the claim, any value
.RequireClaim("tenant_id", "42")                // user must have the claim with value "42"
.RequireClaim("tenant_id", "42", "43")          // user must have the claim with value "42" OR "43"
```

### `RequireAssertion`

The escape hatch. Takes a `Func<AuthorizationHandlerContext, bool>` (or `Task<bool>` for async):

```csharp
.RequireAssertion(ctx =>
{
    var ageClaim = ctx.User.FindFirst("account_age_days")?.Value;
    return int.TryParse(ageClaim, out var days) && days >= 30;
})
```

When a policy gets complicated enough that the predicate needs services, graduate to a custom requirement + handler. The threshold is around five lines or one service dependency.

---

## 4. Custom requirements and handlers

When a built-in `Require*` is not enough, write your own. The pattern is two classes:

1. **The requirement** — a marker type that implements `IAuthorizationRequirement` and carries the data the policy needs.
2. **The handler** — an `AuthorizationHandler<TRequirement>` that does the work.

Example: "the user's account must be at least N days old."

```csharp
public sealed class MinimumAccountAgeRequirement(int minDays) : IAuthorizationRequirement
{
    public int MinDays { get; } = minDays;
}

public sealed class MinimumAccountAgeHandler(IClock clock)
    : AuthorizationHandler<MinimumAccountAgeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumAccountAgeRequirement requirement)
    {
        var ageClaim = context.User.FindFirst("account_created_at")?.Value;
        if (!DateTime.TryParse(ageClaim, out var createdAt))
            return Task.CompletedTask;   // requirement not satisfied; do nothing

        var age = clock.UtcNow - createdAt;
        if (age.TotalDays >= requirement.MinDays)
            context.Succeed(requirement); // requirement satisfied

        return Task.CompletedTask;
    }
}

public interface IClock { DateTime UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
```

Three things to notice:

1. The handler **only calls `context.Succeed(requirement)` on success**. Failing is the default. There is also `context.Fail()` for explicit denial — but use it sparingly; the default-fail behavior is usually what you want, because returning without calling `Succeed` leaves the requirement *unsatisfied*, which lets *another handler* for the same requirement (if you registered one) get a chance to satisfy it.
2. The handler **does not throw** on missing claims or parse failures. It returns. The whole point is "this requirement is not satisfied"; that is conveyed by *not* calling `Succeed`, not by an exception.
3. The handler **takes its dependencies through the constructor**. `IClock` is injected by the standard ASP.NET Core DI container. The handler is registered as a singleton by default; if your dependencies are scoped (e.g. a `DbContext`), see §5.

Register the handler and the policy:

```csharp
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IAuthorizationHandler, MinimumAccountAgeHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MinAge30", policy =>
        policy.AddRequirements(new MinimumAccountAgeRequirement(30)));
});
```

Then use it:

```csharp
[Authorize(Policy = "MinAge30")]
public ... CreatePost(...) { ... }
```

### Multiple handlers for one requirement

You can register *multiple* `AuthorizationHandler<TRequirement>` instances for the same requirement type. The framework runs all of them; if *any* of them calls `context.Succeed(requirement)`, the requirement is satisfied. This is the canonical "OR" pattern at the handler level:

```csharp
public sealed class OwnerRequirement : IAuthorizationRequirement;

public sealed class OwnerHandler(IHttpContextAccessor ctxAccessor)
    : AuthorizationHandler<OwnerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement)
    {
        // ... check whether the user owns the resource ...
        // context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class AdminBypassHandler : AuthorizationHandler<OwnerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement)
    {
        if (context.User.IsInRole("admin"))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

Both handlers run. The user passes the "owner" check if they own the resource *or* if they are an admin. This is much cleaner than baking the admin-bypass into the `OwnerHandler` directly.

---

## 5. Scoped dependencies in handlers

Authorization handlers are registered as singletons by default. If your handler needs a *scoped* service — a `DbContext`, a `IHttpContextAccessor`, a per-request cache — you have two options:

**Option A** — register the handler itself as scoped:

```csharp
builder.Services.AddScoped<IAuthorizationHandler, OwnerHandler>();
```

Now the handler can take a `DbContext` directly. The framework resolves a fresh handler per request.

**Option B** — keep the handler singleton, inject `IServiceScopeFactory`, open a scope inside `HandleRequirementAsync`:

```csharp
public sealed class OwnerHandler(IServiceScopeFactory scopeFactory)
    : AuthorizationHandler<OwnerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // ... use db ...
    }
}
```

Option A is simpler. Option B keeps the handler itself a singleton (slightly faster to dispatch) and is what the framework documentation recommends for handlers that resolve scoped services infrequently. Both work. Pick Option A and don't think about it unless profiling tells you to.

---

## 6. Resource-based authorization

Some policies cannot be expressed as `[Authorize]` because they depend on the *specific resource* being accessed. "May this user edit note 42?" requires knowing both the user *and* note 42. `[Authorize]` runs before the endpoint loads the resource. You need a way to authorize *after* the resource is loaded.

The pattern: `IAuthorizationService.AuthorizeAsync(user, resource, "policy")`.

```csharp
app.MapPut("/api/notes/{id:int}", async (
    int id,
    NoteUpdate update,
    HttpContext ctx,
    AppDbContext db,
    IAuthorizationService authz) =>
{
    var note = await db.Notes.FindAsync(id);
    if (note is null) return Results.NotFound();

    var result = await authz.AuthorizeAsync(ctx.User, note, "EditNote");
    if (!result.Succeeded) return Results.Forbid();

    note.Title = update.Title;
    note.Body  = update.Body;
    await db.SaveChangesAsync();
    return Results.Ok(note);
})
.RequireAuthorization();   // outer [Authorize] still applies — must be authenticated to get here
```

The policy "EditNote" uses an `OperationAuthorizationRequirement` (a built-in marker for resource-based ops) and a handler that *takes the resource as a parameter*:

```csharp
public sealed class EditNoteHandler
    : AuthorizationHandler<OperationAuthorizationRequirement, Note>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        Note resource)
    {
        if (requirement.Name != "EditNote") return Task.CompletedTask;

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is not null && resource.OwnerId == userId)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

And the policy:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("EditNote", policy =>
        policy.AddRequirements(new OperationAuthorizationRequirement { Name = "EditNote" }));
});

builder.Services.AddScoped<IAuthorizationHandler, EditNoteHandler>();
```

The `AuthorizationHandler<TRequirement, TResource>` overload lets the handler receive the resource. Inside, you can check ownership, tenant, status, anything that depends on the resource.

**The reflex.** When the policy is "may this user do X?" with no resource, use `[Authorize(Policy = "...")]`. When the policy is "may this user do X *to this specific thing*?" use `IAuthorizationService.AuthorizeAsync` inside the handler.

---

## 7. The authorization result

`IAuthorizationService.AuthorizeAsync(...)` returns an `AuthorizationResult`:

```csharp
public sealed class AuthorizationResult
{
    public bool Succeeded { get; }
    public AuthorizationFailure? Failure { get; }
}

public sealed class AuthorizationFailure
{
    public bool FailCalled { get; }                                    // context.Fail() was called
    public IEnumerable<IAuthorizationRequirement> FailedRequirements { get; }
    public IEnumerable<AuthorizationFailureReason> FailureReasons { get; }
}
```

In an endpoint, the canonical pattern is:

```csharp
var result = await authz.AuthorizeAsync(ctx.User, resource, "policy");
return result.Succeeded
    ? Results.Ok(...)
    : Results.Forbid();
```

`Results.Forbid()` issues a `403 Forbidden`. If the user is not authenticated at all, the outer `.RequireAuthorization()` will have already issued a `401 Unauthorized`, so by the time you call `AuthorizeAsync`, you can assume the user is authenticated and the only failure mode is a policy denial.

You can attach failure reasons in your handler for debugging (`context.Fail(new AuthorizationFailureReason(this, "user is not the owner"))`), but never expose these to the client — leaking *why* authorization failed gives attackers a search path.

---

## 8. Testing authorization with `WebApplicationFactory<T>`

The highest-ROI tests in the codebase are the ones that prove every `403 Forbidden` is `403 Forbidden`. They are also the easiest to forget. The pattern:

```csharp
public sealed class LedgerApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            services.Configure<AuthorizationOptions>(opts =>
            {
                opts.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                    .RequireAuthenticatedUser()
                    .Build();
            });
        });
    }
}
```

`TestAuthHandler` is a custom `AuthenticationHandler<AuthenticationSchemeOptions>` that reads test-only headers and produces a `ClaimsPrincipal`:

```csharp
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = userValues.ToString();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name,           userId),
        };

        if (Request.Headers.TryGetValue("X-Test-Roles", out var rolesValues))
            foreach (var role in rolesValues.ToString().Split(','))
                claims.Add(new Claim(ClaimTypes.Role, role));

        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

A test that exercises the policy:

```csharp
public sealed class NoteAuthTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    [Fact]
    public async Task EditNote_AsOwner_Returns200()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "alice");

        var response = await client.PutAsJsonAsync("/api/notes/1", new { Title = "New", Body = "Body" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EditNote_AsNonOwner_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "bob");

        var response = await client.PutAsJsonAsync("/api/notes/1", new { Title = "Hacked", Body = "Lol" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditNote_AsAnonymous_Returns401()
    {
        var client = factory.CreateClient();
        // no X-Test-User header

        var response = await client.PutAsJsonAsync("/api/notes/1", new { Title = "Hacked", Body = "Lol" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Three tests. One for the happy path (`200`), one for the policy-denial path (`403`), one for the unauthenticated path (`401`). Every authenticated endpoint deserves all three. They are fast, deterministic, and they catch the regressions that production users actually hit.

---

## 9. Common policy shapes

A short catalogue of policies that show up in production .NET 9 codebases:

### "Owner only"

```csharp
options.AddPolicy("OwnerOnly", policy =>
    policy.RequireAssertion(ctx =>
    {
        var userId    = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var ownerArg  = ctx.Resource as Note;
        return userId is not null && ownerArg?.OwnerId == userId;
    }));
```

For resource-based; see §6 for the cleaner handler form.

### "Tenant scoped"

```csharp
options.AddPolicy("SameTenant", policy =>
    policy.RequireAssertion(ctx =>
    {
        var userTenant     = ctx.User.FindFirst("tenant_id")?.Value;
        var resourceTenant = (ctx.Resource as ITenanted)?.TenantId;
        return userTenant is not null && resourceTenant == userTenant;
    }));
```

The multi-tenant version of "owner only." Challenge 1 builds this in detail.

### "Account confirmed"

```csharp
options.AddPolicy("Confirmed", policy =>
    policy.RequireClaim("email_verified", "true"));
```

A simple claim check.

### "Admin or moderator"

```csharp
options.AddPolicy("Moderation", policy =>
    policy.RequireRole("admin", "moderator"));
```

Multiple roles in one call is OR.

### "Admin AND business hours"

```csharp
options.AddPolicy("AdminDuringHours", policy =>
{
    policy.RequireRole("admin");
    policy.RequireAssertion(ctx =>
    {
        var hour = DateTime.UtcNow.Hour;
        return hour >= 9 && hour < 17;
    });
});
```

Multiple requirements in one policy is AND.

---

## 10. The minimal-API authorization surface

In minimal APIs, `.RequireAuthorization(...)` is the entry point:

```csharp
app.MapGet("/me",         (...) => ...).RequireAuthorization();
app.MapGet("/admin",      (...) => ...).RequireAuthorization("AdminsOnly");
app.MapPost("/notes",     (...) => ...).RequireAuthorization("ConfirmedEmail");
```

The variants:

- `.RequireAuthorization()` — apply the default policy.
- `.RequireAuthorization("name")` — apply a named policy.
- `.RequireAuthorization(new AuthorizationPolicyBuilder().RequireXxx(...).Build())` — apply an inline-built policy.
- `.AllowAnonymous()` — override an outer `RequireAuthorization()` on a route group.

A common production pattern is to group routes and apply auth at the group level:

```csharp
var publicRoutes = app.MapGroup("/").AllowAnonymous();
publicRoutes.MapPost("/auth/login",    (...) => ...);
publicRoutes.MapPost("/auth/register", (...) => ...);
publicRoutes.MapGet("/health",         (...) => ...);

var apiRoutes = app.MapGroup("/api").RequireAuthorization();
apiRoutes.MapGet("/notes",                (...) => ...);
apiRoutes.MapPost("/notes",               (...) => ...);
apiRoutes.MapPut("/notes/{id:int}",       (...) => ...).RequireAuthorization("OwnerOnly");

var adminRoutes = app.MapGroup("/admin").RequireAuthorization("AdminsOnly");
adminRoutes.MapGet("/users", (...) => ...);
```

This reads top-to-bottom as a security review checklist: "the `/api` group is authenticated; the `/admin` group is admins-only; the edit note endpoint requires the owner." A reviewer can confirm the policy in seconds.

---

## 11. What to read next

- The **`AuthorizationPolicy` source** — small, readable: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authorization/Core/src/AuthorizationPolicy.cs>
- The **`AuthorizationHandler<T>` source** — even smaller: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authorization/Core/src/AuthorizationHandlerOfT.cs>
- The **`DefaultAuthorizationService` source** — the class that actually runs your handlers: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authorization/Core/src/DefaultAuthorizationService.cs>
- **Andrew Lock — "Custom authorization policies and requirements"** — the canonical walk-through: <https://andrewlock.net/custom-authorisation-policies-and-requirements-in-asp-net-core/>
- **Microsoft Learn — "Resource-based authorization"** — the official article on the resource handler pattern: <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>

Next: the **mini-project**. You will take the Week-5 ledger API, add cookie authentication, add JWT bearer authentication, add ASP.NET Core Identity, define five named policies, write three custom handlers, and prove every policy decision with integration tests. The same `[Authorize(Policy = "...")]` attribute will work on the browser-facing surface (cookies) and the API surface (JWT). That is the .NET 9 production posture.

---

*Build succeeded · 0 warnings · 0 errors · Lecture 2 complete.*
