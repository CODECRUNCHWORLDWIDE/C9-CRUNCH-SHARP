# Week 10 — Exercises

These four exercises take Entity Framework Core 8 apart and put it back together, one pipeline stage at a time. You will stand up a `DbContext`, author and script migrations, read the generated SQL for every CRUD shape, measure what the change tracker actually costs you, diagnose and cure an N+1 from a SQL log, and drop down to raw SQL safely with a strongly-typed value object along the way. Each file is a self-contained, runnable C# skeleton with acceptance criteria and a checklist baked into the comments — the goal of the week is that you can answer "why is this endpoint slow" with a SQL log, a row count, and a specific fix.

## How to Run an Exercise

Each exercise is a standalone console project you create yourself — there is no template to copy from; the steps live in the comment header of each `.cs` file.

1. Install the .NET 8 SDK and confirm `dotnet --version` reports `8.0.x`.
2. Install the EF Core CLI once per machine: `dotnet tool install --global dotnet-ef --version 8.0.0`, then confirm with `dotnet ef --version`.
3. Scaffold the project named in the exercise header, e.g. `dotnet new console -n Ex01.Migrations -f net8.0`, then `cd` into it.
4. Add the package references listed in the file's `.csproj` block (`Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, and `BenchmarkDotNet` where noted), and paste the file's code into `Program.cs` / `CatalogDb.cs` as the header indicates.
5. Run the EF commands the header lists (`dotnet ef migrations add <Name>`, `dotnet ef database update`) where the exercise calls for them.
6. Run it. Exercises 1, 3, and 4 use `dotnet run`; the benchmark in Exercise 2 must be `dotnet run -c Release` (BenchmarkDotNet refuses a Debug build).
7. Read the SQL log that `LogTo(Console.WriteLine, LogLevel.Information)` prints and check it against the acceptance criteria in the comments.

## Index

| # | File | What you'll practice | Difficulty | Est. time |
|---|------|----------------------|------------|-----------|
| 1 | [exercise-01-migrations-and-sql-log.cs](./exercise-01-migrations-and-sql-log.cs) | Stand up a `DbContext` against SQLite, add two migrations (initial create + add column), apply them, and read the SQL log for INSERT / SELECT-by-key / UPDATE / DELETE; emit an idempotent deploy script | Beginner | 60 min |
| 2 | [exercise-02-tracking-vs-no-tracking.cs](./exercise-02-tracking-vs-no-tracking.cs) | Benchmark a 10,000-row read three ways (`Tracking`, `AsNoTracking`, `AsNoTrackingWithIdentityResolution`) with BenchmarkDotNet and explain the time and allocation differences | Intermediate | 60 min |
| 3 | [exercise-03-fix-n-plus-one.cs](./exercise-03-fix-n-plus-one.cs) | Diagnose an N+1 pathology from the SQL log on a 100-customer / 10-order schema and apply three cures: `Include`, server-side projection, and a batched explicit load | Intermediate+ | 75 min |
| 4 | [exercise-04-raw-sql-and-converters.cs](./exercise-04-raw-sql-and-converters.cs) | Map a `Money` value object to two columns with `ComplexProperty`, write a `FromSqlInterpolated` search, prove it parameterizes to `@p0`, survive a SQL-injection attempt, and compose LINQ on top | Intermediate+ | 75 min |

## Checking Your Work

Annotated walk-throughs for all four exercises live in [SOLUTIONS.md](./SOLUTIONS.md), including the exact SQL log you should reproduce and the most common mistakes for each. Before you peek, self-check against these:

- **The SQL log matches.** You can produce the expected statements on the first attempt without editing the exercise — the single-column `UPDATE SET`, the `@p0` parameter on the raw query, the 1-vs-101 statement count for the N+1 cures.
- **The ratios hold, not just the wall-clock numbers.** In the benchmarks, `AsNoTracking` is at least 15% faster and allocates at least 30% less than tracking; absolute timings will differ by machine.
- **You can explain each design choice in one sentence** — why the tracker snapshot costs allocation, why `FromSqlInterpolated` is structurally injection-safe, and when each N+1 cure (eager, projection, explicit) is the right one.
