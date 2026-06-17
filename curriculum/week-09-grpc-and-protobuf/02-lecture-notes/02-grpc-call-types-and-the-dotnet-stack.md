# Lecture 2 — gRPC over HTTP/2: the Four Call Types, the C# Server, the C# Client, and the Cross-Language Story

> **Time:** 2 hours. **Prerequisites:** Lecture 1 (proto3), Week 2 (ASP.NET Core hosting), Week 8 (`IAsyncEnumerable<T>` and `await foreach`). **Citations:** the gRPC concepts overview at <https://grpc.io/docs/what-is-grpc/core-concepts/>, the gRPC-over-HTTP/2 spec at <https://grpc.io/docs/guides/wire/>, Microsoft Learn's gRPC services with ASP.NET Core at <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>, and Microsoft Learn's gRPC client guide at <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>.

## 1. What gRPC actually is

gRPC is a remote procedure call protocol. The "remote procedure call" part is old — it goes back to Sun RPC in the 1980s, DCE/RPC in the 1990s, CORBA, SOAP, Thrift. The thing that makes gRPC the modern default is the *combination* of choices:

- **HTTP/2 as the transport.** Every gRPC call is an HTTP/2 stream. This means streams are multiplexed (many concurrent calls over one TCP connection), flow-controlled (the receiver can backpressure the sender), and bidirectional (server can stream back without holding open a separate connection). It also means gRPC inherits HTTP/2's TLS story, header compression (HPACK), and the entire HTTP infrastructure ecosystem.
- **Protocol Buffers as the wire format.** Compact, schema-enforced, code-generated. Lecture 1 covered this in depth.
- **A fixed set of call shapes**: unary, server-streaming, client-streaming, bidirectional-streaming. Section 3 below.
- **First-class deadlines, cancellation, and structured errors.** Lecture 3 covers these.
- **Polyglot code generation.** A single `.proto` file generates working clients and servers in 11 mainline languages.

The combination is the value proposition. If you only need one of these — say, just HTTP/2 with JSON — gRPC is overkill, and ASP.NET Core minimal APIs (Week 2) is the right tool. The reason to take on gRPC's complexity is when you want the *whole package*: typed schemas, polyglot clients, streaming primitives, deadlines, all of it.

Read the gRPC concepts overview at <https://grpc.io/docs/what-is-grpc/core-concepts/> before continuing. It is short — 1500 words — and establishes vocabulary we use below.

## 2. gRPC on HTTP/2: how the wire looks

A gRPC call is one HTTP/2 stream. The request headers look approximately like this (citation: <https://grpc.io/docs/guides/wire/>):

```
:method      = POST
:scheme      = https
:path        = /crunch.counter.v1.Counter/Increment
:authority   = counter.example.com
content-type = application/grpc+proto
te           = trailers
grpc-timeout = 2S
user-agent   = grpc-dotnet/2.60.0
```

Five things to notice. First, the path is `/<package>.<Service>/<Method>` — derived directly from the `.proto`. Second, the `content-type` distinguishes gRPC from regular HTTP/2; the `+proto` suffix is the encoding. Third, `te: trailers` is required by gRPC (it tells the server that the client accepts HTTP/2 trailing headers, where the gRPC status code lives). Fourth, `grpc-timeout` is the wire form of the client's deadline — `2S` means "2 seconds from now"; the server reads this and sets its own `ServerCallContext.Deadline` accordingly. Fifth, the user-agent identifies the client implementation; `grpc-dotnet/2.60.0` is what `Grpc.Net.Client` emits.

The request body is a series of **length-prefixed messages**. Each message is:

```
[ compressed-flag (1 byte) ][ length (4 bytes, big-endian) ][ payload (N bytes) ]
```

The compressed-flag is 0 (uncompressed) or 1 (compressed with the per-call codec). The length is the size of the payload. The payload is the protobuf-encoded message bytes. For a unary call there is exactly one message in this stream; for a client-streaming or bidirectional call there are many.

After the last request message, the client half-closes the stream (sends an HTTP/2 `END_STREAM` flag with no body). The server reads the request(s), processes, and writes its response(s) in the same length-prefixed format. After the last response message the server sends **trailers**:

```
grpc-status   = 0
grpc-message  = OK
```

`grpc-status = 0` is success (`StatusCode.OK`). Any non-zero value is an error; `grpc-message` carries the human-readable status text. The client's `RpcException` is constructed from these trailers.

You do not, in normal use, see any of this. `Grpc.Net.Client` produces these bytes; `Grpc.AspNetCore.Server` consumes them. The reason to know the shape is so that when something goes wrong — Wireshark capture in front of you, error from a load balancer — you can read the bytes and locate the problem.

## 3. The four call types

A `.proto` service can declare four kinds of RPC, distinguished by where the `stream` keyword appears:

```proto
service Counter {
  rpc Get(GetRequest) returns (GetResponse);                          // 1. unary
  rpc Subscribe(SubscribeRequest) returns (stream CounterEvent);      // 2. server-streaming
  rpc BatchIncrement(stream IncrementRequest) returns (BatchSummary); // 3. client-streaming
  rpc LiveOps(stream OpRequest) returns (stream OpResponse);          // 4. bidirectional
}
```

The four shapes correspond to four very different C# programming models. Choose deliberately at design time; converting between them post-hoc is a wire-incompatible change.

### 3.1 Unary

The familiar shape: one request in, one response out. Equivalent in feel to an HTTP POST that returns a body.

**On the wire:** the client sends one length-prefixed message, half-closes; the server sends one length-prefixed response message, then trailers.

**Server-side C#** (with `Grpc.AspNetCore`):

```csharp
public sealed class CounterService : Counter.CounterBase
{
    public override Task<GetResponse> Get(GetRequest request, ServerCallContext context)
    {
        var value = _store.Read(request.Name);
        return Task.FromResult(new GetResponse { Value = value });
    }
}
```

**Client-side C#** (with `Grpc.Net.Client`):

```csharp
using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new Counter.CounterClient(channel);

var reply = await client.GetAsync(new GetRequest { Name = "alice" });
Console.WriteLine($"value = {reply.Value}");
```

The `Get` method on the generated `CounterClient` has both a blocking overload (`Get(...)`) and an async one (`GetAsync(...)`). Always use the async one. The async overload returns an `AsyncUnaryCall<TResponse>` which is awaitable directly and also exposes `ResponseAsync`, `ResponseHeadersAsync`, `GetTrailers()`, and `GetStatus()`.

**When to use unary.** This is the default — request/response semantics, the same shape as a REST POST. Use it for ~80% of RPCs.

### 3.2 Server-streaming

One request in, many responses out. The shape for "subscribe to a feed", "stream a query's rows", "watch for changes."

**On the wire:** the client sends one length-prefixed message, half-closes. The server sends N length-prefixed response messages over time, then trailers. The stream is one-way after the request.

**Server-side C#:**

```csharp
public sealed class CounterService : Counter.CounterBase
{
    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<CounterEvent> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        await foreach (var ev in _events.SubscribeAsync(request.Name, ct))
        {
            await responseStream.WriteAsync(ev, ct);
        }
    }
}
```

Three things to notice. First, the method signature has *three* parameters now: the request, the response *stream writer*, and the context. The generator emits this shape automatically — `stream` on the response side becomes `IServerStreamWriter<T>`. Second, the return type is `Task`, not `Task<T>` — there is no single response. Third, the cancellation token from `context.CancellationToken` is propagated into the upstream feed; if the client disconnects or the deadline expires, this token fires and the iteration stops.

**Client-side C#:**

```csharp
using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new Counter.CounterClient(channel);

using var call = client.Subscribe(new SubscribeRequest { Name = "alice" });
await foreach (var ev in call.ResponseStream.ReadAllAsync(cts.Token))
{
    Console.WriteLine($"event: {ev.Kind} value={ev.NewValue}");
}
```

`client.Subscribe(...)` is not awaitable directly — it returns an `AsyncServerStreamingCall<TResponse>` which exposes `ResponseStream`, an `IAsyncStreamReader<TResponse>`. The `ReadAllAsync(ct)` extension method turns the reader into an `IAsyncEnumerable<TResponse>` that you consume with `await foreach`. This is Week 8 territory — the cancellation token threads through the iteration.

**`using` on `call`** is load-bearing. The `AsyncServerStreamingCall<T>` is `IDisposable`; disposing it cancels the underlying HTTP/2 stream if you bail out of the loop early. Forgetting the `using` leaks an HTTP/2 stream per early-exit.

**When to use server-streaming.** Long-running subscriptions, large query results that should not all be buffered in memory, real-time event feeds. The Pub/Sub-feel shape.

### 3.3 Client-streaming

Many requests in, one response out. The shape for "upload a large file in chunks", "batch a stream of writes into one acknowledgement."

**On the wire:** the client sends N length-prefixed messages over time, then half-closes. The server reads them, processes, and sends one length-prefixed response, then trailers.

**Server-side C#:**

```csharp
public sealed class CounterService : Counter.CounterBase
{
    public override async Task<BatchSummary> BatchIncrement(
        IAsyncStreamReader<IncrementRequest> requestStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        long total = 0;
        int count = 0;
        await foreach (var req in requestStream.ReadAllAsync(ct))
        {
            total += req.Delta;
            count++;
        }
        return new BatchSummary { Count = count, Total = total };
    }
}
```

The request stream is `IAsyncStreamReader<IncrementRequest>`. Iterate with `await foreach` and `ReadAllAsync`. The return type is `Task<BatchSummary>`, the same as a unary RPC's return type.

**Client-side C#:**

```csharp
using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new Counter.CounterClient(channel);

using var call = client.BatchIncrement();
foreach (var delta in new[] { 1, 2, 3, 4, 5 })
{
    await call.RequestStream.WriteAsync(new IncrementRequest { Name = "alice", Delta = delta });
}
await call.RequestStream.CompleteAsync();

var summary = await call.ResponseAsync;
Console.WriteLine($"count={summary.Count} total={summary.Total}");
```

Three things to notice. First, `client.BatchIncrement()` takes no arguments — there is no single request to pass at call time. Second, you push requests through `call.RequestStream.WriteAsync(...)` one at a time. Third, you *must* call `CompleteAsync()` to signal the server that there are no more requests; otherwise the server blocks on its `await foreach` forever (until the deadline). Then you `await call.ResponseAsync` to get the reply.

**When to use client-streaming.** Genuinely streaming inputs — file uploads, log shipping, batches that are too large to buffer client-side. For "I have a list of 10 things, send them all": just use a unary RPC with a `repeated` field. Client-streaming is overkill there.

### 3.4 Bidirectional streaming

Many requests in, many responses out, independent of each other. The shape for chat, for live games, for two-way subscriptions.

**On the wire:** both halves of the stream are open concurrently. Client sends a message any time; server sends a message any time; either side closes its half independently.

**Server-side C#:**

```csharp
public sealed class CounterService : Counter.CounterBase
{
    public override async Task LiveOps(
        IAsyncStreamReader<OpRequest> requestStream,
        IServerStreamWriter<OpResponse> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        await foreach (var op in requestStream.ReadAllAsync(ct))
        {
            var result = await _store.ApplyAsync(op, ct);
            await responseStream.WriteAsync(result, ct);
        }
    }
}
```

This particular server reads request, writes response, reads request, writes response — a request/response loop that happens to share a single stream. That is *one* shape for a bidi method; the more interesting shapes have the read loop and the write loop running concurrently:

```csharp
public override async Task LiveOps(
    IAsyncStreamReader<OpRequest> requestStream,
    IServerStreamWriter<OpResponse> responseStream,
    ServerCallContext context)
{
    var ct = context.CancellationToken;

    var readTask = Task.Run(async () =>
    {
        await foreach (var op in requestStream.ReadAllAsync(ct))
        {
            _commands.Enqueue(op);
        }
    }, ct);

    var writeTask = Task.Run(async () =>
    {
        await foreach (var ev in _events.SubscribeAsync(ct))
        {
            await responseStream.WriteAsync(new OpResponse { Event = ev }, ct);
        }
    }, ct);

    await Task.WhenAll(readTask, writeTask);
}
```

Two `Task.Run`s, both consuming the same `ct`, joined with `Task.WhenAll`. This is the "really bidirectional" shape — the read side and write side are independent.

**Client-side C#:**

```csharp
using var call = client.LiveOps();

var sendTask = Task.Run(async () =>
{
    for (int i = 0; i < 10; i++)
    {
        await call.RequestStream.WriteAsync(new OpRequest { Action = "increment" });
        await Task.Delay(100);
    }
    await call.RequestStream.CompleteAsync();
});

var recvTask = Task.Run(async () =>
{
    await foreach (var ev in call.ResponseStream.ReadAllAsync(cts.Token))
    {
        Console.WriteLine($"event: {ev.Kind}");
    }
});

await Task.WhenAll(sendTask, recvTask);
```

**When to use bidi.** Chat, multiplayer games, two-way subscriptions, anything where both sides genuinely produce messages independently. For request/response-with-streaming-extras, prefer two separate RPCs (one unary, one server-streaming) — the simpler shape is easier to reason about and easier to load-balance.

## 4. The C# server: hosting a gRPC service in ASP.NET Core

The hosting model is ASP.NET Core. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore>.

A minimal server `Program.cs`:

```csharp
#nullable enable

using Crunch.Counter.V1;
using Crunch.Counter.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;  // dev-only; see Lecture 3
    options.MaxReceiveMessageSize = 4 * 1024 * 1024;  // 4 MiB
    options.MaxSendMessageSize = 4 * 1024 * 1024;
});

builder.Services.AddSingleton<ICounterStore, InMemoryCounterStore>();

var app = builder.Build();

app.MapGrpcService<CounterService>();
app.MapGet("/", () => "Crunch Counter gRPC. Use a gRPC client.");

app.Run();
```

`AddGrpc` registers the gRPC services-side DI. `MapGrpcService<T>` plugs the generated service routing into the request pipeline. The `MapGet("/")` is so that humans hitting `https://localhost:5001/` in a browser get a hint rather than a 404.

The Kestrel default in .NET 8 is HTTPS-only with HTTP/2 enabled. You do not need to configure HTTPS explicitly; `WebApplication.CreateBuilder` reads `appsettings.json` and uses the `dotnet dev-certs` certificate for development. We will return to TLS in Lecture 3.

### 4.1 The service implementation

The service class inherits from the generated base:

```csharp
#nullable enable

using Grpc.Core;
using Crunch.Counter.V1;

namespace Crunch.Counter.Server;

public sealed class CounterService : Counter.CounterBase
{
    private readonly ICounterStore _store;
    private readonly ILogger<CounterService> _logger;

    public CounterService(ICounterStore store, ILogger<CounterService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public override Task<GetResponse> Get(GetRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "name must be non-empty"));

        var value = _store.Read(request.Name);
        return Task.FromResult(new GetResponse { Value = value });
    }

    public override Task<IncrementResponse> Increment(
        IncrementRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "name must be non-empty"));

        var newValue = _store.Increment(request.Name, request.Delta);
        return Task.FromResult(new IncrementResponse { NewValue = newValue });
    }
}
```

Three things to notice. First, the service is a normal DI-registered class — the constructor takes `ICounterStore` and `ILogger<T>`, the same way any ASP.NET Core service would. Second, validation throws `RpcException` with `StatusCode.InvalidArgument`, not `ArgumentException`. Lecture 3 covers the error model in depth. Third, the methods are `Task<T>`-returning; the framework calls them, awaits, and serialises the response.

### 4.2 The lifetime of a service instance

`MapGrpcService<T>` registers the service with a *transient* lifetime by default — a new `CounterService` instance is created for every gRPC call. If you want a different lifetime, register explicitly:

```csharp
builder.Services.AddSingleton<CounterService>();
// then MapGrpcService<CounterService>() resolves the singleton
```

Singleton is appropriate when the service holds no per-call state and the dependencies are themselves thread-safe. Transient is the safer default.

## 5. The C# client: `Grpc.Net.Client`

The client side. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/client>.

### 5.1 Building a channel

A `GrpcChannel` is a long-lived object representing a connection to a gRPC server. **Create one per server you talk to; reuse it for the lifetime of your process.** Creating a new channel per call is a serious anti-pattern — each channel sets up a TLS handshake and HTTP/2 connection, and discarding it per call is the gRPC equivalent of new `HttpClient` per request.

```csharp
using var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
{
    MaxReceiveMessageSize = 4 * 1024 * 1024,
    MaxSendMessageSize = 4 * 1024 * 1024,
});
```

For a console app, `using` at the call site is fine — the channel lives as long as the program. For a long-lived ASP.NET Core service that talks to another service, register the channel and the client in DI (see Section 5.4 below).

### 5.2 Instantiating a client

`Counter.CounterClient` is the generated client class. It takes a `ChannelBase` in its constructor:

```csharp
var client = new Counter.CounterClient(channel);
```

The client is cheap — instantiate it whenever you need it; the underlying channel does the work.

### 5.3 Call options

Every generated client method takes an optional `CallOptions` (or expanded `deadline:`/`cancellationToken:`/`headers:` arguments):

```csharp
var deadline = DateTime.UtcNow.AddSeconds(5);
var headers = new Metadata { { "x-correlation-id", Guid.NewGuid().ToString() } };

var reply = await client.GetAsync(
    new GetRequest { Name = "alice" },
    headers: headers,
    deadline: deadline,
    cancellationToken: cts.Token);
```

`deadline` is an absolute UTC `DateTime`. The client computes `deadline - DateTime.UtcNow` and sends the difference in the `grpc-timeout` header. The server reads it, sets `ServerCallContext.Deadline`, and `ServerCallContext.CancellationToken` will fire when the deadline expires.

`cancellationToken` is a *local* cancellation source — it cancels the call from the client side but does not propagate to the server. (The deadline does propagate; the cancellation token's cancellation manifests as `RpcException` with `StatusCode.Cancelled`.)

`headers` is gRPC metadata — see Lecture 3 for the full discussion of metadata, trailers, and the `-bin` suffix.

### 5.4 The client factory: `Grpc.Net.ClientFactory`

For services-calling-services, the idiomatic pattern is `Grpc.Net.ClientFactory`, which integrates with `IHttpClientFactory` to give you channel pooling, DI, and the `IHttpClientFactory` lifecycle:

```csharp
// In Program.cs
builder.Services.AddGrpcClient<Counter.CounterClient>(o =>
{
    o.Address = new Uri("https://counter.internal:443");
});

// In a service
public sealed class OrderService
{
    private readonly Counter.CounterClient _counter;
    public OrderService(Counter.CounterClient counter) => _counter = counter;

    public async Task<long> GetCounterAsync(string name, CancellationToken ct)
    {
        var reply = await _counter.GetAsync(new GetRequest { Name = name }, cancellationToken: ct);
        return reply.Value;
    }
}
```

This is the production-shaped pattern. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory>.

## 6. Code-first gRPC (a sidebar)

The mainline gRPC story is **proto-first**: you write a `.proto`, generate types in every language. The variant we will mention but not adopt is **code-first gRPC**, where you write C# interfaces decorated with attributes and a library (Marc Gravell's `protobuf-net.Grpc`, at <https://protobuf-net.github.io/protobuf-net.Grpc/>) generates the schema and the bindings at runtime.

Code-first looks like:

```csharp
[ServiceContract]
public interface ICounter
{
    [OperationContract]
    Task<GetResponse> GetAsync(GetRequest request, CallContext context = default);

    [OperationContract]
    IAsyncEnumerable<CounterEvent> SubscribeAsync(SubscribeRequest request, CallContext context = default);
}
```

The advantage is that there is no `.proto` file and no MSBuild generator step — the interface *is* the schema. The disadvantage is that **only .NET clients can call the service**. A Python or Go client cannot bind against a C# interface; it needs a `.proto`. The "code-first wins" path leads, eventually, to "we need a Python client" and then to "we need to retrofit a `.proto`" and then to "the schema we just retrofitted does not match the on-wire reality our existing C# clients expect."

The senior heuristic: **code-first gRPC is appropriate when, and only when, you control every client and every client is .NET, forever**. The first time anyone says "could we expose this to the Python team?", you have a migration ahead of you. In every other case, write the `.proto`.

We will not use code-first this week. The mini-project is proto-first. The sidebar exists so you can recognise the pattern when you encounter it in someone else's codebase.

## 7. The cross-language story: a Python client over the same proto

The whole point of `.proto` is that the schema is the source of truth, and any language with a gRPC binding can generate from it. To illustrate, take the same `counter.proto`:

```proto
syntax = "proto3";

package crunch.counter.v1;
option csharp_namespace = "Crunch.Counter.V1";

message GetRequest { string name = 1; }
message GetResponse { int64 value = 1; }

service Counter {
  rpc Get(GetRequest) returns (GetResponse);
}
```

The Python toolchain has two pip packages:

```bash
python3 -m pip install grpcio grpcio-tools
```

To generate Python bindings:

```bash
python3 -m grpc_tools.protoc \
    --proto_path=./protos \
    --python_out=./gen \
    --grpc_python_out=./gen \
    ./protos/counter.proto
```

This emits two files: `gen/counter_pb2.py` (the message types) and `gen/counter_pb2_grpc.py` (the service stubs). The Python client:

```python
import grpc
import counter_pb2
import counter_pb2_grpc

def main() -> None:
    with grpc.secure_channel("localhost:5001", grpc.ssl_channel_credentials()) as channel:
        stub = counter_pb2_grpc.CounterStub(channel)
        reply = stub.Get(counter_pb2.GetRequest(name="alice"), timeout=2.0)
        print(f"value = {reply.value}")

if __name__ == "__main__":
    main()
```

Three things to notice. First, the import is the package-and-service name (`CounterStub`), the same as the C# client. Second, the request and response types are the same (`GetRequest`, `GetResponse`), with Python-idiomatic naming (`name=`, `reply.value`). Third, the `timeout=2.0` argument is the Python form of the deadline — the same wire bytes go to the server.

The cross-language wire bytes are *identical*. If you Wireshark the C# call and the Python call, the payloads on the HTTP/2 stream are byte-for-byte the same. That is the promise gRPC makes; the mini-project proves it.

For server-streaming from Python:

```python
for ev in stub.Subscribe(counter_pb2.SubscribeRequest(name="alice"), timeout=30.0):
    print(f"event kind={ev.kind} value={ev.new_value}")
```

Python uses a regular `for` loop over the iterator returned by the streaming method — the language idiom is different but the semantic is the same as C#'s `await foreach`.

## 8. Common mistakes to avoid

A short list, from experience:

- **Constructing a fresh `GrpcChannel` per call.** Same anti-pattern as fresh `HttpClient` per request. Make the channel long-lived.
- **Forgetting `using` on a streaming call.** Leaks an HTTP/2 stream per early-exit.
- **Calling the blocking overload on the client.** `client.Get(req)` is a sync wrapper around `GetAsync` and will deadlock under the same conditions as Week 8's "block on async code." Always use the async overload.
- **Returning `null` from a unary server method.** The serialiser will throw an `InvalidOperationException`; gRPC has no concept of "null response." Return an empty message instead.
- **Returning enormous payloads.** The default max message size is 4 MiB on both ends. If you need bigger, raise it explicitly on both server and client; do not blow past it accidentally.
- **Mixing `await foreach` and manual `MoveNextAsync` on a stream reader.** Pick one — they share state.
- **Forgetting `CompleteAsync()` on a client-streaming call.** The server's `await foreach` will block until the deadline.
- **Catching `Exception` on the server.** `RpcException` is special; if you catch it and rethrow as a different exception, the wire status is wrong. Let `RpcException` propagate; catch *non*-`RpcException` and translate explicitly.

## 9. Wrap-up — what to remember

Three sentences:

1. gRPC is HTTP/2 + protobuf + a fixed call-type model. The call types are not interchangeable; pick one at design time.
2. The C# server is a class inheriting from `Service.ServiceBase`; the C# client is a generated class taking a `GrpcChannel`. Long-lived channels, transient clients.
3. The cross-language promise is the schema. The same `.proto` generates C#, Python, Go, Swift, TypeScript clients with byte-identical wire formats.

Read the gRPC concepts overview at <https://grpc.io/docs/what-is-grpc/core-concepts/> and Microsoft Learn's gRPC services with ASP.NET Core at <https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore> before tomorrow. The next lecture covers the *operational* concerns: deadlines, cancellation, error mapping, interceptors, TLS — the wiring that turns a working gRPC service into a *production* gRPC service.
