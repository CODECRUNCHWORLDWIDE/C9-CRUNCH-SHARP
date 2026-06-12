# Challenge 2 — Supply-Chain Hardening: SBOM, Image Signing with cosign, Provenance Attestation, and a Vulnerability Gate

> **Time:** 2 hours. **Prerequisites:** Lectures 1–2, Exercise 3 (you have a build/publish pipeline). **Citations:** the cosign docs at <https://docs.sigstore.dev/cosign/signing/signing_with_containers/>, keyless signing at <https://docs.sigstore.dev/cosign/signing/overview/>, SLSA provenance at <https://slsa.dev/provenance/v1>, the syft SBOM generator at <https://github.com/anchore/syft>, the grype scanner at <https://github.com/anchore/grype>, and the GitHub artifact-attestation docs at <https://docs.github.com/en/actions/security-guides/using-artifact-attestations-to-establish-provenance-for-builds>.

## The premise

"It built and the tests passed" answers *does the code work*. It does not answer *can I trust the bytes I am about to run in production* — was the image built by my pipeline from my source, or did someone push a look-alike image to my registry? Does it contain a dependency with a known critical CVE? Could an auditor, six months from now, prove which commit produced the running image?

Those are **supply-chain** questions, and the 2020s answer to them is a small stack of artifacts attached to the image:

- An **SBOM** (Software Bill of Materials) — a machine-readable list of every package in the image, so you can answer "is log4shell in here?" with a query instead of a guess.
- A **signature** — a cryptographic proof that *your pipeline* produced this exact image digest, so a tampered or substituted image is detectable.
- A **provenance attestation** — a signed statement of *how* the image was built (which commit, which workflow, which runner), so the build is auditable.
- A **vulnerability gate** — a scan that fails the pipeline if the image ships a known critical CVE, so you do not deploy a known-bad dependency.

Your job is to add all four to the Polyglot Workshop pipeline, and to gate the deploy on the scan.

## Requirements

### R1 — Generate and attach an SBOM

Generate an SBOM for the built image and attach it to the image in the registry. Use `syft` (or the GitHub-native attestation, see R3):

```yaml
- name: Generate SBOM
  uses: anchore/sbom-action@v0
  with:
    image: ghcr.io/${{ github.repository }}:sha-${{ github.sha }}
    format: spdx-json
    output-file: sbom.spdx.json
```

The SBOM must list the .NET runtime, your NuGet dependencies, and the base-image OS packages. Confirm you can answer "which version of `Npgsql` is in the image?" from the SBOM alone.

### R2 — Sign the image with cosign (keyless)

Sign the image digest using cosign's **keyless** (Sigstore/Fulcio) flow, which mints a short-lived certificate bound to the workflow's OIDC identity — no private key to store, no key to leak:

```yaml
- uses: sigstore/cosign-installer@v3
- name: Sign the image (keyless)
  env:
    COSIGN_EXPERIMENTAL: "1"
  run: |
    DIGEST=$(docker buildx imagetools inspect \
      ghcr.io/${{ github.repository }}:sha-${{ github.sha }} \
      --format '{{json .Manifest.Digest}}' | tr -d '"')
    cosign sign --yes "ghcr.io/${{ github.repository }}@$DIGEST"
```

This requires `permissions: id-token: write` (the same OIDC primitive the Azure login uses). Verify the signature from your laptop:

```bash
cosign verify ghcr.io/<org>/polyglot-workshop@<digest> \
  --certificate-identity-regexp "https://github.com/<org>/polyglot-workshop/.*" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com"
```

The `--certificate-identity-regexp` is the load-bearing part: it asserts the signature came from *your repo's* workflow, not just "some Sigstore identity."

### R3 — Provenance attestation

Attach a build-provenance attestation that binds the image digest to the building workflow and commit. The GitHub-native action is the simplest correct path:

```yaml
- uses: actions/attest-build-provenance@v1
  with:
    subject-name: ghcr.io/${{ github.repository }}
    subject-digest: ${{ steps.build.outputs.digest }}
    push-to-registry: true
```

Verify it:

```bash
gh attestation verify oci://ghcr.io/<org>/polyglot-workshop@<digest> --owner <org>
```

The attestation is a SLSA provenance statement; reading the verified output should show the source repo, the commit SHA, and the workflow that built it. Citation: <https://slsa.dev/provenance/v1>.

### R4 — Vulnerability scan as a deploy gate

Scan the image with `grype` and **fail the pipeline on a critical (or high) severity finding**, before the deploy job runs:

```yaml
- name: Scan the image (gate deploy on it)
  uses: anchore/scan-action@v4
  with:
    image: ghcr.io/${{ github.repository }}:sha-${{ github.sha }}
    fail-build: true
    severity-cutoff: high
```

The chiseled base image (Lecture 1) is your friend here: a smaller image has fewer packages and therefore fewer CVEs to triage. Run the scan once on a full `aspnet:9.0` (non-chiseled) base and once on `aspnet:9.0-noble-chiseled` and **count the findings** — the chiseled image should have materially fewer. This is the security argument for chiseled, made concrete.

### R5 — Wire the gate into the pipeline order

The scan must run **after publish and before deploy**, so a critical CVE blocks the deploy the same way a red test blocks publish. Add it as a `needs:` link, or as a step in a `scan` job that `deploy` depends on:

```
build-test → publish → scan → deploy
```

## Deliverables

1. The extended `.github/workflows/deploy.yml` with SBOM generation, keyless signing, provenance attestation, and the grype gate wired between publish and deploy.
2. A `SUPPLY-CHAIN.md` writeup with:
   - The `cosign verify` output proving the image is signed by your workflow identity.
   - The `gh attestation verify` output showing the commit and workflow that built the image.
   - The grype finding count on `aspnet:9.0` vs `aspnet:9.0-noble-chiseled`, with the delta and a one-paragraph interpretation.
   - The SBOM excerpt answering "which version of `Npgsql` ships in the image?"
3. A captured pipeline run where a deliberately-pinned vulnerable dependency (pin an old package with a known high CVE) **fails the scan gate and blocks the deploy** — then the fix (bump the package) lets it through.

## Acceptance criteria

- [ ] The image is signed keyless; `cosign verify` with an identity regexp scoped to your repo passes, and a verify with the *wrong* identity fails.
- [ ] A build-provenance attestation is attached and `gh attestation verify` shows the correct commit + workflow.
- [ ] An SPDX SBOM is produced and you can answer a "is package X at version Y present" question from it.
- [ ] The grype scan gates the deploy: a high/critical finding blocks deploy; you proved it with a pinned-vulnerable then fixed dependency.
- [ ] The chiseled-vs-full base CVE count is captured with the delta.

## Stretch

1. **Verify-on-admission.** Configure the deploy step to `cosign verify` the image *before* `az containerapp update`, so an unsigned or wrong-identity image cannot be deployed even if someone bypasses the pipeline. Discuss where the real enforcement point is (admission control in the cluster) and why pipeline-side verify is necessary-but-not-sufficient.
2. **SBOM diff.** On each deploy, diff the new SBOM against the previously-deployed one and surface added/removed/changed packages in the PR. This turns "what changed in this release's dependencies" into a reviewable artifact.
3. **Pin the base by digest.** Replace `FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled` with the same image pinned by `@sha256:...` digest. Explain what this buys (reproducibility, defense against a re-tagged base) and what it costs (you must bump the digest to get base-image security updates), and add a Dependabot/Renovate config that bumps the pinned digest automatically.
