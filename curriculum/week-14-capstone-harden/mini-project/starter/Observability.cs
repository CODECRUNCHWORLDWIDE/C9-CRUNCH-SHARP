// Polyglot Workshop — Observability scaffolding (starter)
//
// Wires the .NET 9 OpenTelemetry SDK (traces + metrics) and the Serilog->OTLP log
// sink, and declares the workshop's ActivitySource and Meter. Drop in your manual
// spans (WorkshopActivity.Source.StartActivity(...)) and metric calls on the hot
// paths; the automatic instrumentation handles HTTP / gRPC / EF Core / Npgsql.
//
// Citations:
//   .NET + OTel:  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
//   OTel .NET:    https://opentelemetry.io/docs/languages/net/
//   Serilog OTLP: https://github.com/serilog/serilog-sinks-opentelemetry

#nullable enable
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Workshop.Api.Telemetry;

public static class WorkshopTelemetry
{
    public const string ServiceName = "workshop-api";   // the join key across all signals
    public const string ServiceVersion = "1.0.0";
}

public static class WorkshopActivity
{
    public const string SourceName = "Workshop.Api";
    public static readonly ActivitySource Source = new(SourceName, WorkshopTelemetry.ServiceVersion);
}

public sealed class WorkshopMetrics
{
    public const string MeterName = "Workshop.Api";
    private readonly Counter<long> _submissions;
    private readonly Histogram<double> _gradingDuration;

    public WorkshopMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(MeterName);
        _submissions = meter.CreateCounter<long>("workshop.submissions.accepted", unit: "{submission}");
        _gradingDuration = meter.CreateHistogram<double>("workshop.grading.duration", unit: "ms");
    }

    public void SubmissionAccepted(string tenant) =>
        _submissions.Add(1, new KeyValuePair<string, object?>("tenant", tenant));

    public void GradingCompleted(double ms) => _gradingDuration.Record(ms);
}

public static class ObservabilityRegistration
{
    public static WebApplicationBuilder AddWorkshopObservability(this WebApplicationBuilder builder)
    {
        string otlp = builder.Configuration["Otel:Endpoint"] ?? "http://localhost:4317";

        builder.Services.AddSingleton<WorkshopMetrics>();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                WorkshopTelemetry.ServiceName, serviceVersion: WorkshopTelemetry.ServiceVersion)
                .AddAttributes(new KeyValuePair<string, object>[]
                {
                    new("deployment.environment", builder.Environment.EnvironmentName)
                }))
            .WithTracing(t => t
                .AddSource(WorkshopActivity.SourceName)
                .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                .AddGrpcClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlp)))
            .WithMetrics(m => m
                .AddMeter(WorkshopMetrics.MeterName)
                .AddAspNetCoreInstrumentation()     // RED metrics, with exemplars
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlp)));

        // Serilog -> OTLP -> Loki, with TraceId/SpanId enrichment for correlation.
        builder.Host.UseSerilog((ctx, _, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithSpan()                       // stamps TraceId/SpanId on every event
            .WriteTo.OpenTelemetry(o =>
            {
                o.Endpoint = otlp;
                o.ResourceAttributes.Add("service.name", WorkshopTelemetry.ServiceName);
            }));

        return builder;
    }
}
