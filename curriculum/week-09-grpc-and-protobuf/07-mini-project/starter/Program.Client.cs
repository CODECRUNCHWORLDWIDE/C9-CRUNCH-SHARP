// Program.cs for src/CrunchCounter.Client.
//
// Replace the auto-generated Program.cs with this file. Keep the project
// filename as Program.cs; this starter is named Program.Client.cs to
// disambiguate from the server starter.
//
// CLI subcommands:
//   crunch-counter inc <name> <delta>
//   crunch-counter watch <name>
//   crunch-counter load <name> <total>

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Crunch.Counter.V1;
using CrunchCounter.Client.Interceptors;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace CrunchCounter.Client;

public static class Program
{
    private const string DefaultServerAddress = "https://localhost:5001";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 64;  // EX_USAGE
        }

        var serverAddress = Environment.GetEnvironmentVariable("CRUNCH_COUNTER_SERVER")
            ?? DefaultServerAddress;

        using var channel = GrpcChannel.ForAddress(serverAddress);
        var invoker = channel.Intercept(new CorrelationIdInterceptor());
        var client = new Counter.CounterClient(invoker);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        return args[0] switch
        {
            "inc"   => await DoIncrementAsync(client, args, cts.Token),
            "watch" => await DoWatchAsync(client, args, cts.Token),
            "load"  => await DoLoadAsync(client, args, cts.Token),
            _       => UsageError(),
        };
    }

    private static async Task<int> DoIncrementAsync(
        Counter.CounterClient client, string[] args, CancellationToken ct)
    {
        if (args.Length != 3) return UsageError();
        var name = args[1];
        if (!long.TryParse(args[2], out var delta)) return UsageError();

        try
        {
            var reply = await client.IncrementAsync(
                new IncrementRequest { CounterName = name, Delta = delta },
                deadline: DateTime.UtcNow.AddSeconds(2),
                cancellationToken: ct);
            Console.WriteLine($"{reply.CounterName} = {reply.NewValue}");
            return 0;
        }
        catch (RpcException ex)
        {
            Console.Error.WriteLine($"increment failed: {ex.StatusCode} {ex.Status.Detail}");
            return ex.StatusCode == StatusCode.InvalidArgument ? 1 : 2;
        }
    }

    private static async Task<int> DoWatchAsync(
        Counter.CounterClient client, string[] args, CancellationToken ct)
    {
        if (args.Length != 2) return UsageError();
        var name = args[1];

        try
        {
            using var call = client.Subscribe(
                new SubscribeRequest { CounterName = name },
                deadline: DateTime.UtcNow.AddMinutes(60),
                cancellationToken: ct);

            await foreach (var ev in call.ResponseStream.ReadAllAsync(ct))
            {
                Console.WriteLine($"{ev.At.ToDateTime():HH:mm:ss.fff}  {ev.Kind,-15} {ev.CounterName} = {ev.Value}");
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Ctrl+C path; expected.
        }
        catch (RpcException ex)
        {
            Console.Error.WriteLine($"watch failed: {ex.StatusCode} {ex.Status.Detail}");
            return 2;
        }
        return 0;
    }

    private static async Task<int> DoLoadAsync(
        Counter.CounterClient client, string[] args, CancellationToken ct)
    {
        if (args.Length != 3) return UsageError();
        var name = args[1];
        if (!int.TryParse(args[2], out var total) || total <= 0) return UsageError();

        const int maxConcurrent = 16;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sem = new SemaphoreSlim(maxConcurrent);
        var tasks = new Task[total];
        long lastValue = 0;

        for (int i = 0; i < total; i++)
        {
            await sem.WaitAsync(ct);
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    var reply = await client.IncrementAsync(
                        new IncrementRequest { CounterName = name, Delta = 1 },
                        deadline: DateTime.UtcNow.AddSeconds(5),
                        cancellationToken: ct);
                    Interlocked.Exchange(ref lastValue, reply.NewValue);
                }
                finally
                {
                    sem.Release();
                }
            }, ct);
        }

        await Task.WhenAll(tasks);
        sw.Stop();
        Console.WriteLine($"{total} increments in {sw.ElapsedMilliseconds} ms, final value ~= {lastValue}");
        return 0;
    }

    private static int UsageError()
    {
        PrintUsage();
        return 64;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  crunch-counter inc <name> <delta>");
        Console.Error.WriteLine("  crunch-counter watch <name>");
        Console.Error.WriteLine("  crunch-counter load <name> <total>");
    }
}
