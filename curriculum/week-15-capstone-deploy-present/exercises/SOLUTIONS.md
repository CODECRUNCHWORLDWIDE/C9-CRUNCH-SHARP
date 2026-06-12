# Week 15 — Exercise Solutions

Annotated solutions for the four exercises. The point is not the code — it is the *deploy artifact* and the *operational evidence*. For each exercise the deliverable is something you can show a grader: an image, a green pipeline, a captured rollback. Read these after you have attempted the exercises; copying the commands without building the muscle of reading the output defeats the week.

---

## Exercise 1 — Multi-stage Dockerfile

### The Dockerfile, with the reasoning

The deliverable is the two-stage Dockerfile from the exercise. The load-bearing decisions:

- **Restore before copying source.** `COPY *.csproj` then `dotnet restore` then `COPY . .` means the restore layer is cached until a `.csproj` changes. A one-line edit to `Program.cs` re-runs `dotnet publish` (fast) but not `dotnet restore` (slow). Confirm it in the build output:

  ```
   => CACHED [build 3/6] RUN dotnet restore "Workshop.Api.csproj"
   => [build 5/6] RUN dotnet publish ...
  ```

  If you see `dotnet restore` re-run on a source-only change, your `.dockerignore` is letting `bin/obj` into the context and busting the cache — fix the `.dockerignore`.

- **The chiseled runtime is the final stage.** `aspnet:9.0-noble-chiseled` is ~110 MB; the SDK is ~800 MB and is discarded. The `COPY --from=build /app/publish .` line is the only thing that crosses from build to final.

- **`USER $APP_UID` drops root.** The chiseled image defines the non-root user; you reference the variable.

### Expected measurements

```
$ docker image ls workshop-api:dev
REPOSITORY     TAG   IMAGE ID       SIZE
workshop-api   dev   a1b2c3d4e5f6   ~118MB

$ docker inspect workshop-api:dev --format '{{.Config.User}}'
1654          # the non-root UID — NOT empty (root)

$ curl -fsS http://localhost:8080/health
{"status":"Healthy"}
```

`docker history` shows the largest layer is the ~110 MB aspnet base and your app layer is single-digit MB. If your app layer is hundreds of MB, you are copying `bin/obj` — the `.dockerignore` is the fix.

### Stretch answers

- **(A) No `HEALTHCHECK` in the Dockerfile.** A `HEALTHCHECK CMD curl ...` cannot run in a chiseled image (no `curl`, no shell), and more importantly Azure Container Apps owns the readiness probe (Lecture 2 §4). Baking a `HEALTHCHECK` duplicates and conflicts with the platform's probe. Let the platform own it.
- **(B) `-chiseled-extra`** adds ICU (full globalization), the complete TLS root set, `tzdata`, and `ldconfig`. ~15 MB larger. You want it when your app does culture-aware formatting or needs the full CA bundle for outbound TLS to many hosts.
- **(C) `PublishContainer`** produces a comparable-size image with no Dockerfile, great for the inner loop. You ship the **Dockerfile** in CI because it is the explicit, reviewable record a teammate reads in a PR and the pipeline builds identically.

---

## Exercise 2 — Native AOT Dockerfile

### The AOT-clean CLI

The deliverable is the AOT-published CLI image. The thing that makes or breaks AOT is the JSON:

```csharp
// AOT-SAFE: pass the generated type-info.
var json = JsonSerializer.Serialize(rows, AnalyticsJsonContext.Default.LessonProgressArray);

// AOT-HOSTILE: the reflection overload — emits IL3050/IL2026, fails the build.
// var json = JsonSerializer.Serialize(rows);
```

When you deliberately switch to the reflection overload (acceptance criterion 2), the publish fails:

```
error IL3050: Using member 'System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, ...)'
which has 'RequiresDynamicCodeAttribute' can break functionality when AOT compiling.
error IL2026: ... members might be trimmed ...
```

That failure is the guardrail working: an AOT violation caught at build time, not at 2am. Revert and the build passes clean.

### Expected measurements

```
$ ls -lh bin/Release/net9.0/linux-x64/publish/Workshop.Analytics.Cli
-rwxr-xr-x  ...  6.4M  Workshop.Analytics.Cli       # the native binary

$ docker image ls | grep workshop-analytics
workshop-analytics  aot  ...  ~34MB

$ docker run --rm workshop-analytics:aot --out /tmp/progress.json
wrote 3 rows to /tmp/progress.json
```

~34 MB against Exercise 1's ~118 MB — roughly a 3.5x reduction, because `runtime-deps` carries no .NET runtime (the binary baked it in).

### Stretch answers

- **(A) Three things AOT forbids, mapped to this CLI:** (1) reflection-based JSON — avoided via the source-generated context; (2) `MakeGenericType` on a type the compiler never saw — avoided because the CLI has no generic dispatch over runtime types; (3) a reflection-driven command-line parser — avoided by the hand-rolled three-line arg loop instead of a library that reflects over an options class.
- **(B) `InvariantGlobalization=false`** pulls ICU back in (~30 MB), enlarging both binary and image. A real CLI needs it when it formats dates/numbers/strings per culture or does culture-aware comparisons.
- **(C) The gRPC service is not an AOT candidate** because the capstone admin uses MVC/Razor surfaces, and ASP.NET Core AOT supports Minimal APIs but not MVC/Razor; the CLI *is* a candidate because it is a self-contained console app with source-generated serialization and no reflection-heavy framework. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot>.

---

## Exercise 3 — The GitHub Actions pipeline

### The workflow

The deliverable is `.github/workflows/deploy.yml` from Lecture 2 §3, adapted to your repo. The checker enforces the seven safety properties; here is why each matters:

| Property | Why it is a safety property |
|---|---|
| four phases present | build / test / publish / deploy are distinct, with distinct failure modes |
| publish needs build-test | a red test cannot produce a deployable image |
| deploy needs publish | you cannot deploy an image this run did not publish |
| publish/deploy restricted to push on main | a PR runs build+test only; it cannot deploy |
| image pinned to SHA | every deployed image is traceable to one commit; never `:latest` |
| deploy uses OIDC | no long-lived cloud secret in CI; Azure trusts a short-lived token |
| deploy smoke-tests /health | "deployed" becomes "deployed and verified" |

### Running the checker

```
$ npx tsx exercise-03-github-actions-pipeline.ts ../.github/workflows/deploy.yml
[PASS] four phases present — build-test=build-test publish=publish deploy=deploy
[PASS] publish gated on tests (needs build-test) — ok
[PASS] deploy gated on publish (needs publish) — ok
[PASS] publish restricted to push on main — ok
[PASS] deploy restricted to push on main — ok
[PASS] image pinned to commit SHA (not :latest) — ok
[PASS] deploy authenticates via OIDC (id-token: write + azure/login) — ok
[PASS] deploy smoke-tests /health — ok

8/8 properties hold
```

(Eight lines because the "restricted to push on main" check runs once per gated job.)

### Proving the gate with a real red test

Push a commit that breaks a unit test. The pipeline:

```
$ gh run view
build-test  ✗ failed
publish     - skipped   <-- the gate worked: no image was built
deploy      - skipped
```

`publish` is *skipped*, not *failed* — `needs:` short-circuits it. No untested image reached the registry. That skipped line is the safety property in action.

### Stretch answers

- **(A)** Adding the checker as a job creates a bootstrap problem: the checker job verifies the *other* jobs but nothing verifies the checker job's own correctness except code review and its own unit tests. The honest answer is "the checker is reviewed like any other code; it is a guardrail, not a proof."
- **(B)** Add to `checkWorkflow`: iterate jobs, fail if any `permissions` is the string `write-all` or grants `contents: write` it does not need. Least privilege.
- **(C)** Gate the deploy target on a `workflow_dispatch` input `target: aca|fly`; add a property asserting exactly one of `deploy`/`deploy-fly` has a truthy `if:` for the chosen target.

---

## Exercise 4 — RUNBOOK and the real rollback

### The procedures

The deliverable is `RUNBOOK.md` with Procedure 1 (deploy) and Procedure 2 (rollback), each exact-command + expected-output, plus the captured rollback session. The grading bar is Lecture 3's: a teammate executes it cold.

### The smoke checker

The C# checker polls `/health`, asserts `200` + `"Healthy"`, and exits non-zero on failure so it can gate a script:

```
$ dotnet run --project Workshop.Smoke -- https://workshop-api.eastus.azurecontainerapps.io
smoke-checking https://.../health for up to 60s ...
HEALTHY in 412 ms
served body: {"status":"Healthy","revision":"workshop-api--sha1a2b3c4"}
```

Against a bad revision it exits 1 with `NEVER went healthy within 60s`. That exit code is what lets the stretch `rollback.sh` know recovery succeeded.

### The captured rollback

The evidence the grader wants is the terminal session showing: baseline healthy → bad revision deployed → symptom observed → `revision list` → `revision activate <known-good>` → `traffic set <known-good>=100` → smoke checker healthy again on the **known-good** revision, with the wall-clock time under ~60 seconds. The `served body` showing the known-good revision name is the proof that rollback re-pointed traffic, not just restarted the bad one.

### Stretch answers

- **(A) `rollback.sh`:**

  ```bash
  #!/usr/bin/env bash
  set -euo pipefail
  GOOD="$1"
  az containerapp revision activate  --name workshop-api -g rg-workshop-capstone --revision "$GOOD"
  az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone --revision-weight "$GOOD=100"
  dotnet run --project Workshop.Smoke -- "https://workshop-api.<region>.azurecontainerapps.io"
  ```

  `set -e` plus the smoke checker's non-zero exit means the script fails loudly if recovery did not take. That is the README's "reversible in one command."
- **(B)** Pass the expected revision as `args[1]`; parse it out of `/health`'s body and exit non-zero if a different revision answered.
- **(C)** Procedure 3 uses `az containerapp logs show --follow` for the live tail and a Log Analytics `ContainerAppConsoleLogs_CL | where Log_s has '<traceId>'` query for the by-trace path (Lecture 3 §5).

---

## A note on grading these

Every exercise here is graded on the **artifact and the evidence**, not on the prose:

- Exercise 1 — the image exists, is under ~130 MB, runs non-root, serves `/health`.
- Exercise 2 — the AOT image is ~3x smaller, the build fails on the reflection overload (you showed it), runs and writes the JSON.
- Exercise 3 — the pipeline is green, the checker reports all properties hold, and a red test demonstrably skips publish.
- Exercise 4 — `RUNBOOK.md` has both procedures, and you captured a real rollback round-trip against the live deployment.

If you can show those four things, you can deploy and operate the capstone. That is the week.
