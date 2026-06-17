# Week 13 — Challenges

The exercises stand up the vertical slice; these two challenges make the green behind it *trustworthy*. Challenge 1 fills the `TokenForAsync` seam the integration exercise left open — it imports a real Keycloak realm into the Testcontainer, mints a real bearer token, and proves the backend's JWT middleware both accepts it and rejects a forged one, so "OIDC via Keycloak" is a fact CI can verify rather than a claim. Challenge 2 proves the observability you wired actually composes — one `CreateLesson` call from the Blazor admin produces *one* OpenTelemetry trace whose spans span the browser, the gRPC-Web hop, the domain activity, the EF Core insert, and PostgreSQL, all under a single trace id you read in a local collector. Both are project work on the capstone you are building, not detached puzzles: they are the parts of the integration baseline that turn "it works on my machine" into "it is green and observable."

## Ground Rules

- **Build on what you have.** Both challenges extend Exercises 1–3 and the running backend — do not rebuild from scratch. Challenge 1 implements the stub the integration test already calls; Challenge 2 confirms the OpenTelemetry wiring already present in `Program.cs`.
- **Test the real thing, not a stub.** No fake auth handler that fabricates a `ClaimsPrincipal`, and no spans that fail to connect. A real Keycloak validates the token; one trace id stitches every span. If the proof relies on a shortcut, the challenge is not met.
- **Capture your evidence and write it up.** Each challenge asks for a short write-up (`CHALLENGE-01.md` / `CHALLENGE-02.md`) plus concrete artifacts — realm JSON, a green negative test, a trace capture, a side-by-side log/trace correlation. The reasoning answers are the deliverable, not just a passing run.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|-----------|-----------|
| 1 | [challenge-01-keycloak-realm-and-token-minting.md](./challenge-01-keycloak-realm-and-token-minting.md) | A `workshop-realm.json` that seeds the client, two users, and roles; a working `TokenForAsync` that mints a real token against the Keycloak container; and a negative test proving a wrong-issuer token is rejected | Advanced | ~2.5 hrs |
| 2 | [challenge-02-otel-trace-across-the-slice.md](./challenge-02-otel-trace-across-the-slice.md) | A local OpenTelemetry Collector and a browser `traceparent` injection that make one `CreateLesson` call emit one trace spanning Blazor → gRPC-Web → service → EF Core → PostgreSQL, with log-and-trace correlation | Advanced | ~2.5 hrs |

## How to Submit (Self-Check)

1. **Verify the deliverables listed in the challenge.** Challenge 1 wants the realm JSON (confirm the `Imported realm workshop` line in `docker logs`), a green slice test, and a green wrong-issuer negative test. Challenge 2 wants the collector config + run command, a capture of one trace with the full span tree under one trace id, the browser-`traceparent` propagation proof, and a side-by-side log/trace id match.
2. **Write the short answers honestly.** Complete `CHALLENGE-01.md` / `CHALLENGE-02.md` with the reasoning questions each challenge poses — why the password grant is fine for tests but not production, why the EF Core span is a child of the domain activity, and the rest. The explanation is what proves you understood the slice, not just ran it.
3. **Confirm it is green and leaves nothing behind.** Run the integration suite end to end; containers should be reaped (`docker ps` is clean afterward), the traces should connect, and a teammate reading only your write-up and captures should be able to reproduce the result.
