# Mini-Project — Crunch Chat: Multi-Instance Real-Time Chat with JWT Auth, Redis Backplane, MessagePack, and Resilient Reconnect

> **Time:** 8 hours across Thursday-Saturday-Sunday. **Prerequisites:** Exercises 1-4 and (ideally) both challenges. **Citations:** every Microsoft Learn URL referenced in the three lecture notes, the `dotnet/aspnetcore` SignalR source, the `MessagePack-CSharp` repository.

## The spec

You are building **Crunch Chat**, a small multi-room chat application that demonstrates every concept from Week 11. The runtime topology:

```
                  +----------+
                  |  browser |   (Vite, port 5173)
                  +-----+----+
                        |
                        v
                  +----------+
                  |   nginx  |   (port 8080, load-balanced)
                  +-----+----+
                        |
            +-----------+-----------+
            v                       v
       +---------+              +---------+
       | chat-1  |              | chat-2  |   (.NET 8 SignalR servers)
       +----+----+              +----+----+
            |                        |
            +-----------+------------+
                        |
                        v
                  +----------+
                  |  redis   |   (port 6379, pub/sub backplane)
                  +----------+
                        |
                        v
                  +----------+
                  | postgres |   (port 5432, durable message store)
                  +----------+
```

Everything runs via `docker compose up`. The browser connects through nginx at `http://localhost:8080/hubs/chat`; load balancing distributes new connections across `chat-1` and `chat-2`; the Redis backplane fans messages out cross-instance; Postgres is the durable store for message history.

## Functional requirements

### F1 — Authentication

- All hub methods require JWT bearer auth via the `access_token` query-string parameter on the negotiate request.
- The JWT bearer middleware's `OnMessageReceived` extracts the token only for paths starting with `/hubs/`.
- The application includes a dev-only `/dev/token?user=<name>` endpoint that mints a 1-hour JWT for the named user. In production, this endpoint is removed and tokens come from your real auth provider.

### F2 — Rooms

- Users can `JoinRoom(string roomId)` and `LeaveRoom(string roomId)` via hub methods.
- Joining a room broadcasts a `UserJoinedRoom(room, user)` event to existing members.
- Leaving a room broadcasts a `UserLeftRoom(room, user)` event.
- `OnDisconnectedAsync` cleans up the user's rooms in the singleton `RoomTracker` and broadcasts `UserLeftRoom` for each.

### F3 — Messages

- `SendToRoom(string room, string text, string clientMessageId)` broadcasts a `ReceiveMessage(room, user, text, id, timestamp)` event to every connection in the room.
- The `clientMessageId` is a client-generated UUID for idempotent replay. The server keeps a per-user dedupe cache; duplicate `clientMessageId`s are dropped silently.
- Every accepted message is persisted to Postgres with a server-assigned monotonic `id`.

### F4 — History

- `FetchSince(string room, long sinceId)` returns the list of `LogEntry` records in the room with `id > sinceId`. Used by the client on reconnect to fill the gap.
- `StreamHistory(string room, long sinceId)` returns the same data as an `IAsyncEnumerable<LogEntry>` stream, for progressive loading of long histories.

### F5 — Reconnection

- The client uses `withAutomaticReconnect` with a schedule of `[0, 2000, 5000, 10000, 30000]`.
- On `onreconnected`, the client re-joins all rooms, re-fetches missed messages, and replays the outbound queue.
- The outbound queue is persisted to localStorage so it survives a page reload during an outage.

### F6 — Wire format

- The application uses the MessagePack protocol (`AddMessagePackProtocol` on the server; `withHubProtocol(new MessagePackHubProtocol())` on the client).
- The browser dev tools Frames view shows binary frames, not JSON.

### F7 — Scale-out

- Two `chat-N` instances run behind nginx with `least_conn` load balancing.
- The Redis backplane ensures a broadcast from chat-1 reaches connections on chat-2.

### F8 — Observability

- The app exposes `dotnet-counters` for `Microsoft.AspNetCore.Http.Connections` and `Microsoft.AspNetCore.SignalR`.
- A `/health` endpoint returns 200 if SignalR, Redis, and Postgres are all reachable.
- Server logs include the connection lifecycle, the hub-method dispatch, and the disconnect reason for every connection.

## Non-functional requirements

### NF1 — Build and run

- `docker compose up` brings up the full topology in under 60 seconds on commodity hardware.
- The first browser connection succeeds within 500ms of page load.
- A user can send a message and see it in another browser tab within 100ms (95th percentile) on local Docker.

### NF2 — Code quality

- C# code uses nullable references enabled and file-scoped namespaces.
- Every hub method has explicit input validation; invalid input throws `HubException` with a user-presentable message.
- Every async method awaits or returns the task; no fire-and-forget except where explicitly justified in a comment.

### NF3 — Citations

- Every non-trivial implementation choice has a citation comment pointing at Microsoft Learn or the `dotnet/aspnetcore` source.
- The `README.md` lists every external dependency with version and license.

## Suggested project layout

```
Crunch.Chat/
├── docker-compose.yml
├── nginx.conf
├── PERF.md                  <-- the perf write-up (see below)
├── README.md                <-- top-level description, build, run
├── src/
│   ├── Server/
│   │   ├── Server.csproj
│   │   ├── Program.cs
│   │   ├── ChatHub.cs
│   │   ├── CatalogDb.cs     <-- see starter (PostgreSQL DbContext)
│   │   ├── RoomTracker.cs
│   │   ├── DedupeCache.cs
│   │   ├── MessageStore.cs  <-- backed by EF Core + Postgres
│   │   ├── TokenIssuer.cs   <-- dev-only
│   │   ├── PerfMeasurer.cs  <-- see starter
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   └── Client/
│       ├── package.json
│       ├── tsconfig.json
│       ├── vite.config.ts
│       ├── index.html
│       └── src/
│           ├── main.ts
│           ├── reconnect.ts
│           ├── queue.ts
│           └── style.css
└── tests/
    └── Smoke/
        ├── Smoke.csproj
        └── ChatSmokeTests.cs
```

## Starter files

A small starter scaffold is provided in `mini-project/starter/`. Copy it as your starting point:

- `Program.cs` — minimal SignalR + JWT + Redis registration.
- `ChatHub.cs` — the hub class with stubs for all hub methods; the bodies are exercises for you to complete.
- `CatalogDb.cs` — the EF Core `DbContext` for the durable message store (one table: `Messages`).
- `PerfMeasurer.cs` — a small utility that subscribes to SignalR's `EventSource` and prints message throughput every 5 seconds.
- `appsettings.Development.json` — config with JWT and Redis settings.

The starter compiles but does not run end to end. Your work is to fill in the stubbed methods, add the client, write the `docker-compose.yml`, and write the perf write-up.

## The perf write-up (`PERF.md`)

Run the application and capture these measurements. Treat the perf write-up as part of the deliverable, not an afterthought.

### M1 — Cold start

`docker compose up` from clean; how long until the first browser connection succeeds? Target: under 60 seconds on commodity hardware.

### M2 — Message round-trip latency

Open two tabs, both in the same room, ideally landing on different SignalR instances. Send 100 messages from tab-1; record the per-message round-trip time (send-timestamp on tab-1 to receive-timestamp on tab-2). Report median and 95th percentile. Target: median under 10ms; p95 under 50ms on local Docker.

### M3 — Wire-byte saving with MessagePack

Capture 100 message broadcasts in the browser dev tools Frames view. Sum the per-frame byte count under JSON and under MessagePack (toggle the protocol). Report the ratio. Target: MessagePack:JSON ratio between 0.45 and 0.55.

### M4 — Cross-instance broadcast

Verify two browser tabs land on different SignalR instances. Send a message from one; observe arrival in the other. Confirm the message took the Redis backplane path (read `redis-cli MONITOR` while doing it). Report the path you observed.

### M5 — Reconnect window

Connect a tab. `docker stop chat-1` (the instance the tab is on; tab transitions to `Reconnecting`). Wait varying amounts of time, then `docker start chat-1`. Report the reconnect time (Reconnecting → Reconnected) for each of: 5s, 30s, 60s outages. Target: < 30s reconnect after a 30s outage; >47s outages may exhaust the default retry schedule.

### M6 — Idempotency on replay

While disconnected, type three messages. Reconnect. Verify exactly three messages arrive in the other tabs, not duplicates. Then call `connection.invoke("SendToRoomIdempotent", "general", "test", "<a-specific-uuid>")` twice from the browser console with the same UUID; verify exactly one broadcast.

### M7 — Counter values

Run `dotnet-counters monitor` against one chat instance during a 10-minute interactive test. Report the final values of `current-connections`, `connections-started`, `connections-stopped`, and (if exposed) `messages-received`. Sanity-check that `connections-started == connections-stopped + current-connections`.

## Grading rubric

- **40 points: functional correctness.** Every functional requirement (F1-F8) is implemented and demonstrable.
- **20 points: non-functional quality.** Build is clean; code is idiomatic; citations are present.
- **15 points: the perf write-up.** All seven measurements (M1-M7) are reported with the captured numbers and a one-sentence interpretation each.
- **10 points: reconnect / replay resilience.** Killing chat-1 mid-conversation does not lose messages or duplicate them; the user-visible UI state transitions correctly.
- **10 points: source-link comments.** At least 10 distinct citations in the source code pointing at Microsoft Learn or `dotnet/aspnetcore`.
- **5 points: tests.** The smoke-test project demonstrates that the hub starts, accepts a JWT-authenticated connection, and round-trips a message.

## Stretch goals

1. **Direct messages.** Add `SendDirectMessage(string toUserId, string text, string clientMessageId)` that uses `Clients.User(toUserId)` to broadcast to every connection authenticated as that user. Verify with three tabs of the same user (browser, mobile, desktop): all three receive the DM.
2. **Typing indicators.** Add a `Typing(string room)` hub method that broadcasts `UserTyping(room, user)` to the room except the caller. Throttle on the client to fire at most once per 2 seconds per room. Discuss why this is one of the few cases where `Clients.GroupExcept` is the right addressing mode.
3. **Read receipts.** Add per-message read tracking: when a client renders a message, it invokes `MarkRead(messageId)`; the server broadcasts `MessageReadBy(messageId, userId)` to the room. Persist the read-set in Postgres for offline-then-online catch-up.
4. **Strongly-typed client interface.** Replace `Hub` with `Hub<IChatClient>` and the corresponding interface in the client. Verify the compile error if you typo a method name on either side. Discuss the wire shape (the method name is still a string on the wire; the compile-time check is C# only).
5. **OpenTelemetry distributed tracing.** Wire `Microsoft.AspNetCore.OpenTelemetry` so that one connection's lifetime is a single trace, every hub-method invocation is a span, and the Redis publish is a span underneath the hub-method span. Report what the trace looks like in Jaeger.

## Submission

Push the project on a branch named `week11-mini-project/<your-handle>` and open a PR against the C9 curriculum repository. The PR description must link to `PERF.md` and include a screenshot of two browser tabs receiving the same message across instances.

The teaching staff reviews mini-project PRs within 7 business days. Reviews focus on (a) whether the eight functional requirements are met, (b) whether the perf write-up has real measurements, (c) whether the code reads like the editorial code style of the lecture-note examples, and (d) whether the citations are present and accurate.

Cited Microsoft Learn pages: every page referenced in the three lecture notes plus <https://learn.microsoft.com/en-us/aspnet/core/signalr/diagnostics> for the observability section. Source-link references: `Hub.cs`, `HubConnectionHandler.cs`, `RedisHubLifetimeManager.cs`, `MessagePackHubProtocolWorker.cs`, `JwtBearerEvents.cs`, the TypeScript-client `HubConnection.ts` — all in `dotnet/aspnetcore`. External: `MessagePack-CSharp` at <https://github.com/MessagePack-CSharp/MessagePack-CSharp>.
