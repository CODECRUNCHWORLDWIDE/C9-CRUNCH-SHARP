# Capstone Defense — Polyglot Workshop: Deploy, Demo, and Present

> **Time:** ~13.5 hours across Wednesday–Sunday (the bulk of Week 15). **Prerequisites:** Milestones 1 (Week 13 — integration baseline) and 2 (Week 14 — production polish) complete; this week's Lectures 1–3 and Exercises 1–4; ideally both challenges. **Citations:** every Microsoft Learn URL in the three lecture notes, the GitHub Actions docs, the Azure Container Apps docs, the Fly.io docs, the .NET container-images catalogue, and the C9 SYLLABUS capstone framing.

This is the culmination of **C9 · Crunch Sharp**. It is not another mini-project — it is the **capstone defense**. You deploy the Polyglot Workshop you built in Week 13 and hardened in Week 14, you demo the live system, you package it for your portfolio, you write the runbook, and you defend it. The capstone is **35% of the C9 grade and cannot be carried by other components** (C9 SYLLABUS, Assessment matrix). It is graded on contract integrity, test coverage of meaningful paths, deploy-pipeline quality, and the runbook — **not on visual polish.**

## What the capstone is

**One deployable system. Three clients. One contract.** The Polyglot Workshop is a workshop/classroom platform: instructors create lessons, learners enroll, both submit and review exercises, and an analytics surface aggregates progress. It is the same `PolyglotWorkshop` repo throughout the arc:

- **`Workshop.Api`** — ASP.NET Core 9 backend: minimal-API REST plus a gRPC service mirroring the same domain; EF Core (PostgreSQL) for persistence and Dapper for analytics; ASP.NET Identity plus OIDC via Keycloak; SignalR for live presence; background workers with an outbox; Polly on outbound calls; Serilog + OpenTelemetry.
- **`Workshop.Mobile`** — .NET MAUI client: signs in via OIDC, consumes the gRPC contract, works offline against local SQLite, syncs on reconnect.
- **`Workshop.Admin`** — Blazor admin: Auto render mode, MudBlazor, consumes the gRPC-Web contract, moderation queue, charts, tenant-aware authorization.
- **`Workshop.Contracts`** — the single typed gRPC contract (`workshop.v1`), the source of truth both clients generate from.
- **`Workshop.IntegrationTests`** — xUnit, `WebApplicationFactory<T>`, Testcontainers for PostgreSQL and Keycloak.

Entities: `Lesson`, `Enrollment`, `Exercise`, `Submission`, `Review`, and the analytics aggregates. Week 15 changes none of this. It **deploys it.**

## The deploy milestone

The Week 15 milestone is "Live demo + runbook." To pass it, all of the following are true and demonstrable:

1. **`Workshop.Api` is deployed** to Azure Container Apps free tier (primary) or Fly.io (secondary) and reachable at a public HTTPS URL; `GET /readyz` returns 200.
2. **The `Workshop.Admin` Blazor app is reachable** at its own public URL, signs in via Keycloak, and shows live data from the deployed API.
3. **The `Workshop.Mobile` MAUI app is sideloadable** on an Android device (the APK is built and installed) and consumes the same deployed gRPC contract.
4. **`RUNBOOK.md` is in the repo root** with its five required sections (below).
5. **The GitHub Actions pipeline is green**: a push to `main` runs test → publish → (gated migrate) → deploy, and the latest run is green.
6. **The recorded demo is in the portfolio** (a short screen recording of the live demo choreography).

### Deployed topology

```text
   developer push to main
            |
            v
   +--------------------+      build / test / publish / deploy
   |  GitHub Actions    |---- test: Workshop.IntegrationTests (Testcontainers)
   |  (.github/         |---- publish: image :<sha>  -->  +-------------+
   |   workflows/       |---- deploy: OIDC (no secret) -->| ghcr.io     |
   |   deploy.yml)      |                                  | (registry)  |
   +---------+----------+                                  +------+------+
             | OIDC token (5 min)                                  | pull :<sha>
             v                                                     v
   +----------------------------------------------------------------------+
   |                  Azure Container Apps environment                    |
   |   +------------------+   gated migrate (efbundle)   +-------------+   |
   |   |  workshop-api    |----------------------------->| PostgreSQL  |   |
   |   |  (revisions,     |   OIDC back-channel          | Flexible Srv|   |
   |   |   /healthz       |----------------------------->+-------------+   |
   |   |   /readyz)       |          +-----------+                         |
   |   +--------+---------+--------->| Keycloak  |  (OIDC issuer)          |
   |            |  public HTTPS FQDN +-----------+                         |
   +------------|---------------------------------------------------------+
                |                              ^                ^
        public  |                              | gRPC / OIDC    | gRPC-Web / OIDC
        HTTPS    \                             |                |
                  +------------+      +----------------+   +----------------+
                  | curl /     |      | Workshop.Mobile|   | Workshop.Admin |
                  | clients    |      | (sideloaded    |   | (Blazor, own   |
                  +------------+      |  Android APK)  |   |  public URL)   |
                                      +----------------+   +----------------+
```

All three clients hit the same contract on the same deployed backend. That is the whole point of "one contract."

## The defense

The defense is a live session (in person or recorded + Q&A). It has two parts.

### The live demo script

Staged, not improvised — have the API URL, the admin URL, the Android device, and a terminal ready before you begin:

1. **Show the green pipeline.** Open the Actions run for the latest `main` commit: `test` green (Testcontainers spun up Postgres + Keycloak, the integration suite passed), `publish` pushed image `:<sha>` to `ghcr.io`, `deploy` authenticated via OIDC and created the revision. Say the contract out loud: "one push to `main`, tests green, live URL — no human in the loop, no long-lived credential." This sentence is the thesis of the whole capstone; lead with it.
2. **Hit the API.** `curl https://<api-fqdn>/readyz` → `Healthy` (200). Then a real authenticated call — `curl -H "Authorization: Bearer $TOKEN" https://<api-fqdn>/api/lessons` (or the gRPC mirror via `grpcurl`) — returning an instructor's lesson list. Point out that the REST and gRPC surfaces return the *same* domain data because they share `Workshop.Contracts`.
3. **Open the Blazor admin** at its public URL, sign in via Keycloak (OIDC — show the redirect to Keycloak and back), show the moderation queue and an analytics chart pulling live data from the deployed API over gRPC-Web. This is the second client on the one contract.
4. **Sideload and open the MAUI app** on the Android device, sign in via OIDC, enroll in a lesson, and show the enrollment reflected in the admin within seconds — same contract, same backend, two clients, one source of truth. (If the MAUI app is offline-first, this is also the moment to show a write made offline syncing on reconnect.)
5. **Roll back live.** Reweight Container Apps traffic to the previous revision (`az containerapp ingress traffic set ... --revision-weight <prev>=100`) while a small loop hits `/readyz` in a side terminal; show it stays `Healthy` throughout (zero downtime, because the previous revision never stopped running); reweight back. This is challenge-02 performed live.
6. **Walk the `RUNBOOK.md`** — one sentence per section: "here is how the next operator runs this without me." Have the file open; do not recite from memory — the point is that the document, not you, is the operator's interface.

Rehearse the choreography end to end at least once with everything live; the demo is staged precisely so a flaky Wi-Fi moment or an expired token does not derail the defense. Mint a fresh long-lived demo token immediately before you start, and have the previous-revision name copied to your clipboard before step 5.

### Q&A — what graders probe

Expect questions that go behind the demo:

- **Contract integrity.** "Show me where the MAUI and Blazor clients generate from `Workshop.Contracts`. What happens when you change a field in `workshop.v1` — who breaks, and how does CI catch it?"
- **Test coverage of meaningful paths.** "Which integration test would fail if auth regressed? Show the Testcontainers fixture. Why is this test more valuable than a unit test of the same method?"
- **Deploy-pipeline quality.** "Walk me through what happens on a push to `main`. Where is the long-lived Azure credential? (Correct answer: there isn't one — OIDC.) What stops a broken revision from taking traffic?"
- **The runbook.** "It's 3am, the deploy you just shipped is throwing 500s. Walk me through the runbook." (Correct first move: roll back, *then* diagnose.)
- **Operations.** "Where do the logs live? How do you rotate the OIDC client secret? What do you do if the database fills up?"

A confident, specific answer to each — pointing at code, YAML, and the runbook — is what the defense rewards. "It works, trust me" does not.

## `RUNBOOK.md` requirements

`RUNBOOK.md` lives at the repo root and has exactly these five sections, each runnable by someone who did not build the system (Lecture 3 specifies the shape). A runbook section is followable cold only if it contains the literal commands, the expected output, and the next step when the output is wrong — not prose that assumes you already know. The expected shape of each section:

1. **Deploy.** The trigger (push to `main`), the manual path (`gh workflow run deploy.yml`), the pipeline stages (test → publish → gated migrate → deploy revision), and the verification (`curl https://<fqdn>/readyz` → `Healthy`; `az containerapp revision list ...` shows the new `:<sha>` revision at 100% traffic). Include the *abort* paths: what a red test job looks like (deploy never runs, `needs: test` held it) and what a failed migration looks like (deploy aborts; the previous revision is untouched and still serving).
2. **Roll back.** The one-command revision traffic reweight to the last-known-good revision (`az containerapp ingress traffic set -n workshop-api -g rg-workshop --revision-weight <prev>=100`), why it is instant (the previous revision is still running — no image pull, no cold start, no migration), and why migrations being expand-only makes it safe (the previous revision can still read the current schema). State the rule out loud: **roll back first, diagnose second.** A 3am incident is mitigated by moving traffic to the known-good revision, not by debugging the broken one under load.
3. **Where the logs live.** The live-tail command (`az containerapp logs show -n workshop-api -g rg-workshop --follow`), the Log Analytics KQL query (`ContainerAppConsoleLogs_CL | where ... | project TimeGenerated, Log_s`), and how to pivot from a log line's `TraceId` to the full trace in the OTLP backend — the Serilog JSON carries the same `TraceId` the OpenTelemetry span does (the Week 14 correlation), so a log line is a clickable handle into the trace.
4. **Rotate the OIDC client secret.** Regenerate the client secret in Keycloak, update the Container Apps secret (`az containerapp secret set ...`), restart/redeploy the revision so it picks up the new value, and verify with a fresh sign-in that completes the OIDC round-trip and a `/readyz` that returns `Healthy` (the readiness check includes the Keycloak well-known endpoint). Note the failure signature of a stale secret: `/readyz` 503 naming the `keycloak` check, and sign-ins failing at the token exchange.
5. **If the database fills up.** The biggest-tables query (`SELECT relname, pg_size_pretty(pg_total_relation_size(relid)) ... ORDER BY pg_total_relation_size(relid) DESC LIMIT 10`), the outbox-stuck case (the outbox table grows because the drain worker is wedged — check the worker logs, not the disk), the analytics-retention `DELETE` + `VACUUM (FULL)` to reclaim space, and the storage scale-up command for PostgreSQL Flexible Server. State which of these is reversible (scale-up) and which is not (`DELETE`), so the cold reader knows which lever is safe to pull first.

The runbook is graded, and it is graded by being *used*: in the defense Q&A a grader picks one section and walks it without you narrating. A runbook that requires you in the room is not a runbook. Citations: <https://sre.google/workbook/playbooks/> and the incident-response chapter at <https://sre.google/workbook/incident-response/>.

## Portfolio packaging

The portfolio is reviewed in Week 15 (C9 SYLLABUS, Career engineering pack):

- **A public GitHub profile** linking the capstone repo, the weekly mini-projects, and a one-paragraph writeup of the capstone — what it does and what it taught you.
- **The capstone repo** public, with a top-level `README.md` (architecture, the three clients, the contract, how to run locally), the green Actions badge, and `RUNBOOK.md`.
- **The recorded demo** linked from the writeup.
- **The system-design dossier** (career pack): two written designs in the portfolio — one for a SignalR-backed real-time service, one for an EF-Core-backed multi-tenant API. Reviewers expect specifics, not hand-waving: the data model (entities, keys, the indexes that make the hot queries fast), the scaling story (where state lives, what scales horizontally, what the bottleneck is and at what load), and the failure modes (what happens when the database is slow, when a dependency is down, when a deploy is bad — and how each is detected and mitigated). Each design is two to three pages. The Polyglot Workshop is the worked example both can draw on: the multi-tenant API design reuses its `org_id`-scoped EF Core model and expand-only migration discipline; the real-time design reuses its SignalR presence hub and the outbox that decouples the write from the broadcast. Writing the design you already built is the easy half; the dossier exists to show you can reason about a system you have *not* built, so push each design one realistic step past the capstone (e.g. "now it has ten thousand concurrent SignalR connections" or "now a tenant has a hundred million rows").

## Grading rubric

The capstone is **35% of the C9 grade and cannot be carried by other components.** Points sum to 100, mirroring the SYLLABUS — graded on engineering, not polish:

- **30 points — contract integrity.** `Workshop.Contracts` (`workshop.v1`) is the single source of truth; both `Workshop.Mobile` and `Workshop.Admin` generate from it; a contract change propagates and CI catches a break. The REST and gRPC surfaces agree.
- **25 points — test coverage of meaningful paths.** `Workshop.IntegrationTests` covers the paths that matter (auth, the core domain flows, the cross-protocol broadcast) via `WebApplicationFactory<T>` + Testcontainers, and they run green in CI. Coverage of meaningful behavior, not a line-count number.
- **25 points — deploy-pipeline quality.** One push to `main` reaches a live URL with tests green; the image is multi-stage and hardened; CI holds no long-lived cloud credential (OIDC); the migration is a gated step; probes gate traffic; rollback is one command.
- **20 points — the runbook.** `RUNBOOK.md` has all five sections, each followable by someone who did not build the system, demonstrated in the defense Q&A.

**Visual polish earns zero points.** A beautiful admin UI on a system that cannot be deployed, has no tests, and has no runbook fails the capstone. A plain UI on a system that deploys with one push, is covered by meaningful tests, and has a runbook the grader can follow passes it.

## Submission

This is the final submission of C9. Push the deployed, documented capstone to the `PolyglotWorkshop` repo on `main` (the deploy contract requires `main` to be deployable). The submission must include:

- The public API URL, the public Blazor admin URL, and the `Workshop.Mobile` APK (attached or linked).
- The green GitHub Actions run link for the deploying commit.
- `RUNBOOK.md` at the repo root.
- The recorded demo link and the one-paragraph portfolio writeup.
- A short `CAPSTONE.md` mapping each rubric line to where in the repo it is satisfied (the contract project, the test fixture, the workflow, the runbook), so the grader can verify each claim.

The teaching staff schedules the live defense within the final week. The defense is where the rubric is scored: graders run the demo with you, probe the contract, the tests, the pipeline, and the runbook, and ask the operational questions above. Bring the system, not the slides.

This is the end of C9 · Crunch Sharp. You started at the type system in Week 1; you finish operating a deployed, tested, multi-client .NET system with a runbook your future self will thank you for. That is the job.
