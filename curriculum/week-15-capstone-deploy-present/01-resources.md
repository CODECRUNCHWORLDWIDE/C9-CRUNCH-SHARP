# Week 15 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The GitHub Actions documentation is free. The Azure Container Apps free grant requires an Azure account (a credit card for identity verification, but the free grant does not bill a capstone-scale workload). The Fly.io documentation and free allowance are free. The `dotnet/dotnet-docker` and `docker/build-push-action` repositories are public. The SRE / runbook reading below is free to read online. No paywalled material is linked.

## Required reading (work it into your week)

### Containerizing .NET

- **Containerize a .NET app** — the canonical multi-stage Dockerfile walkthrough, SDK stage plus runtime stage:
  <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>
- **.NET container images (the official catalog)** — `sdk`, `aspnet`, `runtime`, `runtime-deps`, and the chiseled / noble variants:
  <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>
- **Chiseled Ubuntu images for .NET** — what "chiseled" removes and why, the `$APP_UID` non-root user, the `-extra` variant:
  <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images#ubuntu-chiseled-images>
- **`dotnet/dotnet-docker` on GitHub** — the source of every official tag, the sample Dockerfiles, the AOT samples:
  <https://github.com/dotnet/dotnet-docker>
- **`dotnet publish` reference** — every flag the build stage uses (`-c`, `-r`, `--self-contained`, `/p:PublishTrimmed`, `/p:PublishAot`):
  <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish>

### Native AOT

- **Native AOT deployment** — what it gives, what it costs, what it forbids; the publish command and the platform prerequisites:
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>
- **Native AOT in containers** — the cross-compile toolchain image and the `runtime-deps:9.0-noble-chiseled-aot` base:
  <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container#native-aot>
- **Trimming incompatibilities** — the reflection patterns AOT and trimming break, and the warnings that catch them:
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities>
- **ASP.NET Core support for Native AOT** — what works (Minimal APIs, the request-delegate generator) and what does not (MVC, Razor) under AOT:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot>

### GitHub Actions

- **GitHub Actions documentation (home)** — workflows, jobs, steps, triggers, the syntax reference:
  <https://docs.github.com/en/actions>
- **Workflow syntax for GitHub Actions** — `on`, `jobs`, `needs`, `permissions`, `env`, `if`, the full schema:
  <https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions>
- **Publishing Docker images to GitHub Packages** — pushing to `ghcr.io`, the `GITHUB_TOKEN` permissions, package visibility:
  <https://docs.github.com/en/actions/publishing-packages/publishing-docker-images>
- **`docker/build-push-action`** — the action that builds and pushes a multi-platform image with layer caching:
  <https://github.com/docker/build-push-action>
- **`docker/metadata-action`** — generating the SHA tag and the OCI labels from the Git context:
  <https://github.com/docker/metadata-action>
- **OIDC hardening: GitHub Actions to Azure** — federating CI to Azure with no long-lived client secret:
  <https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect>
- **`azure/login` action** — the OIDC login step that gets a short-lived Azure token in the runner:
  <https://github.com/Azure/login>

### Azure Container Apps

- **Get started with Azure Container Apps** — `az containerapp up`, the environment, ingress, the public URL:
  <https://learn.microsoft.com/en-us/azure/container-apps/get-started>
- **Deploy from a container registry** — pulling the image from `ghcr.io`, registry credentials, the `--image` flag:
  <https://learn.microsoft.com/en-us/azure/container-apps/containers>
- **Manage secrets in Azure Container Apps** — `--secrets`, referencing a secret from an env var, Key Vault references:
  <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>
- **Health probes in Azure Container Apps** — liveness, readiness, and startup probes; why the readiness probe gates traffic:
  <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>
- **Revisions in Azure Container Apps** — revision modes, `revision list`, `revision activate`, traffic splitting, the rollback story:
  <https://learn.microsoft.com/en-us/azure/container-apps/revisions>
- **Billing and the free grant** — the monthly free vCPU-seconds, GiB-seconds, and requests; scale-to-zero economics:
  <https://learn.microsoft.com/en-us/azure/container-apps/billing>
- **`az containerapp` CLI reference** — every subcommand the runbook uses:
  <https://learn.microsoft.com/en-us/cli/azure/containerapp>

### Fly.io (the secondary target)

- **Launch a .NET app on Fly.io** — `fly launch`, the generated `fly.toml`, `fly deploy`:
  <https://fly.io/docs/launch/>
- **`fly.toml` reference** — the build, env, http_service, and machine sections:
  <https://fly.io/docs/reference/configuration/>
- **Secrets on Fly.io** — `fly secrets set`, how secrets reach the running machine:
  <https://fly.io/docs/apps/secrets/>

### MAUI Android release

- **Publish a .NET MAUI app for Android with the CLI** — the keystore, `dotnet publish -f net9.0-android -c Release`, the signed APK / AAB:
  <https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli>
- **Create a signing keystore** — `keytool`, the alias, the storepass, the validity window:
  <https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli#create-a-keystore>
- **Sideload with `adb install`** — connecting the device, USB debugging, installing the APK:
  <https://developer.android.com/tools/adb>

### Testcontainers in CI

- **Testcontainers for .NET** — the library, the modules, the Docker-socket requirement:
  <https://dotnet.testcontainers.org/>
- **Testcontainers on CI** — the patterns for GitHub-hosted runners (the Docker daemon is present; Ryuk and resource reaping):
  <https://dotnet.testcontainers.org/test_environment/continuous_integration/>

## Recommended reading (runbooks, on-call, deploy discipline)

- **Google SRE Book — "Being On-Call"** — the canonical chapter on what on-call is and how to make it humane (free online):
  <https://sre.google/sre-book/being-on-call/>
- **Google SRE Book — "Emergency Response"** — severity, the rollback-first instinct, the incident roles:
  <https://sre.google/sre-book/emergency-response/>
- **Google SRE Workbook — "On-Call"** — the practical, "how do we actually run this" companion (free online):
  <https://sre.google/workbook/on-call/>
- **Atlassian: how to write an incident runbook** — a concrete template for the procedures a runbook holds:
  <https://www.atlassian.com/incident-management/devops/runbooks>
- **The Twelve-Factor App** — config in the environment, build/release/run separation, disposability; old but load-bearing:
  <https://12factor.net/>
- **OCI Image Specification** — what an image actually is, layers, the manifest, the config; read it once:
  <https://github.com/opencontainers/image-spec>

## Tools you will use

- **The .NET 9 SDK** — `dotnet publish`, the AOT toolchain, the MAUI workload (`dotnet workload install maui`).
- **Docker** (Docker Desktop, Colima, or Podman with the `docker` alias) — `docker build`, `docker run`, `docker image ls`, `dive` (optional) for inspecting layers.
- **The GitHub CLI (`gh`)** — `gh workflow run`, `gh run watch`, `gh run view --log` for driving and reading Actions from the terminal:
  <https://cli.github.com/>
- **The Azure CLI (`az`)** with the `containerapp` extension — `az login`, `az containerapp up`, `az containerapp logs show`, `az containerapp revision`.
- **`flyctl`** (the Fly.io CLI) for the fallback target.
- **`adb`** (Android Debug Bridge) for the MAUI sideload.
- **`cosign`** (challenge 2 only) — signing and verifying container images:
  <https://github.com/sigstore/cosign>

## A note on package and tool versions

- Base images: `mcr.microsoft.com/dotnet/sdk:9.0`, `mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled`, `mcr.microsoft.com/dotnet/runtime-deps:9.0-noble-chiseled` (and the `-aot` variant for the cross-compile build stage).
- Actions: `actions/checkout@v4`, `actions/setup-dotnet@v4`, `docker/login-action@v3`, `docker/build-push-action@v6`, `docker/metadata-action@v5`, `azure/login@v2`.
- The Azure CLI `containerapp` extension auto-installs on first use; pin it in CI with `az extension add --name containerapp` if you want reproducibility.
- Everything targets **.NET 9 / C# 13**, consistent with the rest of C9.
