# Week 11 — Exercises

These exercises walk you through ASP.NET Core SignalR from the wire up: a one-method echo hub and the negotiate handshake, then JWT auth on the WebSocket upgrade with groups, then `IAsyncEnumerable` streaming and the reconnect-and-replay pattern, and finally the TypeScript browser client that ties it all together. Each file builds on the one before it, so work them in order — by the end you will have a small chat application you can connect to from `curl`, `wscat`, the .NET client, and a real browser, and you will have read every byte that crosses the wire.

## How to Run an Exercise

The `.cs` exercise files are not meant to be compiled as-is — each one is a guided build sheet whose header comments give you the exact `dotnet new`, `.csproj`, and command-line steps, with the hub and `Program.cs` source inlined to paste into the project you scaffold. To work an exercise:

1. Read the header comment block for the project layout, the `.csproj` contents, and the commands to run.
2. Scaffold the project it describes, for example:

   ```bash
   dotnet new web -n Ex01.Server -f net8.0
   cd Ex01.Server
   ```

3. Paste the hub class and `Program.cs` sections from the file into the matching files, add any packages the header lists, then `dotnet run` (the servers listen on `http://localhost:5000`).
4. Exercise the running hub with the `curl`, `wscat`, Node, or `dotnet new console` client snippets in the file, and read the negotiate JSON and WebSocket frames in your browser's Network tab.

You will need the .NET 8 SDK (`dotnet --version` reporting `8.0.x`) and, for Exercise 4, Node.js 20+. Exercise 4 is TypeScript — run it with `npm install && npm run dev` in the Vite client it describes, against a server from Exercise 2 or 3.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|-----------:|----------:|
| 1 | [exercise-01-first-hub-and-negotiate.cs](./exercise-01-first-hub-and-negotiate.cs) | Standing up a one-method `EchoHub`, connecting from `curl`/`wscat`/.NET, and reading the negotiate response and WebSocket frames byte by byte | Beginner | 60 min |
| 2 | [exercise-02-groups-and-jwt.cs](./exercise-02-groups-and-jwt.cs) | JWT bearer auth on the upgrade via the `OnMessageReceived` hook, a strongly-typed `Hub<IChatClient>`, and join/leave/send with groups | Intermediate | 75 min |
| 3 | [exercise-03-streaming-and-reconnect.cs](./exercise-03-streaming-and-reconnect.cs) | An `IAsyncEnumerable<LogEntry>` streaming method, `FetchSince` for gap recovery, idempotent sends, and a .NET client with full reconnect/replay | Intermediate+ | 90 min |
| 4 | [exercise-04-client-typescript.ts](./exercise-04-client-typescript.ts) | The browser side in TypeScript: `accessTokenFactory`, automatic reconnect with room rejoin and replay, a streaming consumer, and the optional MessagePack swap | Intermediate | 75 min |

## Checking Your Work

Annotated solutions, including the real wire-format payloads you should reproduce, live in [SOLUTIONS.md](./SOLUTIONS.md). Read them after you attempt each exercise, not before. Every exercise also ends with its own checklist and stretch goals — work down the checklist first. As a quick self-check:

- Your server prints `[connect]`/`[disconnect]` lifecycle lines and `dotnet build` finishes with 0 warnings and 0 errors.
- The browser Network tab shows the negotiate POST followed by the WebSocket upgrade (101 Switching Protocols), and the Frames view shows the protocol handshake and invocation envelopes you expect.
- Auth, groups, streaming, and reconnect behave as the file's checklist describes — for example, an unauthenticated negotiate returns 401, a room broadcast reaches only that room, and a simulated disconnect triggers the rejoin/refetch/replay sequence.
