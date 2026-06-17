# Week 13 — Quiz

Ten multiple-choice questions covering vertical-slice planning, the contract-first build order, `WebApplicationFactory<T>`, Testcontainers, Serilog, and OpenTelemetry. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — The build order

You are starting the Polyglot Workshop capstone. Which order best matches "ship a vertical slice on day one"?

- (A) Build the MAUI lesson screen, then the Blazor admin form, then wire up a backend to whatever shapes those screens assumed.
- (B) Write `workshop.proto`, generate the server and client stubs, then build one thin path (create → enroll → submit → appears in queue) all the way through before adding any breadth.
- (C) Build the entire backend with every endpoint, fully tested, then build the two clients against it.
- (D) Build the database schema first, then generate the proto from the schema, then the clients.

## Question 2 — Identity in the contract

In `workshop.proto`, `SubmitRequest` has `lesson_id` and `content` but no `learner_id`. Why?

- (A) Protobuf does not support string fields for ids.
- (B) The learner's identity comes from the validated bearer token's `sub` claim, read server-side; a client-supplied identity would let any caller impersonate any learner.
- (C) `learner_id` is implied by the gRPC channel and added automatically by the framework.
- (D) It is an oversight; `learner_id` should be added to the request.

## Question 3 — One contract, three clients

The MAUI client uses native gRPC and the Blazor admin uses gRPC-Web to reach the *same* `WorkshopService`. What is the essential reason the Blazor client cannot use native gRPC?

- (A) Blazor WebAssembly cannot reference the `Grpc.Net.Client` package.
- (B) Browsers do not expose HTTP/2 trailers to JavaScript, and native gRPC carries its final status in trailers; gRPC-Web moves the status into the message body so a browser can read it.
- (C) gRPC is slower than gRPC-Web and Blazor needs the speed.
- (D) Keycloak only issues tokens that gRPC-Web can carry.

## Question 4 — The most common gRPC-Web setup bug

A Blazor gRPC-Web call always fails with a "no status" error even though the server log shows the call succeeded. The most likely cause is:

- (A) The token expired between negotiate and invoke.
- (B) `UseGrpcWeb` was registered after `MapGrpcService`.
- (C) The server's CORS policy does not expose the `Grpc-Status` and `Grpc-Message` response headers, so the browser strips them and the client never sees the status.
- (D) The proto was compiled with `GrpcServices="Server"` instead of `"Both"`.

## Question 5 — `WebApplicationFactory<T>` overrides

In the integration test factory for the capstone, which override is correct for an *honest* integration baseline?

- (A) Replace the `DbContext` with `UseInMemoryDatabase` and register a fake "Test" auth scheme.
- (B) Override only the connection string and the OIDC authority to point at the Testcontainers PostgreSQL and Keycloak, leaving the real Npgsql provider and the real JWT middleware in place.
- (C) Mock the gRPC service so the test does not touch the database.
- (D) Override nothing; run the test against the production database.

## Question 6 — `MigrateAsync` vs `EnsureCreated`

The integration test fixture applies the schema with `context.Database.MigrateAsync()` rather than `EnsureCreated()`. Why does this matter for the baseline?

- (A) `EnsureCreated` is slower than `MigrateAsync`.
- (B) `EnsureCreated` builds the schema from the model and bypasses the migration files, so a migration bug passes the test; `MigrateAsync` runs the actual migrations that will run in production.
- (C) `EnsureCreated` cannot create indexes.
- (D) `MigrateAsync` is required by Testcontainers.

## Question 7 — Testcontainers granularity

The capstone starts its PostgreSQL and Keycloak containers in an `IAsyncLifetime` collection fixture rather than per-test. The main reason is:

- (A) Per-test containers would leak because Ryuk only reaps collection fixtures.
- (B) Starting a fresh PostgreSQL and Keycloak per test would be prohibitively slow; per-collection shares them across the tests in the collection while keeping isolation between collections.
- (C) xUnit does not allow containers inside a `[Fact]`.
- (D) Keycloak can only run one container per machine.

## Question 8 — Structured logging

Serilog's advantage over the default `Microsoft.Extensions.Logging` console output, for a system you must debug from logs alone, is:

- (A) Serilog is faster at writing to the console.
- (B) Serilog logs are structured events — a message template plus a typed property bag — so `LessonId` and `InstructorId` become queryable fields rather than text spliced into a sentence.
- (C) Serilog automatically encrypts log output.
- (D) Serilog replaces OpenTelemetry, so you only need one library.

## Question 9 — What OpenTelemetry adds over logs

You have Serilog structured logs already. What does OpenTelemetry tracing add that logs alone do not?

- (A) Nothing; traces and logs are the same thing.
- (B) A trace is a tree of spans across one request — the gRPC call as parent, the EF Core query as child — so you can see where a 200ms request spent its time and how the call flowed, not just discrete events.
- (C) OpenTelemetry stores logs to disk; Serilog only writes to the console.
- (D) OpenTelemetry validates JWT tokens.

## Question 10 — The green-CI contract

The Week 13 milestone is "integration tests green in CI." A team reports the baseline is "done — the integration suite passes on my laptop." Per the milestone, this is:

- (A) Complete; a green local run is the milestone.
- (B) Not complete; the milestone requires the integration suite to be green in CI (Testcontainers starting the containers inside the runner) on every push, so "it works" is a fact CI verifies, not a claim.
- (C) Complete only if the laptop is a Mac.
- (D) Not relevant; CI is a Week 15 concern.

---

## Answer key

**Q1 — (B).** The contract-first, depth-before-breadth order. (A) is the UI-first order that produces three vocabularies for one concept and a Monday reconciliation (Lecture 1 §1). (C) builds breadth before any client agreement; (D) inverts the source of truth — the proto is the source, not the schema. Cite Lecture 1 §§1–2 and <https://wiki.c2.com/?WalkingSkeleton>.

**Q2 — (B).** Identity is the token's `sub` claim, read server-side; a wire `learner_id` is impersonation waiting to happen. This is the single most common gRPC contract security mistake (Lecture 1 §3). Cite <https://protobuf.dev/programming-guides/proto3/>.

**Q3 — (B).** Native gRPC carries its final status in HTTP/2 trailers, which browsers do not expose to JS; gRPC-Web reframes the status into the body so a browser can read it (Lecture 2 §6). Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>.

**Q4 — (C).** Without `WithExposedHeaders("Grpc-Status", "Grpc-Message", ...)` the browser's CORS layer strips the status header and the client sees "no status" on every call even though the server responded correctly (Lecture 2 §6, SOLUTIONS E4). The most common gRPC-Web setup failure.

**Q5 — (B).** Override only the *addresses*. (A) is the anti-pattern that makes the test prove nothing — `UseInMemoryDatabase` skips Npgsql, a stubbed auth scheme skips token validation. The real provider and the real JWT middleware must do their real jobs against the real containers (Lecture 3 §2).

**Q6 — (B).** `MigrateAsync` runs the real migration files, so a passing test is a statement about the migrations (the same ones that run in production); `EnsureCreated` bypasses them and lets a migration bug pass (Lecture 3 §4). Cite <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>.

**Q7 — (B).** Per-collection is the right granularity: per-test would be prohibitively slow; the collection fixture shares the containers across its tests while collections stay isolated (Lecture 3 §3). Ryuk reaps regardless of granularity, so (A) is false.

**Q8 — (B).** Structured events make properties queryable. Cite <https://github.com/serilog/serilog/wiki/Structured-Data> and Lecture 3 §6. (D) is wrong — Serilog and OpenTelemetry are complementary, not substitutes.

**Q9 — (B).** A trace is the *tree* across a request; logs are discrete events. The trace tells you *how the call flowed and where the time went* (Lecture 3 §7). Cite <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>.

**Q10 — (B).** The milestone is green-in-CI, not green-on-a-laptop; Testcontainers makes the same real test run in the runner, so "it works" is a fact CI verifies on every push (Lecture 3 §8, mini-project F7). Cite <https://docs.github.com/actions>.
