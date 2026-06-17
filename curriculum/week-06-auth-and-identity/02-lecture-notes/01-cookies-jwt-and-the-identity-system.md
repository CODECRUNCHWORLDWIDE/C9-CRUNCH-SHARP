# Lecture 1 — Cookies, JWT, and the Identity System

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can wire cookie authentication and JWT bearer authentication into the same ASP.NET Core 9 application, distinguish them by scheme name, read a `ClaimsPrincipal` off `HttpContext.User`, configure ASP.NET Core Identity with EF Core for a real user store, and explain — to a code reviewer — why the order of `app.UseAuthentication()` and `app.UseAuthorization()` in the middleware pipeline is not negotiable.

If you only remember one thing from this lecture, remember this:

> **Authentication answers "who is this caller?" Authorization answers "what may they do?"** These are different middleware, configured by different `Add*` builders, executed in a fixed order, and broken by different bugs. `UseAuthentication()` reads a credential and populates `HttpContext.User`. `UseAuthorization()` reads `HttpContext.User` and decides `200`, `401`, or `403`. Authentication never refuses a request. Authorization never reads a cookie or a JWT. Wire them in the right order and the rest of ASP.NET Core 9's security stack is configuration on top.

---

## 1. The middleware pipeline, in order

An ASP.NET Core 9 application is a sequence of middleware. The minimal authenticated app looks like:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options => { /* ... */ });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();   // (1) reads credential, populates HttpContext.User
app.UseAuthorization();    // (2) reads HttpContext.User, decides 200/401/403

app.MapGet("/me", (HttpContext ctx) => ctx.User.Identity?.Name ?? "anonymous")
   .RequireAuthorization();

app.Run();
```

Two `Add*` calls. Two `Use*` calls. One `RequireAuthorization()` on the route. That is the whole shape.

The order of the `Use*` calls is **not negotiable**. If you call `app.UseAuthorization()` before `app.UseAuthentication()`, the authorization middleware reads an empty `HttpContext.User` and every endpoint behaves as if the caller is anonymous. Both must run *before* the routing middleware's endpoint executor — `app.UseRouting()` is implicit in minimal APIs, and it appears in the pipeline before `UseAuthentication` and `UseAuthorization`. ASP.NET Core 9's minimal-API builder takes care of the routing slot; you just call the two `Use*` methods and the framework places them correctly.

**The one-sentence summary.** Authentication runs, then authorization runs, then the endpoint runs. Get that order wrong and every test you write will pass for the wrong reason.

---

## 2. What `UseAuthentication()` actually does

For each incoming request, `app.UseAuthentication()` runs roughly this loop:

1. Look up the *default authentication scheme* registered in `AddAuthentication("Cookies")` (or the explicit one in `[Authorize(AuthenticationSchemes = "Bearer")]`).
2. Resolve the `IAuthenticationHandler` for that scheme from the service container.
3. Call `handler.AuthenticateAsync()`. The handler reads the credential off the request — a cookie value, an `Authorization: Bearer ...` header, an API key from the query string, whatever the handler knows about — and returns an `AuthenticateResult`.
4. If the result is `Success(ticket)`, attach `ticket.Principal` to `HttpContext.User`.
5. If the result is `NoResult()` or `Fail(...)`, leave `HttpContext.User` as a default `ClaimsPrincipal` with an unauthenticated `ClaimsIdentity` (`Identity.IsAuthenticated == false`).
6. Continue the pipeline. *Authentication does not refuse the request.*

Step 6 is the one people miss. If the cookie is missing, expired, or invalid, `UseAuthentication()` does not return `401`. It populates `HttpContext.User` with an *anonymous* principal and lets the request continue. The endpoint metadata (read by `UseAuthorization()`) decides whether anonymous is acceptable.

This is also why one of the most-common bugs in ASP.NET Core auth — "my endpoint returns `200 OK` for unauthenticated requests when I expected `401`" — has a single fix: add `[Authorize]` (or `.RequireAuthorization()`) to the endpoint. `UseAuthentication()` does not refuse anyone; it only loads identities. The refusal is `UseAuthorization()`'s job.

---

## 3. Cookie authentication, from scratch

The cookie auth handler is in `Microsoft.AspNetCore.Authentication.Cookies`. Wire it up:

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name              = "C9.Auth";
        options.Cookie.HttpOnly          = true;
        options.Cookie.SecurePolicy      = CookieSecurePolicy.Always;
        options.Cookie.SameSite          = SameSiteMode.Lax;
        options.ExpireTimeSpan           = TimeSpan.FromHours(8);
        options.SlidingExpiration        = true;
        options.LoginPath                = "/auth/login";
        options.LogoutPath               = "/auth/logout";
        options.AccessDeniedPath         = "/auth/denied";
    });
```

Each option matters:

- `Cookie.Name = "C9.Auth"` — the cookie's HTTP name. Default is `.AspNetCore.Cookies`. Renaming hides the framework from probes.
- `Cookie.HttpOnly = true` — JavaScript cannot read the cookie via `document.cookie`. Prevents XSS-based cookie theft. Default is `true`; do not turn it off.
- `Cookie.SecurePolicy = CookieSecurePolicy.Always` — the cookie is only sent over HTTPS. Default is `SameAsRequest` (HTTPS in production, HTTP in dev), which is wrong for auth cookies — promote it to `Always`.
- `Cookie.SameSite = SameSiteMode.Lax` — the cookie is not sent on cross-site `POST`s. Prevents the simplest form of CSRF. Default is `Lax`; do not relax it without a documented reason.
- `ExpireTimeSpan = TimeSpan.FromHours(8)` — how long the cookie is valid. Default is 14 days; 8 hours matches a workday.
- `SlidingExpiration = true` — every authenticated request resets the expiration clock. The cookie expires after 8 hours of *inactivity*.
- `LoginPath = "/auth/login"` — where the framework redirects the user when an authenticated endpoint is hit without a cookie. For an API-only app you usually override this behavior; see §4.
- `AccessDeniedPath = "/auth/denied"` — where the framework redirects when authorization fails (`403`). Same caveat.

### Signing a user in

A sign-in endpoint produces the cookie:

```csharp
app.MapPost("/auth/login", async (LoginRequest req, HttpContext ctx) =>
{
    // ... validate credentials against your user store ...
    if (!IsValid(req)) return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, req.Username),
        new(ClaimTypes.Name,           req.Username),
        new(ClaimTypes.Email,          $"{req.Username}@example.com"),
        new(ClaimTypes.Role,           "user"),
    };

    var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await ctx.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal);

    return Results.Ok(new { user = req.Username });
});

public sealed record LoginRequest(string Username, string Password);
```

The four lines that matter:

1. Build a `List<Claim>` describing the user.
2. Wrap it in a `ClaimsIdentity` *with the scheme name* — passing the scheme is what makes `Identity.IsAuthenticated` true. An identity with a `null` scheme is anonymous by convention.
3. Wrap the identity in a `ClaimsPrincipal`.
4. Call `HttpContext.SignInAsync(scheme, principal)`. The cookie handler serializes the principal, encrypts it with the data protection key ring, and emits a `Set-Cookie` header.

The cookie payload is *the entire principal*, encrypted at rest with `IDataProtector`. Pre-.NET-9, this surprises people: a cookie is not a session id pointing to a server-side session table. It is the session, period, signed and encrypted. The server keeps no state. (This is why rotating the data protection keys logs everyone out — every existing cookie becomes undecryptable.)

### Signing a user out

```csharp
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});
```

`SignOutAsync` emits a `Set-Cookie` header with `Expires=...` in the past, which the browser interprets as "delete this cookie." No server-side state changes (because there was none).

### Reading the principal

Inside any authenticated endpoint:

```csharp
app.MapGet("/me", (HttpContext ctx) =>
{
    var user  = ctx.User;
    var name  = user.Identity?.Name ?? "anonymous";
    var email = user.FindFirst(ClaimTypes.Email)?.Value;
    var role  = user.FindFirst(ClaimTypes.Role)?.Value;
    return Results.Ok(new { name, email, role });
})
.RequireAuthorization();
```

`HttpContext.User` is always a `ClaimsPrincipal`. If the user is anonymous, `Identity.IsAuthenticated` is `false` and `FindFirst(...)` returns `null` for every claim. If the user is authenticated, every claim added at sign-in is readable here.

---

## 4. APIs vs browser apps: turning off the redirects

The defaults — `LoginPath`, `AccessDeniedPath` — assume your app serves HTML pages. For a JSON API, redirecting an unauthenticated request to `/auth/login` is wrong; the browser-side JavaScript client expects a `401 Unauthorized` JSON response. The fix:

```csharp
.AddCookie(options =>
{
    // ... all the same options ...

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
});
```

`CookieAuthenticationEvents` is the canonical place to override behavior. The two events above turn the redirect responses into the bare status codes an API client expects.

---

## 5. JWT bearer authentication, from scratch

The JWT bearer handler is in `Microsoft.AspNetCore.Authentication.JwtBearer`. Wire it up:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = "https://c9.example.com",
            ValidateAudience         = true,
            ValidAudience            = "c9-ledger-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
        };
    });
```

Every field of `TokenValidationParameters` matters:

- `ValidateIssuer = true` — reject tokens whose `iss` claim does not match `ValidIssuer`. Default is `true`; do not turn it off.
- `ValidIssuer = "https://c9.example.com"` — the expected `iss` value. Match the `Issuer` you set when minting the token.
- `ValidateAudience = true` — reject tokens whose `aud` claim does not match `ValidAudience`. Same caveat.
- `ValidAudience = "c9-ledger-api"` — the expected `aud` value. Each API instance has its own audience.
- `ValidateIssuerSigningKey = true` — verify the signature against `IssuerSigningKey`. **This is the security boundary.** A token with a tampered payload but a missing signature will fail here.
- `IssuerSigningKey = new SymmetricSecurityKey(...)` — for HS256 (HMAC-SHA256), a symmetric secret known to both the issuer and the validator. Week 7 swaps this for `RsaSecurityKey` and RS256.
- `ValidateLifetime = true` — reject tokens whose `exp` is in the past. Default is `true`.
- `ClockSkew = TimeSpan.FromSeconds(30)` — tolerance for clock drift. Default is **5 minutes**, which is far too generous for most modern infrastructure. Production .NET 9 services run on NTP-synced clocks; 30 seconds is plenty.

Pull the signing key out of source control. Use `dotnet user-secrets` in dev, environment variables in production, a key vault past that:

```bash
cd src/SharpLedger.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 32)"
```

The output of `openssl rand -base64 32` is a 256-bit symmetric key — the right strength for HS256.

### Issuing a JWT

A sign-in endpoint that produces a JWT instead of a cookie:

```csharp
app.MapPost("/auth/token", (LoginRequest req, IConfiguration config) =>
{
    if (!IsValid(req)) return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub,   req.Username),
        new(JwtRegisteredClaimNames.Email, $"{req.Username}@example.com"),
        new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        new(ClaimTypes.Role,               "user"),
    };

    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer:             "https://c9.example.com",
        audience:           "c9-ledger-api",
        claims:             claims,
        notBefore:          DateTime.UtcNow,
        expires:            DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { access_token = jwt, expires_in = 3600 });
});
```

A few things to notice:

- `JwtRegisteredClaimNames.Sub` is the canonical subject claim. Maps to `ClaimTypes.NameIdentifier` on the receiving side.
- `JwtRegisteredClaimNames.Jti` is a unique token id. Useful for revocation lists later.
- `notBefore` and `expires` populate the `nbf` and `exp` claims.
- The `JwtSecurityTokenHandler.WriteToken(...)` call is what produces the dot-separated `header.payload.signature` string.

### Sending a JWT

The client sends the token in the `Authorization` header:

```bash
curl -H "Authorization: Bearer eyJhbGciOiJI..." https://localhost:5001/me
```

The JWT bearer handler reads the header, validates the token against `TokenValidationParameters`, and (on success) populates `HttpContext.User` with the principal derived from the token's claims. Everything downstream — `[Authorize]`, policies, your endpoint code — is identical to the cookie path.

---

## 6. `ClaimsPrincipal` end-to-end

`ClaimsPrincipal` is the abstraction that makes the cookie and JWT paths interchangeable downstream. A principal holds one or more `ClaimsIdentity` objects; an identity holds an `AuthenticationType` (the scheme name) and a list of `Claim` objects.

```csharp
public class ClaimsPrincipal
{
    public IIdentity? Identity { get; }                                // the primary identity
    public IEnumerable<ClaimsIdentity> Identities { get; }              // all identities
    public IEnumerable<Claim> Claims { get; }                           // all claims across all identities
    public Claim? FindFirst(string type);
    public IEnumerable<Claim> FindAll(string type);
    public bool HasClaim(string type, string value);
    public bool IsInRole(string role);                                  // shortcut for HasClaim(ClaimTypes.Role, role)
}
```

The canonical claim types (`System.Security.Claims.ClaimTypes`) are:

| Type | Purpose | Example value |
|------|---------|---------------|
| `ClaimTypes.NameIdentifier` | The user's stable id. | `"42"` |
| `ClaimTypes.Name` | The user's display name. | `"ada"` |
| `ClaimTypes.Email` | The user's email. | `"ada@example.com"` |
| `ClaimTypes.Role` | A role string. May appear multiple times. | `"admin"` |
| `ClaimTypes.GivenName` | The user's first name. | `"Ada"` |
| `ClaimTypes.Surname` | The user's last name. | `"Lovelace"` |

JWT-issued tokens use the JWT registered claim names (`JwtRegisteredClaimNames`):

| JWT name | Maps to (on receive) | Purpose |
|----------|---------------------|---------|
| `sub` | `ClaimTypes.NameIdentifier` | Subject (stable id) |
| `iat` | `"iat"` (no `ClaimTypes` mapping) | Issued-at timestamp |
| `exp` | `"exp"` (no `ClaimTypes` mapping) | Expiration timestamp |
| `nbf` | `"nbf"` (no `ClaimTypes` mapping) | Not-before timestamp |
| `iss` | `"iss"` | Issuer |
| `aud` | `"aud"` | Audience |
| `jti` | `"jti"` | Unique token id |
| `email` | `ClaimTypes.Email` | Email |

The JWT bearer handler does this mapping automatically by default. You can disable it (`JwtSecurityTokenHandler.DefaultMapInboundClaims = false`) if you prefer the raw JWT names on the receive side. Most production codebases keep the mapping on so the rest of the code can use `ClaimTypes.*` consistently regardless of the issuing scheme.

### Reading a claim

```csharp
public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? GetEmail(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Email)?.Value;

    public static bool IsInRole(this ClaimsPrincipal user, string role) =>
        user.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role);
}
```

Extensions like these belong in a shared `Auth` project and turn the entire downstream codebase into `user.GetUserId()` instead of `user.FindFirst(ClaimTypes.NameIdentifier)?.Value` repeated forty times.

---

## 7. Mixing schemes in one app

A production ASP.NET Core 9 service usually accepts both cookies (from a browser) and JWTs (from a mobile app, a CLI, a service-to-service caller). Wire both:

```csharp
builder.Services
    .AddAuthentication(options =>
    {
        // The default scheme picks one for the [Authorize] attribute without arguments.
        options.DefaultScheme        = CookieAuthenticationDefaults.AuthenticationScheme;
        // The default challenge scheme is what happens on [Authorize] failure for the default.
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options => { /* ... cookie options ... */ })
    .AddJwtBearer(options => { /* ... JWT options ... */ });
```

Each scheme has a name. The default name for cookies is `"Cookies"`; for JWT bearer it is `"Bearer"`. Reference them explicitly when an endpoint needs a specific scheme:

```csharp
app.MapGet("/api/notes", () => ...)
    .RequireAuthorization(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());

app.MapGet("/admin", () => ...)
    .RequireAuthorization(new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());
```

Or with attributes on a controller:

```csharp
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class NotesController : ControllerBase { /* ... */ }

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public class AdminController : ControllerBase { /* ... */ }
```

The most common production pattern: one set of policies (defined in `AddAuthorization(...)`), referenced by both schemes. The browser hits `/admin` with a cookie; the mobile app hits `/api/notes` with a JWT; both pass through the same authorization decision.

---

## 8. ASP.NET Core Identity, briefly

The auth stack so far stores zero state. The cookie carries the entire principal; the JWT carries the entire principal. Neither knows whether the user *still exists*, whether their password was rotated, whether their account was deactivated.

ASP.NET Core Identity is the user-store + sign-in-manager + password-hasher + token-providers framework that sits *on top* of cookie auth and gives you a real, queryable user table.

### Wiring Identity with EF Core

```csharp
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite("Data Source=ledger.db"));

builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength       = 12;
        options.Password.RequireDigit         = true;
        options.Password.RequireLowercase     = true;
        options.Password.RequireUppercase     = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail       = true;
        options.SignIn.RequireConfirmedEmail  = false; // toggle on when you ship email
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();
```

`AddIdentityCore<TUser>()` registers the *minimal* set of Identity services. `AddIdentity<TUser, TRole>()` is the all-batteries-included form, but it also registers a full set of cookie authentication options under the `IdentityConstants.ApplicationScheme` name — which conflicts with the cookie auth you may already have configured. For Week 6 we use `AddIdentityCore<>()` and configure our cookie scheme by hand.

`AppDbContext` inherits from `IdentityDbContext<IdentityUser>`:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Note> Notes => Set<Note>();
}

public sealed record Note(int Id, string OwnerId, string Title, string Body);
```

The `IdentityDbContext<TUser>` base configures the `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, and `AspNetUserTokens` tables. Add a migration with `dotnet ef migrations add InitialCreate` and the schema is created.

### Creating a user

```csharp
app.MapPost("/auth/register", async (
    RegisterRequest req,
    UserManager<IdentityUser> users) =>
{
    var user = new IdentityUser
    {
        UserName = req.Username,
        Email    = req.Email,
    };

    var result = await users.CreateAsync(user, req.Password);
    if (!result.Succeeded)
        return Results.ValidationProblem(
            result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));

    return Results.Created($"/users/{user.Id}", new { user.Id, user.UserName });
});

public sealed record RegisterRequest(string Username, string Email, string Password);
```

`UserManager<IdentityUser>.CreateAsync(user, password)` hashes the password with the configured `IPasswordHasher<IdentityUser>` (PBKDF2 + HMAC-SHA512 by default) and writes the row to `AspNetUsers`. The `IdentityResult` carries any validation failures — password too short, username already taken, email malformed.

### Signing a user in via Identity

```csharp
app.MapPost("/auth/login", async (
    LoginRequest req,
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn) =>
{
    var user = await users.FindByNameAsync(req.Username);
    if (user is null) return Results.Unauthorized();

    var result = await signIn.PasswordSignInAsync(user, req.Password,
        isPersistent: req.Remember, lockoutOnFailure: true);

    return result.Succeeded
        ? Results.Ok(new { user = user.UserName })
        : Results.Unauthorized();
});
```

`SignInManager<TUser>.PasswordSignInAsync(...)` does five things:

1. Verifies the password against the stored hash.
2. Increments the lockout counter on failure (if `lockoutOnFailure: true`).
3. Builds a `ClaimsPrincipal` from the user's row plus their roles plus their claims rows.
4. Calls `HttpContext.SignInAsync(...)` with the chosen cookie scheme.
5. Returns a `SignInResult` describing what happened (`Succeeded`, `IsLockedOut`, `RequiresTwoFactor`, `IsNotAllowed`).

The cookie is laid by step 4. Everything downstream is identical to the hand-rolled cookie path.

### Signing in and issuing a JWT instead

If your API serves bearer tokens, replace the `SignInAsync` step with a JWT issuance:

```csharp
app.MapPost("/auth/token", async (
    LoginRequest req,
    UserManager<IdentityUser> users,
    IConfiguration config) =>
{
    var user = await users.FindByNameAsync(req.Username);
    if (user is null) return Results.Unauthorized();
    if (!await users.CheckPasswordAsync(user, req.Password)) return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id),
        new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    };
    var roles = await users.GetRolesAsync(user);
    claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer:             "https://c9.example.com",
        audience:           "c9-ledger-api",
        claims:             claims,
        expires:            DateTime.UtcNow.AddHours(1),
        signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { access_token = jwt });
});
```

The user store is Identity; the credential is a JWT. Same Identity tables, different credential format on the wire.

---

## 9. The five things that go wrong

After teaching this material to several cohorts, the same five bugs come up:

1. **`UseAuthentication()` is missing or in the wrong place.** Symptom: every endpoint is anonymous. Fix: confirm the call exists and runs before `UseAuthorization()`.
2. **`[Authorize]` without a scheme on a mixed-scheme app.** Symptom: the wrong handler runs (or no handler at all). Fix: either set a `DefaultScheme` in `AddAuthentication(...)` that matches the endpoint's intent, or specify the scheme explicitly: `[Authorize(AuthenticationSchemes = "Bearer")]`.
3. **`AddJwtBearer` without `ValidateIssuerSigningKey = true`.** Symptom: any forged token with a matching `iss`/`aud` is accepted. Fix: never disable signature validation. If you must accept multiple keys during rotation, supply an `IssuerSigningKeyResolver`.
4. **`ClockSkew` left at the 5-minute default in a low-latency system.** Symptom: tokens that should have expired are still accepted for up to 5 minutes after `exp`. Fix: set `ClockSkew = TimeSpan.FromSeconds(30)` or even `Zero` for very-short-lived tokens.
5. **Cookie auth's `LoginPath` redirect on an API endpoint.** Symptom: a JavaScript fetch to a `[Authorize]` endpoint without a cookie returns a `302 Found` to `/auth/login` instead of `401 Unauthorized`. Fix: override `OnRedirectToLogin` as shown in §4.

Every one of these has a one-line fix. None of them is obvious from the framework's default behavior. The Week 6 reflex is to wire all five fixes the first time you set up auth, then never think about them again.

---

## 10. What to read next

- The **`CookieAuthenticationHandler` source** — ~600 lines, all readable: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs>
- The **`JwtBearerHandler` source** — slightly larger, the same pattern: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/JwtBearer/src/JwtBearerHandler.cs>
- **Andrew Lock — "Adding authentication to ASP.NET Core"** — the canonical walk-through: <https://andrewlock.net/category/series/authorization/>
- **RFC 7519** for JWT — read sections 4 (claims) and 7 (creating and validating). The whole RFC is ~30 pages.

Next: **Lecture 2 — Policy-Based Authorization and Claims.** Once authentication is wired, the question becomes: given a `ClaimsPrincipal`, who may do what? Policies are the answer.

---

*Build succeeded · 0 warnings · 0 errors · Lecture 1 complete.*
