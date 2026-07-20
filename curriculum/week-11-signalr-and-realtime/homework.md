# Week 11 — Homework

Six practice problems that consolidate the week's material. They are sized to ~45 minutes each. Do them after the lectures and the exercises; do them before the mini-project. Cite the URLs you used while solving each one in the commit message of your homework branch.

## Problem 1 — The negotiate audit

Open the browser dev tools, Network tab, on a running SignalR application (your Exercise 1 or any sample). Capture the full request / response for the negotiate POST. Write a 200-word post explaining:

1. What every field in the negotiate response means (`negotiateVersion`, `connectionId`, `connectionToken`, `availableTransports`, `transferFormats`).
2. Why the response is a POST not a GET (hint: the client sends nothing in the body, but POST is correct because the request is conceptually creating a new connection resource on the server).
3. Why the `connectionToken` is distinct from the `connectionId` (hint: one is the application identifier, the other is the transport-level bearer token).

Cite: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-allowed-transports> and the negotiate-handler source-link at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/common/Http.Connections/src/Internal/HttpConnectionDispatcher.cs>.

Deliverable: `homework/01-negotiate-audit.md` with the captured JSON, a screenshot of the Network tab, and the 200-word write-up.

## Problem 2 — Hub vs raw WebSocket — the decision

Write a 300-word essay arguing the case for SignalR vs a raw `WebSocket` for each of the following four scenarios. For each one, declare a choice and justify it in terms of the decision matrix from Lecture 1.

- **Scenario A:** A collaborative document editor with up to 50 simultaneous editors per document, where every keystroke is broadcast to the other 49 editors. Average payload: 100 bytes. Peak rate: ~30 keystrokes/sec across the document.
- **Scenario B:** A multiplayer game where each player's state (position, velocity, action flags) is broadcast at 60 frames per second. Each state update is ~80 bytes. Latency budget: 50ms end-to-end.
- **Scenario C:** A live financial dashboard showing the latest price of 200 instruments. Updates arrive at 1-5 per second per instrument. Total rate: ~500 updates/sec across all instruments. Each update is ~40 bytes.
- **Scenario D:** A notification system that pushes "you have a new direct message" alerts to logged-in users. Update rate: ~1 update per user per minute. Connection count: 100k concurrent.

Cite the decision matrix in Lecture 1 and at least one Microsoft Learn URL.

Deliverable: `homework/02-hub-vs-websocket.md` with the four declarations and justifications.

## Problem 3 — JWT-on-upgrade threat model

In one page, walk through the threat model of the JWT-on-the-WebSocket-upgrade pattern. Cover:

1. **Token leakage via URL logging.** The `access_token` query-string parameter is the URL; URLs are logged in proxies, webserver access logs, and browser history. What is the mitigation? (Hint: short-lived tokens, restricted query-string acceptance to `/hubs/*`, TLS for the connection.)
2. **Replay attacks.** If an attacker captures the token, they can connect as the user. What is the mitigation? (Hint: short-lived tokens, IP-based audit logs, server-side revocation lists are *not* a SignalR feature and require additional infrastructure.)
3. **Cross-site WebSocket hijacking (CSWSH).** A malicious origin opens a WebSocket to your hub URL. The browser carries cookies; can it carry the access_token? (Hint: no, the malicious site does not have access to the user's token. But cookies are a different story; CORS configuration matters.)
4. **The `[Authorize]` default.** Why is "every hub is `[Authorize]` unless explicitly justified" the right policy? (Hint: anonymous hubs in development frequently ship to production.)

Cite the OWASP HTML5 / WebSocket cheat sheet at <https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html#websockets> and the SignalR auth chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>.

Deliverable: `homework/03-jwt-threat-model.md`.

## Problem 4 — Redis backplane MONITOR walkthrough

Run the mini-project's two-instance + Redis topology (or build a minimal one from Exercise 2). In a third terminal, run `docker exec -it redis-week11 redis-cli MONITOR`. Connect two browser clients, each to a different SignalR instance. Have one send a message to a room that the other is in.

Document what you see in the MONITOR stream:

1. Which Redis channel the message is published on.
2. What the published payload looks like (it is MessagePack-encoded; you can use `xxd` or `hexdump` on a saved sample to see the bytes).
3. Which instance's SignalR process is the publisher and which is the subscriber.
4. What happens when you stop the Redis container mid-conversation (per-instance broadcasts still work; cross-instance does not).
5. What happens when you restart Redis (the SignalR instances reconnect automatically; cross-instance traffic resumes for new messages; in-flight messages from the outage are lost).

Cite the Redis pub/sub reference at <https://redis.io/docs/latest/develop/interact/pubsub/> and `RedisHubLifetimeManager.cs` at <https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/server/StackExchangeRedis/src/RedisHubLifetimeManager.cs>.

Deliverable: `homework/04-redis-monitor.md` with annotated MONITOR output and the answers to the five questions above.

## Problem 5 — Reconnection schedule design

Design a reconnection schedule for each of the following three applications. Justify the schedule in terms of (a) user expectations, (b) network conditions, and (c) server load on simultaneous reconnect storms.

- **Application A:** A team chat application. Users have it open in a browser tab all day. Network failures are typically transient (3-30 seconds). User expectation: messages I sent before the disconnect should arrive once I am back; the UI should show "reconnecting" without alarming.
- **Application B:** A mobile-web sports-scores app. Network conditions are highly variable (LTE, WiFi, train tunnels). Reconnect attempts should be aggressive at first then back off significantly. User expectation: updates resume soon after connectivity returns; old missed updates can be backfilled via a separate HTTP fetch.
- **Application C:** An internal admin dashboard. Users are on a corporate network with stable connectivity. Reconnect failures usually indicate the server itself is down for deployment. User expectation: do not aggressively retry during a planned 5-minute deploy window; surface a clear "server unavailable" error.

For each application, write the `withAutomaticReconnect([...])` array (in ms) and explain the choice. Then think about the **thundering-herd reconnect** problem: when a SignalR server restarts, 10k clients all try to reconnect at the same instant. How does your schedule mitigate that? (Hint: jitter the schedule per-client, or implement an `IReconnectPolicy` that adds randomisation.)

Cite the configuration reference at <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-reconnect>.

Deliverable: `homework/05-reconnect-design.md`.

## Problem 6 — MessagePack byte savings — measured

Repeat the JSON-vs-MessagePack comparison from Lecture 3 on a payload representative of your application. Pick one hub method that you (or one of the exercise hubs) emits frequently. Capture 100 broadcasts of the method via the browser dev tools WebSocket Frames view; sum the total bytes for JSON and for MessagePack. Report:

1. The per-message byte count under each protocol.
2. The ratio MessagePack:JSON.
3. The throughput improvement you would expect on a saturated 100 Mbps link (hint: byte count is the bottleneck, not message count).
4. The parse-time improvement, by adding `performance.now()` markers around the SDK's deserialisation. The browser's `@microsoft/signalr` source is at <https://github.com/dotnet/aspnetcore/tree/main/src/SignalR/clients/ts/signalr> if you want to source-link.

Then write 200 words on when MessagePack is worth the swap and when it is not. The break-even is roughly "broadcasts faster than 100/second sustained or payloads larger than 500 bytes."

Cite the MessagePack-CSharp benchmark page at <https://github.com/MessagePack-CSharp/MessagePack-CSharp#performance> and the SignalR MessagePack chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>.

Deliverable: `homework/06-messagepack-bytes.md` with the captured numbers and the 200-word analysis.

## Submission

Push the six deliverables on a branch named `week11-homework/<your-handle>` and open a PR against the C9 curriculum repository. The PR description should link to each of the six files and include a 100-word summary of what you learned.

The teaching staff reviews homework PRs within 5 business days. Reviews focus on whether you have read the citations and whether your reasoning holds together, not on perfect grammar. The single most common review comment is "where is your citation for this claim" — preempt it by linking the Microsoft Learn URL or GitHub source for every non-trivial assertion.

Cited Microsoft Learn pages this homework draws from: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol>. Source-link references: `HttpConnectionDispatcher.cs`, `RedisHubLifetimeManager.cs`, the `@microsoft/signalr` TS source in `dotnet/aspnetcore`.
