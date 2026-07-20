# Challenge 1 — Redis Backplane plus MessagePack: Two-Instance Cross-Server Broadcast

> **Time:** 2 hours. **Prerequisites:** Exercises 1, 2, 3. **Citations:** the Redis-backplane chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>, the MessagePack chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>, and `RedisHubLifetimeManager.cs` at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/StackExchangeRedis/src/RedisHubLifetimeManager.cs>.

## The premise

You have a working chat hub from Exercise 2 (single instance, JWT auth, groups, JSON protocol). You will add a Redis backplane, swap the protocol to MessagePack, run two SignalR instances behind nginx, and demonstrate that a broadcast from instance 1 reaches a client connected to instance 2.

By the end you will have measured: (a) the wire-byte saving from MessagePack on a representative payload, (b) the cross-instance broadcast latency through the Redis backplane, and (c) the failure mode when Redis itself is interrupted.

## Setup

A `docker-compose.yml` at the project root:

```yaml
services:
  redis:
    image: redis:7
    container_name: redis-week11
    ports: [ "6379:6379" ]
    command: ["redis-server", "--appendonly", "no"]

  chat-1:
    build: .
    container_name: chat-1
    environment:
      - ASPNETCORE_URLS=http://0.0.0.0:8080
      - Redis__ConnectionString=redis:6379
      - Jwt__Issuer=crunch-chat-dev
      - Jwt__Audience=crunch-chat-dev
      - Jwt__Key=DEV-ONLY-CHANGE-IN-PRODUCTION-must-be-32-chars-or-more-DEV
    depends_on: [ redis ]

  chat-2:
    build: .
    container_name: chat-2
    environment:
      - ASPNETCORE_URLS=http://0.0.0.0:8080
      - Redis__ConnectionString=redis:6379
      - Jwt__Issuer=crunch-chat-dev
      - Jwt__Audience=crunch-chat-dev
      - Jwt__Key=DEV-ONLY-CHANGE-IN-PRODUCTION-must-be-32-chars-or-more-DEV
    depends_on: [ redis ]

  nginx:
    image: nginx:1.25
    container_name: chat-lb
    ports: [ "8080:8080" ]
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
    depends_on: [ chat-1, chat-2 ]
```

The `nginx.conf` (load balances with sticky sessions on `ip_hash` so each client lands on the same instance for the duration of a connection):

```nginx
events { worker_connections 1024; }

http {
    upstream chat_upstream {
        ip_hash;
        server chat-1:8080;
        server chat-2:8080;
    }

    server {
        listen 8080;

        location / {
            proxy_pass http://chat_upstream;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_read_timeout 600s;
        }
    }
}
```

## Server changes from Exercise 2

Add the two packages:

```bash
dotnet add package Microsoft.AspNetCore.SignalR.StackExchangeRedis --version 8.0.0
dotnet add package Microsoft.AspNetCore.SignalR.Protocols.MessagePack --version 8.0.0
```

Modify `Program.cs`:

```csharp
var redisConn = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString is required.");

builder.Services
    .AddSignalR(o =>
    {
        o.EnableDetailedErrors = builder.Environment.IsDevelopment();
    })
    .AddMessagePackProtocol()
    .AddStackExchangeRedis(redisConn, options =>
    {
        options.Configuration.ChannelPrefix =
            StackExchange.Redis.RedisChannel.Literal("crunch-chat");
    });
```

That is the entire server change. The hub class is identical to Exercise 2.

## Client changes

Install the MessagePack JS package:

```bash
npm install @microsoft/signalr-protocol-msgpack
```

In `main.ts`, change the connection builder:

```ts
import * as signalR from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:8080/hubs/chat", {  // through nginx
        accessTokenFactory: () => getToken()
    })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();
```

## The measurement plan

Open three browser tabs through `http://localhost:8080` (nginx). Verify in each tab's dev tools Network tab that the WebSocket connected to a different upstream instance (the `Server` response header on the initial nginx response or the `X-Upstream` you add yourself in the nginx config will reveal which). With `ip_hash` you may need to test from three different machines or change `ip_hash` to `least_conn` for the duration of the experiment.

### Measurement 1 — cross-instance broadcast latency

In one tab (connected to chat-1), instrument `SendToRoom` to log `performance.now()` immediately before `await connection.invoke(...)`. In another tab (connected to chat-2), instrument the `ReceiveMessage` handler to log `performance.now()` on receipt and send the value back to the first tab via a separate side channel (or write it to a shared log).

The round-trip time decomposes into:

- Browser-to-chat-1: ~1ms
- chat-1 publish to Redis: ~1ms
- Redis fan-out: ~1ms
- chat-2 subscribe-side: ~1ms
- chat-2 to browser: ~1ms

Total: ~5ms on local Docker. Report the measured value.

### Measurement 2 — wire-byte saving with MessagePack

Capture 100 broadcasts (use a script that fires 100 `SendToRoom` calls back to back) under each protocol. Sum the total bytes per WebSocket frame as reported by the browser dev tools Frames view. Report the ratio.

For the payload `{"room": "general", "user": "alice", "text": "hello"}` the ratio is approximately:

- JSON envelope: ~80 bytes per message.
- MessagePack envelope: ~38 bytes per message.
- Ratio MessagePack:JSON ≈ 0.48 (about 52% saving).

For larger payloads the ratio improves; for sub-50-byte payloads it narrows slightly because the MessagePack overhead has a fixed component.

### Measurement 3 — Redis-outage behaviour

While the chat is running, run `docker stop redis-week11`. Then send a message from tab-1 (chat-1) to a room that tab-2 (chat-2) is in. Observe:

- The send succeeds locally (tab-1's confirmation arrives; chat-1 broadcast to its local connections).
- The message does **not** reach tab-2 (cross-instance fan-out is broken).
- The client-side `connection.state` remains `Connected`; the application-level outage is invisible to the client SDK.

Restart Redis (`docker start redis-week11`). Within ~5s, the SignalR instances reconnect to Redis. New messages now fan out correctly. Messages sent during the outage are lost; there is no replay.

## Acceptance criteria

1. `docker compose up` produces a working three-container topology. `curl -i http://localhost:8080/hubs/chat/negotiate?negotiateVersion=1 -X POST -H "Content-Length: 0"` returns 401 (auth required); with `?access_token=<token>` it returns 200.
2. Two browser tabs connected through `localhost:8080` land on different SignalR instances (verify by adding `app.MapGet("/__instance", () => Environment.MachineName)` and reading the `MachineName` from each tab).
3. A message from tab-1 reaches tab-2 with round-trip latency under 10ms on local Docker.
4. The browser dev tools Frames view shows binary frames (MessagePack), not JSON. Per-frame byte count is roughly half what it was with JSON.
5. Stopping the Redis container while a tab is connected does not crash the SignalR instances; cross-instance broadcast silently stops; per-instance broadcast continues. Restarting Redis restores cross-instance broadcast.
6. A short write-up (`PERF.md`) reports the three measurements with the captured numbers.

## Stretch goals

1. **Add jitter to the reconnect schedule.** With two instances and 50 connections, simultaneously restart one instance. Observe the reconnect storm on the surviving instance. Then implement an `IReconnectPolicy` that adds 0-2000ms of random jitter per attempt and demonstrate the storm flattens.
2. **Sticky sessions for SSE / long polling.** The `ip_hash` directive is a workaround for the fact that SSE and long polling have separate inbound and outbound HTTP requests that must land on the same instance. Replace `ip_hash` with `least_conn`, force long polling on a client, and observe what breaks (the client's POSTs land on a different instance than its GETs; the server returns 404 because the connection token does not exist on that instance). Repair the configuration.
3. **Source-link the backplane.** Read `RedisHubLifetimeManager.cs` end to end. Document the channel naming scheme (`<prefix>:internal:<scope>`), the message envelope shape (the MessagePack-encoded payload published to Redis), and the subscription set the manager opens at startup. Write a 300-word post explaining what would change if you wanted to replace Redis with NATS as the backplane.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/scale>. Source-link references: `RedisHubLifetimeManager.cs` and `MessagePackHubProtocolWorker.cs` in `dotnet/aspnetcore`. External: `MessagePack-CSharp` at <https://github.com/MessagePack-CSharp/MessagePack-CSharp>.
