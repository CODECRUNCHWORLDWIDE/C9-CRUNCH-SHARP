// Crunch.Chat / src/Server / Program.cs
//
// Minimal SignalR + JWT bearer + Redis backplane + MessagePack registration.
// This is the starter scaffold; the hub method bodies are stubs in ChatHub.cs.
//
// Citations:
//   Hub setup:     https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs
//   JWT on upgrade:https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication
//   Redis backplane:https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane
//   MessagePack:   https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol

#nullable enable
using System.Text;
using Crunch.Chat;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---------------------------------------------------------

string jwtIssuer   = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer required");
string jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience required");
string jwtKey      = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key required");
string redisConn   = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString required");
string pgConn      = builder.Configuration.GetConnectionString("Catalog")
    ?? throw new InvalidOperationException("ConnectionStrings:Catalog required");

// --- Services --------------------------------------------------------------

// JWT bearer with the access_token query-string hook gated on /hubs/* paths.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };

        // The load-bearing piece: extract access_token from the query string
        // ONLY for hub paths. Other endpoints continue to use the Authorization
        // header.
        // See https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// EF Core for the durable message store.
builder.Services.AddDbContext<CatalogDb>(o =>
{
    o.UseNpgsql(pgConn);
    if (builder.Environment.IsDevelopment())
    {
        o.LogTo(Console.WriteLine, LogLevel.Information);
        o.EnableSensitiveDataLogging();
    }
});

// Singletons for in-process state shared across hub invocations.
builder.Services.AddSingleton<RoomTracker>();
builder.Services.AddSingleton<DedupeCache>();
builder.Services.AddSingleton<MessageStore>();
builder.Services.AddSingleton<PerfMeasurer>();

// SignalR with MessagePack protocol and Redis backplane.
builder.Services
    .AddSignalR(o =>
    {
        o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        o.KeepAliveInterval    = TimeSpan.FromSeconds(15);
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    })
    .AddMessagePackProtocol()
    .AddStackExchangeRedis(redisConn, options =>
    {
        options.Configuration.ChannelPrefix =
            RedisChannel.Literal("crunch-chat");
    });

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:8080")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// --- Pipeline --------------------------------------------------------------

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Apply pending migrations at startup. Production-grade systems prefer the
// idempotent-script workflow from Week 10; we keep it inline for the demo.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDb>();
    await db.Database.MigrateAsync();
}

// Hub mapping.
app.MapHub<ChatHub>("/hubs/chat");

// Dev-only endpoints. Strip these in production.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/token", (string user, IConfiguration cfg) =>
        Results.Ok(new { token = TokenIssuer.Issue(user, cfg) }));
}

// Identity probe: which instance am I on? Useful for verifying load
// balancing across chat-1 and chat-2.
app.MapGet("/__instance", () => Environment.MachineName);

// Health probe: returns 200 if SignalR, Redis, and Postgres are reachable.
app.MapGet("/health", async (CatalogDb db, IConnectionMultiplexer redis) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        var pong = await redis.GetDatabase().PingAsync();
        return Results.Ok(new { db = "ok", redis = pong.TotalMilliseconds + "ms" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/", () => "Crunch.Chat. Hub at /hubs/chat. Dev token at /dev/token?user=...");

app.Run();
