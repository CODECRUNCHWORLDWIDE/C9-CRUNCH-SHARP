# Lecture 1 — Hubs, Transports, and the Negotiate Handshake

> **Time:** 2 hours. Take the hub-and-protocol material in one sitting and the transport-negotiation material in a second sitting. **Prerequisites:** Week 2 (minimal-APIs, DI registration) and Week 4 (`async`/`await`, `IAsyncEnumerable`). **Citations:** the SignalR introduction at <https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction>, the hubs chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs>, and the transport-configuration chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-allowed-transports>.

## 1. Why SignalR, and why this lecture first

Every backend that serves a browser in 2026 sooner or later needs to send the browser an update that the browser did not ask for. The classic shape is the user opening a dashboard and expecting it to update without F5; the variant is the user typing into a chat window and expecting their counterpart to see the typed letters arrive without lag; the extreme variant is a collaborative editor where two cursors on two laptops have to stay in sync within 50 milliseconds. The naive answer is a polling loop — `setInterval(() => fetch("/state"), 1000)` — and it works, kind of, until the cost of "1000 idle clients all asking for an update every second" overwhelms a server that should be doing useful work for the ten clients that actually have a new update to read. The correct answer is **the server keeps a connection open per client and pushes updates as they happen**. The WebSocket protocol, standardised as RFC 6455, is the wire format that makes this efficient — a single TCP socket carries bidirectional, low-overhead frames after a one-time HTTP upgrade.

The problem is that not every network path between a browser and your server is WebSocket-friendly. Corporate proxies sometimes strip the `Upgrade: websocket` header. Some older HTTP/1.1 reverse proxies do not buffer the upgrade dialogue correctly. Some browsers in restricted environments do not expose the `WebSocket` API. The historical answer was either "give up on those users" or "write three different transports yourself." **ASP.NET Core SignalR is the third answer**: a single protocol on top of three transports (WebSockets, Server-Sent Events, long polling), a negotiation handshake that picks the best one the network allows, an RPC envelope that lets you call C# from JavaScript and JavaScript from C# as if the network were not there, and a reconnection state machine for when the network drops anyway.

This lecture is first because **everything else in the week depends on you being fluent with the negotiate handshake and the hub-method dispatch model**. The groups material in Lecture 2 is invisible without an understanding of `ConnectionId`. The Redis backplane material in Lecture 3 is opaque without an understanding of what a hub broadcast actually emits. The streaming material in Lecture 3 sits directly on top of the per-call hub lifetime that this lecture introduces. We start at the wire and build up.

Read this lecture alongside three Microsoft Learn pages, open in browser tabs: the SignalR introduction, the hubs chapter, and the configuration chapter. The lecture's job is to give you a single 90-minute path through the material that those three pages, read independently, would have taken three hours to cover.

## 2. A minimum-viable hub

A `Hub` is a class deriving from `Microsoft.AspNetCore.SignalR.Hub`. Each method on the class is callable from a connected client; the framework dispatches by method name (case-insensitive) and matches positional argument count. The minimum viable example is approximately fifteen lines:

```csharp
#nullable enable
using Microsoft.AspNetCore.SignalR;

namespace Crunch.Chat;

public sealed class ChatHub : Hub
{
    public Task SendMessage(string user, string text)
    {
        return Clients.All.SendAsync("ReceiveMessage", user, text);
    }
}
```

Five constructs are worth naming:

1. The class derives from `Hub`. That base class brings the `Clients` proxy, the `Groups` proxy, the `Context` (the per-call `HubCallerContext`), and the standard lifecycle hooks `OnConnectedAsync` / `OnDisconnectedAsync`.
2. `SendMessage(string user, string text)` is a server-side method that any connected client can invoke by name. The framework will deserialize the two positional arguments from the wire and call the method.
3. `Clients.All.SendAsync("ReceiveMessage", user, text)` sends a fire-and-forget message to every connected client. The first argument is the **method name on the client** that should be dispatched; the rest are the positional arguments to that handler. Note the inversion: from the server's perspective, "method on the client" is just an event name; from the client's perspective, the event handler is registered with `connection.on("ReceiveMessage", ...)`.
4. The method returns `Task` because `SendAsync` is asynchronous. Always `await` (or `return`) it; never fire and forget the underlying `Task`.
5. The method is not marked `[Authorize]` — meaning anyone who can reach the negotiate URL can invoke it. In Week 11 we will fix that on the second lecture. For now we keep the surface small.

The hub is registered in DI and mapped to a URL like any other endpoint:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.MapHub<ChatHub>("/hubs/chat");

app.Run();
```

Three things are worth naming:

1. **`AddSignalR()` registers all of SignalR's services.** That includes the protocol providers (JSON by default), the hub-lifetime manager (in-memory by default; Redis later), the connection-context accessors, and the dispatcher.
2. **`MapHub<TChat>("/hubs/chat")` maps the hub to a URL path.** The URL is yours; the convention is `/hubs/<name>` so the path predicate that we will need for JWT authentication on Lecture 2 has a stable shape.
3. **The hub class is *not* registered as a service by `AddSignalR`.** A fresh instance is constructed per invocation through `ActivatorUtilities`, so constructor injection of scoped or singleton services works the same way it does in controllers. If you want to share state between calls on the same connection, **do not put it on the hub instance** — the instance does not live that long. Put it in a singleton service.

Citation for the registration shape: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#configure-signalr>. The source-link for `AddSignalR` is in `src/SignalR/server/Core/src/SignalRDependencyInjectionExtensions.cs` in `dotnet/aspnetcore`.

## 3. The per-call hub lifetime — the single most common misunderstanding

A `Hub` instance is created when an invocation arrives, the method runs, and the instance is disposed. The next invocation — even from the same client over the same WebSocket — gets a brand-new instance. This matters in three ways:

```csharp
public sealed class WrongHub : Hub
{
    // BUG: this field is reset to 0 on every call.
    private int _messageCount = 0;

    public Task Track()
    {
        _messageCount++;
        return Clients.Caller.SendAsync("CountIs", _messageCount);
    }
}
```

The field `_messageCount` is reset to zero on every call because the instance carrying it is brand new. The fix is to keep counters in a singleton service:

```csharp
public sealed class MessageCounter
{
    private int _count;
    public int Increment() => Interlocked.Increment(ref _count);
}

public sealed class RightHub : Hub
{
    private readonly MessageCounter _counter;
    public RightHub(MessageCounter counter) => _counter = counter;

    public Task Track()
    {
        int c = _counter.Increment();
        return Clients.Caller.SendAsync("CountIs", c);
    }
}

// In Program.cs:
builder.Services.AddSingleton<MessageCounter>();
```

The same per-call lifetime is why **you must not capture the hub instance in a continuation or a background task**. The line `Task.Run(() => Clients.All.SendAsync(...))` from inside a hub method is a bug: by the time the background task runs, the hub instance has been disposed and `Clients` references a disposed scope. The correct way to broadcast from outside a hub method is to inject `IHubContext<TChat>` into the service that owns the broadcast intent, and call `_hubContext.Clients.All.SendAsync(...)` from there. `IHubContext<TChat>` is registered automatically by `AddSignalR()` and is safe to capture in singletons.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#the-hubcontext>. Source-link for the `Hub` base class: <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/Core/src/Hub.cs>.

## 4. The negotiate handshake — what is actually on the wire

When a SignalR client starts a connection, it issues a POST to `<hub-url>/negotiate?negotiateVersion=1`. The server's response is a small JSON document that looks approximately like this:

```json
{
  "connectionId": "n9yQEdoJaB6xK8d-3Gqw3w",
  "connectionToken": "f1f4f1b7-7c12-4f7e-9c2e-7a2b5d9ab9d6",
  "negotiateVersion": 1,
  "availableTransports": [
    {
      "transport": "WebSockets",
      "transferFormats": [ "Text", "Binary" ]
    },
    {
      "transport": "ServerSentEvents",
      "transferFormats": [ "Text" ]
    },
    {
      "transport": "LongPolling",
      "transferFormats": [ "Text", "Binary" ]
    }
  ]
}
```

Five fields are worth naming:

1. **`connectionId`** is the application-visible identifier you see as `Context.ConnectionId` inside a hub method. It is short and URL-safe.
2. **`connectionToken`** is the bearer token for the actual transport request. The client appends `?id=<connectionToken>` to the transport URL; the server matches it against an internal table. The token is opaque to your code.
3. **`negotiateVersion`** is the protocol version of the handshake itself. Version 1 is the current shape; version 0 (legacy) used `connectionId` for both purposes. Recent SignalR clients send `negotiateVersion=1` and the server replies in kind.
4. **`availableTransports`** is the list, in server-preference order, of transports the server is willing to use. The default is all three (WebSockets, SSE, long polling). You can restrict it server-side or filter it client-side.
5. **`transferFormats`** within each transport entry indicates whether the transport can carry text only, binary only, or both. WebSockets and long polling can carry binary; SSE is text-only (which is why the MessagePack protocol cannot run over SSE — MessagePack is a binary format).

The client picks the first transport in `availableTransports` that it also supports, then opens that transport. For WebSockets, that means an HTTP request with `Upgrade: websocket` to `<hub-url>?id=<connectionToken>`. For SSE, an HTTP GET to the same URL with `Accept: text/event-stream`. For long polling, an HTTP GET that hangs until a message is available or the timeout expires.

You can see all of this in your browser's Network tab. Filter to `/hubs/chat`, click the negotiate request, look at the response body. Then click the connection request that follows it — for a WebSocket, the status is `101 Switching Protocols`; for SSE, it stays open with `200 OK` and `Content-Type: text/event-stream`; for long polling, it cycles through 200 responses. **Read the negotiate response in your browser once per week until it is in your fingers.** The framework will not surprise you if you have read it.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-allowed-transports>. Source-link for the negotiate handler: `HttpConnectionDispatcher.cs` in `dotnet/aspnetcore`.

## 5. Skipping the negotiate step

For low-latency applications where the negotiation round trip itself is a cost, recent SignalR clients support **skipping** the negotiate step and going straight to WebSockets:

```ts
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", {
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
    })
    .build();
```

The trade is: you save one HTTP round trip on connection startup, but you lose the ability to fall back to SSE or long polling if WebSockets are blocked. Set `skipNegotiation: true` **only** when you know the network path supports WebSockets (typically: same-origin connections in a controlled environment, or when you have already proven WebSocket support out-of-band). Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-additional-options>.

## 6. Transports — WebSockets, Server-Sent Events, long polling

### 6.1 WebSockets

The default. After the HTTP upgrade, the connection is a single TCP socket carrying full-duplex framed messages. Each SignalR message is one WebSocket frame; the protocol envelope (an extra 30-100 bytes) sits inside the frame's payload. Latency is the round-trip time of the TCP socket — typically 1-50ms for same-region traffic. Throughput is the bandwidth-delay product of the socket — typically 10-100k messages per second per connection on modest hardware.

The full reference is RFC 6455 at <https://datatracker.ietf.org/doc/html/rfc6455>. You do not have to read it cover to cover, but you should know that the WebSocket framing has a 2-14 byte header per frame (depending on payload size and masking), that "ping" and "pong" control frames keep NAT mappings alive, and that the protocol supports both text (UTF-8) and binary payloads.

### 6.2 Server-Sent Events (SSE)

SSE is a one-way, server-to-client streaming protocol built on top of plain HTTP. The client opens a `GET` to the SSE URL with `Accept: text/event-stream`; the server replies with `Content-Type: text/event-stream` and keeps the response open, writing newline-delimited events as they happen. The client-to-server direction in SignalR-over-SSE uses a separate HTTP POST per message.

The relevant property is that SSE is **text-only and one-way per request**. The MessagePack protocol cannot run over SSE because MessagePack is binary. The reverse direction (client-to-server) is HTTP POSTs, which on HTTP/1.1 means a fresh TCP connection per send (or shared via keep-alive); on HTTP/2 they multiplex over the same TCP connection as the SSE response, which is the only configuration where SSE is competitive with WebSockets for full-duplex use.

SSE survives most corporate proxies because it looks like a long-running HTTP GET. It is the right fallback when WebSockets are blocked. Citation: <https://html.spec.whatwg.org/multipage/server-sent-events.html>.

### 6.3 Long polling

The universal fallback. The client issues an HTTP POST to send a message; the client issues an HTTP GET that hangs on the server until a message is available (or a timeout expires, typically 90 seconds); when the GET returns, the client immediately issues another one. The protocol works through every HTTP-aware proxy, intercepting firewall, and ancient browser. The cost is latency (every server-to-client message waits for the next GET to be in flight) and overhead (every message is a full HTTP request with headers).

Use long polling when neither WebSockets nor SSE is available. SignalR will use it automatically as the last resort. Do not configure it manually unless you are testing.

### 6.4 Forcing a transport (for debugging)

You can restrict the transports on the server with the `AddSignalR` callback:

```csharp
builder.Services.AddSignalR(options =>
{
    // Default: all three transports.
    // For testing fallback behaviour: restrict to long polling only.
    // (Do not ship this; it is for debugging the fallback path.)
});

app.MapHub<ChatHub>("/hubs/chat", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
});
```

Or client-side, on the `withUrl` options object:

```ts
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", {
        transport: signalR.HttpTransportType.WebSockets
                 | signalR.HttpTransportType.LongPolling
        // SSE deliberately excluded
    })
    .build();
```

Citation: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.connections.httptransporttype>.

## 7. Hub vs raw WebSocket — when to pick which

ASP.NET Core supports both. The question of when to pick which is one of the most-asked SignalR questions, and the answer is more nuanced than "always SignalR" or "always raw."

### 7.1 The case for SignalR

Pick SignalR when:

- You want server-to-client method calls (and the reverse) without writing protocol code.
- You want transport fallback for users behind restrictive networks.
- You want reconnection and a reconnect state machine without writing it yourself.
- You want groups (rooms) for fan-out broadcasts.
- You want scale-out across multiple server instances without inventing your own backplane.
- You want SDKs in JavaScript, TypeScript, C#, Java, Swift — written and maintained by the .NET team.

In practice: chat, collaborative editing, live dashboards, notifications, multiplayer turn-based games. The envelope tax (30-100 bytes per message) is invisible at the data rates these applications run at.

### 7.2 The case for raw WebSockets

Pick a raw WebSocket when:

- You control both ends and you want a single, opinionated binary protocol.
- You need sub-100us per-message processing time and the envelope tax matters.
- You are streaming a high-rate binary feed (game state at 60 fps, financial ticks, video frames).
- You do not need transport fallback.
- You do not need reconnection beyond "the client tries again."

In practice: real-time multiplayer game state, low-latency trading feeds, custom binary protocols you wrote yourself. The .NET API is `app.UseWebSockets()` plus an endpoint that calls `HttpContext.WebSockets.AcceptWebSocketAsync()` and reads frames in a loop.

### 7.3 The decision matrix

| Property                              | SignalR | Raw WebSocket |
|---------------------------------------|---------|---------------|
| Transport fallback (SSE, long poll)   | Yes     | No            |
| Reconnection state machine            | Yes     | DIY           |
| Server-to-client RPC framing          | Yes     | DIY           |
| Groups / rooms                        | Yes     | DIY           |
| Scale-out backplane                   | Yes     | DIY           |
| Per-message overhead                  | 30-100B | 2-14B (frame) |
| Latency for cross-region same-DC      | ~1-5ms  | ~1-5ms        |
| Multi-language client SDKs            | Yes     | Browser only  |
| Binary frame support                  | Yes (with MessagePack)  | Yes |
| Streaming (`IAsyncEnumerable`)        | Yes     | DIY           |

Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets> for the raw-WebSocket side; the SignalR introduction for the framed side.

## 8. The `Clients` proxy — six addressing modes

Inside a hub method, `Clients` is a proxy that lets you address a set of connections. Six modes are worth memorising:

```csharp
public sealed class ChatHub : Hub
{
    public Task SendToEveryone(string text)
        => Clients.All.SendAsync("ReceiveMessage", text);

    public Task SendToEveryoneExceptMe(string text)
        => Clients.Others.SendAsync("ReceiveMessage", text);

    public Task SendToMeOnly(string text)
        => Clients.Caller.SendAsync("ReceiveMessage", text);

    public Task SendToRoom(string room, string text)
        => Clients.Group(room).SendAsync("ReceiveMessage", text);

    public Task SendToOneUser(string userId, string text)
        => Clients.User(userId).SendAsync("ReceiveMessage", text);

    public Task SendToOneConnection(string connectionId, string text)
        => Clients.Client(connectionId).SendAsync("ReceiveMessage", text);
}
```

The naming is consistent: `Clients.<who>.SendAsync(<event-name>, <args>...)`. Six variations:

1. `Clients.All` — every connected client on this hub.
2. `Clients.Others` — every connected client except the caller.
3. `Clients.Caller` — just the connection that invoked the current method.
4. `Clients.Group(name)` — every connection in the named group (covered in Lecture 2).
5. `Clients.User(userId)` — every connection authenticated as the given user (covered in Lecture 2).
6. `Clients.Client(connectionId)` — one specific connection by its `ConnectionId`.

Two additional variants exist for "all except some":

- `Clients.AllExcept(connId1, connId2)` — every connection except the listed ones.
- `Clients.GroupExcept(name, connId1)` — every connection in the group except the listed ones.

The first is occasionally useful; the second is the typical shape for "echo this chat message to everyone in the room except the sender."

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#the-clients-object>.

## 9. The lifecycle hooks — `OnConnectedAsync` and `OnDisconnectedAsync`

Two virtual methods on `Hub` are called by the framework around each connection's lifetime:

```csharp
public sealed class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Called once, when this connection is established.
        await Clients.Caller.SendAsync("Welcome", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Called once, when this connection is closed. `exception` is non-null
        // if the disconnect was due to an error; null if the client closed
        // cleanly.
        await base.OnDisconnectedAsync(exception);
    }
}
```

Five properties are worth naming:

1. **`OnConnectedAsync` runs in the same per-call hub-instance model as a regular method.** The instance is constructed, the method runs, the instance is disposed. Any state you need to remember about this connection must be persisted in a singleton service (or, later, in Redis).
2. **`OnDisconnectedAsync` is called with an `Exception?`** that distinguishes clean shutdown (the client called `connection.stop()`) from a transport failure (the WebSocket broke). Clean shutdown is `null`; error shutdown carries the exception.
3. **`OnDisconnectedAsync` is best-effort.** If the client process crashes, the server will not be told immediately — it discovers the disconnect when the next keep-alive timeout expires (server-side default: 30s). Do not rely on `OnDisconnectedAsync` being called within milliseconds of the actual disconnect.
4. **Both hooks have access to `Context.ConnectionId`**, the `Context.UserIdentifier` (if authenticated; null otherwise), and `Context.Items` (a per-connection dictionary scoped to the connection's lifetime, not the per-call hub instance).
5. **`Context.Items` is the place to stash small per-connection state.** It survives across multiple hub-method invocations on the same connection. It does *not* survive a reconnect; the new connection has a fresh `Items` dictionary.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#handle-events-for-a-connection>.

## 10. `HubCallerContext` — the per-call accessor

Inside a hub method, `this.Context` is a `HubCallerContext` that gives you:

- `ConnectionId` — the negotiated identifier for this connection.
- `User` — the `ClaimsPrincipal` if authenticated, with an `Identity` and `Claims`. Anonymous connections have `Context.User` non-null but `Context.User.Identity?.IsAuthenticated == false`.
- `UserIdentifier` — a stable per-user string, produced by `IUserIdProvider` (covered in Lecture 2). Defaults to `User.FindFirst(ClaimTypes.NameIdentifier)?.Value`.
- `ConnectionAborted` — a `CancellationToken` that fires when the connection drops mid-method. Pass it to any `await` you make inside a long-running hub method.
- `Items` — the per-connection dictionary mentioned above.

The full reference is <https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.signalr.hubcallercontext>.

## 11. The connection from the client side — a first .NET client

The .NET client (`Microsoft.AspNetCore.SignalR.Client`) mirrors the server API closely:

```csharp
#nullable enable
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hubs/chat")
    .WithAutomaticReconnect()
    .Build();

connection.On<string, string>("ReceiveMessage", (user, text) =>
{
    Console.WriteLine($"{user}: {text}");
});

await connection.StartAsync();

await connection.InvokeAsync("SendMessage", "alice", "hello");

Console.ReadLine();
await connection.StopAsync();
```

Six constructs are worth naming:

1. **`HubConnectionBuilder`** is the configuration entry point. `.WithUrl(...)`, `.WithAutomaticReconnect(...)`, `.WithHubProtocol(...)`, `.ConfigureLogging(...)` are the methods you reach for.
2. **`connection.On<TA, TB>(name, handler)`** registers a typed handler for server-pushed events. The generic arguments must match the types the server sends. If they do not match, the deserializer throws.
3. **`connection.InvokeAsync(name, args...)`** calls a server-side hub method and awaits the result. If the method returns `Task`, the await completes when the server's task completes; if it returns `Task<T>`, the result is deserialized and returned.
4. **`connection.SendAsync(name, args...)`** is the fire-and-forget variant. It returns when the message has been written to the wire; it does *not* wait for the server to process it. Prefer `InvokeAsync` when you need to know the call succeeded.
5. **`StartAsync` and `StopAsync`** open and close the connection. They are idempotent in the sense that calling `StartAsync` twice without a `StopAsync` in between throws.
6. **The connection is a long-lived object**, often stored in a singleton in DI. It is not cheap to construct; you do not new one up per message.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client>.

## 12. The connection from the client side — a first JS / TS client

The JS / TS client (`@microsoft/signalr` on npm) is the same shape:

```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat")
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Information)
    .build();

connection.on("ReceiveMessage", (user: string, text: string) => {
    console.log(`${user}: ${text}`);
});

await connection.start();

await connection.invoke("SendMessage", "alice", "hello");
```

The naming is camelCase rather than PascalCase, but every method has a one-to-one mapping to the .NET client. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client>.

## 13. Reading the wire with `wscat`

You can connect to a SignalR hub from the command line with `wscat` (a CLI WebSocket client; install via `npm install -g wscat`). The dialogue looks roughly like this:

```bash
# Step 1: negotiate over plain HTTP
curl -X POST -H "Content-Length: 0" http://localhost:5000/hubs/chat/negotiate?negotiateVersion=1
# Response includes connectionToken=...

# Step 2: open the WebSocket with the connection token
wscat -c "ws://localhost:5000/hubs/chat?id=<connectionToken>"

# Step 3: the SignalR protocol handshake (record-separator-terminated JSON)
> {"protocol":"json","version":1}^^

# Step 4: server replies with {}^^ to confirm
< {}

# Step 5: invoke a hub method
> {"type":1,"target":"SendMessage","arguments":["alice","hello"]}^^

# Step 6: server broadcasts to all (you'll see this echoed)
< {"type":1,"target":"ReceiveMessage","arguments":["alice","hello"]}
```

The `^^` in the diagrams represents the ASCII record separator byte (0x1E) that terminates every JSON SignalR message. Without it the server treats the bytes as an incomplete message and waits indefinitely.

**Read the wire at least once.** The discipline of stepping through the protocol byte by byte is what separates "SignalR works on my machine" from "I know what is happening when it does not."

Citation for the protocol spec: <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md>.

## 14. What we did not cover (yet)

- **Groups (rooms).** Lecture 2 walks the full lifecycle and the typical patterns.
- **Authentication.** Lecture 2 walks the JWT-on-the-upgrade story end to end.
- **Scale-out.** Lecture 3 walks the Redis backplane configuration and what it actually puts on the wire.
- **The MessagePack protocol option.** Lecture 3 walks the swap and the byte-savings measurement.
- **Reconnection in detail.** Lecture 3 walks the state machine and the state-on-reconnect pattern.
- **Streaming with `IAsyncEnumerable<T>`.** Lecture 3 walks the wire envelopes and the client-side consumer.

## 15. Exercise pointer

Now do **Exercise 1 — First Hub and Negotiate**. Stand up a one-method `EchoHub`, connect from `curl` plus `wscat`, and read the negotiate JSON byte by byte. The acceptance criterion is that you can recite the contents of `availableTransports` from memory by the time you finish.

## 16. Summary

- A `Hub` is a class with server-callable methods; the framework dispatches by name with reflection (cached).
- A hub instance is **per-call**, not per-connection. Shared state lives in singletons or in `Context.Items`.
- The negotiate handshake at `/hubs/<name>/negotiate` returns a transport list; the client picks the first supported transport.
- Three transports: WebSockets (preferred), Server-Sent Events (text only, falls back when WebSockets are blocked), long polling (universal fallback).
- Hub vs raw WebSocket: pick SignalR for chat / dashboards / notifications; pick raw WebSocket for binary high-rate feeds where the envelope tax matters.
- The `Clients` proxy has six addressing modes: `All`, `Others`, `Caller`, `Group(name)`, `User(userId)`, `Client(connectionId)`.
- `OnConnectedAsync` and `OnDisconnectedAsync` are the per-connection lifecycle hooks; `OnDisconnectedAsync` is best-effort and may fire late.
- Both the .NET and JS/TS clients are thin wrappers over the same protocol; the API names match in spirit.
- Always read the wire at least once with `wscat` plus `curl`. The bytes do not lie.

Cited Microsoft Learn pages this lecture pulled from: <https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/dotnet-client>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client>. Source-link references: `Hub.cs`, `HttpConnectionDispatcher.cs`, `HubConnection.cs` in `dotnet/aspnetcore`.
