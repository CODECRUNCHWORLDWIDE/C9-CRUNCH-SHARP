// Exercise 2 — A Native AOT Dockerfile for the Analytics Export CLI, Weighed Against the Runtime Image.
//
// Goal: take the capstone's analytics export CLI (the small batch tool that reads
// the Dapper analytics aggregates and writes them out), publish it Native AOT,
// containerize it on runtime-deps:9.0-noble-chiseled, and measure the size and
// startup win against a normal runtime image. By the end you can name what AOT
// forbids and prove your CLI respects it (the build FAILS on an AOT warning).
//
// This is a capstone milestone: this CLI is the same Native AOT artifact you
// shipped in Week 12, now wired as the workshop's nightly progress exporter.
// Citation: https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
//
// Create it with:
//   dotnet new console -n Workshop.Analytics.Cli -f net9.0
//   cd Workshop.Analytics.Cli
//   # replace the .csproj with PART 0, Program.cs with PART 1
//
// ----------------------------------------------------------------------------
// PART 0 — Workshop.Analytics.Cli.csproj (AOT on, warnings are errors).
// ----------------------------------------------------------------------------
/*
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
*/

// ============================================================================
// PART 1 — Program.cs. An AOT-CLEAN exporter: source-generated JSON, NO
// reflection-based serialization, NO MakeGenericType, NO dynamic codegen.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workshop.Analytics.Cli;

// The row the exporter writes. A record is fine under AOT.
public sealed record LessonProgress(
    string LessonId,
    string Title,
    int Enrolled,
    int Completed,
    double CompletionRate);

// Source-generated serializer context. THIS is what makes JSON AOT-safe: the
// serialization code is generated at compile time, so there is no runtime
// reflection for the trimmer to choke on. Citation:
// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LessonProgress))]
[JsonSerializable(typeof(LessonProgress[]))]
internal sealed partial class AnalyticsJsonContext : JsonSerializerContext;

internal static class Program
{
    // In the real capstone these rows come from the Dapper analytics query
    // against PostgreSQL. For the exercise we synthesize them so the CLI is
    // self-contained and you can prove the AOT path without a database.
    private static LessonProgress[] LoadAggregates() =>
    [
        new("c9-w15-l01", "Multi-stage Dockerfiles", Enrolled: 120, Completed: 96, CompletionRate: 0.80),
        new("c9-w15-l02", "GitHub Actions CD",        Enrolled: 118, Completed: 81, CompletionRate: 0.686),
        new("c9-w15-l03", "Writing the runbook",      Enrolled: 117, Completed: 73, CompletionRate: 0.624),
    ];

    private static int Main(string[] args)
    {
        // Tiny, dependency-free arg parse — AOT-friendly, no command-line library
        // doing reflection. Usage: Workshop.Analytics.Cli --out progress.json
        var outPath = "progress.json";
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--out") outPath = args[i + 1];

        var rows = LoadAggregates();

        // The AOT-SAFE serialize call: pass the generated type-info, not the type.
        // If you instead call JsonSerializer.Serialize(rows) (the reflection
        // overload), the AOT publish emits IL3050/IL2026 and, because the project
        // sets TreatWarningsAsErrors, the BUILD FAILS. That is the system catching
        // an AOT violation at build time instead of at runtime.
        var json = JsonSerializer.Serialize(rows, AnalyticsJsonContext.Default.LessonProgressArray);

        File.WriteAllText(outPath, json);
        Console.WriteLine($"wrote {rows.Length} rows to {outPath}");
        return 0;
    }
}

// ============================================================================
// PART 2 — Dockerfile.aot (save at the repo root as `Dockerfile.aot`).
// The build stage uses the AOT-ready SDK variant with clang/zlib preinstalled.
// The final stage is runtime-deps (NO .NET runtime — the binary carries it).
// ============================================================================
/*
# ---- build: AOT cross-compile toolchain preinstalled ----
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble-aot AS build
WORKDIR /src
COPY ["Workshop.Analytics.Cli.csproj", "./"]
RUN dotnet restore "Workshop.Analytics.Cli.csproj"
COPY . .
RUN dotnet publish "Workshop.Analytics.Cli.csproj" -c Release -r linux-x64 \
    -o /app/publish /p:PublishAot=true

# ---- final: runtime-deps carries only the OS libs the native binary links ----
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish/Workshop.Analytics.Cli .
USER $APP_UID
ENTRYPOINT ["./Workshop.Analytics.Cli"]
*/

// ============================================================================
// PART 3 — Build, run, and weigh it.
// ============================================================================
/*
# AOT-publish locally first, OUTSIDE Docker, to see the size and prove it is
# AOT-clean (no IL3050/IL2026). On Linux/macOS x64:
dotnet publish -c Release -r linux-x64 /p:PublishAot=true
ls -lh bin/Release/net9.0/linux-x64/publish/Workshop.Analytics.Cli   # ~5-9 MB binary

# Build the AOT image.
docker build -f Dockerfile.aot -t workshop-analytics:aot .

# Run it; it writes progress.json INSIDE the container and prints the count.
docker run --rm workshop-analytics:aot --out /tmp/progress.json

# Weigh the AOT image against a hypothetical runtime image of the same CLI.
docker image ls | grep workshop-analytics                            # ~30-40 MB

# Compare cold start: time the first invocation. AOT has no JIT warm-up.
time docker run --rm workshop-analytics:aot --out /tmp/progress.json  # tens of ms in-process
*/

// ============================================================================
// ACCEPTANCE CRITERIA
//   1. `dotnet publish /p:PublishAot=true` succeeds with ZERO AOT/trim warnings.
//   2. Deliberately swapping the serialize call to the reflection overload
//      (`JsonSerializer.Serialize(rows)`) makes the AOT build FAIL with IL3050
//      and/or IL2026 — capture the error, then revert. (Prove the guardrail.)
//   3. The native binary is single-digit megabytes; the AOT image is ~30-40 MB.
//   4. `docker run ... --out /tmp/progress.json` prints "wrote 3 rows ...".
//   5. The AOT image is ~3x smaller than the equivalent aspnet runtime image
//      from Exercise 1 — record both numbers and the ratio.
//
// STRETCH
//   A. Name three things AOT forbids and, for each, where in this CLI you would
//      have hit it if you were careless (reflection-based JSON, MakeGenericType
//      on an unseen type, a command-line parser that reflects over an options
//      class). Write them as comments.
//   B. Add `InvariantGlobalization=false` and re-measure; explain the size jump
//      (ICU comes back) and when a real CLI needs it.
//   C. Why is the gRPC service NOT an AOT candidate in this capstone, but this
//      CLI is? Answer in two sentences referencing ASP.NET Core AOT support:
//      https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot
// ============================================================================
