// Exercise 2 — Implement and call all four gRPC call types.
//
// This file contains TWO source units:
//   1. NumberService.cs — the server implementation (place in src/Ex02.Server/Services/)
//   2. Program.cs       — the client driver (place in src/Ex02.Client/)
//
// Each unit is bracketed below. Cut the two and place in their respective
// projects. Both must compile with #nullable enable.

// =====================================================================
// PART 1 — Server: src/Ex02.Server/Services/NumberService.cs
// =====================================================================
//
// Replace the auto-generated Services/GreeterService.cs with this file.
// Adjust Program.cs in the server project so it reads:
//
//     using Ex02.Server.Services;
//     var builder = WebApplication.CreateBuilder(args);
//     builder.Services.AddGrpc();
//     var app = builder.Build();
//     app.MapGrpcService<NumberService>();
//     app.MapGet("/", () => "Ex02 NumberService. Use a gRPC client.");
//     app.Run();

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Crunch.Numbers.V1;
using Grpc.Core;

namespace Ex02.Server.Services;

public sealed class NumberService : NumberService.NumberServiceBase
{
    private int _echoSequence;

    // --- Unary ---
    public override Task<SquareResponse> Square(SquareRequest request, ServerCallContext context)
    {
        if (request.Input > 3_000_000_000L || request.Input < -3_000_000_000L)
            throw new RpcException(new Status(StatusCode.OutOfRange, "input would overflow int64 when squared"));

        var squared = checked(request.Input * request.Input);
        return Task.FromResult(new SquareResponse { Squared = squared });
    }

    // --- Server-streaming ---
    public override async Task CountUp(
        CountUpRequest request,
        IServerStreamWriter<NumberMessage> responseStream,
        ServerCallContext context)
    {
        if (request.Step < 1)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "step must be >= 1"));
        if (request.To < request.From)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "to must be >= from"));

        var ct = context.CancellationToken;
        for (int n = request.From; n <= request.To; n += request.Step)
        {
            ct.ThrowIfCancellationRequested();
            await responseStream.WriteAsync(new NumberMessage { Value = n }, ct);
            // Simulate work spread over time so the streaming is visible.
            await Task.Delay(50, ct);
        }
    }

    // --- Client-streaming ---
    public override async Task<SumResponse> Sum(
        IAsyncStreamReader<NumberMessage> requestStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        long sum = 0;
        int count = 0;
        await foreach (var msg in requestStream.ReadAllAsync(ct))
        {
            sum = checked(sum + msg.Value);
            count++;
        }
        return new SumResponse { Sum = sum, Count = count };
    }

    // --- Bidirectional ---
    public override async Task Echo(
        IAsyncStreamReader<EchoRequest> requestStream,
        IServerStreamWriter<EchoResponse> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        await foreach (var req in requestStream.ReadAllAsync(ct))
        {
            var seq = Interlocked.Increment(ref _echoSequence);
            await responseStream.WriteAsync(new EchoResponse { Text = req.Text, Sequence = seq }, ct);
        }
    }
}

// =====================================================================
// PART 2 — Client: src/Ex02.Client/Program.cs
// =====================================================================
//
// Replace the auto-generated Program.cs in Ex02.Client with this file.
// Update the address constant below if your server runs on a different port.

/*
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Crunch.Numbers.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace Ex02.Client;

public static class Program
{
    private const string ServerAddress = "https://localhost:5001";

    public static async Task Main()
    {
        Console.WriteLine($"Exercise 2 — driving all four call types against {ServerAddress}");
        Console.WriteLine(new string('-', 60));

        using var channel = GrpcChannel.ForAddress(ServerAddress);
        var client = new NumberService.NumberServiceClient(channel);

        await DemoUnaryAsync(client);
        await DemoServerStreamingAsync(client);
        await DemoClientStreamingAsync(client);
        await DemoBidirectionalAsync(client);

        Console.WriteLine();
        Console.WriteLine("Done. See REFLECTION QUESTIONS in the .proto file.");
    }

    // --- 1. Unary ---
    private static async Task DemoUnaryAsync(NumberService.NumberServiceClient client)
    {
        Console.WriteLine();
        Console.WriteLine("[1] Unary: Square(7)");
        var reply = await client.SquareAsync(
            new SquareRequest { Input = 7 },
            deadline: DateTime.UtcNow.AddSeconds(2));
        Console.WriteLine($"    squared = {reply.Squared}");
    }

    // --- 2. Server-streaming ---
    private static async Task DemoServerStreamingAsync(NumberService.NumberServiceClient client)
    {
        Console.WriteLine();
        Console.WriteLine("[2] Server-streaming: CountUp(1..5)");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var call = client.CountUp(new CountUpRequest { From = 1, To = 5, Step = 1 });
        await foreach (var msg in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            Console.WriteLine($"    received {msg.Value}");
        }
    }

    // --- 3. Client-streaming ---
    private static async Task DemoClientStreamingAsync(NumberService.NumberServiceClient client)
    {
        Console.WriteLine();
        Console.WriteLine("[3] Client-streaming: Sum(10, 20, 30, 40)");
        using var call = client.Sum(deadline: DateTime.UtcNow.AddSeconds(5));
        foreach (var n in new long[] { 10, 20, 30, 40 })
        {
            await call.RequestStream.WriteAsync(new NumberMessage { Value = n });
        }
        await call.RequestStream.CompleteAsync();
        var summary = await call.ResponseAsync;
        Console.WriteLine($"    sum = {summary.Sum}, count = {summary.Count}");
    }

    // --- 4. Bidirectional ---
    private static async Task DemoBidirectionalAsync(NumberService.NumberServiceClient client)
    {
        Console.WriteLine();
        Console.WriteLine("[4] Bidirectional: Echo(\"alpha\", \"beta\", \"gamma\")");
        using var call = client.Echo();

        var sendTask = Task.Run(async () =>
        {
            foreach (var text in new[] { "alpha", "beta", "gamma" })
            {
                await call.RequestStream.WriteAsync(new EchoRequest { Text = text });
                await Task.Delay(50);
            }
            await call.RequestStream.CompleteAsync();
        });

        var recvTask = Task.Run(async () =>
        {
            await foreach (var msg in call.ResponseStream.ReadAllAsync())
            {
                Console.WriteLine($"    echo: text={msg.Text} sequence={msg.Sequence}");
            }
        });

        await Task.WhenAll(sendTask, recvTask);
    }
}
*/

// HINTS (read after a serious attempt):
//
// 1. The generated client class is `NumberService.NumberServiceClient`. The
//    generator nests the client inside a static partial container named after
//    the service. Likewise the server base is `NumberService.NumberServiceBase`.
//
// 2. For the server-streaming demo, do NOT forget the `using` on the call object.
//    AsyncServerStreamingCall<T> is IDisposable and disposing it on early exit
//    is what closes the HTTP/2 stream cleanly.
//
// 3. For the bidirectional demo, the two `Task.Run` calls are intentional. The
//    request loop and response loop are independent; if you serialise them
//    you have written a unary-shaped service with extra steps.
//
// 4. If you see "the SSL connection could not be established" — run
//    `dotnet dev-certs https --trust` once.
//
// 5. The `deadline:` parameter takes an absolute DateTime in UTC. If you pass
//    a DateTime with Kind=Local, the call may succeed but the wire timeout
//    will be wrong by your timezone offset. Always DateTime.UtcNow.AddX().
