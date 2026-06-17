# Week 11 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `dotnet/aspnetcore` source on GitHub is public. The `MessagePack-CSharp` repository is MIT-licensed. The `@microsoft/signalr` npm package is MIT-licensed. The Redis pub/sub documentation is free. No paywalled material is linked.

## Required reading (work it into your week)

### ASP.NET Core SignalR — overview and concepts

- **Introduction to ASP.NET Core SignalR** — the architectural overview, the supported clients, the protocol bullet list:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction>
- **What is SignalR? (deeper)** — the canonical description of the protocol layering and what each layer owns:
  <https://learn.microsoft.com/en-us/aspnet/signalr/overview/getting-started/introduction-to-signalr>
- **Use hubs in SignalR** — defining hub classes, sending messages, the `Clients` proxy, the per-call lifetime:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs>
- **Strongly typed hubs** — the `Hub<TClient>` shape and the compile-time check on client method names:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#strongly-typed-hubs>
- **Configuration in SignalR** — server and client options, the keep-alive interval, the timeouts, the allowed transports:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration>
- **Hub filters** — server-side cross-cutting concerns (logging, validation) applied to hub methods:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters>

### Transports

- **Transports overview** — WebSockets, Server-Sent Events, long polling, the negotiate handshake:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-allowed-transports>
- **HttpTransportType reference** — the flag enum, the bitwise selection:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.connections.httptransporttype>
- **Skipping the negotiate step** — when you can force WebSockets directly and skip the negotiation round trip:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-additional-options>

### Groups, users, and connections

- **Manage users and groups in SignalR** — `Groups.AddToGroupAsync`, `Groups.RemoveFromGroupAsync`, the lifecycle:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups>
- **`IUserIdProvider`** — mapping JWT claims (or anything else on `HubCallerContext.User`) to a stable user identifier:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups#users-in-signalr>
- **`HubCallerContext` reference** — the per-call context, `ConnectionId`, `UserIdentifier`, `Items`, `ConnectionAborted`:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.signalr.hubcallercontext>

### Authentication on the WebSocket upgrade

- **Authentication and authorization in SignalR** — the canonical reference for `[Authorize]` on hubs, the `access_token` query-string convention, the `OnMessageReceived` JWT hook:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>
- **JWT bearer authentication overview** — the middleware, the token-validation parameters, the events:
  <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
- **The `access_token` query-string convention** — the SignalR-specific carve-out, the security implications:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication>
- **Identity in SignalR** — the `[Authorize(Roles = "...")]` and `[Authorize(Policy = "...")]` shapes on hubs:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#authorize-users-to-access-hubs-and-hub-methods>

### Scale-out

- **SignalR with a Redis backplane** — the canonical multi-instance story, the package, the configuration:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>
- **StackExchange.Redis documentation** — the client library used by the backplane; pub/sub primer:
  <https://stackexchange.github.io/StackExchange.Redis/>
- **Redis pub/sub reference** — what gets published, what subscribes, the failure modes:
  <https://redis.io/docs/latest/develop/interact/pubsub/>
- **Azure SignalR Service** — the managed alternative; the design trade-offs vs self-hosted with Redis:
  <https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-overview>
- **Choose a scale-out strategy** — the official decision matrix between self-hosted, Redis-backplane, and Azure SignalR:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/scale>

### MessagePack protocol

- **MessagePack Hub Protocol in SignalR** — the wire-format swap, the `AddMessagePackProtocol()` call, the client-side counterpart:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>
- **`MessagePack-CSharp` repository** — the fast binary serializer underneath the SignalR MessagePack protocol; the de facto fast binary serializer for .NET independent of SignalR:
  <https://github.com/MessagePack-CSharp/MessagePack-CSharp>
- **MessagePack specification** — the wire format itself; how integers, strings, maps, and arrays are encoded:
  <https://github.com/msgpack/msgpack/blob/master/spec.md>
- **`@microsoft/signalr-protocol-msgpack` npm package** — the browser-side counterpart:
  <https://www.npmjs.com/package/@microsoft/signalr-protocol-msgpack>

### Reconnection

- **Configure client options** — the reconnect schedule, the keep-alive, the server timeout:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-client-options>
- **`withAutomaticReconnect` reference** — the default backoff (1s, 2s, 5s, 10s, 30s), the custom-schedule shape, the give-up rule:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-reconnect>
- **Reconnection events** — `onreconnecting`, `onreconnected`, `onclose`:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client#reconnect-clients>

### Streaming

- **Streaming in SignalR** — server-to-client (`IAsyncEnumerable<T>`, `ChannelReader<T>`) and client-to-server, the wire envelopes, cancellation:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming>
- **`IAsyncEnumerable<T>` reference** — the framework type, the cancellation rules:
  <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1>
- **`System.Threading.Channels`** — the producer / consumer primitive that streaming hub methods build on:
  <https://learn.microsoft.com/en-us/dotnet/core/extensions/channels>

### Hubs vs raw WebSockets

- **WebSockets support in ASP.NET Core** — the raw `WebSocket` API, when to pick it over SignalR:
  <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets>
- **The WebSocket protocol (RFC 6455)** — the underlying wire format that SignalR's WebSocket transport sits on top of:
  <https://datatracker.ietf.org/doc/html/rfc6455>
- **Server-Sent Events specification (HTML Living Standard)** — the EventSource protocol that SignalR's SSE transport uses:
  <https://html.spec.whatwg.org/multipage/server-sent-events.html>

### The `dotnet/aspnetcore` GitHub source — source link these as you read

- **Repository root**:
  <https://github.com/dotnet/aspnetcore>
- **SignalR top-level folder** — server, common, client, samples:
  <https://github.com/dotnet/aspnetcore/tree/main/src/SignalR>
- **`Hub.cs`** — the base class for hubs, the `Clients` proxy, the lifecycle hooks:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/Core/src/Hub.cs>
- **`HubConnectionHandler.cs`** — the per-connection state machine on the server:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/Core/src/Internal/HubConnectionHandler.cs>
- **`HttpConnectionDispatcher.cs`** — the negotiate handler, the transport selection:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/common/Http.Connections/src/Internal/HttpConnectionDispatcher.cs>
- **`HubConnection.cs`** (client) — the .NET client's connection state machine, the reconnect schedule, the protocol handshake:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/clients/csharp/Client.Core/src/HubConnection.cs>
- **`JsonHubProtocol.cs`** — the default protocol implementation; read this once to see what one message looks like on the wire:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/common/Protocols.Json/src/Protocol/JsonHubProtocol.cs>
- **`MessagePackHubProtocolWorker.cs`** — the MessagePack protocol implementation; pair this read with `JsonHubProtocol.cs` to see the difference:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/common/Protocols.MessagePack/src/Protocol/MessagePackHubProtocolWorker.cs>
- **`RedisHubLifetimeManager.cs`** — the Redis backplane implementation; the channel names, the published payload shape:
  <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/StackExchangeRedis/src/RedisHubLifetimeManager.cs>
- **JavaScript client source** — the `@microsoft/signalr` package source in the same monorepo:
  <https://github.com/dotnet/aspnetcore/tree/main/src/SignalR/clients/ts/signalr>

### JavaScript / TypeScript client

- **JavaScript client overview** — installing `@microsoft/signalr`, building a connection, registering handlers:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client>
- **`HubConnectionBuilder` API reference** — the builder pattern, `.withUrl(...)`, `.withAutomaticReconnect(...)`, `.withHubProtocol(...)`, `.configureLogging(...)`:
  <https://learn.microsoft.com/en-us/javascript/api/@microsoft/signalr/hubconnectionbuilder>
- **`HubConnection` API reference** — `start`, `stop`, `on`, `off`, `invoke`, `send`, `stream`, the event surface:
  <https://learn.microsoft.com/en-us/javascript/api/@microsoft/signalr/hubconnection>
- **`@microsoft/signalr` npm package**:
  <https://www.npmjs.com/package/@microsoft/signalr>
- **TypeScript usage notes** — strict-mode typing for handlers and invocations:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/typescript-client>

### .NET client

- **.NET client overview** — installing `Microsoft.AspNetCore.SignalR.Client`, building a connection from C#:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client>
- **`HubConnection` (client) reference** — the C# class, the events, the invocation methods:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.signalr.client.hubconnection>

### Observability

- **Diagnostics in SignalR** — server logs, client logs, `EventSource` names, what to log at which level:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/diagnostics>
- **`dotnet-counters` for SignalR** — `Microsoft.AspNetCore.Http.Connections` and `Microsoft.AspNetCore.SignalR` counters, what each one measures:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/available-counters>
- **OpenTelemetry for ASP.NET Core** — distributed tracing across SignalR invocations:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel>

## Recommended reading (after the required set)

- **SignalR design considerations** — when to use SignalR vs when to use a message queue vs when to use a raw WebSocket:
  <https://learn.microsoft.com/en-us/aspnet/core/signalr/scale#design-considerations>
- **`ASP.NET Core SignalR` blog tag** — the team's announcement posts; useful for "what changed in 8":
  <https://devblogs.microsoft.com/dotnet/tag/signalr/>
- **Damian Edwards on SignalR architecture (recorded talks)** — long-form context from the original author:
  <https://learn.microsoft.com/en-us/shows/on-net/>
- **Stephen Toub on async streaming** — `IAsyncEnumerable` plumbing, the cost model, what SignalR streaming inherits:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/>
- **OWASP WebSocket security cheat sheet** — origin checks, token rotation, the upgrade-header attack surface:
  <https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html#websockets>

## Tools you will install this week

- **`dotnet-counters`** (once per machine; same tool from Week 10): `dotnet tool install --global dotnet-counters`. Verify with `dotnet-counters --version`.
- **`wscat`** — a command-line WebSocket client. One-shot install via `npm install -g wscat`. Verify with `wscat --version`. Used for inspecting the SignalR wire from the terminal.
- **`Redis` via Docker** (required for the mini-project and challenge 1): `docker run --name redis-week11 -p 6379:6379 -d redis:7`. Verify with `docker exec redis-week11 redis-cli PING` (returns `PONG`).
- **`nginx` via Docker** (used in the mini-project to load-balance two SignalR instances): the `mini-project/docker-compose.yml` ships the configuration.
- **Node.js 20.x and npm** (for the TypeScript client and the Vite dev server): install from <https://nodejs.org/> if you do not already have it. Verify with `node --version`.
- **`@microsoft/signalr` and (optional) `@microsoft/signalr-protocol-msgpack` npm packages** — added per-project, not globally: `npm install @microsoft/signalr @microsoft/signalr-protocol-msgpack`.

## Citations policy

This curriculum cites Microsoft Learn URLs, the `dotnet/aspnetcore` GitHub source, the `MessagePack-CSharp` GitHub repository, the `@microsoft/signalr` npm package, and the Redis documentation as the primary references. Every example in the lecture notes and exercises is traced back to one of these. When a third-party blog (Damian Edwards, Andrew Lock, Khalid Abuhakmeh) is the clearer reference, it is cited explicitly with a URL — never paraphrased without attribution. The OWASP HTML5 cheat sheet is cited where WebSocket security is the topic; it is the canonical reference. If a citation is missing from a section of these notes, treat it as a bug and open an issue against the C9 curriculum repository.
