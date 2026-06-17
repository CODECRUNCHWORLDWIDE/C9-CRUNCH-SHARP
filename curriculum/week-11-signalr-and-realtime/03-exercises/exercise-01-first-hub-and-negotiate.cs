// Exercise 1 — First Hub, the Negotiate Handshake, and Reading the Wire.
//
// Goal: stand up a one-method EchoHub, connect from `curl` plus `wscat`, and
// read the negotiate JSON byte by byte. By the end you can recite the contents
// of `availableTransports` from memory and explain what each field of the
// negotiate response means.
//
// Project layout (you create this — there is no template to copy from):
//
//   src/Ex01.Server/
//     Ex01.Server.csproj
//     Program.cs                  <-- this file (parts shown below)
//     EchoHub.cs                  <-- the hub (also this file)
//
// .csproj contents you need:
//
//   <Project Sdk="Microsoft.NET.Sdk.Web">
//     <PropertyGroup>
//       <TargetFramework>net8.0</TargetFramework>
//       <Nullable>enable</Nullable>
//       <ImplicitUsings>enable</ImplicitUsings>
//     </PropertyGroup>
//   </Project>
//
// Commands you run (in this order):
//
//   dotnet new web -n Ex01.Server -f net8.0
//   cd Ex01.Server
//   # replace Program.cs with the Program.cs section below, paste EchoHub.cs
//   dotnet run                              # listens on http://localhost:5000
//
// Then in a second terminal:
//
//   # Step 1: read the negotiate response
//   curl -X POST -H "Content-Length: 0" \
//        http://localhost:5000/hubs/echo/negotiate?negotiateVersion=1 | jq
//
//   # Capture the connectionToken from the response into $TOKEN, then:
//   npm install -g wscat
//   wscat -c "ws://localhost:5000/hubs/echo?id=$TOKEN"
//
//   # Step 2: send the SignalR protocol handshake (note the ^^ is 0x1E, the
//   # ASCII record-separator byte). wscat does not type the 0x1E directly;
//   # use a Node one-liner instead:
//   node -e "const W=require('ws');const w=new W('ws://localhost:5000/hubs/echo?id='+process.argv[1]);w.on('open',()=>{w.send('{\"protocol\":\"json\",\"version\":1}\x1e');setTimeout(()=>w.send('{\"type\":1,\"target\":\"Echo\",\"arguments\":[\"hello\"]}\x1e'),100)});w.on('message',m=>console.log(String(m)))" "$TOKEN"
//
// Acceptance criteria:
//   1. The negotiate response shows three transports: WebSockets,
//      ServerSentEvents, LongPolling (in that order).
//   2. The connectionId and connectionToken in the response are distinct
//      strings.
//   3. The wscat / node session shows the server reply `{}` (the protocol
//      handshake ack) followed by the echo broadcast envelope:
//        {"type":1,"target":"OnEcho","arguments":["hello"]}
//   4. The Network tab in a browser (after wiring up the JS client below)
//      shows the negotiate POST and the WebSocket upgrade (101 Switching
//      Protocols) one after the other.

// ============================================================================
// PART 1 — EchoHub.cs (paste into its own file in the project)
// ============================================================================

#nullable enable
using Microsoft.AspNetCore.SignalR;

namespace Ex01.Server;

public sealed class EchoHub : Hub
{
    // A single server-callable method. The framework dispatches by name.
    // Note the casing: clients invoke "Echo" (or "echo" — names are
    // case-insensitive). The framework matches the C# method name.
    public Task Echo(string text)
    {
        // Broadcast back to every client. The first argument is the
        // *client-side handler name*; the rest are positional arguments
        // to that handler.
        return Clients.All.SendAsync("OnEcho", text);
    }

    // Lifecycle hooks for observability. Watch the console during the
    // exercise; you will see one OnConnectedAsync per browser tab.
    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"[connect]    {Context.ConnectionId}");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        string reason = exception is null ? "clean" : $"error: {exception.Message}";
        Console.WriteLine($"[disconnect] {Context.ConnectionId}  ({reason})");
        return base.OnDisconnectedAsync(exception);
    }
}

// ============================================================================
// PART 2 — Program.cs (replace the default Program.cs with this content)
// ============================================================================
//
// using Ex01.Server;
//
// var builder = WebApplication.CreateBuilder(args);
//
// // Turn on SignalR. This is the entire server-side registration for now.
// builder.Services.AddSignalR(options =>
// {
//     // Surface detailed errors to clients in dev. Default is false so that
//     // exception messages do not leak to the wire in production.
//     options.EnableDetailedErrors = builder.Environment.IsDevelopment();
// });
//
// // CORS for local browser-client testing. Adjust origins for your dev URL.
// builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
//     p.WithOrigins("http://localhost:5173")
//      .AllowAnyHeader()
//      .AllowAnyMethod()
//      .AllowCredentials()));
//
// var app = builder.Build();
//
// app.UseCors();
//
// // Map the hub. The path is yours; the convention is /hubs/<name>.
// app.MapHub<EchoHub>("/hubs/echo");
//
// // A trivial root so `curl http://localhost:5000/` returns something useful.
// app.MapGet("/", () => "Ex01.Server. Hub is at /hubs/echo.");
//
// app.Run();

// ============================================================================
// PART 3 — a tiny browser client (place in wwwroot/index.html OR a separate
// Vite project; the file lives outside the .csproj)
// ============================================================================
//
// <!DOCTYPE html>
// <html>
// <head><title>Ex01 Echo</title></head>
// <body>
//   <input id="text" placeholder="message" />
//   <button onclick="send()">Send</button>
//   <ul id="log"></ul>
//   <script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
//   <script>
//     const log = (m) => {
//       const li = document.createElement("li");
//       li.textContent = m;
//       document.getElementById("log").appendChild(li);
//     };
//
//     const connection = new signalR.HubConnectionBuilder()
//       .withUrl("http://localhost:5000/hubs/echo")
//       .configureLogging(signalR.LogLevel.Information)
//       .build();
//
//     connection.on("OnEcho", (text) => log("server says: " + text));
//
//     async function start() {
//       try {
//         await connection.start();
//         log("connected. id=" + connection.connectionId);
//       } catch (err) {
//         log("connect failed: " + err.toString());
//       }
//     }
//
//     async function send() {
//       const text = document.getElementById("text").value;
//       await connection.invoke("Echo", text);
//     }
//
//     start();
//   </script>
// </body>
// </html>

// ============================================================================
// PART 4 — a tiny .NET client (a console app you build alongside the server)
// ============================================================================
//
// Project: dotnet new console -n Ex01.Client -f net8.0
// Package: dotnet add package Microsoft.AspNetCore.SignalR.Client --version 8.0.0
//
// using Microsoft.AspNetCore.SignalR.Client;
//
// var connection = new HubConnectionBuilder()
//     .WithUrl("http://localhost:5000/hubs/echo")
//     .ConfigureLogging(b => b.AddConsole())
//     .Build();
//
// // Register a handler BEFORE Start, so we do not miss broadcasts.
// connection.On<string>("OnEcho", text =>
// {
//     Console.WriteLine($"[recv] {text}");
// });
//
// await connection.StartAsync();
// Console.WriteLine($"[connected] {connection.ConnectionId}");
//
// // Send a few messages.
// for (int i = 0; i < 3; i++)
// {
//     await connection.InvokeAsync("Echo", $"hello #{i}");
//     await Task.Delay(200);
// }
//
// Console.WriteLine("[done] press Enter to disconnect.");
// Console.ReadLine();
// await connection.StopAsync();

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] `curl http://localhost:5000/hubs/echo/negotiate?negotiateVersion=1`
//       (POST) returns a JSON object with three transports listed and a
//       distinct connectionToken.
//
//   [ ] Opening index.html in a browser shows "connected. id=..." with a
//       short base64-ish identifier. The server console prints
//       "[connect] <same-id>".
//
//   [ ] Typing "hello" + Send produces "server says: hello" in the browser
//       log AND in any other tab that has the same page open.
//
//   [ ] The Ex01.Client console app, run alongside, sees the same broadcasts
//       and prints them at "[recv] hello".
//
//   [ ] In the browser dev tools Network tab, filter to "hubs/echo". You see
//       two requests: the negotiate POST (200, JSON body) and the WebSocket
//       upgrade (101 Switching Protocols). Click the WebSocket and switch to
//       the "Messages" / "Frames" tab; you see the protocol-handshake frame
//       ({"protocol":"json","version":1}^^), the empty-object server ack,
//       and the invocation frames.
//
//   [ ] Closing the browser tab produces "[disconnect] <id> (clean)" on the
//       server console within ~30 seconds (the server keep-alive timeout).
//
// Stretch (counted toward Exercise 1 if you finish the above with time left):
//   1. Force the client to use long polling only (HttpTransportType.LongPolling)
//      and observe in the Network tab that the WebSocket upgrade disappears,
//      replaced by alternating GET (long-poll for inbound) and POST (outbound)
//      requests. Latency is higher; the protocol is otherwise identical.
//   2. Add Microsoft.AspNetCore.SignalR logging at Debug level in
//      appsettings.Development.json. Re-run; observe the per-method dispatch
//      lines.
