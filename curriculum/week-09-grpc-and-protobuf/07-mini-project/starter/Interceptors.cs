// Interceptors.cs — both the server-side logging interceptor and the
// client-side correlation-id interceptor for the Crunch Counter mini-project.
//
// Split into two files in your actual project:
//   src/CrunchCounter.Server/Interceptors/LoggingServerInterceptor.cs
//   src/CrunchCounter.Client/Interceptors/CorrelationIdInterceptor.cs

// =====================================================================
// PART 1 — Server: LoggingServerInterceptor
// =====================================================================

#nullable enable

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace CrunchCounter.Server.Interceptors;

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
        var cid = context.RequestHeaders.GetValue("x-correlation-id") ?? "-";
        try
        {
            var response = await continuation(request, context);
            sw.Stop();
            _logger.LogInformation("{Method} OK in {Ms} ms cid={Cid}",
                context.Method, sw.ElapsedMilliseconds, cid);
            return response;
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogWarning("{Method} {Status} in {Ms} ms cid={Cid} detail={Detail}",
                context.Method, ex.StatusCode, sw.ElapsedMilliseconds, cid, ex.Status.Detail);
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
        var cid = context.RequestHeaders.GetValue("x-correlation-id") ?? "-";
        try
        {
            await continuation(request, responseStream, context);
            sw.Stop();
            _logger.LogInformation("{Method} stream finished in {Ms} ms cid={Cid}",
                context.Method, sw.ElapsedMilliseconds, cid);
        }
        catch (RpcException ex)
        {
            sw.Stop();
            _logger.LogWarning("{Method} stream {Status} in {Ms} ms cid={Cid}",
                context.Method, ex.StatusCode, sw.ElapsedMilliseconds, cid);
            throw;
        }
    }
}

// =====================================================================
// PART 2 — Client: CorrelationIdInterceptor
// =====================================================================

/*
#nullable enable

using System;
using System.Linq;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace CrunchCounter.Client.Interceptors;

public sealed class CorrelationIdInterceptor : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var (newContext, _) = AttachCorrelationId(context);
        return continuation(request, newContext);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var (newContext, _) = AttachCorrelationId(context);
        return continuation(request, newContext);
    }

    private static (ClientInterceptorContext<TReq, TResp>, string) AttachCorrelationId<TReq, TResp>(
        ClientInterceptorContext<TReq, TResp> context)
        where TReq : class
        where TResp : class
    {
        var headers = context.Options.Headers ?? new Metadata();
        var existing = headers.FirstOrDefault(h => h.Key == "x-correlation-id");
        var id = existing?.Value ?? Guid.NewGuid().ToString("N").Substring(0, 12);
        if (existing is null)
        {
            headers.Add("x-correlation-id", id);
        }
        var newOptions = context.Options.WithHeaders(headers);
        return (new ClientInterceptorContext<TReq, TResp>(context.Method, context.Host, newOptions), id);
    }
}
*/
