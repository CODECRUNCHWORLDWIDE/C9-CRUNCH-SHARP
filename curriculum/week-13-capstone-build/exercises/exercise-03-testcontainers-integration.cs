// Exercise 3 — The Integration Baseline: WebApplicationFactory<Program> over
// Testcontainers PostgreSQL + Keycloak, Migrations Applied, the Vertical Slice
// Asserted End to End.
//
// Builds on exercises 1 and 2. This is THE milestone exercise: a single green
// [Fact] that proves the contract works on a real database with real auth. If
// this passes, the integration baseline is real.
//
// Goal: a test that mocks NOTHING that matters. Real Postgres (migrated), real
// Keycloak (real token), real gRPC surface, real JWT validation.
//
// Project layout (new test project):
//
//   tests/Workshop.IntegrationTests/
//     Workshop.IntegrationTests.csproj
//     WorkshopFixture.cs            <-- PART 1 (the containers, IAsyncLifetime)
//     WorkshopAppFactory.cs         <-- PART 2 (WebApplicationFactory<Program>)
//     SliceHarness.cs               <-- PART 3 (migrate + token + gRPC client)
//     VerticalSliceTests.cs         <-- PART 4 (the assertion)
//     Realms/workshop-realm.json    <-- imported by Keycloak (challenge 1)
//
// Packages: xunit, xunit.runner.visualstudio, Microsoft.AspNetCore.Mvc.Testing,
//   Testcontainers.PostgreSql, Testcontainers.Keycloak, Grpc.Net.Client,
//   Microsoft.EntityFrameworkCore.Design.  ProjectReference to Workshop.Api +
//   Workshop.Contract.  <InternalsVisibleTo> or `public partial class Program`.
//
// Run it:
//   docker info                       # Testcontainers needs a reachable socket
//   dotnet test tests/Workshop.IntegrationTests
//
// Acceptance criteria:
//   1. The test starts a postgres:16-alpine and a Keycloak container, applies
//      migrations against the ephemeral DB, and runs green.
//   2. The factory overrides ONLY the connection string and the OIDC authority.
//      It does NOT use UseInMemoryDatabase and does NOT stub authentication.
//   3. CreateLesson is called over a real gRPC channel with a real bearer token
//      minted from Keycloak; the row exists in Postgres afterward.
//   4. The learner's submission appears in the instructor's pending queue.
//   5. The containers are disposed when the collection finishes (no leaks;
//      verify with `docker ps` after the run — nothing lingers).

#nullable enable
using DotNet.Testcontainers.Builders;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;
using Workshop.Api;
using Workshop.Contract;

namespace Workshop.IntegrationTests;

// ============================================================================
// PART 1 — WorkshopFixture: the containers, started once per collection.
// ============================================================================

public sealed class WorkshopFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("workshop")
        .WithUsername("workshop")
        .WithPassword("workshop")
        .Build();

    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder()
        .WithImage("quay.io/keycloak/keycloak:25.0")
        .WithResourceMapping(new FileInfo("Realms/workshop-realm.json"),
                             "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .Build();

    public async ValueTask InitializeAsync()
        => await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Keycloak.DisposeAsync();
    }

    public string Issuer => $"{Keycloak.GetBaseAddress()}realms/workshop";
}

[CollectionDefinition(nameof(WorkshopCollection))]
public sealed class WorkshopCollection : ICollectionFixture<WorkshopFixture>;

// ============================================================================
// PART 2 — WorkshopAppFactory: the real app, in-memory, pointed at the
// containers. Overrides ONLY the addresses.
// ============================================================================

public sealed class WorkshopAppFactory(string connectionString, string issuer)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Workshop"] = connectionString,
                ["Oidc:Authority"] = issuer,
                ["Oidc:Audience"] = "workshop-api",
            }));
    }
}

// ============================================================================
// PART 3 — SliceHarness: applies migrations, mints tokens, builds a gRPC client
// over the in-memory server.
//
// TODO(you): implement TokenForAsync to POST to the Keycloak token endpoint
// (challenge 1 has the full request). For now a stub helper is provided.
// ============================================================================

public sealed class SliceHarness(WorkshopFixture fixture) : IAsyncDisposable
{
    private WorkshopAppFactory? _factory;

    public async Task<SliceHarness> BuildAsync()
    {
        _factory = new WorkshopAppFactory(fixture.Postgres.GetConnectionString(), fixture.Issuer);

        // Apply the REAL migrations against the ephemeral database. Not
        // EnsureCreated — we want the migrations exercised, not bypassed.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkshopDbContext>();
        await db.Database.MigrateAsync();
        return this;
    }

    // A gRPC client that talks to the in-memory TestServer, carrying the token.
    public Workshop.Contract.Workshop.WorkshopClient GrpcClient(string bearer)
    {
        var handler = _factory!.Server.CreateHandler();   // routes to the TestServer
        var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = handler,
            Credentials = ChannelCredentials.Insecure,    // TestServer is plaintext in-memory
        });
        var headers = new CallInvokerInterceptor(bearer);
        return new Workshop.Contract.Workshop.WorkshopClient(channel.Intercept(headers.Attach));
    }

    public async Task<string> TokenForAsync(string subject, string role)
    {
        // TODO(you): POST grant_type=password to
        // {fixture.Issuer}/protocol/openid-connect/token with the workshop-api
        // client and the seeded {subject} user, return access_token.
        // Challenge 1 walks the full HttpClient call.
        throw new NotImplementedException("Implement against the Keycloak token endpoint (challenge 1).");
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
    }

    private sealed class CallInvokerInterceptor(string bearer)
    {
        public Metadata Attach(Metadata metadata)
        {
            metadata.Add("Authorization", $"Bearer {bearer}");
            return metadata;
        }
    }
}

// ============================================================================
// PART 4 — VerticalSliceTests: the milestone assertion.
// ============================================================================

[Collection(nameof(WorkshopCollection))]
public sealed class VerticalSliceTests(WorkshopFixture fixture)
{
    [Fact]
    public async Task Create_enroll_submit_appears_in_pending_queue()
    {
        await using var harness = await new SliceHarness(fixture).BuildAsync();

        var instructor = await harness.TokenForAsync("instructor-1", role: "instructor");
        var learner    = await harness.TokenForAsync("learner-1",    role: "learner");

        var admin = harness.GrpcClient(instructor);
        var lesson = await admin.CreateLessonAsync(
            new CreateLessonRequest { Title = "Records 101", Body = "Value semantics." });
        Assert.False(string.IsNullOrEmpty(lesson.Id));

        var learnerClient = harness.GrpcClient(learner);
        await learnerClient.EnrollAsync(new EnrollRequest { LessonId = lesson.Id });
        var submission = await learnerClient.SubmitAsync(
            new SubmitRequest { LessonId = lesson.Id, Content = "public sealed record Point(int X, int Y);" });
        Assert.Equal(SubmissionStatus.Pending, submission.Status);

        var pending = await admin.ListPendingSubmissionsAsync(
            new ListPendingSubmissionsRequest { PageSize = 50 });
        Assert.Contains(pending.Submissions, s => s.Id == submission.Id);
    }

    [Fact]
    public async Task Submit_with_unknown_lesson_is_NotFound()
    {
        await using var harness = await new SliceHarness(fixture).BuildAsync();
        var learner = await harness.TokenForAsync("learner-2", role: "learner");
        var client = harness.GrpcClient(learner);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            client.SubmitAsync(new SubmitRequest
            {
                LessonId = Guid.NewGuid().ToString(),
                Content = "orphan",
            }).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }
}
