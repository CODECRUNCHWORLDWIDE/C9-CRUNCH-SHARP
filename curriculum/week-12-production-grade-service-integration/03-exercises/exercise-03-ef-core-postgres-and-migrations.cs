// Exercise 03 — EF Core with the Npgsql PostgreSQL provider. Add a
// DbContext, two entities, and an initial migration. Apply the migration
// on startup in development. Reproduce the DbContext-from-singleton
// scoping trap and fix it with IDbContextFactory.
//
// Estimated time: 90 minutes. The solution is in SOLUTIONS.md.
//
// Setup (additive to exercise-02):
//   dotnet add package Microsoft.EntityFrameworkCore --version 8.0.*
//   dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.*
//   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.*
//   dotnet tool install --global dotnet-ef --version 8.0.0
//
// A local Postgres is needed. Easiest path:
//   docker run --name pg-ex03 -e POSTGRES_PASSWORD=devpass -p 5432:5432 -d postgres:16
//   docker exec -it pg-ex03 createdb -U postgres projecthub_ex03
//
// References:
//   https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
//   https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
//   https://www.npgsql.org/efcore/
//   https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory-eg-for-blazor

#nullable enable

using Microsoft.EntityFrameworkCore;

namespace ProjectHub.Exercise03;

// --------------------------------------------------------------------------
// TASK 1. Define the Project and ProjectTask entities. Use Guid primary
// keys, a non-null Name, an OrganizationId for tenancy, and a CreatedAt
// timestamp. ProjectTask carries a foreign key back to its project and a
// Status enum.
// --------------------------------------------------------------------------

public class Project
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<ProjectTask> Tasks { get; set; } = new();
}

public class ProjectTask
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = string.Empty;
    public TaskStatus Status { get; set; } = TaskStatus.Open;
    public DateTime CreatedAt { get; set; }
}

public enum TaskStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3
}

// --------------------------------------------------------------------------
// TASK 2. Define the DbContext. Two DbSet properties; a configured model
// in OnModelCreating that sets table names to snake_case (Postgres
// convention) and indexes by OrganizationId for tenant-scoped queries.
// --------------------------------------------------------------------------

public class ProjectHubDbContext : DbContext
{
    public ProjectHubDbContext(DbContextOptions<ProjectHubDbContext> options)
        : base(options) { }

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var project = modelBuilder.Entity<Project>();
        project.ToTable("projects");
        project.HasKey(p => p.Id);
        project.Property(p => p.Name).HasMaxLength(200).IsRequired();
        project.HasIndex(p => p.OrganizationId);

        var task = modelBuilder.Entity<ProjectTask>();
        task.ToTable("tasks");
        task.HasKey(t => t.Id);
        task.Property(t => t.Title).HasMaxLength(500).IsRequired();
        task.HasIndex(t => t.ProjectId);
        task.HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// --------------------------------------------------------------------------
// TASK 3. The Program class wires the host. Three registrations matter:
//
//   1. AddDbContextPool — for per-request scopes (REST handlers, gRPC).
//   2. AddDbContextFactory — for non-request-scoped consumers (a
//      hypothetical singleton background broadcaster).
//   3. A migration-on-startup hook in development.
//
// The verification at the bottom reproduces the scoping trap: a singleton
// service that captures a DbContext throws on construction; the fix is
// to inject IDbContextFactory<T> instead.
// --------------------------------------------------------------------------

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var connectionString = builder.Configuration.GetConnectionString("ProjectHub")
            ?? "Host=localhost;Port=5432;Database=projecthub_ex03;Username=postgres;Password=devpass";

        builder.Services.AddDbContextPool<ProjectHubDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure());
            if (builder.Environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        builder.Services.AddDbContextFactory<ProjectHubDbContext>(
            options => options.UseNpgsql(connectionString),
            lifetime: ServiceLifetime.Singleton);

        // TASK 4. Register a singleton service that demonstrates the
        // factory pattern. This service runs outside a request scope —
        // imagine it being called from a background timer — and resolves
        // a fresh DbContext per operation.
        builder.Services.AddSingleton<ProjectStatsService>();

        var app = builder.Build();

        // TASK 5. Apply migrations on startup in development. In production,
        // migrations would run as part of the deployment pipeline rather
        // than from the application itself.
        if (app.Environment.IsDevelopment())
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
            await db.Database.MigrateAsync();
        }

        // --------------------------------------------------------------
        // REST endpoints. Plain CRUD on /api/projects, all using the
        // scoped DbContext injected by the framework.
        // --------------------------------------------------------------
        app.MapGet("/api/projects", async (ProjectHubDbContext db, Guid orgId) =>
        {
            var projects = await db.Projects
                .Where(p => p.OrganizationId == orgId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync();
            return Results.Ok(projects);
        });

        app.MapPost("/api/projects", async (
            ProjectHubDbContext db,
            CreateProjectRequest request) =>
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                OrganizationId = request.OrgId,
                Name = request.Name,
                CreatedAt = DateTime.UtcNow
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            return Results.Created($"/api/projects/{project.Id}", project);
        });

        app.MapPost("/api/projects/{projectId:guid}/tasks", async (
            Guid projectId,
            ProjectHubDbContext db,
            CreateTaskRequest request) =>
        {
            var project = await db.Projects.FindAsync(projectId);
            if (project is null) return Results.NotFound();

            var task = new ProjectTask
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Title = request.Title,
                Status = TaskStatus.Open,
                CreatedAt = DateTime.UtcNow
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tasks/{task.Id}", task);
        });

        // --------------------------------------------------------------
        // Stats endpoint that calls the singleton ProjectStatsService.
        // The service uses IDbContextFactory to resolve a fresh context.
        // --------------------------------------------------------------
        app.MapGet("/api/stats", async (
            ProjectStatsService stats,
            Guid orgId) =>
        {
            var result = await stats.GetStatsAsync(orgId);
            return Results.Ok(result);
        });

        await app.RunAsync();
    }
}

public record CreateProjectRequest(Guid OrgId, string Name);
public record CreateTaskRequest(string Title);

// --------------------------------------------------------------------------
// TASK 6. The singleton service that demonstrates the IDbContextFactory
// pattern. Note the constructor: it injects IDbContextFactory<T>, NOT
// ProjectHubDbContext directly. Injecting the context directly would
// throw at construction with "Cannot consume scoped service from singleton".
//
// In each operation, the service resolves a fresh DbContext, uses it, and
// disposes it via `await using`. This is the pattern for any non-request-
// scoped code that needs database access — background workers, hosted
// services, the SignalR IHubContext broadcaster from the mini-project.
// --------------------------------------------------------------------------

public class ProjectStatsService
{
    private readonly IDbContextFactory<ProjectHubDbContext> _dbFactory;
    private readonly ILogger<ProjectStatsService> _logger;

    public ProjectStatsService(
        IDbContextFactory<ProjectHubDbContext> dbFactory,
        ILogger<ProjectStatsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<ProjectStats> GetStatsAsync(Guid orgId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var projectCount = await db.Projects.CountAsync(p => p.OrganizationId == orgId);
        var taskCount = await db.Tasks
            .Where(t => t.Project!.OrganizationId == orgId)
            .CountAsync();
        var openTasks = await db.Tasks
            .Where(t => t.Project!.OrganizationId == orgId && t.Status == TaskStatus.Open)
            .CountAsync();
        _logger.LogInformation(
            "Org {OrgId} has {ProjectCount} projects and {TaskCount} tasks",
            orgId, projectCount, taskCount);
        return new ProjectStats(projectCount, taskCount, openTasks);
    }
}

public record ProjectStats(int ProjectCount, int TaskCount, int OpenTaskCount);

// --------------------------------------------------------------------------
// MIGRATION COMMANDS
//
// After implementing the entities and the context, generate the initial
// migration:
//
//   dotnet ef migrations add InitialCreate
//
// The command will create a Migrations/ folder with two files:
//   - 20260515_InitialCreate.cs    (the migration up/down)
//   - ProjectHubDbContextModelSnapshot.cs (the model state)
//
// Apply it manually with:
//
//   dotnet ef database update
//
// Or let the app apply it on next start (Database.MigrateAsync in
// Program.cs above).
//
// To see the generated SQL without applying:
//
//   dotnet ef migrations script
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// VERIFICATION
//
//   # 1. Start Postgres (skip if already running).
//   docker run --name pg-ex03 -e POSTGRES_PASSWORD=devpass \
//     -p 5432:5432 -d postgres:16
//   docker exec -it pg-ex03 createdb -U postgres projecthub_ex03
//
//   # 2. Run the migration commands above.
//   dotnet ef migrations add InitialCreate
//   dotnet ef database update
//
//   # 3. Start the host.
//   dotnet run
//
//   # 4. Create a project.
//   ORG=11111111-1111-1111-1111-111111111111
//   curl -k -X POST "https://localhost:5001/api/projects" \
//     -H "content-type: application/json" \
//     -d "{\"orgId\":\"$ORG\",\"name\":\"verification project\"}"
//
//   # 5. List projects.
//   curl -k "https://localhost:5001/api/projects?orgId=$ORG"
//
//   # 6. Add a task.
//   PID=<paste the project id from step 4>
//   curl -k -X POST "https://localhost:5001/api/projects/$PID/tasks" \
//     -H "content-type: application/json" \
//     -d '{"title":"first task"}'
//
//   # 7. Fetch stats — exercises the singleton service path.
//   curl -k "https://localhost:5001/api/stats?orgId=$ORG"
//
// In the Postgres console:
//
//   docker exec -it pg-ex03 psql -U postgres -d projecthub_ex03
//   projecthub_ex03=# \dt
//   projecthub_ex03=# SELECT * FROM projects;
//   projecthub_ex03=# SELECT * FROM tasks;
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// THE SCOPING TRAP — reproduce it
//
// To see the trap, comment out AddDbContextFactory and change
// ProjectStatsService to inject ProjectHubDbContext directly:
//
//   public ProjectStatsService(ProjectHubDbContext db, ILogger<...> logger)
//
// Then `dotnet run`. The host will start; the first call to /api/stats
// will throw:
//
//   InvalidOperationException: Cannot consume scoped service
//   'ProjectHubDbContext' from singleton 'ProjectStatsService'.
//
// The fix is the factory pattern shown above. The framework's DI
// validator catches this at the first resolution — earlier in dev than
// in production because dev validates on startup. Citation:
//   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection#service-lifetimes
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// Common stumbles:
//
// - "Could not load type ProjectHubDbContextModelSnapshot": the migration
//   was generated in a different project and the snapshot file is stale.
//   Delete Migrations/ and re-run `dotnet ef migrations add InitialCreate`.
// - "relation 'projects' does not exist": migrations were generated but
//   not applied. Run `dotnet ef database update` or check that the
//   MigrateAsync call in Program.cs is running.
// - The Npgsql enum mapping breaks: by default Npgsql maps enums to int.
//   If you want a Postgres enum type, use HasPostgresEnum() and register
//   the enum on the data source. We do not for this exercise.
// - "Connection refused": the Postgres container is not running. `docker
//   ps` should show it; `docker logs pg-ex03` will show why if not.
//
// Stretch goal: change the connection string to use the Npgsql
// "Application Name" parameter (App=projecthub-ex03), open `psql` and
// run `SELECT pid, application_name, state FROM pg_stat_activity;` to
// observe the named connections in the pool.
// --------------------------------------------------------------------------
