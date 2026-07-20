# Mini-Project — Sharp Notes Auth

> Take the ledger API you shipped in Week 5's mini-project and put a door in front of it. Add cookie authentication for the browser-facing surface, JWT bearer authentication for the API surface, ASP.NET Core Identity for the user store, five named policies, three custom authorization handlers, and an integration test suite that proves every policy decision — including the `403 Forbidden` paths. By the end you have an ASP.NET Core 9 API that any production team would recognize: deny-by-default authorization, scheme-tagged endpoints, testable policy handlers, and zero hardcoded credentials.

This is the canonical "add auth to an existing service" exercise in ASP.NET Core 9. Production codebases have hundreds of endpoints that were written without auth in mind; bolting auth on after the fact is exactly the work a senior engineer does on their first month at a new shop. Doing it deliberately — policies first, tests next, endpoints last — is the single best way to internalize the modern stack.

**Estimated time:** ~8.5 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A console + library combo called `SharpNotes` that:

1. Starts from the Week-5 ledger API (or an equivalent ASP.NET Core 9 minimal API on top of EF Core).
2. Adds cookie authentication for the human-facing surface (a tiny HTML sign-in page is acceptable; a JSON-only sign-in endpoint is even better).
3. Adds JWT bearer authentication for the API surface, signed with an HS256 symmetric key loaded from `dotnet user-secrets`.
4. Adds ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`) on top of an EF Core `IdentityDbContext<IdentityUser>` for a real user store with password hashing.
5. Defines **five** named authorization policies, each with intent: `MustBeAuthenticated`, `MustBeAdmin`, `MustOwnNote`, `MustBeConfirmed`, `MustBeOver30DaysOld`.
6. Implements **three** custom `IAuthorizationRequirement` + `AuthorizationHandler<T>` pairs for the policies that don't fit `RequireRole`/`RequireClaim`: ownership (resource-based), account age (claim + clock-based), email confirmation (claim-based but with a deliberately-non-trivial check).
7. Ships an integration test suite that proves, for every policy-protected endpoint, all three outcomes: `200 OK` for the allowed caller, `403 Forbidden` for the disallowed caller, `401 Unauthorized` for the anonymous caller. At least **30 tests total**.

You ship **one solution** with five projects:

- `src/SharpNotes.Core/` — domain entities (`Note`, `IdentityUser` extensions), `ITenanted`-style interfaces, the `IClock` abstraction.
- `src/SharpNotes.Api/` — the ASP.NET Core 9 minimal API, with all auth/authz wiring.
- `src/SharpNotes.Auth/` — the requirements, handlers, and policy registrations. Standalone library so the test project can reach in.
- `src/SharpNotes.Data/` — the `IdentityDbContext<IdentityUser>` subclass and EF Core migrations.
- `tests/SharpNotes.Tests/` — `WebApplicationFactory<Program>` fixtures, `TestAuthHandler`, and the 30+ integration tests.

---

## Rules

- **You may** read Microsoft Learn, the `dotnet/aspnetcore` source, lecture notes, your Week 6 exercises, and the source of the libraries listed below.
- **You may NOT** depend on any third-party identity provider, IdentityServer/Duende, OIDC server, or external auth SaaS. Local accounts only; the JWT issuer is your own sign-in endpoint. Week 7 introduces external IdPs.
- **You may NOT** depend on any third-party NuGet package other than:
  - `Microsoft.AspNetCore.Authentication.JwtBearer`
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Sqlite` (or `Sqlite` if you prefer SQLite for dev; `Postgres` if you prefer to mirror Week 5's persistence story)
  - `System.IdentityModel.Tokens.Jwt`
  - `xUnit`, `Microsoft.NET.Test.Sdk`, and `Microsoft.AspNetCore.Mvc.Testing` for tests.
  - `Microsoft.EntityFrameworkCore.InMemory` if you prefer an in-memory store for tests (recommended).
- Target framework: `net9.0`. C# language version: the default for the SDK (`13.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.

---

## Acceptance criteria

- [ ] A new public GitHub repo named `c9-week-06-sharp-notes-auth-<yourhandle>`.
- [ ] Solution layout:
  ```
  SharpNotes/
  ├── SharpNotes.sln
  ├── .gitignore
  ├── Directory.Build.props
  ├── src/
  │   ├── SharpNotes.Core/
  │   │   ├── SharpNotes.Core.csproj
  │   │   ├── Note.cs
  │   │   ├── IClock.cs
  │   │   └── SystemClock.cs
  │   ├── SharpNotes.Auth/
  │   │   ├── SharpNotes.Auth.csproj
  │   │   ├── Requirements/
  │   │   │   ├── MinimumAccountAgeRequirement.cs
  │   │   │   ├── EmailConfirmedRequirement.cs
  │   │   │   └── NoteOwnerRequirement.cs
  │   │   ├── Handlers/
  │   │   │   ├── MinimumAccountAgeHandler.cs
  │   │   │   ├── EmailConfirmedHandler.cs
  │   │   │   └── NoteOwnerHandler.cs
  │   │   └── AuthorizationServiceCollectionExtensions.cs
  │   ├── SharpNotes.Data/
  │   │   ├── SharpNotes.Data.csproj
  │   │   ├── AppDbContext.cs
  │   │   └── Migrations/...
  │   └── SharpNotes.Api/
  │       ├── SharpNotes.Api.csproj
  │       ├── Program.cs
  │       └── Endpoints/
  │           ├── AuthEndpoints.cs
  │           └── NoteEndpoints.cs
  └── tests/
      └── SharpNotes.Tests/
          ├── SharpNotes.Tests.csproj
          ├── SharpNotesApiFactory.cs
          ├── TestAuthHandler.cs
          ├── AuthTests.cs
          ├── PolicyTests.cs
          ├── ResourceAuthTests.cs
          └── EndpointSmokeTests.cs
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports **at least 30** passing tests, including:
  - The 3 outcomes (200, 403, 401) for at least 5 policy-protected endpoints (15 tests).
  - At least 5 sign-in / sign-out tests (cookie path).
  - At least 5 JWT mint-and-validate tests, including a tampered-token test.
  - At least 5 resource-based authorization tests (NoteOwner-style).
- [ ] `dotnet run --project src/SharpNotes.Api` starts the API on `https://localhost:5001`. The OpenAPI document (if you choose to add `Microsoft.AspNetCore.OpenApi`) exposes the auth-protected endpoints with their schemes documented.
- [ ] The signing key for JWTs lives in `dotnet user-secrets`. No JWT key is in source.
- [ ] The five named policies are registered in `AddAuthorization(...)`. Each is named with intent. None is named `Policy1` or `RequireAdmin` (use `MustBeAdmin`); names that read like sentences.
- [ ] The three custom requirements each have a corresponding `AuthorizationHandler<T>` and are registered with the DI container.
- [ ] The handler for `NoteOwner` is **resource-based** — called via `IAuthorizationService.AuthorizeAsync(user, note, "MustOwnNote")` inside the endpoint, not via `[Authorize(Policy = "MustOwnNote")]`.
- [ ] Sign-in supports **both** cookie issuance (`POST /auth/login`) and JWT issuance (`POST /auth/token`). The same Identity user store backs both.
- [ ] The `FallbackPolicy` is set to `RequireAuthenticatedUser()` (deny by default). Endpoints that should be public have `.AllowAnonymous()` explicitly.
- [ ] `README.md` in the repo root includes:
  - One paragraph describing the project.
  - The exact commands to clone, build, test, and run with the sample data.
  - A "Threat model" section (200–300 words) describing what the auth layer protects and what it does not (e.g. "this layer protects against cross-user data access; it does not protect against denial of service or supply-chain attacks on the JWT signing key").
  - A "Five policies" section listing each policy, what it requires, and the endpoints it protects.

---

## Suggested order of operations

You'll find it easier if you build incrementally rather than trying to write the whole thing at once.

### Phase 1 — Solution skeleton (~30 min)

```bash
mkdir SharpNotes && cd SharpNotes
dotnet new sln -n SharpNotes
dotnet new gitignore && git init

dotnet new classlib -n SharpNotes.Core -o src/SharpNotes.Core
dotnet new classlib -n SharpNotes.Auth -o src/SharpNotes.Auth
dotnet new classlib -n SharpNotes.Data -o src/SharpNotes.Data
dotnet new web      -n SharpNotes.Api  -o src/SharpNotes.Api
dotnet new xunit    -n SharpNotes.Tests -o tests/SharpNotes.Tests

# Add references; see Acceptance criteria for the wiring.
dotnet add src/SharpNotes.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/SharpNotes.Api package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add src/SharpNotes.Api package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/SharpNotes.Api package System.IdentityModel.Tokens.Jwt
dotnet add tests/SharpNotes.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/SharpNotes.Tests package Microsoft.EntityFrameworkCore.InMemory
```

Add `Directory.Build.props`:

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

Commit: `Skeleton: 5 projects + Directory.Build.props`.

### Phase 2 — Identity + DbContext (~45 min)

In `SharpNotes.Data/AppDbContext.cs`:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Note> Notes => Set<Note>();
}
```

In `SharpNotes.Core/Note.cs`:

```csharp
public sealed class Note
{
    public int    Id      { get; set; }
    public string OwnerId { get; set; } = "";
    public string Title   { get; set; } = "";
    public string Body    { get; set; } = "";
}
```

Wire EF Core + Identity in `Program.cs`:

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=sharp-notes.db"));

builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength       = 12;
        options.Password.RequireDigit         = true;
        options.Password.RequireLowercase     = true;
        options.Password.RequireUppercase     = true;
        options.User.RequireUniqueEmail       = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
```

Add the initial migration:

```bash
cd src/SharpNotes.Api
dotnet tool install --global dotnet-ef    # if you haven't already
dotnet ef migrations add InitialCreate    --project ../SharpNotes.Data --startup-project .
dotnet ef database update                 --project ../SharpNotes.Data --startup-project .
cd ../..
```

Commit: `Identity + DbContext + InitialCreate migration`.

### Phase 3 — Cookie + JWT authentication (~45 min)

Wire both schemes in `Program.cs`:

```csharp
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme        = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name         = "C9.Auth";
        options.Cookie.HttpOnly     = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite     = SameSiteMode.Lax;
        options.ExpireTimeSpan      = TimeSpan.FromHours(8);
        options.SlidingExpiration   = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
        };
    })
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key not configured.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = "https://c9.local",
            ValidateAudience         = true,
            ValidAudience            = "sharp-notes",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
        };
    });
```

Generate and store the JWT key:

```bash
cd src/SharpNotes.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 32)"
cd ../..
```

Commit: `Cookie + JWT auth schemes wired`.

### Phase 4 — The five named policies (~45 min)

In `SharpNotes.Auth/AuthorizationServiceCollectionExtensions.cs`:

```csharp
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddSharpNotesAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, MinimumAccountAgeHandler>();
        services.AddSingleton<IAuthorizationHandler, EmailConfirmedHandler>();
        services.AddScoped  <IAuthorizationHandler, NoteOwnerHandler>();

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy("MustBeAuthenticated", p => p.RequireAuthenticatedUser());

            options.AddPolicy("MustBeAdmin", p =>
                p.RequireAuthenticatedUser().RequireRole("admin"));

            options.AddPolicy("MustOwnNote", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new NoteOwnerRequirement()));

            options.AddPolicy("MustBeConfirmed", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new EmailConfirmedRequirement()));

            options.AddPolicy("MustBeOver30DaysOld", p =>
                p.RequireAuthenticatedUser()
                 .AddRequirements(new MinimumAccountAgeRequirement(30)));
        });

        return services;
    }
}
```

Then `builder.Services.AddSharpNotesAuthorization();` in `Program.cs`.

Commit: `Five named policies + handler registrations`.

### Phase 5 — Endpoints (~75 min)

`SharpNotes.Api/Endpoints/AuthEndpoints.cs`:

```csharp
public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/auth").AllowAnonymous();

        grp.MapPost("/register", async (RegisterRequest req,
            UserManager<IdentityUser> users) =>
        {
            var user = new IdentityUser { UserName = req.Username, Email = req.Email };
            var result = await users.CreateAsync(user, req.Password);
            return result.Succeeded
                ? Results.Created($"/users/{user.Id}", new { user.Id, user.UserName })
                : Results.ValidationProblem(
                    result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        });

        grp.MapPost("/login", async (LoginRequest req,
            UserManager<IdentityUser> users,
            SignInManager<IdentityUser> signIn) =>
        {
            var user = await users.FindByNameAsync(req.Username);
            if (user is null) return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(user, req.Password,
                isPersistent: false, lockoutOnFailure: true);
            return result.Succeeded
                ? Results.Ok(new { user = user.UserName })
                : Results.Unauthorized();
        });

        grp.MapPost("/token", async (LoginRequest req,
            UserManager<IdentityUser> users,
            IConfiguration config) =>
        {
            var user = await users.FindByNameAsync(req.Username);
            if (user is null) return Results.Unauthorized();
            if (!await users.CheckPasswordAsync(user, req.Password)) return Results.Unauthorized();

            // ... build claims, sign, return ...
        });

        grp.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });
    }
}

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record LoginRequest(string Username, string Password);
```

`SharpNotes.Api/Endpoints/NoteEndpoints.cs`:

```csharp
public static class NoteEndpoints
{
    public static void MapNotes(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/notes").RequireAuthorization("MustBeAuthenticated");

        grp.MapGet("",       async (AppDbContext db) =>
            Results.Ok(await db.Notes.ToListAsync()));

        grp.MapGet("/{id:int}", async (
            int id, HttpContext ctx, AppDbContext db, IAuthorizationService authz) =>
        {
            var note = await db.Notes.FindAsync(id);
            if (note is null) return Results.NotFound();
            var result = await authz.AuthorizeAsync(ctx.User, note, "MustOwnNote");
            return result.Succeeded ? Results.Ok(note) : Results.Forbid();
        });

        grp.MapPost("", async (
            CreateNoteRequest req, HttpContext ctx, AppDbContext db) =>
        {
            var ownerId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("No user id.");
            var note = new Note { OwnerId = ownerId, Title = req.Title, Body = req.Body };
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            return Results.Created($"/api/notes/{note.Id}", note);
        }).RequireAuthorization("MustBeConfirmed");

        grp.MapPut("/{id:int}", async (
            int id, NoteUpdate update, HttpContext ctx, AppDbContext db,
            IAuthorizationService authz) =>
        {
            var note = await db.Notes.FindAsync(id);
            if (note is null) return Results.NotFound();
            var result = await authz.AuthorizeAsync(ctx.User, note, "MustOwnNote");
            if (!result.Succeeded) return Results.Forbid();
            note.Title = update.Title;
            note.Body  = update.Body;
            await db.SaveChangesAsync();
            return Results.Ok(note);
        });

        grp.MapDelete("/{id:int}", async (
            int id, HttpContext ctx, AppDbContext db,
            IAuthorizationService authz) =>
        {
            var note = await db.Notes.FindAsync(id);
            if (note is null) return Results.NotFound();
            var result = await authz.AuthorizeAsync(ctx.User, note, "MustOwnNote");
            if (!result.Succeeded) return Results.Forbid();
            db.Notes.Remove(note);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapGet("/admin/users", async (UserManager<IdentityUser> users) =>
            Results.Ok(users.Users.Select(u => new { u.Id, u.UserName, u.Email }).ToList()))
            .RequireAuthorization("MustBeAdmin");
    }
}

public sealed record CreateNoteRequest(string Title, string Body);
public sealed record NoteUpdate(string Title, string Body);
```

Commit: `Auth + Note endpoints with all five policies`.

### Phase 6 — Integration tests (~90 min)

`SharpNotes.Tests/SharpNotesApiFactory.cs`:

```csharp
public sealed class SharpNotesApiFactory : WebApplicationFactory<Program>
{
    public DateTime NowUtc { get; set; } = new(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Replace the production DbContext with an in-memory one.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseInMemoryDatabase("SharpNotesTests-" + Guid.NewGuid()));

            // Replace the real IClock with a deterministic one.
            services.AddSingleton<IClock>(_ => new FixedClock(NowUtc));

            // Swap the production auth schemes for the Test scheme.
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            services.Configure<AuthorizationOptions>(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                    .RequireAuthenticatedUser()
                    .Build();
                options.FallbackPolicy = options.DefaultPolicy;
            });
        });
    }
}
```

The `TestAuthHandler` stamps `ClaimTypes.NameIdentifier`, optional roles, optional `account_created_at`, optional `email_verified`, and optional `note_owner_id` from headers.

Then write the 30+ tests across the four test files. The cluster pattern per endpoint:

```csharp
[Fact] public async Task X_AsAllowedCaller_Returns200() { ... }
[Fact] public async Task X_AsDisallowedCaller_Returns403() { ... }
[Fact] public async Task X_AsAnonymous_Returns401() { ... }
```

Repeat for at least 5 endpoints (15 tests), then 5 sign-in tests, then 5 JWT tests, then 5 resource-based tests. Target ≥ 30 passing tests.

Commit per cluster.

### Phase 7 — Threat model + README polish (~30 min)

Write the threat model (200–300 words) in the root `README.md`:

```markdown
## Threat model

Sharp Notes Auth defends against:

1. **Anonymous access to authenticated endpoints.** The `FallbackPolicy` makes every
   endpoint authenticated unless explicitly `[AllowAnonymous]`. The integration tests
   include an "anonymous returns 401" case for every protected route.

2. **Cross-user data access.** The `MustOwnNote` resource-based policy refuses any
   request where the JWT's `NameIdentifier` claim does not match the note's `OwnerId`.
   A regression test (PolicyTests.OtherUserCannotEditMyNote_Returns403) proves it.

3. **JWT tampering.** `ValidateIssuerSigningKey = true` plus `IssuerSigningKey` make
   any modified-payload token fail validation. A regression test mints a token,
   appends "TAMPERED" to the signature, asserts 401.

It does NOT defend against:

1. **Denial of service.** Rate limiting is a Week 8 topic (the resilience week).
2. **JWT signing key compromise.** If the symmetric key leaks, every issued token
   is forged-able. Key rotation (homework problem 2) is the mitigation; the
   defense in depth is short token lifetimes (1 hour by default) plus a revocation
   list for compromised users.
3. **Cookie theft via XSS.** HttpOnly + Secure + SameSite=Lax raise the bar but do
   not eliminate the risk. Output encoding and a Content Security Policy header
   are required for the browser surface.
4. **Account enumeration via timing.** The /auth/login endpoint takes longer for
   valid usernames (because it runs the password hash) than for invalid ones. This
   leaks user-existence information. Production fix: hash a dummy password on
   the missing-user path so the timing is constant. Week 6 does not require it.
```

Add the "Five policies" section. Run `dotnet format`. Commit.

Commit: `Threat model + README polish`.

---

## Example expected output

```text
$ dotnet run --project src/SharpNotes.Api
Building...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.

$ curl -i https://localhost:5001/api/notes
HTTP/1.1 401 Unauthorized

$ curl -i -X POST https://localhost:5001/auth/register \
       -H "Content-Type: application/json" \
       -d '{"username":"ada","email":"ada@example.com","password":"correct-horse-1A"}'
HTTP/1.1 201 Created
{"id":"...","userName":"ada"}

$ curl -i -X POST https://localhost:5001/auth/token \
       -d '{"username":"ada","password":"correct-horse-1A"}'
HTTP/1.1 200 OK
{"access_token":"eyJ...","expires_in":3600}

$ curl -i -H "Authorization: Bearer eyJ..." https://localhost:5001/api/notes
HTTP/1.1 200 OK
[]
```

And the test output:

```text
$ dotnet test
Passed!  - Failed: 0, Passed: 34, Skipped: 0, Total: 34
Build succeeded · 0 warnings · 0 errors · 4.2 s
```

---

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 10% | `dotnet build`, `dotnet test`, `dotnet run --project ...Api` clean on a fresh clone |
| Cookie auth | 15% | Full sign-in/sign-out flow with proper cookie attributes; `OnRedirectToLogin` overridden for API |
| JWT auth | 15% | `Jwt:Key` from user-secrets; HS256 with a 256-bit key; `ValidateIssuerSigningKey = true`; ClockSkew shrunk; tamper test passes |
| Identity wiring | 10% | `AddIdentityCore<IdentityUser>()` + EF Core stores + password requirements set; sign-in goes through `SignInManager` |
| Five policies | 15% | All five exist, named with intent; `FallbackPolicy = RequireAuthenticatedUser()` |
| Three handlers | 15% | All three are testable, take dependencies via constructor, return silently on bad input, never throw |
| Tests | 15% | ≥ 30 passing; 200/403/401 triplet for every protected endpoint; at least one resource-based test |
| Threat model | 5% | 200–300 words; lists at least three things the layer does NOT defend against; honest about cookies vs JWTs trade-offs |

---

## Stretch (optional)

- **Add a refresh-token endpoint.** `POST /auth/refresh` accepts a refresh token (a separate JWT with a longer lifetime stored server-side), validates it, mints a new access token. Note: refresh-token rotation is non-trivial; see RFC 6749 §6 and OWASP's refresh-token cheat sheet.
- **Add MFA.** `UserManager<IdentityUser>` already supports `GenerateTwoFactorTokenAsync`. Add a `POST /auth/mfa/verify` endpoint and integrate it into `SignInManager.PasswordSignInAsync(...)` (which returns `RequiresTwoFactor` when the user has 2FA enabled).
- **Add API key authentication as a third scheme.** Custom `AuthenticationHandler<T>` that reads `X-Api-Key` and looks it up in an `ApiKeys` table. Note how the same `[Authorize(Policy = "MustBeConfirmed")]` works across all three schemes.
- **Add an audit log.** A `BackgroundService` listens to a `Channel<AuthorizationEvent>` populated by a custom `IAuthorizationMiddlewareResultHandler`. Every authorization decision is written to an `AuditLog` table.
- **Plot the policies.** Generate an ASCII table or Mermaid diagram in the README mapping each endpoint to the policies it enforces. Useful for security review.

---

## What this prepares you for

- **Week 7** introduces OpenID Connect with Keycloak. The five named policies you wrote here do not change. The `[Authorize(Policy = "MustOwnNote")]` attribute does not change. What changes is the *issuer* of the JWTs — instead of `POST /auth/token` minting them locally, Keycloak mints them via the OAuth 2.0 authorization code flow. Your `TokenValidationParameters` swaps `IssuerSigningKey` (symmetric) for a JWKS endpoint (asymmetric, RS256). The rest of your code is unchanged.
- **Week 8** introduces SignalR. The hub authorization model is identical: `[Authorize(Policy = "...")]` on hub method invocations. The handlers you wrote here run unchanged.
- **The capstone** (Week 15+) is a multi-tenant SaaS that uses every policy you wrote here, plus a tenant policy from Challenge 1, plus an OIDC issuer from Week 7. The pattern compounds.

---

## Resources

- *ASP.NET Core authentication overview*: <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/>
- *Policy-based authorization*: <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies>
- *Resource-based authorization*: <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>
- *Configure JWT bearer authentication*: <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- *Introduction to ASP.NET Core Identity*: <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity>
- *Integration tests with `WebApplicationFactory<T>`*: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes the setup commands, the policy table, the threat model, and the test count.
3. Make sure `dotnet build`, `dotnet test`, and `dotnet run --project src/SharpNotes.Api` are green on a fresh clone.
4. Post the repo URL in your cohort tracker. You shipped a production-shaped auth layer; show it.
