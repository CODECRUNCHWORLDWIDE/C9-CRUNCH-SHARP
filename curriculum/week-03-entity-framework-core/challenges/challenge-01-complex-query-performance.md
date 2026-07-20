# Challenge 1 — Complex query performance

**Time estimate:** ~2 hours.

## Problem statement

Take the `BlogContext` from Exercise 2. Seed it with 10,000 posts spread across 50 authors, each post tagged with two or three of 20 tags. Then write three queries — one straightforward, two with deliberate traps — measure them with `BenchmarkDotNet`, read the SQL emitted with `LogTo`, and write a one-page diagnosis of each trap with a tested fix.

This is the diagnostic discipline you will need every time a production EF Core endpoint slows down. The whole challenge is: *measure, then read SQL, then fix*. Not "guess from the C# code."

## The contract

You will produce one repo (`c9-week-03-query-performance-<yourhandle>`) containing:

```
QueryPerformance/
├── QueryPerformance.sln
├── .gitignore
├── Directory.Build.props
├── src/
│   ├── QueryPerformance.Data/
│   │   ├── QueryPerformance.Data.csproj
│   │   ├── BlogContext.cs
│   │   ├── Entities.cs            (Author, Post, Tag — same shapes as Exercise 2)
│   │   ├── Configurations.cs      (the three IEntityTypeConfiguration<T> classes)
│   │   ├── Seeder.cs              (seeds 10,000 posts on first run)
│   │   └── Migrations/...
│   └── QueryPerformance.Bench/
│       ├── QueryPerformance.Bench.csproj
│       ├── Program.cs
│       ├── N_plus_1_Trap.cs
│       ├── Unbounded_Trap.cs
│       └── Join_Without_Index_Trap.cs
├── notes/
│   ├── trap-01-n-plus-1.md
│   ├── trap-02-unbounded.md
│   └── trap-03-missing-index.md
└── README.md
```

`README.md` is the entry point. Each `notes/*.md` is one page documenting one trap with: the bad query, the SQL EF Core emitted, the `BenchmarkDotNet` result, the fix, the fixed SQL, the new `BenchmarkDotNet` result, and a short paragraph on what you learned.

## Acceptance criteria

- [ ] A solution with two projects: `QueryPerformance.Data` (class library) and `QueryPerformance.Bench` (console with `BenchmarkDotNet`).
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet run --project src/QueryPerformance.Bench -c Release` runs all three benchmark groups and produces `BenchmarkDotNet` summary tables.
- [ ] `BlogContext` seeds the database with 10,000 posts on first run; subsequent runs detect the seed is present and skip it.
- [ ] Every benchmark has both a "before" (the trap) and an "after" (the fix), with timing differences large enough to be unambiguous.
- [ ] Each `notes/*.md` file is complete: bad query, bad SQL, bad measurement, fix, good SQL, good measurement, lesson.
- [ ] `README.md` summarises all three traps in a single results table.

## Suggested order of operations

### Phase 1 — Scaffold the data project (~20 min)

```bash
mkdir QueryPerformance && cd QueryPerformance
dotnet new sln -n QueryPerformance
dotnet new gitignore && git init

dotnet new classlib -n QueryPerformance.Data  -o src/QueryPerformance.Data
dotnet new console  -n QueryPerformance.Bench -o src/QueryPerformance.Bench
dotnet sln add src/QueryPerformance.Data/QueryPerformance.Data.csproj
dotnet sln add src/QueryPerformance.Bench/QueryPerformance.Bench.csproj
dotnet add src/QueryPerformance.Bench reference src/QueryPerformance.Data

dotnet add src/QueryPerformance.Data package Microsoft.EntityFrameworkCore
dotnet add src/QueryPerformance.Data package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/QueryPerformance.Data package Microsoft.EntityFrameworkCore.Design
dotnet add src/QueryPerformance.Bench package BenchmarkDotNet
```

Copy the `Author`/`Post`/`Tag` entities and the `BlogContext` from Exercise 2. Add a `Seeder.SeedAsync(BlogContext)` that produces 50 authors and 10,000 posts (200 per author), each with two or three of 20 fixed tags.

Generate and apply the initial migration:

```bash
cd src/QueryPerformance.Data
dotnet ef migrations add InitialCreate --startup-project ../QueryPerformance.Bench
dotnet ef database update --startup-project ../QueryPerformance.Bench
cd ../..
```

Commit: `Skeleton + initial migration + seeder`.

### Phase 2 — Trap 1: the N+1 query (~25 min)

Bench class:

```csharp
[MemoryDiagnoser]
public class N_plus_1_Trap
{
    private BlogContext _db = null!;

    [GlobalSetup] public void Setup() => _db = new BlogContext();
    [GlobalCleanup] public void Cleanup() => _db.Dispose();

    [Benchmark(Baseline = true)]
    public async Task<int> Trap_no_include()
    {
        // BUG: loads posts, then accesses Author.Name in a loop, triggering
        //      one SELECT per post.
        var posts = await _db.Posts.AsNoTracking().ToListAsync();
        var sum = 0;
        foreach (var p in posts)
            sum += (await _db.Authors.FindAsync(p.AuthorId))!.Name.Length;
        return sum;
    }

    [Benchmark]
    public async Task<int> Fix_with_include()
    {
        var posts = await _db.Posts
            .AsNoTracking()
            .Include(p => p.Author)
            .ToListAsync();
        return posts.Sum(p => p.Author.Name.Length);
    }

    [Benchmark]
    public async Task<int> Fix_with_projection()
    {
        var rows = await _db.Posts
            .AsNoTracking()
            .Select(p => new { p.Author.Name })
            .ToListAsync();
        return rows.Sum(r => r.Name.Length);
    }
}
```

Capture the SQL each variant emits by enabling `LogTo(Console.WriteLine)` *outside* of the benchmark run (logging skews timings). Run once with logging, copy the SQL into `notes/trap-01-n-plus-1.md`.

Run benchmarks with `dotnet run -c Release --project src/QueryPerformance.Bench -- --filter '*N_plus_1*'`. Expected:

- `Trap_no_include`: thousands of queries, hundreds of milliseconds, large allocations.
- `Fix_with_include`: one query with a JOIN, milliseconds, small allocations.
- `Fix_with_projection`: one query that selects only `Name`, the fastest of the three, smallest allocations.

Commit: `Trap 1: N+1 with measurements and fixes`.

### Phase 3 — Trap 2: the unbounded tracked load (~25 min)

```csharp
[MemoryDiagnoser]
public class Unbounded_Trap
{
    private BlogContext _db = null!;

    [GlobalSetup] public void Setup() => _db = new BlogContext();
    [GlobalCleanup] public void Cleanup() => _db.Dispose();

    [Benchmark(Baseline = true)]
    public async Task<int> Trap_load_all_then_count()
    {
        // BUG: pulls every row, tracks them all, returns the count.
        var all = await _db.Posts.ToListAsync();
        return all.Count;
    }

    [Benchmark]
    public async Task<int> Fix_count_on_server()
    {
        return await _db.Posts.CountAsync();
    }

    [Benchmark]
    public async Task<int> Fix_no_tracking_count()
    {
        // The point of this variant: even AsNoTracking can't compete with a
        // server-side COUNT. Pulling 10,000 rows over the wire is the cost,
        // not the change tracker alone.
        var all = await _db.Posts.AsNoTracking().ToListAsync();
        return all.Count;
    }
}
```

Run. Expected:

- `Trap_load_all_then_count`: hundreds of milliseconds, tens of megabytes allocated, a fully populated change tracker.
- `Fix_count_on_server`: microseconds, almost no allocations (the result is a single `int`).
- `Fix_no_tracking_count`: faster than the trap but still slow — the wire transfer dominates.

Document in `notes/trap-02-unbounded.md`. The lesson is two-fold: aggregate on the server (`CountAsync` / `SumAsync`), and `AsNoTracking` is necessary but not sufficient for read paths.

Commit: `Trap 2: unbounded tracked load`.

### Phase 4 — Trap 3: the join without an index (~30 min)

This one is more subtle. The seed creates 10,000 posts with random `CreatedAt` timestamps over the last year. We want "all posts in the last 7 days, with their authors." Without an index on `Posts.CreatedAt`, SQLite scans the entire table.

```csharp
[MemoryDiagnoser]
public class Join_Without_Index_Trap
{
    private BlogContext _db = null!;
    private DateTimeOffset _cutoff;

    [GlobalSetup] public void Setup()
    {
        _db = new BlogContext();
        _cutoff = DateTimeOffset.UtcNow.AddDays(-7);
    }

    [GlobalCleanup] public void Cleanup() => _db.Dispose();

    [Benchmark(Baseline = true)]
    public async Task<int> Trap_without_index()
    {
        // BUG: no index on CreatedAt; the filter scans the whole table.
        var rows = await _db.Posts
            .AsNoTracking()
            .Where(p => p.CreatedAt >= _cutoff)
            .Include(p => p.Author)
            .ToListAsync();
        return rows.Count;
    }

    // To run the "with index" variant, generate a new migration that adds
    // HasIndex(p => p.CreatedAt) in PostConfiguration, apply it, then run the
    // benchmarks again. Document both runs in notes/trap-03-missing-index.md.
}
```

Run `EXPLAIN QUERY PLAN` directly against SQLite to confirm the diagnosis:

```bash
sqlite3 src/QueryPerformance.Data/bin/Debug/net9.0/blog.db \
    "EXPLAIN QUERY PLAN SELECT * FROM Posts WHERE CreatedAt >= '...';"
```

Without an index, you should see `SCAN TABLE Posts`. With an index, `SEARCH TABLE Posts USING INDEX IX_Posts_CreatedAt (CreatedAt>?)`.

Add the index in `PostConfiguration` (or via a fluent override in `OnModelCreating`), generate a `AddCreatedAtIndex` migration, apply, re-run benchmarks. Expected: at least a 3x improvement on the filter, often more.

Document in `notes/trap-03-missing-index.md`. Include both `EXPLAIN QUERY PLAN` outputs.

Commit: `Trap 3: missing index, with EXPLAIN QUERY PLAN diagnosis`.

### Phase 5 — README + final polish (~20 min)

`README.md` should include:

- One paragraph describing the project's purpose.
- The exact commands to clone, build, seed, and benchmark.
- A summary table:

  | Trap | Bad (ns) | Good (ns) | Ratio | Diagnosis |
  |------|---------:|----------:|------:|-----------|
  | N+1 query | 287,000,000 | 11,400,000 | 25x | `Include` or projection eliminates per-row queries. |
  | Unbounded tracked load | 184,000,000 | 320,000 | 575x | Aggregate on the server, never in C#. |
  | Missing index | 31,200,000 | 1,800,000 | 17x | Always index columns you filter or sort by. |

  (Your numbers will differ; the ratios should be in the same ballpark.)

- Links to each trap's notes file.

Commit: `README with summary table; link to per-trap notes`.

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 15% | `dotnet build`, `dotnet ef database update`, and `dotnet run -c Release` all clean on a fresh clone |
| Seed correctness | 10% | 10,000 posts, 50 authors, 20 tags — counts verified in `sqlite3` |
| Trap fidelity | 25% | Each trap demonstrates a measurable performance gap; the SQL difference is plainly visible in the notes |
| Fix correctness | 20% | Each fix preserves the original semantics; `BenchmarkDotNet` shows the expected improvement |
| Notes quality | 20% | Each notes file is self-contained: bad SQL, good SQL, bad timing, good timing, one paragraph lesson |
| README | 10% | A new developer can clone, seed, benchmark, and read all three traps in under 10 minutes |

## Stretch (optional)

- Add a fourth trap: the **cartesian explosion** when you `Include` two collections on the same query. Demonstrate the problem with the seed expanded to give each post 5 tags. Fix with `AsSplitQuery()`.
- Add a fifth trap: a `GROUP BY` on a non-translatable expression. Show how rewriting the grouping key keeps the query on the server.
- Run the same benchmarks against PostgreSQL (with the same migrations applied via the `Npgsql.EntityFrameworkCore.PostgreSQL` provider) and add a column to the summary table comparing the two providers.

---

## Resources

- *EF Core performance overview*: <https://learn.microsoft.com/en-us/ef/core/performance/>
- *`AsSplitQuery` documentation*: <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries>
- *`BenchmarkDotNet` Getting Started*: <https://benchmarkdotnet.org/articles/guides/getting-started.html>
- *SQLite `EXPLAIN QUERY PLAN`*: <https://sqlite.org/eqp.html>
