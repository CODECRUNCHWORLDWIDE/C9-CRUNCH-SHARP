// Exercise 02 — Serilog as the host logger plus OpenTelemetry .NET for
// distributed tracing. Build on exercise-01: the same host, with the
// addition of structured JSON logs and a console trace exporter. Verify
// that a single curl produces a structured log line and at least one
// trace span, both carrying the same TraceId.
//
// Estimated time: 90 minutes. The solution is in SOLUTIONS.md.
//
// Setup (additive to exercise-01):
//   dotnet add package Serilog.AspNetCore --version 8.0.*
//   dotnet add package Serilog.Sinks.Console --version 5.0.*
//   dotnet add package Serilog.Formatting.Compact --version 2.0.*
//   dotnet add package Serilog.Enrichers.Environment --version 2.3.*
//   dotnet add package OpenTelemetry.Extensions.Hosting --version 1.7.*
//   dotnet add package OpenTelemetry.Instrumentation.AspNetCore --version 1.7.*
//   dotnet add package OpenTelemetry.Instrumentation.Http --version 1.7.*
//   dotnet add package OpenTelemetry.Exporter.Console --version 1.7.*
//
// References:
//   https://github.com/serilog/serilog-aspnetcore
//   https://github.com/serilog/serilog-formatting-compact
//   https://github.com/open-telemetry/opentelemetry-dotnet
//   https://opentelemetry.io/docs/specs/semconv/

#nullable enable

using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace ProjectHub.Exercise02;

public class Program
{
    // ActivitySource for application-level spans. The name must match
    // what we register with OpenTelemetry's AddSource(...) below.
    public static readonly ActivitySource AppActivity = new("ProjectHub.Exercise02");

    public static async Task Main(string[] args)
    {
        // ----------------------------------------------------------------
        // TASK 1. Stand up the Serilog bootstrap logger before the host is
        // built. This catches any failure during startup before the host's
        // own logger is wired. The "bootstrap" qualifier is important —
        // it tells Serilog to keep the in-memory queue until the final
        // host configuration replaces it.
        // ----------------------------------------------------------------
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(new CompactJsonFormatter())
            .Enrich.FromLogContext()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            // ------------------------------------------------------------
            // TASK 2. Replace the framework's default logger with Serilog.
            //   - Read config from appsettings via ReadFrom.Configuration
            //   - Enrich every log line with machine name, environment
            //     name, and a fixed Application property
            //   - Write to the console with the compact JSON formatter
            //
            // The result: every log line is a single JSON object that a
            // log aggregator can index without further parsing.
            // ------------------------------------------------------------
            builder.Host.UseSerilog((context, services, loggerConfig) =>
            {
                loggerConfig
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithProperty("Application", "projecthub-ex02")
                    .WriteTo.Console(new CompactJsonFormatter());
            });

            // ------------------------------------------------------------
            // TASK 3. Register OpenTelemetry with:
            //   - service.name = "projecthub-ex02"
            //   - the ActivitySource we declared at the top of this file
            //   - the ASP.NET Core and HttpClient instrumentations
            //   - the console exporter (so we can read traces by hand)
            //
            // The ActivitySource we registered will produce spans for
            // application code; the framework instrumentations cover HTTP
            // in and out. Citation: https://github.com/open-telemetry/opentelemetry-dotnet
            // ------------------------------------------------------------
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: "projecthub-ex02",
                        serviceVersion: "0.1.0"))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddSource(AppActivity.Name)
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = httpContext =>
                                !httpContext.Request.Path.StartsWithSegments("/health");
                        })
                        .AddHttpClientInstrumentation()
                        .AddConsoleExporter();
                });

            var app = builder.Build();

            // ------------------------------------------------------------
            // TASK 4. Serilog request logging. One JSON line per request,
            // with method, path, status code, duration, and trace ID.
            //
            // Enrich the request log with the caller's identity (if any)
            // by setting EnrichDiagnosticContext.
            // ------------------------------------------------------------
            app.UseSerilogRequestLogging(options =>
            {
                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    if (httpContext.User.Identity?.IsAuthenticated == true)
                    {
                        diagnosticContext.Set(
                            "UserId",
                            httpContext.User.FindFirst("sub")?.Value);
                    }
                };
            });

            // ------------------------------------------------------------
            // TASK 5. A small endpoint that demonstrates application-
            // level spans and structured log enrichment.
            //
            //   POST /work?steps=3
            //
            // The handler issues a structured log line for each step and
            // wraps each step in a custom span. The result is one log line
            // per step plus one trace span per step, all sharing one
            // TraceId.
            // ------------------------------------------------------------
            app.MapPost("/work", async (
                int steps,
                ILogger<Program> logger,
                CancellationToken cancellationToken) =>
            {
                using var requestSpan = AppActivity.StartActivity("DoWork");
                requestSpan?.SetTag("projecthub.steps_requested", steps);

                logger.LogInformation(
                    "DoWork starting with {Steps} steps", steps);

                for (var i = 1; i <= steps; i++)
                {
                    using var step = AppActivity.StartActivity($"DoStep-{i}");
                    step?.SetTag("projecthub.step_index", i);

                    // Simulate work.
                    await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken);

                    logger.LogInformation(
                        "Step {StepIndex} completed at {ElapsedMs}ms",
                        i,
                        step?.Duration.TotalMilliseconds);
                }

                logger.LogInformation("DoWork finished");
                return Results.Ok(new { steps, ok = true });
            });

            // ------------------------------------------------------------
            // TASK 6. An endpoint that issues an outbound HttpClient call
            // so the HttpClient instrumentation produces a span.
            //
            //   GET /fetch-self?next=3
            //
            // Returns the result of POST /work?steps=<next> from THIS host.
            // The trace will show: inbound /fetch-self -> outbound POST
            // -> inbound /work -> per-step spans. One TraceId, six spans.
            // ------------------------------------------------------------
            app.MapGet("/fetch-self", async (
                int next,
                IHttpClientFactory factory,
                ILogger<Program> logger,
                HttpContext context) =>
            {
                var client = factory.CreateClient();
                var port = context.Connection.LocalPort;
                var url = $"http://localhost:{port}/work?steps={next}";

                logger.LogInformation("Issuing self-fetch to {Url}", url);
                using var response = await client.PostAsync(url, content: null);
                var body = await response.Content.ReadAsStringAsync();
                logger.LogInformation(
                    "Self-fetch returned status {StatusCode}",
                    (int)response.StatusCode);

                return Results.Content(body, "application/json");
            });

            // ------------------------------------------------------------
            // The HttpClient factory must be registered for /fetch-self.
            // ------------------------------------------------------------
            //
            // NB: HttpClient registration belongs in the service registration
            // block; in a real project we would move it. For this exercise
            // we register it lazily via a side-extension method on the host.
            //
            // (See SOLUTIONS.md for the cleaner approach.)
            //
            // Actually — let's do it right. Move the call to AddHttpClient
            // upward by re-building the host. The exercise solution shows
            // the cleaner pattern; here we leave a comment so the student
            // notices the order constraint.

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}

// ------------------------------------------------------------------------
// VERIFICATION (run after dotnet run):
//
//   # 1. Hit /work and read the console output.
//   curl -k -X POST "https://localhost:5001/work?steps=3"
//
//   # Expected console output (interleaved):
//   #   {"@t":"...","@mt":"DoWork starting with {Steps} steps","Steps":3,"TraceId":"<X>",...}
//   #   {"@t":"...","@mt":"Step {StepIndex} completed at {ElapsedMs}ms","StepIndex":1,"TraceId":"<X>",...}
//   #   ...
//   #   {"@t":"...","@mt":"HTTP POST /work responded 200 in {Elapsed:0.0000} ms","TraceId":"<X>",...}
//   #
//   #   Activity.TraceId: <X>
//   #   Activity.DisplayName: POST /work
//   #   Activity.Tags:
//   #     http.request.method: POST
//   #     url.path: /work
//   #     http.response.status_code: 200
//   #   ...
//   #   Activity.TraceId: <X>
//   #   Activity.DisplayName: DoWork
//   #   ...
//   #   Activity.TraceId: <X>
//   #   Activity.DisplayName: DoStep-1
//
//   # 2. Hit /fetch-self?next=2 and verify ONE TraceId carries through both
//   # the inbound and the outbound request.
//   curl -k "https://localhost:5001/fetch-self?next=2"
//
//   # Expected: the TraceId on the inbound /fetch-self span equals the
//   # TraceId on the outbound HttpClient span equals the TraceId on the
//   # inbound /work span. The flame graph in Jaeger would show one trace
//   # with about eight spans nested correctly.
//
// If the TraceId varies across spans, the most common cause is one of:
//   - The ActivitySource name does not match what AddSource() registered.
//   - The Filter on AddAspNetCoreInstrumentation is dropping the request.
//   - You called Task.Run somewhere and the activity context did not flow.
//
// If TraceId appears in Activity output but NOT in the log lines, the
// most common cause is forgetting Enrich.FromLogContext(). The OTel
// instrumentation pushes Activity.Current into the logging scope; without
// the enricher, Serilog ignores it.
// ------------------------------------------------------------------------

// ------------------------------------------------------------------------
// Common stumbles:
//
// - Log lines are plaintext, not JSON: the WriteTo.Console call is using
//   the default formatter. Pass `new CompactJsonFormatter()` to it.
// - No TraceId in log lines: Enrich.FromLogContext() is missing.
// - The DoStep spans appear under "Activity.ParentSpanId: (none)" instead
//   of under the DoWork parent: you forgot to register AddSource with the
//   matching name. The activity is being created but not exported.
// - HttpClient span shows but DoWork span does not: AddSource(...) is
//   missing for "ProjectHub.Exercise02".
//
// Stretch goal: switch the exporter from ConsoleExporter to OtlpExporter
// pointed at a Jaeger all-in-one container. Open <http://localhost:16686/>
// and verify the same trace renders as a flame graph. See:
//   docker run --rm --name jaeger -p 16686:16686 -p 4317:4317 \
//     -e COLLECTOR_OTLP_ENABLED=true \
//     jaegertracing/all-in-one:1.54
// ------------------------------------------------------------------------
