// Exercise 04 — Integration tests with WebApplicationFactory<Program> and
// Testcontainers for .NET. Boot the exercise-03 host inside an xUnit test
// process, against an ephemeral PostgreSQL container started fresh per
// test class, and assert on real HTTP responses.
//
// Estimated time: 90 minutes. The solution is in SOLUTIONS.md.
//
// Setup. Create a new test project alongside the host:
//   dotnet new xunit -n ProjectHub.Tests --framework net8.0
//   cd ProjectHub.Tests
//   dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.*
//   dotnet add package Testcontainers.PostgreSql --version 3.6.*
//   dotnet add package FluentAssertions --version 6.12.*
//   dotnet add reference ../ProjectHub.Exercise03/ProjectHub.Exercise03.csproj
//
// IMPORTANT: the host project's Program.cs must end with a public class
// or partial class so the test project can reference it as the type
// parameter to WebApplicationFactory<Program>. The simplest way:
//   public partial class Program { }
// at the bottom of Program.cs. Top-level statements then have a partial
// Program class the tests can target.
//
// References:
//   https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
//   https://dotnet.testcontainers.org/
//   https://xunit.net/

#nullable enable

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace ProjectHub.Tests;

// --------------------------------------------------------------------------
// TASK 1. The custom WebApplicationFactory. It owns a PostgreSQL
// container, exposes the connection string via in-memory configuration
// to the host, and implements IAsyncLifetime so xUnit's per-class fixture
// machinery starts and stops the container.
// --------------------------------------------------------------------------

public class ProjectHubFactory
    : WebApplicationFactory<ProjectHub.Exercise03.Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("projecthub_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    // Expose the connection string for tests that want direct DB access
    // (e.g. for seeding without going through the API).
    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ProjectHub"] = _postgres.GetConnectionString()
            });
        });

        // ConfigureTestServices is the hook for replacing services with
        // test doubles. We do not need any swaps for this exercise; if we
        // had an external SMTP client, we would swap it here.
        builder.ConfigureTestServices(services =>
        {
            // Intentionally empty for this exercise.
        });
    }
}

// --------------------------------------------------------------------------
// TASK 2. The first test class — exercise the REST CRUD surface.
//
// xUnit's IClassFixture<ProjectHubFactory> ensures one factory (and one
// Postgres container) per test class. Tests within the class share the
// container; we keep them order-independent by using unique OrgIds.
// --------------------------------------------------------------------------

public class ProjectsRestTests : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;
    private readonly HttpClient _client;

    public ProjectsRestTests(ProjectHubFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_project_returns_201()
    {
        var orgId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync(
            "/api/projects",
            new { OrgId = orgId, Name = "test-create" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<ProjectDto>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("test-create");
        body.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task List_returns_only_org_scoped_projects()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        await _client.PostAsJsonAsync("/api/projects",
            new { OrgId = orgA, Name = "orgA-1" });
        await _client.PostAsJsonAsync("/api/projects",
            new { OrgId = orgA, Name = "orgA-2" });
        await _client.PostAsJsonAsync("/api/projects",
            new { OrgId = orgB, Name = "orgB-1" });

        var listA = await _client.GetFromJsonAsync<List<ProjectDto>>(
            $"/api/projects?orgId={orgA}");
        var listB = await _client.GetFromJsonAsync<List<ProjectDto>>(
            $"/api/projects?orgId={orgB}");

        listA.Should().HaveCount(2);
        listA.Should().OnlyContain(p => p.OrganizationId == orgA);

        listB.Should().HaveCount(1);
        listB!.Single().Name.Should().Be("orgB-1");
    }

    [Fact]
    public async Task Create_task_under_project_returns_201()
    {
        var orgId = Guid.NewGuid();
        var projectResponse = await _client.PostAsJsonAsync(
            "/api/projects",
            new { OrgId = orgId, Name = "with-tasks" });
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectDto>();
        project.Should().NotBeNull();

        var taskResponse = await _client.PostAsJsonAsync(
            $"/api/projects/{project!.Id}/tasks",
            new { Title = "task-1" });

        taskResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Stats_endpoint_counts_org_scoped_entities()
    {
        var orgId = Guid.NewGuid();
        var projectResponse = await _client.PostAsJsonAsync(
            "/api/projects",
            new { OrgId = orgId, Name = "stats-target" });
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectDto>();

        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync(
                $"/api/projects/{project!.Id}/tasks",
                new { Title = $"task-{i}" });
        }

        var stats = await _client.GetFromJsonAsync<StatsDto>(
            $"/api/stats?orgId={orgId}");

        stats.Should().NotBeNull();
        stats!.ProjectCount.Should().Be(1);
        stats.TaskCount.Should().Be(3);
        stats.OpenTaskCount.Should().Be(3);
    }
}

// --------------------------------------------------------------------------
// TASK 3. A direct-DB test that bypasses the API. Useful for setting up
// state more efficiently than CRUD calls, and for asserting on side
// effects that the API does not surface (e.g. timestamps, audit columns).
// --------------------------------------------------------------------------

public class DirectDatabaseTests : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;

    public DirectDatabaseTests(ProjectHubFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Connection_string_is_propagated_to_the_host()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ProjectHub.Exercise03.ProjectHubDbContext>();

        // The CanConnectAsync() call verifies the host is wired to the
        // ephemeral Testcontainers instance (and not, say, an accidental
        // local install).
        var canConnect = await db.Database.CanConnectAsync();
        canConnect.Should().BeTrue();
    }

    [Fact]
    public async Task Migration_was_applied_on_startup()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ProjectHub.Exercise03.ProjectHubDbContext>();

        // If the migration ran, the projects table exists and a count
        // query returns zero (or some other number, but not an exception).
        var count = await db.Projects.CountAsync();
        count.Should().BeGreaterThanOrEqualTo(0);
    }
}

// --------------------------------------------------------------------------
// TASK 4. DTOs used for deserialization. These mirror the JSON shape of
// the responses produced by the host. The host project does not expose
// them as record types directly; the test project carries its own.
// --------------------------------------------------------------------------

public record ProjectDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    DateTime CreatedAt);

public record StatsDto(int ProjectCount, int TaskCount, int OpenTaskCount);

// --------------------------------------------------------------------------
// VERIFICATION
//
//   # 1. Make sure Docker is running. Testcontainers needs the daemon.
//   docker ps
//
//   # 2. Run the tests.
//   dotnet test
//
//   # Expected output:
//   #   Test Run Successful.
//   #   Total tests: 6
//   #     Passed: 6
//   #   Total time: ~35 seconds
//
//   # 3. To watch the container come and go:
//   docker ps --filter "ancestor=postgres:16"
//   # During the test run, a container will appear briefly.
//
//   # 4. To debug a failing test, redirect Serilog output to xUnit:
//   # See SOLUTIONS.md for the Serilog.Sinks.XUnit example.
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// Common stumbles:
//
// - "Cannot find type Program": the host project's Program.cs needs to
//   declare a public partial class Program {} after the top-level
//   statements so the type is reachable from the test project.
// - "Docker is not available": the Testcontainers library requires a
//   running Docker daemon. On macOS, ensure Docker Desktop is started.
// - Test hangs: an ephemeral container failed to start. Run `docker
//   logs <container-id>` while the test is hung to see the issue.
// - Tests pass locally but fail in CI: check that the CI runner has
//   Docker available. GitHub Actions ships ubuntu-latest with Docker
//   pre-installed; some other runners do not.
//
// Stretch goal: add a per-test reset using the `Respawn` library at
// https://github.com/jbogard/Respawn. This truncates all tables between
// tests so they are fully isolated. The mini-project ships this.
// --------------------------------------------------------------------------

// --------------------------------------------------------------------------
// THE BIGGER PICTURE
//
// The four exercises in this week each isolate one piece:
//   - exercise-01: composition (REST + gRPC + SignalR in one host)
//   - exercise-02: observability (Serilog + OpenTelemetry)
//   - exercise-03: persistence (EF Core + PostgreSQL + migrations)
//   - exercise-04: integration tests (WebApplicationFactory + Testcontainers)
//
// The mini-project (mini-project/README.md) puts them all together —
// the ProjectHub service that motivated the week — and ships a Dockerfile,
// a docker-compose.yml with Jaeger, a CI workflow, and a full integration
// test suite that exercises every endpoint on every protocol.
//
// Treat these exercises as the "load-bearing pattern lookup table" for
// the mini-project. When the mini-project asks you to wire OpenTelemetry,
// you have the registration code from exercise-02 to lift. When it asks
// you to integration-test the gRPC service, you have the
// GrpcChannel.ForAddress(_factory.Server.BaseAddress) pattern from this
// exercise's solutions to apply.
// --------------------------------------------------------------------------
