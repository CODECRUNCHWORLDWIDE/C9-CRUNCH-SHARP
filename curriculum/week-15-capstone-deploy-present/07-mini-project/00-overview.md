# Mini-Project (Capstone Milestone) — Deploy the Polyglot Workshop Live, Sideload the MAUI Client, Write the RUNBOOK, Record the Demo

> **Time:** ~11 hours across Wednesday–Sunday. **Prerequisites:** Week 13 (integration baseline green), Week 14 (hardened, observable), and this week's Exercises 1–4. **This is the final capstone milestone.** The master capstone specification lives at [`../../projects/capstone/`](../../projects/capstone/) — read it as the authoritative source of truth for the Polyglot Workshop; this README is the Week-15 *deploy-and-present* slice of it.

This is not a new build. Weeks 13 and 14 built and hardened the **Polyglot Workshop** — the ASP.NET Core 9 backend (Minimal APIs + a gRPC service mirroring the domain, EF Core/PostgreSQL, Dapper analytics, ASP.NET Identity + OIDC via Keycloak, SignalR presence, background workers with an outbox, Polly, Serilog + OpenTelemetry), the .NET MAUI client (OIDC sign-in, the gRPC contract, offline SQLite + sync), the Blazor admin (Auto render mode, MudBlazor, gRPC-Web, moderation queue, tenant-aware authz), all over one shared `.proto`, with xUnit + Testcontainers integration tests and a BenchmarkDotNet regression test. Week 15 takes that system and **ships it to a URL a stranger can open**, then proves it with a runbook and a recorded demo.

**The theme is the syllabus's: deploy is a feature.** The pipeline is part of the product, the runbook is part of the product, and the demo is the proof. The deliverable is the Week-15 capstone milestone, verbatim from the syllabus: the Polyglot Workshop is deployed, the MAUI client is sideloadable on an Android device, the Blazor admin is reachable on its public URL, the runbook is in the repo, and the recorded demo is in the portfolio.

---

## What you deliver

### D1 — The backend is live on a public HTTPS URL

The ASP.NET Core 9 backend (gRPC + Minimal API host) is deployed to **Azure Container Apps free tier** (primary) or **Fly.io** / any Linux container host (secondary), built by the multi-stage chiseled Dockerfile from Lecture 1, pushed and deployed by the GitHub Actions pipeline from Lecture 2. It scales to zero when idle, wakes on the first request, and serves `/health` green. PostgreSQL and Keycloak run as managed/companion services the deployed app reaches over the network.

### D2 — The Blazor admin is live on its own public HTTPS URL

The Blazor Auto admin is deployed (a second Container App or static-web-app + API) and reaches the backend over **gRPC-Web**. An admin signs in via OIDC, sees the moderation queue, the charts, and the tenant-scoped data. The admin URL is in the runbook's "At a glance" block.

### D3 — The MAUI client is a signed, sideloadable Android APK

The MAUI client is published `-f net9.0-android -c Release`, signed with your keystore, and **sideloaded onto a real or emulated Android device** (`adb install`). It signs in via OIDC against the **deployed** Keycloak (not localhost), consumes the deployed gRPC contract, works offline against local SQLite, and syncs on reconnect. The custom-scheme redirect URI is registered in the deployed Keycloak client (Lecture 3 §9).

### D4 — The pipeline is the only way to production

Every change reaches production via `git push` → the four-phase pipeline (build, test-with-Testcontainers, publish-SHA-tagged-to-ghcr.io, deploy-new-revision). No hand-deploys. A red test blocks the image; an unpublished image never deploys; the deploy is verified by a `/health` smoke check (Exercise 3).

### D5 — `RUNBOOK.md` is in the repo and was executed by a teammate

A `RUNBOOK.md` at the repo root with the five procedures from Lecture 3 — deploy, rollback, find-the-logs, rotate-the-OIDC-secret, database-full — each exact-command + expected-output + verification. **A teammate (or a member of the teaching staff) followed it cold to perform a deploy and a rollback.** Capture that session as evidence. This is the runbook the syllabus's Career Engineering Pack also asks for.

### D6 — The recorded demo is in the portfolio

A **5-to-8-minute** recorded demo, linked from the C9 portfolio, that traces **one lesson end to end across all three clients and the live backend**:

1. An instructor creates a lesson in the **Blazor admin** (live URL).
2. A learner **enrolls and submits** on the **MAUI client** (sideloaded on the phone), signed in via OIDC, including an offline-then-sync moment.
3. The submission appears in the admin **moderation queue**, and the **analytics** chart updates.
4. You show the trace ID for one request flowing through Serilog → Tempo (the Week-14 observability), and you do one **live rollback** to prove the runbook.

The demo is the proof that the contract held across three clients and the deploy worked. It is graded on the trace being coherent, not on visual polish.

---

## Suggested project layout

This composes the Week 13/14 system; the new files this week are the Dockerfiles, the workflow, the runbook, and the deploy config.

```
polyglot-workshop/
├── Dockerfile                       <-- Lecture 1: chiseled ASP.NET Core backend
├── Dockerfile.aot                   <-- Lecture 1: Native AOT analytics CLI
├── .dockerignore
├── fly.toml                         <-- Lecture 2 §6: the Fly.io fallback (optional)
├── RUNBOOK.md                       <-- Lecture 3: the five procedures (D5)
├── DEMO.md                          <-- the demo script + the portfolio link (D6)
├── .github/workflows/deploy.yml     <-- Lecture 2: build/test/publish/deploy
├── infra/
│   └── bootstrap.sh                 <-- `az containerapp env/create` + secrets (once)
├── src/
│   ├── Workshop.Api/                <-- backend host (Week 13/14)
│   ├── Workshop.Contracts/          <-- the shared .proto + generated code
│   ├── Workshop.Domain/             <-- domain logic (xUnit-tested)
│   ├── Workshop.Analytics.Cli/      <-- the Native AOT exporter (Lecture 1 / Ex 2)
│   ├── Workshop.Admin/              <-- Blazor Auto admin (Week 11/13)
│   └── Workshop.Maui/               <-- MAUI client (Week 10/13)
└── tests/
    ├── Workshop.UnitTests/          <-- xUnit domain tests
    └── Workshop.IntegrationTests/   <-- WebApplicationFactory<T> + Testcontainers
```

## Starter files

A small starter scaffold is in `mini-project/starter/`. These are the **new** Week-15 deploy artifacts; copy them into your capstone repo and adapt the names. They compile/lint as-is but are wired to placeholder names you must replace with your real services.

- `Dockerfile` — the multi-stage chiseled ASP.NET Core Dockerfile for the backend (Lecture 1).
- `deploy.yml` — the four-phase GitHub Actions workflow (Lecture 2), goes in `.github/workflows/`.
- `RUNBOOK.md` — the runbook skeleton with all five procedures stubbed in the exact-command style; you fill the placeholders with your real URLs/resource group and execute the procedures.
- `DeploySmokeCheck.cs` — the `/health` smoke checker (from Exercise 4) the pipeline and the runbook both call.
- `bootstrap.sh` — the one-time `az` script that stands up the Container Apps environment, the app, and the secrets.

The starter is intentionally not a full app — the app is your Week 13/14 capstone. The starter is the *deploy layer* you bolt onto it.

## Acceptance criteria

Mapped to the deliverables; this is the same bar the grader runs.

### Deploy is live (30%)

- [ ] The backend serves `/health` green on a public HTTPS URL (ACA or Fly.io).
- [ ] The Blazor admin is reachable on its own public HTTPS URL and talks gRPC-Web to the backend.
- [ ] The deploy is produced by the pipeline from a `git push`, not by hand.
- [ ] The deployed image is SHA-tagged and traceable to a commit.

### The MAUI client (15%)

- [ ] A signed Release APK sideloads onto a device/emulator (`adb install` succeeds).
- [ ] It signs in via OIDC against the **deployed** Keycloak (redirect URI registered).
- [ ] It works offline and syncs on reconnect against the deployed gRPC contract.

### Pipeline quality (15%)

- [ ] Four phases, gated: red test blocks publish; unpublished image never deploys.
- [ ] Testcontainers integration tests run in CI (not skipped).
- [ ] No long-lived cloud credential in the repo; OIDC to the cloud.
- [ ] The deploy is verified by a `/health` smoke check.

### The runbook (20%)

- [ ] `RUNBOOK.md` has all five procedures, each exact-command + expected-output + verification.
- [ ] A teammate executed a deploy and a rollback from it cold; you captured the session.
- [ ] The rollback is one command (or one script) and you ran it against the live deploy.

### The demo (20%)

- [ ] 5–8 minutes, linked from the portfolio.
- [ ] Traces one lesson across admin → MAUI → admin moderation/analytics on the live system.
- [ ] Shows one real trace ID through the observability stack and one live rollback.

## Suggested order of work

- **Wednesday.** Write `Dockerfile` (Ex 1) and `deploy.yml` (Ex 3). Run `bootstrap.sh` to create the ACA environment + app + secrets. Get the backend deployed and `/health` green from a `git push`. Do not move on until a push deploys.
- **Thursday.** Deploy the Blazor admin to its own URL; confirm gRPC-Web works against the live backend. Build and sideload the signed MAUI APK (Lecture 3 §9); register its redirect URI in the deployed Keycloak; complete a real OIDC sign-in from the phone. Write the runbook's deploy + rollback procedures and do the real rollback drill (Ex 4).
- **Friday.** Finish the runbook (logs, secret rotation, database-full). Have a teammate execute the deploy + rollback cold; fix every place they got stuck (that is the runbook test). Run the offline-then-sync path on the MAUI client end to end.
- **Saturday.** Record the 5–8 minute demo tracing one lesson across all three clients + one trace ID + one live rollback. Link it from the portfolio. Write `DEMO.md`.
- **Sunday.** Final clean cycle as the grader will run it: push a trivial change, watch the pipeline deploy it, smoke-check it, roll it back, smoke-check the rollback. Then run the runbook's teardown procedure (scale to zero / delete) so the free tier is not left running.

## What "done" looks like

A grader opens your backend URL and gets a healthy response; opens your admin URL and signs in; installs your APK on a phone and signs in against the same deployed Keycloak; watches your 7-minute demo trace one lesson from "instructor creates it" in the admin, through "learner enrolls and submits offline then syncs" on the phone, to "it appears in the moderation queue and the analytics chart"; reads your `RUNBOOK.md`, follows the rollback procedure cold, and watches the service revert in one command; then confirms the whole thing was deployed by a pipeline from a `git push`, with the image traceable to a commit and no cloud secret in the repo. Every step works without you touching the keyboard to fix it. That is the Polyglot Workshop, deployed and presented. That is where C9 ends.

## Submission

Push to a branch `week15-capstone/<your-handle>` and open a PR against the C9 curriculum repository. The PR description must link to: the live backend URL, the live admin URL, the APK artifact (or build instructions), `RUNBOOK.md`, the captured teammate-rollback session, and the recorded demo. The teaching staff reviews within 7 business days, against the acceptance criteria above and the master capstone spec at [`../../projects/capstone/`](../../projects/capstone/).

> **Tear it down when it is graded.** The runbook's last procedure scales the app to zero or deletes the resource group. Run it after the grade is in so the free tier stays free.
