# Week 15 — Quiz

Ten multiple-choice questions covering multi-stage Dockerfiles, image hardening, Native AOT, GitHub Actions, OIDC to Azure, Azure Container Apps, health/readiness probes, migration-on-deploy, and rollback. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — Why multi-stage

A `Workshop.Api` image built `FROM mcr.microsoft.com/dotnet/sdk:9.0` as a single stage is ~800 MB; the multi-stage build is ~113 MB. The size difference is mostly because:

- (A) The multi-stage build compresses the layers more aggressively.
- (B) The single-stage final image ships the full SDK (compilers, MSBuild, NuGet, source), none of which runs in production; the multi-stage final image is `FROM` a runtime base and carries only the published output.
- (C) The multi-stage build strips the .NET runtime, which the single-stage build keeps.
- (D) Single-stage images cannot use layer caching.

## Question 2 — The layer-cache trick

The Lecture 1 Dockerfile copies the `.csproj` files and runs `dotnet restore` *before* `COPY . .` of the source. The reason is:

- (A) `dotnet restore` requires the source files to be absent.
- (B) The restore layer is keyed on the project files, so editing a `.cs` file does not invalidate it — Docker reuses the cached restored packages and skips straight to publish.
- (C) It makes the final image smaller.
- (D) `COPY . .` cannot run before a `RUN` step.

## Question 3 — Running as non-root

`USER $APP_UID` in the runtime stage of the Dockerfile:

- (A) Is required for the container to start.
- (B) Runs the app as the non-root `app` user the .NET base image ships, so a process that breaks out of the application does not break out as root.
- (C) Makes the image smaller.
- (D) Is only meaningful on Windows containers.

## Question 4 — When NOT to use Native AOT

Which capstone binary is the *worst* candidate for Native AOT?

- (A) `Workshop.AnalyticsExport`, a Dapper CLI that starts cold each run.
- (B) A tiny CLI that does one HTTP GET and exits.
- (C) `Workshop.Api`, which uses EF Core, the gRPC server, and OIDC — all reliant on reflection and runtime code generation that AOT forbids or makes painful.
- (D) A CLI that serializes records with a source-generated `JsonSerializerContext`.

## Question 5 — The AOT JSON trap

A Native AOT CLI publishes with an `IL3050`/`IL2026` warning about `JsonSerializer.Serialize`, then throws `NotSupportedException` at runtime. The fix is:

- (A) Disable trimming with `PublishTrimmed=false`.
- (B) Catch the exception and fall back to `ToString()`.
- (C) Use a source-generated `[JsonSerializable]` `JsonSerializerContext` and serialize against its metadata, because reflection-based `System.Text.Json` is not AOT-safe.
- (D) Add `<PublishAot>false</PublishAot>` to the API project instead.

## Question 6 — Why no long-lived credential in CI

The Week 15 deploy job authenticates to Azure with GitHub OIDC rather than a stored service-principal secret. The primary reason is:

- (A) OIDC is faster than a stored secret.
- (B) A stored client secret does not expire on its own, grants subscription access to anyone who finds it (leaked logs, a malicious fork's run), and is one more thing to rotate; OIDC mints a token that lives only for the job, so there is nothing stored to leak or rotate.
- (C) Azure no longer accepts client secrets.
- (D) OIDC tokens never expire, which is more convenient.

## Question 7 — The most common OIDC failure

`azure/login@v2` fails with "Unable to get ACTIONS_ID_TOKEN_REQUEST_URL." The most likely cause is:

- (A) The Azure subscription is out of credit.
- (B) The deploy job is missing `permissions: id-token: write`, so GitHub does not mint the OIDC JWT.
- (C) The image tag is wrong.
- (D) The Dockerfile failed to build.

## Question 8 — Liveness vs readiness

A new revision of `Workshop.Api` deploys but receives zero traffic while the previous revision keeps serving. The most likely correct explanation is:

- (A) The deploy failed and should be retried.
- (B) Single-revision mode is enabled.
- (C) The new revision's readiness probe (`/readyz`, which checks the DB and Keycloak) is failing, so the platform correctly withholds traffic from it — the deploy gate doing its job.
- (D) The liveness probe is restarting the container in a loop.

## Question 9 — Why migrations are a gated step, not a startup hook

The recommended pattern runs EF Core migrations as a separate gated job (via `efbundle`) before the new revision takes traffic, rather than `db.Database.MigrateAsync()` at startup. The reason is:

- (A) `MigrateAsync` is deprecated in EF Core 9.
- (B) Startup migration can race when two replicas boot at once, and couples migration failure to app-startup failure (the readiness probe never passes and you cannot diagnose a shell-less container); a gated step isolates the migration and stops the deploy if it fails.
- (C) Migrations cannot run inside a container.
- (D) The gated step is faster.

## Question 10 — Rollback on Container Apps

A deploy is throwing 500s. The fastest correct mitigation on Azure Container Apps in multiple-revision mode is:

- (A) Rebuild the previous image and redeploy it through the full pipeline.
- (B) SSH into the container and revert the code.
- (C) Reweight ingress traffic to 100% on the previous (still-running) revision in one command — instant, with no image pull, cold start, or migration, because expand-only migrations keep the old revision's schema valid.
- (D) Delete the container app and recreate it.

---

## Answer key

- **Q1: (B).** A single-stage image built `FROM sdk:9.0` ships the entire build toolchain to production; the multi-stage final image is `FROM` a runtime base (`aspnet:9.0` or chiseled) and copies only `/app/publish`. The ~7x difference is the SDK and source that never run. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>.
- **Q2: (B).** The restore layer depends only on the `.csproj`/lock files; copying them and restoring before `COPY . .` means a source edit reuses the cached restore layer. Look for `CACHED` on the restore step in the build output. Citation: <https://docs.docker.com/build/cache/>.
- **Q3: (B).** `USER $APP_UID` runs the app as the non-root `app` user the .NET 8+ base images ship, so a container escape does not yield root. The cheapest hardening win, and the one most images skip. Citation: <https://learn.microsoft.com/en-us/dotnet/core/docker/container-security>.
- **Q4: (C).** `Workshop.Api` leans on EF Core (runtime model building), the gRPC server, and OIDC — all reflection/codegen-heavy, which AOT forbids or makes painful. The leaf CLIs that start cold and avoid reflection are the good AOT candidates. AOT the leaves, keep the long-lived host on JIT. Citation: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations>.
- **Q5: (C).** Reflection-based `System.Text.Json` is not AOT-safe; the publish warns (`IL2026`/`IL3050`) and the binary throws at runtime. The fix is a source-generated `JsonSerializerContext`. Disabling trimming (A) fights AOT, which implies trimming. Citation: <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation>.
- **Q6: (B).** A stored client secret is a standing liability — it does not expire, it leaks in logs, it grants subscription access, and it must be rotated. OIDC mints a short-lived federated token per job; nothing is stored, so nothing leaks or needs rotating. Citation: <https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect>.
- **Q7: (B).** GitHub only mints the OIDC JWT when the job has `permissions: id-token: write`. Without it, `azure/login` cannot get the token-request URL. It is a job-level permission. Citation: <https://github.com/Azure/login#login-with-openid-connect-oidc-recommended>.
- **Q8: (C).** A new revision that fails its readiness probe (`/readyz` gated on DB + Keycloak) is correctly withheld from traffic while the previous revision keeps serving. That is the deploy gate, not a failure to retry. Check the logs for the failing check. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>.
- **Q9: (B).** Startup migration races across replicas and couples migration failure to startup failure — the readiness probe never passes and a chiseled container has no shell to diagnose with. A gated `efbundle` step isolates the migration and stops the deploy chain on failure. Citation: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>.
- **Q10: (C).** In multiple-revision mode the previous revision is still running; rolling back is a one-command traffic reweight — instant, no rebuild, no cold start, no migration. Expand-only migrations keep the old revision's schema valid, which is what makes the rollback safe. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage>.

## Self-assessment

- 9-10: you can deploy, roll back, and defend the capstone without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact pages.
- 5-6: re-read all three lecture notes and redo the exercises, paying particular attention to OIDC and the liveness-vs-readiness distinction — those are the two that bite in the defense.
- 0-4: rewind to Lecture 1. The capstone defense assembles every pattern the quiz tests; it will not go well without the conceptual foundation. The deploy contract is not optional — it is 35% of the grade.
