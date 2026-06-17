// ProjectHub / src/ProjectHub / ServiceConfiguration.cs
//
// The single home for every cross-cutting registration. This is the
// "configure once, register everywhere" discipline from Lecture 1 made
// concrete. Program.cs should read as four calls into this class plus the
// route mapping — nothing else. The AddProjectHubAuth method is given in
// full (it is identical to the pattern you proved in Exercise 1). The
// other three have stubbed bodies with TODOs and citations: fill them in
// using Lectures 2 and 3 and your exercise solutions.
//
// Citations:
//   Host config:   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host
//   JWT bearer:    https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn
//   SignalR auth:  https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz
//   Serilog:       https://github.com/serilog/serilog-aspnetcore
//   OpenTelemetry: https://github.com/open-telemetry/opentelemetry-dotnet
//   Npgsql EFCore: https://www.npgsql.org/efcore/

#nullable enable

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ProjectHub;

public static class ServiceConfiguration
{
    /// <summary>
    /// The ActivitySource every application-level span hangs off. Declared
    /// here so the telemetry registration and the endpoint code share one
    /// name ("ProjectHub"); AddSource must match this string exactly or the
    /// hand-written spans never reach the exporter.
    /// </summary>
    public const string ActivitySourceName = "ProjectHub";

    // ----------------------------------------------------------------------
    // AUTH — given in full. One scheme, applied to REST, gRPC, and SignalR.
    // The OnMessageReceived hook is the Week 11 trick: lift the query-string
    // token into context.Token, but ONLY for /hubs/* paths (a query-string
    // credential on any other surface leaks into access logs — see the
    // Challenge 2 threat-model discussion).
    // ----------------------------------------------------------------------
    public static IServiceCollection AddProjectHubAuth(
        this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection("Jwt");
        var signingKey = jwt.GetValue<string>("SigningKey")
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is required. Set it via user-secrets or an environment variable, never appsettings.json.");
        var issuer = jwt.GetValue<string>("Issuer") ?? "projecthub";
        var audience = jwt.GetValue<string>("Audience") ?? "projecthub-clients";

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

                // SignalR's WebSocket upgrade cannot carry an Authorization
                // header, so the client puts the token in ?access_token=.
                // Gate the lift on /hubs/* so REST/gRPC keep using the header.
                // https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication
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
            // Every protected surface — REST endpoints, the gRPC service,
            // the hub — applies this one policy.
            options.AddPolicy("RequireOrg", policy => policy.RequireClaim("org_id"));
        });

        return services;
    }

    // ----------------------------------------------------------------------
    // LOGGING — stub. Wire Serilog as the global logger.
    // ----------------------------------------------------------------------
    public static WebApplicationBuilder AddProjectHubLogging(
        this WebApplicationBuilder builder)
    {
        // TODO: configure Serilog and call builder.Host.UseSerilog(...).
        //   - Use Serilog.Formatting.Compact.RenderedCompactJsonFormatter
        //     for the console sink so a log aggregator can index it.
        //   - Add a rolling file sink (Serilog.Sinks.File) under ./logs.
        //   - Enrich.FromLogContext() so the trace id (set by the OTel
        //     log enricher) is attached to every line.
        //   - Read the minimum level from configuration ("Serilog" section)
        //     so it is overridable per environment.
        //
        // The whole point: every log line carries the trace id, the org id
        // (when the handler logs it as a structured property), the machine
        // name, and the environment, in a format Seq/Loki/Datadog index
        // without a custom parser. Use message templates, never $"..."
        // interpolation, so {ProjectId} stays a queryable field.
        //
        // https://github.com/serilog/serilog-aspnetcore
        // https://github.com/serilog/serilog/wiki/Structured-Data

        throw new NotImplementedException(
            "AddProjectHubLogging: wire Serilog with the compact JSON formatter, a rolling file sink, and FromLogContext enrichment.");
    }

    // ----------------------------------------------------------------------
    // TELEMETRY — stub. Wire OpenTelemetry tracing + metrics.
    // ----------------------------------------------------------------------
    public static IServiceCollection AddProjectHubTelemetry(
        this IServiceCollection services, IConfiguration config)
    {
        var otlpEndpoint = config["Otel:OtlpEndpoint"]; // null => console exporter

        // TODO: services.AddOpenTelemetry()
        //   .ConfigureResource(r => r.AddService("ProjectHub", serviceVersion: ...))
        //   .WithTracing(t => t
        //       .AddSource(ActivitySourceName)        // our hand-written spans
        //       .AddAspNetCoreInstrumentation()
        //       .AddHttpClientInstrumentation()
        //       .AddGrpcClientInstrumentation()
        //       .AddNpgsql()                          // from Npgsql.OpenTelemetry
        //       .AddConsoleExporter() OR .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
        //   .WithMetrics(m => m
        //       .AddAspNetCoreInstrumentation()
        //       .AddHttpClientInstrumentation()
        //       ... exporter as above);
        //
        // Pick the exporter on otlpEndpoint: console in dev (visible wire
        // format), OTLP to Jaeger when the endpoint is configured. The
        // instrumentations produce HTTP/EF/gRPC spans for free; the
        // ActivitySource is what lets the endpoint code add UpdateTaskStatus
        // and BroadcastStatusChanged spans by hand.
        //
        // https://github.com/open-telemetry/opentelemetry-dotnet
        // https://opentelemetry.io/docs/languages/net/exporters/

        _ = otlpEndpoint;
        throw new NotImplementedException(
            "AddProjectHubTelemetry: register AddOpenTelemetry().WithTracing(...).WithMetrics(...) with the four instrumentations and the env-driven exporter.");
    }

    // ----------------------------------------------------------------------
    // PERSISTENCE — stub. Wire EF Core + Postgres two ways.
    // ----------------------------------------------------------------------
    public static IServiceCollection AddProjectHubPersistence(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("ProjectHub")
            ?? "Host=localhost;Port=5432;Database=projecthub;Username=postgres;Password=devpass;Application Name=ProjectHub";

        // TODO (1): AddDbContextPool<ProjectHubDbContext> for per-request
        //   scopes (REST handlers, gRPC methods). Pooling reuses context
        //   instances across requests for less allocation; the reset is
        //   automatic between checkouts.
        //
        // TODO (2): AddDbContextFactory<ProjectHubDbContext> for the
        //   singleton ProjectEventsBroadcaster, which is NOT request-scoped
        //   and therefore must NOT capture a scoped context (that is the
        //   captive-dependency bug from Exercise 3 / Lecture 1). The factory
        //   hands it a fresh context per unit of work.
        //
        // Both registrations use UseNpgsql(connectionString). Note that
        // AddDbContextPool and AddDbContextFactory can coexist; they serve
        // different consumers.
        //
        // https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
        // https://www.npgsql.org/efcore/
        // https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#using-a-dbcontext-factory

        _ = connectionString;
        throw new NotImplementedException(
            "AddProjectHubPersistence: AddDbContextPool for REST/gRPC and AddDbContextFactory for the singleton broadcaster, both UseNpgsql(connectionString).");
    }
}
