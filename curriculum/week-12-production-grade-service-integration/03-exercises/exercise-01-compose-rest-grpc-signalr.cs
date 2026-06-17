// Exercise 01 — Compose REST, gRPC, and SignalR in one ASP.NET Core 8 host.
//
// Goal: stand up a single Program.cs that registers and routes all three
// protocol surfaces, sharing a JwtBearer authentication scheme and an
// authorization policy. Verify with curl (REST), grpcurl (gRPC), and a
// simple .NET HubConnection client (SignalR) that an unauthenticated
// request gets 401 on every surface and an authenticated one gets 200/OK.
//
// Estimated time: 90 minutes. The solution is in SOLUTIONS.md.
//
// Setup:
//   dotnet new web -n ProjectHub.Exercise01 --framework net8.0
//   cd ProjectHub.Exercise01
//   dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.*
//   dotnet add package Grpc.AspNetCore --version 2.60.*
//   dotnet add package Microsoft.AspNetCore.SignalR --version 1.2.*  (in-box; only needed for the SDK ref)
//   dotnet add package System.IdentityModel.Tokens.Jwt --version 7.0.*
//
// Then add a Protos/projects.proto file with the contents at the bottom
// of this file (search for "PROTO FILE"). Add the proto to the .csproj:
//   <ItemGroup>
//     <Protobuf Include="Protos\projects.proto" GrpcServices="Server" />
//   </ItemGroup>
//
// Run: dotnet run. The host should listen on https://localhost:5001.
// Verify all three protocols are alive (see VERIFICATION at the bottom).
//
// References:
//   https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn
//   https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz
//   https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz
//   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/

#nullable enable

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace ProjectHub.Exercise01;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ----------------------------------------------------------------
        // TASK 1. Register JWT bearer authentication.
        //
        // The configuration carries Jwt:SigningKey, Jwt:Issuer, Jwt:Audience.
        // Apply TokenValidationParameters that validate all four properties
        // (issuer, audience, lifetime, signing key) and add an
        // OnMessageReceived event that, for paths starting with /hubs,
        // reads the bearer token from the access_token query string instead
        // of the Authorization header. This is the Week 11 trick — restated.
        //
        // Citation: https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz
        // ----------------------------------------------------------------
        var jwtSection = builder.Configuration.GetSection("Jwt");
        var signingKey = jwtSection.GetValue<string>("SigningKey")
            ?? "ExerciseOneKeyDoNotShipMustBeAtLeastSixtyFourCharactersLongOk";
        var issuer = jwtSection.GetValue<string>("Issuer") ?? "exercise-01";
        var audience = jwtSection.GetValue<string>("Audience") ?? "exercise-01-clients";

        builder.Services
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

        // ----------------------------------------------------------------
        // TASK 2. Add an authorization policy named "RequireOrg" that
        // requires the caller's JWT to carry an "org_id" claim. Every
        // protocol surface in this exercise will apply that policy.
        // ----------------------------------------------------------------
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireOrg", policy =>
                policy.RequireClaim("org_id"));
        });

        // ----------------------------------------------------------------
        // TASK 3. Register the three protocol surfaces.
        //   - SignalR via AddSignalR
        //   - gRPC via AddGrpc
        //   - REST endpoints are added in the routing block below
        // ----------------------------------------------------------------
        builder.Services.AddSignalR();
        builder.Services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        });

        var app = builder.Build();

        // ----------------------------------------------------------------
        // TASK 4. Compose the middleware pipeline in the right order:
        //   1. UseAuthentication
        //   2. UseAuthorization
        //   3. Endpoint mapping
        //
        // If you reverse 1 and 2, every request gets 401. If you forget 2,
        // [Authorize] becomes a no-op and every request is allowed through.
        // ----------------------------------------------------------------
        app.UseAuthentication();
        app.UseAuthorization();

        // ----------------------------------------------------------------
        // TASK 5. REST surface — two endpoints behind the RequireOrg policy.
        //
        //   GET  /api/whoami    — return the caller's claims
        //   GET  /api/ping       — anonymous; should return 200 even without a token
        // ----------------------------------------------------------------
        var rest = app.MapGroup("/api");

        rest.MapGet("/ping", () => Results.Ok(new { ok = true, ts = DateTime.UtcNow }))
            .AllowAnonymous();

        rest.MapGet("/whoami", (ClaimsPrincipal user) =>
            {
                var claims = user.Claims.Select(c => new { c.Type, c.Value });
                return Results.Ok(new
                {
                    name = user.Identity?.Name,
                    authenticated = user.Identity?.IsAuthenticated ?? false,
                    claims
                });
            })
            .RequireAuthorization("RequireOrg");

        // ----------------------------------------------------------------
        // TASK 6. gRPC surface — register the ProjectsGrpcService below.
        // Apply [Authorize(Policy = "RequireOrg")] at the class level (see
        // ProjectsGrpcService below). The endpoint mapping just registers
        // the service; the attribute handles auth.
        // ----------------------------------------------------------------
        app.MapGrpcService<ProjectsGrpcService>();

        // ----------------------------------------------------------------
        // TASK 7. SignalR surface — map the EventsHub below at /hubs/events.
        // The hub class carries [Authorize(Policy = "RequireOrg")] so the
        // negotiate request and every subsequent invocation require auth.
        // ----------------------------------------------------------------
        app.MapHub<EventsHub>("/hubs/events");

        // ----------------------------------------------------------------
        // TASK 8 (bonus). Add an endpoint that mints a token. NOT for
        // production — it lets the curl / grpcurl verification commands
        // pull a token without writing a separate issuer.
        // ----------------------------------------------------------------
        app.MapPost("/dev/mint-token", (MintTokenRequest req) =>
        {
            var token = DevTokenIssuer.Issue(
                subject: req.Subject ?? "dev-user",
                orgId: req.OrgId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                signingKey: signingKey,
                issuer: issuer,
                audience: audience);
            return Results.Ok(new { access_token = token });
        }).AllowAnonymous();

        await app.RunAsync();
    }
}

public record MintTokenRequest(string? Subject, Guid? OrgId);

// --------------------------------------------------------------------------
// gRPC service implementation. The generated base class comes from the
// projects.proto file (see PROTO FILE below). For this exercise the
// implementation just echoes the caller's org id.
// --------------------------------------------------------------------------
[Authorize(Policy = "RequireOrg")]
public class ProjectsGrpcService : ProjectHub.Exercise01.Grpc.Projects.ProjectsBase
{
    public override Task<ProjectHub.Exercise01.Grpc.WhoAmIResponse> WhoAmI(
        ProjectHub.Exercise01.Grpc.WhoAmIRequest request,
        Grpc.Core.ServerCallContext context)
    {
        var user = context.GetHttpContext().User;
        var orgId = user.FindFirst("org_id")?.Value ?? "(none)";
        return Task.FromResult(new ProjectHub.Exercise01.Grpc.WhoAmIResponse
        {
            Subject = user.Identity?.Name ?? "(none)",
            OrgId = orgId
        });
    }
}

// --------------------------------------------------------------------------
// SignalR hub. [Authorize] applies the policy to every invocation,
// including the negotiate request. The OnConnectedAsync hook adds the
// connection to a per-org group.
// --------------------------------------------------------------------------
[Authorize(Policy = "RequireOrg")]
public class EventsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var orgId = Context.User?.FindFirst("org_id")?.Value;
        if (!string.IsNullOrEmpty(orgId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"org-{orgId}");
        }
        await base.OnConnectedAsync();
    }

    public async Task BroadcastTest(string message)
    {
        var orgId = Context.User?.FindFirst("org_id")?.Value;
        await Clients.Group($"org-{orgId}").SendAsync("TestBroadcast", message);
    }
}

// --------------------------------------------------------------------------
// Helper that mints a JWT for the verification steps. Do not ship this
// pattern to production — token issuance belongs in a separate auth
// service. This is the smallest possible "give me a working token" hook.
// --------------------------------------------------------------------------
public static class DevTokenIssuer
{
    public static string Issue(
        string subject,
        Guid orgId,
        string signingKey,
        string issuer,
        string audience,
        TimeSpan? lifetime = null)
    {
        var claims = new[]
        {
            new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, subject),
            new Claim("org_id", orgId.ToString()),
            new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}

// --------------------------------------------------------------------------
// VERIFICATION (run after dotnet run):
//
//   # 1. Anonymous ping — REST endpoint that does not require auth.
//   curl -k https://localhost:5001/api/ping
//   # expect: {"ok":true,"ts":"2026-05-15T..."}
//
//   # 2. Mint a token.
//   TOKEN=$(curl -k -s -X POST https://localhost:5001/dev/mint-token \
//     -H "content-type: application/json" -d '{}' | jq -r .access_token)
//   echo $TOKEN
//
//   # 3. Whoami with the token.
//   curl -k -H "authorization: bearer $TOKEN" https://localhost:5001/api/whoami
//   # expect: 200 with the claims array including org_id
//
//   # 4. Whoami without the token.
//   curl -k -i https://localhost:5001/api/whoami
//   # expect: HTTP/2 401
//
//   # 5. gRPC whoami with the token (requires grpcurl).
//   grpcurl -insecure -H "authorization: bearer $TOKEN" \
//     -d '{}' localhost:5001 projecthub.Projects/WhoAmI
//   # expect: { "subject": "dev-user", "orgId": "11111111-..." }
//
//   # 6. gRPC whoami without the token.
//   grpcurl -insecure -d '{}' localhost:5001 projecthub.Projects/WhoAmI
//   # expect: Unauthenticated (StatusCode 16)
//
//   # 7. SignalR negotiate without a token.
//   curl -k -i -X POST "https://localhost:5001/hubs/events/negotiate?negotiateVersion=1"
//   # expect: HTTP/2 401
//
//   # 8. SignalR negotiate with a token in the query string.
//   curl -k -i -X POST "https://localhost:5001/hubs/events/negotiate?negotiateVersion=1&access_token=$TOKEN"
//   # expect: HTTP/2 200 and a JSON body with connectionId / availableTransports.
//
// If all eight verifications pass, the composition is correct. The auth
// pipeline is feeding all three protocols, the OnMessageReceived hook is
// extracting the SignalR query-string token, and the policies are
// rejecting anonymous traffic on every protected surface.
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// PROTO FILE — paste this into Protos/projects.proto.
//
//   syntax = "proto3";
//   option csharp_namespace = "ProjectHub.Exercise01.Grpc";
//   package projecthub;
//
//   service Projects {
//     rpc WhoAmI (WhoAmIRequest) returns (WhoAmIResponse);
//   }
//
//   message WhoAmIRequest {}
//   message WhoAmIResponse {
//     string subject = 1;
//     string org_id = 2;
//   }
//
// Build will fail until the proto exists and the .csproj includes the
// <Protobuf> item shown at the top of this file.
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// Common stumbles:
//
// - 401 on REST too: the `RequireOrg` policy is correctly enforcing
//   org_id; the dev-mint-token endpoint must be MapPost not MapGet, and
//   you must AllowAnonymous() on it.
// - gRPC returns "Unimplemented" instead of "Unauthenticated": the proto
//   service name does not match the registration. Check the package /
//   csharp_namespace lines in the .proto.
// - SignalR negotiate with token returns 401: the OnMessageReceived hook
//   is checking a path that does not start with /hubs. Print the path in
//   the hook and verify it.
// - "Schemes cannot include scheme alias": you called AddJwtBearer twice
//   with the same default name. Use named schemes if you need two.
//
// Stretch goal: register a second AddJwtBearer scheme called "InternalRpc"
// that validates against a different signing key, and add a gRPC method
// authorized against that scheme only. This is the foundation for a
// service-to-service auth pattern we revisit in Week 14.
// --------------------------------------------------------------------------
