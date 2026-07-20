# Challenge 2 — Prove a Zero-Downtime Rollout and a One-Command Rollback on Azure Container Apps Revisions, with a Load Generator Watching for Dropped Requests

> **Time:** 2 hours. **Prerequisites:** Lectures 2 and 3; a deployed `Workshop.Api` on Azure Container Apps (or Fly.io) with readiness probes wired. **Citations:** the Container Apps revisions doc at <https://learn.microsoft.com/en-us/azure/container-apps/revisions>, the manage-revisions doc at <https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage>, the blue/green doc at <https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment>, the health-probes doc at <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>, and the Fly.io deploy doc at <https://fly.io/docs/launch/deploy/>.

## The premise

A deploy that drops requests is not zero-downtime, no matter how fast it is. This challenge proves the claim quantitatively: you will run a steady stream of requests against the deployed `Workshop.Api` while you roll out a new revision *and* while you roll back, and you will show the dropped-request count is zero (or you will find the bug that makes it non-zero and fix it). This is the operational backbone of the deploy contract — "one push reaches a live URL" is worthless if the push you reach with drops every in-flight request.

The mechanism you are proving is a handoff, not a swap. In multiple-revision mode, a new revision is created *alongside* the running one; the platform polls its readiness probe, and only once that probe reports healthy does it shift traffic weight. The old revision keeps serving every request until the moment traffic moves, and it is not torn down until after. There is never an instant when no ready revision is taking traffic. Picture the timeline:

```text
  t0  rev-A: 100% traffic, healthy        rev-B: (does not exist)
  t1  rev-A: 100% traffic, healthy        rev-B: created, probe polling, 0% traffic
  t2  rev-A: 100% traffic, healthy        rev-B: probe PASSED, 0% traffic
  t3  rev-A:   0% traffic (still running)  rev-B: 100% traffic, healthy   <- handoff
  t4  rev-A: deactivated                   rev-B: 100% traffic, healthy
       ^----- at no point is there a window with no ready revision -----^
```

Contrast single-revision mode, which replaces the running revision in place: there *is* a window — however brief — where the old one is going down and the new one is not yet up, and a request that lands in that window has nowhere to go. That window is the dropped request you are about to measure on the broken control run.

By the end you will have produced: (a) a load-generator log across a full rollout showing zero non-2xx responses attributable to the deploy; (b) the same across a rollback; and (c) a written explanation of exactly which two settings — the readiness probe and the revision traffic weight — make the difference, with a deliberately-broken control run proving it.

## Setup

Enable multiple-revision mode so old and new revisions coexist and traffic is split by weight (single-revision mode tears the old one down and *will* drop requests during the swap):

```bash
az containerapp revision set-mode -n workshop-api -g rg-workshop --mode multiple
```

Confirm readiness probes are wired (Lecture 3): `/readyz` gated on Postgres and Keycloak, `failureThreshold` and `initialDelaySeconds` set so a new revision is polled before it takes traffic. Verify the probe spec:

```bash
az containerapp show -n workshop-api -g rg-workshop \
  --query "properties.template.containers[0].probes" -o jsonc
```

A simple load generator — a steady ~20 req/s against a cheap authenticated endpoint, logging any non-2xx with a timestamp:

```bash
#!/usr/bin/env bash
# loadgen.sh — run for the duration of the rollout.
URL="https://<your-fqdn>/api/lessons"
TOKEN="<a valid bearer token>"
fail=0; total=0
end=$(( $(date +%s) + 120 ))   # run 2 minutes
while [ "$(date +%s)" -lt "$end" ]; do
  code=$(curl -s -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer $TOKEN" "$URL")
  total=$((total+1))
  if [ "$code" -lt 200 ] || [ "$code" -ge 300 ]; then
    fail=$((fail+1)); echo "$(date -u +%H:%M:%S.%3N) NON-2XX: $code"
  fi
  sleep 0.05
done
echo "total=$total fail=$fail"
```

(If you prefer a real tool, `hey -z 120s -q 20 -H "Authorization: Bearer $TOKEN" $URL` or `k6` works identically; the homemade loop is here so the measurement is transparent.)

Two refinements make the evidence stronger. First, classify the failures rather than just counting them — a `502`/`503` during the swap is the platform telling you there was no ready upstream, which is the dropped-request signature you care about; a `401` means your token expired mid-run (mint a fresh long-lived one) and a `429` means you hit a rate limit, neither of which is a deploy-attributable drop. Log the code so you can tell them apart:

```bash
case "$code" in
  502|503|000) echo "$(date -u +%H:%M:%S.%3N) DEPLOY-DROP: $code" ;;  # the one that matters
  2[0-9][0-9])  : ;;                                                   # fine
  *)            echo "$(date -u +%H:%M:%S.%3N) OTHER: $code" ;;        # 401/429 etc — not a deploy drop
esac
```

(`000` is curl's code for "connection failed entirely" — the most damning drop signature of all.) Second, stamp each served response with the revision that served it by adding a response header in the API (`context.Response.Headers["X-Revision"] = revisionSha;`) and capture it with `-w '%{http_code} %header{x-revision}'`. Now your log shows not just *that* every request was 2xx but *which* revision answered it — direct proof of the handoff moment and the foundation for the canary stretch goal.

## The rollout drill

1. Start `loadgen.sh` in one terminal. Note the start time.
2. In a second terminal, deploy a new revision (bump a trivial value, push to `main`, or `az containerapp update --image <registry>/workshop-api:<new-sha>`).
3. Watch the revisions: `watch -n2 'az containerapp revision list -n workshop-api -g rg-workshop -o table --query "[].{name:name, active:properties.active, traffic:properties.trafficWeight, health:properties.healthState}"'`.
4. Observe: the new revision appears, is polled by the readiness probe, and only after it reports healthy does traffic shift to it. The old revision keeps serving until then.
5. When `loadgen.sh` finishes, record `total` and `fail`.

A correct configuration reports `fail=0`: every request was served by *some* ready revision throughout, because the new revision took traffic only after readiness passed and the old one was not torn down until traffic moved.

One thing to get right so the zero is meaningful: the load generator must be running *across* the handoff, not finish before it or start after it. The deploy on a free-tier Container App can take a minute or more to pull the image and pass the readiness probe, so size the loadgen window (`end=$(( $(date +%s) + 180 ))`) to comfortably cover image pull, probe polling, and the traffic shift. Cross-check the loadgen start/end timestamps against the `revision list` snapshot timestamps and confirm the handoff (the moment `trafficWeight` moved from the old revision to the new one) falls *inside* the window. A `fail=0` from a run that ended before traffic ever shifted proves nothing — it measured a period when only one revision was ever serving.

## The rollback drill

1. Restart `loadgen.sh`.
2. Reweight 100% of traffic back to the previous revision in one command:
   ```bash
   az containerapp ingress traffic set -n workshop-api -g rg-workshop \
     --revision-weight workshop-api--rev-PREVIOUS=100
   ```
3. Observe: traffic moves instantly to the already-running previous revision — no image pull, no cold start, no migration (this is why migrations are expand-only).
4. Record `total` and `fail`. It should again be `fail=0`.

## The broken control run

Prove the settings matter by breaking one and watching requests drop:

- Set the mode to `single` (`az containerapp revision set-mode ... --mode single`), redeploy, and run the load generator across the swap. Single-revision mode replaces the running revision; depending on timing you will see a burst of non-2xx during the swap. Record it, then switch back to `multiple`.
- *Or* deploy a revision whose `/readyz` is deliberately broken (point it at a wrong DB password). Observe that the platform never shifts traffic to it — `fail=0` because the broken revision is correctly quarantined. This is the readiness gate doing its job; note the contrast with single-revision mode.

The two control runs prove different halves of the claim and you want both in the report:

```text
  control run            setting changed           expected loadgen result
  single-revision swap   mode: multiple -> single  fail > 0  (drops in the swap window)
  broken-readiness rev   /readyz cannot pass        fail = 0  (broken rev quarantined at 0% traffic)
```

The single-revision run shows what happens when you *remove* the safe-handoff machinery — requests drop. The broken-readiness run shows what happens when the handoff machinery is intact but the new revision is bad — the platform refuses to hand off, the old revision keeps serving, and the bad deploy harms no one. Said together: multiple-revision mode is what makes the handoff *possible*, and the readiness probe is what makes the handoff *safe*. Remove either and you have a worse deploy.

## Why the rollback is instant and safe

The rollback command reweights traffic to a revision that is *already running* — no image pull, no container start, no migration. That is why it returns in seconds and why it cannot itself drop requests: the previous revision never stopped being ready. But "instant" is only "safe" if the previous revision can still talk to the *current* database schema. That is the whole reason migrations are **expand-only**:

```text
  expand-only:  Deploy N adds  Submission.Feedback (nullable), writes both columns.
                rev N-1 (old) still works — it only reads Submission.Note, which still exists.
                rev N   (new) reads Feedback. Both revisions are valid against the schema. -> rollback safe.

  destructive:  Deploy N renames Note -> Feedback in one migration.
                rev N-1 (old) queries Submission.Note -> column missing -> every read 500s.
                rollback to N-1 reweights traffic to a revision that the schema can no longer serve. -> rollback BROKEN.
```

A `RenameColumn` is the canonical trap: the deploy succeeds, the new revision is happy, and the rollback button is now a loaded gun — pulling it sends traffic to a revision that 500s on the renamed column. Expand-then-contract (add nullable, dual-write, backfill, *then* a later deploy drops the old column) keeps every adjacent revision pair mutually compatible, which is the precondition that makes "reweight to last-known-good" a real safety net rather than a second outage.

## Acceptance criteria

1. Multiple-revision mode is enabled and the readiness probe spec is captured in `ROLLOUT-REPORT.md`.
2. A full rollout under load reports `fail=0`, with the load-generator output and the `az containerapp revision list` snapshots (before, during, after) captured.
3. A rollback under load reports `fail=0`, with the one-command reweight and the revision snapshots captured.
4. The broken control run (single-revision swap *or* broken-readiness revision) is captured, showing either dropped requests (single mode) or correct quarantine (broken readiness), with a one-paragraph explanation of which setting caused which outcome.
5. `ROLLOUT-REPORT.md` includes a 200-word section naming the two settings that produce zero-downtime — multiple-revision mode + a correct readiness probe — and explaining why migrations must be expand-only for the rollback to be safe.
6. The report states the rollout's measured statistic explicitly: `total` requests served, `fail=0` deploy-drops, and the wall-clock window during which both revisions showed nonzero or transitioning traffic in the `revision list` snapshots — so the zero is anchored to a real handoff that actually happened, not a deploy that finished before the load generator noticed.

## Stretch goals

1. **Canary by weight.** Instead of a 0→100 cutover, shift 90/10 to the new revision, run the load generator, confirm ~10% of responses carry the new revision's build header (add `X-Revision: <sha>` to a response header), then promote to 100. Explain how canary weighting lets you catch a bad revision on 10% of traffic instead of 100%. Cite <https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment>.
2. **Scale-to-zero cold-start measurement.** On the free tier the app scales to zero when idle. Let it scale down, then time the first request's latency (the cold start). Compare to the steady-state latency and explain why the Native AOT companion's fast cold start matters for scale-to-zero workloads — and why the API host's cold start is amortized differently. Cite <https://learn.microsoft.com/en-us/azure/container-apps/scale-app>.
3. **Do the same on Fly.io.** Reproduce the rollout/rollback drill with `flyctl deploy` (rolling and `bluegreen` strategies) and `flyctl releases`. Tabulate the operational differences: revision weights vs releases, the rollback command, how each platform gates traffic on health. Cite <https://fly.io/docs/launch/deploy/> and <https://fly.io/docs/reference/configuration/#http_service-checks>.

## Deliverable

`ROLLOUT-REPORT.md` in the capstone repo: the captured probe spec, the three labeled `revision list` snapshots (before / during the handoff / after) for both the rollout and the rollback, the load-generator logs with `total`/`fail` for each run, the broken-control-run output, and the 200-word settings explanation. This report backs the live-demo moment in the capstone defense where you reweight traffic to the previous revision and the grader watches `/readyz` stay 200 the whole time — the report is the proof that the live demo was not luck.

The line this challenge defends, in one sentence for the report: *a rollout and a rollback both served every request because a new revision only ever took traffic after its readiness probe passed, the old revision was never torn down until traffic had moved, and the schema stayed compatible across both revisions because the migration was expand-only.* If you can say that sentence and point at the numbers behind every clause of it, you have proven zero-downtime rather than asserted it — which is exactly the difference the capstone is graded on.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/azure/container-apps/revisions>, <https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage>, <https://learn.microsoft.com/en-us/azure/container-apps/blue-green-deployment>, <https://learn.microsoft.com/en-us/azure/container-apps/health-probes>, <https://learn.microsoft.com/en-us/azure/container-apps/scale-app>. External: the Fly.io deploy docs at <https://fly.io/docs/launch/deploy/> and the EF Core migrations-in-production guide at <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>.
