// Exercise 2 — Groups (Rooms) and JWT on the WebSocket Upgrade.
//
// Goal: extend Ex01 with JWT bearer authentication, configure the
// OnMessageReceived hook to extract the access_token query-string parameter
// for /hubs/* paths only, add JoinRoom / LeaveRoom / SendToRoom hub methods
// that use groups, and verify that:
//   (a) unauthenticated negotiate returns 401,
//   (b) authenticated negotiate succeeds,
//   (c) messages to room:A reach connections in room:A and nowhere else.
//
// Project layout:
//
//   src/Ex02.Server/
//     Ex02.Server.csproj
//     Program.cs
//     ChatHub.cs                 <-- this file
//     TokenIssuer.cs             <-- a tiny dev-only JWT issuer endpoint
//     appsettings.Development.json
//
// .csproj contents:
//
//   <Project Sdk="Microsoft.NET.Sdk.Web">
//     <PropertyGroup>
//       <TargetFramework>net8.0</TargetFramework>
//       <Nullable>enable</Nullable>
//       <ImplicitUsings>enable</ImplicitUsings>
//     </PropertyGroup>
//     <ItemGroup>
//       <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
//     </ItemGroup>
//   </Project>
//
// appsettings.Development.json:
//
//   {
//     "Jwt": {
//       "Issuer":   "crunch-chat-dev",
//       "Audience": "crunch-chat-dev",
//       "Key":      "DEV-ONLY-CHANGE-IN-PRODUCTION-must-be-32-chars-or-more-DEV"
//     },
//     "Logging": {
//       "LogLevel": {
//         "Default": "Information",
//         "Microsoft.AspNetCore.SignalR":            "Debug",
//         "Microsoft.AspNetCore.Http.Connections":   "Debug"
//       }
//     }
//   }

// ============================================================================
// PART 1 — ChatHub.cs (paste into its own file in the project)
// ============================================================================

#nullable enable
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ex02.Server;

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
    private readonly ILogger<ChatHub> _log;

    public ChatHub(RoomTracker tracker, ILogger<ChatHub> log)
    {
        _tracker = tracker;
        _log = log;
    }

    public override async Task OnConnectedAsync()
    {
        string user = Context.UserIdentifier ?? "anonymous";
        _log.LogInformation("connected: connId={ConnId} user={User}",
            Context.ConnectionId, user);
        await Clients.Caller.Welcome(Context.ConnectionId, user);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string user = Context.UserIdentifier ?? "anonymous";
        _log.LogInformation("disconnected: connId={ConnId} user={User} ex={Ex}",
            Context.ConnectionId, user, exception?.Message ?? "clean");

        // Best-effort cleanup: leave every room this connection had joined.
        // We tracked the per-connection set in Context.Items in JoinRoom.
        if (Context.Items.TryGetValue("rooms", out var roomsObj) &&
            roomsObj is HashSet<string> rooms)
        {
            foreach (var room in rooms)
            {
                _tracker.Leave(room, user);
                await Clients.Group(room).UserLeftRoom(room, user);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinRoom(string room)
    {
        if (string.IsNullOrWhiteSpace(room) || room.Length > 64)
        {
            throw new HubException("Room name must be 1-64 characters.");
        }

        string user = Context.UserIdentifier ?? "anonymous";

        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        _tracker.Join(room, user);

        // Remember this room on the connection for cleanup on disconnect.
        if (!Context.Items.TryGetValue("rooms", out var roomsObj) ||
            roomsObj is not HashSet<string> rooms)
        {
            rooms = new HashSet<string>();
            Context.Items["rooms"] = rooms;
        }
        rooms.Add(room);

        await Clients.Group(room).UserJoinedRoom(room, user);
        _log.LogInformation("join: connId={ConnId} user={User} room={Room}",
            Context.ConnectionId, user, room);
    }

    public async Task LeaveRoom(string room)
    {
        string user = Context.UserIdentifier ?? "anonymous";

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        _tracker.Leave(room, user);

        if (Context.Items.TryGetValue("rooms", out var roomsObj) &&
            roomsObj is HashSet<string> rooms)
        {
            rooms.Remove(room);
        }

        await Clients.Group(room).UserLeftRoom(room, user);
        _log.LogInformation("leave: connId={ConnId} user={User} room={Room}",
            Context.ConnectionId, user, room);
    }

    public Task SendToRoom(string room, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            throw new HubException("Message must be 1-4096 characters.");
        }

        string user = Context.UserIdentifier ?? "anonymous";
        return Clients.Group(room).ReceiveMessage(room, user, text);
    }

    public IReadOnlyList<string> ListRoomMembers(string room)
    {
        // Server-side query into the singleton tracker. Returns the list of
        // user-ids currently in the room.
        return _tracker.GetMembers(room);
    }
}

// ============================================================================
// PART 2 — RoomTracker.cs (paste into its own file in the project)
// ============================================================================
//
// using System.Collections.Concurrent;
//
// namespace Ex02.Server;
//
// public sealed class RoomTracker
// {
//     private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _roomMembers = new();
//
//     public void Join(string room, string userId)
//     {
//         var bag = _roomMembers.GetOrAdd(room, _ => new ConcurrentDictionary<string, byte>());
//         bag[userId] = 0;
//     }
//
//     public void Leave(string room, string userId)
//     {
//         if (_roomMembers.TryGetValue(room, out var bag))
//         {
//             bag.TryRemove(userId, out _);
//             if (bag.IsEmpty) _roomMembers.TryRemove(room, out _);
//         }
//     }
//
//     public IReadOnlyList<string> GetMembers(string room)
//         => _roomMembers.TryGetValue(room, out var bag)
//             ? bag.Keys.ToList()
//             : Array.Empty<string>();
// }

// ============================================================================
// PART 3 — Program.cs (replace the default Program.cs with this content)
// ============================================================================
//
// using System.Text;
// using Ex02.Server;
// using Microsoft.AspNetCore.Authentication.JwtBearer;
// using Microsoft.IdentityModel.Tokens;
//
// var builder = WebApplication.CreateBuilder(args);
//
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddJwtBearer(options =>
//     {
//         options.TokenValidationParameters = new TokenValidationParameters
//         {
//             ValidateIssuer           = true,
//             ValidateAudience         = true,
//             ValidateLifetime         = true,
//             ValidateIssuerSigningKey = true,
//             ValidIssuer              = builder.Configuration["Jwt:Issuer"],
//             ValidAudience            = builder.Configuration["Jwt:Audience"],
//             IssuerSigningKey         = new SymmetricSecurityKey(
//                 Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
//         };
//
//         // The load-bearing piece: pull access_token from the query string
//         // when (and ONLY when) the request path is for a hub.
//         options.Events = new JwtBearerEvents
//         {
//             OnMessageReceived = context =>
//             {
//                 var accessToken = context.Request.Query["access_token"];
//                 var path = context.HttpContext.Request.Path;
//                 if (!string.IsNullOrEmpty(accessToken) &&
//                     path.StartsWithSegments("/hubs"))
//                 {
//                     context.Token = accessToken;
//                 }
//                 return Task.CompletedTask;
//             }
//         };
//     });
//
// builder.Services.AddAuthorization();
// builder.Services.AddSingleton<RoomTracker>();
// builder.Services.AddSignalR(o =>
// {
//     o.EnableDetailedErrors = builder.Environment.IsDevelopment();
// });
//
// builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
//     p.WithOrigins("http://localhost:5173")
//      .AllowAnyHeader()
//      .AllowAnyMethod()
//      .AllowCredentials()));
//
// var app = builder.Build();
//
// app.UseCors();
// app.UseAuthentication();
// app.UseAuthorization();
//
// // A dev-only token issuer. In production, this lives in your auth service.
// app.MapPost("/dev/token", (string user, IConfiguration cfg) =>
// {
//     return Results.Ok(new { token = TokenIssuer.Issue(user, cfg) });
// });
//
// app.MapHub<ChatHub>("/hubs/chat");
//
// app.Run();

// ============================================================================
// PART 4 — TokenIssuer.cs (dev-only JWT minting; paste into its own file)
// ============================================================================
//
// using System.IdentityModel.Tokens.Jwt;
// using System.Security.Claims;
// using System.Text;
// using Microsoft.IdentityModel.Tokens;
//
// namespace Ex02.Server;
//
// public static class TokenIssuer
// {
//     public static string Issue(string user, IConfiguration cfg)
//     {
//         var key = new SymmetricSecurityKey(
//             Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
//         var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
//         var token = new JwtSecurityToken(
//             issuer:   cfg["Jwt:Issuer"],
//             audience: cfg["Jwt:Audience"],
//             claims: new[]
//             {
//                 new Claim(JwtRegisteredClaimNames.Sub, user),
//                 new Claim(ClaimTypes.NameIdentifier,   user),
//                 new Claim(ClaimTypes.Name,             user)
//             },
//             expires: DateTime.UtcNow.AddHours(1),
//             signingCredentials: creds);
//         return new JwtSecurityTokenHandler().WriteToken(token);
//     }
// }

// ============================================================================
// COMMANDS — what you actually run to exercise this
// ============================================================================
//
//   dotnet run --project src/Ex02.Server
//
//   # In another terminal: try negotiate WITHOUT a token. Expect 401.
//   curl -i -X POST -H "Content-Length: 0" \
//        http://localhost:5000/hubs/chat/negotiate?negotiateVersion=1
//   # HTTP/1.1 401 Unauthorized
//
//   # Mint a token, then negotiate WITH it. Expect 200.
//   TOKEN=$(curl -s -X POST "http://localhost:5000/dev/token?user=alice" | jq -r .token)
//   curl -i -X POST -H "Content-Length: 0" \
//        "http://localhost:5000/hubs/chat/negotiate?negotiateVersion=1&access_token=$TOKEN"
//   # HTTP/1.1 200 OK
//
//   # Open two browser tabs (alice and bob), both connected with their own
//   # token, both joined to room "general". Send from alice; bob sees it.
//   # Have bob join "bob-only" and send to bob-only; alice does NOT see it.

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] Negotiate without a token returns 401 Unauthorized.
//
//   [ ] Negotiate with a valid token returns 200 OK with the transport list.
//
//   [ ] Negotiate with an EXPIRED token returns 401 Unauthorized.
//
//   [ ] Negotiate with a token signed by a DIFFERENT key returns 401.
//
//   [ ] After OnConnectedAsync, Context.UserIdentifier equals the `sub` claim
//      from the JWT (verify by logging it in the hub).
//
//   [ ] Two connections in the same room both receive SendToRoom broadcasts.
//      A third connection NOT in the room receives nothing.
//
//   [ ] Calling JoinRoom("") throws HubException; the client sees the
//      message "Room name must be 1-64 characters."
//
//   [ ] On clean disconnect, OnDisconnectedAsync fires with `exception=null`.
//      The RoomTracker's GetMembers reflects the user gone.
//
// Stretch (counted toward Exercise 2 if you finish the above with time left):
//   1. Add a method GetRoomMembers(string room) decorated with
//      [Authorize(Policy = "ChatModerator")]. Configure a policy that
//      requires a "role" claim equal to "moderator". Mint two tokens — one
//      with the role, one without — and verify the unauthorized call fails
//      with HubException.
//   2. Configure an IUserIdProvider that uses the "email" claim instead of
//      sub. Mint a token with an email claim; verify Context.UserIdentifier
//      matches the email.
