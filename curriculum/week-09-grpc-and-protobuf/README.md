# Week 9 — gRPC and Protocol Buffers in .NET 8: Proto3, the Four Call Types, Interceptors, Deadlines, and Cross-Language Clients

Welcome to **C9 · Crunch Sharp**, Week 9. Last week made you fluent in the in-process side of `async` correctness: the `await` lowering, `IAsyncEnumerable<T>`, bounded channels, `Parallel.ForEachAsync`, and the four canonical deadlocks. Channels solved producer/consumer *inside* a single process. This week we step across the network boundary. The question changes from "how do these two goroutines coordinate?" to "how do these two processes — written in two different languages, deployed to two different machines — agree on the *shape* of a message, the *semantics* of a call, and the *deadline* by which a reply is required?". The answer the industry has converged on, after RPC arguments lasting twenty years, is **Protocol Buffers** for the wire format and **gRPC** for the call semantics. By Friday you will be the person on your team who can write a `.proto` file, code-generate a typed server and client in C#, generate a matching client in Python, and explain to a curious junior why the wire bytes are smaller than JSON, the deadline propagates automatically, and the streaming primitives are first-class rather than glued-on.

The first thing to internalize is that **gRPC is two pieces, not one**. The first piece is **Protocol Buffers** (proto3), a schema language and a wire format. A `.proto` file declares message types and service interfaces; the protobuf compiler (`protoc`, or the `Grpc.Tools` MSBuild integration in .NET) reads that file and generates code in your target language. The wire format is a compact tagged-and-length-prefixed encoding — typically 3 to 10 times smaller than the equivalent JSON for the same data, and an order of magnitude faster to parse, because the tags are integers, the integers are varints, and the parser does not have to find string keys. The second piece is **gRPC** itself, a remote procedure call protocol layered over **HTTP/2**, with four call shapes: unary (one request, one response), server-streaming (one request, many responses), client-streaming (many requests, one response), and bidirectional streaming (many requests interleaved with many responses, both directions independent). These two pieces are independent in principle — you can use protobuf without gRPC, and you can use gRPC with other encodings in theory — but in practice 95% of gRPC traffic is protobuf and 95% of new protobuf usage is gRPC. We will treat them as a pair. Lecture 1 covers protobuf 3; lecture 2 covers gRPC itself.

The second thing to internalize is that **the four call types map to four very different programming models in C#**. A unary call is `Task<TResponse> CallAsync(TRequest, CallOptions)` — the shape you already know from any `HttpClient` method. A server-streaming call returns an `IAsyncEnumerable<TResponse>` (through a wrapper, `AsyncServerStreamingCall<TResponse>`) — the shape you internalized in Week 8 with `await foreach`. A client-streaming call gives you a writer (`IClientStreamWriter<TRequest>`) and a single response `Task<TResponse>` — you push N requests, complete the stream, await the reply. A bidirectional call gives you both a writer and a reader; you read and write concurrently, and the order between them is decided by your application. The choice between these is a *design* choice, not a *transport* choice: gRPC is HTTP/2 underneath, and all four shapes use the same HTTP/2 framing — but the API surface in C# and Python looks different enough that picking the wrong one early will cost you a refactor later. Lecture 2 walks through when to reach for each one.

The third thing to internalize is that **deadlines and cancellation propagate automatically through gRPC**, and this is one of the most under-appreciated features of the system. When a C# client sets `CallOptions.Deadline = DateTime.UtcNow.AddSeconds(2)`, that absolute timestamp is serialised into the `grpc-timeout` HTTP/2 header and sent to the server. The server's `ServerCallContext.CancellationToken` will fire when the deadline expires, *regardless of what the server is doing*. If the server is itself making outbound gRPC calls (a service mesh fan-out), it should pass that token into the outbound `CallOptions`, and the deadline will propagate again. The whole call graph becomes deadline-respecting without any per-hop bookkeeping. The flip side of this is that a server that *ignores* its `ServerCallContext.CancellationToken` is the gRPC equivalent of a runaway thread — it keeps doing work after the client has given up, wasting capacity. Lecture 3 covers this; the mini-project exercises both halves.

The fourth thing to internalize is that **error reporting in gRPC is `RpcException` with a `Status`, not exceptions-as-payloads**. When the server wants to tell the client "you sent a malformed request," it does *not* throw an arbitrary C# exception — it throws `new RpcException(new Status(StatusCode.InvalidArgument, "field 'name' was empty"))`, and that maps to a specific gRPC trailer the client decodes back into an `RpcException` with the same status code. There are 17 standard status codes (`OK`, `Cancelled`, `Unknown`, `InvalidArgument`, `DeadlineExceeded`, `NotFound`, `AlreadyExists`, `PermissionDenied`, `ResourceExhausted`, `FailedPrecondition`, `Aborted`, `OutOfRange`, `Unimplemented`, `Internal`, `Unavailable`, `DataLoss`, `Unauthenticated`) and one of them is the right answer for almost every failure mode you care about. Choosing well is a senior skill; choosing badly turns every error into "Internal" and your operators into people who cannot tell whether retrying is safe. Lecture 3 covers the table.

The fifth thing to internalize is that **the cross-language story is the entire point**. If you only ever called a C# service from a C# client, you would reach for ASP.NET Core minimal APIs (Week 2) or signalR or, if you wanted RPC semantics, `Microsoft.Extensions.Http`'s typed clients. The reason to take on gRPC's complexity — the `.proto` file, the code generation step, the HTTP/2 dependency — is that the *same `.proto` file* generates a C# server, a Python client, a Go client, a Java client, a Swift iOS client, and a TypeScript browser client (via `grpc-web`). Every consumer sees the same types, the same field numbers, the same call semantics. Wire compatibility is a *guaranteed property*, not a hope. We will prove this concretely in the mini-project: a C# server is queried by both a C# client and a Python client (`grpcio`), and the two clients show identical behaviour over the same wire.

## Learning objectives

By the end of this week, you will be able to:

- **Write** a proto3 `.proto` file from scratch: package declaration, service declaration with all four call types, message declarations with scalar fields, enums, `oneof`, `repeated`, nested messages, and the relevant well-known types (`google.protobuf.Timestamp`, `google.protobuf.Duration`, `google.protobuf.Empty`, `google.protobuf.Any`). Cite the protobuf 3 language guide for each construct.
- **Explain** the proto3 wire format at a level sufficient to predict the byte size of a simple message: varint encoding, tag = `(field_number << 3) | wire_type`, the four common wire types (`VARINT`, `I64`, `LEN`, `I32`), and why field numbers 1–15 cost one byte and field numbers 16–2047 cost two. Cite the encoding spec.
- **Configure** a `.csproj` for proto-first code generation: `Grpc.AspNetCore` on the server, `Grpc.Net.Client` + `Grpc.Net.ClientFactory` on the client, the `<Protobuf Include="..." GrpcServices="Server|Client|Both" />` MSBuild item, and the resulting `obj/Debug/net8.0/Protos/*.cs` generated output.
- **Implement** all four gRPC call types on a single C# server: a unary RPC, a server-streaming RPC, a client-streaming RPC, and a bidirectional streaming RPC. Override the base service class generated by `Grpc.Tools`, return `Task<TResponse>` for unary, write to `IServerStreamWriter<TResponse>` for streaming.
- **Consume** the same service from a C# client using `Grpc.Net.Client`: create a `GrpcChannel`, instantiate the typed `Greeter.GreeterClient`, call the four shapes, iterate server streams with `await foreach`, write client streams with the request stream's `WriteAsync`/`CompleteAsync` pair.
- **Consume** the same service from a Python client using `grpcio` + `grpcio-tools`. Generate Python stubs from the *identical* `.proto` file. Call all four shapes, observe the cross-language wire compatibility.
- **Propagate** deadlines through a gRPC call chain. Set `CallOptions.Deadline` on the outer call, observe the deadline reach the server as `ServerCallContext.Deadline`, pass it into outbound calls the server makes, and confirm `StatusCode.DeadlineExceeded` is the failure mode when the budget is exhausted.
- **Write** a client-side and a server-side `Interceptor`: log every call's method name and elapsed time, attach a correlation-id metadata header on the client, read it on the server, propagate it into the response trailers. Register interceptors in DI on both ends.
- **Map** server-side exceptions to gRPC `Status` codes correctly. Choose `InvalidArgument` for "field missing", `NotFound` for "row not present", `PermissionDenied` for "authn ok, authz failed", `Unauthenticated` for "no/invalid token", `Unavailable` for "downstream service down", `Internal` for "this is our bug". Defend each choice in one sentence.
- **Configure** TLS for a gRPC channel: `GrpcChannel.ForAddress("https://...", new GrpcChannelOptions { ... })` against an HTTPS endpoint with a valid certificate, the development-time `https://localhost` flow, and the channel-credentials hook for client certificates if needed. Explain why HTTP/2 over plain TCP (`h2c`) requires an opt-in flag in .NET 8.
- **Read** a `.proto` file written by someone else and predict, from the field numbers, which fields are new and which were present in v1 of the schema. Identify the rules of *forward and backward compatibility* (never reuse field numbers, never change types, reserve removed fields).
- **Distinguish** code-first gRPC (the `protobuf-net.Grpc` library, where you declare service contracts as C# interfaces and the library generates the schema at runtime) from proto-first gRPC (the mainline approach), and defend the choice — code-first only when you control every client and every client is .NET; proto-first in every other case.
- **Cite** Microsoft Learn's gRPC chapter, grpc.io's documentation, and the protobuf 3 language guide for each technique.

## Standards this week meets

| Bar | What this week is measured against |
| --- | --- |
| University | `EECS 280` — Past the outcome set: a second programming course's types live inside one program. This week makes the type definition itself the shared artefact, and shows what those types look like as bytes on a wire. |
| Industry | Publish a service contract, generate both sides from it, and evolve it so clients already deployed against version one keep working against version two. |
| Beyond the bar | A Python client generated from the identical `.proto` file, calling the C# server across all four call types — the wire-format guarantee tested rather than asserted — `challenges/challenge-01-cross-language-client.md` |

## Prerequisites

- **Week 2 of C9 complete.** You can write an ASP.NET Core minimal API, configure DI, and read an `appsettings.json`. gRPC ASP.NET Core is the same hosting model with a different request-pipeline middleware.
- **Week 4 of C9 complete.** You understand `Task<T>`, `CancellationToken`, and basic `async`/`await`. Streaming RPCs build directly on `IAsyncEnumerable<T>`.
- **Week 8 of C9 complete.** You can iterate an `IAsyncEnumerable<T>` with `await foreach` and propagate `CancellationToken`. Server-streaming gRPC is `IAsyncEnumerable<TResponse>` at the call site with an extra cancellation surface.
- **A working `dotnet --version` of `8.0.x`.** This week targets .NET 8 (LTS). The `Grpc.AspNetCore` 2.60+ and `Grpc.Net.Client` 2.60+ packages we use are stable on net8.0.
- **A working `python3 --version` of `3.10+`.** The cross-language exercises use `grpcio` and `grpcio-tools` from PyPI. Both install with `pip install grpcio grpcio-tools`; no compilation is required on macOS or Windows.
- **`grpcurl` recommended but not required.** A free command-line tool for calling a gRPC service from the shell, useful for "is the server up?" smoke tests. Install via `brew install grpcurl` on macOS or download a binary from the project's GitHub releases.
- **An understanding of TLS and HTTPS.** You know what a certificate is and what `https://localhost` means in .NET development. We will not re-derive PKI; we will use the dev certificate `dotnet dev-certs https --trust` produced in Week 2.

## Topics covered

- **The proto3 language.** `syntax = "proto3";`, `package`, `option csharp_namespace`, `message`, `enum`, `oneof`, `repeated`, nested messages, the scalar types (`int32`, `int64`, `uint32`, `uint64`, `sint32`, `sint64`, `fixed32`, `fixed64`, `float`, `double`, `bool`, `string`, `bytes`), the well-known types (`Timestamp`, `Duration`, `Empty`, `Any`, `FieldMask`, the wrapper types), `reserved` for compatibility.
- **The proto3 wire format.** Tag + wire type, varint encoding, length-delimited fields, packed encoding for repeated scalars, why the encoding is forward- and backward-compatible.
- **`Grpc.Tools` MSBuild integration.** The `<Protobuf>` item, `GrpcServices` attribute values, `ProtoRoot`, the generated code at `obj/Debug/net8.0/Protos/*.cs`, the partial-class extension points.
- **`Grpc.AspNetCore` on the server side.** `builder.Services.AddGrpc()`, `app.MapGrpcService<MyService>()`, the generated base class `MyService.MyServiceBase`, overriding the four RPC methods.
- **`Grpc.Net.Client` on the client side.** `GrpcChannel.ForAddress("https://...")`, typed-client instantiation, `CallOptions(headers, deadline, cancellationToken, writeOptions, propagationToken, credentials)`.
- **Unary RPCs.** `rpc GetX(GetXRequest) returns (GetXResponse);`. The C# server signature `public override Task<GetXResponse> GetX(GetXRequest request, ServerCallContext context)`. The C# client call `await client.GetXAsync(request, deadline: ...)`.
- **Server-streaming RPCs.** `rpc Subscribe(SubscribeRequest) returns (stream Event);`. The C# server signature with `IServerStreamWriter<Event> responseStream`. The C# client consumes `using var call = client.Subscribe(request, ...); await foreach (var ev in call.ResponseStream.ReadAllAsync(ct)) ...`.
- **Client-streaming RPCs.** `rpc Upload(stream Chunk) returns (UploadResult);`. The C# server consumes `IAsyncStreamReader<Chunk>`. The C# client writes `await call.RequestStream.WriteAsync(chunk); await call.RequestStream.CompleteAsync(); var result = await call.ResponseAsync;`.
- **Bidirectional streaming RPCs.** `rpc Chat(stream Message) returns (stream Message);`. Two concurrent loops on each side: one reading, one writing. Coordinated with `Task.WhenAll`.
- **Deadlines and cancellation.** `CallOptions.Deadline` on the client. `ServerCallContext.Deadline` and `ServerCallContext.CancellationToken` on the server. The `grpc-timeout` HTTP/2 header on the wire. Propagation into outbound calls via `CallOptions.WithDeadline(context.Deadline)` and `WithCancellationToken(context.CancellationToken)`.
- **Error mapping.** `RpcException`, `Status`, the 17 `StatusCode` values, the choice matrix, the `Status.Detail` string convention. Custom error-detail messages via `grpc.protobuf/google.rpc.Status` (sidebar; not core).
- **Interceptors.** `Interceptor` base class, `UnaryServerHandler`, `UnaryClientHandler`, the streaming variants. Registration via `services.AddGrpc(o => o.Interceptors.Add<MyInterceptor>())` server-side, `channel.Intercept(new MyClientInterceptor())` client-side. The correlation-id pattern.
- **Metadata and trailers.** `Metadata` collection, request headers vs response headers vs response trailers, `-bin` suffix for binary metadata.
- **TLS and channel credentials.** The default HTTPS path, the `https://localhost` development flow with `dotnet dev-certs`, `GrpcChannelOptions.HttpHandler` for custom certificate validation, the `h2c` (cleartext HTTP/2) opt-in via `System.Net.Http.SocketsHttpHandler` for local-only testing.
- **Code-first sidebar.** `protobuf-net.Grpc` — what it is, when it is the right answer (closed .NET ecosystems), when it is the wrong answer (any cross-language clients).
- **Cross-language workflow.** One `.proto` file, two code generators, two clients, identical wire bytes. `python -m grpc_tools.protoc -I ./protos --python_out=. --grpc_python_out=. ./protos/counter.proto`.

## Weekly schedule

The schedule adds up to approximately **33 hours**. Treat it as a target, not a contract. Schema work rewards a fresh mind; do not write your `.proto` files at 2am.

| Day       | Focus                                                                | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|----------------------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | proto3 language, wire format, scalar and well-known types            |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | gRPC over HTTP/2, the four call types, code generation pipeline      |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Wednesday | Deadlines, cancellation, errors, interceptors, TLS                   |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Thursday  | Cross-language: Python client over the same proto, challenge work    |    0.5h  |    0h     |     2h     |    0.5h   |   1h     |     2h       |    0.5h    |     6.5h    |
| Friday    | Mini-project — build Crunch Counter, server and both clients         |    0h    |    0h     |     1h     |    0.5h   |   1h     |     3h       |    0.5h    |     6h      |
| Saturday  | Mini-project polish, deadline propagation tests, observability       |    0h    |    0h     |     0h     |    0h     |   0h     |     2.5h     |    0h      |     2.5h    |
| Sunday    | Quiz, review, schema-evolution thought exercise                      |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                                      | **6.5h** | **4.5h**  | **3h**     | **3.5h**  | **5h**   | **8h**       | **2.5h**   | **33h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | grpc.io docs, Microsoft Learn gRPC chapter, the protobuf 3 language guide, the `dotnet/aspnetcore` gRPC source, the `grpc/grpc-dotnet` repository, the `protocolbuffers/protobuf` repository |
| [lecture-notes/01-protobuf3-and-the-wire-format.md](./lecture-notes/01-protobuf3-and-the-wire-format.md) | The proto3 language: messages, enums, oneof, repeated, well-known types; the wire format; `Grpc.Tools` integration; the generated C# code |
| [lecture-notes/02-grpc-call-types-and-the-dotnet-stack.md](./lecture-notes/02-grpc-call-types-and-the-dotnet-stack.md) | gRPC over HTTP/2; the four call types in C# (`Grpc.AspNetCore` server, `Grpc.Net.Client` client); code-first vs proto-first sidebar; cross-language client story |
| [lecture-notes/03-deadlines-errors-interceptors-tls.md](./lecture-notes/03-deadlines-errors-interceptors-tls.md) | Deadlines and cancellation propagation; `RpcException` and the 17 status codes; client and server interceptors; metadata; TLS configuration; the `h2c` opt-in |
| [exercises/exercise-01-design-a-proto.proto](./exercises/exercise-01-design-a-proto.proto) | Write a `.proto` from a real-world spec: messages, enums, `oneof`, `repeated`, `Timestamp`, `Duration`, schema evolution |
| [exercises/exercise-01-design-a-proto.cs](./exercises/exercise-01-design-a-proto.cs) | The matching `.csproj` snippet, the generated-code consumption demo, and the round-trip serialization test |
| [exercises/exercise-02-four-call-types.proto](./exercises/exercise-02-four-call-types.proto) | A service with all four call shapes — unary, server-streaming, client-streaming, bidirectional |
| [exercises/exercise-02-four-call-types.cs](./exercises/exercise-02-four-call-types.cs) | The C# server implementation and the C# client driver |
| [exercises/exercise-03-deadlines-and-interceptors.proto](./exercises/exercise-03-deadlines-and-interceptors.proto) | A small service plus a logging interceptor and a deadline-propagating client |
| [exercises/exercise-03-deadlines-and-interceptors.cs](./exercises/exercise-03-deadlines-and-interceptors.cs) | The interceptor, the deadline plumbing, the assertion on `StatusCode.DeadlineExceeded` |
| [exercises/SOLUTIONS.md](./exercises/SOLUTIONS.md) | Annotated solutions for the three exercises, with the output you should reproduce |
| [challenges/challenge-01-cross-language-client.md](./challenges/challenge-01-cross-language-client.md) | Take the Exercise 2 server and call it from a Python `grpcio` client. Same proto, different language. Verify wire compatibility |
| [challenges/challenge-02-schema-evolution.md](./challenges/challenge-02-schema-evolution.md) | Evolve a v1 proto into a v2 proto, keeping backward and forward compatibility. Run the v1 client against the v2 server and vice versa |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on proto3, wire format, the four call types, deadlines, error codes, interceptors |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for "Crunch Counter" — a distributed counter service with unary and server-streaming endpoints, a C# client, and a Python client |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

For Week 9 we add a network-shaped contract: **every RPC you ship accepts a deadline, propagates the deadline into any downstream calls, returns a precise `StatusCode` on failure, and is observable through a client-side interceptor that logs latency**. A gRPC method that ignores `context.CancellationToken` is the network equivalent of a sync-over-async deadlock from Week 8 — it keeps doing work after the caller has given up. A gRPC method that returns `StatusCode.Internal` for every error is the network equivalent of `catch (Exception)` — it eats the information that would have let the operator decide whether retrying is safe.

We add a schema contract too: **every `.proto` you ship reserves removed field numbers, never reuses a number, and never changes a field's type**. A wire-incompatible schema change is a production outage the next time your v1 client meets your v2 server. The rules are small in number; obeying them is non-negotiable.

> **Note on `dotnet build`.** Some learners will run this week's code in an environment without the .NET 8 SDK installed (a VM, a Codespace without the right image, a fresh laptop). Installing the SDK is your responsibility — `https://dotnet.microsoft.com/download` is free and the installer takes two minutes. The lecture examples and the exercise scaffolds assume `dotnet --version` reports `8.0.x` and that `dotnet new` and `dotnet add package` work. If you are missing the SDK, install it before Monday's exercise.
