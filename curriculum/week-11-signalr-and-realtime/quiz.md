# Week 11 — Quiz

Ten multiple-choice questions covering hubs, transports, groups, JWT on the upgrade, the Redis backplane, MessagePack, reconnection, and streaming. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — Hub instance lifetime

A `ChatHub : Hub` declares a `private int _messageCount = 0;` field and increments it inside `SendMessage`. After three calls from the same client over the same WebSocket, the value of `_messageCount` is:

- (A) 3, because the connection is reused.
- (B) 1, on every call, because a fresh hub instance is constructed per invocation.
- (C) 3, because instance fields survive across invocations on the same connection.
- (D) Undefined, because the framework does not guarantee field initialisation order.

## Question 2 — The negotiate response

When a SignalR client opens a connection, the first network request is:

- (A) An HTTP GET to the hub URL with `Upgrade: websocket`.
- (B) An HTTP POST to `<hub-url>/negotiate?negotiateVersion=1`.
- (C) A WebSocket open frame with the protocol name "signalr-v1" in the subprotocol header.
- (D) An HTTP OPTIONS request to discover allowed transports.

## Question 3 — Transports

Which transport in SignalR cannot carry binary payloads, making it incompatible with the MessagePack protocol?

- (A) WebSockets
- (B) Server-Sent Events
- (C) Long polling
- (D) All three transports support binary

## Question 4 — Groups

Which statement about SignalR groups is correct?

- (A) Group memberships are persisted to disk and survive a server restart.
- (B) When a connection drops, the framework retains its group memberships and re-applies them when the client reconnects with a new `ConnectionId`.
- (C) Group memberships exist only in memory (or in the Redis backplane in scale-out); when a connection drops, the framework discards them, and the client must re-join on reconnect.
- (D) Groups have a hard upper limit of 1000 members per group.

## Question 5 — JWT on the WebSocket upgrade

You want SignalR connections to authenticate via a JWT bearer token. The browser cannot set headers on a `WebSocket` upgrade request, so you cannot use `Authorization: Bearer ...`. What is the canonical SignalR pattern?

- (A) Send the token in a custom HTTP header on the negotiate request only; the WebSocket inherits the authentication.
- (B) Send the token in the `access_token` query-string parameter on the negotiate request; configure the JWT bearer middleware's `OnMessageReceived` event to extract it for hub paths only.
- (C) Send the token in a cookie; SignalR requires cookies for authentication.
- (D) Send the token as the first message after the WebSocket upgrade; the server validates it before accepting any other messages.

## Question 6 — Redis backplane failure modes

In a SignalR application scaled out across two instances with a Redis backplane, Redis becomes unreachable. Which statement is correct?

- (A) All SignalR functionality stops; both instances refuse new connections.
- (B) Per-instance broadcasts continue to work; cross-instance broadcasts silently fail until Redis recovers. Messages broadcast during the outage are lost.
- (C) The Redis client buffers all outbound publishes and replays them when Redis recovers, so no messages are lost.
- (D) The framework falls back to a peer-to-peer mesh between SignalR instances.

## Question 7 — MessagePack protocol

Which two configuration changes are required to switch a SignalR application from JSON to MessagePack?

- (A) Server: `services.AddSignalR().AddMessagePackProtocol()`. JS client: `.withHubProtocol(new MessagePackHubProtocol())` from `@microsoft/signalr-protocol-msgpack`.
- (B) Server: set `options.ContentType = "application/msgpack"`. JS client: no change required.
- (C) Server: change the URL from `/hubs/chat` to `/hubs/chat/msgpack`. JS client: pass `{ format: "binary" }` to `withUrl`.
- (D) MessagePack is the default in SignalR 8; no configuration is required.

## Question 8 — Reconnection state

A SignalR connection reconnects after a transient network failure. Which state from before the disconnect does the server preserve?

- (A) Group memberships, `Context.Items`, and the `ConnectionId`.
- (B) Group memberships only; `Context.Items` is reset and the `ConnectionId` is new.
- (C) Nothing; the server has forgotten the old connection. The new connection has fresh group memberships (none), fresh `Context.Items`, and a new `ConnectionId`. The client is responsible for re-establishing state.
- (D) Everything; the server holds the connection's state in Redis until the client returns.

## Question 9 — Streaming hub methods

A hub method declared as `public async IAsyncEnumerable<T> StreamFoo(...)` is dispatched by the framework as:

- (A) A regular method; the framework synchronously enumerates the sequence and returns it as one large result.
- (B) A stream; each `yield return` is sent as a `StreamItem` envelope, and the stream ends with a `StreamCompletion` envelope.
- (C) A push subscription; the framework wires the enumerable to the client's `connection.on("StreamFoo", ...)` handler.
- (D) Streaming hub methods must return `Task<IEnumerable<T>>`; `IAsyncEnumerable<T>` is not supported.

## Question 10 — `Clients` proxy

Inside a hub method, which `Clients.<...>` invocation sends a message to every connection in `room-42` *except* the connection that triggered the current invocation?

- (A) `Clients.GroupExcept("room-42", Context.ConnectionId).SendAsync(...)`
- (B) `Clients.Others.SendAsync(...)` after the connection has joined `room-42`
- (C) `Clients.Group("room-42").SendAsync(...)` minus a manual filter
- (D) Both (A) and (B), but (A) is the idiomatic answer for the room-broadcast case

---

## Answer key

- **Q1: (B).** A `Hub` instance is per-call. The framework constructs a fresh instance for every inbound invocation and disposes it when the invocation returns. Instance fields are reset. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#the-hubcontext>. For per-connection state, use `Context.Items` (lifetime = connection) or a singleton service (lifetime = process).
- **Q2: (B).** The negotiate is a POST to `<hub-url>/negotiate?negotiateVersion=1`. The server responds with a JSON document listing available transports; the client then opens the chosen transport. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-allowed-transports>.
- **Q3: (B).** SSE is text-only because it carries `text/event-stream`. WebSockets and long polling can carry binary. The MessagePack protocol requires binary; therefore MessagePack cannot run over SSE. The SignalR negotiate reports `"transferFormats": ["Text"]` for SSE.
- **Q4: (C).** Groups live in memory (Redis when scaled out). When a connection drops, memberships are discarded. The client must re-join on `onreconnected`. This is a property, not a bug — it means the server never has to garbage-collect dead group entries. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups>.
- **Q5: (B).** The canonical pattern is the `access_token` query-string parameter plus the `OnMessageReceived` hook gated on `Path.StartsWithSegments("/hubs")`. (A) is wrong because custom headers cannot be set on a browser WebSocket. (C) is wrong because cookies are one option but not required. (D) is wrong because the middleware authenticates the negotiate request before the WebSocket upgrade. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication>.
- **Q6: (B).** Per-instance broadcasts go through the in-instance manager and do not depend on Redis. Cross-instance broadcasts publish to Redis; if Redis is down, the publish fails silently and the message is not delivered to other instances. Messages broadcast during the outage are lost; SignalR's backplane is fire-and-forget without buffering. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane#considerations>.
- **Q7: (A).** Server: `services.AddSignalR().AddMessagePackProtocol()` (from `Microsoft.AspNetCore.SignalR.Protocols.MessagePack`). JS client: `.withHubProtocol(new MessagePackHubProtocol())` (from `@microsoft/signalr-protocol-msgpack`). Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>.
- **Q8: (C).** The server has forgotten the old connection entirely. Group memberships, `Context.Items`, and `ConnectionId` are all gone. The new connection is fresh. The client's `onreconnected` handler is responsible for re-joining rooms, re-fetching missed messages, and replaying buffered actions. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client#reconnect-clients>.
- **Q9: (B).** `IAsyncEnumerable<T>` (and `ChannelReader<T>`) return types are recognised as streams. Each `yield return` becomes a `StreamItem` envelope on the wire; the stream ends with a `StreamCompletion` envelope. Cancellation flows via a `[EnumeratorCancellation]`-tagged `CancellationToken` parameter. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming>.
- **Q10: (D).** Both `Clients.GroupExcept("room-42", Context.ConnectionId)` and `Clients.Others` after joining the room produce the same recipient set. The idiomatic choice for "broadcast to a room except the sender" is `GroupExcept`, because it does not rely on the implicit "is the current connection a member of this room" assumption that `Others` would otherwise require. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#the-clients-object>.

## Self-assessment

- 9-10: you can ship this week's mini-project without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact Microsoft Learn pages.
- 5-6: re-read the lecture notes end to end and redo the exercises.
- 0-4: rewind to Lecture 1 and read all three lecture notes carefully. The mini-project will not make sense without the conceptual foundation.
