// Exercise 3 — Deadlines, cancellation propagation, and interceptors.
//
// This file contains four source units, bracketed below:
//   1. SlowService.cs            — server-side gRPC implementation
//   2. LoggingServerInterceptor.cs — server-side interceptor
//   3. CorrelationIdInterceptor.cs — client-side interceptor
//   4. Program.cs                — client driver
//
// Cut each and place in the appropriate project. The server uses
// builder.Services.AddGrpc(o => o.Interceptors.Add<LoggingServerInterceptor>())
// to register its interceptor.

// =====================================================================
// PART 1 — Server: src/Ex03.Server/Services/SlowService.cs
// =====================================================================

#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Crunch.Slow.V1;
using Grpc.Core;

namespace Ex03.Server.Services;

public sealed class SlowService : SlowService.SlowServiceBase
{
    private readonly ILogger<SlowService> _logger;

    public SlowService(ILogger<SlowService> logger) => _logger = logger;

    public override async Task<SlowResponse> DoWork(SlowRequest request, ServerCallContext context)
    {
        if (request.WorkMs < 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "work_ms must be >= 0"));
        if (request.WorkMs > 5000)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "work_ms must be <= 5000"));

        var ct = context.CancellationToken;
        var sw = Stopwatch.StartNew();

        try
        {
            await Task.Delay(request.WorkMs, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogInformation("DoWork cancelled after {Ms} ms (label={Label})",
                sw.ElapsedMilliseconds, request.Label);
            // Propagate as the appropriate status code. If the deadline fired,
            // the framework will translate this to DeadlineExceeded for us.
            // If the client cancelled, it surfaces as Cancelled.
            throw new RpcException(new Status(StatusCode.Cancelled, "work cancelled"));
        }

        sw.Stop();
        return new SlowResponse
        {
            Label = request.Label,
            WorkMsActual = (int)sw.ElapsedMilliseconds,
        };
    }
}

// =====================================================================
// PART 2 — Server interceptor: src/Ex03.Server/Interceptors/LoggingServerInterceptor.cs
// =====================================================================

/*
#nullable enable

using System.Diagnostics;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ex03.Server.Interceptors;

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
        var correlationId = context.RequestHeaders.GetValue("x-correlation-id") ?? "(none)";
        try
        {
            var response = await continuation(request, context);
            sw.Stop();
            _logger.LogInformation(
                "RPC {Method} OK in {Ms} ms (correlation-id={Cid})",
                context.Method, sw.ElapsedMilliseconds, correlationId);
            return response;
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogWarning(
                "RPC {Method} failed with {Status} in {Ms} ms (correlation-id={Cid})",
                context.Method, ex.StatusCode, sw.ElapsedMilliseconds, correlationId);
            throw;
        }
    }
}
*/

// =====================================================================
// PART 3 — Client interceptor: src/Ex03.Client/Interceptors/CorrelationIdInterceptor.cs
// =====================================================================

/*
#nullable enable

using System;
using System.Linq;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ex03.Client.Interceptors;

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
            headers.Add("x-correlation-id", Guid.NewGuid().ToString("N").Substring(0, 8));
        }
        var newOptions = context.Options.WithHeaders(headers);
        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, newOptions);
        return continuation(request, newContext);
    }
}
*/

// =====================================================================
// PART 4 — Client driver: src/Ex03.Client/Program.cs
// =====================================================================

/*
#nullable enable

using System;
using System.Threading.Tasks;
using Crunch.Slow.V1;
using Ex03.Client.Interceptors;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace Ex03.Client;

public static class Program
{
    private const string ServerAddress = "https://localhost:5001";

    public static async Task Main()
    {
        Console.WriteLine($"Exercise 3 — deadlines and interceptors against {ServerAddress}");
        Console.WriteLine(new string('-', 60));

        using var channel = GrpcChannel.ForAddress(ServerAddress);
        var invoker = channel.Intercept(new CorrelationIdInterceptor());
        var client = new SlowService.SlowServiceClient(invoker);

        // (a) Short deadline against a long work time → DeadlineExceeded.
        Console.WriteLine();
        Console.WriteLine("[a] 300 ms deadline against 1000 ms work — expect DeadlineExceeded");
        try
        {
            var reply = await client.DoWorkAsync(
                new SlowRequest { WorkMs = 1000, Label = "deadline-short" },
                deadline: DateTime.UtcNow.AddMilliseconds(300));
            Console.WriteLine($"    unexpected success: actual={reply.WorkMsActual}");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            Console.WriteLine($"    got DeadlineExceeded as expected: {ex.Status.Detail}");
        }

        // (b) Comfortable deadline → success.
        Console.WriteLine();
        Console.WriteLine("[b] 2000 ms deadline against 1000 ms work — expect success");
        var ok = await client.DoWorkAsync(
            new SlowRequest { WorkMs = 1000, Label = "deadline-comfortable" },
            deadline: DateTime.UtcNow.AddMilliseconds(2000));
        Console.WriteLine($"    success: actual={ok.WorkMsActual} label={ok.Label}");

        // (c) Out-of-range work_ms → InvalidArgument.
        Console.WriteLine();
        Console.WriteLine("[c] work_ms=6000 — expect InvalidArgument");
        try
        {
            await client.DoWorkAsync(
                new SlowRequest { WorkMs = 6000, Label = "too-big" },
                deadline: DateTime.UtcNow.AddSeconds(5));
            Console.WriteLine("    unexpected success");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            Console.WriteLine($"    got InvalidArgument as expected: {ex.Status.Detail}");
        }

        Console.WriteLine();
        Console.WriteLine("Done. Inspect the server console — every call should be logged");
        Console.WriteLine("with a correlation-id and an elapsed-ms value.");
    }
}
*/

// HINTS (read after a serious attempt):
//
// 1. The server's DoWork catches OperationCanceledException because Task.Delay
//    throws that on the linked token. The framework will see the deadline
//    fired and translate the eventual exception path to StatusCode.DeadlineExceeded.
//    Re-throwing as RpcException(Cancelled) is for the explicit-client-cancel
//    case; the deadline case overrides it.
//
// 2. The LoggingServerInterceptor registration in Program.cs:
//
//      builder.Services.AddGrpc(o => o.Interceptors.Add<LoggingServerInterceptor>());
//      builder.Services.AddScoped<LoggingServerInterceptor>();
//
//    Registering the interceptor type in DI is required because Interceptor
//    instances are resolved per call.
//
// 3. context.RequestHeaders.GetValue("x-correlation-id") returns null if the
//    header is absent. Default to "(none)" or generate a server-side id and
//    record both.
//
// 4. The client interceptor's `context.Options.WithHeaders(headers)` returns
//    a fresh CallOptions; the original is immutable. Construct a fresh
//    ClientInterceptorContext with the new options before delegating to
//    `continuation`.
//
// 5. If the server fails to propagate context.CancellationToken into
//    Task.Delay, the call will run to completion and the server will write
//    a response into a stream the client has already closed. The framework
//    swallows the resulting IOException and reports DeadlineExceeded to the
//    client anyway — but the server has wasted the full work_ms of CPU/wallclock.
//    This is the gRPC equivalent of Week 8's "respect your cancellation token"
//    rule.
