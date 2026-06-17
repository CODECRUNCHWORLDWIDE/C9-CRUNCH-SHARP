# Week 9 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The grpc.io documentation, the protobuf language guide, and the `grpc/grpc-dotnet`, `protocolbuffers/protobuf`, and `dotnet/aspnetcore` source repositories are public on GitHub. No paywalled material is linked.

## Required reading (work it into your week)

### Protocol Buffers (proto3)

- **protobuf 3 language guide** — the canonical reference. Required reading; plan for 90 minutes total over the week:
  <https://protobuf.dev/programming-guides/proto3/>
- **protobuf encoding (wire format) reference** — varints, tags, wire types, packed encoding:
  <https://protobuf.dev/programming-guides/encoding/>
- **protobuf well-known types** — `Timestamp`, `Duration`, `Empty`, `Any`, `FieldMask`, the wrapper types:
  <https://protobuf.dev/reference/protobuf/google.protobuf/>
- **protobuf style guide** — naming conventions for messages, fields, and enums:
  <https://protobuf.dev/programming-guides/style/>
- **protobuf field-presence semantics in proto3** — the `optional` keyword and the `has_X` accessor:
  <https://protobuf.dev/programming-guides/field_presence/>

### gRPC (grpc.io)

- **gRPC concepts overview** — the four call types, channels, deadlines, the gRPC-over-HTTP/2 mapping:
  <https://grpc.io/docs/what-is-grpc/core-concepts/>
- **gRPC over HTTP/2 specification** — how `:path`, `:method`, `te`, `grpc-timeout`, `grpc-status` are framed:
  <https://grpc.io/docs/guides/wire/>
- **gRPC authentication overview** — TLS, ALTS, token-based auth:
  <https://grpc.io/docs/guides/auth/>
- **gRPC deadlines guide** — propagation rules, what happens when deadlines expire:
  <https://grpc.io/docs/guides/deadlines/>
- **gRPC error handling guide** — the `Status` model, the 17 status codes, when to use which:
  <https://grpc.io/docs/guides/error/>
- **gRPC status code reference table** — every code, the canonical meaning, when each is appropriate:
  <https://grpc.github.io/grpc/core/md_doc_statuscodes.html>

### .NET-specific gRPC documentation (Microsoft Learn)

- **gRPC on .NET — overview**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/>
- **gRPC services with ASP.NET Core**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>
- **Create a gRPC client in .NET**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>
- **gRPC client factory integration with HttpClientFactory**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory>
- **Authentication and authorization in gRPC for ASP.NET Core**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>
- **gRPC services with C#** — the generated-code shape, service contracts:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>
- **Calling gRPC services with the gRPC client factory** — interceptors, DI:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors>
- **gRPC deadlines and cancellation**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation>
- **gRPC reliability — retries, hedging, transient-fault handling**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/retries>
- **gRPC for WCF developers — book length, free** (the migration guide; relevant background on the design rationale):
  <https://learn.microsoft.com/en-us/dotnet/architecture/grpc-for-wcf-developers/>
- **`Grpc.Tools` MSBuild integration**:
  <https://github.com/grpc/grpc/blob/master/src/csharp/BUILD-INTEGRATION.md>
- **`Microsoft.Extensions.Diagnostics.Metrics` with gRPC**:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/diagnostics>

### Python gRPC (cross-language)

- **gRPC Python quickstart**:
  <https://grpc.io/docs/languages/python/quickstart/>
- **gRPC Python basics — the four call types from Python**:
  <https://grpc.io/docs/languages/python/basics/>
- **`grpcio` package documentation**:
  <https://grpc.github.io/grpc/python/>
- **`grpcio-tools` — the `python -m grpc_tools.protoc` code generator**:
  <https://pypi.org/project/grpcio-tools/>

## Authoritative deep dives

- **James Newton-King (gRPC for .NET PM) — "gRPC performance improvements in .NET 8"**:
  <https://devblogs.microsoft.com/dotnet/grpc-performance-improvements-in-net-8/>
- **James Newton-King — "gRPC performance improvements in .NET 7"**:
  <https://devblogs.microsoft.com/dotnet/grpc-performance-improvements-in-net-7/>
- **James Newton-King — "gRPC performance improvements in .NET 5"** (the foundational post; the architecture has not changed):
  <https://devblogs.microsoft.com/aspnet/grpc-performance-improvements-in-net-5/>
- **gRPC Blog — "gRPC on HTTP/2 Engineering a Robust, High Performance Protocol"**:
  <https://grpc.io/blog/grpc-on-http2/>
- **CNCF white paper — "gRPC Motivation and Design Principles"** (Louis Ryan, the original lead):
  <https://grpc.io/blog/principles/>
- **Brad Fitzpatrick — "Why we built protobuf"** (talk; the historical motivation for tagged-and-varint encodings):
  search YouTube for "Brad Fitzpatrick protobuf".
- **Marc Gravell — `protobuf-net.Grpc` design rationale** (the code-first sidebar; Marc is the author):
  <https://protobuf-net.github.io/protobuf-net.Grpc/>

## Source you should read

The runtime and gRPC libraries are MIT and Apache 2.0 licensed; source-link works. When a lecture says "the interceptor base class is 60 lines, go read it," it means literally that — open the link, scroll through, return.

- **`grpc/grpc-dotnet` — the canonical .NET gRPC repository**:
  <https://github.com/grpc/grpc-dotnet>
- **`Grpc.AspNetCore.Server` — service infrastructure**:
  <https://github.com/grpc/grpc-dotnet/tree/master/src/Grpc.AspNetCore.Server>
- **`Grpc.Net.Client` — the client channel**:
  <https://github.com/grpc/grpc-dotnet/tree/master/src/Grpc.Net.Client>
- **`Grpc.Core.Interceptors.Interceptor` — the interceptor base class**:
  <https://github.com/grpc/grpc-dotnet/blob/master/src/Grpc.AspNetCore.Server/Internal/CallHandlers/ServerCallHandlerBase.cs>
- **`protocolbuffers/protobuf` — the protobuf reference implementation**:
  <https://github.com/protocolbuffers/protobuf>
- **`google.protobuf.Timestamp` definition** — the canonical well-known type for absolute time:
  <https://github.com/protocolbuffers/protobuf/blob/main/src/google/protobuf/timestamp.proto>
- **`google.protobuf.Duration` definition**:
  <https://github.com/protocolbuffers/protobuf/blob/main/src/google/protobuf/duration.proto>
- **`google.protobuf.Any` definition** — the type-erased message envelope:
  <https://github.com/protocolbuffers/protobuf/blob/main/src/google/protobuf/any.proto>

## Selected `grpc-dotnet` and `protobuf` issues to skim

These are closed issues and PRs that contain extended design discussion. Each is a self-contained case study in how the gRPC team reasons about wire compatibility, performance, and API surface.

- **The proto3 `optional` keyword reintroduction** (the original removal was a design mistake; this is the fix):
  <https://github.com/protocolbuffers/protobuf/issues/1606>
- **`Grpc.Net.Client` HTTP/2 over cleartext (`h2c`) discussion**:
  <https://github.com/grpc/grpc-dotnet/issues/431>
- **gRPC retry policy implementation**:
  <https://github.com/grpc/grpc-dotnet/issues/142>
- **`ServerCallContext.UserState` design**:
  <https://github.com/grpc/grpc-dotnet/issues/676>
- **gRPC interceptor execution order**:
  <https://github.com/grpc/grpc-dotnet/issues/1469>
- **`Grpc.AspNetCore.HealthChecks` design**:
  <https://github.com/grpc/grpc-dotnet/tree/master/src/Grpc.AspNetCore.HealthChecks>

## Tools (all free, first-party or first-party-adjacent)

- **`grpcurl`** — `curl` for gRPC services; the smoke-test tool of choice:
  <https://github.com/fullstorydev/grpcurl>
- **`grpcui`** — a browser-based interactive gRPC client:
  <https://github.com/fullstorydev/grpcui>
- **`buf`** — a modern protobuf linter, breaking-change detector, and build orchestrator (free for individual use):
  <https://buf.build/docs/introduction>
- **`dotnet-counters` with gRPC counters** — `dotnet-counters monitor Grpc.AspNetCore.Server Grpc.Net.Client` shows live RPC counts and latencies:
  <https://learn.microsoft.com/en-us/aspnet/core/grpc/diagnostics>
- **Wireshark with HTTP/2 dissector** — see the actual wire bytes if you want to verify the encoding by eye:
  <https://www.wireshark.org/>

## Talks worth watching (all free, no account)

- **James Newton-King — "gRPC in .NET" (.NET Conf)** — the canonical introduction, 60 minutes:
  search YouTube for "James Newton-King gRPC .NET Conf".
- **Louis Ryan — "gRPC: The Story of Microservices at Square"** (the original 2015 launch talk):
  search YouTube for "Louis Ryan gRPC Square".
- **Eric Anderson — "gRPC Deep Dive"**:
  search YouTube for "Eric Anderson gRPC deep dive".
- **Marc Gravell — "Code-first gRPC with protobuf-net"** (the sidebar from lecture 2):
  search YouTube for "Marc Gravell protobuf-net gRPC".

## How to use this resource list

The lectures cite specific URLs from this page at decision points. When a lecture says "see grpc.io's deadlines guide," you can find the URL above. The links you should read end-to-end this week are:

1. **protobuf 3 language guide** — the language reference. Plan for 90 minutes, spread across the week.
2. **gRPC concepts overview (grpc.io)** — the model. Plan for 30 minutes.
3. **Microsoft Learn — gRPC services with ASP.NET Core** — the .NET implementation. Plan for 45 minutes.
4. **grpc.io — deadlines guide and error guide** — the two design areas with the most subtle wording. Plan for 30 minutes each.
5. **gRPC Python basics (grpc.io)** — required before Thursday's cross-language challenge. Plan for 30 minutes.

The rest are reference material. Bookmark and return to them when a specific question arises.

---

*Bookmarks decay. If a link rots, search the title — these are all canonical pieces and they reappear on the same authors' new homes.*
