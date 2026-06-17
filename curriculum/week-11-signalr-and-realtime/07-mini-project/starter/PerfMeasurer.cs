// Crunch.Chat / src/Server / PerfMeasurer.cs
//
// A small singleton service that tracks SignalR-level counters and prints
// a summary every 5 seconds. Useful for the PERF.md write-up. In production,
// replace this with dotnet-counters subscriptions or OpenTelemetry metrics.
//
// Counters tracked:
//   connections-started:  total connections opened on this process
//   connections-stopped:  total connections closed on this process
//   current-connections:  start - stop (connections currently open)
//   messages-broadcast:   total ReceiveMessage broadcasts this process emitted
//
// Citations:
//   dotnet-counters:  https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters
//   SignalR counters: https://learn.microsoft.com/en-us/aspnet/core/signalr/diagnostics

#nullable enable
using System.Diagnostics;

namespace Crunch.Chat;

public sealed class PerfMeasurer : IHostedService, IDisposable
{
    private long _connectionsStarted;
    private long _connectionsStopped;
    private long _messagesBroadcast;
    private Timer? _timer;
    private readonly ILogger<PerfMeasurer> _log;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public PerfMeasurer(ILogger<PerfMeasurer> log)
    {
        _log = log;
    }

    public void OnConnected()
    {
        Interlocked.Increment(ref _connectionsStarted);
    }

    public void OnDisconnected()
    {
        Interlocked.Increment(ref _connectionsStopped);
    }

    public void OnMessageBroadcast()
    {
        Interlocked.Increment(ref _messagesBroadcast);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(Tick, null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void Tick(object? state)
    {
        long started   = Interlocked.Read(ref _connectionsStarted);
        long stopped   = Interlocked.Read(ref _connectionsStopped);
        long broadcast = Interlocked.Read(ref _messagesBroadcast);
        long current   = started - stopped;
        double uptimeMin = _uptime.Elapsed.TotalMinutes;
        double broadcastPerMin = uptimeMin > 0.01 ? broadcast / uptimeMin : 0;

        _log.LogInformation(
            "[perf] uptime={UptimeMin:F1}min current={Current} started={Started} stopped={Stopped} broadcast={Broadcast} ({Rate:F1}/min)",
            uptimeMin, current, started, stopped, broadcast, broadcastPerMin);
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

// To wire this up so it actually ticks, register it as a hosted service in
// Program.cs (alongside the singleton registration):
//
//   builder.Services.AddSingleton<PerfMeasurer>();
//   builder.Services.AddHostedService(sp => sp.GetRequiredService<PerfMeasurer>());
//
// The two registrations are not the same: the singleton registration is what
// ChatHub injects from; the hosted-service registration is what makes the
// Timer fire. Both point at the same singleton instance via `sp.GetRequired`.
