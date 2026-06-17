# Week 15 — Challenges

The exercises get the capstone deployed and reversible. **These two challenges harden the way it ships.** Both are ~2-hour, portfolio-grade builds that extend the Lecture 2 pipeline into the practices a real platform team runs: a blue/green (canary) deploy on Azure Container Apps that promotes a release only after it passes a smoke check on its own per-revision URL — and automatically reverts to the old revision if it does not — and a supply-chain layer that attaches an SBOM, a keyless cosign signature, and a provenance attestation to every image, then gates the deploy on a vulnerability scan. Each proves *both* paths: the good release that promotes and the bad one the safety net catches. Do them and you do not just have a live URL — you have a rollout the rest of the community would trust to run.

## Ground Rules

- **Prove both paths, not just the happy one.** Every challenge requires you to ship one good release *and* one deliberately broken one, and to capture the evidence that the automated safety net (revert to blue, fail the scan gate) actually fired. A safety net you never trip is a safety net you cannot trust.
- **Automate the safety net — no human in the loop.** The rollback on a failed canary and the deploy-blocking CVE gate must live in the pipeline, not in a command you remember to run. If recovery depends on you being awake, it is not recovery.
- **Capture the proof.** Each challenge names a writeup (`BLUE-GREEN.md`, `SUPPLY-CHAIN.md`) and the exact outputs it must contain — traffic-weight tables, `cosign verify` / `gh attestation verify` results, CVE counts, pipeline run links. Build the artifact as you go; it is the deliverable.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|------------|-----------|
| 1 | [challenge-01-blue-green-on-container-apps.md](./challenge-01-blue-green-on-container-apps.md) | A blue/green (canary) deploy job: put the app in multiple-revision mode, bring green up at 0% traffic, canary 10% and smoke-check green's per-revision FQDN, promote to 100% on success, and automatically revert to blue (and fail the job) on a failed smoke check — proven with one good and one bad release. | Advanced | 120 min |
| 2 | [challenge-02-supply-chain-sbom-and-signing.md](./challenge-02-supply-chain-sbom-and-signing.md) | Supply-chain hardening on the pipeline: generate and attach an SPDX SBOM, sign the image digest keyless with cosign over OIDC, attach a SLSA provenance attestation, and gate the deploy on a grype scan that blocks high/critical CVEs — ordered build-test → publish → scan → deploy. | Advanced | 120 min |

## How to Submit (Self-Check)

1. **Show both paths working.** Push a good release and a deliberately broken one, and capture the before/during/after evidence for each — Challenge 1's `ingress traffic show` tables (blue at 100% after a failed canary; no user-facing 500) and Challenge 2's pinned-vulnerable-then-fixed run where the scan blocks the deploy and the fix lets it through.
2. **Verify from outside the pipeline.** Run the independent checks from your own machine: for Challenge 1, confirm the per-revision FQDN smoke check; for Challenge 2, run `cosign verify` with an identity regexp scoped to your repo (and confirm a wrong identity *fails*) and `gh attestation verify` showing the building commit and workflow.
3. **Write up and link it.** Complete the named writeup (`BLUE-GREEN.md` or `SUPPLY-CHAIN.md`) with the required outputs and the pipeline run links (one green, one red-with-automated-safety-net), commit it to your Week 15 repository, and confirm every acceptance-criteria box in the challenge file is checked.
