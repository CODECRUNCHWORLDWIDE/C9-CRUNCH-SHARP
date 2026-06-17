# Week 9 — Exercises

These exercises take you hands-on with Protocol Buffers and gRPC on the .NET stack. You'll design a proto3 schema from a real-world spec, implement and drive all four gRPC call types, and wire up deadlines, cancellation propagation, and interceptors. Each file ships with a scaffold recipe, acceptance criteria, and reflection questions — work through them in order, since each one builds on the last. Take your time; the goal is fluency with the wire format and the call shapes, not speed.

## How to Run an Exercise

Every exercise pairs a `.proto` schema with a `.cs` driver. To run one:

1. Make sure the .NET 8 SDK is installed (`dotnet --version` should report 8.x), and run `dotnet dev-certs https --trust` once so the gRPC TLS endpoint works.
2. Follow the "HOW TO USE THIS FILE" recipe at the top of each `.proto`. It scaffolds the project(s) — `dotnet new classlib` for Exercise 1, and a `dotnet new grpc` server plus a `dotnet new console` client for Exercises 2 and 3 — and tells you where to drop the `.proto` and the code from the matching `.cs` file.
3. Add the `Protobuf` item and the `Google.Protobuf` / `Grpc.Tools` / `Grpc.Net.Client` package references shown in the recipe, then `dotnet build`.
4. Run it. Exercise 1 is a single console program (`dotnet run -c Release`). Exercises 2 and 3 run a server in one terminal (`dotnet run --project src/ExNN.Server`) and a client in another (`dotnet run --project src/ExNN.Client`).
5. Check your output against the acceptance criteria, then answer the reflection questions in a `results-exNN.md` of your own.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|-----------:|----------:|
| 1 | [exercise-01-design-a-proto.proto](./exercise-01-design-a-proto.proto) / [exercise-01-design-a-proto.cs](./exercise-01-design-a-proto.cs) | Designing a proto3 schema (enums, `oneof`, `repeated`, well-known types, `optional`, `reserved`) and proving the round-trip with `CalculateSize()` and byte-size prediction | Intermediate | 60 min |
| 2 | [exercise-02-four-call-types.proto](./exercise-02-four-call-types.proto) / [exercise-02-four-call-types.cs](./exercise-02-four-call-types.cs) | Implementing and driving all four gRPC call types — unary, server-streaming, client-streaming, and bidirectional — across a C# server and client | Intermediate+ | 90 min |
| 3 | [exercise-03-deadlines-and-interceptors.proto](./exercise-03-deadlines-and-interceptors.proto) / [exercise-03-deadlines-and-interceptors.cs](./exercise-03-deadlines-and-interceptors.cs) | Deadlines, cancellation propagation, status codes, and writing a server logging interceptor plus a client correlation-id interceptor | Advanced | 90 min |

## Checking Your Work

Annotated solutions live in [SOLUTIONS.md](./SOLUTIONS.md), including completed schema fragments, illustrative program output, reflection answers, and the common mistakes to avoid. Make a serious attempt first, then read your work against it. For each exercise, verify that:

- `dotnet build` (and `dotnet run`) succeeds with 0 warnings and 0 errors on every project.
- The program output meets the acceptance-criteria checklist at the top of the file — for example, predicted and actual byte sizes agree in Exercise 1, all four call types print their expected results in Exercise 2, and the short-deadline call surfaces `DeadlineExceeded` in Exercise 3.
- You can answer the reflection questions in your own words, not just observe the behavior.
