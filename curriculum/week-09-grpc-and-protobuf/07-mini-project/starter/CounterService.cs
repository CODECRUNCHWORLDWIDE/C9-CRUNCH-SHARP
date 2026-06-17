// CounterService.cs — server-side gRPC implementation for the Crunch Counter
// mini-project.
//
// Place at src/CrunchCounter.Server/Services/CounterService.cs.
//
// The TODOs below mark the load-bearing implementation choices. Fill them
// according to the acceptance criteria in mini-project/README.md.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Crunch.Counter.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace CrunchCounter.Server.Services;

public sealed class CounterService : Counter.CounterBase
{
    private const int MaxCounterNameLength = 64;

    private readonly ICounterStore _store;
    private readonly IEventBus _events;
    private readonly ILogger<CounterService> _logger;

    public CounterService(ICounterStore store, IEventBus events, ILogger<CounterService> logger)
    {
        _store = store;
        _events = events;
        _logger = logger;
    }

    // --- Unary: Increment ---
    public override async Task<IncrementResponse> Increment(IncrementRequest request, ServerCallContext context)
    {
        ValidateCounterName(request.CounterName);
        if (request.Delta == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delta must be non-zero"));

        var ct = context.CancellationToken;

        // Special "slow" counter for the DeadlineTests fixture in the test project.
        // Sleeps deliberately so a short client deadline produces DeadlineExceeded.
        // The Task.Delay throws OperationCanceledException on cancel and the
        // framework translates the resulting status to DeadlineExceeded for the
        // client. The catch below maps the rare explicit-client-cancel path.
        if (request.CounterName == "slow")
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException)
            {
                throw new RpcException(new Status(StatusCode.Cancelled, "slow path cancelled"));
            }
        }

        var newValue = _store.Increment(request.CounterName, request.Delta);
        var now = DateTimeOffset.UtcNow;
        _events.Publish(new CounterEvent
        {
            Kind = CounterEvent.Types.Kind.Increment,
            CounterName = request.CounterName,
            Value = newValue,
            At = Timestamp.FromDateTimeOffset(now),
        });

        return new IncrementResponse
        {
            CounterName = request.CounterName,
            NewValue = newValue,
            AppliedAt = Timestamp.FromDateTimeOffset(now),
        };
    }

    // --- Server-streaming: Subscribe ---
    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<CounterEvent> responseStream,
        ServerCallContext context)
    {
        ValidateCounterName(request.CounterName);

        var ct = context.CancellationToken;

        // 1. Send the SNAPSHOT first so the client has the current value.
        var initial = _store.Read(request.CounterName);
        await responseStream.WriteAsync(new CounterEvent
        {
            Kind = CounterEvent.Types.Kind.Snapshot,
            CounterName = request.CounterName,
            Value = initial,
            At = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        }, ct);

        // 2. Subscribe to the event bus. The bus returns an IAsyncEnumerable<CounterEvent>
        //    filtered by counter name.
        await foreach (var ev in _events.SubscribeAsync(request.CounterName, ct))
        {
            // The bus filters by name already; defensive re-check in case of bugs.
            if (ev.CounterName == request.CounterName)
            {
                await responseStream.WriteAsync(ev, ct);
            }
        }
    }

    private static void ValidateCounterName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "counter_name must be non-empty"));
        if (name.Length > MaxCounterNameLength)
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"counter_name must be <= {MaxCounterNameLength} chars"));
    }
}

// --- Supporting interfaces (define in src/CrunchCounter.Server/State/) ---

public interface ICounterStore
{
    long Read(string name);
    long Increment(string name, long delta);
}

public interface IEventBus
{
    void Publish(CounterEvent ev);
    IAsyncEnumerable<CounterEvent> SubscribeAsync(string counterName, CancellationToken ct);
}

// --- Reference implementation: in-memory store ---

public sealed class InMemoryCounterStore : ICounterStore
{
    private readonly ConcurrentDictionary<string, long> _values = new();

    public long Read(string name) => _values.GetOrAdd(name, 0L);

    public long Increment(string name, long delta)
    {
        return _values.AddOrUpdate(name, delta, (_, existing) => existing + delta);
    }
}

public sealed class ChannelBackedEventBus : IEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Guid, (string Filter, Channel<CounterEvent> Channel)> _subs = new();

    public void Publish(CounterEvent ev)
    {
        foreach (var (_, (filter, ch)) in _subs)
        {
            if (filter == ev.CounterName)
            {
                // Bounded channels (capacity 256) drop the oldest event under load.
                // For a counter event stream this is acceptable; the receiver only
                // needs eventual convergence with the current value.
                ch.Writer.TryWrite(ev);
            }
        }
    }

    public async IAsyncEnumerable<CounterEvent> SubscribeAsync(
        string counterName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var ch = Channel.CreateBounded<CounterEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subs[id] = (counterName, ch);
        try
        {
            await foreach (var ev in ch.Reader.ReadAllAsync(ct))
            {
                yield return ev;
            }
        }
        finally
        {
            _subs.TryRemove(id, out _);
            ch.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        foreach (var (_, (_, ch)) in _subs)
        {
            ch.Writer.TryComplete();
        }
        _subs.Clear();
    }
}
