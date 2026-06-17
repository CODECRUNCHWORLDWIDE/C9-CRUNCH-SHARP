// ProjectHub / src/ProjectHub / ProjectHubDbContext.cs
//
// The EF Core persistence boundary, given in full — you proved this in
// Exercise 3, so the mini-project hands it to you complete. Two aggregates,
// Guid keys, an OrganizationId for tenancy, snake_case Postgres tables, and
// an index on OrganizationId so the tenant-scoped queries the REST and gRPC
// surfaces issue stay sargable.
//
// One DbContext is shared by every protocol surface. Whether it arrives via
// AddDbContextPool (REST/gRPC) or AddDbContextFactory (the singleton
// broadcaster) it is the same type and the same model; the difference is
// lifetime, configured once in ServiceConfiguration.AddProjectHubPersistence.
//
// Citations:
//   DbContext config:  https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
//   Modeling:          https://learn.microsoft.com/en-us/ef/core/modeling/
//   Npgsql provider:   https://www.npgsql.org/efcore/

#nullable enable

using Microsoft.EntityFrameworkCore;

namespace ProjectHub;

public sealed class Project
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<ProjectTask> Tasks { get; set; } = new();
}

public sealed class ProjectTask
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = string.Empty;
    public ProjectTaskStatus Status { get; set; } = ProjectTaskStatus.Open;
    public DateTime CreatedAt { get; set; }
}

// Named ProjectTaskStatus, not TaskStatus, to avoid colliding with
// System.Threading.Tasks.TaskStatus — a real-world papercut when a using
// directive pulls the framework type into scope.
public enum ProjectTaskStatus
{
    Open = 0,
    InProgress = 1,
    Done = 2,
    Cancelled = 3
}

public sealed class ProjectHubDbContext : DbContext
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
        // Every tenant-scoped read filters on OrganizationId; index it.
        project.HasIndex(p => p.OrganizationId);

        var task = modelBuilder.Entity<ProjectTask>();
        task.ToTable("tasks");
        task.HasKey(t => t.Id);
        task.Property(t => t.Title).HasMaxLength(500).IsRequired();
        // Store the enum as a string column so a DBA reading the table sees
        // "InProgress", not "1". Costs a few bytes; worth it for legibility.
        task.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        task.HasIndex(t => t.ProjectId);
        task.HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
