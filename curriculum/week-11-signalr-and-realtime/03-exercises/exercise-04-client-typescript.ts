// Exercise 4 — The Browser Client, in TypeScript.
//
// Goal: build the JS / TS browser side of the chat application. Demonstrate
// the full lifecycle: connect with a JWT, join rooms, handle reconnect with
// state replay, consume a streaming hub method, and (optionally) swap the
// protocol to MessagePack.
//
// Project layout (a tiny Vite project alongside the C# server):
//
//   client/
//     package.json
//     vite.config.ts
//     index.html
//     src/main.ts                <-- this file
//     src/style.css
//
// package.json:
//
//   {
//     "name": "ex04-client",
//     "private": true,
//     "type": "module",
//     "scripts": {
//       "dev":   "vite",
//       "build": "vite build"
//     },
//     "dependencies": {
//       "@microsoft/signalr":                   "8.0.0",
//       "@microsoft/signalr-protocol-msgpack":  "8.0.0"
//     },
//     "devDependencies": {
//       "vite":       "^5.0.0",
//       "typescript": "^5.3.0"
//     }
//   }
//
// vite.config.ts: standard `defineConfig({})` is fine; default port 5173.
//
// Commands:
//
//   cd client && npm install && npm run dev
//   # The server (Ex02 or Ex03) must already be running on :5000.

import * as signalR from "@microsoft/signalr";
// Uncomment the next line for MessagePack (and add `.withHubProtocol(...)`):
// import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

// ----------------------------------------------------------------------------
// State that survives across reconnects
// ----------------------------------------------------------------------------

type LogEntry = {
    id: number;
    room: string;
    user: string;
    text: string;
    timestamp: number;
};

type Pending = {
    room: string;
    text: string;
    clientMessageId: string;
};

const myRooms = new Set<string>(["general"]);
const outbound: Pending[] = [];
let lastSeenId = 0;

// ----------------------------------------------------------------------------
// Token acquisition (dev mode: mint via the server's /dev/token endpoint;
// in production, integrate with your real auth flow).
// ----------------------------------------------------------------------------

let cachedToken: string | null = null;

async function getToken(): Promise<string> {
    if (cachedToken !== null) return cachedToken;
    const resp = await fetch(
        "http://localhost:5000/dev/token?user=alice",
        { method: "POST" });
    const json = await resp.json();
    cachedToken = json.token as string;
    return cachedToken;
}

// ----------------------------------------------------------------------------
// Build the connection
// ----------------------------------------------------------------------------

const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5000/hubs/chat", {
        accessTokenFactory: () => getToken()
    })
    // .withHubProtocol(new MessagePackHubProtocol())  // <-- uncomment to swap
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Information)
    .build();

// Generous timeouts; adjust to match your network's idle behaviour.
connection.serverTimeoutInMilliseconds = 30000;
connection.keepAliveIntervalInMilliseconds = 15000;

// ----------------------------------------------------------------------------
// Handlers for server-pushed events
// ----------------------------------------------------------------------------

connection.on("ReceiveMessage", (room: string, user: string, text: string) => {
    appendLine(`[${room}] <${user}> ${text}`);
    // Note: this handler does not learn the message id. In production, the
    // server-side ReceiveMessage event carries the full LogEntry; we keep
    // the simple shape here because the Ex02 hub did not return ids.
});

connection.on("UserJoinedRoom", (room: string, user: string) => {
    appendLine(`[${room}] -- ${user} joined`);
});

connection.on("UserLeftRoom", (room: string, user: string) => {
    appendLine(`[${room}] -- ${user} left`);
});

connection.on("Welcome", (connId: string, user: string) => {
    appendLine(`[hub] welcome ${user} (connId=${connId})`);
});

// ----------------------------------------------------------------------------
// Reconnection lifecycle
// ----------------------------------------------------------------------------

connection.onreconnecting((err) => {
    setStatus("reconnecting", err?.message ?? "");
});

connection.onreconnected(async (newConnId) => {
    setStatus("connected", `connId=${newConnId}`);

    // Step 1: re-join rooms.
    for (const room of myRooms) {
        await connection.invoke("JoinRoom", room);
    }

    // Step 2: re-fetch missed messages per room. (Requires the FetchSince
    // method from Exercise 3.)
    for (const room of myRooms) {
        const missed = await connection.invoke<LogEntry[]>(
            "FetchSince", room, lastSeenId);
        for (const m of missed) {
            appendLine(`[${m.room}] (replay) <${m.user}> ${m.text}`);
            if (m.id > lastSeenId) lastSeenId = m.id;
        }
    }

    // Step 3: replay buffered outbound. Idempotent on clientMessageId.
    const toReplay = outbound.splice(0);
    for (const pending of toReplay) {
        try {
            await connection.invoke(
                "SendToRoomIdempotent",
                pending.room, pending.text, pending.clientMessageId);
        } catch (err) {
            // Push back on the buffer; we will retry on the next reconnect.
            outbound.unshift(pending);
            break;
        }
    }
});

connection.onclose((err) => {
    setStatus("disconnected", err?.message ?? "clean");
});

// ----------------------------------------------------------------------------
// Send (with offline buffering)
// ----------------------------------------------------------------------------

async function send(room: string, text: string): Promise<void> {
    const clientMessageId = crypto.randomUUID();

    if (connection.state !== signalR.HubConnectionState.Connected) {
        // Offline: buffer for replay on reconnect.
        outbound.push({ room, text, clientMessageId });
        appendLine(`[${room}] (buffered) ${text}`);
        return;
    }

    try {
        await connection.invoke(
            "SendToRoomIdempotent", room, text, clientMessageId);
    } catch (err) {
        // Send threw mid-flight: buffer for replay.
        outbound.push({ room, text, clientMessageId });
        appendLine(`[${room}] (send failed; buffered) ${text}`);
    }
}

// ----------------------------------------------------------------------------
// Streaming consumer (from Exercise 3's StreamLogs)
// ----------------------------------------------------------------------------

let streamSubscription: signalR.ISubscription<LogEntry> | null = null;

function startStream(room: string): void {
    if (streamSubscription !== null) {
        streamSubscription.dispose();
    }
    streamSubscription = connection
        .stream<LogEntry>("StreamLogs", room, lastSeenId)
        .subscribe({
            next: (entry) => {
                appendLine(`[stream:${entry.room}] ${entry.id}: <${entry.user}> ${entry.text}`);
                if (entry.id > lastSeenId) lastSeenId = entry.id;
            },
            complete: () => {
                appendLine(`[stream:${room}] -- complete`);
            },
            error: (err) => {
                appendLine(`[stream:${room}] -- error: ${err}`);
            }
        });
}

// ----------------------------------------------------------------------------
// DOM wiring (lives in index.html; helpers below)
// ----------------------------------------------------------------------------

function appendLine(line: string): void {
    const log = document.getElementById("log");
    if (log === null) {
        console.log(line);
        return;
    }
    const li = document.createElement("li");
    li.textContent = line;
    log.appendChild(li);
}

function setStatus(kind: "connecting" | "connected" | "reconnecting" | "disconnected",
                   detail: string): void {
    const status = document.getElementById("status");
    if (status === null) return;
    status.textContent = `${kind}: ${detail}`;
    status.className = `status-${kind}`;
}

// ----------------------------------------------------------------------------
// Entry point
// ----------------------------------------------------------------------------

async function main(): Promise<void> {
    setStatus("connecting", "");
    await connection.start();
    setStatus("connected", `connId=${connection.connectionId}`);

    for (const room of myRooms) {
        await connection.invoke("JoinRoom", room);
    }

    startStream("general");

    // Hook up the send box.
    const form = document.getElementById("send") as HTMLFormElement | null;
    if (form !== null) {
        form.addEventListener("submit", async (ev) => {
            ev.preventDefault();
            const input = document.getElementById("text") as HTMLInputElement;
            const text = input.value.trim();
            if (text.length === 0) return;
            input.value = "";
            await send("general", text);
        });
    }
}

main().catch((err) => {
    setStatus("disconnected", err?.message ?? String(err));
});

// ----------------------------------------------------------------------------
// index.html — the matching markup. Place at client/index.html.
// ----------------------------------------------------------------------------
//
//   <!DOCTYPE html>
//   <html lang="en">
//   <head>
//     <meta charset="UTF-8" />
//     <title>Ex04 — TypeScript Chat Client</title>
//     <link rel="stylesheet" href="/src/style.css" />
//   </head>
//   <body>
//     <h1>Crunch Chat (Ex04)</h1>
//     <div id="status" class="status-connecting">connecting...</div>
//     <ul id="log"></ul>
//     <form id="send">
//       <input id="text" placeholder="message" autocomplete="off" />
//       <button type="submit">Send</button>
//     </form>
//     <script type="module" src="/src/main.ts"></script>
//   </body>
//   </html>

// ----------------------------------------------------------------------------
// CHECKLIST AFTER YOU RUN IT
// ----------------------------------------------------------------------------
//
//   [ ] `npm run dev` starts Vite on :5173. The page loads; the status bar
//       transitions connecting -> connected within ~500ms of page load.
//
//   [ ] The log shows "welcome alice (connId=...)" — the OnConnectedAsync
//       handler from the hub fired and called Clients.Caller.Welcome.
//
//   [ ] Open a second browser tab. The first tab's log shows "-- alice
//       joined" (or whichever user the second tab is). Messages from one
//       tab appear in the other.
//
//   [ ] Kill the server with Ctrl+C. The status bar transitions to
//       "reconnecting". The send button still works; typed messages are
//       buffered (status line shows "(buffered)"). Restart the server;
//       within ~10s the status transitions to "connected", the rejoin /
//       refetch / replay sequence runs, and the buffered messages appear
//       in both tabs.
//
//   [ ] Open the browser dev tools. Network tab, filter "hubs/chat". You
//       see the negotiate POST (with ?access_token=eyJ... in the URL),
//       the WebSocket upgrade (101), and a steady stream of WebSocket
//       frames. Switch to the Frames tab to read the per-message envelope.
//
//   [ ] (Optional) Uncomment the MessagePack import + .withHubProtocol(...)
//       line. Reload. Verify the WebSocket frames in the dev tools are now
//       binary (hex view) instead of JSON, and that the per-message byte
//       count is roughly half what it was with JSON.
//
// Stretch (counted toward Exercise 4 if you finish the above with time left):
//   1. Wire the rooms set to a UI: a list of "join room" buttons. Verify
//      the onreconnected handler re-joins exactly the set the user had
//      active before the disconnect.
//   2. Persist `myRooms` and `outbound` to localStorage so that a page
//      reload (not just a transport reconnect) restores the user's state.
//      What is the trade-off vs server-side persistence?
//   3. Add a "show connection state" indicator that uses connection.state
//      polled every second. Visually distinguish Connecting, Connected,
//      Reconnecting, Disconnected.
