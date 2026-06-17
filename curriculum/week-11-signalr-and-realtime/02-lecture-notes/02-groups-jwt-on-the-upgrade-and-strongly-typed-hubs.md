# Lecture 2 — Groups (Rooms), JWT on the WebSocket Upgrade, `IUserIdProvider`, and Strongly-Typed Hubs

> **Time:** 2 hours. Take the groups material in one sitting and the authentication material in a second sitting. **Prerequisites:** Lecture 1 (hub anatomy, the negotiate handshake) and Week 6 (ASP.NET Core JWT bearer auth). **Citations:** the groups chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups>, the authentication-and-authorization chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, and the hubs chapter section on strongly-typed hubs at <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#strongly-typed-hubs>.

## 1. Why these three topics together

These three topics are grouped because they share a single design question: **who is on the other end of this connection, and what subset of broadcasts should reach them?** Groups answer "what topical room is this connection in." JWT-on-the-upgrade answers "what authenticated user is on this connection." Strongly-typed hubs answer "what method names is the client willing to receive, in a way the C# compiler can check." If you skip any of these three, your chat application either has no privacy (everyone gets every message), no identity (every user is `Context.User.Identity?.IsAuthenticated == false`), or no compile-time safety (you typo `"ReceiveMessage"` as `"RecieveMessage"` on one side and the bug only shows up at runtime).

Read this lecture alongside the three Microsoft Learn pages cited above, open in browser tabs.

## 2. Groups — a server-side bag of `ConnectionId`s

A **group** is a named set of `ConnectionId`s. You add a connection to a group with `Groups.AddToGroupAsync(connectionId, groupName)`, and you remove it with the symmetric `RemoveFromGroupAsync`. The framework keeps the mapping in memory (or in Redis when scaled out). Broadcast to the group with `Clients.Group(groupName).SendAsync(...)`.

### 2.1 A minimal join-and-broadcast example

```csharp
#nullable enable
using Microsoft.AspNetCore.SignalR;

namespace Crunch.Chat;

public sealed class ChatHub : Hub
{
    public Task JoinRoom(string room)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, room);
    }

    public Task LeaveRoom(string room)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
    }

    public Task SendToRoom(string room, string text)
    {
        string user = Context.User?.Identity?.Name ?? "anonymous";
        return Clients.Group(room).SendAsync("ReceiveMessage", room, user, text);
    }
}
```

Five things are worth naming:

1. **`Groups.AddToGroupAsync` is asynchronous** because in scale-out (Redis backplane) it requires a network round trip to publish the membership to other instances. In single-instance mode it is effectively synchronous but still returns a `Task`.
2. **Group names are arbitrary strings.** Use a stable naming convention — `room:<id>`, `org:<id>:announcements`, `chat:<channelId>` — so you can audit memberships in the Redis backplane later.
3. **A connection can be in any number of groups simultaneously.** The mapping is many-to-many. There is no enforced upper bound; in practice keep the per-connection group count under 50 or measure the cost.
4. **There is no "list members" API.** SignalR does not expose `Groups.GetMembers("room-42")`. The framework owns the membership table and does not let you read it; you can only broadcast to it. If you need to know who is in a room, maintain that state yourself in a singleton service (or, scaled out, in Redis directly).
5. **`Clients.Group(name).SendAsync` is fire-and-forget by design.** It returns when the message has been published to the backplane (or to local connections in single-instance mode); it does *not* wait for every client to acknowledge receipt.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups>.

### 2.2 Groups are not persisted across reconnect — the load-bearing rule

When a connection drops — the WebSocket breaks, the client process crashes, the network partitions — the framework removes the connection from every group it was in. The cleanup is automatic; you do not have to do anything. **But the new connection that comes back (with a fresh `ConnectionId`) is *not* automatically re-added.** That is the client's responsibility.

The canonical pattern is:

```csharp
public sealed class ChatHub : Hub
{
    public override Task OnConnectedAsync()
    {
        // Server side: we do NOT auto-join rooms here, because the server
        // does not know which rooms this user was in. The client must tell us.
        return base.OnConnectedAsync();
    }
}
```

And on the client:

```ts
// Browser side: re-join the user's rooms after every reconnect.
const myRooms = new Set<string>();

connection.onreconnected(async () => {
    for (const room of myRooms) {
        await connection.invoke("JoinRoom", room);
    }
});

async function joinRoom(room: string) {
    await connection.invoke("JoinRoom", room);
    myRooms.add(room);
}
```

Two properties are worth naming:

1. **The client tracks the local room set.** The server cannot, because it has forgotten the old connection.
2. **`onreconnected` is called after a successful reconnect.** The new `ConnectionId` is already in place; calling `JoinRoom` from inside the handler joins the new connection to the room.

This pattern is the load-bearing reason that the mini-project's chat client maintains an in-memory `myRooms` set. We will see it again in Lecture 3 in the context of the full reconnect state machine.

### 2.3 Users — a name above `ConnectionId`

A `User` in SignalR is a higher-level addressing concept built on top of connections. One user may have multiple connections (browser tabs, mobile app, desktop client), and `Clients.User(userId).SendAsync(...)` broadcasts to all of them. The framework maintains the user-to-connections mapping automatically, keyed by the `UserIdentifier` exposed via `Context.UserIdentifier`.

By default, `UserIdentifier` is the value of the `ClaimTypes.NameIdentifier` claim on `Context.User`. For JWT tokens with a `sub` claim (the standard subject claim), the JWT bearer middleware maps `sub` to `NameIdentifier` automatically, so `Context.UserIdentifier == jwt.sub` for free.

For non-standard tokens, or for cases where you want to derive the user identifier from something other than the `sub` claim, implement `IUserIdProvider`:

```csharp
public sealed class EmailUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst("email")?.Value;
}

// In Program.cs:
builder.Services.AddSingleton<IUserIdProvider, EmailUserIdProvider>();
```

Now `Context.UserIdentifier` is the user's email, and `Clients.User("alice@example.com").SendAsync(...)` broadcasts to every connection authenticated as alice.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups#users-in-signalr>.

## 3. Authentication on the WebSocket upgrade — the load-bearing pattern

A normal HTTP request carries `Authorization: Bearer <jwt>` on every call. The middleware sees the header, validates the JWT, and populates `HttpContext.User`. Easy.

A WebSocket upgrade is one HTTP request that becomes a long-lived TCP socket. After the upgrade, there is no second HTTP request and therefore no second `Authorization` header. The server must authenticate the user *on the upgrade request itself*, and after that the connection inherits the identity for the rest of its lifetime.

The browser side has a constraint that compounds the problem: the JS `WebSocket` API does not let you set custom headers on the upgrade. You cannot say `new WebSocket(url, { headers: { Authorization: "Bearer xxx" } })`; the API does not accept it. The only configurable knob is the URL.

**SignalR's solution is to accept the token via the `access_token` query-string parameter on the negotiate request, and configure the JWT bearer middleware to extract it from the query string only on hub URLs.** The two-line client change plus the four-line server change is the entire pattern.

### 3.1 Server side — configuring the JWT bearer middleware

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };

        // The load-bearing piece: pull access_token from the query string
        // when the request path is for a hub.
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
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ChatHub>("/hubs/chat");

app.Run();
```

Five constructs are worth naming:

1. **`AddJwtBearer(options => ...)` configures the standard JWT bearer middleware**, the same one you used in Week 6 for HTTP endpoints. The `TokenValidationParameters` chunk is identical.
2. **The `JwtBearerEvents.OnMessageReceived` hook fires before the middleware tries to read the `Authorization` header.** If we set `context.Token`, the middleware uses that value instead. If we leave `context.Token` null, the middleware falls back to reading the header (the normal HTTP-endpoint behavior).
3. **The path predicate (`path.StartsWithSegments("/hubs")`) is non-optional.** Without it, every endpoint in the app accepts a JWT in the query string, which is a security regression — query strings are logged in proxies and webserver access logs, so a token in the query string is a token in the log. Restrict the query-string acceptance to hub paths only.
4. **The order of middleware matters.** `UseAuthentication()` must come *before* `UseAuthorization()` and *before* `MapHub`. The negotiate request goes through the middleware pipeline like any other request; it needs the middleware in place to validate the token.
5. **`AddAuthorization()` registers the authorization services** that `[Authorize]` will look up. Without it, `[Authorize(Roles = "...")]` fails at runtime.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication>. Source-link for the JWT bearer events: <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/JwtBearer/src/JwtBearerEvents.cs>.

### 3.2 Apply `[Authorize]` to the hub

With the middleware in place, `[Authorize]` works on the hub class or on individual methods:

```csharp
[Authorize]
public sealed class ChatHub : Hub
{
    public Task JoinRoom(string room)
    {
        // Context.User is non-null and authenticated here.
        return Groups.AddToGroupAsync(Context.ConnectionId, room);
    }

    [Authorize(Roles = "moderator")]
    public Task KickUserFromRoom(string room, string userId)
    {
        // Only members of the "moderator" role can call this.
        // ...
        return Task.CompletedTask;
    }
}
```

Two properties are worth naming:

1. **`[Authorize]` at the class level applies to every method** including `OnConnectedAsync`. An unauthenticated connection cannot even establish — the negotiate request fails with a 401.
2. **`[Authorize(Roles = "...")]` or `[Authorize(Policy = "...")]` on individual methods** lets you split a single hub into authenticated and authorized surfaces. The base `[Authorize]` (no roles, no policies) just requires "any authenticated user."

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#authorize-users-to-access-hubs-and-hub-methods>.

### 3.3 Client side — passing the token

```ts
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/chat", {
        accessTokenFactory: () => myToken
    })
    .withAutomaticReconnect()
    .build();
```

Or, for the .NET client:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hubs/chat", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult<string?>(myToken);
    })
    .WithAutomaticReconnect()
    .Build();
```

Three properties are worth naming:

1. **`accessTokenFactory` is a function, not a value.** The SDK calls it on every negotiate request, including reconnect-driven negotiate requests. This is intentional: if your token rotates, the factory hands the SDK the *current* token, not the value the connection was originally built with.
2. **The SDK appends `?access_token=<value>` to the negotiate URL.** You can verify in the Network tab; the query string is visible.
3. **For WebSocket transports, the token is on the negotiate request only.** Once the WebSocket is established, the token is not re-sent; the middleware authenticated the negotiate request, and the negotiated connection inherits that identity. For SSE and long-polling transports, the token is on every request, which is part of why SSE and long polling are slightly more expensive than WebSockets.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication>.

### 3.4 Why the query string and not a cookie?

A common follow-up question is "why not use cookies?" The answer is that cookies work — `SignalR` will use the cookie on the negotiate request and the WebSocket inherits the cookie context — but cookies require either the API and the SignalR endpoint to be on the same origin or full CORS-with-credentials configuration. The query-string token is origin-agnostic and works with token-based auth flows that have no notion of cookies (mobile clients, single-page-app native fetches, server-to-server calls).

The choice is not exclusive; you can use both. Authenticated sessions in a same-origin web app typically use cookies; cross-origin token-based clients typically use the query-string `access_token`. The middleware accepts both.

### 3.5 Token rotation

JWTs are short-lived by design — typical lifetimes are 15-60 minutes. If a SignalR connection lives longer than the token, the connection itself is fine (the middleware authenticated it at connect time; the identity is cached on the connection), but a *reconnect* will re-run negotiate with the same (now-expired) token and fail.

The standard pattern: the `accessTokenFactory` reads from a shared cache that is refreshed by a separate background loop. The cache returns the most recent token. On reconnect, the SDK calls the factory, gets a fresh token, succeeds. The detailed implementation depends on your refresh-token flow; the SignalR side is just "make sure the factory returns a current token."

## 4. The `IUserIdProvider` interface

Mentioned in Section 2.3; here is the full pattern.

`IUserIdProvider` has one method:

```csharp
public interface IUserIdProvider
{
    string? GetUserId(HubConnectionContext connection);
}
```

The default implementation reads `connection.User.FindFirst(ClaimTypes.NameIdentifier)?.Value`. For most JWT-based applications this is correct without modification — the `sub` claim is mapped to `NameIdentifier` by the JWT bearer middleware.

When you would replace it: when your user identity lives in a non-standard claim. A common example is a `"user_id"` claim added by a custom token issuer:

```csharp
public sealed class CustomUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst("user_id")?.Value;
}

// In Program.cs (before AddSignalR):
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();
builder.Services.AddSignalR();
```

The user identifier is what `Clients.User(...)` uses to route. If two browsers are authenticated as the same user-id, both receive the broadcast.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups#users-in-signalr>.

## 5. Strongly-typed hubs — `Hub<TClient>`

The hub class we have written so far uses string method names: `Clients.All.SendAsync("ReceiveMessage", user, text)`. The string is not type-checked at compile time; if you typo it as `"RecieveMessage"`, the bug shows up at runtime when no client handler fires.

A **strongly-typed hub** uses a C# interface to constrain the names and signatures of the methods the server can call on the client:

```csharp
public interface IChatClient
{
    Task ReceiveMessage(string room, string user, string text);
    Task UserJoinedRoom(string room, string user);
    Task UserLeftRoom(string room, string user);
}

public sealed class ChatHub : Hub<IChatClient>
{
    public async Task JoinRoom(string room)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        string user = Context.User?.Identity?.Name ?? "anonymous";
        await Clients.Group(room).UserJoinedRoom(room, user);
    }

    public Task SendToRoom(string room, string text)
    {
        string user = Context.User?.Identity?.Name ?? "anonymous";
        return Clients.Group(room).ReceiveMessage(room, user, text);
    }
}
```

Five properties are worth naming:

1. **`Hub<TClient>` is generic over the client-method interface.** `Clients.All`, `Clients.Group(name)`, `Clients.Caller`, etc., now return objects typed as `TClient` rather than `IClientProxy`. You call interface methods on them directly.
2. **The method name on the wire is the C# method name.** `Clients.Group(room).UserJoinedRoom(...)` sends a message whose `target` is `"UserJoinedRoom"`. The client side still registers `connection.on("UserJoinedRoom", ...)` by string; the discipline is that the string on the client side has to match the C# method name on the server.
3. **The framework generates a dynamic proxy implementing `TClient`.** You do not write the implementation; the framework does. The implementation simply serializes the call into a `SendAsync(methodName, args)` under the hood.
4. **All methods on `TClient` must return `Task`.** They are dispatch-only — the framework cannot wait for the client to acknowledge a fire-and-forget broadcast. If you write `void` or `int` as a return type, the build breaks at startup.
5. **The compile-time check is on the C# side only.** The JS client still uses string names. If you forget to update the client when you rename a method on the interface, the bug shows up at runtime (handler not found). The mitigation is naming discipline plus a small wire-format integration test.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#strongly-typed-hubs>.

### 5.1 When to bother with strongly-typed hubs

Always, for production hubs with more than 3-4 client-side events. The compile-time-checked method names save more debugging than the modest extra ceremony costs. For one-method demos and exploratory hubs, the plain `Hub` is fine.

## 6. The IConnection State for "who is in which room" — a singleton pattern

SignalR does not let you enumerate group members; it only lets you broadcast to them. If your application needs to answer questions like "who is currently in `room-42`?" you maintain the state yourself.

The standard pattern is a singleton service plus the lifecycle hooks:

```csharp
public sealed class RoomTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<string, HashSet<string>> _roomMembers = new();

    public void Join(string room, string userId)
    {
        lock (_lock)
        {
            if (!_roomMembers.TryGetValue(room, out var members))
            {
                members = new HashSet<string>();
                _roomMembers[room] = members;
            }
            members.Add(userId);
        }
    }

    public void Leave(string room, string userId)
    {
        lock (_lock)
        {
            if (_roomMembers.TryGetValue(room, out var members))
            {
                members.Remove(userId);
                if (members.Count == 0) _roomMembers.Remove(room);
            }
        }
    }

    public IReadOnlyList<string> GetMembers(string room)
    {
        lock (_lock)
        {
            return _roomMembers.TryGetValue(room, out var members)
                ? members.ToList()
                : Array.Empty<string>();
        }
    }
}

[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    private readonly RoomTracker _tracker;
    public ChatHub(RoomTracker tracker) => _tracker = tracker;

    public async Task JoinRoom(string room)
    {
        string userId = Context.UserIdentifier ?? Context.ConnectionId;
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        _tracker.Join(room, userId);
        await Clients.Group(room).UserJoinedRoom(room, userId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Best-effort cleanup. We do not know which rooms this connection was in
        // unless we tracked the connection-to-rooms mapping separately. For
        // simplicity here, the application is responsible for calling LeaveRoom
        // before disconnecting. In production, maintain a connection-to-rooms
        // map in Context.Items and clean it up here.
        await base.OnDisconnectedAsync(exception);
    }
}

// In Program.cs:
builder.Services.AddSingleton<RoomTracker>();
```

Three properties are worth naming:

1. **The tracker is a singleton.** Its state lives for the lifetime of the process, which means it is *not* shared across SignalR instances in scale-out. In scale-out, the tracker must be backed by Redis (see Lecture 3).
2. **The tracker is thread-safe.** Multiple hub invocations from different connections can call `Join` and `Leave` concurrently. The simplest implementation uses `lock`; for higher throughput, use `ConcurrentDictionary<TKey, ConcurrentBag<TValue>>`.
3. **Cleanup on `OnDisconnectedAsync` is best-effort.** If the application requires precise membership state on disconnect, maintain the connection-to-rooms mapping in `Context.Items` (or in the tracker keyed by `ConnectionId` rather than `UserId`) and iterate it on disconnect.

This is the pattern the mini-project's `RoomTracker` follows.

## 7. The user-aware broadcast — `Clients.User(userId)`

With JWT auth and a configured `IUserIdProvider` in place, `Clients.User("alice@example.com").SendAsync(...)` broadcasts to every connection authenticated as alice. This is the right shape for a single-user notification — "you have a new message" — that should reach the user wherever they are connected (browser, mobile app, desktop client) without the application code having to enumerate connections.

The framework keeps the user-to-connections mapping internally; you do not maintain it. The mapping is per-instance in single-instance mode and global-across-instances in Redis-backplane mode.

```csharp
public sealed class NotificationsHub : Hub<INotificationsClient>
{
    public Task SendDirectMessage(string toUserId, string text)
    {
        // Send to every connection authenticated as `toUserId`. If they have
        // three tabs open, all three get the message.
        return Clients.User(toUserId).ReceiveDirectMessage(
            Context.UserIdentifier ?? "anonymous", text);
    }
}
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#send-messages-to-a-specific-user>.

## 8. Authorization policies on hub methods

For finer-grained authorization than "any authenticated user," use authorization policies. A policy is a named bundle of `IAuthorizationRequirement`s registered in `AddAuthorization`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ChatModerator", policy =>
        policy.RequireRole("moderator")
              .RequireClaim("scope", "chat:moderate"));
});

[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    [Authorize(Policy = "ChatModerator")]
    public Task KickUserFromRoom(string room, string userId)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

The policy is evaluated when the method is invoked, against `Context.User`. If it fails, the framework throws `HubException` which the client sees as an invocation failure. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#use-authorization-handlers-to-customize-hub-method-authorization>.

## 9. The `HubException` class — controlled error surfacing

By default, exceptions thrown from a hub method are *not* surfaced to the client; the client sees a generic "Invocation failed" without the exception message. This is by design — exception messages can leak internal state. To surface a controlled error message, throw `HubException`:

```csharp
public async Task JoinRoom(string room)
{
    if (string.IsNullOrWhiteSpace(room) || room.Length > 64)
    {
        throw new HubException("Room name must be 1-64 characters.");
    }
    await Groups.AddToGroupAsync(Context.ConnectionId, room);
}
```

The `HubException` message reaches the client verbatim and shows up in the rejected promise of the corresponding `invoke()` call. Use it for user-presentable validation errors. Do not put internal state (database IDs, stack frames) in the message; an attacker who can call the method can read the message.

For diagnostic-level errors that should be logged but not surfaced, throw a regular exception and let the framework log it server-side. To opt into surfacing all exception details (development only), set `options.EnableDetailedErrors = true` in `AddSignalR`:

```csharp
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/configuration#configure-server-options>.

## 10. Logging — the SignalR side of the SQL-log discipline

Week 10 told you to read every SQL statement EF emits. Week 11 tells you to read every hub log line.

The relevant logger categories are:

- `Microsoft.AspNetCore.SignalR` — hub-method invocations.
- `Microsoft.AspNetCore.SignalR.HubConnectionHandler` — per-connection lifecycle.
- `Microsoft.AspNetCore.Http.Connections` — the transport layer.

In `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "Microsoft.AspNetCore.Http.Connections": "Debug"
    }
  }
}
```

You will now see, per request, the negotiate handshake, the protocol selection, the hub-method dispatch, the broadcast targets, and the disconnect reason. **Read the log in development for every PR.** The bytes do not lie; the log is the bytes interpreted for you.

Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/diagnostics>.

## 11. Putting it together — a chat hub with rooms and auth

```csharp
#nullable enable
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Crunch.Chat;

public interface IChatClient
{
    Task ReceiveMessage(string room, string user, string text);
    Task UserJoinedRoom(string room, string user);
    Task UserLeftRoom(string room, string user);
    Task Welcome(string connectionId, string user);
}

[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    private readonly RoomTracker _tracker;
    public ChatHub(RoomTracker tracker) => _tracker = tracker;

    public override async Task OnConnectedAsync()
    {
        string user = Context.UserIdentifier ?? "anonymous";
        await Clients.Caller.Welcome(Context.ConnectionId, user);
        await base.OnConnectedAsync();
    }

    public async Task JoinRoom(string room)
    {
        if (string.IsNullOrWhiteSpace(room) || room.Length > 64)
            throw new HubException("Room name must be 1-64 characters.");

        string user = Context.UserIdentifier ?? "anonymous";
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        _tracker.Join(room, user);
        await Clients.Group(room).UserJoinedRoom(room, user);
    }

    public async Task LeaveRoom(string room)
    {
        string user = Context.UserIdentifier ?? "anonymous";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        _tracker.Leave(room, user);
        await Clients.Group(room).UserLeftRoom(room, user);
    }

    public Task SendToRoom(string room, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
            throw new HubException("Message must be 1-4096 characters.");

        string user = Context.UserIdentifier ?? "anonymous";
        return Clients.Group(room).ReceiveMessage(room, user, text);
    }
}
```

This is the hub the mini-project builds on. It is intentionally small (about 50 lines) and demonstrates every concept from this lecture: strongly-typed `Hub<IChatClient>`, `[Authorize]` at the class level, the `JoinRoom` / `LeaveRoom` / `SendToRoom` triplet using groups, the `HubException` for validation errors, the `OnConnectedAsync` welcome message, and the singleton `RoomTracker`.

## 12. Exercise pointer

Now do **Exercise 2 — Groups and JWT**. Add JWT bearer authentication to the hub from Exercise 1, configure `OnMessageReceived` to extract the token from the query string on `/hubs/*` paths only, and verify that an unauthenticated connection fails the negotiate with `401 Unauthorized` while an authenticated connection succeeds. Build the `JoinRoom` / `LeaveRoom` / `SendToRoom` triplet, connect from two browser tabs as two different users, and verify that messages to `room:A` reach the user in `room:A` only.

## 13. What we did not cover (yet)

- **Scale-out with Redis.** Lecture 3 covers the backplane and what the cross-instance broadcast looks like on the wire.
- **The MessagePack protocol.** Lecture 3 covers the swap and the byte-savings measurement.
- **Reconnection.** Lecture 3 covers the state machine and the state-on-reconnect pattern.
- **Streaming.** Lecture 3 covers `IAsyncEnumerable<T>` hub methods.

## 14. Summary

- A **group** is a server-side bag of `ConnectionId`s, addressable with `Clients.Group(name)`. Memberships are not persisted across reconnect; the client must re-join.
- A **user** is a higher-level identity built on top of connections; `Clients.User(userId)` broadcasts to every connection authenticated as that user.
- `IUserIdProvider` controls which claim becomes `Context.UserIdentifier`. Default is `ClaimTypes.NameIdentifier` (the `sub` claim for JWTs).
- **JWT on the WebSocket upgrade** is configured by the `OnMessageReceived` hook on `JwtBearerEvents`, gated on `Path.StartsWithSegments("/hubs")`.
- The client passes the token via `accessTokenFactory: () => token` (JS) or `options.AccessTokenProvider` (.NET).
- `[Authorize]` works on hubs and on individual hub methods. Combine with policies for finer-grained authorization.
- **Strongly-typed hubs** (`Hub<TClient>`) move the client-method name check from runtime to compile time. Use them for every production hub.
- **Singleton trackers** (`RoomTracker`, etc.) are the right pattern for "who is in which room" state that the framework does not expose. Back them with Redis in scale-out.
- Throw `HubException` for user-presentable validation errors; let regular exceptions log server-side.
- Turn `Microsoft.AspNetCore.SignalR` logging up to `Debug` in development. Read the log.

Cited Microsoft Learn pages this lecture pulled from: <https://learn.microsoft.com/en-us/aspnet/core/signalr/groups>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/diagnostics>. Source-link references: `Hub.cs`, `HubCallerContext.cs`, `JwtBearerEvents.cs` in `dotnet/aspnetcore`.
