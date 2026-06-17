# Week 9 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 10. Answer key at the bottom — do not peek.

---

**Q1.** In proto3, a field declared `int32 x = 1;` is left unset by the sender. What does the receiver observe?

- A) The receiver's parser throws a "required field missing" error.
- B) The receiver sees `x = 0`, the zero default for `int32`. There is no way to distinguish "unset" from "set to zero" without the `optional` keyword.
- C) The receiver sees `x = null`, the C# null reference.
- D) The receiver's behaviour is undefined; different protobuf implementations disagree.

---

**Q2.** A `.proto` file declares:

```proto
enum Color {
  COLOR_UNSPECIFIED = 0;
  COLOR_RED = 1;
  COLOR_GREEN = 2;
}
```

A v2 server adds `COLOR_BLUE = 3` and starts emitting it in responses. A v1 client receives a message whose `color` field is `3`. What happens?

- A) The v1 client throws a "deserialisation failed" exception because `3` is not a known enum value.
- B) The v1 client reads `color` as the integer `3` and the C# generated code surfaces it as `(Color)3` — a value with no name. No exception is thrown.
- C) The v1 client sees `color = COLOR_UNSPECIFIED` (the zero default).
- D) The protobuf parser strips unknown enum values from the wire bytes.

---

**Q3.** What does the proto3 wire format look like for `message X { int32 a = 1; int32 b = 2; }` with `a = 0` and `b = 7`?

- A) Two field entries: tag `0x08` value `0`, tag `0x10` value `7`. Five bytes total.
- B) One field entry: tag `0x10` value `7`. Two bytes total. Field `a = 0` is the default and is not emitted.
- C) Three field entries: `a = 0`, `b = 7`, and a trailing "end-of-message" marker.
- D) JSON-encoded `{"a":0,"b":7}` wrapped in a length prefix.

---

**Q4.** A gRPC server method has signature:

```csharp
public override async Task GetStream(GetRequest request,
    IServerStreamWriter<Event> responseStream, ServerCallContext context)
```

What call type is this?

- A) Unary
- B) Server-streaming
- C) Client-streaming
- D) Bidirectional streaming

---

**Q5.** A C# client calls `client.IncrementAsync(request)` with no `deadline:` argument. The server is slow and takes 60 seconds. What happens?

- A) The client times out after the default 30-second deadline.
- B) The client waits indefinitely. There is no default deadline in `Grpc.Net.Client`.
- C) The client throws `RpcException` with `StatusCode.DeadlineExceeded` after 5 seconds.
- D) The HTTP/2 keepalive forces the call to fail at the 30-second mark.

---

**Q6.** A gRPC server method needs to indicate "the caller's authentication token is invalid." Which `StatusCode` is the correct choice?

- A) `StatusCode.PermissionDenied`
- B) `StatusCode.Unauthenticated`
- C) `StatusCode.Unauthorized`
- D) `StatusCode.Internal`

---

**Q7.** Which of the following is **the** principal reason to choose proto-first gRPC over code-first (`protobuf-net.Grpc`)?

- A) Proto-first generates faster server code; code-first is slower.
- B) Proto-first allows cross-language clients (Python, Go, Java, Swift, TypeScript) against the same schema. Code-first only supports .NET clients.
- C) Proto-first is required by .NET 8; code-first only works on .NET Framework.
- D) Proto-first supports streaming RPCs; code-first does not.

---

**Q8.** A `BoundedChannelOptions` is to in-process channels what `CallOptions.Deadline` is to gRPC. Which of these statements about the gRPC deadline is **false**?

- A) The deadline is an absolute UTC timestamp on the wire; the client computes the difference from "now" and sends it in the `grpc-timeout` HTTP/2 header.
- B) The server's `ServerCallContext.Deadline` is the same absolute UTC timestamp, derived from the wire header at the moment the request arrives.
- C) When the deadline fires server-side, `ServerCallContext.CancellationToken` is cancelled. Code that respects the token aborts cleanly.
- D) Deadlines are guaranteed to be accurate to one millisecond regardless of network latency.

---

**Q9.** A server-streaming RPC is opened, the C# client iterates `await foreach (var ev in call.ResponseStream.ReadAllAsync(ct))`, and the client breaks out of the loop after the third item. What happens to the underlying HTTP/2 stream?

- A) It is left open and held by the channel; the server keeps sending events that are silently discarded by the client.
- B) It is closed by the client's `RST_STREAM` frame when the `AsyncServerStreamingCall<T>` is disposed (typically by a `using` declaration on the call object). Forgetting the `using` leaks the stream.
- C) The stream cannot be closed until the server has sent all events; the client must drain to the end.
- D) The HTTP/2 connection is torn down entirely; all other concurrent streams on the channel are also broken.

---

**Q10.** Which of these is the **correct order** of interceptor execution for a server with three interceptors registered in this order: `Logging`, `Auth`, `Validation` (where "outer" runs before the inner)?

- A) `Logging-before → Auth-before → Validation-before → handler → Validation-after → Auth-after → Logging-after`
- B) `Validation-before → Auth-before → Logging-before → handler → Logging-after → Auth-after → Validation-after`
- C) `Logging, Auth, Validation` run concurrently around `handler`.
- D) Only the first-registered interceptor runs; the others are dead code.

---

## Answer key (no peeking until you have answered all ten)

1. **B.** Without `optional`, proto3 cannot distinguish unset from default. The wire emits nothing for default-valued scalars; the receiver sees the default. This is by design — proto3 traded the distinction for terser wire bytes — and the `optional` keyword is the way to opt back in to presence tracking.

2. **B.** Proto3 enums are *open*: unknown integer values are preserved and surfaced as `(EnumType)integer`. The C# generated code does not throw. This is the basis of forward-compatible enum extension — v2 can add values and v1 clients tolerate them.

3. **B.** Default-valued scalars are not emitted in proto3. The tag for `b = 7` is `(2 << 3) | 0 = 16 = 0x10` (one byte) followed by the varint `7` (one byte). Total two bytes. `a = 0` produces zero bytes on the wire.

4. **B.** A method whose response side has `IServerStreamWriter<T>` is server-streaming. The presence of `IServerStreamWriter<T>` on the response side and the absence of `IAsyncStreamReader<T>` on the request side together identify the shape.

5. **B.** `Grpc.Net.Client` has no default deadline. A call with no `deadline:` argument waits forever (or until the HTTP/2 keepalive eventually times out at the transport layer, but that is not a gRPC-level deadline). Always set a deadline on every call; the absence of one is the bug.

6. **B.** `Unauthenticated` is "who are you?" — the caller has not authenticated, or has authenticated with an invalid token. `PermissionDenied` is "you are authenticated as X, but X cannot do this." (C is not a real `StatusCode`; the gRPC analogue of HTTP 401 is `Unauthenticated`.)

7. **B.** Cross-language client generation is the principal value proposition of proto-first. Code-first generates only .NET code and locks you out of every other-language client your team will eventually want.

8. **D.** Deadlines are not millisecond-accurate. They are accurate to whatever resolution the runtime's clock and the cancellation-token plumbing provide — typically 10-50ms in practice. Designing for sub-50ms deadline precision is a mistake.

9. **B.** `AsyncServerStreamingCall<T>` is `IDisposable`. Disposing it (which a `using` declaration does at scope exit) sends `RST_STREAM` and tears the HTTP/2 stream down cleanly. Forgetting the `using` leaks a stream per early-exit — visible in `dotnet-counters monitor Grpc.AspNetCore.Server` as a growing current-streams count.

10. **A.** Interceptors wrap around the handler in registration order. First-registered runs "outermost" — its before-code runs first and its after-code runs last. ASP.NET Core middleware ordering uses the same model.

---

## Scoring

- **10/10**: You can teach this material. Move to Week 10 with confidence.
- **8–9**: Solid. Re-read the lecture sections corresponding to the questions you missed, then move on.
- **6–7**: Re-read all three lectures and retake. The gRPC operational model is dense; do not skim it.
- **≤5**: Slow down. Spend an extra evening on the lectures and the SOLUTIONS.md before attempting the mini-project.
