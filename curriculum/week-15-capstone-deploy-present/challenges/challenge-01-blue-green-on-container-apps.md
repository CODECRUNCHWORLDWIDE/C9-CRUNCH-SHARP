# Challenge 1 — Blue/Green (Canary) Deploy on Azure Container Apps with Automated Rollback on a Failed Smoke Check

> **Time:** 2 hours. **Prerequisites:** Lectures 1–2, Exercises 1, 3, 4 (you can build the image, you have a pipeline, you can roll back a revision). **Citations:** the ACA revisions and traffic-splitting docs at <https://learn.microsoft.com/en-us/azure/container-apps/revisions> and <https://learn.microsoft.com/en-us/azure/container-apps/traffic-splitting>, the ACA health-probes doc at <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>, and the GitHub Actions environments doc at <https://docs.github.com/en/actions/deployment/targeting-different-environments>.

## The premise

Lecture 2 deploys by replacing the active revision and letting the readiness probe gate traffic. That is fine, but it is all-or-nothing: the new revision either passes its probe and takes 100% of traffic, or it does not start. A **blue/green (canary)** deploy is more careful: the new revision (green) comes up alongside the old (blue), you send it a *small* slice of traffic (say 10%), you run a smoke check against it, and only if it is healthy do you shift the remaining 90% — and if it is *not* healthy, you shift traffic back to blue automatically and the deploy fails. This is the deploy strategy that lets you catch a bad release that passes its own health probe but breaks under real traffic.

Azure Container Apps gives you the primitive for free: it can run multiple active revisions and split traffic between them by weight. Your job is to wire that primitive into the pipeline so a deploy is blue/green with an automated rollback.

By the end you will have: a pipeline that brings up green, canaries it at 10%, smoke-checks it, promotes it to 100% on success, and **automatically reverts to blue on failure** — and you will have proved both paths by shipping one good release and one deliberately broken one.

## Setup

Put the app in **multiple-revision mode** so two revisions can be active at once (single-revision mode deactivates the old revision on update, which is why Lecture 2's simple path works but cannot canary):

```bash
az containerapp revision set-mode \
  --name workshop-api -g rg-workshop-capstone \
  --mode multiple
```

Confirm the current (blue) revision is active and serving 100%:

```bash
az containerapp ingress traffic show --name workshop-api -g rg-workshop-capstone -o table
```

## Requirements

### R1 — Bring up green without taking traffic

The deploy job creates the new (green) revision with a known suffix and **0% traffic** initially, so it starts and passes its readiness probe without affecting users:

```bash
az containerapp update --name workshop-api -g rg-workshop-capstone \
  --image ghcr.io/<org>/polyglot-workshop:sha-${GITHUB_SHA} \
  --revision-suffix "g${GITHUB_SHA::8}"
GREEN="workshop-api--g${GITHUB_SHA::8}"
# Pin all traffic to the existing blue revision for now (green is up but cold).
az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone \
  --revision-weight "$BLUE=100" "$GREEN=0"
```

### R2 — Canary 10% and smoke-check green specifically

Shift 10% to green and smoke-check **green's own URL** (each revision gets a stable per-revision FQDN, so you can hit green directly without depending on the 10% routing lottery):

```bash
GREEN_FQDN=$(az containerapp revision show --name workshop-api -g rg-workshop-capstone \
  --revision "$GREEN" --query "properties.fqdn" -o tsv)
az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone \
  --revision-weight "$BLUE=90" "$GREEN=10"
# Smoke-check green directly (Exercise 4's checker, pointed at the green FQDN).
dotnet run --project Workshop.Smoke -- "https://$GREEN_FQDN"
```

The per-revision FQDN is the key trick: it lets you verify *the new revision specifically*, not "whichever revision the load balancer happened to pick."

### R3 — Promote on success

If the smoke check passes, shift 100% to green and (optionally) deactivate blue:

```bash
az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone \
  --revision-weight "$GREEN=100" "$BLUE=0"
```

### R4 — Automated rollback on failure

If the smoke check fails (non-zero exit), the pipeline must **revert traffic to blue and fail the job** — no green traffic, no manual intervention:

```bash
# This runs in an `if: failure()` step OR via set -e trapping the smoke exit code.
az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone \
  --revision-weight "$BLUE=100" "$GREEN=0"
echo "::error::green failed smoke check; reverted to blue $BLUE"
exit 1
```

### R5 — Prove both paths

- **Good release:** push a working commit; watch green come up at 10%, pass smoke, promote to 100%. Capture the `ingress traffic show` before/after.
- **Bad release:** push a commit whose `/health` returns 500 (or whose green revision crashes on a code path the readiness probe misses but your smoke check hits); watch the canary smoke check fail, traffic revert to blue, and the job go red. Capture the same before/after — traffic must show **blue at 100%, green at 0%** after the failed deploy, and **no user ever saw a 500** from the 90% on blue.

## Deliverables

1. The blue/green deploy job in `.github/workflows/deploy.yml` (replacing or extending the Lecture 2 deploy job).
2. A `BLUE-GREEN.md` writeup with:
   - The `ingress traffic show` output before, during (10% canary), and after, for **both** the good and the bad release.
   - The wall-clock time from "green starts" to "green at 100%" (good path) and to "reverted to blue" (bad path).
   - A paragraph on what blue/green catches that the simple readiness-probe deploy does not (a release that passes its own probe but fails under real traffic or on a path the probe does not exercise).
3. The captured pipeline run links (one green, one red-with-automated-revert).

## Acceptance criteria

- [ ] The app is in multiple-revision mode and the pipeline canaries at 10% before promoting.
- [ ] Green is smoke-checked on its **per-revision FQDN**, not via the shared ingress.
- [ ] A good release promotes to 100% automatically; the writeup shows the traffic shift.
- [ ] A bad release reverts traffic to blue automatically and fails the job; the writeup shows blue at 100% afterward and proves no user-facing 500 on the 90%.
- [ ] The rollback is automated (no human ran a command); it is in the pipeline.

## Stretch

1. **Progressive canary.** Instead of 10% → 100%, ramp 10% → 30% → 60% → 100% with a smoke check and a 60-second soak at each step. Discuss the trade between deploy speed and blast-radius control.
2. **Metric-based gate.** Instead of (or in addition to) the smoke check, query the green revision's error rate from your Week-14 metrics during the soak and abort if it exceeds a threshold. This is the bridge from "smoke check" to "real canary analysis."
3. **Session affinity caveat.** Turn on session affinity and explain why it complicates a percentage canary (a sticky user stays on green or blue), and when you would want it anyway.
