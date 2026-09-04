# Week 13 — Quiz

Ten multiple-choice questions covering the contract-first discipline, code generation, EF Core 9 + PostgreSQL, the vertical slice, the integration baseline with `WebApplicationFactory` and Testcontainers, and green-in-CI. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — What is the source of truth

In the Polyglot Workshop, the single declaration of the domain shape (`Lesson`, `Enrollment`, …) lives in:

- (A) The EF Core entities; the contract is generated from them.
- (B) A hand-written shared C# DTO library each client copies.
- (C) `workshop.proto`; the service and every client consume the *generated* types, never a hand-rolled parallel DTO.
- (D) An OpenAPI document that each client reads at runtime.

<details>
<summary>Answer</summary>

**(C).** The capstone's architectural bet is that `workshop.proto` is the single source of truth and that the service and every client consume the *generated* types. A hand-rolled DTO in a client breaks the single-source rule. Citation: <https://protobuf.dev/programming-guides/proto3/> and the contract-first framing in Lecture 1.

</details>

## Question 2 — Why proto3 enums start at zero

`LessonStatus` is declared with `LESSON_STATUS_UNSPECIFIED = 0` as its first member. The reason is:

- (A) Protobuf sorts enums numerically and zero must be first.
- (B) proto3 treats an unset enum scalar as `0`, so a zero `UNSPECIFIED` lets you distinguish "explicitly draft" from "client forgot to set it."
- (C) C# requires enums to start at zero.
- (D) Zero is reserved for the wire framing and cannot hold a real value.

<details>
<summary>Answer</summary>

**(B).** proto3 has no "absent" for scalar fields; an unset enum reads as `0`. A zero `UNSPECIFIED` member preserves the distinction between an explicit value and a forgotten one. Citation: <https://protobuf.dev/programming-guides/proto3/#enum>.

</details>

## Question 3 — `Grpc.Tools` and `PrivateAssets`

The `Workshop.Contracts.csproj` references `Grpc.Tools` with `<PrivateAssets>All</PrivateAssets>`. This is because:

- (A) `Grpc.Tools` is a build-time code generator that must not flow transitively to consumers as a runtime dependency.
- (B) It hides the package from `dotnet list package`.
- (C) It is required for the generated client to be `public`.
- (D) Without it, the `.proto` is not compiled.

<details>
<summary>Answer</summary>

**(A).** `Grpc.Tools` runs `protoc` at build time; it is a tool, not a runtime library, so `PrivateAssets="All"` stops it flowing transitively to anything referencing `Workshop.Contracts`. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>.

</details>

## Question 4 — `GrpcServices="Both"`

The `<Protobuf Include="workshop.proto" GrpcServices="Both" />` item generates:

- (A) Only the message types, no service code.
- (B) Both the abstract `WorkshopBase` server class (the service overrides it) and the concrete `WorkshopClient` (every client constructs it).
- (C) Two copies of the messages, one for the client and one for the server.
- (D) A REST and a gRPC surface from the same file.

<details>
<summary>Answer</summary>

**(B).** `GrpcServices="Both"` emits the abstract `WorkshopBase` (overridden by the service) and the concrete `WorkshopClient` (constructed by clients). `"Server"` alone would omit the client; `"Client"` alone would omit the base class. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics#generated-c-assets>.

</details>

## Question 5 — Why entities are separate from contract messages

The lecture keeps the EF Core `Enrollment` entity separate from the generated `Enrollment` message and maps between them in `ToContract`. The motivation is:

- (A) EF Core cannot persist protobuf-generated types.
- (B) So that a persistence concern (a `timestamptz`, a navigation property, a `Guid` key) never leaks into the wire contract and a wire concern (a string GUID, an int enum) never dictates a column type.
- (C) Performance — mapping is faster than direct persistence.
- (D) There is no real reason; it is boilerplate.

<details>
<summary>Answer</summary>

**(B).** Separating the wire shape from the persistence shape keeps database concerns out of the contract and wire concerns out of the schema; the single `ToContract` boundary is where they meet. EF Core *can* persist many shapes, so (A) overstates; the real reason is design isolation. Citation: <https://learn.microsoft.com/en-us/ef/core/modeling/>.

</details>

## Question 6 — The vertical slice

"Ship a vertical slice on day one" means:

- (A) Finish all five entities, then all the service methods, then the first client.
- (B) Build the thinnest path that touches every layer — proto message → entity → migration → service method → client call — and get *that* green before building breadth.
- (C) Build the UI first and stub the backend.
- (D) Write every integration test before any production code.

<details>
<summary>Answer</summary>

**(B).** A vertical slice is the thinnest end-to-end path through every layer. Finishing one slice green de-risks the architecture; horizontal layer-by-layer building defers the integration risk to the worst possible moment. Citation: <https://www.jimmybogard.com/vertical-slice-architecture/>.

</details>

## Question 7 — Idempotent enroll

`Enroll` reads for an existing `(lesson_id, learner_id)` enrollment before inserting, and the database also has a unique index on that pair. The relationship between the two is:

- (A) Redundant; one of them should be removed.
- (B) The read-first branch is the primary idempotency mechanism (returns the same enrollment on a repeat call); the unique index is the backstop against a concurrent race that slips past the read.
- (C) The unique index is the primary mechanism; the read is decorative.
- (D) The read prevents the index from ever firing, so the index is dead code.

<details>
<summary>Answer</summary>

**(B).** The read-first branch makes a repeat enroll return the same enrollment (the primary idempotency behavior); the unique index is the database backstop for a race where two requests both pass the read before either inserts. Using the index violation *as* the idempotency mechanism turns an expected case into an exception. Citation: <https://learn.microsoft.com/en-us/ef/core/modeling/indexes>.

</details>

## Question 8 — Why a real PostgreSQL in tests

The integration baseline uses a real PostgreSQL 16 via Testcontainers rather than SQLite-in-memory. The reason is:

- (A) SQLite is slower than a container.
- (B) SQLite-in-memory hides Npgsql-specific behavior — `timestamptz` round-tripping, the unique-index violation shape, snake_case case folding — that the baseline exists to catch.
- (C) Testcontainers cannot run SQLite.
- (D) The contract requires PostgreSQL at the wire level.

<details>
<summary>Answer</summary>

**(B).** SQLite-in-memory is a different provider and silently papers over Npgsql-specific behavior — exactly the behavior the baseline must validate. The capstone's rule is a real PostgreSQL via Testcontainers, every time. Citation: <https://dotnet.testcontainers.org/modules/postgres/>.

</details>

## Question 9 — The gRPC client in the integration test

The integration test builds its `WorkshopClient` over `Server.CreateHandler()` rather than `GrpcChannel.ForAddress("https://localhost:7080")`. The reason is:

- (A) Port 7080 is reserved.
- (B) `Server.CreateHandler()` routes the gRPC call through the in-memory `TestServer`'s pipeline — real middleware, routing, and service — with no TCP socket and no separately-running server.
- (C) `ForAddress` cannot construct a `WorkshopClient`.
- (D) The handler form skips authentication, which the test needs.

<details>
<summary>Answer</summary>

**(B).** `WebApplicationFactory`'s `Server.CreateHandler()` gives an `HttpMessageHandler` that drives the in-memory `TestServer` pipeline directly. The `WorkshopClient` over that handler exercises the real service with no socket and no separately-hosted server. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/test-services>.

</details>

## Question 10 — Green in CI, not green locally

Milestone 1's pass condition is the integration baseline green on a GitHub Actions runner. A baseline that passes locally but fails in CI most commonly fails because:

- (A) GitHub runners do not support .NET 9 at all.
- (B) Of the runner OS, a missing/unreachable Docker daemon for Testcontainers, an SDK-version mismatch, or a cold-start image-pull timeout — none of which your warm laptop exhibits.
- (C) The tests are non-deterministic by nature and cannot pass in CI.
- (D) `WebApplicationFactory` only works on Windows.

<details>
<summary>Answer</summary>

**(B).** "Works locally, red in CI" is almost always the runner OS, the Docker socket Testcontainers needs, the pinned SDK version, or a cold-runner image-pull timeout — none present on your warm dev box. Challenge 2 makes you reproduce and fix each. Citation: <https://docs.github.com/en/actions/use-cases-and-examples/building-and-testing/building-and-testing-net>.

</details>

---

## Self-assessment

- 9-10: you can ship this week's capstone milestone without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact pages.
- 5-6: re-read the lecture notes end to end and redo the exercises, paying particular attention to the contract↔entity boundary and the Testcontainers-in-CI sections.
- 0-4: rewind to Lecture 1 and read all three lecture notes carefully. The milestone assembles every pattern the quiz tests; it will not make sense without the conceptual foundation, and Weeks 14–15 build on the same repo.
