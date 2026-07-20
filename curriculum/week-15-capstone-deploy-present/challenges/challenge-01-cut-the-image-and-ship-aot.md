# Challenge 1 — Cut the `Workshop.Api` Image in Half, Ship a Native AOT Companion, and Measure Cold Start and Size Before and After

> **Time:** 2 hours. **Prerequisites:** Lecture 1; Exercises 1 and 2; a working `Workshop.Api` and `Workshop.AnalyticsExport`. **Citations:** the containerize-a-.NET-app guide at <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>, the .NET container images catalogue at <https://github.com/dotnet/dotnet-docker>, the chiseled-images doc at <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>, the Native AOT deployment doc at <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>, and the Docker build-cache doc at <https://docs.docker.com/build/cache/>.

## The premise

You have the multi-stage Dockerfile from Lecture 1 and the AOT companion from Exercise 2. This challenge turns "I followed the lecture" into "I measured it." You will produce a before/after table for two artifacts — the API image and the analytics CLI — across image size, build time, and cold start, and you will explain every number. The skill is not "make it smaller"; it is "prove the smaller thing is the same thing, and know what you traded."

Image size is not a vanity metric. It is pull time on every cold scale-out, attack surface on every CVE scan, and storage cost on every registry. A 113 MB chiseled image pulls in roughly half the time of the 226 MB `aspnet:9.0` image and a seventh of the time of the 810 MB single-stage mistake; on a free-tier Container App that scales to zero and back, that pull time is part of your cold-start latency, paid on the first request after every idle window. So "smaller" cashes out as faster scale-out, fewer vulnerabilities, and lower cost — but only if the smaller image still runs the identical bytes of `Workshop.Api`. The measurement is how you prove it does.

By the end you will have produced: (a) a measurements table comparing single-stage, multi-stage `aspnet:9.0`, and multi-stage chiseled for the API; (b) a measurements table comparing framework-dependent JIT, self-contained JIT, and Native AOT for the analytics CLI; and (c) a written analysis of which artifact you would ship for the capstone and why. The two artifacts pull in opposite directions — the API host wants the *managed-runtime* chiseled image (it is long-lived; JIT is fine and AOT would forbid EF Core), the analytics CLI wants *Native AOT* (it is cold and short-lived). Proving both with numbers is the point.

## Setup

Start from a known baseline. Build the deliberately-wrong single-stage image first so you have the "before" number:

```dockerfile
# Dockerfile.single — the WRONG baseline, for measurement only.
FROM mcr.microsoft.com/dotnet/sdk:9.0
WORKDIR /app
COPY . .
RUN dotnet publish src/Workshop.Api/Workshop.Api.csproj -c Release -o /app/publish
ENTRYPOINT ["dotnet", "/app/publish/Workshop.Api.dll"]
```

```bash
docker build -t workshop-api:single -f Dockerfile.single .
docker images workshop-api:single --format "{{.Size}}"
```

Then build the two improved API images (multi-stage `aspnet:9.0`, multi-stage chiseled) from Lecture 1, and the three CLI builds (framework-dependent, self-contained, AOT). The only difference between the two improved API images is the final `FROM` line — everything above it (the `sdk:9.0` build stage, the restore-before-copy ordering, the publish) is identical:

```dockerfile
# workshop-api:multi      -> FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
# workshop-api:chiseled   -> FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
```

Keeping the build stage byte-for-byte identical across the two is what lets you attribute the entire size delta to the runtime base — a controlled experiment, one variable changed.

Where the bytes go, conceptually, so the table tells a story rather than just listing numbers:

```text
  single-stage (810 MB)  =  SDK (~700) + NuGet restore cache + your publish output
  multi-stage  (226 MB)  =  managed runtime + ICU globalization data + your DLLs
  chiseled     (113 MB)  =  managed runtime + your DLLs        (no shell, no apt, no ICU-extras)
                              ^----- the SDK and the OS userland are what you deleted -----^
```

Measurement hygiene matters or your table is noise. Three rules:

- **Cold build means cold cache.** Before timing a "cold" build, clear BuildKit's cache (`docker builder prune -f`) so the restore actually runs. A build that reused a cache you forgot to clear will report a fake-fast cold number and your layer-cache proof becomes meaningless.
- **Run cold-start timings three times and take the median.** The first `docker run` after a build can pay a one-time image-decompression cost; the steady number is what you report. Note any outlier and why.
- **Pin the runtime base by digest while measuring** so an SDK patch landing mid-experiment doesn't shift your numbers under you. Record the digest in `IMAGE-REPORT.md` next to the table.

A measurement helper for cold start — start the container, time until `/healthz` answers, stop it:

```bash
cold_start_ms() {
  local image=$1
  docker run -d --name cs -p 8080:8080 \
    -e ASPNETCORE_ENVIRONMENT=Development "$image" >/dev/null
  local start=$(date +%s%3N)
  until curl -sf http://localhost:8080/healthz >/dev/null 2>&1; do
    [ $(( $(date +%s%3N) - start )) -gt 30000 ] && { echo "timeout"; break; }
  done
  echo "$(( $(date +%s%3N) - start )) ms"
  docker rm -f cs >/dev/null
}
```

## Part A — the API image

Build all three API images and fill in this table:

| Image | Base | Size | Build time (cold) | Build time (1 `.cs` edit) | Cold start to `/healthz` |
|-------|------|-----:|------------------:|--------------------------:|-------------------------:|
| `workshop-api:single` | `sdk:9.0` | | | | |
| `workshop-api:multi` | `aspnet:9.0` | | | | |
| `workshop-api:chiseled` | `aspnet:9.0-noble-chiseled` | | | | |

The "1 `.cs` edit" column is the layer-cache proof: edit one source file, rebuild, and time it. The multi-stage builds should be dramatically faster on the second build because the restore layer is `CACHED`. If yours is not, your `COPY` order is wrong — you copied source before restoring. Confirm with `docker build` output showing `CACHED` on the restore step. Citation: <https://docs.docker.com/build/cache/>.

## Part B — the analytics CLI

Build all three CLI variants and fill in this table:

| Build | Command | Image size | Publish time | Cold start (`--help`) |
|-------|---------|-----------:|-------------:|----------------------:|
| Framework-dependent JIT | `dotnet publish` | | | |
| Self-contained JIT | `dotnet publish -r linux-x64 --self-contained` | | | |
| Native AOT | `dotnet publish -r linux-x64 /p:PublishAot=true` | | | |

The AOT build must publish with **zero** `IL2xxx`/`IL3xxx` warnings. If you have them, you have a latent runtime failure (almost always reflection-based JSON); fix the source-generated `JsonSerializerContext` before you trust the binary. Citation: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations>.

## The shape your numbers should take

You are measuring on your own hardware, so absolute values will differ — but the *ratios* are structural and the grader checks the ratios, not the milliseconds. For reference, here is the order of magnitude a quiet laptop produces, so you can sanity-check whether your run is in the right ballpark or whether you have a misconfiguration:

```text
Part A — Workshop.Api image
  single-stage  (sdk:9.0)          ~810 MB   build ~95s cold / ~70s after .cs edit
  multi-stage   (aspnet:9.0)       ~226 MB   build ~90s cold / ~12s after .cs edit  (restore CACHED)
  multi-stage   (chiseled)         ~113 MB   build ~92s cold / ~12s after .cs edit  (restore CACHED)

Part B — Workshop.AnalyticsExport
  framework-dependent JIT          ~226 MB   publish ~8s   cold --help ~480 ms
  self-contained   JIT              ~95 MB   publish ~14s  cold --help ~430 ms
  Native AOT                        ~28 MB   publish ~40s  cold --help  ~35 ms
```

Read the two tells. In Part A, the "after .cs edit" build collapses from ~70s to ~12s on the multi-stage images — that drop *is* the layer cache, and the build log must show `CACHED` on the `dotnet restore` step to prove it. The single-stage image shows almost no improvement on the second build because it copies and restores everything in one undifferentiated layer. In Part B, the AOT publish is the *slowest* to build (it compiles to native code) but produces the *smallest* image and the *fastest* cold start — that inversion (you pay at build time to win at run time) is the entire AOT trade and your analysis must name it. If your AOT cold start is hundreds of milliseconds rather than tens, you almost certainly still have a reflection-JSON path that the AOT compiler could not statically resolve; re-check for `IL` warnings.

## What you traded — the part the analysis must own

A size win is never free; the challenge grades whether you can name the cost, not whether you pretended there wasn't one.

- **Chiseled has no shell.** You cannot `docker exec -it … sh` into a running chiseled container to poke around — there is no `sh`, no `cat`, no `ps`. You debug it the way you will debug it in production: through logs (`az containerapp logs show`) and probes, from outside. That is a discipline win disguised as a limitation, but it *is* a change in how you operate, and pretending otherwise is how people get stuck at 3am. Cite <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>.
- **Native AOT forbids things.** No runtime reflection over types the compiler could not see, no runtime code generation, no `Assembly.Load`, and — the one that bites — no reflection-based `System.Text.Json`. EF Core's model building and expression compilation fall on the wrong side of this line, which is the structural reason the API host stays JIT and only the Dapper-based analytics CLI goes AOT. Cite <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations>.
- **AOT moves cost from run time to build time.** Your CI gets slower (the AOT publish compiles native code) so that every cold invocation gets ~10x faster. For a CLI run thousands of times that trade is obviously right; for the API host, whose startup is paid once per revision, it is obviously wrong. Knowing which side of that line a binary sits on is the actual skill.

## Acceptance criteria

1. Both measurement tables are filled in with real numbers from your machine, captured in `IMAGE-REPORT.md`.
2. The chiseled API image is at least 40% smaller than the multi-stage `aspnet:9.0` image, and both are at least 60% smaller than the single-stage baseline.
3. The "1 `.cs` edit" rebuild of a multi-stage image is faster than its cold build, and the build output shows `CACHED` on the restore layer — proving the layer-cache trick.
4. The Native AOT CLI image is at least 50% smaller than the self-contained JIT build and starts at least 5x faster, with a zero-warning publish.
5. `IMAGE-REPORT.md` includes a 200-word analysis answering: which API image you ship for the capstone and why; whether you would AOT the API host (and why not); and one concrete downside of the chiseled image you accepted.
6. You prove the chiseled API image runs the *same* application as the `aspnet:9.0` one: boot both, hit `/readyz`, and confirm an identical 200 with the same JSON body and the same Serilog "Now listening on" line. "Smaller" only counts if it is the same program — a size win that broke a dependency is a regression, not an optimization.
7. The AOT analytics CLI is functionally verified, not just measured: run it against a real Postgres connection string and diff its CSV/JSON output against the JIT build's output for the same query. They must be byte-identical. A fast binary that emits wrong data fails the challenge.

## Stretch goals

1. **`docker scout` or Trivy the images.** Scan the single-stage, `aspnet:9.0`, and chiseled images for known CVEs and tabulate the count. The chiseled image should report dramatically fewer — fewer packages, fewer vulnerabilities. Explain why "smaller is safer" is a security claim, not just a size claim. Cite <https://docs.docker.com/scout/>.
2. **`distroless`-equivalent root filesystem audit.** `docker run --rm --entrypoint /bin/sh workshop-api:chiseled -c 'ls /'` and observe it fails (no shell). Then `docker export` the chiseled container and `tar tf` the result; count the files and compare to the `aspnet:9.0` image. Explain what an attacker loses when there is no shell to live off. Cite <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>.
3. **Multi-arch build.** Use `docker buildx build --platform linux/amd64,linux/arm64` to produce a multi-architecture API image and push it. Note which CI runner architecture you would need and what AOT cross-compilation costs for the arm64 variant. Cite <https://docs.docker.com/build/building/multi-platform/>.
4. **The GHA layer cache, measured.** Wire `cache-from: type=gha` / `cache-to: type=gha,mode=max` into a `docker/build-push-action` step (the CI equivalent of the local layer cache) and measure the build time of a no-change rebuild on a *fresh* runner — it should approach the local cached time, not the cold time, because the restore layer is pulled from the GitHub Actions cache rather than recomputed. Report the cold-runner vs warm-cache delta and explain why this matters for every push to `main`. Cite <https://docs.docker.com/build/cache/backends/gha/>.
5. **Quantify the pull-time payoff.** `docker push` each API image to a registry, then `docker rmi` it locally and `time docker pull` it cold. Tabulate pull time against image size for the three API images and show the relationship is roughly linear. Connect the number to the free-tier scale-to-zero story: this pull time is part of the cold-start latency on the first request after an idle window. Cite <https://learn.microsoft.com/en-us/azure/container-apps/scale-app>.

## Deliverable

`IMAGE-REPORT.md` in the capstone repo, containing: the two filled-in measurement tables with the base-image digests recorded; a one-line note of your cold-build cache-clear procedure; the `docker history` excerpt proving no SDK layer in the chiseled final image; the byte-identical-output diff result for the AOT CLI; and the 200-word analysis. This report is the artifact the capstone defense points at when a grader asks "why this image?" — it is not busywork, it is the evidence behind a sentence you will say out loud in the defense.

The shape of the win, stated once so you can put it in the analysis: the API image dropped ~86% from the single-stage mistake to chiseled while running byte-identical code, and the analytics CLI dropped ~70% from self-contained JIT to AOT while starting ~10x faster — and the reason those are *different* artifacts (managed-runtime chiseled vs native AOT) is the reason you cannot apply one recipe to everything. Long-lived host: shrink the OS, keep the runtime. Cold short-lived tool: compile away the runtime. Measure both, ship the right one for each, and you can defend the choice instead of asserting it.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/dotnet/core/docker/build-container>, <https://learn.microsoft.com/en-us/dotnet/core/docker/container-images>, <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>. Source/registry references: the .NET image catalogue at <https://github.com/dotnet/dotnet-docker> and the chiseled-image docs at <https://github.com/dotnet/dotnet-docker/blob/main/documentation/ubuntu-chiseled.md>. External: the Docker build-cache docs at <https://docs.docker.com/build/cache/> and Docker Scout at <https://docs.docker.com/scout/>.
