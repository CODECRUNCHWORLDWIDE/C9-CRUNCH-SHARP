# Lecture 3 — Scale-Out with a Redis Backplane, the MessagePack Protocol, Reconnection State, and Streaming with `IAsyncEnumerable`

> **Time:** 2 hours. Take the backplane and protocol material in one sitting and the reconnection / streaming material in a second sitting. **Prerequisites:** Lectures 1 and 2. **Citations:** the Redis-backplane chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>, the MessagePack-protocol chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>, the configuration / reconnect chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-reconnect>, and the streaming chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming>.

## 1. Why these four topics together

These four topics share a single design pressure: **production conditions are not development conditions, and the patterns that ship to production all relax some assumption that development implicitly held.** Development assumes one server instance; production runs three for redundancy and the Redis backplane is the canonical fix. Development assumes JSON for ease of debugging; production swaps to MessagePack for throughput. Development assumes the WebSocket stays open; production assumes it breaks every twelve hours on average for a thousand subtle reasons, and the reconnection state machine plus the state-on-reconnect pattern is the canonical fix. Development assumes a hub method returns a single value; production assumes a hub method may need to stream a thousand values progressively, and `IAsyncEnumerable<T>` is the canonical fix.

Read this lecture alongside the four Microsoft Learn pages cited above.

## 2. Scale-out — the problem that needs a backplane

Suppose you have one SignalR server. A connection on that server joins `room:42`; the membership is in the server's in-memory `HubLifetimeManager`. Someone sends a message to `room:42`; the manager looks up the connections in the group and writes the message to each WebSocket. End to end the broadcast is local.

Now suppose you have two SignalR servers behind a load balancer for redundancy. Connection A lands on server 1; it joins `room:42` in server 1's in-memory manager. Connection B lands on server 2; it joins `room:42` in server 2's in-memory manager. Each server has its own view of `room:42` membership. Connection A sends a message to `room:42` via server 1; server 1 looks up local members of `room:42` and finds only itself. The message never reaches connection B.

This is the **two-instance fan-out problem**. The fix is a **backplane**: a shared message bus that every SignalR instance publishes outbound broadcasts to, and every SignalR instance subscribes to. When server 1 wants to broadcast to `room:42`, it publishes the message to the backplane; every server (including server 1) receives it; each server then writes the message to its locally-connected members of `room:42`. The shared bus has decoupled "the instance the message originated on" from "the instance the recipient is connected to."

`Microsoft.AspNetCore.SignalR.StackExchangeRedis` is the canonical backplane. It uses Redis pub/sub channels as the shared bus; every instance subscribes to a set of well-known channel names; every broadcast publishes one message per addressing mode.

## 3. Configuring the Redis backplane

The package is `Microsoft.AspNetCore.SignalR.StackExchangeRedis`. The configuration is one method call:

```csharp
builder.Services
    .AddSignalR()
    .AddStackExchangeRedis("localhost:6379", options =>
    {
        options.Configuration.ChannelPrefix =
            StackExchange.Redis.RedisChannel.Literal("crunch-chat");
    });
```

Three things are worth naming:

1. **`AddStackExchangeRedis` takes a connection string** in the same format as `StackExchange.Redis` directly. Single-instance Redis: `"localhost:6379"`. Redis Sentinel: `"sentinel1:26379,sentinel2:26379,serviceName=master"`. Redis Cluster: a comma-separated list of cluster nodes. Citation: <https://stackexchange.github.io/StackExchange.Redis/Configuration>.
2. **`ChannelPrefix` is non-optional in production.** Without it, every SignalR application sharing the Redis instance will receive every other application's broadcasts. The prefix isolates channels by application name. Pick something descriptive — `"crunch-chat"`, `"notifications-prod"` — and put it in configuration.
3. **The package is server-side only.** The JS and .NET clients are unaware of the backplane; they connect to whichever server the load balancer routes them to and see broadcasts arrive via that server's WebSocket as if everything were local.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>.

## 4. What the backplane actually publishes — reading the Redis MONITOR stream

The cleanest way to understand the backplane is to watch what it puts on Redis. Open three terminals:

- Terminal 1: `docker exec -it redis-week11 redis-cli MONITOR`. Streams every Redis command in real time.
- Terminal 2: `dotnet run --project src/Server` (instance 1, listening on 5001).
- Terminal 3: `dotnet run --project src/Server` (instance 2, listening on 5002, same Redis).

Now connect two browser clients, one to each instance. Have client A (on instance 1) send a message to `room:42`; observe the MONITOR stream. You will see, approximately:

```
1700000000.000 [0 192.168.1.100:55432] "PUBLISH" "crunch-chat:internal:groups" "<binary message>"
1700000000.001 [0 192.168.1.101:55433] "SUBSCRIBE" "crunch-chat:internal:groups"
```

The message is a MessagePack-encoded blob containing: the group name (`room:42`), the method name (`ReceiveMessage`), the positional arguments (`["room:42", "alice", "hello"]`), and a list of `excludedConnectionIds` (for `GroupExcept`). Both server instances are subscribed; both see the message; each one looks up its local members of `room:42` and writes the message to their WebSockets.

The channel naming follows a pattern: `<prefix>:internal:<scope>`. Scopes include `groups`, `user-<id>`, `all`, `connection-<id>`. The source is `RedisHubLifetimeManager.cs` at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/StackExchangeRedis/src/RedisHubLifetimeManager.cs>. Reading that file once will pin the design in your head; it is about 700 lines and well-commented.

## 5. Failure modes — what happens when Redis goes down

If the Redis backplane becomes unreachable:

1. **Per-instance broadcasts still work.** A connection on instance 1 can still receive messages from connections on instance 1. The in-instance fan-out path does not go through Redis.
2. **Cross-instance broadcasts silently fail.** A connection on instance 1 will not receive messages from connections on instance 2 until Redis comes back. The framework does not throw; it just cannot deliver.
3. **The instances reconnect to Redis automatically** using the `StackExchange.Redis` reconnection logic. When Redis comes back, the cross-instance fan-out resumes for new messages. **Messages that were broadcast during the Redis outage are lost.** SignalR's backplane is fire-and-forget; it does not buffer.
4. **There is no built-in "missed messages" replay.** If your application requires that no message is ever lost, you cannot rely on the backplane alone; you must also persist messages to a durable store (Postgres, Kafka, etc.) and have the client re-fetch from that store on reconnect. The mini-project demonstrates this pattern.

For most chat applications, the "messages lost during Redis outage" failure mode is acceptable: a brief outage drops a few seconds of messages and the application recovers. For applications where every message matters (audit logs, financial events), use a durable store as the source of truth and SignalR as the live notification layer.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane#considerations>.

## 6. Choosing between Redis backplane and Azure SignalR Service

ASP.NET Core supports a managed alternative: the **Azure SignalR Service**. The hub class is unchanged; the configuration adds `services.AddSignalR().AddAzureSignalR(...)`. The connection management is offloaded to Azure; your application server only handles hub method invocations.

The trade:

- **Redis backplane**: you operate Redis. Cheaper for moderate scale (under 10k concurrent connections). All hops are on your network. Configuration is one line.
- **Azure SignalR**: Azure operates the connection pool. Scales to 1M+ concurrent connections out of the box. Higher per-message cost. Configuration is two lines plus a connection string.

For C9 we cover the Redis backplane in depth because it is the open-source, language-portable, debuggable option. Azure SignalR is fine and a one-line swap when needed. Citation: <https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-overview>.

## 7. The MessagePack protocol — the byte-savings switch

By default, SignalR's protocol is JSON. Every message looks like this on the wire:

```json
{"type":1,"target":"ReceiveMessage","arguments":["room:42","alice","hello"]}
```

That is 76 bytes. The same message encoded with the MessagePack protocol is approximately 38 bytes — about half. For high-frequency broadcasts the saving is real.

### 7.1 Server side — add the protocol

Install `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`:

```bash
dotnet add package Microsoft.AspNetCore.SignalR.Protocols.MessagePack --version 8.0.0
```

In `Program.cs`:

```csharp
builder.Services
    .AddSignalR()
    .AddMessagePackProtocol(options =>
    {
        // Optional: customise the MessagePack serialization options.
        // The default uses the contractless resolver, which serializes
        // public properties of types you have not annotated.
    });
```

That is it. The server now advertises both JSON and MessagePack in the negotiate response's `availableProtocols`. The client picks one; the chosen protocol is used for the lifetime of the connection.

### 7.2 Client side — opt the JS client into MessagePack

Install the npm package:

```bash
npm install @microsoft/signalr-protocol-msgpack
```

In your TypeScript:

```ts
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", { accessTokenFactory: () => myToken })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect()
    .build();
```

The .NET client picks up the protocol from `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hubs/chat")
    .AddMessagePackProtocol()
    .Build();
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>.

### 7.3 What changes on the wire

The same `SendToRoom("room:42", "hello")` invocation that produced 76 bytes of JSON now produces 38 bytes of MessagePack. The negotiate response gains `"protocol": "messagepack"` in the chosen-protocol field. The frame structure is otherwise identical.

In the browser dev tools, switch the Network tab to the WebSocket frames view. JSON frames are human-readable; MessagePack frames show as binary (a hex view). Toggle between protocols and watch the per-frame size drop.

### 7.4 The serialization model

MessagePack uses a different serialization model than `System.Text.Json`. The default `ContractlessStandardResolver` serializes public properties by name; the alternative `StandardResolver` requires `[MessagePackObject]` and `[Key(int)]` attributes on every type. The contractless resolver is the default for SignalR and is the easier starting point.

For maximum performance and explicit control, annotate types:

```csharp
using MessagePack;

[MessagePackObject]
public sealed class ChatMessage
{
    [Key(0)] public string Room { get; set; } = "";
    [Key(1)] public string User { get; set; } = "";
    [Key(2)] public string Text { get; set; } = "";
    [Key(3)] public long Timestamp { get; set; }
}
```

The `[Key(n)]` integer is the position in the MessagePack array; you cannot reorder these without breaking wire compatibility. For SignalR-broadcast types, this matters because clients pinned to an older `[Key]` ordering will deserialize incorrectly. Treat `[Key(n)]` ordering as part of your protocol contract.

Citation: <https://github.com/MessagePack-CSharp/MessagePack-CSharp#quick-start>.

### 7.5 The MessagePack-CSharp library

The serializer underneath SignalR's MessagePack protocol is **`MessagePack-CSharp`** at <https://github.com/MessagePack-CSharp/MessagePack-CSharp>. It is one of the fastest binary serializers for .NET; benchmark numbers are at <https://github.com/MessagePack-CSharp/MessagePack-CSharp#performance>. It is also useful outside of SignalR for any binary serialization need (caching, message queues, on-disk storage). The package is MIT-licensed and well-maintained.

## 8. Reconnection — the state machine

A `HubConnection` (both .NET and JS) maintains an internal state machine with these states:

- **Disconnected** — initial state, or after `stop()`.
- **Connecting** — the negotiate request and transport handshake are in flight.
- **Connected** — fully established, sending and receiving normally.
- **Reconnecting** — the transport dropped and the client is waiting / retrying.
- **Disconnected (terminal)** — give-up state after exhausting retries.

The transitions you care about:

- `start()` from `Disconnected` → `Connecting` → `Connected` (or → `Disconnected` on failure).
- Transport drop while `Connected` (network blip, server restart) → `Reconnecting` → `Connected` (or → `Disconnected` after retries exhaust).
- `stop()` from any non-terminal state → `Disconnected`.

`withAutomaticReconnect()` opts the client into the state machine. Without it, a transport drop terminates the connection and the application must call `start()` again manually.

### 8.1 The default reconnect schedule

The default backoff is `[0, 2000, 10000, 30000]` milliseconds (immediate, 2s, 10s, 30s) and then give up. After the fourth failed retry, the connection transitions to terminal `Disconnected`. The total reconnect window is therefore 42 seconds.

```ts
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat")
    .withAutomaticReconnect()  // default schedule
    .build();
```

For a longer window, pass an array:

```ts
.withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 60000, 60000, 60000])
```

For "retry forever," implement `IReconnectPolicy` (JS) or `IRetryPolicy` (.NET) and return a non-null next-delay forever:

```ts
.withAutomaticReconnect({
    nextRetryDelayInMilliseconds: (retryContext) => {
        // Linear backoff, capped at 60s, forever.
        return Math.min(60000, 1000 * retryContext.previousRetryCount);
    }
})
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-reconnect>.

### 8.2 The reconnect callbacks

The connection exposes three event hooks:

```ts
connection.onreconnecting((error) => {
    console.warn("Connection lost. Reconnecting.", error);
    // Update UI to show "reconnecting..." state.
});

connection.onreconnected((connectionId) => {
    console.log("Reconnected with new ConnectionId:", connectionId);
    // Re-join rooms, re-fetch state, replay buffered actions.
});

connection.onclose((error) => {
    console.error("Connection closed permanently.", error);
    // Update UI to show "disconnected" state; offer manual retry.
});
```

The `onreconnected` callback is the load-bearing one. The new `ConnectionId` is already in place; the connection is fully usable; this is where you re-establish whatever state you need.

### 8.3 The state-on-reconnect pattern

**The server has forgotten about the old connection.** Its groups, its `Context.Items`, its in-flight invocations — all gone. The new connection is fresh.

The application's responsibility on reconnect:

1. **Re-join rooms.** The client tracks the local set of rooms in `myRooms`; the `onreconnected` handler iterates the set and calls `JoinRoom(...)` for each.
2. **Re-fetch state.** If the application has a "last seen message ID" cursor, the client invokes `FetchMessagesSince(lastSeenId)` to fill in the gap.
3. **Replay unsent local actions.** If the user typed three messages during the disconnect window and the client buffered them locally, replay them via `SendToRoom(...)` after the rooms are rejoined.
4. **Idempotency.** Replayed actions must be idempotent or de-duplicated server-side. The mini-project demonstrates the client-generated `messageId` pattern: the client assigns a UUID to every message; the server keeps a small recent-message-id set per user and ignores duplicates.

```ts
const myRooms = new Set<string>();
const outboundBuffer: ChatMessage[] = [];
let lastSeenId = 0;

connection.onreconnected(async () => {
    // Step 1: re-join rooms
    for (const room of myRooms) {
        await connection.invoke("JoinRoom", room);
    }

    // Step 2: re-fetch missed messages
    const missed = await connection.invoke<ChatMessage[]>(
        "FetchMessagesSince", lastSeenId);
    for (const m of missed) {
        renderMessage(m);
        lastSeenId = Math.max(lastSeenId, m.id);
    }

    // Step 3: replay buffered outbound
    const toReplay = outboundBuffer.splice(0);
    for (const m of toReplay) {
        await connection.invoke("SendToRoom", m.room, m.text, m.clientMessageId);
    }
});
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client#reconnect-clients>.

### 8.4 Keep-alive and server timeout

Two timeouts govern the connection's idle behaviour:

- **Server keep-alive interval** (default 15s): if the server has nothing to send for 15s, it sends a keep-alive ping frame.
- **Client server-timeout** (default 30s): if the client receives nothing from the server for 30s, it considers the connection dead and triggers a reconnect.

The ratio should be approximately 2:1 — the client expects two missed keep-alives before giving up. The defaults work for most networks; if you are behind a proxy with a shorter idle timeout (some load balancers cut idle connections at 60s), shorten the server keep-alive to 30s and the client server-timeout to 90s on both sides.

Server side:

```csharp
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});
```

Client side:

```ts
connection.serverTimeoutInMilliseconds = 30000;
connection.keepAliveIntervalInMilliseconds = 15000;
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-additional-options>.

## 9. Streaming — server-to-client with `IAsyncEnumerable<T>`

A hub method that returns `IAsyncEnumerable<T>` is treated by the framework as a **stream**: each yielded item is sent as a `StreamItem` envelope, the consumer receives them one at a time, and the stream ends with a `StreamCompletion` envelope.

### 9.1 The server side

```csharp
public sealed class LogsHub : Hub
{
    public async IAsyncEnumerable<LogEntry> StreamLogs(
        string room,
        int sinceId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in _logStore.ReadSince(room, sinceId, cancellationToken))
        {
            yield return entry;
        }
    }
}
```

Three properties are worth naming:

1. **The return type is `IAsyncEnumerable<T>` (or `ChannelReader<T>`).** The framework recognises both and dispatches as a stream.
2. **The `[EnumeratorCancellation]` attribute on the `CancellationToken`** wires the token into the `await foreach`. When the client cancels the stream, the token fires and the underlying `await foreach` cancels.
3. **The method is `async` even though it does not return `Task`.** The `async` keyword with `IAsyncEnumerable<T>` permits `await` inside the body. The compiler generates the state machine for the asynchronous iteration.

### 9.2 The client side — JavaScript

```ts
const subscription = connection.stream<LogEntry>("StreamLogs", "room:42", 1000);

subscription.subscribe({
    next: (entry) => {
        renderLogEntry(entry);
    },
    complete: () => {
        console.log("Stream completed.");
    },
    error: (err) => {
        console.error("Stream errored:", err);
    }
});

// To cancel from the client side:
subscription.dispose();
```

The `subscribe` callbacks fire as items arrive. `dispose()` sends a `CancelInvocation` envelope to the server, which fires the `CancellationToken` and stops the iteration.

### 9.3 The client side — .NET

```csharp
var stream = connection.StreamAsync<LogEntry>("StreamLogs", "room:42", 1000);

await foreach (var entry in stream)
{
    Console.WriteLine($"{entry.Id}: {entry.Text}");
}
```

The .NET client returns an `IAsyncEnumerable<T>` of its own; the consumer `await foreach`s it like any other async sequence. Cancellation via the standard `CancellationToken` pattern.

### 9.4 Client-to-server streaming

The reverse direction works the same way with a `ChannelReader<T>` parameter:

```csharp
public async Task UploadLogs(ChannelReader<LogEntry> entries)
{
    await foreach (var entry in entries.ReadAllAsync())
    {
        await _logStore.AppendAsync(entry);
    }
}
```

The client constructs a channel and writes to it as items become available:

```ts
const channel = new signalR.Subject<LogEntry>();

connection.send("UploadLogs", channel);  // start the stream

for (const entry of localBuffer) {
    channel.next(entry);
}
channel.complete();
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming>.

## 10. The full reconnect-aware client — putting it together

```ts
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

interface ChatMessage {
    id: number;
    clientMessageId: string;  // for idempotency on replay
    room: string;
    user: string;
    text: string;
    timestamp: number;
}

const myRooms = new Set<string>();
const outboundBuffer: ChatMessage[] = [];
let lastSeenId = 0;

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", { accessTokenFactory: () => getToken() })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Information)
    .build();

connection.on("ReceiveMessage", (msg: ChatMessage) => {
    renderMessage(msg);
    lastSeenId = Math.max(lastSeenId, msg.id);
});

connection.onreconnecting((error) => {
    setStatusBar("reconnecting", error?.message);
});

connection.onreconnected(async () => {
    setStatusBar("connected", null);
    for (const room of myRooms) {
        await connection.invoke("JoinRoom", room);
    }
    const missed = await connection.invoke<ChatMessage[]>(
        "FetchMessagesSince", lastSeenId);
    for (const m of missed) {
        renderMessage(m);
        lastSeenId = Math.max(lastSeenId, m.id);
    }
    const toReplay = outboundBuffer.splice(0);
    for (const m of toReplay) {
        await connection.invoke("SendToRoom", m.room, m.text, m.clientMessageId);
    }
});

connection.onclose((error) => {
    setStatusBar("disconnected", error?.message);
});

async function send(room: string, text: string) {
    const clientMessageId = crypto.randomUUID();
    if (connection.state !== signalR.HubConnectionState.Connected) {
        outboundBuffer.push({
            id: 0, clientMessageId, room, user: "me", text,
            timestamp: Date.now()
        });
        return;
    }
    await connection.invoke("SendToRoom", room, text, clientMessageId);
}
```

This is the client the mini-project builds on. It demonstrates: MessagePack protocol, JWT-via-`accessTokenFactory`, custom reconnect schedule, the `onreconnecting` / `onreconnected` / `onclose` triplet, the rejoin-rooms-and-refetch pattern, and the offline-buffer-with-replay pattern.

## 11. Observability — `dotnet-counters` for SignalR

The relevant counter sources are:

- `Microsoft.AspNetCore.Http.Connections` — connection-level counters.
- `Microsoft.AspNetCore.SignalR` — hub-level counters.

```bash
dotnet-counters monitor \
    --process-id $(pgrep -f Crunch.Chat.Server) \
    Microsoft.AspNetCore.Http.Connections \
    Microsoft.AspNetCore.SignalR
```

The counters you will see include:

- `connections-started` — total connections opened on this process.
- `connections-stopped` — total connections closed on this process.
- `connections-duration` — moving distribution of connection lifetimes.
- `current-connections` — connections currently open.
- `messages-received` (per hub) — total inbound messages.
- `messages-sent` (per hub) — total outbound messages.

For Redis-backplane installations, add `StackExchange.Redis` to the counter list to monitor pub/sub publish rate.

Citation: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/available-counters>.

## 12. Hub filters — cross-cutting concerns

Hub filters are the SignalR analogue of MVC action filters: server-side middleware that wraps every hub-method invocation:

```csharp
public sealed class LoggingHubFilter : IHubFilter
{
    private readonly ILogger<LoggingHubFilter> _log;
    public LoggingHubFilter(ILogger<LoggingHubFilter> log) => _log = log;

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        _log.LogInformation("Invoking {Method} on connection {ConnId}",
            invocationContext.HubMethodName, invocationContext.Context.ConnectionId);
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Hub method {Method} threw", invocationContext.HubMethodName);
            throw;
        }
    }
}

builder.Services.AddSignalR(options =>
{
    options.AddFilter<LoggingHubFilter>();
});
```

Use cases: structured logging, metrics, validation, rate-limiting per connection. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters>.

## 13. Putting the week together — Crunch Chat architecture

The mini-project, `Crunch Chat`, combines every concept from the three lectures:

- **Lecture 1**: a strongly-typed `ChatHub : Hub<IChatClient>` with `OnConnectedAsync` and `OnDisconnectedAsync` hooks. The negotiate handshake is observed through the browser Network tab; the wire format is observed through `wscat` and the MONITOR stream.
- **Lecture 2**: `[Authorize]` at the class level, `JoinRoom` / `LeaveRoom` / `SendToRoom` with groups, JWT bearer on the upgrade via `OnMessageReceived`, a `RoomTracker` singleton for "who is in which room."
- **Lecture 3**: Redis backplane via `AddStackExchangeRedis`, MessagePack protocol via `AddMessagePackProtocol`, full reconnect / replay client, an `IAsyncEnumerable<LogEntry>` history-stream method.

The runtime topology is two `Crunch.Chat.Server` instances behind nginx, a Redis container for the backplane, a Postgres container for message durability, and a Vite-served browser client that connects through nginx. The `docker-compose.yml` brings the whole thing up with `docker compose up`.

## 14. Exercise pointer

Now do **Exercise 3 — Streaming and Reconnect**. Write an `IAsyncEnumerable<int>` hub method that yields 1..1000 with a `Task.Delay(10)` between each, consume it from the .NET and JS clients, and verify the items arrive progressively. Then disconnect the network (toggle wifi, kill the WebSocket from the browser dev tools, or `iptables`-block the port) mid-stream and verify the stream completes with an error rather than hanging. After that, do **Exercise 4 — Client TypeScript**, building the full JS/TS client with reconnect / replay against the hub from Exercise 2.

## 15. Summary

- A single SignalR server keeps group memberships in memory; multiple instances need a **backplane** (Redis) to fan out broadcasts across instances.
- `AddStackExchangeRedis(connectionString)` is the one-line configuration. Set a `ChannelPrefix` to isolate applications sharing a Redis instance.
- The backplane publishes per-broadcast messages on `<prefix>:internal:<scope>` channels. Watch with `redis-cli MONITOR`.
- Redis-outage failure mode: per-instance broadcasts still work; cross-instance is lost until Redis comes back. No built-in replay.
- The **MessagePack protocol** swap is two lines (server: `AddMessagePackProtocol`; JS client: `withHubProtocol(new MessagePackHubProtocol())`). Byte savings are roughly 50%; parse-time savings 3-5x.
- `MessagePack-CSharp` at <https://github.com/MessagePack-CSharp/MessagePack-CSharp> is the underlying library, useful outside SignalR too.
- **Reconnection** is opt-in via `withAutomaticReconnect()`. Default schedule: 0s, 2s, 10s, 30s, then give up.
- **State on reconnect** is the application's responsibility: re-join rooms, re-fetch missed state, replay buffered actions. The server has forgotten the old connection.
- **Streaming** with `IAsyncEnumerable<T>` returns: each `yield return` is a `StreamItem` envelope. Use `[EnumeratorCancellation]` to wire the token.
- Reverse-direction streaming with `ChannelReader<T>` parameter; client-side `signalR.Subject<T>`.
- `dotnet-counters` exposes `connections-started`, `connections-stopped`, `connections-duration`, `current-connections`, `messages-received`, `messages-sent`.

Cited Microsoft Learn pages this lecture pulled from: <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-reconnect>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters>. Source-link references: `RedisHubLifetimeManager.cs`, `MessagePackHubProtocolWorker.cs`, `HubConnection.cs` in `dotnet/aspnetcore`. External: `MessagePack-CSharp` at <https://github.com/MessagePack-CSharp/MessagePack-CSharp>.
