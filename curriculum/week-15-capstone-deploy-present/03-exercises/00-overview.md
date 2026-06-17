# Week 15 — Exercises

This is the capstone deploy week, and these four exercises are the muscle memory behind the rollout. Together they take the hardened Polyglot Workshop build and turn it into an artifact a pipeline ships: you containerize the API in a multi-stage chiseled Dockerfile, weigh a Native AOT companion CLI against that runtime image, author the build/test/publish/deploy GitHub Actions workflow that pushes a SHA-tagged image to `ghcr.io` and rolls out an Azure Container Apps revision over OIDC, then write and *execute* the RUNBOOK's deploy and rollback procedures against a live deployment. The thesis of the week — deploy is a feature, and the runbook is the product — runs through every one. The deliverable is never the prose; it is the image, the green pipeline, and the captured rollback you can hand a grader.

## How to Run an Exercise

These exercises are deploy and CI/CD prep, so "running" them means building images, running a workflow checker, and exercising a real cloud deployment — not just `dotnet run`. The content files carry their setup commands inline; the shape is:

1. **Read the file top to bottom first.** Each exercise embeds the project scaffold, the Dockerfile / workflow / runbook deliverable, and the exact commands. The `.cs` and `.ts` files are containers for those deliverables, not programs you compile as-is.
2. **Scaffold the project** the file names at the top — for example `dotnet new web -n Workshop.Api -f net9.0` (Exercise 1), `dotnet new console -n Workshop.Analytics.Cli -f net9.0` (Exercise 2), or `dotnet new console -n Workshop.Smoke -f net9.0` (Exercise 4) — then paste in the marked `PART` blocks.
3. **Build and measure the image.** Exercises 1 and 2 are Dockerfile exercises: `docker build`, `docker run`, `docker image ls`, `docker history`, `docker inspect`. Record the sizes — the size win and the non-root user are the graded outcomes.
4. **For the pipeline (Exercise 3),** author `.github/workflows/deploy.yml`, then run the TypeScript checker against it: `npm init -y && npm i -D typescript tsx yaml`, then `npx tsx exercise-03-github-actions-pipeline.ts ../.github/workflows/deploy.yml`. It must report all properties hold and exit 0.
5. **For the runbook (Exercise 4),** write the deploy and rollback procedures into `RUNBOOK.md`, then run a real revision rollback against your live Azure Container Apps deployment, using the C# smoke checker to verify health before and after.
6. **Capture the evidence.** Image sizes, the checker output, the `gh run view` skip, the rollback terminal session — that captured output is what proves the exercise, so save it as you go.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|------------|-----------|
| 1 | [exercise-01-multi-stage-dockerfile.cs](./exercise-01-multi-stage-dockerfile.cs) | Write a multi-stage Dockerfile for the ASP.NET Core 9 API: restore-before-copy layer caching, the chiseled `aspnet:9.0-noble-chiseled` runtime, the non-root `$APP_UID` user, and a sub-130 MB image. Read `docker history` and prove the SDK never ships. | Beginner+ | 60 min |
| 2 | [exercise-02-native-aot-dockerfile.cs](./exercise-02-native-aot-dockerfile.cs) | Publish the analytics export CLI Native AOT, containerize it on `runtime-deps:9.0-noble-chiseled`, and weigh the ~30–40 MB image against Exercise 1's runtime image. Prove the build *fails* on a reflection-based serialize call (IL3050/IL2026). | Intermediate | 60 min |
| 3 | [exercise-03-github-actions-pipeline.ts](./exercise-03-github-actions-pipeline.ts) | Author the build/test/publish/deploy workflow YAML — `needs:` gating, push-on-main guards, SHA tagging, OIDC to Azure, a `/health` smoke step — then verify all seven safety properties with the bundled TypeScript checker. | Intermediate+ | 90 min |
| 4 | [exercise-04-runbook-and-rollback.cs](./exercise-04-runbook-and-rollback.cs) | Write the RUNBOOK deploy + rollback procedures (exact command + expected output), then execute a *real* revision rollback against the live deployment, verified by the C# smoke checker reporting the known-good revision serving. | Advanced | 90 min |

## Checking Your Work

[SOLUTIONS.md](./SOLUTIONS.md) carries annotated walkthroughs for all four exercises — the Dockerfiles with their reasoning, the expected measurements, the checker output, the captured rollback, and answers to every stretch — so attempt the exercise first, then read it to grade the artifact rather than the code. Quick self-check before you call one done:

- **Exercise 1/2 — the image is right.** `docker image ls` shows the API under ~130 MB and the AOT image at ~30–40 MB (roughly 3x smaller), `docker inspect ... .Config.User` reports a non-zero UID, and `docker history` proves the SDK is not in the final layer.
- **Exercise 3 — the gate holds.** The checker prints all properties hold and exits 0, and a pushed commit that breaks a unit test turns the pipeline red at build-test with `publish` *skipped*, not failed — no untested image reaches the registry.
- **Exercise 4 — the rollback round-trips.** `RUNBOOK.md` has both procedures with exact commands, and you captured a live rollback where the smoke checker goes healthy again on the known-good revision in under ~60 seconds, with the served revision name proving traffic actually re-pointed.
