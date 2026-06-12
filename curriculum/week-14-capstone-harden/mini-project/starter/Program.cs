// Polyglot Workshop — Program.cs (starter, week-14 hardened composition)
//
// This wires together the four hardening concerns in the correct order. It assumes
// the week-13 baseline (EF Core, gRPC, SignalR, Identity/OIDC, outbox worker) is
// already present in the referenced extension methods; this file shows the
// HARDENING additions and where they slot into the pipeline.
//
// The 'public partial class Program' at the bottom makes the entry point
// referenceable from Workshop.IntegrationTests via WebApplicationFactory<Program>.

#nullable enable
using Workshop.Api.Authorization;
using Workshop.Api.Endpoints;
using Workshop.Api.Mapping;
using Workshop.Api.Security;
using Workshop.Api.Telemetry;
using Workshop.Application.Behaviors;

var builder = WebApplication.CreateBuilder(args);

// --- Week-13 baseline (persistence, gRPC, SignalR, outbox worker) ------------
builder.AddWorkshopPersistence();      // EF Core + Npgsql + Dapper + global tenant filter
builder.Services.AddWorkshopGrpc();    // the gRPC service mirroring the domain
builder.Services.AddWorkshopSignalR(); // the presence hub

// --- Week-14 hardening: observability FIRST (so you can see everything else) --
builder.AddWorkshopObservability();    // OTLP traces + metrics + Serilog->OTLP logs

// --- Week-14 hardening: auth ---------------------------------------------------
builder.Services.AddWorkshopAuthentication(builder.Configuration);  // hardened JWT bearer
builder.Services.AddWorkshopAuthorization();                        // policies + deny-by-default

// --- Week-14 hardening: the MediatR pipeline ----------------------------------
builder.Services.AddWorkshopMediatr(typeof(Workshop.Application.AssemblyMarker).Assembly);

// --- Week-14 hardening: AutoMapper (projection only) --------------------------
builder.Services.AddAutoMapper(typeof(WorkshopMappingProfile).Assembly);

// --- Week-14 hardening: rate limiting (API4) ----------------------------------
builder.Services.AddWorkshopRateLimiting();

builder.Services.AddProblemDetails();   // RFC 9457 for validation + auth failures

var app = builder.Build();

// --- Middleware order matters -------------------------------------------------
app.UseWorkshopSecurityHeaders();       // API8
app.UseHttpsRedirection();
app.UseExceptionHandler();              // maps ValidationException/NotFound/Forbidden -> ProblemDetails
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// --- Endpoints ----------------------------------------------------------------
app.MapSubmissionEndpoints();           // resource-based authz inside
app.MapLessonEndpoints();               // InstructorOnly via the group
app.MapAnalyticsEndpoints()             // Dapper analytics; rate-limited
   .RequireRateLimiting("per-user");
app.MapWorkshopGrpcServices();          // [Authorize] on the services
app.MapPresenceHub("/hubs/presence");   // [Authorize] on the hub

// Dev-only token endpoint — absent outside Development (API8 misconfiguration guard).
if (app.Environment.IsDevelopment())
{
    app.MapDevTokenEndpoint();
}

app.Run();

// Required so WebApplicationFactory<Program> can reference the entry point.
public partial class Program;
