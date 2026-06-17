# Lecture 3 — Integration Tests with `WebApplicationFactory<Program>` and Testcontainers

## What we are building

Two previous lectures stood up a host that serves REST, gRPC, and SignalR with shared auth, shared persistence, structured logging, and distributed tracing. This lecture writes the safety net under that host: a test suite that boots the entire application in-process and exercises every endpoint against a real PostgreSQL container, proving that the cross-cutting wiring works end to end.

The promise we are honoring is the integration-test contract from the README: every endpoint shipped in this week's mini-project has at least one xUnit test that exercises it through `WebApplicationFactory<Program>` and a real database. The tools are `Microsoft.AspNetCore.Mvc.Testing` for the in-process host harness, `xunit` for the test framework, **Testcontainers for .NET** for the ephemeral database, and `FluentAssertions` for readable assertions. The tests run in CI in under a minute and prove the deployment is shaped the way the team thinks it is.

A note on terminology before we begin. The word "integration test" in this lecture means what Microsoft Learn means by it: a test that boots the ASP.NET Core host inside the test process and issues real HTTP / gRPC / WebSocket traffic against it. It does **not** mean "a test that talks to a real third-party service over the network." Those are sometimes called "contract tests" or "end-to-end tests"; we will not write them this week. Our tests run hermetically — the only external dependency is Docker, which Testcontainers requires for the PostgreSQL container.

## `WebApplicationFactory<TEntryPoint>` — what it does

`Microsoft.AspNetCore.Mvc.Testing` ships a single load-bearing class: `WebApplicationFactory<TEntryPoint>`. The generic parameter is the program-entry type — in modern minimal-API hosts, the implicitly-generated `Program` class. The factory does four things on construction:

1. Locates the test target's `Program` class via the type parameter.
2. Constructs a `WebApplicationBuilder` and runs the target's startup pipeline.
3. Substitutes the framework's `Kestrel` server with an in-memory `TestServer`.
4. Exposes a `CreateClient()` method that returns an `HttpClient` wired to the test server.

The substitution in step 3 is the magic. `TestServer` exposes an `HttpMessageHandler` that the returned `HttpClient` uses, so HTTP calls go directly into the ASP.NET Core pipeline without serializing over a real TCP socket. The pipeline is otherwise identical to production — same middleware, same routes, same auth — so the tests exercise real code paths, not stub behaviors.

The Microsoft Learn page is at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. Read the "Customize WebApplicationFactory" subsection; we will override that hook to inject the Testcontainers connection string.

The minimum test class looks like this:

```csharp
public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

`IClassFixture<T>` is the xUnit idiom for "create one instance of `T` per test class, share it across all test methods." That is exactly what we want for the factory — booting a host is expensive (200-500ms), so we reuse it across the class. The xUnit citation is <https://xunit.net/docs/shared-context#class-fixture>.

For this minimum test class there is no database, no auth, no Testcontainers. We add those next.

## Testcontainers for .NET — the ephemeral PostgreSQL

The package is `Testcontainers.PostgreSql`. The model is "wrap a Docker container in a C# object, start it before the tests, stop it after." Here is the canonical use:

```csharp
using Testcontainers.PostgreSql;

public class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; }

    public PostgresFixture()
    {
        Container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("projecthub_tests")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
    }

    public async Task InitializeAsync() => await Container.StartAsync();
    public async Task DisposeAsync() => await Container.DisposeAsync();

    public string ConnectionString => Container.GetConnectionString();
}
```

`IAsyncLifetime` is xUnit's pre-test / post-test hook; `InitializeAsync` runs once before the first test in the class, `DisposeAsync` runs once after the last. Inside `InitializeAsync` we call `Container.StartAsync()` — Testcontainers pulls the `postgres:16` image if it isn't cached, runs `docker run` with random port bindings, waits for the container's health-check to pass (PostgreSQL responding to `pg_isready`), and returns. `Container.GetConnectionString()` produces a connection string like `Host=127.0.0.1;Port=49483;Database=projecthub_tests;Username=test;Password=test` — note the random port; Testcontainers picks an unused one so parallel test classes do not collide.

The fixture lifetime depends on the xUnit collection model. By default, every test class with `IClassFixture<PostgresFixture>` gets its own instance, which means a container per class. If you want a container shared across multiple classes (faster, but tests must clean up after themselves), use `ICollectionFixture<T>` and an `[CollectionDefinition]` attribute. For ProjectHub we use the per-class model — the cost is one container start per class (~3 seconds) but the win is full isolation: no test can be poisoned by state another test left behind.

The Testcontainers .NET citation is the project documentation at <https://dotnet.testcontainers.org/> and the GitHub repository at <https://github.com/testcontainers/testcontainers-dotnet>.

## Combining the factory and the fixture

The two pieces compose. We subclass `WebApplicationFactory<Program>` to inject the Testcontainers connection string and clear out any services the production registration wired up that we want to stub:

```csharp
public class ProjectHubFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("projecthub_tests")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();
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
                ["ConnectionStrings:ProjectHub"] = _postgres.GetConnectionString(),
                ["Jwt:SigningKey"] =
                    "TestingKeyDoNotUseInProductionMustBeAtLeastSixtyFourCharactersLong",
                ["Jwt:Issuer"] = "projecthub-tests",
                ["Jwt:Audience"] = "projecthub-tests",
                ["OpenTelemetry:Exporter"] = "Console",
                ["Database:LogParameters"] = "true"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Swap any production-only services for test doubles.
            // Example: a stub email sender, a fake clock, etc.
        });
    }
}
```

The `ConfigureAppConfiguration` block adds an in-memory dictionary as the highest-precedence configuration source. The connection string from the running Postgres container, the JWT signing key, the issuer, the audience — all flow through `IConfiguration` exactly the way production reads them. The test code is not aware of any of these values; it just calls `factory.CreateClient()` and the host wires itself up against the ephemeral database.

The `ConfigureTestServices` block is the seam where we can replace services with test doubles. We use it sparingly — every replacement is a deviation from production behavior, and the point of an integration test is to exercise production behavior. The places we do replace: the email sender (if there were one; there isn't in ProjectHub), the clock (`IClock` from NodaTime or a custom `ITimeProvider`), and any external HTTP client (replaced with a `HttpMessageHandler` stub).

The factory is used as a test fixture:

```csharp
public class ProjectsApiTests : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;
    private readonly HttpClient _client;

    public ProjectsApiTests(ProjectHubFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> IssueTestTokenAsync(Guid orgId)
    {
        // Helper that mints a JWT with the test signing key.
        // Body in exercise-04; full implementation in mini-project starter.
        return TestTokenIssuer.IssueToken(
            "test-user", orgId, factory: _factory);
    }

    [Fact]
    public async Task Create_project_returns_201_and_persists()
    {
        var orgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var token = await IssueTestTokenAsync(orgId);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/projects",
            new { Name = "Integration test project" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ProjectDto>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("Integration test project");

        // Verify it actually landed in Postgres.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
        var persisted = await db.Projects.FindAsync(body.Id);
        persisted.Should().NotBeNull();
        persisted!.OrganizationId.Should().Be(orgId);
    }
}
```

Read this carefully. The test issues a token using a helper that signs with the test-key the factory injected; it sets the bearer header on the client; it issues a real `POST /api/projects` against the in-memory `TestServer`; the request flows through the full middleware pipeline (Serilog request logging, JWT bearer auth, authorization policy, endpoint routing, the endpoint handler); the handler runs against the real `ProjectHubDbContext` connected to the Testcontainers Postgres; the response comes back. The test asserts on the status code, the response body, and a re-fetch from the database to verify the persistence side-effect actually happened.

This is the shape of every integration test we write this week. It is long compared to a unit test; the length pays for itself by covering the entire deployment shape.

## Asserting on gRPC

gRPC tests follow the same pattern, with one difference: instead of `HttpClient`, we use a `GrpcChannel` pointed at the test server.

```csharp
public class ProjectsGrpcTests : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;

    public ProjectsGrpcTests(ProjectHubFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_returns_authorized_projects()
    {
        var orgId = Guid.NewGuid();
        var token = TestTokenIssuer.IssueToken("test-user", orgId, _factory);
        var seededId = await SeedProjectAsync(orgId, "grpc-list-target");

        var handler = _factory.Server.CreateHandler();
        var channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = handler });

        var client = new Projects.ProjectsClient(channel);
        var metadata = new Metadata
        {
            { "authorization", $"Bearer {token}" }
        };

        var response = await client.ListAsync(
            new ListProjectsRequest { Page = 1, PageSize = 20 },
            metadata);

        response.Total.Should().BeGreaterThanOrEqualTo(1);
        response.Items.Should().Contain(p => p.Id == seededId.ToString());
    }

    private async Task<Guid> SeedProjectAsync(Guid orgId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProjectHubDbContext>();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }
}
```

The `_factory.Server.CreateHandler()` returns the `HttpMessageHandler` that talks to the in-memory test server; `GrpcChannel.ForAddress` builds a gRPC channel that uses that handler. The result is a gRPC client that issues real gRPC calls against the real gRPC service, through the real auth middleware, with no network involvement. The `Metadata` object is gRPC's headers-equivalent; the `authorization` key carries the bearer token the same way the REST surface receives it.

The gRPC client is generated from the `.proto` file at build time; the test project references the same `.proto` (or a shared `.csproj` that produces the contract types) so the test code uses the same `Projects.ProjectsClient` the production client would use. The semantic-conventions for the metadata header are in the gRPC HTTP/2 spec; in practice it is `authorization: Bearer <token>`, lowercase.

## Asserting on SignalR

SignalR tests are the most involved of the three protocols. We need a real `HubConnection`, pointed at the test server, with the JWT in the `accessTokenFactory`:

```csharp
public class ProjectEventsHubTests : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;

    public ProjectEventsHubTests(ProjectHubFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Connection_with_valid_token_receives_project_created_event()
    {
        var orgId = Guid.NewGuid();
        var token = TestTokenIssuer.IssueToken("test-user", orgId, _factory);
        var hubUrl = new Uri(_factory.Server.BaseAddress, "/hubs/events");
        var handler = _factory.Server.CreateHandler();

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => handler;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var received = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("ProjectCreated", payload =>
        {
            received.TrySetResult(payload);
        });

        await connection.StartAsync();

        // Trigger an event by issuing a REST POST.
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        var post = await http.PostAsJsonAsync(
            "/api/projects",
            new { Name = "Event trigger" });
        post.EnsureSuccessStatusCode();

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        payload.GetProperty("Name").GetString().Should().Be("Event trigger");

        await connection.DisposeAsync();
    }
}
```

Five details deserve attention.

**`HttpMessageHandlerFactory = _ => handler`**. The SignalR client builder normally creates its own `HttpClient` to call the negotiate endpoint. In the test scenario, we need it to route through the test server's handler instead. The factory hook is exactly that seam.

**`Transports = HttpTransportType.LongPolling`**. The in-memory `TestServer` does not support WebSocket upgrades — its model is request-response over the message-handler pipeline. Long polling works because every poll is a discrete HTTP request that the test server can route. The production client would prefer WebSockets and SignalR's negotiation would pick them; we override the preference to keep the test in-process.

**`AccessTokenProvider`** is SignalR's hook for the JWT. The SDK calls it on the negotiate request, appends `?access_token=<token>`, and our `OnMessageReceived` event handler (from Lecture 1) reads it back. The test exercises that whole chain.

**The `TaskCompletionSource`**. The test needs to wait for the asynchronous SignalR event without busy-looping. `connection.On(...)` registers a handler that fires when the event arrives; we capture the payload into a `TaskCompletionSource`; the test awaits the resulting task with a timeout. If the event never arrives within five seconds, the test fails with a timeout exception, which makes the failure mode debuggable.

**`await connection.DisposeAsync()`**. Always tear down the connection at the end of the test. Leaks here cascade — Testcontainers keeps the database alive but a leaked connection holds a hub instance which holds a scope which holds a `DbContext` which holds an Npgsql connection. Disposing the connection releases the chain.

The result is a test that exercises the full cross-protocol path: a REST POST triggers a database write, the REST handler calls the broadcaster, the broadcaster sends a SignalR event, the SignalR client receives it, the test asserts on the payload. The same `TraceId` will appear in the logs for the REST handler, the EF Core INSERT, and the SignalR broadcast — Lecture 2's contract holds inside the test process exactly the way it does in production.

## Test-time JWT issuance

`TestTokenIssuer.IssueToken` is the helper that produces a signed JWT with the test signing key. The implementation is roughly thirty lines:

```csharp
public static class TestTokenIssuer
{
    public static string IssueToken(
        string subject,
        Guid orgId,
        ProjectHubFactory factory,
        TimeSpan? lifetime = null)
    {
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var signingKey = config.GetValue<string>("Jwt:SigningKey")!;
        var issuer = config.GetValue<string>("Jwt:Issuer")!;
        var audience = config.GetValue<string>("Jwt:Audience")!;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim("org_id", orgId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10)),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

The helper reads the same `Jwt:SigningKey` the host validates against — the in-memory configuration we injected via `ConfigureAppConfiguration` ensures both sides agree. The `org_id` claim is what the `RequireOrg` policy enforces. The lifetime is short (10 minutes by default) to mimic production behavior; tests that need to exercise expiration override it.

Citation: `System.IdentityModel.Tokens.Jwt` at <https://learn.microsoft.com/en-us/dotnet/api/system.identitymodel.tokens.jwt>.

## Capturing logs from inside a test

When a test fails, the operator wants the logs the host emitted during the test run. xUnit provides `ITestOutputHelper` for per-test output; Serilog can be redirected to it via `Serilog.Sinks.XUnit`:

```csharp
public class LoggingTests : IClassFixture<ProjectHubFactory>
{
    private readonly ProjectHubFactory _factory;
    private readonly ITestOutputHelper _output;

    public LoggingTests(ProjectHubFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        // Redirect Serilog to the xUnit output for this test.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(_output, outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}")
            .CreateLogger();
    }
}
```

This is useful for diagnosing flaky tests — when CI reports a failure, the test output includes every log line the host produced, with trace IDs that you can correlate against any test-time telemetry.

In practice we redirect Serilog to xUnit output only in tests that explicitly need it; the default factory configuration keeps Serilog writing to the console (which CI captures anyway). The Serilog.Sinks.XUnit package is at <https://github.com/trbenning/serilog-sinks-xunit>.

## Cleanup between tests

The Testcontainers container is shared across the test class. By default, every test in the class sees state left behind by earlier tests. That is fine if the tests are written defensively (every assertion filters by an `OrgId` the test invented), but it is fragile.

The cleaner pattern is a per-test transaction that rolls back at the end. xUnit's `IAsyncLifetime` works at the test level too — implement it on the test class and use `InitializeAsync` / `DisposeAsync` to seed and tear down. Or, more aggressively, a `Respawn` library that truncates every table between tests:

```csharp
public class ProjectsApiTests : IClassFixture<ProjectHubFactory>, IAsyncLifetime
{
    private readonly ProjectHubFactory _factory;
    private Respawner _respawner = null!;

    public ProjectsApiTests(ProjectHubFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        var connectionString = _factory.Services
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("ProjectHub")!;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public" }
        });
    }

    public async Task DisposeAsync()
    {
        var connectionString = _factory.Services
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("ProjectHub")!;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }
}
```

`Respawn` at <https://github.com/jbogard/Respawn> is a small library that truncates tables in dependency order. It runs in milliseconds and leaves the schema intact. We use it in tests that mutate state aggressively; we skip it in tests that read-only.

## What CI looks like

The full test run on CI:

```bash
dotnet test \
    --collect:"XPlat Code Coverage" \
    --results-directory ./TestResults \
    --logger "trx" \
    --logger "console;verbosity=normal"
```

The output shows the test count, the pass / fail summary, and the per-test diagnostic if any failed. Coverage flows to Coverlet (<https://github.com/coverlet-coverage/coverlet>), which produces an OpenCover XML the CI uploads to Codecov / Coveralls. The mini-project ships a GitHub Actions workflow that does exactly this.

The full test run on the mini-project, in CI, takes roughly 35 seconds: 3 seconds for the Postgres container to start, 5 seconds for the host to boot, ~25 seconds for the test methods to execute. Local runs are similar. Faster than a real network round-trip; faster than any deploy. The cost is the Docker daemon dependency; the win is the deployment shape is proved on every commit.

## What we built

By the end of Lecture 3, the test suite:

- Boots the entire `ProjectHub` host inside the test process via `WebApplicationFactory<Program>`.
- Spins an ephemeral PostgreSQL 16 container per test class via Testcontainers; tears it down after.
- Injects test-time configuration via `ConfigureAppConfiguration`, overriding the connection string, JWT signing key, and exporter selection.
- Mints test JWTs with the same signing key the host validates against.
- Asserts on REST endpoints via `HttpClient`, on gRPC endpoints via `GrpcChannel.ForAddress(_factory.Server.BaseAddress)`, and on SignalR events via `HubConnectionBuilder().WithUrl(...).WithAccessTokenProvider(...)`.
- Cleans up state between tests via Respawn.
- Captures Serilog output to xUnit's `ITestOutputHelper` for failed-test diagnosis.

The contract: every endpoint shipped in the mini-project has at least one integration test that exercises it through the harness. The PR cannot land without the tests; the tests cannot pass without Docker running; Docker running is the price of confidence that the deployment shape is what the team thinks it is.

The slogan: integration tests are a deployable invariant. They cost a Docker daemon and a CI minute; they pay for themselves the first time the auth scheme silently breaks on a SignalR upgrade in production. Spend the time. Wire the harness once.
