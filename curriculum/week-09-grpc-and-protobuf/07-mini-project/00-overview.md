# Mini-Project — Crunch Counter: a Distributed Counter Service with C# and Python Clients

> Build a small distributed counter service in C# and .NET 8 with a proto3 schema, a unary `Increment` RPC, a server-streaming `Subscribe` RPC, a C# client, and a Python client. Prove the cross-language wire compatibility by running both clients against the same server, with a logging interceptor recording every call, deadlines on every call, and TLS protecting the channel. By the end you have a small, schema-driven, two-language gRPC service that an operator at a real company would be willing to put behind a load balancer.

This is the canonical "build a small gRPC service end-to-end" exercise for .NET 8. The shape is genuinely production-shaped: a `.proto` schema in a known location, a C# server with structured error handling, two clients in two languages reading from the same schema, observability in the form of an interceptor, and deadlines on every call. Every senior .NET engineer who has shipped gRPC has built something with this exact skeleton. The mini-project is that experience in microcosm.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A solution called `CrunchCounter` with four projects plus a Python client tree:

- `protos/counter.proto` — the shared schema.
- `src/CrunchCounter.Server/` — the C# gRPC server (`net8.0`) implementing one unary RPC and one server-streaming RPC.
- `src/CrunchCounter.Client/` — a C# console client that exercises both RPCs, drives a small load test, and emits per-call logs.
- `tests/CrunchCounter.Tests/` — xUnit tests asserting correctness, deadline propagation, error mapping, and the interceptor wiring.
- `clients/python/` — a Python client (`grpcio` 1.62+) that exercises the same two RPCs against the same C# server. Demonstrates cross-language wire compatibility.

You ship one solution. The three `src/`-and-`tests/` projects each have their own `.csproj`. The Python client lives in its own directory with a `requirements.txt`.

---

## The schema

`protos/counter.proto`:

```proto
syntax = "proto3";

package crunch.counter.v1;
option csharp_namespace = "Crunch.Counter.V1";

import "google/protobuf/timestamp.proto";

// One increment applied to a counter. Used both as the request body for
// Increment and as the payload of server-streaming events.
message IncrementRequest {
  string counter_name = 1;
  int64 delta = 2;
}

message IncrementResponse {
  string counter_name = 1;
  int64 new_value = 2;
  google.protobuf.Timestamp applied_at = 3;
}

message SubscribeRequest {
  string counter_name = 1;
}

message CounterEvent {
  enum Kind {
    KIND_UNSPECIFIED = 0;
    KIND_INCREMENT = 1;
    KIND_SNAPSHOT = 2;        // emitted on subscribe so client has current state
  }
  Kind kind = 1;
  string counter_name = 2;
  int64 value = 3;
  google.protobuf.Timestamp at = 4;
}

service Counter {
  // Unary. Validates counter_name (non-empty, <= 64 chars), atomically applies
  // the delta, returns the new value.
  rpc Increment(IncrementRequest) returns (IncrementResponse);

  // Server-streaming. Subscribes to a single counter and yields a SNAPSHOT
  // event immediately, then one INCREMENT event per applied delta until the
  // client disconnects or the deadline expires.
  rpc Subscribe(SubscribeRequest) returns (stream CounterEvent);
}
```

This schema is the contract. Every project — C# server, C# client, Python client, xUnit tests — references this single `.proto`.

---

## Rules

- **You may** read Microsoft Learn, grpc.io documentation, the protobuf 3 language guide, the Week 9 lecture notes and exercises, the `grpc/grpc-dotnet` source, and any free .NET or Python documentation.
- **You may NOT** depend on third-party NuGet packages other than:
  - `Grpc.AspNetCore` (server) and `Grpc.Net.Client` (client) and `Grpc.Tools` (build) and `Google.Protobuf` (always).
  - `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Console`.
  - `xUnit`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `FluentAssertions` for the test project.
- Target framework: `net8.0` for every C# project. C# language version: the default (`12.0`).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props` from the start.
- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in `Directory.Build.props`.
- No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` anywhere in `src/` or `tests/`.
- Every public async method ends in `Async` and accepts a `CancellationToken` as its last parameter.
- Every client RPC call sets a `deadline:`.
- Server-side validation throws `RpcException` with a specific `StatusCode`, never a raw `ArgumentException`.
- The server has a logging interceptor recording method, status code, elapsed time, and the `x-correlation-id` from request headers.
- The client has a correlation-id interceptor that attaches `x-correlation-id` when missing.
- `EnableDetailedErrors` is `false` on the server (this is a production-shaped project).
- Channels are created once per client process and reused.

---

## Project structure

```
CrunchCounter/
├── Directory.Build.props
├── CrunchCounter.sln
├── protos/
│   └── counter.proto
├── src/
│   ├── CrunchCounter.Server/
│   │   ├── CrunchCounter.Server.csproj
│   │   ├── Program.cs
│   │   ├── Services/CounterService.cs
│   │   ├── Interceptors/LoggingServerInterceptor.cs
│   │   ├── State/CounterStore.cs
│   │   └── appsettings.json
│   │
│   └── CrunchCounter.Client/
│       ├── CrunchCounter.Client.csproj
│       ├── Program.cs
│       ├── Interceptors/CorrelationIdInterceptor.cs
│       └── Scenarios/LoadDriver.cs
│
├── tests/
│   └── CrunchCounter.Tests/
│       ├── CrunchCounter.Tests.csproj
│       ├── IncrementTests.cs
│       ├── SubscribeTests.cs
│       ├── DeadlineTests.cs
│       └── ErrorMappingTests.cs
│
└── clients/
    └── python/
        ├── requirements.txt
        ├── counter.proto         (symlink or copy of protos/counter.proto)
        ├── client.py
        └── README.md
```

---

## Acceptance criteria

### Server (`CrunchCounter.Server`)

- [ ] Builds with 0 warnings, 0 errors.
- [ ] `Program.cs` registers gRPC with the logging interceptor.
- [ ] `CounterService` inherits from `Counter.CounterBase` and overrides `Increment` and `Subscribe`.
- [ ] `Increment` validates `counter_name` (non-empty, ≤ 64 chars) and throws `RpcException` with `StatusCode.InvalidArgument` on violations.
- [ ] `Increment` rejects `delta = 0` with `StatusCode.InvalidArgument` (no-op deltas are bugs).
- [ ] `Increment` is atomic: concurrent calls against the same counter never lose updates. Implement with `Interlocked.Add` or an in-memory `ConcurrentDictionary<string, long>`.
- [ ] `Subscribe` emits a `SNAPSHOT` event immediately (with the current counter value).
- [ ] `Subscribe` respects `context.CancellationToken` — exits the loop within 100ms of cancellation or deadline.
- [ ] Listens on HTTPS using the `dotnet dev-certs` certificate.
- [ ] `EnableDetailedErrors = false`.

### Client (`CrunchCounter.Client`)

- [ ] Builds with 0 warnings, 0 errors.
- [ ] Constructs **one** `GrpcChannel` for the lifetime of the process.
- [ ] Attaches the `CorrelationIdInterceptor` to every call.
- [ ] Every RPC call sets a `deadline:` argument.
- [ ] Provides three CLI subcommands:
  - `crunch-counter inc <name> <delta>` — one unary `Increment` call.
  - `crunch-counter watch <name>` — subscribes and prints events until Ctrl+C.
  - `crunch-counter load <name> <total>` — fires N increments in parallel with a small concurrency limit, prints the final value.

### Tests (`CrunchCounter.Tests`)

- [ ] All tests pass.
- [ ] `IncrementTests`: a successful increment, an `InvalidArgument` on empty name, an `InvalidArgument` on `delta=0`, and a concurrency test that fires 1000 increments and asserts the final value.
- [ ] `SubscribeTests`: a subscription yields a SNAPSHOT event first, then an INCREMENT for every applied delta; cancellation aborts within 100ms.
- [ ] `DeadlineTests`: a deliberately-slow handler (introduce a 1-second delay on a special counter name like `"slow"`) called with a 300ms deadline produces `RpcException` with `StatusCode.DeadlineExceeded` client-side.
- [ ] `ErrorMappingTests`: every documented validation rule maps to the expected `StatusCode`.

### Python client (`clients/python/`)

- [ ] `requirements.txt` pins `grpcio==1.62.0` and `grpcio-tools==1.62.0`.
- [ ] `client.py` regenerates Python stubs from `counter.proto` and calls both `Increment` and `Subscribe`.
- [ ] Successful `Increment` from Python alters the value as seen by a subsequent C# `Increment` against the same counter.
- [ ] Successful `Subscribe` from Python receives the SNAPSHOT event and at least one INCREMENT event when a parallel C# client increments the same counter.
- [ ] Python `RpcError` correctly distinguishes `INVALID_ARGUMENT` and `DEADLINE_EXCEEDED` cases (use `e.code()` for the gRPC status).

---

## Day-by-day plan

### Thursday afternoon (2h) — Scaffolding and the proto

1. `dotnet new sln`. `dotnet new grpc` for the server. `dotnet new console` for the client. `dotnet new xunit` for the tests.
2. Place `counter.proto` at `protos/counter.proto`. Link it into all three .NET projects with `<Protobuf Include="..\..\protos\counter.proto" GrpcServices="..." Link="Protos\counter.proto" />`.
3. `dotnet build`. Confirm the generated types compile.
4. Stub `CounterService` with the four method overrides throwing `Unimplemented`. Build, run, confirm Kestrel listens on HTTPS.

### Friday morning (3h) — Server, interceptor, basic client

1. Implement `CounterStore` with `ConcurrentDictionary<string, long>` and `Increment(string, long)` returning the new value.
2. Implement `Increment` and `Subscribe` against the store. Wire `CounterStore` as a singleton in DI.
3. Implement `LoggingServerInterceptor`. Register in `Program.cs`.
4. Implement `CorrelationIdInterceptor` on the client.
5. Implement the three CLI subcommands in `Program.cs`.
6. Run end-to-end: server in one terminal, `crunch-counter inc alice 5` in another, watch the server log.

### Friday afternoon (2h) — Tests

1. Set up the xUnit project. Use `Grpc.AspNetCore.Server.ClientFactory`'s `WebApplicationFactory<Program>` pattern (or roll your own with `WebHost.CreateDefaultBuilder`) to host the server in-process for tests.
2. Write `IncrementTests`, `SubscribeTests`, `DeadlineTests`, `ErrorMappingTests`. Run all.

### Saturday (2.5h) — Python client

1. `pip install grpcio==1.62.0 grpcio-tools==1.62.0` in a venv.
2. Copy or symlink `counter.proto` into `clients/python/`.
3. Generate Python stubs.
4. Write `client.py` with both `increment` and `watch` modes.
5. Run a cross-language scenario: Python `subscribe` in one terminal, C# `inc` in another. Watch events flow.

### Sunday (0.5h) — Polish

1. Run `dotnet build` once more. Verify 0 warnings.
2. Update `README.md` with the actual run commands.
3. Tag the commit.

---

## What you will be graded on

| Area                              | Weight |
|-----------------------------------|-------:|
| Schema correctness (proto3 hygiene, reserved fields, well-known types) |  15% |
| Server implementation (atomicity, validation, error mapping)           |  20% |
| Client implementation (channel reuse, deadlines, interceptors)         |  15% |
| Test coverage (the four areas listed)                                  |  20% |
| Python cross-language client (works end-to-end)                        |  15% |
| Observability (interceptor logs, structured logging)                   |  10% |
| Build hygiene (0 warnings, no `.Result`, project structure)            |   5% |
| **Total**                                                              | **100%** |

The passing bar is **80**. The "you would put this behind a load balancer" bar is **90**.

---

## A note on `dotnet build`

Some students will run this in an environment without the .NET 8 SDK installed. Installing the SDK is your responsibility — <https://dotnet.microsoft.com/download> is free and the installer takes two minutes. Likewise, Python 3.10+ for the Python client is your responsibility (<https://www.python.org/downloads/>). The starter files assume `dotnet --version` reports `8.0.x` and `python3 --version` reports `3.10+`. The C# build pipeline runs `protoc` via the `Grpc.Tools` MSBuild integration — you do not need a separate `protoc` install for the .NET side. The Python side does need `grpcio-tools`, installed via pip.

---

## Submission

Zip the entire `CrunchCounter/` tree (excluding `bin/`, `obj/`, `.venv/`) as `crunch-counter-<your-name>.zip`. Commit to your branch with the message:

```
mini-project: crunch-counter — distributed counter with C# and Python clients
```

Push and open a PR against `main`. The PR description should include:

1. The output of `dotnet test` showing all tests passing.
2. A short demo: `crunch-counter watch alice` in one window, `crunch-counter inc alice 1` ten times in another, the event log showing the SNAPSHOT + ten INCREMENTs.
3. The output of `python3 client.py subscribe alice` running concurrently and receiving the same events.

If `dotnet test` is not green, the PR is not reviewable — fix the failures first.
