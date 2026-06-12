// ProjectHub / src/ProjectHub / ProjectEventsHub.cs
//
// The SignalR surface plus the singleton broadcaster that the REST
// status-change endpoint calls. This file is where the DbContext-scoping
// discipline from Lecture 1 is load-bearing: the broadcaster is a SINGLETON
// (it holds an IHubContext for its whole lifetime), so it must NOT capture a
// scoped DbContext. When it needs the database it creates a fresh context
// from IDbContextFactory<ProjectHubDbContext>. Capturing a scoped context in
// this singleton is the exact captive-dependency bug that throws "A second
// operation was started on this context instance..." under concurrency.
//
// Citations:
//   Hubs:           https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs
//   Auth on hubs:   https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz
//   IHubContext:    https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext
//   DbContextFactory: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory
//   Captive deps:   https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#scoped-service-as-singleton

#nullable enable

using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ProjectHub;

// Strongly-typed client interface. The C# method name is the wire-format
// target string the client subscribes to with connection.on("TaskStatusChanged", ...).
public interface IProjectClient
{
    Task TaskStatusChanged(Guid taskId, string status);
}

[Authorize(Policy = "RequireOrg")]
public sealed class ProjectEventsHub : Hub<IProjectClient>
{
    private readonly ILogger<ProjectEventsHub> _log;

    public ProjectEventsHub(ILogger<ProjectEventsHub> log) => _log = log;

    public override async Task OnConnectedAsync()
    {
        // org_id is guaranteed by the RequireOrg policy on the hub.
        var orgId = Context.User?.FindFirst("org_id")?.Value;
        if (!string.IsNullOrEmpty(orgId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"org-{orgId}");
            _log.LogInformation(
                "hub connected: connId={ConnId} org={OrgId}",
                Context.ConnectionId, orgId);
        }
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Group memberships are discarded by the framework on disconnect
        // (Week 11, Q4/Q8). Nothing to clean up here; logged for the trace.
        _log.LogInformation(
            "hub disconnected: connId={ConnId} reason={Reason}",
            Context.ConnectionId, exception?.Message ?? "clean");
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Singleton helper the REST handler injects to push a broadcast. It is a
/// singleton because IHubContext is safe to hold for the process lifetime —
/// but that is exactly why it must resolve its own DbContext per call rather
/// than capturing a scoped one. Registered with services.AddSingleton in
/// Program.cs.
/// </summary>
public sealed class ProjectEventsBroadcaster
{
    private readonly IHubContext<ProjectEventsHub, IProjectClient> _hub;
    private readonly IDbContextFactory<ProjectHubDbContext> _dbFactory;
    private readonly ILogger<ProjectEventsBroadcaster> _log;

    public ProjectEventsBroadcaster(
        IHubContext<ProjectEventsHub, IProjectClient> hub,
        IDbContextFactory<ProjectHubDbContext> dbFactory,
        ILogger<ProjectEventsBroadcaster> log)
    {
        _hub = hub;
        // Note: IDbContextFactory, NOT ProjectHubDbContext. Injecting the
        // scoped context here is the captive-dependency bug — it would be
        // captured once, shared forever, and blow up under concurrency.
        _dbFactory = dbFactory;
        _log = log;
    }

    /// <summary>
    /// Broadcast a status change to the org's SignalR group. (stub)
    /// </summary>
    public async Task BroadcastStatusChanged(
        Guid orgId, Guid taskId, ProjectTaskStatus status)
    {
        // TODO (1): start a named span so the broadcast is its own node in
        //   the Jaeger waterfall:
        //   using var activity = ProjectEndpoints.AppActivity
        //       .StartActivity("BroadcastStatusChanged");
        //   activity?.SetTag("org.id", orgId);
        //   activity?.SetTag("task.id", taskId);
        //
        // TODO (2): if you need to read/write the database from here (e.g.
        //   an outbox row, stretch goal 5), create a FRESH context — never
        //   capture one in a field:
        //       await using var db = await _dbFactory.CreateDbContextAsync();
        //       ... use db ...
        //   This is the whole reason the broadcaster takes IDbContextFactory.
        //
        // TODO (3): send to the org group via the strongly-typed client:
        //   await _hub.Clients.Group($"org-{orgId}")
        //       .TaskStatusChanged(taskId, status.ToString());
        //
        // TODO (4): log it with structured properties.
        //
        // https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext
        _ = (orgId, taskId, status, _hub, _dbFactory, _log);
        throw new NotImplementedException(
            "BroadcastStatusChanged: span + (optional) fresh-context DB read + group send. See the TODOs.");
    }

    // Demonstration of the SAFE pattern, given so you have a reference: this
    // is how you touch the database from a singleton — a fresh context per
    // call, disposed when the call returns. Never a captured scoped context.
    private async Task<int> CountOpenTasksForOrg(Guid orgId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks
            .CountAsync(t => t.Project!.OrganizationId == orgId
                          && t.Status == ProjectTaskStatus.Open);
    }
}
