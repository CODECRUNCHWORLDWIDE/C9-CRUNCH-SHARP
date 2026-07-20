# Week 15 — Homework

Six practice problems that consolidate the week's deploy material. They are sized to ~45 minutes each. Do them after the lectures and the exercises; do them alongside the capstone defense work, not after it — several feed directly into the deliverable. Cite the URLs you used while solving each one in the commit message of your homework branch.

## Problem 1 — The image-hardening audit

Take your `Workshop.Api` Dockerfile and write a one-page audit of its hardening posture. For each of the following, state whether the Dockerfile satisfies it and why it matters:

1. Multi-stage (the SDK does not ship to the runtime image).
2. Runs as a non-root user (`USER $APP_UID`).
3. A `.dockerignore` keeps `bin/`, `obj/`, `.git/`, and local secrets out of the build context.
4. The runtime base is minimal (chiseled or at least `aspnet`, not `sdk`).
5. No secrets baked into image layers (check with `docker history --no-trunc`).

Then identify one further hardening step you have not taken (e.g. pinning the base image by digest, dropping Linux capabilities, a read-only root filesystem) and describe how you would add it.

Cite the container-security doc at <https://learn.microsoft.com/en-us/dotnet/core/docker/container-security> and the build-container guide at <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>.

Deliverable: `homework/01-image-hardening-audit.md`.

## Problem 2 — The AOT decision, justified

For each of the five binaries in (or adjacent to) the capstone, decide AOT vs JIT and justify it in two sentences referencing what AOT gives, costs, or forbids:

1. `Workshop.Api` (EF Core, gRPC server, OIDC).
2. `Workshop.AnalyticsExport` (Dapper, source-gen JSON, runs cold each time).
3. A hypothetical `Workshop.SeedData` CLI that uses EF Core to seed the dev DB.
4. A hypothetical `Workshop.HealthPing` that does one HTTP GET and exits.
5. `Workshop.IntegrationTests` (the test project).

Then publish one of your "AOT" choices with `/p:PublishAot=true` and report the build warnings (there should be none) and the size/cold-start delta against its JIT build.

Cite the Native AOT deployment doc at <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/> and its limitations section.

Deliverable: `homework/02-aot-decisions.md`.

## Problem 3 — The pipeline, broken four ways

Start from a working `deploy.yml`. Produce four broken variants, each changing exactly one thing, and document the observable symptom of each:

- Remove `permissions: id-token: write` from the deploy job.
- Change the federated-credential subject to a branch that does not match (`refs/heads/develop`) while pushing to `main`.
- Deploy the `:latest` tag instead of `:<github.sha>`.
- Move the `deploy` job's `needs:` so it no longer depends on `test` (so it deploys even on red tests).

For each, write what you observed (the exact error or the wrong behavior) and the one-line reason. Then restore the correct workflow and note why each line is where it is.

Cite the OIDC guide at <https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect> and the workflow-syntax doc at <https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions>.

Deliverable: `homework/03-pipeline-broken-four-ways.md`.

## Problem 4 — Liveness vs readiness, reproduced

Wire `/healthz` (liveness, no DB) and `/readyz` (readiness, DB + Keycloak) into `Workshop.Api`, then demonstrate the difference in three states and document each:

1. **Healthy.** Both return 200.
2. **DB down.** Stop the Postgres container. Show `/healthz` still returns 200 (the process is alive) and `/readyz` returns 503 with the failing check named. Explain why a liveness probe that depended on the DB would cause a restart storm here.
3. **Deploy gate.** Deploy a revision whose `/readyz` cannot pass (wrong connection string). Show that the platform withholds traffic from it and the old revision keeps serving. Explain why this is the safety net.

Cite the ASP.NET Core health-checks doc at <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks> and the Container Apps health-probes doc at <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>.

Deliverable: `homework/04-liveness-vs-readiness.md`.

## Problem 5 — Expand-then-contract, worked

Take one schema change the Polyglot Workshop plausibly needs — for example, renaming `Submission.Note` to `Submission.Feedback`. Write the migration plan as two deploys and explain why one deploy would break rollback:

1. **Deploy N (expand).** The additive migration (add `Feedback` nullable, write both, backfill). Show the EF Core migration and which code reads/writes which column during the window when both revisions are live.
2. **Deploy N+1 (contract).** The cleanup migration (drop `Note`). Explain what must be true before it is safe.
3. **The wrong way.** Show the single `RenameColumn` migration and explain exactly which request fails if a rollback to revision N−1 happens after it ran.

Cite the EF Core migrations-applying doc at <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying> and the schema-evolution guidance at <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>.

Deliverable: `homework/05-expand-then-contract.md`.

## Problem 6 — Draft the runbook

Write the first full draft of `RUNBOOK.md` for your deployed capstone — all five sections (deploy, roll back, where the logs live, rotate the OIDC client secret, database full). Then hand it to a peer (or, failing that, read it as if you had never seen the system) and have them attempt one section without asking you a question. Record where they got stuck and revise that section until it is followable cold.

This deliverable *is* part of the capstone runbook; do it once, well, and reuse it.

Cite the SRE workbook playbook chapter at <https://sre.google/workbook/playbooks/> and the manage-secrets doc at <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>.

Deliverable: `homework/06-runbook-draft.md` (plus the revised `RUNBOOK.md` in the repo root), with a note on what your reviewer got stuck on and how you fixed it.

## Submission

Push the six deliverables on a branch named `week15-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link each of the six files and include a 100-word summary of what you learned.

The teaching staff reviews homework PRs within 5 business days. Reviews focus on whether you read the citations and whether your reasoning holds together, not on perfect grammar. The single most common review comment is "where is your citation for this claim" — preempt it by linking the Microsoft Learn URL, the GitHub Actions doc, or the source for every non-trivial assertion.

Cited pages this homework draws from: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>, <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>, <https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect>, <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks>, <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>, <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>, and the SRE workbook at <https://sre.google/workbook/playbooks/>.
