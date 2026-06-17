# Week 13 — Exercises

These four exercises are not warm-ups; they are the capstone milestone broken into the order you should build it. Together they walk the **integration baseline** for the Polyglot Workshop one layer at a time: first you lock the `workshop.proto` contract and the domain entities behind it (E1), then you implement that contract on both the gRPC and REST surfaces with a real proto↔entity mapping (E2), then you prove it works on real infrastructure with a `WebApplicationFactory<Program>` test over a Testcontainers PostgreSQL and Keycloak (E3), and finally you show the contract is genuinely language-neutral by reaching the same service from a TypeScript gRPC-Web browser client (E4). Do them in order — each one builds on the last, and the order *is* the lesson: contract first, depth before breadth. By the end you have the integration baseline in miniature, which the mini-project then assembles into the full repository with Serilog, OpenTelemetry, and CI.

## How to Run an Exercise

These are capstone build steps. There is no template to copy from — you stand up the `Workshop.sln` solution as you go, and each exercise tells you the exact projects and packages to add.

1. **Confirm your toolchain.** This week targets .NET 9 / C# 13 across every project. Verify with `dotnet --version` (expect `9.0.x`). Exercises 3 and the challenges also need a container runtime reachable over the Docker socket — check with `docker info`.
2. **Open the exercise file and read the header.** Every `.cs` file opens with a goal, the project layout it expects, the exact `dotnet new` / package-add commands, and numbered acceptance criteria. Build the projects it names before writing code.
3. **For the C# exercises (`exercise-01`…`exercise-03`),** create the projects, paste in the provided code, and complete the `TODO(you)` sections — the compiler tells you what is missing. Run `dotnet build` for E1/E2 (expect `Build succeeded · 0 warnings · 0 errors`) and `dotnet test` for E3 (it spins real Postgres + Keycloak containers, applies migrations, and asserts the vertical slice end to end).
4. **For `exercise-04` (the `.ts` client file),** this is the browser side of the contract. Set up the `admin-web/` folder, install `grpc-web` + `google-protobuf`, generate the TypeScript stubs from the *same* `workshop.proto` with `protoc`, then build with `tsc` (`npm run build`, expect 0 type errors). It is a client against the backend the C# exercises stand up.
5. **Keep the build green across the board.** A green build that leaves one project broken is not green — the capstone build-succeeded promise spans the backend, the clients, and the tests together.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|-----------|-----------|
| 1 | [exercise-01-vertical-slice-plan.cs](./exercise-01-vertical-slice-plan.cs) | Author the `workshop.proto` contract and the domain entities (`Lesson`, `Enrollment`, `Submission`) with `required` members and factory invariants; write down the one vertical path and three scope cuts | Intermediate | 45–60 min |
| 2 | [exercise-02-contract-and-mapping.cs](./exercise-02-contract-and-mapping.cs) | Implement the gRPC service over the generated base class, mirror `CreateLesson` as a Minimal-API REST endpoint, build the `WorkshopDbContext`, and write the proto↔entity mapping that keeps both surfaces honest | Intermediate+ | 60–75 min |
| 3 | [exercise-03-testcontainers-integration.cs](./exercise-03-testcontainers-integration.cs) | Stand up a `WebApplicationFactory<Program>` integration test over Testcontainers PostgreSQL + Keycloak, apply real migrations, mint a real token, and assert the create→enroll→submit→pending-queue slice end to end | Advanced | 75–90 min |
| 4 | [exercise-04-blazor-grpc-web-client.ts](./exercise-04-blazor-grpc-web-client.ts) | Configure a gRPC-Web client in TypeScript against the same service the .NET clients hit, attach the OIDC bearer token as metadata, and translate gRPC status codes into typed errors | Intermediate | 45–60 min |

## Checking Your Work

Annotated solutions for all four exercises live in [SOLUTIONS.md](./SOLUTIONS.md) — read each one *after* you attempt the exercise; the value is in the reasoning and the lecture/citation links, not the answer. Use these self-checks first:

- **The build/test output matches.** E1 and E2 end on `Build succeeded · 0 warnings · 0 errors`; E2's mapping unit test passes; E3 runs green against the real `postgres:16-alpine` and `keycloak:25.0` containers and leaves nothing behind (`docker ps` shows no lingering containers after the run); E4's `tsc` build reports 0 type errors.
- **Identity comes from the token, never the request.** No message in your proto carries a caller-identity field — no `instructor_id` on `CreateLessonRequest`, no `learner_id` on `SubmitRequest`. Both surfaces read the `sub` claim from the validated token.
- **The integration test mocks nothing that matters.** The E3 factory overrides only the connection string and the OIDC authority — no `UseInMemoryDatabase`, no stubbed auth scheme — and it applies migrations with `MigrateAsync`, not `EnsureCreated`. If yours does either of the anti-patterns, the green is hollow.
