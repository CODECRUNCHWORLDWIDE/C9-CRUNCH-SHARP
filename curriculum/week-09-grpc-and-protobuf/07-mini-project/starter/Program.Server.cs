// Program.cs for src/CrunchCounter.Server.
//
// Replace the auto-generated Program.cs (from `dotnet new grpc`) with this
// file. Note: keep the filename as Program.cs in the project; this starter
// file is named Program.Server.cs to disambiguate from the client starter.

#nullable enable

using CrunchCounter.Server.Interceptors;
using CrunchCounter.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// gRPC + the server-side logging interceptor.
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = false;       // never true in production
    options.Interceptors.Add<LoggingServerInterceptor>();
    options.MaxReceiveMessageSize = 4 * 1024 * 1024;
    options.MaxSendMessageSize = 4 * 1024 * 1024;
});

// Interceptor registration in DI — Interceptors are resolved per call.
builder.Services.AddScoped<LoggingServerInterceptor>();

// In-process state.
builder.Services.AddSingleton<ICounterStore, InMemoryCounterStore>();
builder.Services.AddSingleton<IEventBus, ChannelBackedEventBus>();

// Logging — console only for the mini-project; in production wire to
// OpenTelemetry's logging exporter.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});

var app = builder.Build();

// gRPC route plus a human-friendly hint at /.
app.MapGrpcService<CounterService>();
app.MapGet("/", () => "Crunch Counter gRPC service. Use a gRPC client.");

app.Run();
