// Crunch.Chat / src/Server / ChatHub.cs
//
// The hub class for the mini-project. The method bodies are stubs — your
// job is to fill them in, using the patterns from Lectures 1-3 and the
// exercises. Every stub has a TODO comment with a hint and a citation.
//
// Citations:
//   Hubs:           https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs
//   Groups:         https://learn.microsoft.com/en-us/aspnet/core/signalr/groups
//   Auth on hubs:   https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz
//   Strongly typed: https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs#strongly-typed-hubs
//   Streaming:      https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming

#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Crunch.Chat;

// Strongly-typed client interface. The C# method names are the wire-format
// "target" strings; the JS client registers handlers by these names.
public interface IChatClient
{
    Task ReceiveMessage(string room, string user, string text, long id, long timestamp);
    Task UserJoinedRoom(string room, string user);
    Task UserLeftRoom(string room, string user);
    Task Welcome(string connectionId, string user);
}

[Authorize]
public sealed class ChatHub : Hub<IChatClient>
{
    private readonly RoomTracker  _tracker;
    private readonly DedupeCache  _dedupe;
    private readonly MessageStore _store;
    private readonly PerfMeasurer _perf;
    private readonly ILogger<ChatHub> _log;

    public ChatHub(
        RoomTracker  tracker,
        DedupeCache  dedupe,
        MessageStore store,
        PerfMeasurer perf,
        ILogger<ChatHub> log)
    {
        _tracker = tracker;
        _dedupe  = dedupe;
        _store   = store;
        _perf    = perf;
        _log     = log;
    }

    // --- Lifecycle hooks ---------------------------------------------------

    public override async Task OnConnectedAsync()
    {
        string user = Context.UserIdentifier ?? "anonymous";
        _log.LogInformation("connected: connId={ConnId} user={User}",
            Context.ConnectionId, user);
        _perf.OnConnected();

        // Send a welcome message back to just this connection.
        await Clients.Caller.Welcome(Context.ConnectionId, user);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string user = Context.UserIdentifier ?? "anonymous";
        _log.LogInformation("disconnected: connId={ConnId} user={User} reason={Reason}",
            Context.ConnectionId, user, exception?.Message ?? "clean");
        _perf.OnDisconnected();

        // Best-effort cleanup: leave every room this connection had joined.
        // We stash the per-connection rooms in Context.Items from JoinRoom.
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

    // --- Hub methods (callable from clients) -------------------------------

    public async Task JoinRoom(string room)
    {
        // TODO: validate room name (1-64 characters; HubException on failure).
        // TODO: Groups.AddToGroupAsync, RoomTracker.Join.
        // TODO: stash the room in Context.Items["rooms"] for cleanup.
        // TODO: broadcast UserJoinedRoom to the group.
        // Citation: https://learn.microsoft.com/en-us/aspnet/core/signalr/groups
        if (string.IsNullOrWhiteSpace(room) || room.Length > 64)
        {
            throw new HubException("Room name must be 1-64 characters.");
        }

        string user = Context.UserIdentifier ?? "anonymous";

        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        _tracker.Join(room, user);

        if (!Context.Items.TryGetValue("rooms", out var roomsObj) ||
            roomsObj is not HashSet<string> rooms)
        {
            rooms = new HashSet<string>();
            Context.Items["rooms"] = rooms;
        }
        rooms.Add(room);

        await Clients.Group(room).UserJoinedRoom(room, user);
    }

    public async Task LeaveRoom(string room)
    {
        // TODO: symmetric with JoinRoom; remove from group, tracker, items;
        //       broadcast UserLeftRoom.
        string user = Context.UserIdentifier ?? "anonymous";

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
        _tracker.Leave(room, user);

        if (Context.Items.TryGetValue("rooms", out var roomsObj) &&
            roomsObj is HashSet<string> rooms)
        {
            rooms.Remove(room);
        }

        await Clients.Group(room).UserLeftRoom(room, user);
    }

    public async Task SendToRoomIdempotent(
        string room, string text, string clientMessageId)
    {
        // TODO: validate text (1-4096 characters).
        // TODO: dedupe by clientMessageId; drop silently if duplicate.
        // TODO: persist via MessageStore.Append to get a server-assigned id.
        // TODO: broadcast ReceiveMessage to the group.
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            throw new HubException("Message must be 1-4096 characters.");
        }

        string user = Context.UserIdentifier ?? "anonymous";

        if (!_dedupe.TryRegister(user, clientMessageId))
        {
            // Already saw this clientMessageId from this user; the original
            // broadcast already happened. Drop silently.
            return;
        }

        var entry = await _store.AppendAsync(room, user, text, clientMessageId);
        _perf.OnMessageBroadcast();

        await Clients.Group(room).ReceiveMessage(
            room, user, text, entry.Id, entry.Timestamp);
    }

    public Task<IReadOnlyList<LogEntry>> FetchSince(string room, long sinceId)
    {
        // TODO: read from the durable store. Bound the result size (e.g. 500
        //       rows) to avoid a runaway query on first-load.
        return _store.FetchSinceAsync(room, sinceId, maxRows: 500);
    }

    public async IAsyncEnumerable<LogEntry> StreamHistory(
        string room,
        long sinceId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: stream entries one at a time. The [EnumeratorCancellation]
        //       attribute wires the client's cancellation into our token.
        // Citation: https://learn.microsoft.com/en-us/aspnet/core/signalr/streaming
        await foreach (var entry in _store.StreamSinceAsync(room, sinceId, cancellationToken))
        {
            yield return entry;
        }
    }

    public IReadOnlyList<string> ListRoomMembers(string room)
    {
        // Read-only query into the singleton tracker.
        return _tracker.GetMembers(room);
    }
}
