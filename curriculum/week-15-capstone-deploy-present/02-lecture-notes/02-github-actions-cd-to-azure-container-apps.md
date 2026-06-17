# Lecture 2 — A GitHub Actions Pipeline: Build, Test, Publish, Deploy to Azure Container Apps (with a Fly.io Fallback) and Revision-Based Rollback

> **Time:** 2 hours. Read it with the YAML open in an editor and the GitHub Actions docs in a tab. **Prerequisites:** Lecture 1 (you can build both images). **Citations:** the GitHub Actions docs at <https://docs.github.com/en/actions>, the workflow syntax at <https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions>, the OIDC-to-Azure guide at <https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect>, the Azure Container Apps quickstart at <https://learn.microsoft.com/en-us/azure/container-apps/get-started>, the revisions doc at <https://learn.microsoft.com/en-us/azure/container-apps/revisions>, and Fly.io launch at <https://fly.io/docs/launch/>.

## 1. The four phases, and why they are four

A continuous-delivery pipeline for a containerized .NET service has four logical phases, and the discipline is keeping them honest about their dependencies:

1. **Build** — restore and compile. Fails on a compiler error or, because you treat warnings as errors, on a warning.
2. **Test** — run the xUnit unit tests and the Testcontainers integration tests (`WebApplicationFactory<T>` against ephemeral PostgreSQL and Keycloak). **A red test must stop the pipeline here.** This is the gate; everything downstream assumes it passed.
3. **Publish** — build the container image from the Dockerfile and push it to a registry, tagged with the **commit SHA** so the image is traceable to exactly one commit. Runs only if Test passed.
4. **Deploy** — tell Azure Container Apps to run a new revision of that exact, immutable image. Runs only if Publish pushed.

The ordering is expressed in GitHub Actions with `needs:`. The reason the phases are separate jobs and not one long script is failure isolation and feedback: when the pipeline goes red you want to see *which* phase failed at a glance, and you never want a test failure to produce a deployable image. **A build that does not pass its tests does not become an image. An image that was not built by the pipeline does not reach production.** Those two sentences are the whole philosophy. Citation: <https://docs.github.com/en/actions/using-jobs/using-jobs-in-a-workflow>.

## 2. OIDC federation: no cloud credentials in GitHub secrets

Before the YAML, the single most important security decision: **how does the runner authenticate to Azure to deploy?** The wrong-but-common answer is to create an Azure service-principal client secret and paste it into a GitHub Actions secret. That is a long-lived credential sitting in CI that can deploy to your cloud; if it leaks (and secrets leak), an attacker has standing deploy access until someone notices and rotates it.

The right answer is **OIDC federation**: GitHub Actions mints a short-lived OIDC token for the workflow run, Azure is configured to trust tokens from your specific repo and branch (a *federated credential*), and `azure/login` exchanges that token for a short-lived Azure access token that expires in an hour. **There is no long-lived secret anywhere.** You configure it once:

```bash
# 1) Create an app registration and a service principal for it
az ad app create --display-name "workshop-capstone-cd"
APP_ID=$(az ad app list --display-name "workshop-capstone-cd" --query "[0].appId" -o tsv)
az ad sp create --id "$APP_ID"

# 2) Grant it the role it needs, scoped to the resource group (least privilege)
az role assignment create \
  --assignee "$APP_ID" \
  --role "Contributor" \
  --scope "/subscriptions/$SUB_ID/resourceGroups/rg-workshop-capstone"

# 3) Federate: trust tokens from THIS repo on THIS branch only
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:your-org/polyglot-workshop:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Now the only "secrets" the workflow needs are non-sensitive identifiers — the client ID, the tenant ID, the subscription ID — which are not credentials; a leaked client ID cannot authenticate anything without the federated trust you scoped to one repo and branch. Citation: <https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect>.

## 3. The full workflow

Here is `.github/workflows/deploy.yml` for the Polyglot Workshop, complete and annotated. It triggers on a push to `main` and on PRs (PRs run build + test only; they do not deploy).

```yaml
name: cd

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

# Least privilege at the top: most jobs need nothing. The deploy job requests
# id-token (for the OIDC handshake to Azure) and packages (to push to ghcr.io).
permissions:
  contents: read

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}        # your-org/polyglot-workshop
  DOTNET_VERSION: "9.0.x"

jobs:
  # ---- Phase 1+2: build and test in one job; Testcontainers needs Docker, which
  # the ubuntu-latest runner already has running. The integration tests spin up
  # PostgreSQL and Keycloak containers and tear them down. ----
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      # Cache the NuGet restore so we don't re-download the world every run.
      - uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ hashFiles('**/Directory.Packages.props', '**/*.csproj') }}
          restore-keys: nuget-

      - name: Restore
        run: dotnet restore

      # Warnings are errors; a sloppy commit fails here, not in review.
      - name: Build
        run: dotnet build --configuration Release --no-restore -warnaserror

      # xUnit unit tests + WebApplicationFactory<T> + Testcontainers integration
      # tests. Testcontainers reaches the runner's Docker daemon over the socket;
      # no extra setup is needed on ubuntu-latest. Citation:
      # https://dotnet.testcontainers.org/test_environment/continuous_integration/
      - name: Test
        run: dotnet test --configuration Release --no-build --logger "trx" --results-directory ./test-results

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: ./test-results

  # ---- Phase 3: publish the image. `needs: build-test` makes a red test block
  # this job entirely. Only runs on a push to main, never on a PR. ----
  publish:
    needs: build-test
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write          # to push to ghcr.io
    outputs:
      image: ${{ steps.meta.outputs.tags }}
    steps:
      - uses: actions/checkout@v4

      - uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}   # auto-provided, scoped to this repo

      # The SHA tag is the whole point: the image is traceable to one commit and
      # is IMMUTABLE. We never deploy :latest to production.
      - id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=sha,format=long
            type=ref,event=branch

      - uses: docker/build-push-action@v6
        with:
          context: .
          file: ./Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

  # ---- Phase 4: deploy the SHA-tagged image to Azure Container Apps. OIDC login,
  # no long-lived secret. `needs: publish` chains it after the push. ----
  deploy:
    needs: publish
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      id-token: write          # REQUIRED for the OIDC token exchange
    environment:
      name: production
      url: ${{ steps.deploy.outputs.url }}
    steps:
      - name: Azure login (OIDC, no secret)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Deploy a new revision to Azure Container Apps
        id: deploy
        run: |
          IMAGE="${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:sha-${{ github.sha }}"
          az containerapp update \
            --name workshop-api \
            --resource-group rg-workshop-capstone \
            --image "$IMAGE" \
            --revision-suffix "sha${GITHUB_SHA::8}"
          URL=$(az containerapp show \
            --name workshop-api --resource-group rg-workshop-capstone \
            --query "properties.configuration.ingress.fqdn" -o tsv)
          echo "url=https://$URL" >> "$GITHUB_OUTPUT"

      - name: Smoke test the new revision
        run: |
          URL="${{ steps.deploy.outputs.url }}"
          # /health is the readiness endpoint Week 14 wired. ACA already gated the
          # new revision on its probe; this is belt-and-suspenders before we
          # declare victory.
          for i in $(seq 1 10); do
            if curl -fsS "$URL/health"; then echo "healthy"; exit 0; fi
            sleep 5
          done
          echo "new revision never went healthy" && exit 1
```

A few design choices worth naming:

- **`needs:` is the gate.** `publish` needs `build-test`; `deploy` needs `publish`. A red test never produces an image; a failed push never deploys. The dependency graph *is* the safety property.
- **The PR runs build + test only.** The `if:` on `publish` and `deploy` restricts them to a push on `main`. A PR gets the same build and tests but cannot deploy — exactly what you want from review.
- **The SHA tag is immutable.** `type=sha,format=long` tags the image `sha-<full-commit>`. The deploy references that exact tag. You can read the deployed revision and know, to the commit, what is running.
- **The smoke test confirms readiness.** ACA already gates the new revision's traffic on its readiness probe, but a fast `curl /health` loop in the pipeline turns "deployed" into "deployed and verified."

Citation: <https://docs.github.com/en/actions/publishing-packages/publishing-docker-images> and <https://learn.microsoft.com/en-us/azure/container-apps/github-actions>.

## 4. Standing up the Azure Container Apps target (once)

Before the pipeline can `az containerapp update`, the app must exist. You create the environment and the app once, by hand or in a bootstrap script, then let the pipeline update its image forever after:

```bash
# Resource group + Container Apps environment (the shared network/logging plane)
az group create --name rg-workshop-capstone --location eastus
az containerapp env create \
  --name workshop-env \
  --resource-group rg-workshop-capstone \
  --location eastus

# The app, created with the first image, external ingress on the app's port,
# scale-to-zero (min 0) to stay inside the free grant, and a readiness probe.
az containerapp create \
  --name workshop-api \
  --resource-group rg-workshop-capstone \
  --environment workshop-env \
  --image ghcr.io/your-org/polyglot-workshop:sha-<first-commit> \
  --registry-server ghcr.io \
  --target-port 8080 \
  --ingress external \
  --min-replicas 0 \
  --max-replicas 3 \
  --secrets "db-conn=$PG_CONN" "keycloak-secret=$KC_SECRET" \
  --env-vars "ConnectionStrings__Workshop=secretref:db-conn" \
             "Oidc__ClientSecret=secretref:keycloak-secret" \
             "ASPNETCORE_ENVIRONMENT=Production"
```

Three things to internalize:

1. **Secrets are referenced, not inlined.** `--secrets "db-conn=..."` stores the secret in the app; the env var uses `secretref:db-conn` to reference it. The connection string and the OIDC client secret never appear in plaintext in the app definition or the logs. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>.
2. **`--min-replicas 0` is what keeps you on the free tier.** The app scales to zero when idle and pays nothing; it wakes on the first request (the cold start Lecture 1's small image minimizes). For the demo you may bump `--min-replicas 1` ten minutes before, then drop it back.
3. **The readiness probe gates traffic.** Configure it to hit `/health`; ACA does not route requests to a new revision until the probe passes, so a broken deploy never serves traffic. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>.

## 5. Revisions: the rollback story is "the previous bytes are still there"

Azure Container Apps keeps **revisions** — each `containerapp update` with a new image creates a new immutable revision, and old revisions are retained (deactivated, not deleted). This is the entire reason ACA rollback is fast and safe: **the previous version is already-built bytes sitting in the platform; rolling back is re-pointing traffic at a revision that already exists, not rebuilding and redeploying an old commit.**

```bash
# List revisions, newest first; the Active one is serving traffic.
az containerapp revision list \
  --name workshop-api --resource-group rg-workshop-capstone \
  --query "[].{name:name, active:properties.active, created:properties.createdTime, image:properties.template.containers[0].image}" \
  -o table

# Roll back: activate the previous revision and send it 100% of traffic.
az containerapp revision activate \
  --name workshop-api --resource-group rg-workshop-capstone \
  --revision workshop-api--sha1a2b3c4

# Then make it the single-revision target (single-revision mode shifts all
# traffic to the latest active; or set explicit traffic weights in multi mode).
az containerapp ingress traffic set \
  --name workshop-api --resource-group rg-workshop-capstone \
  --revision-weight workshop-api--sha1a2b3c4=100
```

That is the one-command rollback the README promised and the runbook (Lecture 3) documents. **You will run it for real in Thursday's exercise, against the live deployment, while nothing is on fire** — because the moment you discover your rollback does not work should never be the moment you need it. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/revisions>.

### 5.1 Reading the logs without a shell

The chiseled image has no shell, so you read logs through ACA, not through `docker exec`:

```bash
# Tail the live console stream from the running replica.
az containerapp logs show \
  --name workshop-api --resource-group rg-workshop-capstone --follow

# Or query the structured Log Analytics table (Serilog's JSON lands here).
az monitor log-analytics query \
  --workspace "$WORKSPACE_ID" \
  --analytics-query "ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'workshop-api' | order by TimeGenerated desc | take 100"
```

This is why Week 14's Serilog + OpenTelemetry investment pays off here: you operate the chiseled, shell-less container entirely from its logs and traces. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/log-monitoring>.

## 6. The Fly.io fallback — one job changes, not the pipeline

For students who cannot create an Azure account, Fly.io is the documented secondary target, and the point of the four-phase split is that **only the deploy job changes**; build, test, and publish are identical. You add a `fly.toml`:

```toml
# fly.toml
app = "polyglot-workshop"
primary_region = "iad"

[build]
  image = "ghcr.io/your-org/polyglot-workshop:latest"   # CI overrides with the SHA tag

[http_service]
  internal_port = 8080
  force_https = true
  auto_stop_machines = "stop"      # scale to zero when idle (free)
  auto_start_machines = true
  min_machines_running = 0

[[http_service.checks]]
  path = "/health"
  interval = "15s"
  timeout = "2s"
```

And the deploy job becomes:

```yaml
  deploy-fly:
    needs: publish
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: superfly/flyctl-actions/setup-flyctl@master
      - name: Deploy to Fly.io
        run: |
          flyctl deploy \
            --image "ghcr.io/your-org/polyglot-workshop:sha-${{ github.sha }}" \
            --strategy bluegreen
        env:
          FLY_API_TOKEN: ${{ secrets.FLY_API_TOKEN }}
```

`flyctl deploy --strategy bluegreen` brings up the new version, health-checks it, and cuts over only when it is healthy — the same "verify before traffic" discipline ACA gives you. Rollback on Fly is `flyctl releases` to list and `flyctl deploy --image <previous-sha>` to redeploy a prior immutable image. Citation: <https://fly.io/docs/launch/> and <https://fly.io/docs/blue-green-deployments/>.

## 6b. Environments, required reviewers, and concurrency

Two GitHub Actions features turn the pipeline from "deploys on every push" into "deploys safely on every push." The first is **environments**. The `deploy` job declares `environment: production`, and a GitHub environment can carry *protection rules*: a required reviewer who must approve before the job runs, a wait timer, and a branch restriction. For a solo capstone you may not gate on a reviewer, but the moment there is a second person, an environment with a required reviewer means a deploy to production is a deliberate click, not an accident of a merge. The environment is also where you scope environment-specific secrets (the production `AZURE_*` IDs live on the `production` environment, not the repo, so a workflow that does not target `production` cannot read them). Citation: <https://docs.github.com/en/actions/deployment/targeting-different-environments/using-environments-for-deployment>.

The second is **concurrency**. Two pushes to `main` in quick succession would, by default, start two deploy jobs that race — the older one might finish *after* the newer one and leave the older image running. The `concurrency` block serializes them and cancels the stale one:

```yaml
concurrency:
  group: deploy-production
  cancel-in-progress: true   # a newer push cancels an in-flight older deploy
```

`cancel-in-progress: true` is correct for deploys specifically because you only ever want the *newest* commit live; an in-flight deploy of an older commit is wasted work at best and a race at worst. (For the build/test phase you might set it `false` so you do not lose test results, but for deploy, newest-wins is the rule.) Citation: <https://docs.github.com/en/actions/using-jobs/using-concurrency>.

## 6c. Where secrets live, and the three places people leak them

Restated from Week 7's auth lecture, now in a CI context, because CI is where secrets most often leak. There are exactly three categories and three correct homes:

1. **Cloud-deploy identity** (how the runner authenticates to Azure). Correct home: **OIDC federation** (§2). No secret at all — a short-lived token. The IDs (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) are non-sensitive identifiers stored as environment secrets for convenience, not credentials.
2. **Registry push** (how the runner pushes to `ghcr.io`). Correct home: the **auto-provided `GITHUB_TOKEN`**, scoped to the repository and the run, expiring when the run ends. You never create or store a PAT for this.
3. **Runtime application secrets** (the DB connection string, the OIDC client secret the *app* uses). Correct home: **the Container App's secret store** (`--secrets`, referenced by `secretref:`), set once by the bootstrap script and rotated via the runbook (Lecture 3 §6). These never pass through the pipeline at all — the pipeline deploys an *image*; it does not carry the app's runtime secrets.

The three places people leak them, every time: (a) pasting a long-lived service-principal secret into a repo secret instead of using OIDC; (b) `echo`-ing a secret in a `run:` step for "debugging" (it lands in the build log, which is readable by anyone with repo access, forever); (c) baking a secret into the image as a build arg or env var in the Dockerfile (it is in a layer, in the registry, extractable by anyone who can pull the image). The defenses: OIDC for (a), never echo secrets and rely on Actions' automatic masking for (b), and `secretref:` at runtime instead of build-time bake for (c). Citation: <https://docs.github.com/en/actions/security-guides/using-secrets-in-github-actions>.

## 6d. A note on the test job and Testcontainers in CI

The `build-test` job runs the integration tests, and those tests use Testcontainers to spin up real PostgreSQL and Keycloak containers. The thing students worry about — "does the GitHub-hosted runner even have Docker?" — has a reassuring answer: **`ubuntu-latest` runners ship with the Docker daemon running**, so Testcontainers reaches it over the default socket with no extra setup. The integration tests in CI are byte-for-byte the same tests you run locally; the only difference is the runner provides the Docker daemon instead of Docker Desktop. Two practical notes: Testcontainers' resource reaper (Ryuk) cleans up the containers when the test run ends, so you do not leak containers on the runner; and the first test that pulls the `postgres:16` and `keycloak:25` images pays a one-time pull cost, which you can shave by adding a cache or a pre-pull step if the runtime bothers you. The point that matters for the pipeline's *correctness* is that **these tests run in the gate** — they are not skipped in CI "because Docker," which is the most common and most damaging integration-test anti-pattern. A test that only runs on the author's laptop protects nothing. Citation: <https://dotnet.testcontainers.org/test_environment/continuous_integration/>.

## 7. Driving and reading the pipeline from the terminal

You do not need the GitHub web UI to operate the pipeline. The `gh` CLI watches and reads runs:

```bash
gh workflow run cd                    # manually trigger (if workflow_dispatch is added)
gh run watch                          # live status of the most recent run
gh run view --log-failed              # dump only the logs of failed steps
gh run view <run-id> --json conclusion,jobs
```

For the capstone, the habit to build is: push, `gh run watch`, and when it goes red, `gh run view --log-failed` to see exactly which phase and step failed without leaving the terminal. Citation: <https://cli.github.com/manual/gh_run>.

## 8. What this lecture earns you for the capstone

You now have a pipeline that, on every push to `main`, builds, runs the full unit + integration test suite (Testcontainers and all), publishes a SHA-tagged immutable image to `ghcr.io`, and rolls out a new Azure Container Apps revision that only takes traffic after its readiness probe passes — with no long-lived cloud credential anywhere, because Azure trusts a short-lived OIDC token scoped to your repo and branch. You can roll back in one command to a revision that already exists. And you can swap the deploy target to Fly.io by changing one job. The remaining piece — the document a tired human follows to *operate* this at 2am — is Lecture 3.

> **Citations recap.** GitHub Actions: <https://docs.github.com/en/actions>. Publishing Docker images: <https://docs.github.com/en/actions/publishing-packages/publishing-docker-images>. OIDC to Azure: <https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect>. ACA quickstart: <https://learn.microsoft.com/en-us/azure/container-apps/get-started>. ACA + GitHub Actions: <https://learn.microsoft.com/en-us/azure/container-apps/github-actions>. ACA revisions: <https://learn.microsoft.com/en-us/azure/container-apps/revisions>. ACA secrets: <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>. Fly.io launch: <https://fly.io/docs/launch/>.
