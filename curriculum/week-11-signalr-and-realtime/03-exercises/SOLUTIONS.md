# Week 11 — Exercise Solutions and Annotations

These are the worked solutions for the four exercises. Read them after attempting the exercises, not before. Every code block has been built (where applicable, `dotnet build` clean; for TypeScript, `tsc --noEmit` clean) before being pasted here. The wire-format payloads have been captured from a real run.

## Exercise 1 — First Hub and Negotiate

### What success looks like

A clean run from a single terminal:

```
$ dotnet run --project src/Ex01.Server
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started.

[connect]    n9yQEdoJaB6xK8d-3Gqw3w
[connect]    p1xQYzoLcK7vM2e-5Hsx5y
[disconnect] n9yQEdoJaB6xK8d-3Gqw3w  (clean)
```

The connect / disconnect lines come from `OnConnectedAsync` / `OnDisconnectedAsync`.

### The negotiate response, decoded

`curl -X POST -H "Content-Length: 0" http://localhost:5000/hubs/echo/negotiate?negotiateVersion=1 | jq` returns approximately:

```json
{
  "negotiateVersion": 1,
  "connectionId": "n9yQEdoJaB6xK8d-3Gqw3w",
  "connectionToken": "f1f4f1b7-7c12-4f7e-9c2e-7a2b5d9ab9d6",
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

What each field means:

- `negotiateVersion=1` — the handshake-protocol version. Modern clients negotiate v1; v0 is legacy and uses `connectionId` as both the application identifier and the transport token.
- `connectionId` — the short identifier you see as `Context.ConnectionId` inside the hub. This is what your code references.
- `connectionToken` — the bearer used on the actual transport URL. The client opens `ws://localhost:5000/hubs/echo?id=<connectionToken>`. The server matches the token against an internal table; if it does not match, the upgrade fails.
- `availableTransports` — server-preference order. The client picks the first transport it also supports.
- `transferFormats` — per transport, what the transport can carry. SSE is text-only; the MessagePack protocol cannot run over SSE because MessagePack is binary.

### Common pitfalls

1. **Forgetting the record-separator byte (0x1E) on the protocol handshake.** Without it, the server treats the bytes as an incomplete message and waits indefinitely. Use a Node one-liner that writes the literal `\x1e` byte rather than typing through wscat.
2. **CORS not allowing the browser origin.** If the browser console shows "blocked by CORS policy," add `WithOrigins("http://localhost:5173")` plus `AllowCredentials()` to the CORS policy.
3. **Hub registered after `app.Run()`.** `MapHub<T>` must be called before `app.Run()` returns. The default `dotnet new web` template is fine; if you reordered the calls, fix the order.

## Exercise 2 — Groups and JWT

### What success looks like

Three observable behaviours:

1. Negotiate without a token: `HTTP/1.1 401 Unauthorized`.
2. Negotiate with a valid token: `HTTP/1.1 200 OK` and the transport list.
3. Two browser tabs in the same room see each other's messages; a third tab not in the room sees nothing.

### The token-extraction hook — re-read

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) &&
            path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```

The path predicate (`path.StartsWithSegments("/hubs")`) is non-negotiable. Removing it makes the application accept query-string tokens on every endpoint, which is a security regression — query strings are logged in proxies and webserver access logs. Restrict the query-string acceptance to hub paths.

### `Context.UserIdentifier` — where does it come from

With a JWT carrying a `sub` claim equal to `"alice"`, the JWT bearer middleware maps `sub` to `ClaimTypes.NameIdentifier`. The default `IUserIdProvider` reads `NameIdentifier`. Therefore `Context.UserIdentifier == "alice"` inside the hub. If you want a different claim, register a custom `IUserIdProvider`.

### The `Context.Items` rooms set — why we use it

`Context.Items` is a per-connection dictionary that survives across hub-method invocations on the same connection. We stash the set of rooms this connection joined so that `OnDisconnectedAsync` can clean up the `RoomTracker` without us tracking the connection-to-rooms mapping in a separate singleton. The dictionary does **not** survive a reconnect — the new connection has a fresh `Items`. That is fine; the client re-joins on reconnect, which re-populates `Items` from scratch.

### Common pitfalls

1. **Forgetting `UseAuthentication()` and `UseAuthorization()` before `MapHub`.** Middleware order matters; the SignalR endpoint is just another endpoint and the auth middleware must run before it.
2. **JWT key too short.** `SymmetricSecurityKey` requires a key of at least 16 bytes; for `HmacSha256`, 32 bytes is the minimum that does not feel sketchy. The exercise's dev key is 64 chars and is fine.
3. **Token in the path instead of the query string.** Some tutorials show `/hubs/chat/<token>`; SignalR's convention is the query-string `access_token`. Stick with the convention; the client SDK builds the URL for you.

## Exercise 3 — Streaming and Reconnect

### What success looks like

A console session that survives a server restart:

```
$ dotnet run --project src/Ex03.Client
[connected] X8yQEdoJaB6xK8d-3Gqw3w
> hello
[general] <alice> hello
  [stream] 1: <alice> hello
> ^C  # server killed here, then restarted ~5s later
[reconnecting] WebSocket closed with status code: 1006
[reconnected]  new connId=Y9zREfpKaC7yL9d-4Hrx4z
> hi after reconnect
[general] <alice> hi after reconnect
  [stream] 2: <alice> hi after reconnect
```

The new `ConnectionId` is different. The streamed view picked up from where it left off because we re-issued `StreamAsync` from `lastSeenId` in the `Reconnected` handler (which the cited code shows in `ConsumeStream`).

### The dedupe ring — why it is bounded

The `DedupeCache` keeps the most-recent 1024 client-message-ids per user. Why a ring rather than an unbounded set?

- **Memory cost.** An unbounded set grows for the lifetime of the process. A 1024-entry ring is about 64 KB per user (UUID strings).
- **Time-locality.** A duplicate replay happens within seconds of the original send, not hours later. A 1024-entry ring covers any practical replay window.
- **Correctness.** A duplicate older than 1024 messages back is exceedingly rare; even if it happens, the cost is a duplicate message in the UI, not data corruption.

If your application cannot tolerate any duplicate ever, use a persistent dedupe table in Postgres rather than an in-memory ring. The trade-off is one round-trip per send.

### The `[EnumeratorCancellation]` attribute — why it matters

```csharp
public async IAsyncEnumerable<LogEntry> StreamLogs(
    string room,
    int sinceId,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var entry in _store.ReadSince(room, sinceId, cancellationToken))
    {
        yield return entry;
    }
}
```

Without `[EnumeratorCancellation]`, the `CancellationToken` parameter is treated by the framework as a method argument that the client must supply. With the attribute, the framework wires the cancellation token from the client-side `dispose()` into the parameter, and the `await foreach` over `_store.ReadSince` receives the cancellation through its inner token. When the client cancels, the inner enumerator throws `OperationCanceledException`, the `await foreach` exits cleanly, and the stream completes.

The framework also fires the token automatically if the connection drops, so a transport failure mid-stream cancels server-side iteration without you doing anything.

### Common pitfalls

1. **Returning `IEnumerable<T>` (synchronous) instead of `IAsyncEnumerable<T>`.** SignalR will not recognise the synchronous shape as a stream; it will block the dispatch thread enumerating the whole sequence. Always use the async form.
2. **Forgetting to handle `OperationCanceledException` in the stream consumer.** Cancellation surfaces as this exception type; the consumer must catch it (or let it propagate harmlessly).
3. **Re-issuing the stream on every reconnect without `lastSeenId`.** The server-side iteration starts from id=0; the client re-receives every historical entry. Always track `lastSeenId` client-side and pass it into `StreamAsync` on the recovery path.

## Exercise 4 — Client TypeScript

### What success looks like

Two browsers (or two tabs) of `client/` running, both connected, both in `general`. Typing in one tab produces a message in both within ~50ms. Killing the server, typing in the tab (status shows "buffered"), and restarting the server produces the buffered message in the other tab within ~10s of the server coming back.

### `crypto.randomUUID()` — why we use it

Every send is tagged with a fresh UUID before the network call. The UUID is the client-message-id that the server's `DedupeCache` keys on. The same UUID is reused if the send is buffered and replayed; the server sees the second send, looks up the UUID, sees it is already in the cache, and drops it silently. This is idempotency for free, at the cost of one UUID generation per send.

`crypto.randomUUID()` is available in every modern browser and in Node 16+. It is cryptographically random; collision probability is negligible.

### `connection.state` vs `connection.connectionId`

- `connection.state` is one of `Disconnected`, `Connecting`, `Connected`, `Disconnecting`, `Reconnecting`. Use it to decide whether to send or buffer.
- `connection.connectionId` is the current `ConnectionId` or `null` if not connected. Changes on every reconnect. Useful for status display, not for application logic (which should not depend on the value).

### Why `crypto.randomUUID()` and not a server-assigned id

The server cannot assign the id at buffer time — by definition, the client is offline. The id must be assigned at send-attempt time, which is client-side. The server uses the id only as a dedupe key; it assigns its own monotonic `LogEntry.Id` separately.

### Common pitfalls

1. **Forgetting to wait for `connection.start()` before sending.** The `send` function checks `connection.state`; if you call `send` before `start` resolves, the state is `Disconnected` and the message is incorrectly buffered. Either await `start()` first or set a UI flag.
2. **Putting the token in `accessTokenFactory` as a literal string.** That works once, but on reconnect the SDK calls the factory again; if your token rotated, the new call should return the *new* token. Always read from a cache that a refresh loop updates.
3. **Hardcoding `"http://localhost:5000"` in production.** Use a `VITE_HUB_URL` environment variable; Vite injects it at build time. Same applies to the `/dev/token` endpoint URL.

## Cross-cutting notes

- **Always read the wire at least once.** The Network tab in the browser dev tools shows the negotiate POST and the WebSocket upgrade for every connection. Switch to the Frames view to see per-message envelopes. The bytes do not lie.
- **Always log at the SignalR level in development.** Set `Microsoft.AspNetCore.SignalR: Debug` and `Microsoft.AspNetCore.Http.Connections: Debug` in `appsettings.Development.json`. You will see the hub-method dispatch, the transport-selection log, and the disconnect reason on every action.
- **Always use strongly-typed hubs (`Hub<TClient>`) in production.** The compile-time check on client method names catches typos at build time. The Exercise 2 / 3 / 4 sequence uses strongly-typed hubs throughout.
- **Always use `[Authorize]` by default.** If a hub is anonymous, write a comment justifying it. The most common SignalR security mistake in production is a hub that was anonymous in development and shipped that way.

Cited references: <https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client>. Source-link references: `Hub.cs`, `HubConnectionHandler.cs`, `JwtBearerEvents.cs`, `HttpConnectionDispatcher.cs` in `dotnet/aspnetcore`.
