// Workshop.Api / Observability/WorkshopTelemetry.cs — the domain's telemetry
// surface: one ActivitySource for spans the framework does not emit, one Meter
// for the domain counters. Created once, shared everywhere.
//
// The names ("Workshop.Api") are what Program.cs registers via .AddSource(...)
// and .AddMeter(...); they must match exactly or the spans/metrics are dropped
// silently by the OpenTelemetry SDK (it only collects sources it was told about).
//
// Citations:
//   ActivitySource:  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing
//   Metrics:         https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation

#nullable enable
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Workshop.Api.Observability;

public static class WorkshopTelemetry
{
    public const string ActivitySourceName = "Workshop.Api";
    public const string MeterName = "Workshop.Api";

    // Spans. Start one per domain operation with Activity.StartActivity("Name").
    // Returns null if no listener is attached, so always use the ?. operator.
    public static readonly ActivitySource Activity = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> LessonsCreated =
        Meter.CreateCounter<long>(
            name: "workshop.lessons.created",
            unit: "{lesson}",
            description: "Number of lessons created.");

    public static readonly Counter<long> SubmissionsReceived =
        Meter.CreateCounter<long>(
            name: "workshop.submissions.received",
            unit: "{submission}",
            description: "Number of submissions accepted.");

    // A histogram for the create-lesson latency, so the metrics surface carries
    // a distribution, not just a count. Record with .Record(elapsedMs) around
    // the persist call.
    public static readonly Histogram<double> CreateLessonDuration =
        Meter.CreateHistogram<double>(
            name: "workshop.lessons.create.duration",
            unit: "ms",
            description: "Wall-clock time to create and persist a lesson.");
}
