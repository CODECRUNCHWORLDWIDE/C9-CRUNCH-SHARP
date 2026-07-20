# Challenge 2 — Green in CI From a Clean Checkout: Make the Testcontainers Baseline Pass on a GitHub Actions Runner

> **Time:** 2 hours. **Prerequisites:** Exercise 4 (a passing local integration baseline) and a GitHub repository you can push to. **Citations:** the GitHub Actions .NET guide at <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>, the `actions/setup-dotnet` action at <https://github.com/actions/setup-dotnet>, Testcontainers for .NET at <https://dotnet.testcontainers.org/>, the ASP.NET Core integration-test docs at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, and the GitHub-hosted-runner spec at <https://docs.github.com/en/actions/using-github-hosted-runners/about-github-hosted-runners/about-github-hosted-runners>.

## The premise

Milestone 1's pass condition is not "the tests pass on my laptop." It is **a green checkmark on the GitHub Actions tab** — proof that the contract, the service, the EF Core migration, and the first client compose against a real PostgreSQL on a machine that is not yours, that you cannot SSH into, and whose OS may differ from your development box. This challenge takes your locally-green baseline and gets it green in CI, then deliberately breaks it three ways so you have *seen* the failures before they ambush you on a Friday.

The reason this is a challenge and not a one-liner: "works locally, red in CI" is one of the most common and most frustrating experiences in real .NET work, and almost all of it comes from a small set of causes — the runner OS, the Docker socket, the SDK version, and timeouts on a cold runner. You will reproduce each, read its symptom, and fix it.

The mental model to carry in: your laptop is a *warm, stateful, trusted* machine — it has the SDK you installed, the Docker images you have pulled before, your dev database already migrated, your OS. A CI runner is a *cold, stateless, adversarial* machine — every run starts from a fresh VM with nothing of yours on it, a possibly-different OS, an empty image cache, and only the toolchain your workflow explicitly installs. Almost every "green local, red CI" failure is a thing your warm machine had that the cold runner did not, and that your workflow forgot to make explicit. Reproducing the three breaks below is really one lesson learned three ways: *make every dependency explicit, because the runner assumes nothing.*

By the end you will have produced: (a) a green Actions run from a clean checkout, (b) three captured *red* runs with their root-cause diagnoses, and (c) a short runbook of "if CI is red and local is green, check these four things in order."

## Setup

Push your Exercise-4 repo to a GitHub repository with the workflow from Lecture 3 at `.github/workflows/ci.yml`:

```yaml
name: CI
on:
  push:
    branches: [ main ]
  pull_request:
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release --logger "trx;LogFileName=results.trx"
      - if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/results.trx'
```

Open a PR. Confirm the baseline goes green:

```
✓ CI / build-and-test (pull_request)  —  Passed in 1m 52s
```

If it is green on the first try, good — now break it on purpose and learn the failure shapes.

Work the breaks one at a time on separate commits or branches so each red run is captured in isolation — a single push that introduces two breaks at once gives you one red run with two tangled causes, which defeats the point of seeing each signature cleanly. After each break, revert it, confirm green, and only then introduce the next.

## Break 1 — Wrong SDK pin

Change the workflow's `dotnet-version` to `8.0.x` and push. The build fails because the projects target `net9.0`:

```
error NETSDK1045: The current .NET SDK does not support targeting .NET 9.0.
  Either target .NET 8.0 or lower, or use a version of the .NET SDK that supports .NET 9.0.
```

**Diagnosis:** the runner ships some SDKs preinstalled, but the *pinned* version in `setup-dotnet` determines what is used. A `net9.0` TFM needs a `9.0.x` SDK. Fix: pin `9.0.x`. The lesson: the runner's preinstalled toolchain is not your laptop's; pin explicitly. Cite <https://github.com/actions/setup-dotnet>.

## Break 2 — Docker not where Testcontainers expects it

Add `runs-on: windows-latest` (or simulate a self-hosted runner without Docker). The build may succeed but the integration tests fail at container start:

```
Testcontainers.PostgreSql ... 
  Docker.DotNet.DockerApiException / TimeoutException:
  Cannot connect to the Docker daemon. Is the docker daemon running?
```

**Diagnosis:** Testcontainers needs a reachable Docker daemon. `ubuntu-latest` has Linux Docker running and is the supported substrate; `windows-latest` runs Windows containers by default and a self-hosted runner may have no Docker at all. Fix: keep `ubuntu-latest` for the integration job. The lesson: Testcontainers' substrate requirement is a *CI* requirement, not just a local one. Cite <https://dotnet.testcontainers.org/> and the runner spec at <https://docs.github.com/en/actions/using-github-hosted-runners/about-github-hosted-runners/about-github-hosted-runners>.

## Break 3 — A cold-runner timeout

Restore `ubuntu-latest`. Now simulate the first-run image pull cost: the runner has no cached `postgres:16`, so the first test that starts a container waits ~10–20 seconds for the pull. If your test or fixture sets a tight startup timeout, it flakes:

```
Testcontainers ... container did not become ready within 00:00:05
```

**Diagnosis:** the image pull is a one-time cost the cold runner pays that your warm laptop does not. Fix either by raising the wait strategy timeout in the fixture or by accepting the default Testcontainers wait, which is already generous. The explicit form, if you do need to widen it:

```csharp
new PostgreSqlBuilder()
    .WithImage("postgres:16")
    .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilCommandIsCompleted("pg_isready"))   // readiness, not just port-open
    .Build();
// note: the image PULL timeout is separate from the readiness wait;
// the default pull has no tight deadline, so a slow first pull is tolerated.
```

The key distinction learners miss: the *pull* and the *readiness wait* are different clocks. Testcontainers will wait for a slow `docker pull` without tripping the readiness timeout — the timeout you might be tightening governs how long it waits for the container to become *ready after it starts*, not how long the image takes to download. So a `did not become ready within 00:00:05` almost always means your fixture overrode the default wait with too short a value, not that the pull was slow. The lesson: account for cold-start costs the runner pays and you do not, and do not hand-set a tight readiness timeout to "speed things up." Cite the Testcontainers wait-strategy docs at <https://dotnet.testcontainers.org/api/wait_strategies/>.

## Break 4 (optional but instructive) — the order-dependent flake

This one is not about the runner — it is about your tests — but it surfaces *as* a "green local, red CI" because CI often runs tests in a different order or in parallel. Make two enroll tests share the *same* learner id and the *same* lesson id. Locally, if they happen to run in the order you wrote them, the idempotency branch makes the second a no-op and both pass. In CI, the runner may execute them in the reverse order or concurrently, and the test that expected a *fresh* enrollment finds the one the other test already created:

```
  Passed  Enroll_in_an_existing_lesson_creates_an_enrollment
  Failed  Enroll_creates_a_distinct_row_per_learner
    Expected enrollment.Id to differ from the seeded row, but they were equal.
```

**Diagnosis:** the two tests are coupled through shared database state and a shared identity, so their result depends on execution order — which you do not control. Fix: give each test a distinct learner id and lesson id (the cheap fix), or truncate the database between tests with Respawn (the thorough fix, a mini-project stretch goal). The lesson: a test that passes only in a particular order is not green, it is *lucky*; CI strips away the luck. Cite the xUnit shared-context docs at <https://xunit.net/docs/shared-context>.

## Reading the Actions tab

When a run goes red, the diagnosis starts on the Actions tab, not in the code. Each step in the workflow is a separate collapsible log, and the *first* red step is the one to open — a failure in `Build` makes the `Test` step irrelevant (it never ran). The `if: always()` artifact-upload step is what lets you download the `results.trx` even on a red test run, so you can read which test failed and its message without re-running the whole pipeline. Make a habit of: open the run → find the first red step → expand it → read the last 20 lines, where the actual error almost always is. The four breaks above each produce a distinctive last-20-lines signature — `NETSDK1045`, `Cannot connect to the Docker daemon`, `did not become ready within`, and an assertion failure — and recognizing the signature *is* the diagnosis.

## The runbook

Collapse the three breaks into a four-step checklist for "CI red, local green," in the order you should check them:

```
1. SDK version  — does setup-dotnet pin match the project's TFM? (Break 1)
2. Runner OS    — is the integration job on ubuntu-latest with Docker? (Break 2)
3. Docker reach — does the runner have a daemon Testcontainers can hit? (Break 2)
4. Timeouts     — are wait strategies generous enough for a cold pull? (Break 3)
5. Test isolation — do any tests depend on order or shared state? (Break 4)
```

The order is deliberate: it goes from *cheapest to check* to *hardest to diagnose*. The SDK pin is a one-line look at the workflow file. The runner OS is the next line down. Docker reachability is a log search for the daemon-connection error. Timeouts need you to read the fixture's wait strategy. And test isolation — the subtlest — needs you to think about what state two tests share, which is why it is last. Working the list top-down means you spend the cheap checks first and only reach the expensive reasoning when the simple causes are ruled out. Keep this list in `CI-RUNBOOK.md`; you will run it more times across Weeks 14-15 than you expect, because every new RPC adds tests and every new test is a chance to reintroduce one of these five.

## Acceptance criteria

1. A green Actions run from a clean checkout, screenshotted or linked, showing restore + build + test all passing with the three enroll tests green.
2. Three captured *red* runs (Breaks 1–3), each with the failing log excerpt and a one-sentence root-cause diagnosis.
3. The fix for each break applied and reconfirmed green.
4. The four-step "CI red, local green" runbook committed as `CI-RUNBOOK.md`, each step tied to the break that motivates it and a citation.
5. A one-paragraph statement of why "green in CI" — not "green locally" — is the milestone's pass condition, in terms of who can verify a checkmark versus who can verify a claim about your laptop.
6. (If you did Break 4) the order-dependent flake reproduced, diagnosed, and fixed, with a note on why CI exposed it and the local run hid it.

## Why the checkmark, not the claim

Close the write-up with the argument that makes this challenge worth doing. "It passes on my machine" is a claim only you can verify, and only right now — nobody else can re-run it, and you cannot prove it will pass on the next person's checkout. "It is green in CI" is a claim *anyone* can verify, *repeatedly*, on a machine that holds none of your state. The reviewer reads a checkmark, not your word; the next contributor's PR re-runs the same workflow on the same clean substrate; the branch protection rule makes the checkmark a *gate*, not a courtesy. That is the difference between a build that happens to work and a build that is *demonstrably* reproducible, and the second is the only kind a team can stand on for two more weeks of capstone. The milestone gates on the checkmark precisely because the checkmark is the part that does not depend on trusting you.

## Stretch goals

1. **Cache the NuGet restore.** Add `actions/cache@v4` keyed on `packages.lock.json` (enable `RestorePackagesWithLockFile`) and measure the wall-clock saving on a warm run. Cite <https://github.com/actions/cache>.
2. **Split build and test jobs with an artifact handoff.** Make `build` produce the compiled output as an artifact and `test` consume it with `--no-build`, so the two run as separate, independently-retryable jobs. Report the trade-off (parallelism vs artifact upload time). Cite <https://docs.github.com/en/actions/use-workflows/workflow-syntax-for-github-actions#jobsjob_idneeds>.
3. **Matrix the runner.** Run the *unit* tests (no Docker) across `ubuntu-latest` and `windows-latest` in a matrix while keeping the *integration* job ubuntu-only, proving you understand which tests are substrate-portable and which are not. Cite <https://docs.github.com/en/actions/using-jobs/using-a-matrix-for-your-jobs>.
4. **Require the check before merge.** Turn on branch protection (Settings → Branches → require status checks to pass) so `build-and-test` gates merges to `main`. Then push a deliberately-broken `.proto` change on a branch and confirm the PR's merge button is blocked until CI is green — the Challenge-1 lesson and this one, combined and enforced. Cite <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches>.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. GitHub docs: <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>, the runner spec at <https://docs.github.com/en/actions/using-github-hosted-runners/about-github-hosted-runners/about-github-hosted-runners>, and the matrix syntax at <https://docs.github.com/en/actions/using-jobs/using-a-matrix-for-your-jobs>. External: Testcontainers for .NET at <https://dotnet.testcontainers.org/>, its wait strategies at <https://dotnet.testcontainers.org/api/wait_strategies/>, `actions/setup-dotnet` at <https://github.com/actions/setup-dotnet>, and `actions/cache` at <https://github.com/actions/cache>.
