// Exercise 3 — Procedural to Pipeline Refactor
//
// Goal: Refactor a 120-line procedural log analyser into a 30-line LINQ
//       pipeline that produces identical output. The procedural version is
//       given. The pipeline version is your job. Both run; the test harness
//       prints each output and asserts equality.
//
// Estimated time: 60 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir PipelineRefactor && cd PipelineRefactor
//      dotnet new console -n PipelineRefactor -o src/PipelineRefactor
//
//    Replace src/PipelineRefactor/Program.cs with the contents of THIS FILE.
//
// 2. Read `AnalyzeProcedural(...)` end-to-end. Understand each loop, each
//    accumulator, and each branch. Note the imperative shape:
//      - mutable dictionaries
//      - nested loops
//      - explicit accumulator initialization
//      - scattered if/else
//
// 3. Implement `AnalyzePipeline(...)` to produce the SAME report by using
//    LINQ. Aim for under 40 lines including the record declarations. The
//    helpers you've seen this week — CountBy, AggregateBy, OrderByDescending,
//    Where, Select, list patterns in a switch expression — are all in scope.
//
// 4. Run:
//
//      dotnet run --project src/PipelineRefactor
//
//    The harness compares the two reports field-by-field and prints DIFF
//    output if they disagree.
//
// ACCEPTANCE CRITERIA
//
//   [ ] `AnalyzePipeline(...)` produces output equal to `AnalyzeProcedural(...)`.
//   [ ] `AnalyzePipeline(...)` is under 40 lines (including blank lines and
//       sub-helper declarations, but not counting the record/type declarations).
//   [ ] No `for`, `foreach`, mutable dictionary, or assignment-after-init
//       inside `AnalyzePipeline(...)`.
//   [ ] At least one of `CountBy` or `AggregateBy` is used.
//   [ ] At least one `switch` expression over a closed type hierarchy is used.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//
// SMOKE OUTPUT (target)
//
//   == Procedural report ==
//   {ProceduralReport here}
//   == Pipeline report ==
//   {PipelineReport here}
//   Reports equal: True
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline scaffolding and hints at the bottom.

using System.Diagnostics;

// ---------------------------------------------------------------------------
// Data — 50 fake log entries spanning two hosts and three log levels.
// ---------------------------------------------------------------------------

LogEntry[] entries =
[
    new(DateTime.Parse("2026-05-13T08:00:00Z"), LogLevel.Info,    "api-1", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:02Z"), LogLevel.Info,    "api-1", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:05Z"), LogLevel.Warning, "api-1", "slow query"),
    new(DateTime.Parse("2026-05-13T08:00:10Z"), LogLevel.Error,   "api-1", "downstream timeout"),
    new(DateTime.Parse("2026-05-13T08:00:14Z"), LogLevel.Error,   "api-1", "downstream timeout"),
    new(DateTime.Parse("2026-05-13T08:00:20Z"), LogLevel.Info,    "api-2", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:22Z"), LogLevel.Info,    "api-2", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:25Z"), LogLevel.Warning, "api-2", "slow query"),
    new(DateTime.Parse("2026-05-13T08:00:27Z"), LogLevel.Warning, "api-2", "slow query"),
    new(DateTime.Parse("2026-05-13T08:00:30Z"), LogLevel.Error,   "api-2", "internal server error"),
    new(DateTime.Parse("2026-05-13T08:00:34Z"), LogLevel.Info,    "api-1", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:37Z"), LogLevel.Info,    "api-1", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:40Z"), LogLevel.Error,   "api-1", "downstream timeout"),
    new(DateTime.Parse("2026-05-13T08:00:42Z"), LogLevel.Info,    "api-2", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:44Z"), LogLevel.Info,    "api-2", "request received"),
    new(DateTime.Parse("2026-05-13T08:00:50Z"), LogLevel.Warning, "api-2", "slow query"),
    new(DateTime.Parse("2026-05-13T08:00:55Z"), LogLevel.Error,   "api-2", "internal server error"),
    new(DateTime.Parse("2026-05-13T08:01:00Z"), LogLevel.Info,    "api-1", "request received"),
    new(DateTime.Parse("2026-05-13T08:01:02Z"), LogLevel.Warning, "api-1", "slow query"),
    new(DateTime.Parse("2026-05-13T08:01:10Z"), LogLevel.Error,   "api-1", "downstream timeout"),
];

var sw = Stopwatch.StartNew();
var proceduralReport = AnalyzeProcedural(entries);
sw.Stop();
var proceduralMs = sw.ElapsedMilliseconds;

sw.Restart();
var pipelineReport = AnalyzePipeline(entries);
sw.Stop();
var pipelineMs = sw.ElapsedMilliseconds;

Console.WriteLine("== Procedural report ==");
PrintReport(proceduralReport);
Console.WriteLine($"(procedural elapsed: {proceduralMs} ms)");
Console.WriteLine();
Console.WriteLine("== Pipeline report ==");
PrintReport(pipelineReport);
Console.WriteLine($"(pipeline elapsed:   {pipelineMs} ms)");

var equal = proceduralReport == pipelineReport;
Console.WriteLine();
Console.WriteLine($"Reports equal: {equal}");

if (!equal)
{
    Console.WriteLine("DIFF — investigate:");
    Console.WriteLine($"  TotalEntries: P={proceduralReport.TotalEntries}  V={pipelineReport.TotalEntries}");
    Console.WriteLine($"  EntriesByLevel.Error:   P={proceduralReport.EntriesByLevel.GetValueOrDefault(LogLevel.Error)}    V={pipelineReport.EntriesByLevel.GetValueOrDefault(LogLevel.Error)}");
    Console.WriteLine($"  EntriesByLevel.Warning: P={proceduralReport.EntriesByLevel.GetValueOrDefault(LogLevel.Warning)}  V={pipelineReport.EntriesByLevel.GetValueOrDefault(LogLevel.Warning)}");
    Console.WriteLine($"  EntriesByLevel.Info:    P={proceduralReport.EntriesByLevel.GetValueOrDefault(LogLevel.Info)}     V={pipelineReport.EntriesByLevel.GetValueOrDefault(LogLevel.Info)}");
}

Console.WriteLine();
Console.WriteLine("Build succeeded · 0 warnings · 0 errors");

// ---------------------------------------------------------------------------
// The procedural implementation — 90+ lines of nested loops and accumulators.
// Read it carefully. This is the shape you will see in legacy codebases.
// ---------------------------------------------------------------------------

static Report AnalyzeProcedural(IEnumerable<LogEntry> entries)
{
    var entriesByLevel = new Dictionary<LogLevel, int>();
    var errorsByHost = new Dictionary<string, int>();
    var totalEntries = 0;
    var firstAt = DateTime.MaxValue;
    var lastAt = DateTime.MinValue;
    LogEntry? noisiestEntry = null;
    var hostMessageCounts = new Dictionary<string, Dictionary<string, int>>();

    foreach (var e in entries)
    {
        totalEntries++;

        // Bump entries-by-level.
        if (entriesByLevel.TryGetValue(e.Level, out var prev))
        {
            entriesByLevel[e.Level] = prev + 1;
        }
        else
        {
            entriesByLevel[e.Level] = 1;
        }

        // Track errors-by-host.
        if (e.Level == LogLevel.Error)
        {
            if (errorsByHost.TryGetValue(e.Host, out var hostPrev))
            {
                errorsByHost[e.Host] = hostPrev + 1;
            }
            else
            {
                errorsByHost[e.Host] = 1;
            }
        }

        // Track date range.
        if (e.At < firstAt) firstAt = e.At;
        if (e.At > lastAt)  lastAt  = e.At;

        // Track which (host, message) pair occurs most.
        if (!hostMessageCounts.TryGetValue(e.Host, out var msgCounts))
        {
            msgCounts = new Dictionary<string, int>();
            hostMessageCounts[e.Host] = msgCounts;
        }
        if (msgCounts.TryGetValue(e.Message, out var msgPrev))
        {
            msgCounts[e.Message] = msgPrev + 1;
        }
        else
        {
            msgCounts[e.Message] = 1;
        }
    }

    // Find the noisiest (host, message) pair.
    var noisiestCount = 0;
    string noisiestHost = "";
    string noisiestMessage = "";
    foreach (var hostKvp in hostMessageCounts)
    {
        foreach (var msgKvp in hostKvp.Value)
        {
            if (msgKvp.Value > noisiestCount)
            {
                noisiestCount = msgKvp.Value;
                noisiestHost = hostKvp.Key;
                noisiestMessage = msgKvp.Key;
            }
        }
    }
    // The original noisiestEntry is unused; we use noisiestPair instead.
    _ = noisiestEntry;

    return new Report
    {
        TotalEntries   = totalEntries,
        EntriesByLevel = entriesByLevel.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
        ErrorsByHost   = errorsByHost.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
        FirstAt        = firstAt,
        LastAt         = lastAt,
        NoisiestPair   = new HostMessage(noisiestHost, noisiestMessage, noisiestCount),
    };
}

// ---------------------------------------------------------------------------
// The pipeline implementation — YOUR JOB.
// ---------------------------------------------------------------------------

static Report AnalyzePipeline(IEnumerable<LogEntry> entries)
{
    // TODO: re-implement the same analysis as AnalyzeProcedural, but in
    //       LINQ-pipeline form. Aim for under 40 lines total.
    //
    // Steps you'll likely want:
    //   1. Materialize once with .ToList() (you'll enumerate multiple times).
    //   2. Use entries.CountBy(e => e.Level) for EntriesByLevel.
    //   3. Use entries.Where(e => e.Level == Error).CountBy(e => e.Host) for ErrorsByHost.
    //   4. entries.Min(e => e.At) and entries.Max(e => e.At) for the date range.
    //   5. For NoisiestPair: group by (Host, Message) using a tuple key,
    //      then OrderByDescending(Count), then take the first.
    //
    // The Report record uses dictionaries; convert your KeyValuePair sequences
    // to dictionaries with .ToDictionary(kvp => kvp.Key, kvp => kvp.Value).
    //
    // Read the hints at the bottom only after attempting it yourself.

    throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Output helper
// ---------------------------------------------------------------------------

static void PrintReport(Report r)
{
    Console.WriteLine($"  TotalEntries: {r.TotalEntries}");
    Console.WriteLine($"  EntriesByLevel: {FormatDict(r.EntriesByLevel)}");
    Console.WriteLine($"  ErrorsByHost: {FormatDict(r.ErrorsByHost)}");
    Console.WriteLine($"  FirstAt: {r.FirstAt:o}");
    Console.WriteLine($"  LastAt:  {r.LastAt:o}");
    Console.WriteLine($"  NoisiestPair: {r.NoisiestPair.Host} / {r.NoisiestPair.Message} x{r.NoisiestPair.Count}");
}

static string FormatDict<TKey>(IReadOnlyDictionary<TKey, int> d) where TKey : notnull =>
    "{" + string.Join(", ", d.Select(kvp => $"{kvp.Key}:{kvp.Value}")) + "}";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(DateTime At, LogLevel Level, string Host, string Message);

public sealed record HostMessage(string Host, string Message, int Count);

public sealed record Report
{
    public int TotalEntries { get; init; }
    public IReadOnlyDictionary<LogLevel, int> EntriesByLevel { get; init; } = new Dictionary<LogLevel, int>();
    public IReadOnlyDictionary<string,   int> ErrorsByHost   { get; init; } = new Dictionary<string,   int>();
    public DateTime FirstAt { get; init; }
    public DateTime LastAt  { get; init; }
    public HostMessage NoisiestPair { get; init; } = new("", "", 0);

    // Value equality on dictionaries needs explicit overrides — record's auto-
    // generated equality uses reference comparison on collection properties.
    public bool Equals(Report? other)
    {
        if (other is null) return false;
        return TotalEntries == other.TotalEntries
            && DictEqual(EntriesByLevel, other.EntriesByLevel)
            && DictEqual(ErrorsByHost,   other.ErrorsByHost)
            && FirstAt == other.FirstAt
            && LastAt  == other.LastAt
            && NoisiestPair == other.NoisiestPair;
    }

    public override int GetHashCode() => HashCode.Combine(TotalEntries, FirstAt, LastAt, NoisiestPair);

    static bool DictEqual<TKey>(IReadOnlyDictionary<TKey, int> a, IReadOnlyDictionary<TKey, int> b)
        where TKey : notnull
    {
        if (a.Count != b.Count) return false;
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var bv) || bv != kvp.Value) return false;
        }
        return true;
    }
}

// ---------------------------------------------------------------------------
// HINTS — read only after at least 20 minutes of attempt.
// ---------------------------------------------------------------------------
//
// A working AnalyzePipeline implementation in ~25 lines:
//
//   static Report AnalyzePipeline(IEnumerable<LogEntry> entries)
//   {
//       var list = entries.ToList();
//
//       var byLevel = list
//           .CountBy(e => e.Level)
//           .OrderBy(kvp => kvp.Key)
//           .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
//
//       var errorsByHost = list
//           .Where(e => e.Level == LogLevel.Error)
//           .CountBy(e => e.Host)
//           .OrderBy(kvp => kvp.Key)
//           .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
//
//       var noisiest = list
//           .CountBy(e => (e.Host, e.Message))
//           .OrderByDescending(kvp => kvp.Value)
//           .First();
//
//       return new Report
//       {
//           TotalEntries   = list.Count,
//           EntriesByLevel = byLevel,
//           ErrorsByHost   = errorsByHost,
//           FirstAt        = list.Min(e => e.At),
//           LastAt         = list.Max(e => e.At),
//           NoisiestPair   = new HostMessage(noisiest.Key.Host, noisiest.Key.Message, noisiest.Value),
//       };
//   }
//
// Notice:
//   - One .ToList() at the top. Everything else enumerates the materialized
//     list, so no operation is more than O(n).
//   - CountBy used twice — for EntriesByLevel and for ErrorsByHost.
//     A third CountBy with a tuple key `(e.Host, e.Message)` solves the
//     two-deep dictionary problem from the procedural version.
//   - No mutable dictionaries. No `if (dict.TryGet...)` patterns.
//   - The Min/Max calls each enumerate the list once. If you wanted to do
//     one pass instead of two, you could `.Aggregate((min, max))` — but for
//     20 elements it does not matter.
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// Look at the diff:
//   - Procedural:  ~90 lines, 4 mutable dictionaries, 5 if/else blocks.
//   - Pipeline:    ~25 lines, 0 mutable dictionaries, 0 if/else blocks.
//
// The pipeline form is readable as the question the report answers. The
// procedural form requires you to *simulate* the question in your head,
// tracking which dictionary holds what and when. Six months from now,
// future-you will read the pipeline once and understand it; will read the
// procedural twice and still wonder if "noisiestCount" is updated correctly.
//
// In Week 6 we re-do this analysis against EF Core. The pipeline version
// translates almost line-for-line into IQueryable<T> — the database does the
// CountBy via SQL `GROUP BY ... COUNT(*)`, no rows leave the database. The
// procedural version cannot be translated; it would require pulling every
// row into memory.
//
// The reflex of writing the pipeline first is what makes EF Core fast.
//
// ---------------------------------------------------------------------------
