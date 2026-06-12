# Week 15 — Homework

Five practice problems that consolidate the deploy and runbook material. They are sized to ~45–60 minutes each. Do them after the lectures and exercises, alongside the mini-project. Cite the URLs you used in the commit message of your homework branch. Because this is the capstone deploy week, every problem produces an artifact you can show — an image, a pipeline run, a runbook section — not just prose.

## Problem 1 — The image-size audit

Build the Workshop API image three ways and produce a comparison table:

1. Single-stage, `FROM sdk:9.0` (the anti-pattern).
2. Multi-stage onto `aspnet:9.0` (non-chiseled).
3. Multi-stage onto `aspnet:9.0-noble-chiseled` (the one you ship).

For each, record the total size from `docker image ls` and, from `docker history`, the size of the largest layer and the size of *your app's* layer. Write a 200-word analysis: where does the size go in each, why is the chiseled image the one you deploy, and what is the cold-start consequence on a scale-to-zero free tier.

**Deliverable:** `homework/01-image-audit.md` with the three Dockerfiles, the size table, and the analysis.

Cite: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container> and <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>.

## Problem 2 — The AOT guardrail, demonstrated

Take the analytics CLI from Exercise 2. Deliberately introduce three AOT violations, one at a time, and capture the exact build error each produces, then fix each:

1. Swap the source-generated serialize for the reflection overload `JsonSerializer.Serialize(rows)`.
2. Add a call that uses `Type.MakeGenericType(...)` on a type constructed from a string.
3. Reference a NuGet package that is not trim-compatible and observe the warning.

For each, record the `ILxxxx` warning/error code, one sentence on *why* AOT forbids it, and the fix.

**Deliverable:** `homework/02-aot-guardrail.md` with the three error captures and fixes.

Cite: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/> and <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings>.

## Problem 3 — Prove the pipeline gate

On your capstone pipeline (or a sandbox repo with the workflow), push two commits and capture the evidence for each:

1. A commit that **breaks a unit test.** Capture the `gh run view` output showing `build-test` failed and `publish`/`deploy` **skipped** — proving no untested image was published.
2. A commit that **passes**, then `docker manifest inspect ghcr.io/<org>/<repo>:sha-<sha>` proving the SHA-tagged image exists in the registry.

Write a paragraph explaining why `publish` shows *skipped* and not *failed*, and what that distinction tells you about how `needs:` works.

**Deliverable:** `homework/03-pipeline-gate.md` with both captured runs and the explanation.

Cite: <https://docs.github.com/en/actions/using-jobs/using-jobs-in-a-workflow> and <https://docs.github.com/en/actions/publishing-packages/publishing-docker-images>.

## Problem 4 — Write and test one runbook procedure

Write the **"rotate the OIDC client secret"** procedure (Lecture 3 §6) for your deployment as a complete runbook section — exact commands, expected output, verification step. Then **execute it against your live deployment** and capture the session: regenerate the secret in Keycloak, `az containerapp secret set`, restart the revision, and confirm a sign-in still works on the live admin URL. Finally, have a teammate read your procedure cold and note every place they hesitated; revise the procedure to remove the hesitation.

**Deliverable:** `homework/04-secret-rotation.md` with the procedure, the captured execution, and the teammate's friction notes with your revisions.

Cite: <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>.

## Problem 5 — The rollback drill, timed

Against your live deployment, run a complete rollback drill and time it:

1. Record the baseline (current revision healthy).
2. Deploy a second revision (it can be a trivial change — you just need two).
3. Roll back to the first using Procedure 2 (`revision activate` + `traffic set`).
4. Verify with the smoke checker that the first revision is serving again, and record the wall-clock time from `activate` to a healthy smoke check.

Write a paragraph: was it under 60 seconds? What dominated the time (the activate, the traffic shift, the cold start)? What would make it faster?

**Deliverable:** `homework/05-rollback-drill.md` with the timed session and the analysis.

Cite: <https://learn.microsoft.com/en-us/azure/container-apps/revisions>.

---

## Grading rubric

Each problem is worth 20 points (100 total). The bar is the artifact, not the prose.

| Problem | 20 / Full credit | 12 / Partial | 0 / Missing |
|---|---|---|---|
| 1 — Image audit | Three images built, size table with largest + app-layer sizes, correct analysis of where size goes and the cold-start consequence | Sizes recorded but no layer breakdown or weak analysis | No images built |
| 2 — AOT guardrail | Three distinct violations introduced, exact `ILxxxx` codes captured, each fixed with a correct one-sentence why | Fewer than three, or codes not captured | No violations demonstrated |
| 3 — Pipeline gate | Both runs captured; `publish` shown *skipped* on the red test; SHA image proven in registry; correct skipped-vs-failed explanation | One run, or the explanation conflates skipped/failed | No pipeline evidence |
| 4 — Secret rotation | Complete procedure, executed live with captured session, teammate friction notes incorporated | Procedure written but not executed, or no teammate test | No procedure |
| 5 — Rollback drill | Timed live rollback, smoke-verified on the known-good revision, analysis of what dominated the time | Rollback done but not timed or not smoke-verified | No drill |

**Submission.** Push to a `week15-homework/<your-handle>` branch with the `homework/` directory and open a PR. Reviews focus on whether the artifacts are real (actual images, actual pipeline runs, actual live executions) and whether the runbook procedures would survive a stranger executing them cold.
