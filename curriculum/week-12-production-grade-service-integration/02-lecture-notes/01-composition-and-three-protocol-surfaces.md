# Lecture 1 — Composition and the Three Protocol Surfaces: REST, gRPC, SignalR in One Host

## Where we are, restated

The previous eleven weeks of C9 each isolated a single technology and let us look at it without distraction. Week 2 gave us the minimal-API host with one endpoint that returned `Hello, world` and a swagger document. Week 3 wired EF Core into that host. Week 4 made every long-lived call `async` and showed us why. Week 6 added JWT bearer authentication to the REST surface. Week 9 stood up a gRPC service in its own host and we cross-called it from a console client. Week 10 came back to EF Core under load and we read the SQL the framework emitted to the console. Week 11 added a SignalR hub and broadcast messages to browser clients over WebSockets. Each week ended with a project that proved one thing. Each week, the project was self-contained.

This week the discipline is different. We take all of those technologies and put them in **one process**. One `Program.cs`, one `dotnet run`, one Dockerfile, one URL. A REST request lands at `https://localhost:5001/api/projects`, a gRPC request lands at the same port but on the HTTP/2 multiplex with a `content-type: application/grpc` body, a SignalR negotiate lands at `https://localhost:5001/hubs/events`, and all three flow through the same authentication middleware, the same EF Core `DbContext` factory, the same Serilog pipeline, and the same OpenTelemetry trace. The technology stack is **ASP.NET Core 8** plus its companions: `Microsoft.AspNetCore.SignalR` (in-box), `Grpc.AspNetCore` (the gRPC implementation), `Microsoft.AspNetCore.Authentication.JwtBearer` (the bearer-token middleware), `Microsoft.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL` (the persistence layer), `Serilog.AspNetCore` (the logger), and `OpenTelemetry.Extensions.Hosting` (the tracer).

The question carrying us through this lecture is: **what stays in one place and what gets repeated?** Cross-cutting concerns — auth, logging, tracing, the database context, the configuration source — must live in exactly one block of code and be reused by every protocol surface. Protocol-specific concerns — the REST route table, the gRPC service implementations, the SignalR hub class — must each get their own focused module. The shape of `Program.cs` falls out of that distinction: it is short, it composes service-collection extensions, and it routes endpoints into the pipeline in a fixed order. Everything else is in supporting classes.

## The shape of `Program.cs`

Here is the target. The file is roughly seventy lines, and every line is intentional.

```csharp
using ProjectHub.Configuration;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Logging — Serilog as the host-level provider. Configured before
// any other service so that registration errors are logged.
builder.AddProjectHubLogging();

// 2. Configuration is loaded automatically by CreateBuilder. We add
// no extra sources here in dev; production overrides via environment
// variables and (optionally) a sidecar JSON file mounted by Kubernetes.

// 3. Service registration — one entry point per cross-cutting concern.
builder.Services.AddProjectHubPersistence(builder.Configuration);
builder.Services.AddProjectHubAuth(builder.Configuration);
builder.Services.AddProjectHubTelemetry(builder.Configuration);
builder.Services.AddProjectHubProblemDetails();

// 4. Protocol surfaces — three of them, each registered with one call.
builder.Services.AddProjectHubRest();
builder.Services.AddProjectHubGrpc();
builder.Services.AddProjectHubSignalR();

var app = builder.Build();

// 5. Migrations on startup in development; manual in production.
if (app.Environment.IsDevelopment())
{
    await app.MigrateProjectHubDatabaseAsync();
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 6. Middleware order — fixed. Authentication before authorization
// before endpoint matching. Serilog request logging wraps everything
// so the request span includes the auth decision.
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

// 7. Endpoint mapping — one call per protocol surface.
app.MapProjectHubRest();
app.MapProjectHubGrpc();
app.MapProjectHubSignalR();
app.MapProjectHubHealthChecks();

try
{
    Log.Information("ProjectHub starting");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ProjectHub terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
```

The file does three things, each in a clearly-named block: it builds the host, it composes the middleware pipeline in a fixed order, and it runs. The cross-cutting code that does the actual work — what `AddProjectHubAuth` does, what `AddProjectHubTelemetry` does — lives in `ServiceConfiguration.cs` and `HostConfiguration.cs` under the `ProjectHub.Configuration` namespace. Why split it out? Because the next time we add a protocol surface — a Kafka consumer in Week 13, a hosted background worker, an outbound webhook publisher — we add exactly one line in the right block of this file and one method in the configuration class. We do not rewrite `Program.cs`.

The reference is Microsoft Learn's host fundamentals at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>. Read it once. The "Generic Host" terminology is being slowly retired in favor of "WebApplication" in `WebApplication.CreateBuilder`, but the lifecycle the chapter describes is identical: configuration loads, services register, the host builds, middleware composes, the server starts.

## `ServiceConfiguration` — the cross-cutting extension methods

The `ServiceConfiguration` static class is where the cross-cutting concerns live. Here is the auth method.

```csharp
namespace ProjectHub.Configuration;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

public static class AuthServiceConfiguration
{
    public static IServiceCollection AddProjectHubAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var signingKey = jwtSection.GetValue<string>("SigningKey")
            ?? throw new InvalidOperationException("Jwt:SigningKey is required");
        var issuer = jwtSection.GetValue<string>("Issuer") ?? "projecthub";
        var audience = jwtSection.GetValue<string>("Audience") ?? "projecthub-clients";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                // The SignalR-on-WebSocket-upgrade trick: pull the JWT
                // out of the query string for hub paths only.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireOrg", policy =>
                policy.RequireClaim("org_id"));
        });

        return services;
    }
}
```

Three things deserve attention. First, the signing-key, issuer, and audience all come from `IConfiguration`. None of them are hard-coded. The dev `appsettings.Development.json` ships a known-bad key (`"DevelopmentKeyDoNotUseInProductionMustBeAtLeastSixtyFourCharactersLong"`) so the integration tests pass; production overrides it via the `Jwt__SigningKey` environment variable. The double underscore is the cross-platform convention for nested-config keys; on Windows it can be a colon (`Jwt:SigningKey`) too. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/>.

Second, **the `OnMessageReceived` event is exactly the Week 11 trick, generalized**. SignalR's WebSocket upgrade cannot carry an `Authorization` header after the upgrade frame, so the SDK puts the token in the `access_token` query string of the negotiate request. The bearer middleware reads it from there if the request path starts with `/hubs`. We could have set this up only when registering SignalR, but the bearer middleware does not know about the SignalR-vs-REST split; the auth registration owns it. The path check is the discriminator. Cite <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>.

Third, the `RequireOrg` policy is registered **once** and used by every protocol surface. REST endpoints call `.RequireAuthorization("RequireOrg")` on the route group; gRPC service methods carry `[Authorize(Policy = "RequireOrg")]`; the hub class carries `[Authorize(Policy = "RequireOrg")]`. The policy is the single source of truth about "this endpoint requires the caller's JWT to carry an `org_id` claim." If we add a second policy — `RequireAdmin` — we add it here, in one place, and the endpoints reference it.

## EF Core registration and the scoping trap

The persistence registration is shorter than the auth one but the scoping subtlety is sharper.

```csharp
namespace ProjectHub.Configuration;

using Microsoft.EntityFrameworkCore;
using ProjectHub.Data;

public static class PersistenceServiceConfiguration
{
    public static IServiceCollection AddProjectHubPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ProjectHub")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:ProjectHub is required");

        services.AddDbContextPool<ProjectHubDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
                npgsql.CommandTimeout(30);
            });
            options.EnableSensitiveDataLogging(
                configuration.GetValue<bool>("Database:LogParameters"));
        });

        // The factory is registered alongside the pool so that long-lived
        // services — the hub broadcaster, hosted background workers — can
        // resolve a fresh context without coupling to a request scope.
        services.AddDbContextFactory<ProjectHubDbContext>(
            options => options.UseNpgsql(connectionString),
            lifetime: ServiceLifetime.Singleton);

        return services;
    }

    public static async Task MigrateProjectHubDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
        await db.Database.MigrateAsync();
    }
}
```

`AddDbContextPool` is the second-week-of-EF-Core registration; `AddDbContextFactory` is the new one. The reason both exist is the scoping trap: REST endpoints and gRPC service methods run inside a per-request scope, so injecting `ProjectHubDbContext` directly works and the framework's `IServiceProvider.CreateScope` machinery returns the pooled instance. SignalR's hub method invocations also run inside per-call scopes, so the same direct-injection pattern works inside a `Hub` method.

But SignalR's `IHubContext<TProjectEventsHub>` — the singleton service that lets non-hub code (a REST handler) broadcast to hub clients — does **not** run inside a scope. If you write this:

```csharp
public class ProjectEventsBroadcaster
{
    private readonly IHubContext<ProjectEventsHub> _hub;
    private readonly ProjectHubDbContext _db; // BUG: _db is a captured singleton.
    public ProjectEventsBroadcaster(IHubContext<ProjectEventsHub> hub, ProjectHubDbContext db)
    { _hub = hub; _db = db; }
}
```

the DI container will throw on construction: "Cannot consume scoped service `ProjectHubDbContext` from singleton". If you register `ProjectEventsBroadcaster` as scoped instead, the singleton `IHubContext` is fine but every caller resolves a fresh broadcaster, and the scoped `_db` is whatever scope was active at construction time — which may be one of three depending on where the broadcaster is injected.

The safe pattern is the factory:

```csharp
public class ProjectEventsBroadcaster
{
    private readonly IHubContext<ProjectEventsHub> _hub;
    private readonly IDbContextFactory<ProjectHubDbContext> _dbFactory;

    public ProjectEventsBroadcaster(
        IHubContext<ProjectEventsHub> hub,
        IDbContextFactory<ProjectHubDbContext> dbFactory)
    {
        _hub = hub;
        _dbFactory = dbFactory;
    }

    public async Task BroadcastTaskStatusAsync(Guid taskId, string status)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(taskId);
        if (task is null) return;
        await _hub.Clients
            .Group($"org-{task.OrganizationId}")
            .SendAsync("TaskStatusChanged", new { TaskId = taskId, Status = status });
    }
}
```

`IDbContextFactory<T>` resolves a fresh context per call, owns it for the duration of the operation, and disposes it via `await using`. The factory is a singleton; the contexts it produces are short-lived. This is the canonical "non-request-scope-needs-a-context" pattern; the citation is <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor> — the doc page mentions Blazor because that is where the pattern was popularized, but it applies anywhere a non-scoped service needs a context.

## REST surface — minimal APIs with route groups

The REST registration adds the framework services and the routing helpers.

```csharp
namespace ProjectHub.Configuration;

public static class RestServiceConfiguration
{
    public static IServiceCollection AddProjectHubRest(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }

    public static IEndpointRouteBuilder MapProjectHubRest(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/api/projects")
            .RequireAuthorization("RequireOrg")
            .WithTags("Projects");

        projects.MapGet("/", ProjectEndpoints.ListAsync);
        projects.MapGet("/{id:guid}", ProjectEndpoints.GetByIdAsync);
        projects.MapPost("/", ProjectEndpoints.CreateAsync);
        projects.MapPut("/{id:guid}", ProjectEndpoints.UpdateAsync);
        projects.MapDelete("/{id:guid}", ProjectEndpoints.DeleteAsync);

        var tasks = app.MapGroup("/api/projects/{projectId:guid}/tasks")
            .RequireAuthorization("RequireOrg")
            .WithTags("Tasks");

        tasks.MapGet("/", TaskEndpoints.ListAsync);
        tasks.MapPost("/", TaskEndpoints.CreateAsync);
        tasks.MapPut("/{id:guid}", TaskEndpoints.UpdateAsync);
        tasks.MapDelete("/{id:guid}", TaskEndpoints.DeleteAsync);

        return app;
    }
}
```

The `MapGroup` helper is the right abstraction. It composes a base path, an authorization requirement, an OpenAPI tag, and any endpoint filters into one object that every subsequent `MapGet` / `MapPost` inherits. We chose minimal APIs over controllers for two reasons: the routing is closer to the surface (it is easier to see what URL maps to what code), and the per-endpoint lambda is easier to read than a controller method when the body is short. The cost is that complex endpoints — paged queries with five query parameters, multipart uploads, custom model binders — push harder against the abstraction than they would in a controller. For ProjectHub's CRUD surface, the trade is correct. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis>.

Endpoint handlers live in static classes (`ProjectEndpoints`, `TaskEndpoints`) and follow a fixed signature. Here is the create handler:

```csharp
public static class ProjectEndpoints
{
    public static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        ProjectHubDbContext db,
        ClaimsPrincipal user,
        ProjectEventsBroadcaster broadcaster,
        ILogger<ProjectEndpoints> logger,
        CancellationToken cancellationToken)
    {
        var orgId = user.GetOrgId();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
        await broadcaster.BroadcastProjectCreatedAsync(project);
        logger.LogInformation(
            "Project {ProjectId} created in org {OrgId}", project.Id, orgId);
        return Results.Created($"/api/projects/{project.Id}", project);
    }
}
```

Every parameter the handler needs — the request body, the `DbContext`, the user's `ClaimsPrincipal`, the broadcaster, the logger, the cancellation token — is supplied by the minimal-API parameter binder. The body is bound from JSON; the `ClaimsPrincipal` comes from `HttpContext.User`; the rest come from DI. The handler is unaware of `HttpContext` directly, which is the property that makes minimal-API handlers easy to integration-test.

## gRPC surface — same auth, different protocol

The gRPC registration is shorter because the `Grpc.AspNetCore` package does most of the work.

```csharp
namespace ProjectHub.Configuration;

public static class GrpcServiceConfiguration
{
    public static IServiceCollection AddProjectHubGrpc(this IServiceCollection services)
    {
        services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true; // dev only; flip via config in prod
            options.MaxReceiveMessageSize = 4 * 1024 * 1024; // 4 MB
        });
        return services;
    }

    public static IEndpointRouteBuilder MapProjectHubGrpc(this IEndpointRouteBuilder app)
    {
        app.MapGrpcService<ProjectGrpcService>().RequireAuthorization("RequireOrg");
        return app;
    }
}
```

The `.proto` file lives next to the `.csproj` and is compiled by the `Grpc.Tools` package:

```protobuf
syntax = "proto3";
option csharp_namespace = "ProjectHub.Grpc";

package projecthub;

service Projects {
  rpc List (ListProjectsRequest) returns (ListProjectsResponse);
  rpc Get (GetProjectRequest) returns (Project);
  rpc Create (CreateProjectRequest) returns (Project);
}

message Project {
  string id = 1;
  string name = 2;
  string created_at = 3;
}

message ListProjectsRequest {
  int32 page = 1;
  int32 page_size = 2;
}

message ListProjectsResponse {
  repeated Project items = 1;
  int32 total = 2;
}

message GetProjectRequest { string id = 1; }
message CreateProjectRequest { string name = 1; }
```

The service implementation is a class deriving from the generated `Projects.ProjectsBase`:

```csharp
namespace ProjectHub.Grpc;

[Authorize(Policy = "RequireOrg")]
public class ProjectGrpcService : Projects.ProjectsBase
{
    private readonly ProjectHubDbContext _db;
    private readonly ILogger<ProjectGrpcService> _logger;

    public ProjectGrpcService(
        ProjectHubDbContext db,
        ILogger<ProjectGrpcService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task<ListProjectsResponse> List(
        ListProjectsRequest request,
        ServerCallContext context)
    {
        var user = context.GetHttpContext().User;
        var orgId = user.GetOrgId();
        var query = _db.Projects
            .Where(p => p.OrganizationId == orgId)
            .OrderBy(p => p.Name);
        var total = await query.CountAsync(context.CancellationToken);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(context.CancellationToken);

        var response = new ListProjectsResponse { Total = total };
        response.Items.AddRange(items.Select(p => new Grpc.Project
        {
            Id = p.Id.ToString(),
            Name = p.Name,
            CreatedAt = p.CreatedAt.ToString("o")
        }));
        return response;
    }
}
```

Two observations carry the rest of the lecture. First, the `[Authorize]` attribute is the same attribute REST uses. The bearer middleware ran before the gRPC endpoint matched the request; the user is on `HttpContext.User` accessible via `context.GetHttpContext()`. There is no gRPC-specific auth registration. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>.

Second, the `ProjectHubDbContext` is injected into the service the same way it is injected into a REST handler. The gRPC pipeline creates a per-call scope; the context resolves out of it; the operation completes; the context disposes. The same scoping discipline that applied to REST applies here. If we needed to broadcast a SignalR event from inside a gRPC handler, we would inject the same `ProjectEventsBroadcaster` and call it the same way.

## SignalR surface — same auth, ephemeral state

The SignalR registration is the third one and the shortest.

```csharp
namespace ProjectHub.Configuration;

public static class SignalRServiceConfiguration
{
    public static IServiceCollection AddProjectHubSignalR(this IServiceCollection services)
    {
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true; // dev only
            options.MaximumReceiveMessageSize = 32 * 1024; // 32 KB; we send small events
        });
        services.AddSingleton<ProjectEventsBroadcaster>();
        return services;
    }

    public static IEndpointRouteBuilder MapProjectHubSignalR(this IEndpointRouteBuilder app)
    {
        app.MapHub<ProjectEventsHub>("/hubs/events")
            .RequireAuthorization("RequireOrg");
        return app;
    }
}
```

The hub class is intentionally thin. It exists primarily to track per-connection group membership; outbound broadcasts happen from `ProjectEventsBroadcaster`.

```csharp
namespace ProjectHub.SignalR;

[Authorize(Policy = "RequireOrg")]
public class ProjectEventsHub : Hub
{
    private readonly ILogger<ProjectEventsHub> _logger;

    public ProjectEventsHub(ILogger<ProjectEventsHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var orgId = Context.User?.GetOrgId();
        if (orgId is null)
        {
            _logger.LogWarning(
                "Connection {ConnectionId} arrived without org_id; aborting",
                Context.ConnectionId);
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"org-{orgId}");
        _logger.LogInformation(
            "Connection {ConnectionId} joined group org-{OrgId}",
            Context.ConnectionId, orgId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var orgId = Context.User?.GetOrgId();
        if (orgId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"org-{orgId}");
        }
        await base.OnDisconnectedAsync(exception);
    }
}
```

Three observations. First, the `[Authorize]` attribute reuses the same policy. Same source of truth across three protocols. Second, the `Context.User` is populated by the same JWT bearer middleware that handles REST — the `access_token` query-string trick is what makes that work and we wired it in `AuthServiceConfiguration`. Third, the group-naming convention (`org-{orgId}`) is the single contract between the hub (which adds connections to the group on connect) and the broadcaster (which sends to the group from outside the hub). If the broadcaster uses a different name, broadcasts silently fail. Lecture 2 covers the OpenTelemetry instrumentation that surfaces silent failures as missing spans.

## Middleware order — why the pipeline is what it is

The order of `app.Use*` calls in `Program.cs` is not arbitrary. Read the order carefully:

1. `UseSerilogRequestLogging()`. The wrapper that produces one structured log line per request. Goes first so the line includes everything that follows.
2. `UseAuthentication()`. Reads the `Authorization` header (or the `access_token` query string for hub paths), validates the JWT, sets `HttpContext.User`.
3. `UseAuthorization()`. Reads the `[Authorize]` attributes on the matched endpoint, evaluates them against `HttpContext.User`, short-circuits with `401` or `403` if the check fails.
4. The endpoint mapping calls (`MapProjectHubRest`, `MapProjectHubGrpc`, `MapProjectHubSignalR`, `MapProjectHubHealthChecks`) register the routes; the framework's endpoint middleware (implicit) matches and invokes them.

If you put `UseAuthorization` before `UseAuthentication`, every request gets `401` because `HttpContext.User` is the unauthenticated default. If you put `UseSerilogRequestLogging` after `UseAuthentication`, the request-log line will not include the user identity (the wrapper captures the state at the point in the pipeline it sat). If you forget `UseAuthorization` entirely, the `[Authorize]` attributes are inert and every request is allowed through. None of these failures will crash the host on startup; all of them will leak into production silently if not tested. The fix is to write integration tests that exercise the auth path; we do that in Lecture 3.

The citation is Microsoft Learn's middleware fundamentals chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>. Read the "ASP.NET Core middleware order" section every time you change `Program.cs`. It is the third-most-common source of subtle production bugs in ASP.NET Core apps; the first two are connection-string typos and silently-misconfigured logging providers.

## Configuration layering — what overrides what

`appsettings.json` ships with the binary; `appsettings.Development.json` overlays it when `ASPNETCORE_ENVIRONMENT=Development`; environment variables overlay those. The precedence order is:

1. Command-line arguments (`dotnet run -- --Jwt:SigningKey=...`).
2. Environment variables (`Jwt__SigningKey=...`).
3. User secrets (dev only; `dotnet user-secrets set Jwt:SigningKey ...`).
4. `appsettings.{Environment}.json`.
5. `appsettings.json`.

The lookup is "first match wins" so the command-line override beats the environment variable beats the user secret beats the per-environment file beats the base file. We use this layering deliberately. The base `appsettings.json` carries every key with a known-good default or a placeholder; the per-environment file overrides what dev / staging / prod need; the environment variables override secrets (signing keys, DB passwords); the command line overrides for one-off operator commands.

Here is the base `appsettings.json` for ProjectHub:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "Jwt": {
    "Issuer": "projecthub",
    "Audience": "projecthub-clients",
    "SigningKey": "OVERRIDE_ME_VIA_ENV_VAR"
  },
  "ConnectionStrings": {
    "ProjectHub": "Host=localhost;Port=5432;Database=projecthub;Username=projecthub;Password=OVERRIDE_ME"
  },
  "Database": {
    "LogParameters": false
  },
  "OpenTelemetry": {
    "ServiceName": "projecthub",
    "ServiceVersion": "0.12.0",
    "Exporter": "Console"
  }
}
```

The `OVERRIDE_ME` placeholders are deliberate. If a developer forgets to set `Jwt__SigningKey`, the integration tests will fail (the policy will reject every token because the signing key is not what the test-token-issuer used); production will fail on first request. We want the failure loud and early. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/>.

## What we built

By the end of Lecture 1, you have:

- One `Program.cs` of roughly seventy lines that boots a host serving REST, gRPC, and SignalR.
- One `AuthServiceConfiguration` that registers JWT bearer with the SignalR-upgrade extension and the `RequireOrg` policy used by every protocol.
- One `PersistenceServiceConfiguration` that registers a pooled `DbContext` and a `DbContextFactory` for non-scoped consumers.
- Three protocol-surface modules — `RestServiceConfiguration`, `GrpcServiceConfiguration`, `SignalRServiceConfiguration` — each with one `Add*` and one `Map*` method.
- A `ProjectEventsBroadcaster` singleton that resolves a fresh `DbContext` per broadcast.
- A fixed middleware order with documented reasoning for every line.
- A configuration layering that ships placeholders for every secret and fails loudly when an override is missing.

Lecture 2 wires this host to Serilog and OpenTelemetry and reads the resulting trace by hand. Lecture 3 wraps the whole thing in `WebApplicationFactory<Program>` and writes integration tests that boot the host against an ephemeral PostgreSQL container.

The slogan, again: composition is not "more code." It is "fewer surprises." Every shortcut you skip in the cross-cutting layer becomes a 401 on the SignalR upgrade at 3am that nobody can reproduce because the REST surface is fine. Spend the time. Wire the cross-cutting concerns once.
