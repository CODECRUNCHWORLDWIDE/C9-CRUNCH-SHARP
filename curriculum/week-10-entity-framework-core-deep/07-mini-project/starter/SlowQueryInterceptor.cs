// CrunchCatalog.Api/SlowQueryInterceptor.cs
//
// Logs every SQL command whose elapsed time exceeds 100ms. Wired into the
// DbContext via `options.AddInterceptors(...)` in Program.cs.
//
// The DbCommandInterceptor base class is the EF Core extension point for
// observing every command the framework emits. Override only the events you
// care about; the rest are no-ops.
//
// Citation:
//   https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors

#nullable enable

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CrunchCatalog.Api;

public sealed class SlowQueryInterceptor : DbCommandInterceptor
{
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(100);
    private readonly ILogger<SlowQueryInterceptor> _log;

    public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> log)
    {
        _log = log;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Duration > SlowThreshold)
        {
            _log.LogWarning(
                "Slow query ({DurationMs} ms): {Sql}",
                eventData.Duration.TotalMilliseconds,
                command.CommandText);
        }
        return new ValueTask<DbDataReader>(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Duration > SlowThreshold)
        {
            _log.LogWarning(
                "Slow non-query ({DurationMs} ms, {Rows} rows affected): {Sql}",
                eventData.Duration.TotalMilliseconds,
                result,
                command.CommandText);
        }
        return new ValueTask<int>(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Duration > SlowThreshold)
        {
            _log.LogWarning(
                "Slow scalar ({DurationMs} ms): {Sql}",
                eventData.Duration.TotalMilliseconds,
                command.CommandText);
        }
        return new ValueTask<object?>(result);
    }
}
