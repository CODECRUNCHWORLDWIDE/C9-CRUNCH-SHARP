// Exercise 3 — Author the build/test/publish/deploy Pipeline, then Verify It in TypeScript.
//
// Goal: write the .github/workflows/deploy.yml that builds, runs the Testcontainers
// integration tests, publishes a SHA-tagged image to ghcr.io, and deploys a new
// Azure Container Apps revision over OIDC (no long-lived cloud secret). Then prove
// the four-phase gating with a small TypeScript checker you can run in CI itself —
// a meta-test that parses the workflow and asserts the safety properties hold.
//
// Why TypeScript here? Because the YAML is the deliverable, but "is the YAML
// correct?" is a thing you should TEST, not eyeball. This checker is the kind of
// guardrail a real platform team ships so a teammate cannot accidentally remove
// the `needs:` gate and ship an untested image. Run it with:
//
//   npm init -y && npm i -D typescript tsx yaml
//   npx tsx exercise-03-github-actions-pipeline.ts ../.github/workflows/deploy.yml
//
// Citation: https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions
//
// ----------------------------------------------------------------------------
// PART 0 — The workflow you must author (save as .github/workflows/deploy.yml).
// This is the deliverable; the checker below verifies it. It mirrors Lecture 2
// §3 — copy that, adapt the names, and make the checker pass.
// ----------------------------------------------------------------------------

import { readFileSync } from "node:fs";
import { parse } from "yaml";

// The shape of the pieces of a GitHub Actions workflow we care about.
interface Step {
  uses?: string;
  run?: string;
  with?: Record<string, unknown>;
}
interface Job {
  needs?: string | string[];
  if?: string;
  permissions?: Record<string, string>;
  steps?: Step[];
}
interface Workflow {
  on: unknown;
  permissions?: Record<string, string>;
  jobs: Record<string, Job>;
}

// A single assertion result. We collect them all so one run reports every
// failure, not just the first — the way a test runner should.
interface Check {
  name: string;
  ok: boolean;
  detail: string;
}

function normalizeNeeds(needs: Job["needs"]): string[] {
  if (needs === undefined) return [];
  return Array.isArray(needs) ? needs : [needs];
}

function jobStepsText(job: Job): string {
  return (job.steps ?? [])
    .map((s) => `${s.uses ?? ""}\n${s.run ?? ""}`)
    .join("\n");
}

// The heart of the exercise: the safety properties a correct CD pipeline must
// satisfy. Each one mirrors a sentence from Lecture 2.
function checkWorkflow(wf: Workflow): Check[] {
  const checks: Check[] = [];
  const jobs = wf.jobs ?? {};
  const names = Object.keys(jobs);

  const find = (pred: (n: string) => boolean) => names.find(pred);
  const buildTest = find((n) => /build|test/i.test(n));
  const publish = find((n) => /publish/i.test(n));
  const deploy = find((n) => /deploy/i.test(n));

  // PROPERTY 1 — there is a build/test job, a publish job, and a deploy job.
  checks.push({
    name: "four phases present",
    ok: Boolean(buildTest && publish && deploy),
    detail: `build-test=${buildTest} publish=${publish} deploy=${deploy}`,
  });

  // PROPERTY 2 — publish NEEDS build-test. A red test must block the image.
  if (publish && buildTest) {
    const ok = normalizeNeeds(jobs[publish].needs).includes(buildTest);
    checks.push({
      name: "publish gated on tests (needs build-test)",
      ok,
      detail: ok ? "ok" : `publish.needs is ${JSON.stringify(jobs[publish].needs)}`,
    });
  }

  // PROPERTY 3 — deploy NEEDS publish. You never deploy an image that was not
  // published by this run.
  if (deploy && publish) {
    const ok = normalizeNeeds(jobs[deploy].needs).includes(publish);
    checks.push({
      name: "deploy gated on publish (needs publish)",
      ok,
      detail: ok ? "ok" : `deploy.needs is ${JSON.stringify(jobs[deploy].needs)}`,
    });
  }

  // PROPERTY 4 — publish and deploy run ONLY on a push to main, never on a PR.
  for (const j of [publish, deploy].filter(Boolean) as string[]) {
    const cond = jobs[j].if ?? "";
    const ok = cond.includes("refs/heads/main") && cond.includes("push");
    checks.push({
      name: `${j} restricted to push on main`,
      ok,
      detail: ok ? "ok" : `if: ${cond || "(missing)"}`,
    });
  }

  // PROPERTY 5 — the image is tagged by COMMIT SHA, not :latest. Immutability.
  if (publish) {
    const text = jobStepsText(jobs[publish]);
    const usesSha = /type=sha/.test(text) || /github\.sha/.test(text);
    const deploysLatest = deploy ? /:latest/.test(jobStepsText(jobs[deploy])) : false;
    checks.push({
      name: "image pinned to commit SHA (not :latest)",
      ok: usesSha && !deploysLatest,
      detail: usesSha ? (deploysLatest ? "deploy references :latest!" : "ok") : "no SHA tagging found",
    });
  }

  // PROPERTY 6 — deploy uses OIDC (id-token: write + azure/login), NOT a pasted
  // long-lived client secret. This is the security property of Lecture 2 §2.
  if (deploy) {
    const perms = jobs[deploy].permissions ?? {};
    const hasIdToken = perms["id-token"] === "write";
    const usesAzureLogin = /azure\/login/.test(jobStepsText(jobs[deploy]));
    checks.push({
      name: "deploy authenticates via OIDC (id-token: write + azure/login)",
      ok: hasIdToken && usesAzureLogin,
      detail: `id-token=${perms["id-token"]} azure/login=${usesAzureLogin}`,
    });
  }

  // PROPERTY 7 — the deploy job smoke-tests /health before declaring success.
  if (deploy) {
    const ok = /\/health/.test(jobStepsText(jobs[deploy]));
    checks.push({
      name: "deploy smoke-tests /health",
      ok,
      detail: ok ? "ok" : "no /health smoke step found",
    });
  }

  return checks;
}

function main(): void {
  const path = process.argv[2] ?? ".github/workflows/deploy.yml";
  const wf = parse(readFileSync(path, "utf8")) as Workflow;
  const results = checkWorkflow(wf);

  let failed = 0;
  for (const r of results) {
    const mark = r.ok ? "PASS" : "FAIL";
    if (!r.ok) failed++;
    console.log(`[${mark}] ${r.name} — ${r.detail}`);
  }
  console.log(`\n${results.length - failed}/${results.length} properties hold`);
  // Exit non-zero on any failure so this checker itself can be a CI gate.
  process.exit(failed === 0 ? 0 : 1);
}

main();

// ============================================================================
// ACCEPTANCE CRITERIA
//   1. You authored .github/workflows/deploy.yml with build-test, publish, and
//      deploy jobs (Lecture 2 §3 is the template).
//   2. Running this checker against it prints 7/7 properties hold and exits 0.
//   3. Pushing a commit that fails a unit test turns the pipeline red at the
//      build-test job and the publish job NEVER runs (confirm with `gh run view`).
//   4. A pushed image is tagged `sha-<full-commit>` in ghcr.io (confirm with
//      `docker manifest inspect ghcr.io/<org>/<repo>:sha-<sha>`).
//   5. The deploy job logs an `azure/login` step that used OIDC (no AZURE_CLIENT
//      _SECRET anywhere in the repo secrets).
//
// STRETCH
//   A. Add this checker as a job in the workflow itself (build the YAML, then
//      gate the pipeline on its own correctness). Discuss the bootstrap problem:
//      what verifies the checker job?
//   B. Add a property that fails if any job runs as `permissions: write-all`
//      (least privilege). Implement it in checkWorkflow and make it pass.
//   C. Extend the deploy to ALSO support Fly.io (Lecture 2 §6) behind a workflow
//      input, and add a property asserting exactly one deploy target runs.
// ============================================================================
