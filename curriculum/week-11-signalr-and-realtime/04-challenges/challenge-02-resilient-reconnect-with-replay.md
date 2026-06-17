# Challenge 2 — Resilient Reconnect with Idempotent Replay

> **Time:** 2 hours. **Prerequisites:** Exercises 1, 2, 3, 4. Challenge 1 is helpful but not required. **Citations:** the configuration / reconnect chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-reconnect>, the JavaScript-client chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client>, and `HubConnection.cs` at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/clients/csharp/Client.Core/src/HubConnection.cs>.

## The premise

Build a chat client that **never loses a user's intent** across a network disconnect. The user types a message; the message either arrives at the server exactly once, or the client surfaces an explicit "could not send" error after retrying for a configurable window. Duplicates are not allowed; lost messages are not allowed (within the retry window).

The constraints:

1. The client may go offline at any moment, including between when the user types and when the SDK has handed the message to the WebSocket.
2. The client may go offline after the WebSocket has sent the message but before the server has acknowledged it (the typical case: TCP keepalive timeout, ~30 seconds after the underlying network dropped).
3. The server may receive the same message twice if the client replays it after a reconnect. The server must dedupe.
4. The client must show the user a clear status: "sent," "sending," "queued (offline)," "failed (gave up after N retries)."

By the end, you will have implemented the full end-to-end resilient send pattern: client-side queue with persistence, idempotency keys, server-side dedupe, retry with exponential backoff, and an "abandon after N retries" terminal state.

## The architecture

```
+----------+   1. user types          +-------------------+
|  user    +-------------------------> |  outbound queue   |
+----------+                          +---------+---------+
                                                |
                                                | 2. flush attempt
                                                v
                                       +-------------------+
                                       | connection.invoke |
                                       +---------+---------+
                                                |
                                                | 3. wire (with clientMessageId)
                                                v
                                       +-------------------+
                                       |     hub method    |
                                       +---------+---------+
                                                |
                                                | 4. dedupe lookup
                                                v
                                       +-------------------+
                                       |   dedupe cache    |
                                       +-------------------+
                                       (drop duplicate;
                                        broadcast original)
```

## Server side

Reuse the `SendToRoomIdempotent` method and `DedupeCache` from Exercise 3. The server-side contribution is small: the dedupe cache, the storage append, and the broadcast. The hard work is client-side.

```csharp
public async Task SendToRoomIdempotent(
    string room, string text, string clientMessageId)
{
    if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        throw new HubException("Message must be 1-4096 characters.");

    string user = Context.UserIdentifier ?? "anonymous";

    if (!_dedupe.TryRegister(user, clientMessageId))
    {
        // Duplicate. Drop silently; the original broadcast already happened.
        return;
    }

    var entry = await _store.Append(room, user, text, clientMessageId);
    await Clients.Group(room).ReceiveMessage(room, user, text);
}
```

For full idempotency on the broadcast side, also store the broadcast in a durable store keyed by `clientMessageId`. On replay, look up the existing broadcast and resend it from the store rather than re-publishing — that way the same message gets the same server-assigned `LogEntry.Id`. The Exercise 3 implementation skips this step for simplicity; for production-grade systems it matters.

## Client side — the outbound queue with persistence

The queue is a localStorage-backed array of `{room, text, clientMessageId, attemptCount, firstQueuedAt}`:

```ts
interface QueuedMessage {
    room: string;
    text: string;
    clientMessageId: string;
    attemptCount: number;
    firstQueuedAt: number;
}

const STORAGE_KEY = "crunch-chat-outbound";

function loadQueue(): QueuedMessage[] {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw === null) return [];
    try { return JSON.parse(raw) as QueuedMessage[]; }
    catch { return []; }
}

function saveQueue(queue: QueuedMessage[]): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(queue));
}

const outbound: QueuedMessage[] = loadQueue();
```

The `firstQueuedAt` timestamp lets us implement the "abandon after N minutes" terminal state. The `attemptCount` lets us back off exponentially per-message.

## Client side — the send-or-queue function

```ts
async function send(room: string, text: string): Promise<void> {
    const msg: QueuedMessage = {
        room,
        text,
        clientMessageId: crypto.randomUUID(),
        attemptCount: 0,
        firstQueuedAt: Date.now()
    };
    outbound.push(msg);
    saveQueue(outbound);
    renderQueueStatus();
    await flushQueue();
}

async function flushQueue(): Promise<void> {
    if (connection.state !== signalR.HubConnectionState.Connected) {
        return;  // wait for onreconnected to retry
    }

    while (outbound.length > 0) {
        const msg = outbound[0];
        try {
            await connection.invoke(
                "SendToRoomIdempotent",
                msg.room, msg.text, msg.clientMessageId);
            outbound.shift();
            saveQueue(outbound);
            renderQueueStatus();
        } catch (err) {
            msg.attemptCount++;
            saveQueue(outbound);
            const ageMs = Date.now() - msg.firstQueuedAt;
            if (ageMs > 10 * 60 * 1000 || msg.attemptCount >= 50) {
                // Give up on this message; surface terminal error.
                renderFailed(msg, err);
                outbound.shift();
                saveQueue(outbound);
                continue;
            }
            // Stop flushing for now; rely on onreconnected to retry.
            renderRetrying(msg, err);
            return;
        }
    }
}
```

Two properties are worth naming:

1. **`outbound.shift()` only after the `invoke` resolves.** The successful invoke is the acknowledgment that the server has received and processed the message; only then is it safe to drop the local copy.
2. **The give-up condition has two limits.** Time (10 minutes) bounds the user-visible "stuck queued" state; attempt count (50) bounds the cost of a misbehaving server.

## Client side — the reconnect handler

```ts
connection.onreconnected(async (newConnId) => {
    // Re-join rooms (the server has forgotten our memberships).
    for (const room of myRooms) {
        await connection.invoke("JoinRoom", room);
    }
    // Re-fetch any messages we missed.
    for (const room of myRooms) {
        const missed = await connection.invoke<LogEntry[]>(
            "FetchSince", room, lastSeenId);
        for (const m of missed) {
            renderMessage(m);
            if (m.id > lastSeenId) lastSeenId = m.id;
        }
    }
    // Replay outbound queue. Each message's clientMessageId is unchanged;
    // the server's DedupeCache drops anything it already saw.
    await flushQueue();
});
```

The `flushQueue` call replays the queue in arrival order. Each invocation carries the original `clientMessageId`, so:

- If the server already processed the message during a partial outage, the dedupe cache drops the second send. The local queue clears.
- If the server never processed the message, the dedupe cache registers it for the first time. The broadcast happens. The local queue clears.

Either way, the user's intent is honoured exactly once.

## Acceptance criteria

1. Typing a message with the server reachable produces an immediate broadcast and clears the queue.
2. Typing a message with the server unreachable produces a queued entry visible in the UI; the page reload (Cmd+R / F5) persists the queue (localStorage); reconnecting flushes the queue automatically.
3. Killing the server mid-send (after the WebSocket has accepted the bytes but before the broadcast) and restarting it produces exactly one broadcast in the other tab, not two — verifying the dedupe cache works.
4. Replaying the same `clientMessageId` via the dev tools (call `connection.invoke("SendToRoomIdempotent", "general", "test", "<same-uuid-as-before>")` twice) produces exactly one broadcast.
5. Disconnecting for longer than the "give up" window (10 minutes) surfaces a terminal "failed to send" error in the UI; the queue is cleared of that message; subsequent messages still work.
6. The UI shows four distinct states for every queued message: `sending`, `sent`, `queued (offline)`, `failed (gave up)`.

## Stretch goals

1. **Server-side deletion of dedupe entries.** Currently the `DedupeCache` is a ring of fixed size; very old replays could in principle slip through. Replace the ring with a Redis-backed dedupe (keys with TTL of 1 hour) and verify that replays still dedupe correctly within the window.
2. **Per-message ack from the server.** Currently `SendToRoomIdempotent` returns `void` (the client treats invoke completion as the ack). Change the method to return `Task<long>` returning the assigned `LogEntry.Id`, and surface that id in the client UI so the user sees the server-assigned identity, not just the local UUID. Verify that replayed messages return the **same** id (because they hit the dedupe cache and return the original id).
3. **Cross-tab queue coordination.** With two tabs of the same user on the same browser, both have their own `outbound` queue in localStorage. If both tabs queue a message during an outage, both replay on reconnect — that is correct, both messages should arrive. But if both tabs queue the *same* message (the user clicked Send in both tabs), the dedupe cache catches it. Document this behaviour and consider whether you want a shared cross-tab queue (using the `BroadcastChannel` API) or per-tab isolation.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client>. Source-link references: `HubConnection.cs` (server side) at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/Core/src/HubConnectionContext.cs> and `HubConnection.cs` (.NET client) at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/clients/csharp/Client.Core/src/HubConnection.cs>. The JS client's reconnect state machine is at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/clients/ts/signalr/src/HubConnection.ts>.
