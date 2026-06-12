// Workshop.Api / Program.cs — the backend host for the Polyglot Workshop.
//
// This starter wires the full integration baseline: Minimal API (REST mirror)
// + gRPC + gRPC-Web (for Blazor) + EF Core/Npgsql + JWT bearer (Keycloak) +
// Serilog structured logging + OpenTelemetry traces/metrics. The TODOs mark the
// spots you fill from your exercises (the proto, the service body, migrations).
//
// Targets net9.0. Packages:
//   Grpc.AspNetCore, Grpc.AspNetCore.Web,
//   Microsoft.EntityFrameworkCore, Npgsql.EntityFrameworkCore.PostgreSQL, Dapper,
//   Microsoft.AspNetCore.Authentication.JwtBearer,
//   Serilog.AspNetCore, Serilog.Sinks.Console,
//   OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore,
//   OpenTelemetry.Instrumentation.Http, OpenTelemetry.Instrumentation.GrpcNetClient,
//   OpenTelemetry.Instrumentation.EntityFrameworkCore,
//   OpenTelemetry.Instrumentation.Runtime, OpenTelemetry.Exporter.OpenTelemetryProtocol
//
// Citations:
//   Minimal APIs:   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview
//   gRPC:           https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore
//   gRPC-Web:       https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb
//   JWT bearer:     https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication
//   Serilog:        https://github.com/serilog/serilog-aspnetcore
//   OpenTelemetry:  https://opentelemetry.io/docs/languages/net/getting-started/

#nullable enable
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Workshop.Api;
using Workshop.Api.Grpc;
using Workshop.Api.Mapping;
using Workshop.Api.Observability;
using Workshop.Contract;
using Workshop.Domain;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Serilog: structured logging from the first line, so even startup is queryable.
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "workshop-api")
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

// ---------------------------------------------------------------------------
// Persistence: EF Core over PostgreSQL.
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<WorkshopDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Workshop")));

// A raw Npgsql connection factory for the Dapper analytics path (see
// Analytics/ProgressQueries.cs).
builder.Services.AddScoped(sp =>
    new Npgsql.NpgsqlConnection(builder.Configuration.GetConnectionString("Workshop")));

// ---------------------------------------------------------------------------
// gRPC (+ gRPC-Web for the Blazor admin) and CORS that exposes gRPC headers.
// ---------------------------------------------------------------------------
builder.Services.AddGrpc(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddCors(o => o.AddPolicy("grpc-web", p => p
    .WithOrigins(builder.Configuration["AdminOrigin"] ?? "https://localhost:7200")
    .AllowAnyMethod()
    .AllowAnyHeader()
    // The easy-to-forget line: without exposing these, the browser strips the
    // gRPC status and every gRPC-Web call fails with "no status".
    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

// ---------------------------------------------------------------------------
// Auth: JWT bearer validated against the Keycloak realm.
// ---------------------------------------------------------------------------
builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", o =>
{
    o.Authority = builder.Configuration["Oidc:Authority"];
    o.Audience = builder.Configuration["Oidc:Audience"];
    o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    o.TokenValidationParameters.NameClaimType = "preferred_username";
});
builder.Services.AddAuthorization();

builder.Services.AddScoped<WorkshopService>();

// ---------------------------------------------------------------------------
// OpenTelemetry: traces and metrics to the OTLP collector.
// ---------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("workshop-api"))
    .WithTracing(t => t
        .AddSource(WorkshopTelemetry.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddMeter(WorkshopTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline. Order matters: CORS and gRPC-Web before the gRPC endpoint; auth
// before authorization; request logging early.
// ---------------------------------------------------------------------------
app.UseSerilogRequestLogging();
app.UseCors("grpc-web");
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.UseAuthentication();
app.UseAuthorization();

// Apply migrations on startup outside Testing (tests apply them in the harness).
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<WorkshopDbContext>().Database.MigrateAsync();
}

// The gRPC surface — native gRPC for MAUI, gRPC-Web for Blazor.
app.MapGrpcService<WorkshopService>().EnableGrpcWeb().RequireCors("grpc-web");

// The REST mirror of CreateLesson. Same domain factory, same DbContext.
app.MapPost("/api/lessons", async (
    CreateLessonDto dto, WorkshopDbContext db, HttpContext http, CancellationToken ct) =>
{
    var instructorId = http.User.FindFirst("sub")?.Value;
    if (instructorId is null) return Results.Unauthorized();
    var tenantId = http.User.FindFirst("tenant")?.Value ?? "default";

    var lesson = Lesson.Create(tenantId, instructorId, dto.Title, dto.Body);
    db.Lessons.Add(lesson);
    await db.SaveChangesAsync(ct);
    WorkshopTelemetry.LessonsCreated.Add(1);
    return Results.Created($"/api/lessons/{lesson.Id}", lesson.ToProto());
})
.RequireAuthorization();

// A health endpoint the CI smoke check and the runbook (Week 15) use.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public sealed record CreateLessonDto(string Title, string Body);

// Exposes the implicit Program class so WebApplicationFactory<Program> can host
// it from the integration test project.
public partial class Program { }
