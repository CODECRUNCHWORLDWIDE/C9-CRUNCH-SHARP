# Week 15 — Quiz

Ten multiple-choice questions covering multi-stage Dockerfiles, Native AOT, GitHub Actions, Azure Container Apps, revision-based rollback, and the runbook. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — Why multi-stage

The primary reason an ASP.NET Core production Dockerfile uses a separate SDK `build` stage and a `runtime` final stage is:

- (A) The SDK cannot run a published application.
- (B) The SDK image (~800 MB) carries the compiler and tooling that are useless at runtime and are attack surface; the final stage copies only the published output onto a thin runtime, so the SDK never reaches the registry.
- (C) Multi-stage builds are required for the image to be pushed to `ghcr.io`.
- (D) The runtime image cannot compile the source, so two stages are mandatory for any .NET app.

## Question 2 — Layer caching

In a Dockerfile, copying the `.csproj` files and running `dotnet restore` *before* `COPY . .` exists to:

- (A) Reduce the final image size.
- (B) Keep the `dotnet restore` layer cached across source-only changes, so editing a `.cs` file does not re-download every NuGet package.
- (C) Make the build run as non-root.
- (D) Avoid copying the `.git` directory into the image.

## Question 3 — Chiseled images

A `-noble-chiseled` runtime image:

- (A) Includes a full shell and package manager for easy `docker exec` debugging.
- (B) Strips the shell, package manager, and most of the OS, leaving the minimum to run a .NET process — smaller and less attack surface, but you cannot `docker exec bash` into it.
- (C) Is larger than the non-chiseled image because of the extra security hardening.
- (D) Can only run Native AOT binaries, not framework-dependent apps.

## Question 4 — `$APP_UID`

`USER $APP_UID` in a chiseled .NET Dockerfile:

- (A) Sets the app to run as root with a friendly name.
- (B) Runs the process as the non-root user the chiseled image defines, so the container does not run as root — and is why Kestrel defaults to port 8080, not 80.
- (C) Is required for the image to be smaller.
- (D) Grants the process permission to bind to port 443.

## Question 5 — Native AOT forbids

Which of these will break a Native AOT publish?

- (A) Using records and pattern matching.
- (B) Source-generated `System.Text.Json` serialization.
- (C) Reflection-based `JsonSerializer.Serialize(obj)` (the overload without the generated type-info), which emits IL3050/IL2026.
- (D) Writing to a file with `File.WriteAllText`.

## Question 6 — The pipeline gate

In the build/test/publish/deploy workflow, `publish` declares `needs: build-test`. If a unit test fails, the `publish` job:

- (A) Runs anyway and pushes the image, because publish is independent.
- (B) Is skipped (not run), because `needs:` short-circuits it when the dependency fails — so no untested image reaches the registry.
- (C) Runs but pushes the image with a `:broken` tag.
- (D) Retries the tests up to three times before publishing.

## Question 7 — OIDC to Azure

Authenticating the deploy job to Azure via OIDC federation (rather than a stored client secret) means:

- (A) A long-lived service-principal secret is stored in a GitHub Actions secret.
- (B) GitHub mints a short-lived token for the run, Azure trusts tokens from your specific repo+branch, and `azure/login` exchanges it for a short-lived Azure token — no long-lived cloud credential anywhere.
- (C) The deploy cannot be restricted to a single branch.
- (D) The runner stores the Azure password encrypted at rest.

## Question 8 — The SHA tag

Deploying an image tagged with the commit SHA (e.g. `sha-<commit>`) rather than `:latest` matters because:

- (A) `:latest` images are larger.
- (B) A SHA tag is immutable and traceable to exactly one commit, so you always know what is running and you deploy the exact bytes that passed the tests; `:latest` is a moving target.
- (C) `ghcr.io` does not support the `:latest` tag.
- (D) SHA tags deploy faster.

## Question 9 — Revision rollback

Rolling back an Azure Container Apps deployment with `az containerapp revision activate` on the previous revision is fast and safe because:

- (A) It rebuilds the previous commit from source and redeploys it.
- (B) The previous revision is already-built bytes the platform retained; rollback re-points traffic at a revision that already exists, with no rebuild.
- (C) It reverts the Git history on `main`.
- (D) It deletes the bad revision's image from the registry.

## Question 10 — What a runbook is

The defining property of a good runbook procedure is:

- (A) It explains the architectural reasoning behind each component.
- (B) It is a sequence of exact, copy-pasteable commands, each with its expected output, ending in a verification step — written for a tired operator who did not write the code.
- (C) It is the same document as the README.
- (D) It lists every dependency with its version and license.

---

## Answer key

**Q1 — (B).** The SDK is build-time tooling (~800 MB); the final stage copies only the published output onto a thin runtime so the SDK never ships. (A) is false — the SDK *can* run apps; it is just wasteful and a larger attack surface. Lecture 1 §2.

**Q2 — (B).** Restore-before-source keeps the expensive `dotnet restore` layer cached across source-only edits. (D) is the job of `.dockerignore`, not the COPY ordering. Lecture 1 §3.

**Q3 — (B).** Chiseled strips the shell, package manager, and most of the OS — smaller, less attack surface, but no `docker exec bash`. You debug from logs (Week 14's investment) instead. Lecture 1 §4.

**Q4 — (B).** `$APP_UID` is the non-root user the chiseled image defines; running non-root is why Kestrel binds 8080 (a non-root process cannot bind < 1024). (D) is exactly backwards. Lecture 1 §5.

**Q5 — (C).** The reflection-based serialize overload carries `RequiresDynamicCode` and emits IL3050/IL2026; with `TreatWarningsAsErrors` the AOT build fails. The source-generated context (B) is the AOT-safe path. Lecture 1 §7.1.

**Q6 — (B).** `needs:` short-circuits a job when its dependency fails — the dependent job is *skipped*, not failed, so no untested image is produced. Lecture 2 §1, §3.

**Q7 — (B).** OIDC federation means no long-lived cloud secret: a short-lived token, trust scoped to repo+branch, exchanged at run time. (A) is the anti-pattern OIDC replaces. Lecture 2 §2.

**Q8 — (B).** A SHA tag is immutable and traceable to one commit; you deploy the exact tested bytes. `:latest` is a moving target and the most common cause of "it worked yesterday." Lecture 2 §3.

**Q9 — (B).** The previous revision is retained, already-built bytes; rollback re-points traffic at it instantly with no rebuild. That is the whole reason it is the first on-call instinct. Lecture 2 §5, Lecture 3 §4.

**Q10 — (B).** A runbook procedure is exact commands + expected output + a verification step, for a tired operator who did not write the code. It is not the README (C) and not an architecture doc (A). Lecture 3 §1.
