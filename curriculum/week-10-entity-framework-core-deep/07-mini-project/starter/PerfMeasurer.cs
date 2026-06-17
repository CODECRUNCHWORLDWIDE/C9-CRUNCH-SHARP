// CrunchCatalog.Api/PerfMeasurer.cs
//
// Helper that runs an async block, captures the elapsed wall time, and counts
// SQL statements via a LogTo callback wired onto a one-shot DbContext.
//
// Usage:
//   var result = await PerfMeasurer.RunAsync<List<Foo>>(options, async db =>
//   {
//       return await db.Foos.AsNoTracking().ToListAsync();
//   });
//   Console.WriteLine($"  {result.Elapsed.TotalMilliseconds:F1} ms");
//   Console.WriteLine($"  {result.SqlCount} SQL statements");

#nullable enable

using System.Diagnostics;
using CrunchCatalog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrunchCatalog.Api;

public record PerfRun<T>(T Value, TimeSpan Elapsed, int SqlCount);

public static class PerfMeasurer
{
    public static async Task<PerfRun<T>> RunAsync<T>(
        DbContextOptions<CatalogDb> baseOptions,
        Func<CatalogDb, Task<T>> body)
    {
        int sqlCount = 0;

        var options = new DbContextOptionsBuilder<CatalogDb>(baseOptions.GetExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>() is null ? new DbContextOptions<CatalogDb>() : baseOptions)
            // Wire a per-run logger that just counts executed commands.
            .LogTo(line =>
            {
                if (line.Contains("Executed DbCommand", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref sqlCount);
                }
            }, LogLevel.Information)
            .Options;

        using var db = new CatalogDb(options);

        var sw = Stopwatch.StartNew();
        var value = await body(db);
        sw.Stop();

        return new PerfRun<T>(value, sw.Elapsed, sqlCount);
    }

    // A simpler variant that takes an already-open DbContext. The SQL count is
    // not available here because the LogTo callback is wired at options-build
    // time. Useful for ad-hoc timing without instrumenting the count.
    public static async Task<(T Value, TimeSpan Elapsed)> TimeAsync<T>(Func<Task<T>> body)
    {
        var sw = Stopwatch.StartNew();
        var value = await body();
        sw.Stop();
        return (value, sw.Elapsed);
    }
}
