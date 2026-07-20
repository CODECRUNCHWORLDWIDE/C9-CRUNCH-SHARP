# Challenge 1 — Multi-Tenant Authorization

> Build a multi-tenant authorization layer on top of the JWT-bearer + EF Core stack from this week. Every JWT carries a `tenant_id` claim; every `Note` row carries a `TenantId` column; every read, update, and delete enforces "the caller's `tenant_id` claim must equal the resource's `TenantId`." Write a regression test that proves a user on tenant `acme` *cannot* read a note belonging to tenant `globex`, even when they guess the note's id correctly. Then write a 200-word note explaining why the `tenant_id` claim must come from a trusted source (the JWT or the database), never from the request body.

**Estimated time:** ~2 hours.

---

## Why this exists

Multi-tenant authorization is the most-asked authorization question in any SaaS code review and the most-leaked authorization surface in production. The shape is always the same: one database, many tenants, every row tagged with a tenant id, every query filtered by that tenant id. Forget the filter once — or trust the tenant id from the wrong source — and tenant A reads tenant B's data. The bug is silent (no error, no log line), it does not show up in load tests, and it is usually found by a customer.

This challenge codifies the reflex: a `TenantClaimRequirement` requirement, a `TenantClaimHandler<TResource>` handler, an `ITenanted` interface every multi-tenant entity implements, and an integration test that proves cross-tenant access returns `403 Forbidden`. After this challenge you will reach for the requirement-and-handler pattern every time a code review surfaces a tenant check, and you will write the test before the endpoint.

---

## Phase 1 — Scaffold (~20 min)

Start from the JWT exercise (Exercise 2) or the mini-project skeleton. The solution needs:

- An ASP.NET Core 9 minimal API with JWT bearer authentication wired (per Lecture 1).
- EF Core (`Microsoft.EntityFrameworkCore.Sqlite` is fine) with one entity: `Note`.
- A test project referencing `Microsoft.AspNetCore.Mvc.Testing`.

```bash
mkdir TenantAuthz && cd TenantAuthz
dotnet new sln -n TenantAuthz
dotnet new web   -n TenantAuthz.Api   -o src/TenantAuthz.Api
dotnet new xunit -n TenantAuthz.Tests -o tests/TenantAuthz.Tests
dotnet sln add src/TenantAuthz.Api tests/TenantAuthz.Tests

cd src/TenantAuthz.Api
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
cd ../..

dotnet add tests/TenantAuthz.Tests reference src/TenantAuthz.Api
dotnet add tests/TenantAuthz.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/TenantAuthz.Tests package Microsoft.EntityFrameworkCore.InMemory
```

Add a `Directory.Build.props` at the root:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Commit: `Skeleton: API + tests + EF Core`.

## Phase 2 — Domain and persistence (~20 min)

Add an `ITenanted` marker interface and a `Note` entity:

```csharp
public interface ITenanted
{
    string TenantId { get; }
}

public sealed class Note : ITenanted
{
    public int    Id       { get; set; }
    public string OwnerId  { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Title    { get; set; } = "";
    public string Body     { get; set; } = "";
}
```

The `DbContext`:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Note>().HasIndex(n => n.TenantId);
    }
}
```

Seed two tenants in `Program.cs`:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    if (!db.Notes.Any())
    {
        db.Notes.AddRange(
            new Note { Id = 1, OwnerId = "ada",   TenantId = "acme",   Title = "Acme TODO",   Body = "..." },
            new Note { Id = 2, OwnerId = "linus", TenantId = "globex", Title = "Globex TODO", Body = "..." });
        db.SaveChanges();
    }
}
```

Commit: `Domain: Note + ITenanted + seed`.

## Phase 3 — The requirement and the handler (~30 min)

In `src/TenantAuthz.Api/Authz/TenantClaimRequirement.cs`:

```csharp
public sealed class TenantClaimRequirement : IAuthorizationRequirement;

public sealed class TenantClaimHandler<TResource>
    : AuthorizationHandler<TenantClaimRequirement, TResource>
    where TResource : ITenanted
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantClaimRequirement requirement,
        TResource resource)
    {
        var userTenant = context.User.FindFirst("tenant_id")?.Value;
        if (userTenant is null)        return Task.CompletedTask;
        if (resource.TenantId is null) return Task.CompletedTask;

        if (string.Equals(userTenant, resource.TenantId, StringComparison.Ordinal))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
```

Three things to notice:

1. The handler **is generic** in the resource type. C# generics on authorization handlers work: `services.AddSingleton<IAuthorizationHandler, TenantClaimHandler<Note>>()` registers the handler for `Note` resources specifically.
2. The user's tenant comes from a **claim**, not from the request body. The JWT issuer (Identity in production; the test handler in tests) is the only trusted source.
3. The resource's tenant comes from the **loaded entity**, not from a route parameter. The route parameter is the resource id; the entity is loaded from the database; the database is trusted.

Register the policy and the handler:

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, TenantClaimHandler<Note>>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SameTenant", policy =>
        policy.AddRequirements(new TenantClaimRequirement()));
});
```

Commit: `Authz: TenantClaimRequirement + TenantClaimHandler<T>`.

## Phase 4 — The endpoints (~20 min)

```csharp
app.MapGet("/api/notes/{id:int}", async (
    int id,
    HttpContext ctx,
    AppDbContext db,
    IAuthorizationService authz) =>
{
    var note = await db.Notes.FindAsync(id);
    if (note is null) return Results.NotFound();

    var result = await authz.AuthorizeAsync(ctx.User, note, "SameTenant");
    if (!result.Succeeded) return Results.Forbid();

    return Results.Ok(note);
})
.RequireAuthorization();

app.MapDelete("/api/notes/{id:int}", async (
    int id,
    HttpContext ctx,
    AppDbContext db,
    IAuthorizationService authz) =>
{
    var note = await db.Notes.FindAsync(id);
    if (note is null) return Results.NotFound();

    var result = await authz.AuthorizeAsync(ctx.User, note, "SameTenant");
    if (!result.Succeeded) return Results.Forbid();

    db.Notes.Remove(note);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
.RequireAuthorization();
```

Two endpoints, same pattern: load resource, call `IAuthorizationService.AuthorizeAsync(user, resource, policy)`, branch on the result. The outer `.RequireAuthorization()` enforces "must be authenticated"; the inner `AuthorizeAsync` enforces "must be on the right tenant."

Commit: `Endpoints: GET/DELETE /api/notes/{id} with tenant check`.

## Phase 5 — The regression test (~30 min)

This is the load-bearing test. Without it, the bug is invisible.

```csharp
public sealed class TenantIsolationTests(TenantAuthzFactory factory)
    : IClassFixture<TenantAuthzFactory>
{
    [Fact]
    public async Task AcmeUser_CanReadAcmeNote()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User",   "ada");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "acme");

        var response = await client.GetAsync("/api/notes/1"); // acme note

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AcmeUser_CannotReadGlobexNote_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User",   "ada");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "acme");

        var response = await client.GetAsync("/api/notes/2"); // globex note

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcmeUser_CannotDeleteGlobexNote_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User",   "ada");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "acme");

        var response = await client.DeleteAsync("/api/notes/2"); // globex note

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithNoTenantClaim_Returns403()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "ghost");
        // no X-Test-Tenant header

        var response = await client.GetAsync("/api/notes/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousUser_Returns401()
    {
        var client = factory.CreateClient();
        // no headers at all

        var response = await client.GetAsync("/api/notes/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Five tests. The crucial one is `AcmeUser_CannotReadGlobexNote_Returns403` — the regression test that prevents tenant data leaks. If this test ever turns green when it should be red, **stop, audit every handler, and ship a fix before the next deploy**. This single test is worth more than the rest of the auth suite combined.

The factory uses a `TestAuthHandler` that stamps both `ClaimTypes.NameIdentifier` (from `X-Test-User`) and `tenant_id` (from `X-Test-Tenant`) into the principal. Reuse the pattern from Exercise 3.

Commit: `Tests: tenant isolation regression suite (5 tests)`.

## Phase 6 — The 200-word note (~15 min)

Create `notes/tenant-claim-source.md` and write 200 words answering:

1. **Why must the `tenant_id` claim come from the JWT or the database, never from the request body?** Hint: anything from the request body is attacker-controlled. A user on tenant `acme` who sends `{ "tenant_id": "globex" }` would otherwise grant themselves access to a different tenant. The JWT is signed (the claim cannot be forged without the key). The database is the source of truth (no one can change it without going through your update endpoints, which themselves enforce the policy).
2. **Where does the JWT's `tenant_id` claim come from?** It comes from the user's row in `AspNetUsers` (or a related `AspNetUserClaims` row, or a `UserTenants` join table you maintain). The sign-in endpoint reads the user's tenant from the database and includes it in the JWT. The JWT issuer is the only place tenant assignment happens; tenant assignment never happens at request time.
3. **What about API keys and service-to-service callers?** Same shape, different issuer. Each API key is bound to one tenant; the API key validator stamps `tenant_id` into the principal it produces. The downstream policy is identical.

Commit: `Notes: tenant-claim-source.md`.

## Phase 7 — Stretch (optional, ~30 min)

- **EF Core global query filter.** Add `b.Entity<Note>().HasQueryFilter(n => n.TenantId == _currentTenant);` so every `db.Notes` query is scoped to the current tenant automatically. The handler still runs as a second line of defense; the filter prevents the rest of the codebase from accidentally bypassing the check. Inject an `ITenantContext` that reads `tenant_id` off `IHttpContextAccessor.HttpContext.User`.
- **Audit log.** Every `AuthorizeAsync` denial writes a row to an `AuditLog` table: who tried, what they tried, when. Useful for forensic investigation later.
- **Soft tenant transfer.** Add an admin-only `POST /admin/notes/{id}/transfer-tenant` endpoint that moves a note from one tenant to another, gated by `[Authorize(Policy = "AdminsOnly")]`. Note the difference: the admin role bypasses the per-tenant check; their tenant claim is "admin" but the policy that *only* admins can transfer is a separate policy.

---

## Acceptance criteria

- [ ] `TenantClaimRequirement` and `TenantClaimHandler<TResource> where TResource : ITenanted` exist.
- [ ] The handler reads `tenant_id` from `context.User`, NOT from the request.
- [ ] The handler returns silently on a missing claim — never throws.
- [ ] `Note : ITenanted` carries a `TenantId` property.
- [ ] At least 2 endpoints (GET and DELETE) call `IAuthorizationService.AuthorizeAsync(user, note, "SameTenant")`.
- [ ] 5 tests pass: same-tenant 200, cross-tenant GET 403, cross-tenant DELETE 403, no-tenant-claim 403, anonymous 401.
- [ ] `notes/tenant-claim-source.md` is 180–220 words and addresses the three questions in Phase 6.
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Requirement + handler | 25% | Generic over `TResource`, claim source documented, no exceptions on bad input |
| Endpoint integration | 20% | At least 2 endpoints use `IAuthorizationService.AuthorizeAsync`; both load the resource first |
| Tests | 30% | All 5 tests pass; the cross-tenant test is named explicitly so it cannot be removed by accident |
| 200-word note | 15% | Answers all three questions; identifies the JWT as the trust boundary |
| Code hygiene | 10% | `TreatWarningsAsErrors = true`; no `SuppressMessage` to silence warnings; nullable annotations correct |

## What this prepares you for

- **Week 7** introduces OpenID Connect. The `tenant_id` claim flow is identical when the issuer is Keycloak — you just configure Keycloak to include the claim in its tokens; the handler does not change.
- **Week 8** introduces SignalR. A live-updates hub for a multi-tenant SaaS must scope subscriptions per tenant; the same `TenantClaimRequirement` applies on hub method invocations.
- **The capstone** (Week 15+) is a multi-tenant SaaS by default. Every entity in the capstone schema is `ITenanted`; every policy uses the requirement you wrote here.

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes a one-paragraph description, the `dotnet test` command, and a link to `notes/tenant-claim-source.md`.
3. Make sure `dotnet build` and `dotnet test` are green on a fresh clone.
4. Post the repo URL in your cohort tracker. You shipped a multi-tenant authorization layer with a regression test; show it.
