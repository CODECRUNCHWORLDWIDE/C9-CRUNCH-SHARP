# Lecture 1 — Multi-Stage Dockerfiles for ASP.NET Core and Native AOT: Chiseled Runtimes, Layer Caching, and Running as Non-Root

> **Time:** 2 hours. Take the ASP.NET Core multi-stage Dockerfile in one sitting and the Native AOT image in a second. **Prerequisites:** Week 12 (you have published a Native AOT binary and read a BenchmarkDotNet report). **Citations:** the .NET containerization guide at <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>, the official image catalog at <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>, the Native AOT docs at <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>, and the `dotnet/dotnet-docker` repository at <https://github.com/dotnet/dotnet-docker>.

## 1. The mental model: an image is a tarball of layers, and you control what is in each layer

Before any Dockerfile, fix the mental model, because everything else in this lecture is a consequence of it. An OCI image is an ordered stack of filesystem layers plus a small JSON config that says how to run it (the entrypoint, the user, the exposed ports, the environment). Each instruction in a Dockerfile that changes the filesystem — `COPY`, `RUN`, `ADD` — produces one new layer on top of the previous ones. When you `docker run` the image, the engine stacks the layers into a single root filesystem and starts your process inside it. The image specification is worth reading once, at <https://github.com/opencontainers/image-spec>; it is shorter than you expect and it pays off forever.

Two consequences drive the whole design of a good .NET Dockerfile:

1. **Layers are cached and shared.** If two images are built `FROM` the same base, they share the base layers on disk and on the wire — you pull them once. And within one build, a layer is rebuilt only if its instruction or any earlier layer changed. This is why the *order* of instructions matters: put the things that rarely change (the base image, the restored NuGet packages) early, and the things that change every commit (your source) late, so a one-line code change does not invalidate the restore.
2. **Everything in a layer ships.** If you `COPY . .` into your build stage, then your `.git` directory, your local `bin/obj`, your secrets file, and your editor's scratch files are all in that layer. They will not be in the *final* image if you use multiple stages (more on that in a moment), but they will be in the build cache and, if you are careless with stages, in production. A `.dockerignore` file is not optional; it is the first file you write.

## 2. Why multi-stage: the SDK is 800 MB and you do not deploy a compiler

The naive Dockerfile builds and runs in one stage:

```dockerfile
# DON'T do this — the 800 MB anti-pattern
FROM mcr.microsoft.com/dotnet/sdk:9.0
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o /out
ENTRYPOINT ["dotnet", "/out/Workshop.Api.dll"]
```

This works and it is wrong. The final image carries the entire .NET **SDK** — the compiler, MSBuild, the NuGet client, the analyzers, every targeting pack — roughly 800 MB of tooling that exists only to *build* your app and does nothing at runtime. You ship the factory along with the car. It pulls slowly, it scales-from-zero slowly on a free tier, and every one of those SDK binaries is attack surface in production.

The fix is **multi-stage builds**: a `build` stage `FROM` the SDK that compiles and publishes, and a separate `final` stage `FROM` a thin runtime image that copies *only* the published output out of the build stage. Docker discards every stage except the last; the SDK never reaches the registry. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container#create-a-dockerfile>.

## 3. The canonical ASP.NET Core 9 multi-stage Dockerfile

Here is the Dockerfile for the Polyglot Workshop backend (`Workshop.Api`), annotated line by line. This is the file you will adapt for the mini-project.

```dockerfile
# ---- Stage 1: build ----
# The SDK image has the compiler, MSBuild, and the NuGet client. It is fat on
# purpose; nothing from this stage reaches the final image.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy ONLY the project files first and restore. This layer is cached as long as
# no .csproj changes — so a one-line source edit does NOT re-download NuGet.
# This is the single highest-leverage caching trick in a .NET Dockerfile.
COPY ["src/Workshop.Api/Workshop.Api.csproj", "src/Workshop.Api/"]
COPY ["src/Workshop.Contracts/Workshop.Contracts.csproj", "src/Workshop.Contracts/"]
COPY ["src/Workshop.Domain/Workshop.Domain.csproj", "src/Workshop.Domain/"]
COPY ["Directory.Packages.props", "Directory.Build.props", "./"]
RUN dotnet restore "src/Workshop.Api/Workshop.Api.csproj"

# NOW copy the rest of the source and publish. This layer rebuilds on every code
# change, but the expensive restore above is already cached.
COPY . .
RUN dotnet publish "src/Workshop.Api/Workshop.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false      # no native launcher; we invoke via `dotnet`

# ---- Stage 2: final ----
# The chiseled ASP.NET runtime: the .NET runtime + ASP.NET Core, on a stripped
# Ubuntu with no shell, no package manager, ~110 MB. Citation:
# https://learn.microsoft.com/en-us/dotnet/core/docker/container-images
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app

# Copy ONLY the published output out of the build stage. The SDK, the source,
# the .git directory — none of it crosses this line.
COPY --from=build /app/publish .

# The chiseled images ship a non-root user; $APP_UID resolves to it. Running as
# root in a container is a finding in every security review. Drop the privilege.
USER $APP_UID

# Kestrel inside the container listens on 8080 by default in .NET 8+ chiseled
# images (the old 80 default needed root to bind). Document it; ACA maps to 443.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "Workshop.Api.dll"]
```

Build and inspect it:

```bash
docker build -t workshop-api:dev .
docker image ls workshop-api:dev
# REPOSITORY     TAG   IMAGE ID       SIZE
# workshop-api   dev   a1b2c3d4e5f6   118MB
```

118 MB instead of 800-plus. That difference is real money on a free tier where cold-start pull time is on the critical path of the first request after a scale-to-zero.

### 3.1 The `.dockerignore` that makes this fast and safe

`COPY . .` copies everything in the build context that is not excluded. Without a `.dockerignore`, that includes `bin/`, `obj/`, `.git/`, and any local secrets — bloating the context, busting the cache, and risking a leak. Write this next to the Dockerfile:

```gitignore
# .dockerignore
**/bin/
**/obj/
**/.vs/
**/.vscode/
.git/
.github/
**/*.user
**/appsettings.*.local.json
**/.env
README.md
Dockerfile
.dockerignore
```

Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container#create-a-dockerignore-file>.

## 4. What "chiseled" means, and the one time it bites you

A **chiseled** image (`-noble-chiseled`) is an Ubuntu Noble base with everything non-essential removed: no shell (`/bin/sh` is gone), no package manager, no `ls`, no `cat`, no users other than the app user. What remains is the minimum to run a .NET process: the C runtime, ICU (for globalization), the TLS root certificates, and the .NET runtime you layered on. The benefits are direct: a smaller image, a smaller attack surface, fewer CVEs to triage because there is almost nothing installed to have a CVE.

The cost is the thing that bites you the first time: **you cannot `docker exec -it <container> bash` into a chiseled image to poke around, because there is no `bash` and no shell at all.** This is a feature in production (an attacker who lands in the container has no shell either) and an annoyance in development. The escape hatches, in order of preference:

1. **Read the logs.** A well-instrumented service (Serilog + OpenTelemetry, from Week 14) tells you what is wrong without a shell. This is the right answer 90% of the time and the reason Week 14 came before Week 15.
2. **Use the `-chiseled-extra` variant** when you genuinely need ICU and the full TLS root set and a couple of debug tools — it is a documented middle ground, still small, still non-root. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images#ubuntu-chiseled-images>.
3. **Attach the .NET diagnostics tools** (`dotnet-trace`, `dotnet-dump`, `dotnet-monitor` as a sidecar) which talk to the runtime over the diagnostic socket and need no shell. This is the production-grade way to inspect a chiseled container.

The rule for the capstone: **ship chiseled, debug from logs.** If you find yourself wanting a shell in production, the missing thing is usually an instrumentation gap, and the fix is a log line, not a shell.

## 5. Why non-root is non-negotiable, and how `$APP_UID` gives it to you for free

By default, a process in a container runs as `root` (UID 0) *inside the container's user namespace*. Even with namespacing, a root process in a container is a larger blast radius: it can write anywhere in the container filesystem, and a container-escape vulnerability turns container-root into a much worse problem on the host. Every serious security review flags root containers, and Azure Container Apps, Kubernetes Pod Security Standards, and OpenShift all push you toward non-root.

The chiseled .NET images solve this for you: they create a non-root user and expose its UID as the build arg / environment value `$APP_UID` (it resolves to `1654` in the current images, but you reference the variable, not the number). A single `USER $APP_UID` line drops the privilege. The only thing to watch: a non-root process **cannot bind to ports below 1024**, which is exactly why the modern chiseled images default Kestrel to `8080` instead of the old `80`. Bind high, let the platform (ACA, Kubernetes, nginx) terminate `443` and forward to `8080`. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images#net-and-the-app_uid-environment-variable>.

## 6. Native AOT: a 30 MB image and a sub-50 ms cold start, with constraints

Week 12 introduced Native AOT: the .NET compiler emits a self-contained native executable with no JIT, no IL, and (mostly) no runtime reflection. For a containerized service the payoff is twofold and it matters specifically on a scale-to-zero free tier:

- **Size.** A trimmed AOT binary on `runtime-deps:9.0-noble-chiseled` is roughly 30–40 MB total, versus ~110 MB for the chiseled-runtime image plus your DLLs. The `runtime-deps` image carries no .NET runtime at all — just the OS libraries the native binary links against — because AOT bakes the runtime into the binary.
- **Startup.** No JIT warm-up means the process is serving requests in tens of milliseconds instead of a few hundred. On a free tier that scales to zero, every request after an idle period pays the cold start, and AOT shrinks it.

The constraints, restated from Week 12 because they decide what *can* be AOT-published: no runtime IL generation (no `Reflection.Emit`, no dynamic proxies that emit code), constrained reflection (the trimmer must be able to see, statically, every type you reflect over — `MakeGenericType` on a type the compiler never saw will throw at runtime), and no `System.Text.Json` reflection-based serialization (you use the **source-generated** `JsonSerializerContext` instead, which AOT loves). ASP.NET Core supports AOT for **Minimal APIs** with the request-delegate generator, but **not** for MVC or Razor. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot>.

For the Polyglot Workshop this means: the gRPC service and the MVC/Razor admin are **not** AOT candidates. The right AOT target is the small **analytics export CLI** — a self-contained command-line tool (the one you AOT-published in Week 12, now the capstone's batch exporter) that reads the Dapper analytics aggregates and writes a CSV. That is the binary the next Dockerfile builds.

## 7. The Native AOT multi-stage Dockerfile

AOT needs a C toolchain (`clang`, the linker, `zlib`) at *build* time to link the native binary. The .NET image catalog provides an AOT-ready SDK variant with that toolchain preinstalled, so you do not hand-install `clang`:

```dockerfile
# ---- Stage 1: build (AOT cross-compile toolchain preinstalled) ----
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble-aot AS build
WORKDIR /src

COPY ["src/Workshop.Analytics.Cli/Workshop.Analytics.Cli.csproj", "src/Workshop.Analytics.Cli/"]
RUN dotnet restore "src/Workshop.Analytics.Cli/Workshop.Analytics.Cli.csproj"

COPY . .
# PublishAot=true emits a native binary. -r linux-x64 picks the RID; AOT is
# always self-contained and platform-specific. The build FAILS on any AOT/trim
# warning if you set TreatWarningsAsErrors, which you should.
RUN dotnet publish "src/Workshop.Analytics.Cli/Workshop.Analytics.Cli.csproj" \
    -c Release \
    -r linux-x64 \
    -o /app/publish \
    /p:PublishAot=true

# ---- Stage 2: final (no .NET runtime — the binary carries it) ----
# runtime-deps has the OS libs the native binary links against, nothing else.
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish/Workshop.Analytics.Cli .
USER $APP_UID
ENTRYPOINT ["./Workshop.Analytics.Cli"]
```

And the project file that makes the CLI AOT-clean:

```xml
<!-- Workshop.Analytics.Cli.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Turn AOT on, and turn the diagnostics on so the build tells you the
         moment something in the dependency tree is AOT-hostile. -->
    <PublishAot>true</PublishAot>
    <TrimmerSingleWarn>false</TrimmerSingleWarn>
    <InvariantGlobalization>true</InvariantGlobalization> <!-- smaller, no ICU -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Build and weigh it:

```bash
docker build -f Dockerfile.aot -t workshop-analytics:aot .
docker image ls | grep workshop-analytics
# workshop-analytics  aot  ...  34.2MB
```

34 MB against 118 MB. The CLI is not the whole capstone, but it is the artifact that proves you know what AOT is for: small, fast-starting, no-JIT batch and edge workloads. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container#native-aot>.

### 7.1 AOT-clean JSON: the source generator, not reflection

The AOT CLI cannot use reflection-based `JsonSerializer.Serialize(obj)`. It uses a source-generated context, which the compiler can see through at build time:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workshop.Analytics.Cli;

// One DTO the exporter writes to JSON. Records work fine under AOT.
public sealed record LessonProgress(string LessonId, string Title, int Enrolled, int Completed, double CompletionRate);

// The source-generated context. AOT-safe: no runtime reflection, the serializer
// code is generated at compile time. Citation:
// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LessonProgress))]
[JsonSerializable(typeof(LessonProgress[]))]
internal sealed partial class AnalyticsJsonContext : JsonSerializerContext;

internal static class Exporter
{
    public static string ToJson(LessonProgress[] rows) =>
        // Pass the generated type-info, NOT the type — this is the AOT-safe call.
        JsonSerializer.Serialize(rows, AnalyticsJsonContext.Default.LessonProgressArray);
}
```

If you call the reflection overload instead, the AOT publish emits `IL3050` / `IL2026` warnings, and because the project sets `TreatWarningsAsErrors`, the build fails. That is the system catching an AOT violation at build time instead of at 2am in production. Citation: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings>.

## 8. Building locally with `dotnet publish` and the built-in container support

You do not strictly need a Dockerfile at all for the ASP.NET Core service: the .NET SDK can build an OCI image directly from `dotnet publish`, using the same chiseled base, without Docker installed:

```bash
dotnet publish src/Workshop.Api/Workshop.Api.csproj \
  -c Release \
  /t:PublishContainer \
  /p:ContainerBaseImage=mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled \
  /p:ContainerRepository=workshop-api \
  /p:ContainerImageTag=dev
```

This is convenient and it is *not* what we ship in CI, for one reason: **the Dockerfile is the explicit, reviewable record of what is in the image.** A `Dockerfile` in the repo is a thing a reviewer reads in a PR and a thing the pipeline builds identically. The `PublishContainer` target is great for a quick local image; the Dockerfile is the source of truth for the deploy. We cover both because you will use both — `PublishContainer` for a fast inner loop, the Dockerfile for CI. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/publish-as-container>.

## 9. The full local stack — `docker compose` for the dependencies

The Workshop API does not run alone: it needs PostgreSQL (EF Core persistence and the Dapper analytics), Keycloak (OIDC), and, in the harden week, the observability stack (Grafana, Loki, Tempo). For the inner loop you bring those up with `docker compose` so the API container talks to real dependencies on a Docker network, exactly as it will in production — the difference being that in production those are managed services and locally they are containers. The compose file is part of the repo because "how do I run the dependencies" is a question every contributor asks once:

```yaml
# docker-compose.yml — the local dependency stack
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: workshop
      POSTGRES_USER: workshop
      POSTGRES_PASSWORD: dev-only-not-a-secret
    ports: ["5432:5432"]
    healthcheck:                                  # so `depends_on` can wait for ready
      test: ["CMD-SHELL", "pg_isready -U workshop"]
      interval: 5s
      timeout: 3s
      retries: 10

  keycloak:
    image: quay.io/keycloak/keycloak:25.0
    command: ["start-dev", "--import-realm"]
    environment:
      KC_BOOTSTRAP_ADMIN_USERNAME: admin
      KC_BOOTSTRAP_ADMIN_PASSWORD: admin
    volumes:
      - ./infra/keycloak/realm-export.json:/opt/keycloak/data/import/realm.json:ro
    ports: ["8081:8080"]

  api:
    build:
      context: .
      dockerfile: Dockerfile            # the chiseled image from §3 — same one CI builds
    environment:
      ConnectionStrings__Workshop: "Host=postgres;Database=workshop;Username=workshop;Password=dev-only-not-a-secret"
      Oidc__Authority: "http://keycloak:8080/realms/workshop"
      ASPNETCORE_ENVIRONMENT: Development
    ports: ["8080:8080"]
    depends_on:
      postgres:
        condition: service_healthy      # wait for pg_isready, not just "started"
```

Two things to internalize from this file. First, **the API service builds the same chiseled Dockerfile CI builds** — you are not maintaining a separate "dev" image; the thing you run locally is byte-for-byte the thing the pipeline ships, which is the whole point of containers. Second, **`depends_on` with `condition: service_healthy`** waits for PostgreSQL to actually accept connections (via its `pg_isready` healthcheck), not merely for the container to start — the classic flake where the API races the database to the connection is solved by the healthcheck, not by a `sleep`. Citation: <https://docs.docker.com/compose/how-tos/startup-order/>. The integration tests (Testcontainers) do not use this compose file — they spin their own ephemeral containers — but for hands-on-keyboard development against the running API, `docker compose up` is the one command.

## 10. BuildKit, cache mounts, and faster restores

Modern `docker build` uses **BuildKit** (the default since Docker 23) and BuildKit gives you one more caching lever beyond layer caching: a **cache mount** for the NuGet package cache, which persists *across builds* even when the restore layer itself is invalidated. The syntax is a `RUN --mount`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Workshop.Api/Workshop.Api.csproj", "src/Workshop.Api/"]
# The cache mount keeps ~/.nuget/packages between builds. Even if the restore
# layer is invalidated (a .csproj changed), the already-downloaded packages are
# reused from the cache mount instead of re-fetched from nuget.org.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "src/Workshop.Api/Workshop.Api.csproj"
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "src/Workshop.Api/Workshop.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false
```

The distinction between *layer caching* (§3) and a *cache mount* (here) is worth holding clearly: layer caching reuses an entire layer when its inputs are unchanged; a cache mount is a persistent scratch directory that survives even when the layer is rebuilt. For NuGet they compound — layer caching skips the restore entirely when no `.csproj` changed; the cache mount makes the restore fast when it *does* run. In CI, the GitHub Actions cache (`cache-from: type=gha` in Lecture 2) is the runner-side equivalent. Citation: <https://docs.docker.com/build/cache/optimize/>.

## 11. Multi-arch: ARM is not a someday problem

Your laptop may be an Apple Silicon (arm64) Mac; your CI runner is amd64; Azure Container Apps runs amd64; a Raspberry Pi or an AWS Graviton box is arm64. If you `docker build` on the Mac and push, you push an arm64 image that will not run on the amd64 ACA host — a confusing "exec format error" at startup. The fix is to build a **multi-arch** image with `docker buildx`, which produces a manifest list pointing at one image per architecture:

```bash
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  -t ghcr.io/your-org/polyglot-workshop:dev \
  --push .
```

The .NET base images are already multi-arch, so the only thing you do is ask buildx for both platforms. In the pipeline (Lecture 2) `docker/build-push-action` takes a `platforms:` input that does the same. For the capstone you can ship amd64 only (it is what ACA and Fly.io run), but the moment someone tries to run your image on an arm64 host, the single-arch image bites them — so building both is cheap insurance. Citation: <https://docs.docker.com/build/building/multi-platform/>.

## 12. Reading the layers — `docker history` and `dive`

When an image is unexpectedly large, do not guess. `docker history` shows every layer and its size:

```bash
docker history workshop-api:dev
# IMAGE          CREATED BY                                      SIZE
# a1b2c3d4e5f6   ENTRYPOINT ["dotnet" "Workshop.Api.dll"]        0B
# <missing>      COPY /app/publish . # buildkit                  6.2MB   <-- your app
# <missing>      USER $APP_UID                                   0B
# <missing>      /bin/sh -c #(nop) ... aspnet base layers ...    112MB   <-- the runtime
```

The `dive` tool (<https://github.com/wagoodman/dive>) is the interactive version — it shows, per layer, which files were added and flags wasted space (files added in one layer and deleted in a later one, which still ship). For the capstone you want one number in your head: **how big is the layer that is *your code*?** For the Workshop API it should be single-digit megabytes; if it is hundreds, you are copying something you should not — usually a missing `.dockerignore` letting `bin/obj` through.

## 10. What this lecture earns you for the capstone

By the end of this lecture you can produce two images: a ~118 MB chiseled ASP.NET Core image for the backend (the gRPC + Minimal API + admin host), built by a multi-stage Dockerfile that restores before it copies source, runs non-root, and carries no SDK; and a ~34 MB Native AOT image for the analytics export CLI, built on `runtime-deps`, with source-generated JSON and a build that fails on any AOT warning. These are the artifacts Lecture 2's pipeline builds and pushes, and they are the things that scale-from-zero quickly on the free tier you deploy to. The Dockerfile is the contract; the pipeline is what enforces it on every push.

> **Citations recap.** .NET containerization guide: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>. Image catalog and `$APP_UID`: <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>. Native AOT: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>. ASP.NET Core AOT support: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot>. Source-generated JSON: <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation>. `dotnet/dotnet-docker`: <https://github.com/dotnet/dotnet-docker>. OCI image spec: <https://github.com/opencontainers/image-spec>.
