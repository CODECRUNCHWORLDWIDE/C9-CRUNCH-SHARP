# Week 12 — Exercises

These four exercises walk you from a single ASP.NET Core 8 host that serves three protocol surfaces all the way to an integration test suite that boots the whole thing against an ephemeral PostgreSQL container. Each one builds on the last — composition first, then observability, then persistence, then the tests that prove the lot composes — and together they form the "load-bearing pattern lookup table" you will lift from when you assemble the ProjectHub mini-project. Work them in order; every exercise is additive to the one before it, and each ships a `VERIFICATION` block so you can confirm your wiring with concrete `curl`, `grpcurl`, `psql`, and `dotnet test` commands before you move on.

## How to Run an Exercise

Each exercise is a self-contained C#/.NET 8 program with its setup, verification, and common-stumble notes written into the file header. To work one:

1. Read the comment header at the top of the `.cs` file: it lists the exact `dotnet new`, `dotnet add package`, and (where relevant) `dotnet tool install` commands you need. Run them to scaffold the project.
2. Implement the `TASK` markers in order. The scaffolding is real, compilable code with the cross-cutting wiring already shown; your job is to fill the gaps, add the proto/migration/test pieces called out, and make `dotnet build` come back clean (`Build succeeded · 0 warnings · 0 errors`).
3. Start the host with `dotnet run` (it listens on `https://localhost:5001`), or for Exercise 4 run `dotnet test`. A local PostgreSQL (`docker run ... postgres:16`) is needed for Exercise 3, and the Docker daemon must be running for Exercise 4's Testcontainers.
4. Walk the numbered `VERIFICATION` steps at the bottom of each file — `curl` for REST, `grpcurl` for gRPC, a query-string `negotiate` for SignalR, `psql` for the database, `dotnet test` for the suite. If every check matches its expected output, the exercise is done.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|------------|-----------|
| 1 | [exercise-01-compose-rest-grpc-signalr.cs](./exercise-01-compose-rest-grpc-signalr.cs) | Stand up one host that serves REST minimal APIs, gRPC, and SignalR; share a single JWT bearer scheme and a `RequireOrg` policy across all three; lift the SignalR token from the `access_token` query string; verify 401-vs-200 on every surface | Intermediate | 90 min |
| 2 | [exercise-02-serilog-and-opentelemetry.cs](./exercise-02-serilog-and-opentelemetry.cs) | Wire Serilog as the host logger with the compact JSON formatter and enrichers; register OpenTelemetry with custom `ActivitySource` spans plus the ASP.NET Core and HttpClient instrumentations; prove one `TraceId` threads through logs and spans on a self-fetch | Intermediate+ | 90 min |
| 3 | [exercise-03-ef-core-postgres-and-migrations.cs](./exercise-03-ef-core-postgres-and-migrations.cs) | Add the `ProjectHubDbContext`, the `Project`/`ProjectTask` entities, and an initial migration over Npgsql; apply it on startup; build org-scoped CRUD; reproduce and fix the "scoped service from singleton" trap with `IDbContextFactory` | Intermediate+ | 90 min |
| 4 | [exercise-04-integration-tests.cs](./exercise-04-integration-tests.cs) | Author xUnit integration tests with `WebApplicationFactory<Program>` and a fresh Testcontainers PostgreSQL container per class; assert on real `HttpClient` CRUD responses and on direct `DbContext` connectivity | Advanced | 90 min |

## Checking Your Work

Annotated, end-to-end solutions for all four exercises — including the sample Serilog log lines and OpenTelemetry trace exports you should be able to reproduce — live in [SOLUTIONS.md](./SOLUTIONS.md). Read it only after you have made a genuine attempt; the value is in comparing your wiring to the reference, not in copying it.

Before you call an exercise finished, self-check against these:

- **It builds and runs clean.** `dotnet build` reports `Build succeeded · 0 warnings · 0 errors`, and `dotnet run` (or `dotnet test`) starts without an unhandled exception.
- **Every `VERIFICATION` step matches.** Each command in the file's verification block produces the documented output — the right status codes on each protocol, the rows in Postgres, the passing test count.
- **The trace-ID contract holds.** Where the exercise crosses two surfaces (Exercise 2's self-fetch, the cross-protocol paths), one `TraceId` appears in both the structured log lines and the exported spans. If it varies, revisit `Enrich.FromLogContext()` and your `AddSource(...)` name.
