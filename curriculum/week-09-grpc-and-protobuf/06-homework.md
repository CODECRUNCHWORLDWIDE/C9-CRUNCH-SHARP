# Week 9 — Homework

Six practice problems. Allocate roughly 1 hour per problem; the last two are longer and may need 90 minutes. Submit one .zip of code + a single `homework.md` write-up. Rubric at the bottom.

---

## Problem 1 — Map a domain to a `.proto` from scratch (60 min)

Take the following domain spec and produce a `.proto` file that models it. Justify each design decision in a comment.

> **Domain:** a "support ticket" system. A `Ticket` has an id, a subject, a body, a status (one of `OPEN`, `IN_PROGRESS`, `RESOLVED`, `CLOSED`), a priority (`LOW`/`MEDIUM`/`HIGH`/`URGENT`), a creation timestamp, an optional `resolved_at` timestamp, a list of `Comment`s (each with id, author, body, timestamp), an optional `assignee_email`, and a list of `label` tags (free-text strings). The service exposes one unary RPC `CreateTicket(CreateTicketRequest)` returning `CreateTicketResponse { string id, Ticket ticket }`.

**Required of your `.proto`:**

- `syntax = "proto3";` and a versioned `package` (e.g. `crunch.support.v1`).
- `option csharp_namespace = "...";`.
- `enum`s for status and priority, with `XXX_UNSPECIFIED = 0` for each.
- `optional` on the two genuinely-optional fields.
- `repeated` for the comments and the labels.
- `google.protobuf.Timestamp` for the two timestamp fields.
- A `reserved 99;` line with a comment explaining what hypothetical v0 field it represents.
- Field numbers chosen with hot/cold awareness (hot fields in 1–15).

**Deliverable:** `support.proto` plus a `notes.md` paragraph (one paragraph) defending each choice.

---

## Problem 2 — Predict the wire bytes (45 min)

Take the `Ticket` message from Problem 1. Construct one in C# with these specific values:

- `id = "T0042"`
- `subject = "ok"`
- `body = ""`
- `status = OPEN` (value 1, not the unspecified default)
- `priority = MEDIUM` (value 2)
- `created_at` = a specific Timestamp (you choose)
- `resolved_at` unset
- `comments` empty
- `assignee_email` unset
- `labels` = `["urgent", "auth"]`

**Predict the byte count by hand** in a markdown table, field by field — show the tag byte, the length byte (where applicable), and the payload bytes. Then construct the message in C# and call `.CalculateSize()` to verify.

**Deliverable:** a `wire-prediction.md` with the per-field breakdown and a one-line confirmation of the actual size from `.CalculateSize()`. If your prediction and the actual differ, find the bug and document the cause.

---

## Problem 3 — Implement a unary RPC with proper error mapping (75 min)

Take the `CreateTicket` RPC from Problem 1. Implement it in `Grpc.AspNetCore`. Validation rules:

- `subject` must be non-empty and ≤ 200 chars.
- `body` may be empty but must be ≤ 10,000 chars.
- `priority` must be one of `LOW`/`MEDIUM`/`HIGH`/`URGENT` (not `UNSPECIFIED`).
- `assignee_email`, if present, must match the basic shape `*@*.*`.

For each violated rule, throw `RpcException` with the *most specific* status code. Defend each choice in a comment.

Write a small console client that exercises:

1. A happy-path successful create.
2. An empty-subject create — expect `InvalidArgument`.
3. A 250-character subject — expect `InvalidArgument`.
4. An `assignee_email = "notanemail"` create — expect `InvalidArgument`.
5. A priority of `UNSPECIFIED` create — expect `InvalidArgument` (defend why this is the right code rather than `FailedPrecondition`).

**Deliverable:** the server, the client, and a `notes.md` with each status-code choice defended in one sentence.

---

## Problem 4 — Server-streaming with proper cancellation (75 min)

Implement a server-streaming RPC `WatchTickets(WatchRequest)` returning `stream TicketEvent` where `WatchRequest` has a `string assignee_email` filter, and `TicketEvent` has a `kind` (CREATED/UPDATED/COMMENTED) and a `Ticket` payload.

Server requirements:

- In-memory event source (a `Channel<TicketEvent>` populated by a background producer).
- The RPC reads from the channel, filters by assignee, and writes to `IServerStreamWriter<TicketEvent>`.
- The RPC respects `context.CancellationToken` — exit the loop the moment the token fires.
- The RPC produces at least one event every 2 seconds to avoid HTTP/2 idle timeouts (you may emit a synthetic "keepalive" event of `kind = UNSPECIFIED`).

Client requirements:

- Open the watch stream with a 60-second deadline.
- Iterate with `await foreach`, printing each event.
- After receiving 5 non-keepalive events, cancel the call (cancel the `cts`).
- Observe `RpcException` with `StatusCode.Cancelled` and verify the server-side log shows the loop exited within 100ms of cancellation.

**Deliverable:** server + client + a `notes.md` answering: how does the C# `IServerStreamWriter<T>.WriteAsync` interact with HTTP/2 flow control? What happens if the consumer is slow — does the server back up?

---

## Problem 5 — Client-side and server-side interceptors with metadata propagation (90 min)

Build:

1. A client-side `RequestIdInterceptor` that ensures every outbound call carries an `x-request-id` header. If the calling code did not provide one, generate a fresh GUID (truncated to 8 hex chars).
2. A server-side `RequestContextInterceptor` that reads `x-request-id`, stores it in `ServerCallContext.UserState`, and pushes it into the logger scope (`ILogger.BeginScope`).
3. A small chain of three services: `ServiceA` calls `ServiceB` calls `ServiceC`. Each service is its own gRPC server, each uses `EnableCallContextPropagation()`, and each logs every call with the `x-request-id` in the log scope.

Verify by hand that a single `x-request-id` value threads through every log line across all three services for one client call. Capture the three servers' logs and align them.

**Deliverable:** the three services, the interceptors, and a screenshot or text snippet of the aligned logs showing the same `x-request-id` on the call's entry into A, A's call into B, B's call into C, and C's return.

---

## Problem 6 — Cross-language: a Go client (90 min, stretch)

If you have Go installed (or are willing to install it: <https://go.dev/dl/>), this problem repeats Challenge 1 but for Go instead of Python. Generate Go stubs from the Exercise 2 `numbers.proto`:

```bash
go install google.golang.org/protobuf/cmd/protoc-gen-go@latest
go install google.golang.org/grpc/cmd/protoc-gen-go-grpc@latest

protoc --go_out=. --go_opt=paths=source_relative \
       --go-grpc_out=. --go-grpc_opt=paths=source_relative \
       numbers.proto
```

Write a Go `main.go` that exercises all four call types against the C# server.

**Deliverable:** a Go module with `go.mod`, `go.sum`, `main.go`, and the generated stubs. A `notes.md` comparing the Go API surface to the Python and C# ones — which language has the most ergonomic stream consumption? Which has the cleanest error model?

If you do not have Go and do not want to install it, substitute this with: **write a second Python client that exercises the bidirectional Echo RPC using `grpc.aio` (the asyncio variant of `grpcio`)** instead of the synchronous `grpcio`. Compare the API ergonomics.

---

## Rubric

For each problem (max 100 points):

| Tier | Points | Description |
|------|--------|-------------|
| Master | 90–100 | Compiles. Every acceptance criterion met. The `notes.md` shows reasoning beyond the literal answer — at least one observation the spec did not ask for. |
| Solid | 75–89 | Compiles. Every acceptance criterion met. The `notes.md` answers what was asked, no more. |
| Working | 60–74 | Compiles. Most acceptance criteria met; one or two missed. |
| Partial | 40–59 | Compiles in places but with significant gaps; the spec was not fully read. |
| Submitted | 0–39 | Submission exists; substantial parts are missing or broken. |

Total: **600 points** across the six problems. **480** is the C9-passing threshold for this week's homework. The mini-project is graded separately.

## Submission

Zip the six problem folders together as `week-09-homework-<your-name>.zip`. Include a top-level `homework.md` that links to each problem's `notes.md` and lists your self-assigned score in each tier.

Submit by Sunday 11:59 PM local time. Late submissions are accepted with a one-tier markdown per 24h past the deadline.
