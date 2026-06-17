# Lecture 3 — Deadlines, Cancellation, Error Mapping, Interceptors, and TLS

> **Time:** 2 hours. **Prerequisites:** Lectures 1 and 2 of this week, Week 8 on `CancellationToken` propagation. **Citations:** grpc.io's deadlines guide at <https://grpc.io/docs/guides/deadlines/>, the gRPC error guide at <https://grpc.io/docs/guides/error/>, the gRPC status-code reference at <https://grpc.github.io/grpc/core/md_doc_statuscodes.html>, Microsoft Learn's deadlines and cancellation chapter at <https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation>, and the gRPC interceptors documentation at <https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors>.

## 1. Why this lecture exists

Lectures 1 and 2 made you literate in the *shape* of gRPC: schemas, generated code, the four call types, the C# server and client. That is the surface. This lecture covers the *operational* concerns — the wiring that distinguishes a gRPC service that survives contact with a production network from one that does not.

Production networks fail. Calls take too long. Clients hang up. Servers run out of capacity. TLS certificates expire. The work this lecture covers is how gRPC handles each of these — and in particular, how the .NET binding gives you knobs to control behaviour at each failure boundary. By the end of this lecture you should be able to look at any RPC method in your codebase and answer four questions instantly:

1. What is the deadline budget at the call site?
2. What `StatusCode` does this method return when each thing goes wrong?
3. Are there interceptors logging the call's latency and correlation-id?
4. Is the channel TLS-protected, and what is the certificate validation policy?

Each of these is a *senior* gRPC skill. The schema is the obvious thing; this lecture is the less-obvious thing.

## 2. Deadlines

### 2.1 What a deadline is

A gRPC deadline is an **absolute UTC timestamp** by which the call must complete. The wire form is the `grpc-timeout` HTTP/2 header, a string like `2S` (2 seconds), `500m` (500 milliseconds), `30M` (30 minutes), or `5H` (5 hours). The client sets the deadline; the server reads the header and computes "this call has X time left."

Three properties make deadlines load-bearing:

1. **Deadlines are absolute, not relative.** A client setting "5 seconds from now" sends "5S" (which the server reads as "5 seconds from when I received this"); but the *meaning* is "the call must finish by clock-time T+5s," and any time spent in network transit, queueing, or load-balancer routing eats into that budget. The server's view of "how much time is left" can be smaller than the client's intended 5 seconds.
2. **Deadlines propagate.** When the server makes outbound gRPC calls, it should pass its `ServerCallContext.Deadline` into the outbound `CallOptions.Deadline`. The downstream service sees the same deadline; the budget shrinks naturally as time passes.
3. **Deadlines fire as cancellation.** The server's `ServerCallContext.CancellationToken` is cancelled when the deadline expires. If the server's code respects the token (and it must — see Week 8), the call aborts cleanly and `StatusCode.DeadlineExceeded` is sent back to the client.

### 2.2 Setting a deadline on the client

```csharp
var deadline = DateTime.UtcNow.AddSeconds(2);

try
{
    var reply = await client.GetAsync(
        new GetRequest { Name = "alice" },
        deadline: deadline);
    Console.WriteLine($"value = {reply.Value}");
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
{
    Console.WriteLine("Call exceeded its deadline.");
}
```

Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/deadlines-cancellation>. The `deadline:` parameter is an absolute UTC `DateTime`. The library computes `deadline - DateTime.UtcNow` and encodes the difference as `grpc-timeout`.

**A common bug:** using a local `DateTime` instead of `DateTime.UtcNow`. The library will still send the right wire value if `DateTime.Kind == DateTimeKind.Utc`, but if `Kind == Unspecified` (the default for `new DateTime(2026, 5, 14, ...)`), the conversion is wrong by the local-machine timezone offset. **Always `DateTime.UtcNow.AddX(...)`. Always.**

### 2.3 Reading a deadline on the server

```csharp
public override async Task<GetResponse> Get(GetRequest request, ServerCallContext context)
{
    var ct = context.CancellationToken;
    var deadline = context.Deadline;  // DateTime in UTC; DateTime.MaxValue if no deadline

    if (deadline != DateTime.MaxValue)
    {
        var remaining = deadline - DateTime.UtcNow;
        _logger.LogInformation("Get for {Name} has {Remaining} ms left", request.Name, remaining.TotalMilliseconds);
    }

    var value = await _store.ReadAsync(request.Name, ct);
    return new GetResponse { Value = value };
}
```

`context.Deadline` is the absolute UTC `DateTime`. `context.CancellationToken` is what you propagate into downstream awaits. If the client did not send a deadline, `context.Deadline` is `DateTime.MaxValue` and the token never fires from a deadline.

### 2.4 Deadline propagation across services

The propagation pattern. Suppose service A calls service B, which calls service C:

```csharp
// In service A's gRPC method
public override async Task<AResponse> DoA(ARequest request, ServerCallContext context)
{
    var deadline = context.Deadline;
    var ct = context.CancellationToken;

    // Propagate the same deadline and token to the downstream call
    var bReply = await _bClient.DoBAsync(
        new BRequest { ... },
        deadline: deadline,
        cancellationToken: ct);

    return new AResponse { Body = bReply.Body };
}
```

This is the propagation rule: **every outbound gRPC call from a server context takes the inbound context's `Deadline` and `CancellationToken`**. The library will not do this for you; you have to write it. The `Grpc.Net.ClientFactory` integration has an option (`PropagateDeadline = true`) that does it implicitly, citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/clientfactory>:

```csharp
builder.Services
    .AddGrpcClient<B.BClient>(o => o.Address = new Uri("https://b.internal"))
    .EnableCallContextPropagation();
```

`EnableCallContextPropagation` reads the ambient `ServerCallContext` (when present), and uses it to populate `deadline` and `cancellationToken` on every outbound call automatically. **Use this in production.** Forgetting to propagate is a quiet bug — the outer call deadlines correctly, but the inner call runs past it, wasting capacity.

### 2.5 Choosing a deadline value

The choice is workload-specific, but the senior heuristic is **two principles**:

- **Set a deadline on every call.** No deadline means "wait forever," which is a production hazard: a slow downstream can pile up unfinished calls indefinitely.
- **The deadline should be shorter than the SLO of the call that triggered it.** If your service has a 1-second p99 SLO, and inside that you call three downstream services, each downstream call's deadline should be a fraction of 1 second (300 ms is typical for the fan-out case).

For interactive requests, deadlines in the 200ms–2s range are typical. For background jobs, 30s–5min. For long-lived streaming subscriptions, deadlines of 10+ minutes or no deadline at all. Use your operational judgment; document the choice.

## 3. Cancellation

Cancellation is the *client-side* mirror of deadlines: a way for the client to say "I have given up; you can stop." The wire form is the HTTP/2 `RST_STREAM` frame with code `CANCEL`. The server sees its `ServerCallContext.CancellationToken` fire and is expected to abort.

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var reply = await client.GetAsync(
        new GetRequest { Name = "alice" },
        cancellationToken: cts.Token);
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
{
    // The local cancellation token fired before the deadline.
}
```

A subtle point: **a cancelled call surfaces as `RpcException` with `StatusCode.Cancelled`, not as `OperationCanceledException`**. This is a gRPC convention — every failure on the wire is an `RpcException` with a status code. If your code has `catch (OperationCanceledException)` around a gRPC call, the handler will not fire on cancellation; you need `catch (RpcException) when (ex.StatusCode == StatusCode.Cancelled)`.

The interaction with deadlines: if both a deadline and a cancellation token are set, whichever fires first wins. The client distinguishes them in the status code (`DeadlineExceeded` vs `Cancelled`), and your `catch` should distinguish them too.

## 4. Error mapping: `RpcException` and the 17 status codes

The gRPC error model is **structured exceptions over the wire**, expressed as a status code and a string message. The wire form is the `grpc-status` HTTP/2 trailer (an integer 0–16) and the optional `grpc-message` trailer (a percent-encoded string). The client surface is `RpcException`:

```csharp
public class RpcException : Exception
{
    public Status Status { get; }       // wraps StatusCode + Detail
    public Metadata Trailers { get; }   // any custom trailers
    public StatusCode StatusCode => Status.StatusCode;
}
```

### 4.1 The 17 status codes

Citation: <https://grpc.github.io/grpc/core/md_doc_statuscodes.html>. The status codes and their canonical meanings:

| Code  | Name                | Use when                                                                          |
|------:|---------------------|-----------------------------------------------------------------------------------|
| 0     | OK                  | Success. Servers do not throw this; the wire sends it automatically on no-error.  |
| 1     | Cancelled           | The call was cancelled by the client. Mirror of the client-side cancellation.     |
| 2     | Unknown             | An unexpected internal error you cannot categorise. Avoid; almost always wrong.   |
| 3     | InvalidArgument     | The request was malformed. "Field X is required" or "value Y is out of range."     |
| 4     | DeadlineExceeded    | The deadline expired before completion. Sent automatically; rarely thrown by you. |
| 5     | NotFound            | The referenced entity (by id, by name) does not exist.                            |
| 6     | AlreadyExists       | A create operation failed because the entity already exists.                      |
| 7     | PermissionDenied    | Authn succeeded but authz failed. "You are X but X cannot do Y."                  |
| 8     | ResourceExhausted   | Quota, rate limit, capacity ceiling hit.                                          |
| 9     | FailedPrecondition  | The system state forbids the operation right now. "Cannot delete; has dependents." |
| 10    | Aborted             | Concurrent-modification conflict. Use for optimistic-locking style failures.      |
| 11    | OutOfRange          | A value crossed a boundary the operation cannot honour. "Page > last page."       |
| 12    | Unimplemented       | The RPC is recognised but not implemented.                                        |
| 13    | Internal            | This is our bug. Use sparingly; prefer a more specific code where possible.       |
| 14    | Unavailable         | Transient failure; downstream is down. Clients should retry with backoff.         |
| 15    | DataLoss            | Unrecoverable data corruption. Very rare.                                         |
| 16    | Unauthenticated     | No or invalid credentials. Use before "PermissionDenied" — "who" before "may."     |

### 4.2 Choosing the right code

The single most common error is to default to `Internal` (or `Unknown`) for everything. This is the gRPC equivalent of `500 Internal Server Error` for every HTTP failure — it tells the client nothing and the operator nothing. The senior discipline is to map each *category* of failure to its specific code.

A short decision matrix:

- **"You sent me bad input"** → `InvalidArgument` (almost always) or `OutOfRange` (when there is a specific boundary), `FailedPrecondition` (when the input is fine in isolation but the system can't honour it).
- **"You sent me no credentials"** → `Unauthenticated`.
- **"You sent me credentials but you can't do this"** → `PermissionDenied`.
- **"I don't know what you're asking for"** → `NotFound`.
- **"I won't make a duplicate of what you're asking me to create"** → `AlreadyExists`.
- **"I am out of capacity"** → `ResourceExhausted`.
- **"My downstream is down"** → `Unavailable`. **This is the only retry-safe failure code by convention.** Clients see `Unavailable` and retry with backoff; they should not retry on `Internal`.
- **"My state has changed under you"** → `Aborted`. The optimistic-concurrency code.
- **"This is our bug"** → `Internal`. The last resort. If you reach for `Internal`, write a follow-up to make the underlying bug fix produce a more specific code in v2.

### 4.3 Throwing an `RpcException` correctly

```csharp
public override Task<GetResponse> Get(GetRequest request, ServerCallContext context)
{
    if (string.IsNullOrWhiteSpace(request.Name))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "name must be non-empty"));

    if (!_store.TryRead(request.Name, out var value))
        throw new RpcException(new Status(StatusCode.NotFound, $"counter '{request.Name}' does not exist"));

    return Task.FromResult(new GetResponse { Value = value });
}
```

Two rules:

1. **Construct the status with both code and message.** The code is for code; the message is for humans. Default the message; never leave it empty in production paths.
2. **Do not throw arbitrary exceptions from server methods.** A raw `ArgumentException` or `InvalidOperationException` is caught by the gRPC framework and translated to `StatusCode.Unknown` (or `Internal` if `EnableDetailedErrors = true`) with the exception's `.ToString()` as the message — which leaks stack traces to clients. Always translate to `RpcException` explicitly.

### 4.4 `EnableDetailedErrors`

```csharp
builder.Services.AddGrpc(o => o.EnableDetailedErrors = true);
```

This flag tells the server to include the full exception details (type, message, stack trace) in the response when an *unhandled* exception escapes a service method. Useful in development; **never enable in production** — it leaks internal structure. The right pattern is to leave `EnableDetailedErrors = false` and explicitly throw `RpcException` for every failure path.

### 4.5 Client-side error handling

```csharp
try
{
    var reply = await client.IncrementAsync(req, deadline: DateTime.UtcNow.AddSeconds(2));
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
{
    // Client-side bug: we sent bad input. Surface to caller; do not retry.
    throw new ArgumentException(ex.Status.Detail, ex);
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
{
    // Downstream is down. Retry with backoff.
    return await RetryAsync(...);
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
{
    // We exceeded the budget. Decide: surface, partial result, or retry-with-more-time.
    throw new TimeoutException("Counter increment exceeded its deadline.", ex);
}
catch (RpcException ex)
{
    _logger.LogError(ex, "Unexpected RPC failure: {Status}", ex.Status);
    throw;
}
```

The `when` clauses make the dispatch readable. Each branch handles one status-code class with the correct response. The default branch logs and rethrows.

## 5. Interceptors

Interceptors are the gRPC equivalent of ASP.NET Core middleware: a wedge in the request pipeline where you can run code before, after, or around every RPC. Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/interceptors>.

There are two flavours: **server interceptors** (run inside the gRPC server pipeline) and **client interceptors** (run inside the gRPC client pipeline). Both subclass `Grpc.Core.Interceptors.Interceptor`, and both override the four-call-type-specific methods.

### 5.1 A server interceptor: logging

```csharp
#nullable enable

using Grpc.Core;
using Grpc.Core.Interceptors;
using System.Diagnostics;

namespace Crunch.Counter.Server;

public sealed class LoggingServerInterceptor : Interceptor
{
    private readonly ILogger<LoggingServerInterceptor> _logger;
    public LoggingServerInterceptor(ILogger<LoggingServerInterceptor> logger) => _logger = logger;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await continuation(request, context);
            sw.Stop();
            _logger.LogInformation("RPC {Method} succeeded in {Ms} ms",
                context.Method, sw.ElapsedMilliseconds);
            return response;
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogWarning("RPC {Method} failed with {Status} in {Ms} ms",
                context.Method, ex.StatusCode, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await continuation(request, responseStream, context);
            sw.Stop();
            _logger.LogInformation("Server-streaming RPC {Method} completed in {Ms} ms",
                context.Method, sw.ElapsedMilliseconds);
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogWarning("Server-streaming RPC {Method} failed with {Status} in {Ms} ms",
                context.Method, ex.StatusCode, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
```

Registration in `Program.cs`:

```csharp
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<LoggingServerInterceptor>();
});
```

Now every RPC is logged with its method name, status, and latency. This is the minimum-viable observability for a production gRPC service.

### 5.2 A client interceptor: correlation-id propagation

```csharp
#nullable enable

using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Crunch.Counter.Client;

public sealed class CorrelationIdInterceptor : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = context.Options.Headers ?? new Metadata();
        if (!headers.Any(h => h.Key == "x-correlation-id"))
        {
            headers.Add("x-correlation-id", Guid.NewGuid().ToString());
        }
        var newOptions = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, newOptions);
        return continuation(request, newContext);
    }
}
```

Registration on the client channel:

```csharp
var channel = GrpcChannel.ForAddress("https://localhost:5001");
var invoker = channel.Intercept(new CorrelationIdInterceptor());
var client = new Counter.CounterClient(invoker);
```

Now every outbound call carries an `x-correlation-id` header. A matching server interceptor reads `context.RequestHeaders.GetValue("x-correlation-id")` and threads it into the logger scope so every log line in the call chain carries the same id. This is the gRPC version of distributed tracing's correlation; OpenTelemetry's gRPC instrumentation does the same job at a different layer.

### 5.3 Interceptor ordering

When you register multiple interceptors, they execute in the order registered, *wrapped around* the actual handler. The first-registered interceptor's "before" code runs first; the last-registered interceptor's "after" code runs first. This is identical to ASP.NET Core middleware ordering.

```csharp
builder.Services.AddGrpc(o =>
{
    o.Interceptors.Add<LoggingInterceptor>();           // outermost
    o.Interceptors.Add<AuthInterceptor>();              // middle
    o.Interceptors.Add<ValidationInterceptor>();        // innermost
});
```

The call flow is: Logging-before → Auth-before → Validation-before → handler → Validation-after → Auth-after → Logging-after. Put telemetry on the outside; put validation closest to the handler.

## 6. Metadata and trailers

**Metadata** is the gRPC name for HTTP/2 headers and trailers carried with an RPC. There are three slots:

- **Request headers** (sent by client, available to server as `context.RequestHeaders`).
- **Response headers** (sent by server *before* the first response message; available to client via `call.ResponseHeadersAsync`).
- **Response trailers** (sent by server *after* the last response message; available to client via `call.GetTrailers()`).

A `Metadata` is a list of `Metadata.Entry` instances; each entry has a key (lowercase ASCII), a value, and a flag indicating whether the value is text (`string`) or binary (`byte[]`). Binary entries must have keys ending in `-bin`:

```csharp
var headers = new Metadata
{
    { "x-correlation-id", "c0ffee..." },
    { "x-tenant-id", "tenant-42" },
    { "x-signature-bin", new byte[] { 0x01, 0x02, 0x03 } },  // binary; key ends -bin
};
```

The HTTP/2 reserved pseudo-headers (`:method`, `:path`, etc) are *not* in the metadata; the gRPC framework owns them.

Trailers are useful for status information that depends on the call's outcome — for example, a server might emit a `x-rate-remaining` trailer indicating how much of the client's quota is left after this call. Read on the client side with:

```csharp
using var call = client.IncrementAsync(req);
var reply = await call.ResponseAsync;
var trailers = call.GetTrailers();
var rateRemaining = trailers.GetValue("x-rate-remaining");
```

## 7. TLS and channel credentials

Production gRPC runs over TLS. The .NET binding makes this transparent in the common case (`GrpcChannel.ForAddress("https://...")` Just Works against a server with a valid certificate) and gives you knobs for the uncommon cases.

### 7.1 Development: the `dotnet dev-certs` flow

In Week 2 you ran `dotnet dev-certs https --trust` once. That installed a self-signed certificate into your machine's trust store. ASP.NET Core uses it automatically for `https://localhost:5001` and `https://localhost:7XXX`. The C# gRPC client trusts it automatically because it is in the trust store. No code changes; this is the default development experience.

### 7.2 Production: a real certificate

In production, your server runs behind Kestrel (or behind a reverse proxy that terminates TLS) with a CA-signed certificate. `GrpcChannel.ForAddress("https://counter.example.com")` does the right thing: it negotiates TLS, validates the server certificate against the system trust store, and establishes the connection. No special code on the client side.

### 7.3 Custom certificate validation

When you have a custom CA, an internal-only PKI, or you want to pin a certificate, configure the `HttpHandler` on the channel:

```csharp
var handler = new SocketsHttpHandler
{
    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
    {
        RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
        {
            // Validate against your internal CA, pin by thumbprint, etc.
            return cert is not null && cert.GetCertHashString() == "EXPECTED_THUMBPRINT";
        }
    }
};

var channel = GrpcChannel.ForAddress("https://counter.internal", new GrpcChannelOptions
{
    HttpHandler = handler
});
```

This is the hook for "I do not want to trust the system CA store" — common for service-mesh deployments where every service has a per-mesh CA.

### 7.4 Client certificates (mutual TLS)

For mTLS, where the client also authenticates with a certificate:

```csharp
var clientCert = X509Certificate2.CreateFromPemFile("client.crt", "client.key");

var handler = new SocketsHttpHandler();
handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCert };

var channel = GrpcChannel.ForAddress("https://counter.internal", new GrpcChannelOptions
{
    HttpHandler = handler
});
```

The server side must request a client certificate (configured in Kestrel options), validate it, and use it for authorization. This is the gold-standard authentication pattern in service meshes.

### 7.5 The `h2c` opt-in (HTTP/2 over cleartext)

Some local-only or sidecar deployments run gRPC over plain HTTP/2, no TLS. The .NET binding requires an explicit opt-in:

```csharp
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var channel = GrpcChannel.ForAddress("http://localhost:5000");  // note: http, not https
```

The `AppContext.SetSwitch` line is required because the default `SocketsHttpHandler` policy is "HTTP/2 only over TLS." Without the switch, the handshake will fail and the channel will fall back to HTTP/1.1, which gRPC cannot use. **Set the switch in development if you need it. Do not set it in production.** Cleartext gRPC is fine for sidecars on localhost; it is never fine for cross-host traffic.

### 7.6 The `Authority` field

The `:authority` HTTP/2 pseudo-header is what gRPC uses for virtual-hosting on a load balancer. By default it is the hostname from the URL. If you need to override it — typically because you are talking to a server that hosts multiple services on the same IP and routes by authority — set it on the channel:

```csharp
var channel = GrpcChannel.ForAddress("https://shared-lb.internal", new GrpcChannelOptions
{
    // (No direct property; set via HttpHandler.RequestVersionPolicy or via per-call options.)
});

// Per-call:
var headers = new Metadata { { ":authority", "counter.specific.internal" } };
```

In practice the default is right 99% of the time. The flag exists for the 1%.

## 8. Wrap-up — the operational checklist

For every gRPC method you ship this week:

- [ ] The client sets a `deadline` on every call.
- [ ] The server reads `context.CancellationToken` and propagates it into every internal `await`.
- [ ] Every outbound call from the server uses `EnableCallContextPropagation` or manually passes `deadline` and `cancellationToken`.
- [ ] Failure paths throw `RpcException(new Status(StatusCode.X, "message"))` with the *most specific* status code that applies.
- [ ] The server has a logging interceptor recording method, status, latency.
- [ ] The client has a correlation-id interceptor; the server reads it and threads it into the logger scope.
- [ ] The channel is TLS-protected in production; `h2c` only in tested sidecar setups.
- [ ] `EnableDetailedErrors` is `false` in production builds.

The next two days are exercises and the challenge against this checklist. Friday is the mini-project: a counter service satisfying every item.
