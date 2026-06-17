// ProjectHub / src/ProjectHub / ProjectEndpoints.cs
//
// The REST surface as a minimal-API route group. The list and create
// endpoints are given complete so you can see the editorial style; the
// cross-protocol status-change endpoint — the one that writes via EF Core
// AND broadcasts to SignalR, and is the centerpiece of the Jaeger trace —
// is stubbed for you to finish.
//
// Every endpoint is tenant-scoped: it reads org_id off the ClaimsPrincipal
// and filters on it. Cross-tenant fetches return 404, never 403 — do not
// leak the existence of another tenant's data.
//
// Citations:
//   Minimal APIs:   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis
//   Route groups:   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers#route-groups
//   Custom spans:   https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs
//   EF Core query:  https://learn.microsoft.com/en-us/ef/core/querying/

#nullable enable

using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace ProjectHub;

public static class ProjectEndpoints
{
    // The hand-written spans hang off this source. AddSource("ProjectHub")
    // in the telemetry registration must match ServiceConfiguration
    // .ActivitySourceName, or these spans never reach the exporter.
    public static readonly ActivitySource AppActivity =
        new(ServiceConfiguration.ActivitySourceName);

    public static RouteGroupBuilder MapProjectEndpoints(this WebApplication app)
    {
        // One group, one authorization policy. Every endpoint below inherits
        // RequireOrg, so an anonymous (or org_id-less) caller gets 401/403
        // before the handler runs.
        var group = app.MapGroup("/api/projects")
            .RequireAuthorization("RequireOrg")
            .WithTags("projects");

        // GET /api/projects — list the caller's org's projects. (given)
        group.MapGet("/", async (ProjectHubDbContext db, ClaimsPrincipal user) =>
        {
            var orgId = OrgIdOf(user);
            var projects = await db.Projects
                .Where(p => p.OrganizationId == orgId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new ProjectDto(p.Id, p.Name, p.CreatedAt))
                .ToListAsync();
            return Results.Ok(projects);
        });

        // GET /api/projects/{id} — one project, tenant-scoped. (given)
        group.MapGet("/{id:guid}", async (
            Guid id, ProjectHubDbContext db, ClaimsPrincipal user) =>
        {
            var orgId = OrgIdOf(user);
            var project = await db.Projects
                .Where(p => p.Id == id && p.OrganizationId == orgId)
                .Select(p => new ProjectDto(p.Id, p.Name, p.CreatedAt))
                .FirstOrDefaultAsync();
            // 404 (not 403) on a cross-tenant id: do not confirm it exists.
            return project is null ? Results.NotFound() : Results.Ok(project);
        });

        // POST /api/projects — create, scoped to the caller's org. (given)
        group.MapPost("/", async (
            CreateProjectRequest body, ProjectHubDbContext db,
            ClaimsPrincipal user, ILogger<Project> log) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = new[] { "Name is required and must be 1-200 characters." }
                });
            }

            var orgId = OrgIdOf(user);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                Name = body.Name.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();

            // Structured properties, not interpolation — {OrgId}/{ProjectId}
            // stay queryable fields in the Serilog JSON output.
            log.LogInformation("Project {ProjectId} created in org {OrgId}",
                project.Id, orgId);

            return Results.Created($"/api/projects/{project.Id}",
                new ProjectDto(project.Id, project.Name, project.CreatedAt));
        });

        // POST /api/projects/{id}/tasks — add a task. (given)
        group.MapPost("/{id:guid}/tasks", async (
            Guid id, CreateTaskRequest body, ProjectHubDbContext db,
            ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title) || body.Title.Length > 500)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["title"] = new[] { "Title is required and must be 1-500 characters." }
                });
            }

            var orgId = OrgIdOf(user);
            // Confirm the project is in the caller's org before attaching.
            var owns = await db.Projects
                .AnyAsync(p => p.Id == id && p.OrganizationId == orgId);
            if (!owns) return Results.NotFound();

            var task = new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = id,
                Title = body.Title.Trim(),
                Status = ProjectTaskStatus.Open,
                CreatedAt = DateTime.UtcNow
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return Results.Created($"/api/projects/{id}/tasks/{task.Id}",
                new TaskDto(task.Id, task.Title, task.Status));
        });

        // POST /api/projects/{id}/tasks/{taskId}/status — THE CROSS-PROTOCOL
        // PATH. (stub) This is the endpoint the whole week builds toward: it
        // updates a row via EF Core AND broadcasts to SignalR, and Challenge
        // 1's Jaeger trace shows one trace id spanning the inbound HTTP span,
        // this handler's UpdateTaskStatus span, the Npgsql UPDATE span, and
        // the broadcaster's BroadcastStatusChanged span.
        group.MapPost("/{id:guid}/tasks/{taskId:guid}/status", async (
            Guid id, Guid taskId, UpdateStatusRequest body,
            ProjectHubDbContext db, ProjectEventsBroadcaster broadcaster,
            ClaimsPrincipal user, ILogger<ProjectTask> log) =>
        {
            // TODO (1): start an application span:
            //   using var activity = AppActivity.StartActivity("UpdateTaskStatus");
            //   activity?.SetTag("project.id", id);
            //   activity?.SetTag("task.id", taskId);
            //   activity?.SetTag("task.status", body.Status.ToString());
            //
            // TODO (2): load the task tenant-scoped (join through Project to
            //   the caller's org); return Results.NotFound() if it is not in
            //   the caller's org. Do NOT 403.
            //
            // TODO (3): set task.Status = body.Status; await db.SaveChangesAsync();
            //   The Npgsql instrumentation parents the UPDATE span under your
            //   activity automatically (Activity.Current flows via AsyncLocal).
            //
            // TODO (4): await broadcaster.BroadcastStatusChanged(orgId, taskId,
            //   body.Status); — the SignalR span, same trace.
            //
            // TODO (5): log it with structured properties and return
            //   Results.NoContent().
            //
            // https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing
            _ = (id, taskId, body, db, broadcaster, user, log);
            throw new NotImplementedException(
                "Status-change endpoint: span + tenant-scoped load + SaveChanges + broadcast. See the TODOs.");
        });

        return group;
    }

    // org_id is validated to exist by the RequireOrg policy, so the parse is
    // safe inside these handlers. If you reach here without one, the policy
    // is misconfigured.
    private static Guid OrgIdOf(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("org_id")!);
}

public record CreateProjectRequest(string Name);
public record CreateTaskRequest(string Title);
public record UpdateStatusRequest(ProjectTaskStatus Status);

public record ProjectDto(Guid Id, string Name, DateTime CreatedAt);
public record TaskDto(Guid Id, string Title, ProjectTaskStatus Status);
