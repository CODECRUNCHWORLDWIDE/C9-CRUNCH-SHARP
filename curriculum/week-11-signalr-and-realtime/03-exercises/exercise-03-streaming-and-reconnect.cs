// Exercise 3 — Streaming with IAsyncEnumerable, Reconnection, and Cancellation.
//
// Goal: add a streaming hub method to the chat hub, consume it from the .NET
// and JS clients, and verify the items arrive progressively. Then simulate a
// network interruption mid-stream and verify the stream terminates with an
// error rather than hanging. Finally, implement the state-on-reconnect
// pattern in the .NET client (re-join rooms, re-fetch missed messages).
//
// Project layout (continues from Ex02):
//
//   src/Ex03.Server/
//     ... (same as Ex02.Server)
//     ChatHub.cs                 <-- extended with StreamLogs + Fetch (this file)
//     MessageStore.cs            <-- in-memory durable store
//   src/Ex03.Client/
//     Ex03.Client.csproj
//     Program.cs                 <-- the .NET client with reconnect (this file)

// ============================================================================
// PART 1 — ChatHub.cs additions (the streaming + fetch methods)
// ============================================================================

#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ex03.Server;

// Reused from Ex02: IChatClient interface, RoomTracker singleton, [Authorize].
// We extend ChatHub here with the streaming method and a fetch-since method.

[Authorize]
public sealed partial class ChatHub : Hub<IChatClient>
{
    // (Constructor and methods from Ex02 elided for brevity; only the
    //  additions for this exercise are shown.)

    // 1. STREAMING: yield log entries to a client one at a time.
    //
    // The return type IAsyncEnumerable<LogEntry> tells the framework this is
    // a streaming method, not a one-shot invocation. The framework will
    // dispatch each yield as a StreamItem envelope and emit a StreamCompletion
    // envelope when the iteration ends. Cancellation flows through the
    // [EnumeratorCancellation] token; when the client cancels (or its
    // transport drops mid-stream), the token fires and our `await foreach`
    // breaks cleanly.
    public async IAsyncEnumerable<LogEntry> StreamLogs(
        string room,
        int sinceId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // _store is the singleton MessageStore injected by DI.
        await foreach (var entry in _store.ReadSince(room, sinceId, cancellationToken))
        {
            yield return entry;
        }
    }

    // 2. FETCH-SINCE: one-shot return of all messages with id > sinceId in a
    // room. Used by the client on reconnect to fill the gap that the
    // disconnect created. Idempotent: the same sinceId returns the same list.
    public async Task<IReadOnlyList<LogEntry>> FetchSince(string room, int sinceId)
    {
        var list = new List<LogEntry>();
        await foreach (var entry in _store.ReadSince(room, sinceId, default))
        {
            list.Add(entry);
        }
        return list;
    }

    // 3. SENDTOROOM with idempotency key (clientMessageId). The client
    // generates a UUID for every send; the server keeps a small recent-id
    // cache per user and drops duplicates. This is what makes replay on
    // reconnect safe — the client can resend the same message and the
    // server will not duplicate it.
    public async Task SendToRoomIdempotent(
        string room, string text, string clientMessageId)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        {
            throw new HubException("Message must be 1-4096 characters.");
        }

        string user = Context.UserIdentifier ?? "anonymous";

        if (!_dedupe.TryRegister(user, clientMessageId))
        {
            // Already saw this id. Drop silently; the original broadcast
            // already happened.
            return;
        }

        var entry = await _store.Append(room, user, text, clientMessageId);
        await Clients.Group(room).ReceiveMessage(room, user, text);
    }

    // Constructor and injected fields — declared once for the whole partial.
    private readonly MessageStore _store;
    private readonly DedupeCache _dedupe;
    public ChatHub(MessageStore store, DedupeCache dedupe,
                   RoomTracker tracker, ILogger<ChatHub> log)
    {
        _store = store;
        _dedupe = dedupe;
        _tracker = tracker;
        _log = log;
    }

    private readonly RoomTracker _tracker;
    private readonly ILogger<ChatHub> _log;
}

public sealed record LogEntry(int Id, string Room, string User, string Text, long Timestamp);

// ============================================================================
// PART 2 — MessageStore.cs (singleton, in-memory; back with Postgres in prod)
// ============================================================================
//
// using System.Collections.Concurrent;
// using System.Threading.Channels;
//
// namespace Ex03.Server;
//
// public sealed class MessageStore
// {
//     private int _nextId;
//     private readonly ConcurrentBag<LogEntry> _all = new();
//     // For pub/sub of new entries to in-flight streams in this instance.
//     // Cross-instance pub/sub goes through the Redis backplane in the
//     // mini-project; here we keep it in-process for simplicity.
//     private readonly Channel<LogEntry> _bus = Channel.CreateUnbounded<LogEntry>();
//
//     public async Task<LogEntry> Append(string room, string user, string text, string _ignoredClientId)
//     {
//         int id = Interlocked.Increment(ref _nextId);
//         var entry = new LogEntry(id, room, user, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
//         _all.Add(entry);
//         await _bus.Writer.WriteAsync(entry);
//         return entry;
//     }
//
//     public async IAsyncEnumerable<LogEntry> ReadSince(
//         string room,
//         int sinceId,
//         [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
//     {
//         // Yield everything we have so far.
//         foreach (var entry in _all.Where(e => e.Room == room && e.Id > sinceId).OrderBy(e => e.Id))
//         {
//             ct.ThrowIfCancellationRequested();
//             yield return entry;
//         }
//         // Then yield further entries as they arrive, up to cancellation.
//         await foreach (var entry in _bus.Reader.ReadAllAsync(ct))
//         {
//             if (entry.Room == room && entry.Id > sinceId)
//             {
//                 yield return entry;
//             }
//         }
//     }
// }
//
// public sealed class DedupeCache
// {
//     // user-id -> ring buffer of recent client-message-ids.
//     // 1024 entries per user is enough to catch replays within a typical
//     // disconnect window without growing unboundedly.
//     private const int Capacity = 1024;
//     private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Ring> _byUser = new();
//
//     public bool TryRegister(string user, string clientMessageId)
//     {
//         var ring = _byUser.GetOrAdd(user, _ => new Ring(Capacity));
//         return ring.AddIfMissing(clientMessageId);
//     }
//
//     private sealed class Ring
//     {
//         private readonly HashSet<string> _set;
//         private readonly Queue<string> _queue;
//         private readonly int _cap;
//         private readonly object _lock = new();
//         public Ring(int cap) { _cap = cap; _set = new(); _queue = new(); }
//         public bool AddIfMissing(string id)
//         {
//             lock (_lock)
//             {
//                 if (!_set.Add(id)) return false;
//                 _queue.Enqueue(id);
//                 if (_queue.Count > _cap)
//                 {
//                     _set.Remove(_queue.Dequeue());
//                 }
//                 return true;
//             }
//         }
//     }
// }

// ============================================================================
// PART 3 — Ex03.Client/Program.cs (the .NET client with full reconnect)
// ============================================================================
//
// using System.Collections.Concurrent;
// using Microsoft.AspNetCore.SignalR.Client;
//
// var token = await MintToken("alice");  // call /dev/token from Ex02
//
// var myRooms = new HashSet<string> { "general" };
// var outbound = new ConcurrentQueue<(string Room, string Text, string ClientMsgId)>();
// int lastSeenId = 0;
//
// var connection = new HubConnectionBuilder()
//     .WithUrl("http://localhost:5000/hubs/chat", o =>
//     {
//         o.AccessTokenProvider = () => Task.FromResult<string?>(token);
//     })
//     .WithAutomaticReconnect(new[]
//     {
//         TimeSpan.Zero,
//         TimeSpan.FromSeconds(2),
//         TimeSpan.FromSeconds(5),
//         TimeSpan.FromSeconds(10),
//         TimeSpan.FromSeconds(30)
//     })
//     .ConfigureLogging(b => b.AddConsole())
//     .Build();
//
// connection.On<string, string, string>("ReceiveMessage", (room, user, text) =>
// {
//     Console.WriteLine($"[{room}] <{user}> {text}");
// });
//
// connection.Reconnecting += err =>
// {
//     Console.WriteLine($"[reconnecting] {err?.Message}");
//     return Task.CompletedTask;
// };
//
// connection.Reconnected += async newId =>
// {
//     Console.WriteLine($"[reconnected]  new connId={newId}");
//
//     // Step 1: re-join rooms.
//     foreach (var room in myRooms)
//     {
//         await connection.InvokeAsync("JoinRoom", room);
//     }
//
//     // Step 2: refetch missed messages per room.
//     foreach (var room in myRooms)
//     {
//         var missed = await connection.InvokeAsync<IReadOnlyList<LogEntry>>(
//             "FetchSince", room, lastSeenId);
//         foreach (var m in missed)
//         {
//             Console.WriteLine($"[{m.Room}] (replay) <{m.User}> {m.Text}");
//             if (m.Id > lastSeenId) lastSeenId = m.Id;
//         }
//     }
//
//     // Step 3: replay buffered outbound.
//     while (outbound.TryDequeue(out var pending))
//     {
//         await connection.InvokeAsync("SendToRoomIdempotent",
//             pending.Room, pending.Text, pending.ClientMsgId);
//     }
// };
//
// connection.Closed += err =>
// {
//     Console.WriteLine($"[closed] {err?.Message ?? "clean"}");
//     return Task.CompletedTask;
// };
//
// await connection.StartAsync();
// await connection.InvokeAsync("JoinRoom", "general");
//
// // Consume a server-side stream.
// var cts = new CancellationTokenSource();
// var streamTask = ConsumeStream(connection, "general", lastSeenId, cts.Token);
//
// // Drive an interactive prompt.
// while (true)
// {
//     Console.Write("> ");
//     var input = Console.ReadLine();
//     if (input is null or "quit") break;
//
//     string clientMsgId = Guid.NewGuid().ToString();
//
//     if (connection.State != HubConnectionState.Connected)
//     {
//         Console.WriteLine("  (offline; buffering)");
//         outbound.Enqueue(("general", input, clientMsgId));
//         continue;
//     }
//
//     try
//     {
//         await connection.InvokeAsync(
//             "SendToRoomIdempotent", "general", input, clientMsgId);
//     }
//     catch (Exception ex)
//     {
//         Console.WriteLine($"  (send failed: {ex.Message}; buffering)");
//         outbound.Enqueue(("general", input, clientMsgId));
//     }
// }
//
// cts.Cancel();
// try { await streamTask; } catch { /* expected cancellation */ }
// await connection.StopAsync();
//
// static async Task ConsumeStream(HubConnection c, string room, int since, CancellationToken ct)
// {
//     try
//     {
//         await foreach (var entry in c.StreamAsync<LogEntry>("StreamLogs", room, since, ct))
//         {
//             Console.WriteLine($"  [stream] {entry.Id}: <{entry.User}> {entry.Text}");
//         }
//     }
//     catch (OperationCanceledException) { /* normal */ }
// }
//
// record LogEntry(int Id, string Room, string User, string Text, long Timestamp);
//
// async Task<string> MintToken(string user)
// {
//     using var http = new HttpClient();
//     var resp = await http.PostAsync(
//         $"http://localhost:5000/dev/token?user={user}", null);
//     var json = await resp.Content.ReadAsStringAsync();
//     return System.Text.Json.JsonDocument.Parse(json).RootElement
//         .GetProperty("token").GetString()!;
// }

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] connection.StartAsync() completes. You see [reconnected]-style
//       output only AFTER you simulate a disconnect (see next step).
//
//   [ ] Simulate a network interruption while the stream is in flight:
//       (a) kill the server with Ctrl+C, wait 3s, restart it. The client
//           transitions Reconnecting -> Reconnected; the Reconnected handler
//           runs the rejoin+refetch+replay sequence. The stream tasks
//           throws OperationCanceledException; the new StreamAsync call
//           (you can wire it into Reconnected) resumes from lastSeenId.
//       (b) Alternatively, briefly toggle wifi off and on. Same flow.
//
//   [ ] If you type messages while the connection is in Reconnecting state,
//       they queue in `outbound` and are replayed in order on reconnect.
//
//   [ ] If you replay the same clientMessageId twice (modify the loop to
//       always reuse a fixed Guid for one second), the server's DedupeCache
//       drops the second one. Verify with a count of broadcast events.
//
//   [ ] After 30 + 10 + 5 + 2 + 0 = 47s with the server down, the client
//       transitions Reconnecting -> Closed. The Closed handler fires. Manual
//       restart is required.
//
// Stretch (counted toward Exercise 3 if you finish the above with time left):
//   1. Implement an `IReconnectPolicy` (custom retry policy) that retries
//      forever with exponential backoff capped at 60s. Verify the connection
//      survives an outage longer than the default 47s window.
//   2. Add a CancellationToken parameter to ConsumeStream and pass a
//      CancellationTokenSource you can trigger from a keyboard shortcut.
//      Verify the server-side `await foreach` exits within 100ms of cancel.
