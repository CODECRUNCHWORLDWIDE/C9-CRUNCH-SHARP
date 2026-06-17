# Exercise Solutions — Week 9

These annotated solutions assume you have made a serious attempt at each exercise. Read your own attempt against the explanations below; do not copy without trying first.

---

## Exercise 1 — Design a proto

### The completed `.proto` (key fragments)

```proto
syntax = "proto3";

package crunch.shipment.v1;
option csharp_namespace = "Crunch.Shipment.V1";

import "google/protobuf/timestamp.proto";
import "google/protobuf/duration.proto";

enum ShipmentStatus {
  SHIPMENT_STATUS_UNSPECIFIED = 0;
  SHIPMENT_STATUS_CREATED = 1;
  SHIPMENT_STATUS_PICKED_UP = 2;
  SHIPMENT_STATUS_IN_TRANSIT = 3;
  SHIPMENT_STATUS_OUT_FOR_DELIVERY = 4;
  SHIPMENT_STATUS_DELIVERED = 5;
  SHIPMENT_STATUS_RETURNED = 6;
}

message ResidentialAddress {
  string street = 1;
  string city = 2;
  string region = 3;
  string postal_code = 4;
  string country_code = 5;
  optional string buzzer_code = 6;  // TODO-2 — presence matters
}

message Shipment {
  string id = 1;
  ShipmentStatus status = 2;

  oneof destination {
    ResidentialAddress residential = 3;
    CommercialAddress commercial = 4;
    LockerAddress locker = 5;
  }

  repeated ShipmentEvent events = 6;
  google.protobuf.Duration sla_window = 7;
  optional string carrier = 8;

  reserved 9;
  reserved "deprecated_legacy_field";
}
```

### Expected program output (illustrative)

```
Exercise 1 — proto3 round-trip and size prediction
------------------------------------------------------------
DestinationCase = Residential
HasCarrier (before set) = False
HasCarrier (after set)  = True
Carrier                 = ups

Predicted size = 79 bytes
Actual size    = 79 bytes
Sizes match.

Parsed Shipment:
  Id              = S0001
  Status          = ShipmentStatusInTransit
  DestinationCase = Residential
  Residential.Street = 123 Code Crunch Way
  Residential.HasBuzzerCode = False
  Events.Count    = 1
  SlaWindow       = 2.00:00:00
  Carrier         = ups (HasCarrier=True)

Switching destination from residential to locker...
DestinationCase after reassignment = Locker
Residential is now: not null
```

Byte count varies with the actual string lengths and timestamp; the predicted-equals-actual invariant is the thing to verify.

### Reflection answers

1. **Why `SHIPMENT_STATUS_UNSPECIFIED = 0`?** Because proto3 makes 0 the default for any enum field; if you do not set the field, the reader sees 0. Naming that 0 case `UNSPECIFIED` (rather than `CREATED` or `PENDING`) ensures the absence of information is *unambiguous*. If the zero value were `CREATED`, a sender who forgot to set the field would inadvertently report the shipment as created. The `UNSPECIFIED` convention is in the official protobuf style guide at <https://protobuf.dev/programming-guides/style/>.

2. **`optional string carrier = ""` vs unset.** With `optional`, the C# generated code emits a `HasCarrier` boolean. Setting `Carrier = ""` produces `HasCarrier = true` and a wire byte sequence containing the (zero-length) field. Leaving `Carrier` unset produces `HasCarrier = false` and *no bytes* on the wire for that field. The receiver can distinguish the two. Without the `optional` keyword, the two are indistinguishable on the wire — both produce zero bytes.

3. **Size prediction.** With `id = "S0001"` (5 bytes UTF-8), one event with `kind = PICKED_UP` and a `Timestamp` whose seconds is around 1.7 billion (≈ 5-byte varint, plus the tag), an empty notes string, an `sla_window` of 48h (one varint, ~3 bytes plus tag), the message is roughly 60–80 bytes. The exact value depends on the timestamp; `.CalculateSize()` will agree with `ToByteArray().Length` to the byte.

---

## Exercise 2 — Four call types

### The completed server (see the .cs file). Key correctness properties:

- **Unary** validates input and uses `checked` to surface overflow as `OutOfRange`.
- **Server-streaming** delays between writes so the streaming is observable, propagates `ct` into the `Task.Delay`, and bails out on cancellation.
- **Client-streaming** sums with `checked` so an overflow surfaces as a runtime exception (which the framework translates to `StatusCode.Internal` by default; a senior implementation would catch and rethrow `RpcException(StatusCode.OutOfRange)`).
- **Bidirectional** uses `Interlocked.Increment` on the sequence counter so it is safe under concurrent calls.

### Expected client output (illustrative)

```
Exercise 2 — driving all four call types against https://localhost:5001
------------------------------------------------------------

[1] Unary: Square(7)
    squared = 49

[2] Server-streaming: CountUp(1..5)
    received 1
    received 2
    received 3
    received 4
    received 5

[3] Client-streaming: Sum(10, 20, 30, 40)
    sum = 100, count = 4

[4] Bidirectional: Echo("alpha", "beta", "gamma")
    echo: text=alpha sequence=1
    echo: text=beta sequence=2
    echo: text=gamma sequence=3
```

### Reflection answers

1. **Server-streaming is the right shape for subscribe-to-events because** the server holds the stream open and pushes events the moment they are produced; the client does not poll. Polling unary RPCs costs one HTTP/2 stream per poll, has a worst-case event latency equal to the polling interval, and breaks under load. Server-streaming is one stream per subscription, near-zero added latency, and stable under load.

2. **If the client never calls `CompleteAsync()` on a client-streaming RPC**, the server's `await foreach (var req in requestStream.ReadAllAsync(ct))` will block forever — until either the client cancels, the deadline fires, or the HTTP/2 connection is torn down. If the deadline expires first, the server's `ct` fires (because `ServerCallContext.CancellationToken` is wired to the deadline), the `await foreach` throws `OperationCanceledException`, and the framework reports `StatusCode.DeadlineExceeded` to the client.

3. **In a bidi RPC where both sides only read**, you have a deadlock. Each side waits for the other to write; nobody writes; both `await foreach` calls hang forever. The deadline is the only thing that breaks the deadlock — which is why setting a deadline on every call is the senior discipline. The right design is to write a state-machine in your head: who is allowed to write first, what happens if both sides write concurrently, who closes their half first.

---

## Exercise 3 — Deadlines and interceptors

### Key correctness properties of the completed server

- **`DoWork` propagates `context.CancellationToken` into `Task.Delay`.** This is the load-bearing line. Without it, the server keeps delaying after the client has given up.
- **The validation paths throw `RpcException` with specific status codes.** `InvalidArgument` for "work_ms < 0" or "work_ms > 5000". Not `Unknown`, not raw `ArgumentException`.
- **The interceptor reads `x-correlation-id` from request headers.** Not from response headers; not from trailers. Request headers are where the client's metadata lives.

### Expected client output (deadline-exceeded case)

```
[a] 300 ms deadline against 1000 ms work — expect DeadlineExceeded
    got DeadlineExceeded as expected: Deadline Exceeded
```

### Expected server log (deadline-exceeded case)

```
info: SlowService[0]
      DoWork cancelled after 302 ms (label=deadline-short)
warn: LoggingServerInterceptor[0]
      RPC /crunch.slow.v1.SlowService/DoWork failed with DeadlineExceeded in 305 ms (correlation-id=a1b2c3d4)
```

The elapsed time is close to the deadline, not the requested work_ms. That is the proof the cancellation propagated.

### Reflection answers

1. **The elapsed time ≈ deadline (not work_ms) because `Task.Delay(workMs, ct)` throws `OperationCanceledException` the moment `ct` fires.** `ct` is wired to the deadline. So at deadline-time, `Task.Delay` throws, the `catch` records the elapsed time (~deadline + a few ms of scheduling overhead), and the server returns. The server did not waste 1000 ms; it wasted only ~300 ms. This is the dollar-cost of respecting your cancellation token, multiplied by every RPC in production.

2. **If the server forgot the `ct` argument to `Task.Delay`**, the log would show elapsed ≈ 1000 ms, even though the client gave up at 300 ms. The server would run to completion, write the response into a closed stream, and only at write-time discover the client was gone. The framework reports `DeadlineExceeded` to the client (correctly), but the server has wasted 700 ms of work and capacity. Multiply by 10,000 requests per second and the cost shows up in your dashboards as elevated tail latency on *unrelated* RPCs, because the wasted threads are not available for new work.

3. **For "client asked for too long a delay (> 5 seconds)"**, the correct status code is `InvalidArgument`. The client's request is malformed at the application layer — a value is out of an acceptable range. `OutOfRange` is technically also defensible (the gRPC error guide says `OutOfRange` is for "value crossed a boundary"), but `InvalidArgument` is the more common idiom for "this field's value is unacceptable." Either is defensible; `Internal`, `Unknown`, and `Unavailable` are all wrong — they imply the failure is not the client's fault, when it is.

---

## Common mistakes across the three exercises

- **Setting `EnableDetailedErrors = true` in `AddGrpc`.** Fine for development; do not ship it. The mini-project rubric explicitly checks that this is `false`.
- **Forgetting `using` on streaming calls.** `AsyncServerStreamingCall<T>` and `AsyncDuplexStreamingCall<TReq, TResp>` are `IDisposable`; forgetting the `using` leaks HTTP/2 streams on early exit. Run `dotnet-counters monitor Grpc.AspNetCore.Server` and watch the "current-streams" counter creep upward — that is the leak in action.
- **Calling the blocking client overload (`client.Square(req)` instead of `client.SquareAsync(req)`).** The blocking version is a thin sync wrapper that will deadlock under the same conditions as Week 8's "block on async code." The async version is the default; pretend the blocking one does not exist.
- **Constructing a fresh `GrpcChannel` per RPC.** The channel owns the TLS handshake and the HTTP/2 connection. Each new channel is a new connection. Reuse channels — one per server you talk to, for the lifetime of the process.

Next: the challenges. Challenge 1 takes the Exercise 2 server and calls it from a Python client; Challenge 2 evolves a schema while preserving wire compatibility.
