# Week 15 Exercise Solutions

These are the worked solutions to the four exercises. Each shows the canonical implementation, the verification output the grader looks for, and the most common ways the exercise gets done wrong. Read your own solution first; check it against the canonical one second. The point of this file is not to be copied — it is to surface the patterns and the failure modes so you recognize them in your own pipeline when it goes red. The four exercise stubs are `exercise-01-multi-stage-dockerfile.dockerfile`, `exercise-02-native-aot-cli.dockerfile`, `exercise-03-actions-build-test.yml`, and `exercise-04-actions-deploy-oidc.yml`; the canonical answers below correspond to each.

---

## Exercise 01 — The multi-stage Dockerfile for `Workshop.Api`

The canonical solution is the Lecture 1 multi-stage Dockerfile: an `sdk:9.0` build stage that copies the `.csproj` files and restores *before* copying the source (the layer-cache trick), publishes in Release, and a chiseled `aspnet:9.0-noble-chiseled` runtime stage that copies only `/app/publish`, runs as the non-root `$APP_UID`, exposes 8080, and `ENTRYPOINT`s `dotnet Workshop.Api.dll`. A `.dockerignore` excludes `bin/`, `obj/`, `.git/`, and local secrets.

The canonical file, in full, so you can diff your own against it line for line:

```dockerfile
# syntax=docker/dockerfile:1
# --- build stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the project files first, then restore. This layer is CACHED on
# any rebuild that does not touch a .csproj — the single biggest build-time win.
COPY PolyglotWorkshop.sln .
COPY src/Workshop.Api/Workshop.Api.csproj           src/Workshop.Api/
COPY src/Workshop.Contracts/Workshop.Contracts.csproj src/Workshop.Contracts/
RUN dotnet restore src/Workshop.Api/Workshop.Api.csproj

# Now copy the rest of the source. Editing a .cs file invalidates from here,
# not from the restore above.
COPY . .
RUN dotnet publish src/Workshop.Api/Workshop.Api.csproj \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

# --- runtime stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Workshop.Api.dll"]
```

`/p:UseAppHost=false` drops the native apphost executable from the publish output — the chiseled image launches via `dotnet Workshop.Api.dll`, so the apphost is dead weight. The two-line `.csproj` copy is the load-bearing part of the cache trick; if you `COPY . .` before restoring, every source edit re-downloads NuGet.

### Verification output

Running the eight verification steps in order should produce:

1. `docker build -t workshop-api:local -f src/Workshop.Api/Dockerfile .` succeeds; the first build restores, a second build after editing a `.cs` file reuses the restore layer (look for `CACHED` on the restore step).
2. `docker run --rm -p 8080:8080 -e ConnectionStrings__Workshop=... workshop-api:local` starts and logs the Serilog "Now listening on: http://[::]:8080" line.
3. `curl -s http://localhost:8080/healthz` returns `Healthy` (liveness; does not touch the DB).
4. `curl -s http://localhost:8080/readyz` returns `Healthy` only when Postgres and Keycloak are reachable; otherwise it returns 503 with the failing check named — that is correct behaviour, not a bug.
5. `docker images workshop-api:local --format "{{.Size}}"` prints ~226 MB for the `aspnet:9.0` base.
6. Switching the runtime base to `aspnet:9.0-noble-chiseled` drops it to ~113 MB.
7. `docker run --rm workshop-api:chiseled id` (if you try it) fails — there is no shell in the chiseled image. That is expected; you observe via logs, not a shell.
8. `docker history workshop-api:chiseled` shows the published-output layer as the largest, with no SDK layers in the final image.

If the image is ~800 MB, you built single-stage (`FROM sdk:9.0` for the final stage) — the most common mistake. The final `FROM` must be a runtime base, not the SDK.

The size numbers stack up like this, and the grader expects roughly these proportions (your absolute MB will drift by SDK patch level):

```text
sdk:9.0 (single-stage)        ~810 MB   <- SDK + restore cache + publish, all shipped
aspnet:9.0  (multi-stage)     ~226 MB   <- managed runtime + ICU + your DLLs
aspnet:9.0-noble-chiseled     ~113 MB   <- no shell, no apt, no package manager
runtime-deps:9.0 (AOT, ex02)   ~28 MB   <- native deps only, see Exercise 02
```

The jump from 810 to 226 is the multi-stage win (the SDK never enters the final image); the jump from 226 to 113 is the chiseled win (the OS surface shrinks to almost nothing). They are two independent levers and the table proves you pulled both.

Confirm the layering with `docker history`:

```text
$ docker history workshop-api:chiseled --format "{{.Size}}\t{{.CreatedBy}}" | head
0B        ENTRYPOINT ["dotnet" "Workshop.Api.dll"]
0B        ENV ASPNETCORE_HTTP_PORTS=8080
0B        EXPOSE map[8080/tcp:{}]
0B        USER 1654
2.1MB     COPY /app/publish . # buildkit          <- your code + deps
0B        WORKDIR /app
111MB     (chiseled base layers)
```

The published-output layer (`COPY /app/publish .`) is the only layer you own; everything below it is the immutable base. If you see an SDK-sized layer above the base, an SDK stage leaked into `final`.

### Common stumbles

The build that "works locally but not in CI" is almost always a `.dockerignore` problem: a stale `obj/` from your host gets copied into the build context and poisons the restore with host paths. Excluding `**/obj/` and `**/bin/` is mandatory, not cosmetic.

If the container starts but `/healthz` connection-refuses, you bound to the wrong port. The .NET 8+ images default Kestrel to 8080 because a non-root user cannot bind 80; set `ASPNETCORE_HTTP_PORTS=8080` and `EXPOSE 8080`, and map `-p 8080:8080`. Mapping `-p 8080:80` connects host 8080 to a container port nothing is listening on.

If the build fails with a permission error writing to `/app`, you switched to `USER $APP_UID` *before* the `COPY --from=build`, so the non-root user could not write the destination. Put the `USER` line after the copy, or `COPY --chown`. (The Lecture 1 ordering puts `USER` before `COPY` and works because the `app` user owns `/app` in the base image; if you changed `WORKDIR`, you may need `--chown`.)

A subtler one: the second build is *not* faster even though your `COPY` order looks right. The cause is almost always a dirty build context — `bin/` and `obj/` from a host `dotnet build` get copied in by `COPY . .`, their timestamps change every local build, and that busts the cache on the `COPY . .` layer (everything after `restore`). The `.dockerignore` is what keeps the context clean; verify it is being read with `docker build` printing a small "transferring context" size (a few MB, not hundreds). If the transferred context is 400 MB, your `.dockerignore` is missing or in the wrong directory — it must sit next to the build context root, not next to the Dockerfile if those differ.

One more, specific to the chiseled base: there is no `curl`, no `wget`, and no shell, so a `HEALTHCHECK CMD curl ...` baked into the Dockerfile will silently never pass. On chiseled images you do health checks from the orchestrator (Container Apps probes hit `/healthz` over HTTP from outside the container), not from an in-image `HEALTHCHECK`. Removing the in-image `HEALTHCHECK` on a chiseled base is correct, not a regression.

---

## Exercise 02 — The Native AOT analytics CLI

The canonical solution publishes `Workshop.AnalyticsExport` with `<PublishAot>true</PublishAot>`, a source-generated `JsonSerializerContext` (reflection-based JSON is not AOT-safe), `-r linux-x64`, on a `runtime-deps:9.0-noble-chiseled` runtime stage with the native toolchain (`clang`, `zlib1g-dev`) installed in the build stage.

The canonical AOT Dockerfile — note the native toolchain in the *build* stage and the `runtime-deps` (not `aspnet`) base in the final stage:

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# Native AOT compiles to native code, so the build stage needs a C toolchain.
RUN apt-get update && apt-get install -y --no-install-recommends \
      clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY src/Workshop.AnalyticsExport/Workshop.AnalyticsExport.csproj src/Workshop.AnalyticsExport/
RUN dotnet restore src/Workshop.AnalyticsExport/Workshop.AnalyticsExport.csproj -r linux-x64
COPY . .
RUN dotnet publish src/Workshop.AnalyticsExport/Workshop.AnalyticsExport.csproj \
      -c Release -r linux-x64 --no-restore \
      /p:PublishAot=true -o /app/publish

# runtime-deps: native libs only, NO managed runtime — the AOT binary is self-contained native code.
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish/Workshop.AnalyticsExport .
USER $APP_UID
ENTRYPOINT ["./Workshop.AnalyticsExport"]
```

The `ENTRYPOINT` is the native executable directly (`./Workshop.AnalyticsExport`), not `dotnet Something.dll` — there is no `dotnet` host in a `runtime-deps` image because AOT does not need one. That is the whole point: the binary *is* the program.

### Verification output

1. `dotnet publish ... -r linux-x64 -c Release` completes with **zero `IL2xxx`/`IL3xxx` trim or AOT warnings**. Any such warning is a latent runtime failure — treat it as an error.
2. The Native AOT Docker image (`runtime-deps` base) measures ~28 MB.
3. A framework-dependent JIT build of the same tool measures ~226 MB; a self-contained JIT build ~95 MB.
4. Cold-start timing (`time docker run --rm workshop-analytics:aot --help`) shows ~35 ms for AOT vs ~480 ms for the framework-dependent build. Your absolute numbers will differ; the ~10x shape is the point.
5. The tool runs the analytics export against a real Postgres connection string and writes valid CSV/JSON.

The cold-start gap comes from what each binary does at process start. The JIT build must load the CLR, JIT-compile the startup path, and warm the type system before your `Main` runs meaningful work; the AOT binary's code is already native, so process start is "map the executable, jump to entry point." Measured on a quiet laptop:

```text
                          image     cold start (--help)   what runs at startup
framework-dependent JIT   ~226 MB   ~480 ms               CLR load + JIT of startup path
self-contained   JIT      ~95 MB    ~430 ms               bundled CLR load + JIT
Native AOT                ~28 MB    ~35 ms                 mmap + jump to native entry
```

This is exactly why the analytics CLI — which is invoked cold on a schedule and exits — is the right place to spend the AOT effort, and why the long-lived `Workshop.Api` host is not (its startup cost is paid once and amortized over the life of the revision). The challenge-01 measurement table makes you prove this rather than take it on faith.

### Common stumbles

The classic AOT failure is the JSON one: using `JsonSerializer.Serialize(obj)` with reflection metadata. It compiles, publishes with an `IL2026`/`IL3050` warning you ignored, and throws `NotSupportedException` at runtime in the AOT binary. The fix is the `[JsonSerializable]` partial context and `JsonSerializer.Serialize(obj, AnalyticsJsonContext.Default.LessonCompletionRowArray)`.

The "it won't cross-compile" stumble: publishing `-r linux-x64` from an Apple-silicon (arm64) host needs either a matching cross-toolchain or building inside the `linux/amd64` Docker build stage (which the Dockerfile does — the SDK image runs the publish on the target architecture under emulation or a native runner). Doing the AOT publish *inside* the Dockerfile build stage sidesteps the host-architecture problem entirely; that is why the exercise publishes in the container, not on the host.

The "why is EF Core not AOT" question: EF Core relies on runtime model building and expression compilation that AOT forbids. The analytics CLI uses **Dapper** (the Week 13 analytics path) precisely because Dapper's lightweight mapping is far friendlier to trimming than EF Core's. That is the design reason the analytics surface was Dapper, not EF Core — it pays off here.

The canonical project shape for the AOT-safe serialization, in case yours throws at runtime:

```csharp
// AnalyticsJsonContext.cs — every type you serialize must be declared here.
[JsonSerializable(typeof(LessonCompletionRow[]))]
[JsonSerializable(typeof(EnrollmentSummary))]
internal partial class AnalyticsJsonContext : JsonSerializerContext;

// Use the generated metadata, NOT the reflection overload:
var json = JsonSerializer.Serialize(
    rows, AnalyticsJsonContext.Default.LessonCompletionRowArray);
```

The tell that you got it right: the publish is silent (no `IL` warnings) and `Workshop.AnalyticsExport --help` returns in tens of milliseconds, not hundreds.

---

## Exercise 03 — The GitHub Actions build-and-test workflow

The canonical solution is the Lecture 2 `test` job: `actions/checkout@v4`, `actions/setup-dotnet@v4` pinned to `9.0.x`, `dotnet restore`, `dotnet build -c Release --no-restore`, then `dotnet test tests/Workshop.IntegrationTests/... --no-build` with a `.trx` logger and coverage, and an `if: always()` artifact upload.

The canonical `test` job, in full:

```yaml
name: ci
on:
  push:
    branches: [main]
  workflow_dispatch:

jobs:
  test:
    runs-on: ubuntu-latest          # ships a Docker daemon; Testcontainers finds it
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - run: dotnet restore PolyglotWorkshop.sln
      - run: dotnet build PolyglotWorkshop.sln -c Release --no-restore
      - name: Integration tests (Testcontainers: Postgres + Keycloak)
        run: >
          dotnet test tests/Workshop.IntegrationTests/Workshop.IntegrationTests.csproj
          -c Release --no-build
          --logger "trx;LogFileName=results.trx"
          --collect:"XPlat Code Coverage"
          --results-directory ./test-results
      - name: Upload test results
        if: always()                # upload even when the suite is red
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: ./test-results
```

The `if: always()` on the upload is the line learners most often omit, and it is exactly the line you want on a red run: when CI fails, the `.trx` and the coverage XML are the evidence, and they only exist as an artifact if the upload runs *despite* the failed test step.

### Verification output

1. The workflow triggers on push to `main` and on `workflow_dispatch`.
2. The `test` job goes green; the log shows Testcontainers pulling `postgres:16` and the Keycloak image, running the suite, and ripping the containers down (Ryuk reaper line).
3. The `.trx` artifact is downloadable from the run summary, **even on a failed run** (that is what `if: always()` buys you).
4. Deliberately break a test; the job goes red and the publish/deploy jobs (if present) do not run — `needs: test` held the line.

### Common stumbles

The "Testcontainers can't reach Docker" failure in CI is almost never real on GitHub-hosted `ubuntu-latest` (it ships a daemon). When it appears, it is usually because someone added a `container:` to run the job *inside* a container without mounting the Docker socket — Docker-in-Docker. Run the job directly on the runner, not in a job container, and Testcontainers finds the host daemon.

The "tests pass locally, time out in CI" stumble is the image pull: the first CI run pulls `postgres:16` and Keycloak cold, which can exceed a short Testcontainers startup timeout. Either raise the wait strategy timeout or accept the slower first run; subsequent runs hit the runner's image cache. Do not "fix" it by skipping integration tests in CI — that defeats the entire deploy contract.

`--no-build` failing with "test project was not built" means the `dotnet build` step built the solution but the test project is excluded from `PolyglotWorkshop.sln`, or you built a different configuration than you tested. Build and test the same `-c Release`.

---

## Exercise 04 — The deploy job with GitHub OIDC

The canonical solution is the Lecture 2 `deploy` job: `permissions: id-token: write`, `azure/login@v2` with `client-id`/`tenant-id`/`subscription-id` (no secret), the gated migration step, and `azure/container-apps-deploy-action@v2` deploying the `:<github.sha>` tag, gated behind a `production` environment.

The canonical `deploy` job, in full:

```yaml
  deploy:
    needs: [test, publish]          # never deploy on red tests or a missing image
    runs-on: ubuntu-latest
    environment: production         # required-reviewer / wait-timer gate lives here
    permissions:
      id-token: write               # MINT the OIDC token — the #1 omission
      contents: read
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2         # NO AZURE_CREDENTIALS secret — three IDs only
        with:
          client-id:       ${{ vars.AZURE_CLIENT_ID }}
          tenant-id:       ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: Apply migrations (gated — deploy aborts if this fails)
        run: ./efbundle --connection "${{ secrets.WORKSHOP_DB_CONNECTION }}"
      - name: Deploy revision pinned to the commit SHA
        uses: azure/container-apps-deploy-action@v2
        with:
          containerAppName: workshop-api
          resourceGroup: rg-workshop
          imageToDeploy: ghcr.io/${{ github.repository }}/workshop-api:${{ github.sha }}
```

The Azure side is set up once, out of band, and is the half of OIDC that lives outside the repo:

```bash
# Federate the GitHub repo's main branch to an Entra app registration.
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:your-org/PolyglotWorkshop:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

The `subject` string is the security boundary: GitHub mints a token whose `sub` claim is `repo:your-org/PolyglotWorkshop:ref:refs/heads/main`, Azure accepts it only if it matches this federated credential exactly. No password, no `AZURE_CREDENTIALS` JSON, nothing to leak or rotate. The three `vars.AZURE_*` values are identifiers, not secrets — they are useless without a token whose subject Azure trusts.

### Verification output

1. The job authenticates to Azure with no `AZURE_CREDENTIALS` secret in the repo — only the three non-secret IDs.
2. The migration job runs `efbundle` and exits 0; the deploy proceeds. (Force it to exit 1 once — the deploy must not run.)
3. `azure/container-apps-deploy-action` creates a new revision pinned to the `:<sha>` tag; `az containerapp revision list` shows it active with 100% traffic after the readiness probe passes.
4. `curl https://<fqdn>/readyz` returns 200 from the public URL.
5. The `production` environment shows the required-reviewer approval (if you configured one) before the deploy step ran.

### Common stumbles

The single most common OIDC failure: omitting `permissions: id-token: write` on the deploy job. The symptom is `azure/login` failing with "Unable to get ACTIONS_ID_TOKEN_REQUEST_URL" or "Not all values are present." It is a *job-level* permission; setting it at the workflow level is not enough if a job overrides `permissions`.

The second most common: the federated-credential **subject does not match the branch**. The error names the presented subject (e.g. `repo:org/PolyglotWorkshop:ref:refs/heads/main`); compare it character-for-character to what `az identity federated-credential create --subject` configured. Deploying from a tag or a PR (different `sub`) is correctly rejected — that is the security boundary working, not a bug. If you want tag deploys, add a second federated credential for `ref:refs/tags/*` or `environment:production`.

The third: deploying the `:latest` tag instead of `:<sha>`. It "works" but you have lost the ability to say which build is in production and to roll back to a specific image. Deploy the immutable SHA tag; reserve `latest` for convenience pulls.

The fourth: the new revision deploys but never takes traffic because its **readiness probe fails** — usually because `/readyz` depends on Keycloak and the OIDC client secret in the Container Apps secret store is stale or unset. Check `az containerapp logs show` for the readiness-check failure; rotate the secret per the runbook if needed. This is the probe gate doing exactly its job: a misconfigured revision does not get traffic.

### The readiness probe and migration-on-deploy, in detail

Two pieces of the deploy job are easy to wire wrong because they *look* like they work in dev and fail only under a real rollout. Both matter for grading, so the canonical shapes:

**The probes.** Liveness (`/healthz`) and readiness (`/readyz`) are separate endpoints with separate dependency graphs. Liveness answers "is the process alive" and must touch nothing external — if `/healthz` queried Postgres and Postgres hiccuped, the platform would kill an otherwise-healthy process and start a restart storm. Readiness answers "should this revision receive traffic" and *does* check Postgres and Keycloak. The ASP.NET Core wiring:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connString, name: "postgres", tags: ["ready"])
    .AddUrlGroup(new Uri($"{keycloakBase}/.well-known/openid-configuration"),
                 name: "keycloak", tags: ["ready"]);

app.MapHealthChecks("/healthz", new() { Predicate = _ => false });          // liveness: no checks
app.MapHealthChecks("/readyz",  new() { Predicate = c => c.Tags.Contains("ready") }); // readiness: DB + Keycloak
```

The Container Apps probe spec points its `readinessProbe` at `/readyz`; the platform polls it and shifts traffic to the new revision only after it returns 200. A revision whose `/readyz` never passes is held at zero traffic — quarantined, not crashed — which is the deploy safety net working.

**Migration-on-deploy.** The migration is its own step that runs *before* the deploy step and gates it (`efbundle` non-zero exit aborts the deploy). It is not run on app startup in production: startup-migration races every replica against the same schema and couples a slow migration to the readiness deadline. The bundle is built in CI from the checked-in migrations:

```bash
dotnet ef migrations bundle --self-contained -r linux-x64 -o efbundle \
  --project src/Workshop.Api
```

`efbundle` is a single self-contained executable carrying the migrations; it runs once, applies what is pending, exits 0. Because the migrations are **expand-only** (Lecture 3), the new schema is readable by the *old* revision too — which is precisely what makes a one-command rollback to the previous revision safe: that revision never sees a column it does not understand.

### The end-to-end smoke test

When all four exercises are done, the whole pipeline runs from a single push. The smoke test the grader runs:

```bash
# 1. Make a trivial change and push to main.
git commit --allow-empty -m "deploy: smoke test" && git push origin main

# 2. Watch the run go green: test -> publish -> migrate -> deploy.
gh run watch

# 3. The deployed URL answers.
curl -s https://<your-fqdn>/readyz          # -> Healthy (200)

# 4. The new revision carries the pushed SHA and 100% traffic.
az containerapp revision list -n workshop-api -g rg-workshop \
  -o table --query "[?properties.active].{rev:name, traffic:properties.trafficWeight}"
```

If all four steps pass, the deploy contract holds: one push to `main` reached a live URL with the tests green. That is the line the whole week builds toward, and the line the capstone defense opens with. If any step is red, the failing job names the layer — read it top down (test, then publish, then deploy) rather than guessing; the four exercises above each cover one of those layers and its characteristic failures.

---

## Synthesis — how the four exercises connect

The four exercises are the four layers of the deploy pipeline, bottom to top:

- **Exercise 01** produced the **artifact**: a hardened, multi-stage, chiseled image that is the *same* `Workshop.Api` you built in Weeks 13–14, now shrunk from ~810 MB of build detritus to ~113 MB of exactly-what-runs.
- **Exercise 02** produced the **fast cold-start companion**: a Native AOT analytics CLI proving you know which workloads earn AOT (cold, short-lived, Dapper) and which do not (the long-lived EF Core host).
- **Exercise 03** produced the **gate**: a CI `test` job that boots the real host against real Postgres and Keycloak via Testcontainers, so "the tests pass" means "the integrated system works," not "the mocks agree."
- **Exercise 04** produced the **delivery**: a secret-free OIDC deploy that gates on the tests, applies expand-only migrations, pins the immutable SHA, and lets the readiness probe decide when the revision is real.

Stacked, they are the deploy contract the capstone defense opens with: *one push to `main` runs the tests, builds the artifact, applies the migration, and reaches a live URL — with no human in the loop and no long-lived credential in the repo.* The exercises build that contract one layer at a time; the mini-project (the capstone defense) is where you stand in front of it, push to `main`, and watch a live URL answer while a grader asks you why each layer is shaped the way it is.

Read the patterns. Reproduce them. Then defend them.
