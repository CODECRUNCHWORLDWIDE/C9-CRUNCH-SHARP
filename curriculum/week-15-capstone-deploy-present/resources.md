# Week 15 Resources — Capstone Deploy and Present

This is the canonical reading list for Week 15. Every URL has been opened, every CLI installed, every section referenced by the lectures, exercises, challenges, or the capstone defense. Read what you need when you need it; the lecture notes tell you which section of which document is load-bearing for the technique under discussion.

The list is grouped by the role the document plays in the deploy story — containerizing the app, Native AOT, GitHub Actions, OIDC and secret-free CI, Azure Container Apps, Fly.io, health and migrations, rollback, the runbook and on-call, the MAUI sideload, and adjacent reading. The "adjacent" section is the most valuable for the engineer who wants to outgrow the lectures; do not skip it because it sits last.

## Containerizing the .NET app

- **Containerize a .NET app (the canonical guide)** — <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>. The "build then publish" multi-stage Dockerfile for ASP.NET Core. Read this before Lecture 1; the rest of the week assumes the multi-stage pattern.
- **.NET container images** — <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>. The image variants — `sdk`, `aspnet`, `runtime`, `runtime-deps`, and the chiseled `-noble-chiseled` distroless-style images. Which to use as the build base and which as the runtime base.
- **Container security for .NET** — <https://learn.microsoft.com/en-us/dotnet/core/docker/container-security>. Running as the non-root `app` user (`$APP_UID`), the read-only-root and dropped-capabilities hardening steps.
- **The `dotnet/dotnet-docker` repository** — <https://github.com/dotnet/dotnet-docker>. The source of every official .NET image; the tag catalogue and the chiseled-image documentation at <https://github.com/dotnet/dotnet-docker/blob/main/documentation/ubuntu-chiseled.md>.
- **Docker build cache** — <https://docs.docker.com/build/cache/>. Why copying `.csproj` and restoring before `COPY . .` is the single biggest build-time win; the `type=gha` cache backend for CI at <https://docs.docker.com/build/cache/backends/gha/>.
- **`.dockerignore` and build context** — <https://docs.docker.com/build/concepts/context/#dockerignore-files>. Keeping `bin/`, `obj/`, `.git/`, and local secrets out of the image.
- **Docker Scout** — <https://docs.docker.com/scout/>. Image vulnerability scanning; used in challenge-01's stretch goal to show "smaller is safer" is a CVE-count claim, not just a size claim.

## Native AOT

- **Native AOT deployment** — <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>. What AOT gives, costs, and forbids; the `PublishAot` property; the limitations section is mandatory reading before you trust an AOT binary.
- **`System.Text.Json` source generation** — <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation>. The AOT-safe serialization path; the `[JsonSerializable]` `JsonSerializerContext` that replaces reflection-based JSON.
- **Trimming options and warnings** — <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options>. The `IL2xxx`/`IL3xxx` warnings, how to keep reflection-reached code with trimming attributes.
- **`runtime-deps` image** — <https://hub.docker.com/r/microsoft/dotnet-runtime-deps>. The native-dependencies-only base for self-contained and AOT binaries — no managed runtime.

## GitHub Actions

- **Building and testing .NET** — <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>. The canonical `setup-dotnet` + `restore`/`build`/`test` workflow; the starting point for the `test` job.
- **Workflow syntax** — <https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions>. `on:`, `jobs:`, `needs:`, `permissions:`, `outputs:` — the grammar of the pipeline.
- **`actions/checkout`** — <https://github.com/actions/checkout>. Pin `@v4`.
- **`actions/setup-dotnet`** — <https://github.com/actions/setup-dotnet>. Pin `@v4`; sets the `9.0.x` SDK on the runner.
- **`docker/build-push-action`** — <https://github.com/docker/build-push-action>. Build and push the image; `cache-from`/`cache-to: type=gha` for the layer cache. Pair with `docker/login-action` (<https://github.com/docker/login-action>) and `docker/metadata-action` (<https://github.com/docker/metadata-action>) for the SHA tag.
- **`docker/setup-buildx-action`** — <https://github.com/docker/setup-buildx-action>. BuildKit on the runner; required for the GHA cache backend and multi-arch.
- **Using environments for deployment** — <https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment>. Required reviewers, wait timers, and branch restrictions gating the `production` deploy.

## OIDC and secret-free CI

- **About security hardening with OpenID Connect** — <https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect>. Why OIDC removes the long-lived credential; the subject-claim model.
- **Configuring OIDC in Azure** — <https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure>. The end-to-end setup: federated credential, the `azure/login` step, the three non-secret IDs.
- **Workload identity federation (Entra)** — <https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation>. The Azure side: federating a managed identity or app registration to the GitHub issuer, scoping the subject to a branch.
- **`azure/login`** — <https://github.com/Azure/login>. The action; the `id-token: write` permission requirement is the single most common gotcha.

## Azure Container Apps

- **Get started with Container Apps** — <https://learn.microsoft.com/en-us/azure/container-apps/get-started>. The shortest path from an image to a public URL; `az containerapp up`.
- **Container Apps overview** — <https://learn.microsoft.com/en-us/azure/container-apps/overview>. The environment, ingress, and the free-tier allowance.
- **Revisions** — <https://learn.microsoft.com/en-us/azure/container-apps/revisions>. The revision model; single vs multiple mode; traffic weights.
- **Manage revisions** — <https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage>. The `az containerapp ingress traffic set` rollback command.
- **Blue/green deployment** — <https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment>. Canary weighting; promoting a revision after watching it on a slice of traffic.
- **Health probes** — <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>. Liveness, readiness, and startup probes; the readiness gate that withholds traffic from a broken revision.
- **Scale and scale-to-zero** — <https://learn.microsoft.com/en-us/azure/container-apps/scale-app>. The free-tier scale-to-zero behavior; why cold start matters.
- **Manage secrets** — <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>. Storing the OIDC client secret; the rotation procedure.
- **Log monitoring** — <https://learn.microsoft.com/en-us/azure/container-apps/log-monitoring>. `az containerapp logs show`, the Log Analytics workspace, where the Serilog JSON lands.
- **The `containerapp` Azure CLI** — <https://learn.microsoft.com/en-us/cli/azure/containerapp>. Every `az containerapp` subcommand the lectures and runbook use.
- **`azure/container-apps-deploy-action`** — <https://github.com/Azure/container-apps-deploy-action>. The deploy action used in the pipeline.

## Fly.io (secondary target)

- **Deploy on Fly.io** — <https://fly.io/docs/launch/deploy/>. `flyctl deploy`, the rolling and `bluegreen` strategies.
- **`fly.toml` reference** — <https://fly.io/docs/reference/configuration/>. The app config; the `[http_service.checks]` health checks.
- **Install `flyctl`** — <https://fly.io/docs/flyctl/install/>. The CLI.
- **Fly.io access tokens** — <https://fly.io/docs/security/tokens/>. Scoping a deploy token to one app so a leak cannot reach the account.

## EF Core migrations on deploy

- **Applying migrations (production)** — <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>. The startup-vs-gated discussion; the `dotnet ef migrations bundle` / `efbundle` pattern.
- **Managing schemas / migrations** — <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>. The expand-then-contract discipline for backward-compatible changes.
- **PostgreSQL Flexible Server storage** — <https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/concepts-storage>. The free-tier allowance; the scale-up command for the database-full runbook section.
- **PostgreSQL `VACUUM`** — <https://www.postgresql.org/docs/current/sql-vacuum.html>. Reclaiming space after the analytics-retention DELETE.

## The MAUI sideload

- **Publish a .NET MAUI Android app with the CLI** — <https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli>. Building the APK that the demo sideloads.
- **MAUI deployment overview** — <https://learn.microsoft.com/en-us/dotnet/maui/deployment/>. The platform targets; what "sideloadable" means on Android.

## The runbook and on-call

- **SRE workbook — playbooks** — <https://sre.google/workbook/playbooks/>. The operational-playbook chapter; the shape of a runbook someone else can follow.
- **SRE workbook — incident response** — <https://sre.google/workbook/incident-response/>. Acknowledge, assess severity, mitigate before you diagnose.
- **ASP.NET Core health checks** — <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks>. The tagged-checks pattern behind `/healthz` and `/readyz`.

## Adjacent reading — strongly recommended

- **"How .NET 8/9 ships smaller, faster containers"** — <https://devblogs.microsoft.com/dotnet/announcing-dotnet-chiseled-containers/>. The framework-team narrative on chiseled images; why distroless-style is the right default.
- **GitHub Actions security hardening** — <https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions>. Beyond OIDC: pinning actions to SHAs, the `GITHUB_TOKEN` permission model, untrusted-input handling.
- **OpenTelemetry .NET SDK** — <https://github.com/open-telemetry/opentelemetry-dotnet>. Carried from Week 14; the OTLP exporter is what ships traces to the deployed backend. The semantic conventions at <https://opentelemetry.io/docs/specs/semconv/> remain the trace vocabulary.
- **The Twelve-Factor App** — <https://12factor.net/>. Config in the environment, logs as event streams, stateless processes — the principles the whole deploy story rests on. Read factors III (config), XI (logs), and IX (disposability).
- **Azure Container Apps free tier and pricing** — <https://azure.microsoft.com/en-us/pricing/details/container-apps/>. The free monthly allowance; what scale-to-zero saves; the cost section the runbook references.

## Bookmarks worth saving past C9

- The containerize-a-.NET-app guide.
- The Azure Container Apps documentation hub.
- The GitHub Actions OIDC guide.
- The SRE workbook.
- The `dotnet/dotnet-docker` repository.
- The EF Core migrations-applying doc.

By the end of this week you should have all six pinned. Operating a deployed .NET service means moving between three or four of these per incident; the time saved by not re-googling at 3am is the runbook's whole reason for existing.
