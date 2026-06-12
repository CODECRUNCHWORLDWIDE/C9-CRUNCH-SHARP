# Lecture 1 — Threat Modeling the API Boundary, the OWASP API Security Top 10 in .NET 9, Resource-Based and Tenant-Aware Authorization, and Rate Limiting

> **Time:** 2 hours. Take the threat-modeling method in one sitting and the authorization implementation in a second sitting. **Prerequisites:** Week 7 (ASP.NET Core auth, JWT validation, the `RequireOwner` policy), Week 9 (the gRPC service), Week 13 (the integration baseline). **Citations:** the OWASP API Security Top 10 (2023) at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>, the OWASP Threat Modeling cheat sheet at <https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html>, and the ASP.NET Core resource-based authorization chapter at <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>.

## 1. Why hardening is editing

Last week the Polyglot Workshop got to "it works." This week the job is "it is trustworthy and operable," and the surprising thing about that job is how much of it is *deletion*. You will delete an `[AllowAnonymous]` that crept in during a debugging session. You will delete a handler that returned an entity directly to the wire — including the `InternalNotes` column nobody meant to expose — and replace it with a projection to a DTO that names exactly the four fields the client is allowed to see. You will delete three near-identical endpoint bodies and replace them with one pipeline. The net diff is smaller, and the system is harder. That is not a coincidence: most security bugs are *extra* surface — a field you exposed, an endpoint you forgot to gate, a token-validation check you turned off "just for local." Hardening is the discipline of finding that extra surface and removing it.

The method that finds it is **threat modeling**, and the discipline that proves you removed it is the **integration test**. This lecture is the first half (find the surface); Lecture 1's exercise and the mini-project are the second half (prove it is gone).

## 2. Threat modeling, the lightweight version

You do not need a security consultant or a six-week engagement. You need a data-flow diagram, a list of trust boundaries, and one pass of STRIDE per boundary, written into a `THREATMODEL.md` that ships in the repo and gets reviewed in pull requests like any other file. The OWASP Threat Modeling cheat sheet (<https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html>) frames it as four questions: *What are we building? What can go wrong? What are we going to do about it? Did we do a good job?* We answer all four for the Polyglot Workshop.

### 2.1 What are we building — the data-flow diagram

The workshop has three clients, one contract, and one backend. The diagram an attacker cares about is the one that marks the **trust boundaries** — the lines an attacker-controlled request crosses to reach something they should not control:

```
   UNTRUSTED                          | TRUST BOUNDARY |        TRUSTED
                                       |
  [ MAUI client ] --OIDC token-->      |
  [ Blazor admin ] --OIDC token-->     |       +------------------------+
  [ curl / grpcurl / attacker ] ---->  |  (1)  | Minimal API (HTTP)     |
                                       |  (2)  | gRPC service (HTTP/2)  |
                                       |  (3)  | SignalR hub (WS)       |
                                       |       +-----------+------------+
                                       |                   |
                                       |          +--------+--------+
                                       |          | MediatR pipeline |
                                       |          +--------+--------+
                                       |                   |
                                       |     +-------------+-------------+
                                       |     |   EF Core   |   Dapper    |
                                       |     +------+------+------+------+
                                       |            |             |
                                       |       +----+-------------+----+
                                       |       |   PostgreSQL (tenant) |
                                       |       +-----------------------+
```

There are **exactly three boundaries** an attacker's bytes can cross: the Minimal API over HTTP (1), the gRPC service over HTTP/2 (2), and the SignalR hub over the WebSocket upgrade (3). Keycloak issues the tokens that those three boundaries validate; it is not itself a boundary your code defends — it is a dependency you configure correctly. Everything below the line — the MediatR pipeline, EF Core, Dapper, PostgreSQL, the outbox worker — is reachable *only* through one of those three doors. That is the whole point of the architecture: it gives you three places to stand guard instead of a hundred.

### 2.2 What can go wrong — STRIDE per boundary

STRIDE is a checklist of six threat categories you walk for each boundary (<https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats>):

| Letter | Threat | The question at a boundary |
|--------|--------|----------------------------|
| **S** | Spoofing | Can the caller pretend to be someone they are not? (auth) |
| **T** | Tampering | Can the caller modify data they should not? (integrity, authz on writes) |
| **R** | Repudiation | Can the caller deny having done something? (audit logging) |
| **I** | Information disclosure | Can the caller read data they should not? (authz on reads, DTO allow-lists) |
| **D** | Denial of service | Can the caller exhaust a resource? (rate limiting, pagination caps, timeouts) |
| **E** | Elevation of privilege | Can the caller gain capabilities they should not have? (function-level authz) |

Walk it once for the Minimal API boundary and the OWASP API Top 10 falls out almost line for line:

- **S — Spoofing.** Does every authenticated endpoint validate a real token? Is `ValidateIssuer`/`ValidateAudience`/`ValidateLifetime` on? → **OWASP API2, Broken Authentication.**
- **T/I — Tampering and Information disclosure on objects.** When the caller names an object by id (`/submissions/{id}`), do we check they own it before reading or writing it? → **OWASP API1, BOLA** (the big one).
- **I — Information disclosure on properties.** Do we return the entity directly (leaking `InternalNotes`, `LearnerEmail`, the grader's private comments) or a DTO allow-list? → **OWASP API3, BOPLA.**
- **E — Elevation of privilege.** Can a learner call the instructor-only `POST /lessons` or `DELETE /submissions/{id}`? → **OWASP API5, BFLA.**
- **D — Denial of service.** Can a caller request `?pageSize=1000000`, or hammer the analytics endpoint 10,000 times a second? → **OWASP API4, Unrestricted Resource Consumption.**
- **R — Repudiation.** When alice deletes a submission, is there an audit log entry with her `sub` and the `TraceId`? (This is where the observability work in Lecture 3 pays a security dividend.)

The same six-row walk applies to the gRPC and SignalR boundaries, with the carve-outs each protocol adds (gRPC returns status codes not HTTP codes; SignalR carries the token in a query string). You do all three in the exercise and write them into `THREATMODEL.md`.

## 3. The OWASP API Security Top 10 (2023), mapped to .NET mitigations

The list is the catalog of what actually goes wrong at an API boundary (<https://owasp.org/API-Security/editions/2023/en/0x11-t10/>). For a .NET engineer it is, mostly, a list of authorization bugs. Here is the whole list with the concrete .NET fix:

| OWASP | Name | .NET mitigation |
|-------|------|-----------------|
| API1 | Broken Object Level Authorization (BOLA) | Resource-based authz: check ownership before reading the object |
| API2 | Broken Authentication | Hardened `TokenValidationParameters`; deny-by-default `[Authorize]` |
| API3 | Broken Object Property Level Authorization (BOPLA) | DTO allow-lists; never bind/return entities directly |
| API4 | Unrestricted Resource Consumption | Rate-limiting middleware; pagination caps; request timeouts |
| API5 | Broken Function Level Authorization (BFLA) | Policy-gated endpoints; deny-by-default on route groups |
| API6 | Unrestricted Access to Sensitive Business Flows | Per-flow throttling; CAPTCHA/step-up for high-value flows |
| API7 | Server-Side Request Forgery (SSRF) | Allow-list outbound hosts; never fetch a user-supplied URL raw |
| API8 | Security Misconfiguration | Security headers; HTTPS redirection; disable detailed errors in prod |
| API9 | Improper Inventory Management | One source-of-truth `.proto` + OpenAPI; no undocumented endpoints |
| API10 | Unsafe Consumption of APIs | Validate and time-out responses from third-party APIs you call |

The four that dominate a workshop platform are **API1 (BOLA)**, **API3 (BOPLA)**, **API5 (BFLA)**, and **API4 (resource consumption)**. The next three sections are the .NET implementations.

## 4. Resource-based authorization — the antidote to BOLA

BOLA is the bug where the handler trusts the id in the URL. The naive submission handler:

```csharp
// BROKEN: any authenticated learner can read ANY submission by guessing/iterating ids.
app.MapGet("/api/submissions/{id:guid}", async (Guid id, WorkshopDbContext db) =>
{
    var submission = await db.Submissions.FindAsync(id);
    return submission is null ? Results.NotFound() : Results.Ok(submission);
})
.RequireAuthorization();   // authenticated, but NOT authorized for THIS object
```

`RequireAuthorization()` proves the caller is *someone*; it says nothing about whether they own *this* submission. The fix is **resource-based authorization**: an `AuthorizationHandler<TRequirement, TResource>` that runs after you have loaded the resource, with the resource in hand, and the `ClaimsPrincipal` to check it against (<https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>).

### 4.1 The requirement and the handler

```csharp
#nullable enable
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Workshop.Api.Authorization;

// A marker requirement: "the caller must own this submission."
public sealed class SubmissionOwnerRequirement : IAuthorizationRequirement;

public sealed class SubmissionOwnerHandler
    : AuthorizationHandler<SubmissionOwnerRequirement, Submission>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SubmissionOwnerRequirement requirement,
        Submission resource)
    {
        // The 'sub' claim is the stable user identifier from Keycloak.
        string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Instructors in the same tenant may also read submissions in their lessons.
        bool isInstructor = context.User.IsInRole("instructor");

        if (userId is not null &&
            (resource.LearnerId == userId ||
             (isInstructor && resource.TenantId == context.User.FindFirstValue("tenant"))))
        {
            context.Succeed(requirement);
        }

        // Note: we do NOT call context.Fail(). Leaving the requirement unmet is a
        // soft failure that lets OTHER handlers for the same requirement succeed.
        // We only Fail() when we want to veto regardless of other handlers.
        return Task.CompletedTask;
    }
}
```

Register the handler and a policy:

```csharp
builder.Services.AddScoped<IAuthorizationHandler, SubmissionOwnerHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SubmissionOwner", p => p.AddRequirements(new SubmissionOwnerRequirement()));
```

### 4.2 The endpoint: check before you return

```csharp
app.MapGet("/api/submissions/{id:guid}",
    async (Guid id, WorkshopDbContext db, IAuthorizationService authz, ClaimsPrincipal user) =>
{
    var submission = await db.Submissions
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.Id == id);

    if (submission is null)
    {
        return Results.NotFound();
    }

    var result = await authz.AuthorizeAsync(user, submission, "SubmissionOwner");
    if (!result.Succeeded)
    {
        // Return 404, not 403. A 403 confirms the object exists, which is itself
        // an information disclosure (the caller learns a valid id belongs to someone).
        // For objects the caller must not even know exist, 404 is the correct answer.
        return Results.NotFound();
    }

    return Results.Ok(submission.ToDto());   // DTO, not the entity — see section 5.
})
.RequireAuthorization();
```

Three points carry the weight:

1. **The check happens after the load and before the return.** You cannot resource-authorize an object you have not loaded. The cost is one extra query you would have run anyway.
2. **403 vs 404 is a deliberate choice.** For a resource whose *existence* is not a secret (a public lesson the caller lacks edit rights on), 403 is honest. For a resource whose existence is a secret (another learner's private submission), 404 hides the id space. The OWASP BOLA guidance leans toward 404 for object-level denials. Citation: <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>.
3. **This is the pattern you repeat for every endpoint that names an object by id.** Lecture 1's exercise is to find all of them in the workshop and add the check + the deny-path test.

### 4.3 Tenant-aware authorization, in depth-defense

The workshop is multi-tenant: org A's instructors must never see org B's lessons. Resource-based checks catch this at the boundary, but you add a second layer — an **EF Core global query filter** — so a *missing* check still cannot leak across tenants:

```csharp
public sealed class WorkshopDbContext(
    DbContextOptions<WorkshopDbContext> options,
    ITenantContext tenant) : DbContext(options)
{
    private readonly string _tenantId = tenant.TenantId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Every tenant-scoped entity is filtered by the current request's tenant.
        // A query that FORGETS to filter by tenant still returns only this tenant's rows.
        b.Entity<Lesson>().HasQueryFilter(l => l.TenantId == _tenantId);
        b.Entity<Submission>().HasQueryFilter(s => s.TenantId == _tenantId);
        b.Entity<Enrollment>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}
```

`ITenantContext` reads the `tenant` claim off `HttpContext.User` and is registered `Scoped`. The global filter is **defense in depth**, not the primary control — the primary control is the resource-based check. But when a junior engineer adds an endpoint and forgets the check, the global filter means the blast radius is "leaks within a tenant," not "leaks across tenants." Citation: <https://learn.microsoft.com/en-us/ef/core/querying/filters>.

## 5. BOPLA — return a DTO allow-list, never the entity

BOPLA (OWASP API3) is the bug where you return the entity directly and leak properties the client should never see (<https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>). The `Submission` entity has `InternalNotes` (the grader's private comments) and `LearnerEmail` (PII). The DTO has neither:

```csharp
public sealed record SubmissionDto(
    Guid Id,
    Guid LessonId,
    int? Grade,
    DateTimeOffset SubmittedAtUtc);

// Hand-written here because it is the public contract — a reviewer must see
// exactly which fields cross the boundary. (Lecture 2 covers when AutoMapper
// is appropriate for this and when it is not.)
public static class SubmissionMappings
{
    public static SubmissionDto ToDto(this Submission s) =>
        new(s.Id, s.LessonId, s.Grade, s.SubmittedAtUtc);
}
```

The same rule governs **input**: never bind the request body straight onto an entity, or a caller can set `Grade` or `TenantId` by including them in the JSON (the "mass assignment" half of BOPLA). Bind to a request DTO that contains *only* the fields the caller may set, and map deliberately. The `[Bind]`-the-entity habit from older MVC tutorials is exactly the anti-pattern OWASP is warning about.

## 6. BFLA — gate the function by role and policy

BFLA (OWASP API5) is the bug where a learner can call an instructor-only function (<https://owasp.org/API-Security/editions/2023/en/0xa5-broken-function-level-authorization/>). The fix is a policy on the endpoint, and **deny-by-default on the route group** so a new endpoint is gated unless you opt it out:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("InstructorOnly", p => p.RequireRole("instructor"))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()   // deny-by-default
        .RequireAuthenticatedUser()
        .Build());

var instructorGroup = app.MapGroup("/api/lessons").RequireAuthorization("InstructorOnly");

instructorGroup.MapPost("/", CreateLesson);              // instructor only, by group
instructorGroup.MapDelete("/{id:guid}", DeleteLesson);   // instructor only, by group
```

The `SetFallbackPolicy` is the load-bearing line: it means an endpoint with *no* explicit authorization still requires an authenticated user. The OWASP guidance is explicit that BFLA is usually a *default* problem — a developer remembers to gate the endpoints they think are sensitive and forgets the one they added last week. Deny-by-default removes the "remember to gate it" step.

## 7. API4 — rate limiting to bound resource consumption

Unrestricted Resource Consumption (OWASP API4) is closed with the built-in rate-limiting middleware (`Microsoft.AspNetCore.RateLimiting`, in-box in ASP.NET Core 9; <https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit>). Partition by authenticated user so one noisy tenant cannot starve the others:

```csharp
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "1";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "rate_limited", retryAfterSeconds = 1 }, ct);
    };

    // A per-user token bucket: 100 tokens, refilled 20/sec.
    options.AddPolicy("per-user", httpContext =>
    {
        string key = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? httpContext.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,
            TokensPerPeriod = 20,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

app.UseRateLimiter();
app.MapGroup("/api/analytics").RequireRateLimiting("per-user");   // the expensive surface
```

The pagination cap is the other half of API4 — every list endpoint clamps `pageSize`:

```csharp
const int MaxPageSize = 100;
int effectivePageSize = Math.Clamp(pageSize, 1, MaxPageSize);
```

A request for `?pageSize=1000000` returns 100 rows, not a multi-gigabyte response that OOM-kills the pod.

## 8. The gRPC boundary — same threats, different status codes

The gRPC service mirrors the domain, so it has the same BOLA/BFLA/BOPLA surface — but it speaks gRPC status codes, not HTTP codes (<https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>):

```csharp
[Authorize]   // deny-by-default on the whole service
public sealed class SubmissionGrpcService(
    WorkshopDbContext db, IAuthorizationService authz)
    : SubmissionService.SubmissionServiceBase
{
    public override async Task<SubmissionReply> GetSubmission(
        GetSubmissionRequest request, ServerCallContext context)
    {
        var user = context.GetHttpContext().User;
        var submission = await db.Submissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(request.Id),
                context.CancellationToken);

        if (submission is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "not found"));
        }

        var result = await authz.AuthorizeAsync(user, submission, "SubmissionOwner");
        if (!result.Succeeded)
        {
            // PermissionDenied for "exists but not yours" where existence is not secret;
            // NotFound where it is. Same 403-vs-404 reasoning as the HTTP boundary.
            throw new RpcException(new Status(StatusCode.NotFound, "not found"));
        }

        return submission.ToReply();
    }
}
```

The `[Authorize]` on the service class is the gRPC equivalent of the fallback policy: anonymous calls get `Unauthenticated` (mapped to HTTP 401 on the wire) before the method body runs. The resource-based check inside the method is identical to the HTTP one — the *same* `IAuthorizationService` and the *same* `SubmissionOwnerHandler`, because authorization is a domain concern, not a transport concern. That re-use is the reward for putting the check in a handler rather than inlining it.

## 9. The SignalR boundary — review the access_token carve-out

SignalR carries the token in the `access_token` query string (Week 11). Threat-modeling that boundary surfaces one specific risk: **query strings are logged** — by proxies, by the web server's access log, by Grafana if you are careless about what you put in span attributes. The mitigations are the ones from Week 11's homework, now made concrete: (a) the `OnMessageReceived` hook accepts the query-string token *only* for `/hubs/*` paths, so no other endpoint reads tokens from URLs; (b) tokens are short-lived (Keycloak access tokens default to 5 minutes), so a leaked URL is a small window; (c) the connection is TLS, so the query string is not on the wire in clear text; and (d) — new this week — you scrub `access_token` from any span attribute or log property, because OpenTelemetry's ASP.NET Core instrumentation records the request path. Lecture 3's collector config includes a processor that redacts it.

## 10. What you can do now, and what comes next

You can now walk the three boundaries with STRIDE, name the OWASP item each threat maps to, and implement the four dominant mitigations — resource-based authz (BOLA), DTO allow-lists (BOPLA), policy-gated functions with deny-by-default (BFLA), and rate limiting (API4). The exercise has you do exactly this to the workshop and write `THREATMODEL.md`.

What is missing is *proof*. A resource-based check you believe is wired is not the same as one a test proves is wired — the policy name might be misspelled, the handler might not be registered, the gRPC method might have lost its `[Authorize]` in a merge. Tuesday's material (the second half of Monday's block in the schedule) builds the **auth integration-test harness** with a real Testcontainers Keycloak, and Exercise 1 makes you write the deny-path test that proves alice cannot read bob's submission. Lecture 2 then takes the *implementation* of these handlers and shows where MediatR's pipeline lets you run the authorization check **once, for every request**, instead of inlining `AuthorizeAsync` into every endpoint — which is the deliberate-MediatR case, and which, true to the theme, removes more lines than it adds.

Citations for this lecture: the OWASP API Security Top 10 (2023) at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/> and its per-item pages for API1/API3/API4/API5; the OWASP Threat Modeling cheat sheet at <https://cheatsheetseries.owasp.org/cheatsheets/Threat_Modeling_Cheat_Sheet.html>; ASP.NET Core resource-based authorization at <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>; policy-based authorization at <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies>; EF Core global query filters at <https://learn.microsoft.com/en-us/ef/core/querying/filters>; the rate-limiting middleware at <https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit>; and gRPC auth at <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>.
