// Exercise 1 — A Multi-Stage Dockerfile for the Workshop API, Measured and Non-Root.
//
// Goal: containerize the Polyglot Workshop backend with a multi-stage Dockerfile
// that (a) restores before it copies source so a code change does not bust the
// NuGet cache, (b) ships on the chiseled aspnet:9.0-noble-chiseled runtime, (c)
// runs as the non-root $APP_UID user, and (d) comes out under ~130 MB. By the end
// you can read `docker history`, point at the layer that is YOUR code, and explain
// why the SDK never reaches the registry.
//
// This is a capstone milestone, not a toy: the image you build here is the exact
// artifact Lecture 2's pipeline pushes to ghcr.io and deploys to Azure Container
// Apps. Citation: https://learn.microsoft.com/en-us/dotnet/core/docker/build-container
//
// ----------------------------------------------------------------------------
// PART 0 — A minimal Workshop API to containerize (paste into Program.cs).
//
// In the real capstone this is your Week 13/14 backend. For the exercise, a tiny
// Minimal API with a /health endpoint is enough to prove the container runs and
// the readiness probe passes. Create it with:
//
//   dotnet new web -n Workshop.Api -f net9.0
//   cd Workshop.Api
//   # replace Program.cs with PART 0 below
// ----------------------------------------------------------------------------

using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args); // Slim builder: AOT-friendly, fewer defaults.

// Source-generated JSON so this stays small and (if you later AOT it) clean.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, HealthJsonContext.Default));

var app = builder.Build();

// The readiness endpoint the ACA probe and the pipeline smoke test both hit.
app.MapGet("/health", () => Results.Ok(new HealthStatus("Healthy")));

// One real-ish endpoint so the image does something demonstrable.
app.MapGet("/lessons/{id}", (string id) =>
    Results.Ok(new Lesson(id, $"Lesson {id}", Published: true)));

app.Run();

public sealed record HealthStatus(string Status);
public sealed record Lesson(string Id, string Title, bool Published);

[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(Lesson))]
internal sealed partial class HealthJsonContext : JsonSerializerContext;

// ============================================================================
// PART 1 — Dockerfile (create at the repo root, next to the .csproj or solution).
//
// Save the block below as `Dockerfile` (no extension). It is the deliverable.
// ============================================================================
/*
# ---- build stage: the fat SDK, discarded after publish ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore FIRST, from just the project file, so this layer is cached across
# source-only changes. This single ordering trick is the difference between a
# 90-second rebuild and a 6-second one on every code change.
COPY ["Workshop.Api.csproj", "./"]
RUN dotnet restore "Workshop.Api.csproj"

# Now the source, then publish.
COPY . .
RUN dotnet publish "Workshop.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- final stage: chiseled runtime, ~110 MB, no shell, non-root ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Workshop.Api.dll"]
*/

// ============================================================================
// PART 2 — .dockerignore (create at the repo root, save as `.dockerignore`).
// Without this, `COPY . .` drags bin/obj/.git into the build context, busting
// the cache and risking a leak.
// ============================================================================
/*
**/bin/
**/obj/
**/.vs/
**/.vscode/
.git/
.github/
**/*.user
**/appsettings.*.local.json
**/.env
Dockerfile
.dockerignore
*/

// ============================================================================
// PART 3 — Build, run, and inspect. Run these in order; record the outputs.
// ============================================================================
/*
# Build the image.
docker build -t workshop-api:dev .

# Run it; map the container's 8080 to the host's 8080.
docker run --rm -d -p 8080:8080 --name wapi workshop-api:dev

# Prove it serves and the readiness endpoint is healthy.
curl -fsS http://localhost:8080/health        # -> {"status":"Healthy"}
curl -fsS http://localhost:8080/lessons/42     # -> {"id":"42","title":"Lesson 42","published":true}

# Measure the image size.
docker image ls workshop-api:dev               # SIZE should be ~115-125 MB

# Read the layers; find the one that is YOUR code (single-digit MB).
docker history workshop-api:dev

# Prove it runs as NON-ROOT. A chiseled image has no shell, so you cannot
# `docker exec ... whoami`; instead, read the configured user from the image:
docker inspect workshop-api:dev --format '{{.Config.User}}'   # -> a non-zero UID

docker stop wapi
*/

// ============================================================================
// ACCEPTANCE CRITERIA
//   1. `docker build` succeeds with no warnings.
//   2. `docker image ls` shows the image under ~130 MB (chiseled runtime + app).
//   3. `curl /health` returns {"status":"Healthy"}; `/lessons/42` returns JSON.
//   4. `docker inspect ... .Config.User` shows a non-zero UID (NOT root / empty).
//   5. `docker history` shows the SDK is NOT in the final image — the largest
//      layer is the ~110 MB aspnet runtime base, and YOUR app layer is small.
//   6. Editing Program.cs and rebuilding does NOT re-run `dotnet restore`
//      (the restore layer is served from cache) — confirm by watching the
//      build output for "CACHED [build 4/6] RUN dotnet restore".
//
// STRETCH
//   A. Add a HEALTHCHECK to the Dockerfile? NO — and explain in a comment why
//      you let Azure Container Apps own the readiness probe instead of baking a
//      curl-based HEALTHCHECK into a chiseled image that has no curl.
//   B. Swap the final stage to `aspnet:9.0-noble-chiseled-extra` and compare the
//      size delta; note what the `-extra` tag adds and when you would want it.
//   C. Build the same image with `dotnet publish /t:PublishContainer` (no
//      Dockerfile) and compare the resulting size and layers. Explain which one
//      you ship in CI and why (hint: the Dockerfile is the reviewable contract).
// ============================================================================
