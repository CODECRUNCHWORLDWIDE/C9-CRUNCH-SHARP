// Exercise 4 — Wire the OpenTelemetry SDK, emit a manual span and a RED metric,
//              bring up the Grafana + Loki + Tempo stack, and correlate ONE
//              request across all three signals.
//
// Goal: instrument the workshop backend so a single POST /api/submissions produces
//   - a TRACE in Tempo (HTTP span -> MediatR -> grade.submission -> EF Core -> Npgsql),
//   - LOGS in Loki carrying the same TraceId,
//   - METRICS in Prometheus (RED + workshop.submissions.accepted),
// and then click from the metric's exemplar to the trace to the logs in Grafana.
//
// Citations:
//   .NET + OTel:  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel
//   OTel .NET:    https://opentelemetry.io/docs/languages/net/
//   Exemplars:    https://opentelemetry.io/docs/specs/otel/metrics/data-model/#exemplars
//   Tempo:        https://grafana.com/docs/tempo/latest/
//
// The compose stack and the collector/datasource configs ship in
// mini-project/observability/. This exercise wires the .NET side and runs the walk.

#nullable enable

// ============================================================================
// PART 1 — Telemetry.cs (the ActivitySource and the Meter)
// ============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Workshop.Api.Telemetry;

public static class WorkshopActivity
{
    public const string SourceName = "Workshop.Api";
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}

public sealed class WorkshopMetrics
{
    public const string MeterName = "Workshop.Api";
    private readonly Counter<long> _submissions;
    private readonly Histogram<double> _gradingDuration;

    public WorkshopMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(MeterName);
        _submissions = meter.CreateCounter<long>(
            "workshop.submissions.accepted", unit: "{submission}");
        _gradingDuration = meter.CreateHistogram<double>(
            "workshop.grading.duration", unit: "ms");
    }

    public void SubmissionAccepted(string tenant) =>
        _submissions.Add(1, new KeyValuePair<string, object?>("tenant", tenant));

    public void GradingCompleted(double ms) => _gradingDuration.Record(ms);
}

// ============================================================================
// PART 2 — The SDK registration (Program.cs)
// ============================================================================
//
// using OpenTelemetry.Metrics;
// using OpenTelemetry.Resources;
// using OpenTelemetry.Trace;
// using Serilog;
// using Workshop.Api.Telemetry;
//
// string otlp = builder.Configuration["Otel:Endpoint"] ?? "http://localhost:4317";
//
// builder.Services.AddSingleton<WorkshopMetrics>();
//
// builder.Services.AddOpenTelemetry()
//     .ConfigureResource(r => r.AddService("workshop-api", serviceVersion: "1.0.0"))
//     .WithTracing(t => t
//         .AddSource(WorkshopActivity.SourceName)
//         .AddAspNetCoreInstrumentation(o => o.RecordException = true)
//         .AddGrpcClientInstrumentation()
//         .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
//         .AddHttpClientInstrumentation()
//         .AddOtlpExporter(o => o.Endpoint = new Uri(otlp)))
//     .WithMetrics(m => m
//         .AddMeter(WorkshopMetrics.MeterName)
//         .AddAspNetCoreInstrumentation()       // the RED metrics, for free
//         .AddRuntimeInstrumentation()
//         .AddOtlpExporter(o => o.Endpoint = new Uri(otlp)));
//
// builder.Host.UseSerilog((ctx, _, cfg) => cfg
//     .ReadFrom.Configuration(ctx.Configuration)
//     .Enrich.WithSpan()                        // stamps TraceId/SpanId on every log
//     .WriteTo.OpenTelemetry(o => { o.Endpoint = otlp; o.ResourceAttributes.Add("service.name", "workshop-api"); }));

// ============================================================================
// PART 3 — A manual span + the metric, inside the grading code
// ============================================================================
//
// using System.Diagnostics;
// using Workshop.Api.Telemetry;
//
// public sealed class GradingService(
//     WorkshopDbContext db, WorkshopMetrics metrics, ILogger<GradingService> log)
// {
//     public async Task<int> GradeAsync(Guid submissionId, CancellationToken ct)
//     {
//         using var activity = WorkshopActivity.Source.StartActivity("grade.submission");
//         var sw = Stopwatch.StartNew();
//
//         var submission = await db.Submissions.FirstAsync(s => s.Id == submissionId, ct);
//         activity?.SetTag("workshop.lesson_id", submission.LessonId.ToString());
//         activity?.SetTag("workshop.tenant", submission.TenantId);
//
//         int grade = Rubric.Evaluate(submission.Content);   // the (child EF spans nest here)
//         submission.Grade = grade;
//         await db.SaveChangesAsync(ct);
//
//         activity?.SetTag("workshop.grade", grade);
//         metrics.SubmissionAccepted(submission.TenantId);
//         metrics.GradingCompleted(sw.Elapsed.TotalMilliseconds);
//
//         // Structured log (template + named props), enriched with TraceId by WithSpan().
//         log.LogInformation("Graded submission {SubmissionId} in lesson {LessonId}: {Grade}",
//             submission.Id, submission.LessonId, grade);
//
//         return grade;
//     }
// }
//
// // NEVER put a token or PII in a span tag — tags are visible to anyone with Grafana.

// ============================================================================
// PART 4 — Bring up the stack and generate traffic
// ============================================================================
//
//   # 1. Start the observability stack:
//   docker compose -f mini-project/observability/docker-compose.yml up -d
//
//   # 2. Point the app at the collector and run it:
//   Otel__Endpoint=http://localhost:4317 dotnet run --project src/Workshop.Api
//
//   # 3. Generate some traffic (mint a token, then submit a few times):
//   for i in $(seq 1 20); do
//     curl -s -X POST http://localhost:5000/api/lessons/$L/submissions \
//          -H "Authorization: Bearer $TOKEN" \
//          -H "Content-Type: application/json" \
//          -d '{"content":"answer '$i'"}' > /dev/null
//   done
//
//   # 4. Open Grafana at http://localhost:3000 (anonymous Admin is enabled in dev).

// ============================================================================
// PART 5 — The correlated walk (do this in Grafana; it is the deliverable)
// ============================================================================
//
//   [ ] Explore -> Prometheus: graph
//          histogram_quantile(0.95, sum(rate(http_server_request_duration_seconds_bucket[1m])) by (le))
//       and confirm exemplar diamonds appear under the line.
//   [ ] Click an exemplar diamond -> it opens the TRACE in Tempo.
//   [ ] In the trace, confirm the span tree:
//          POST /api/.../submissions -> MediatR pipeline -> grade.submission
//          -> EF Core SaveChanges -> Npgsql command
//   [ ] Click the TraceId on a span -> it opens Loki filtered to that trace.
//   [ ] Confirm the "Graded submission ... " log line is present with the same TraceId.
//   [ ] Explore -> Prometheus: confirm workshop_submissions_accepted_total increments.

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] One request produces a trace, logs (same TraceId), and metrics.
//   [ ] The EF Core / Npgsql spans nest UNDER your grade.submission span.
//   [ ] No token / access_token / PII appears in any span tag or log property
//      (verify the collector's redaction processor scrubbed it).
//   [ ] You can click metric exemplar -> trace -> logs without leaving Grafana.
//   [ ] workshop.submissions.accepted increments per accepted submission.
//
// Stretch (counted toward Exercise 4 if you finish the above with time left):
//   1. Add a span event (activity?.AddEvent(new ActivityEvent("rubric.matched"))) and
//      confirm it renders on the span timeline in Tempo.
//   2. Build a Grafana dashboard JSON (committed to the repo) with three panels:
//      request rate, error rate, and p99 duration (the RED method) for the API.
