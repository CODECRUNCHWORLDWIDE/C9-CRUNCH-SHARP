# Week 10 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `devblogs.microsoft.com/dotnet` posts are free. The `dotnet/efcore` source on GitHub is public. The Npgsql documentation is free. No paywalled material is linked.

## Required reading (work it into your week)

### Entity Framework Core 8 — overview and concepts

- **EF Core overview** — what EF Core is, the providers, the supported platforms:
  <https://learn.microsoft.com/en-us/ef/core/>
- **What's new in EF Core 8** — the November 2023 LTS release notes; complex properties, raw SQL improvements, JSON columns, the bulk-update API:
  <https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew>
- **`DbContext` configuration** — `DbContextOptionsBuilder`, the DI registration shape, the lifetime debate:
  <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>
- **Logging, events, and diagnostics** — `LogTo`, `EnableSensitiveDataLogging`, the event-IDs reference:
  <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/extensions-logging>
- **Event counters reference** — every counter EF Core emits, with descriptions:
  <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/event-counters>

### Migrations

- **Migrations overview** — the workflow, the snapshot file, the migrations table:
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>
- **Managing migrations** — `add`, `remove`, `list`, the rule "never edit a migration after it has shipped":
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing>
- **Applying migrations** — `dotnet ef database update`, the idempotent script, the runtime API `Database.MigrateAsync`:
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>
- **Migrations in team environments** — handling merge conflicts in the snapshot file:
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/teams>
- **Custom migration operations** — extending the migration emitter when you need something the conventions do not cover:
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations>

### The change tracker

- **Change tracking overview** — the `EntityState` enum, the snapshot model, when `DetectChanges` runs:
  <https://learn.microsoft.com/en-us/ef/core/change-tracking/>
- **Tracking vs no-tracking queries** — the standing reference for `AsNoTracking` and `AsNoTrackingWithIdentityResolution`:
  <https://learn.microsoft.com/en-us/ef/core/querying/tracking>
- **Identity resolution** — the in-memory `(entity-type, primary-key)` dictionary, when two reads return the same instance:
  <https://learn.microsoft.com/en-us/ef/core/change-tracking/identity-resolution>
- **Working with the change tracker** — `Entries()`, `DetectChanges`, `AutoDetectChangesEnabled`, the perf cliff:
  <https://learn.microsoft.com/en-us/ef/core/change-tracking/debug-views>

### Loading strategies and the N+1 problem

- **Loading related data** — eager, explicit, lazy loading; the conceptual taxonomy:
  <https://learn.microsoft.com/en-us/ef/core/querying/related-data/>
- **Eager loading** — `Include`, `ThenInclude`, filtered `Include`:
  <https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager>
- **Explicit loading** — `Entry(...).Collection(...).LoadAsync`, `Entry(...).Reference(...).LoadAsync`:
  <https://learn.microsoft.com/en-us/ef/core/querying/related-data/explicit>
- **Lazy loading** — `UseLazyLoadingProxies`, the `Castle.Core` proxy generator, the danger:
  <https://learn.microsoft.com/en-us/ef/core/querying/related-data/lazy>
- **Single vs split queries** — `AsSplitQuery`, `AsSingleQuery`, the cartesian-explosion guard:
  <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries>

### Raw SQL and the escape hatches

- **SQL queries** — `FromSql`, `FromSqlInterpolated`, `FromSqlRaw`, the safety story:
  <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries>
- **Executing non-query SQL** — `Database.ExecuteSqlInterpolatedAsync`, `ExecuteSqlRawAsync`:
  <https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete>
- **OWASP SQL-injection prevention cheat sheet** — the canonical reference for "why parameterize":
  <https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html>

### Modelling: value converters, owned types, complex properties

- **Value conversions** — `HasConversion<>`, the built-in converters, the strongly-typed-ID pattern:
  <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions>
- **Owned entity types** — `OwnsOne`, `OwnsMany`, the table-mapping rules:
  <https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities>
- **Complex types** (EF Core 8 new) — the value-object construct without table mapping:
  <https://learn.microsoft.com/en-us/ef/core/modeling/complex-types>
- **Modelling overview** — keys, indexes, constraints, conventions:
  <https://learn.microsoft.com/en-us/ef/core/modeling/>

### Performance

- **EF Core performance overview** — the team's "what to measure first" guide:
  <https://learn.microsoft.com/en-us/ef/core/performance/>
- **Efficient querying** — `AsNoTracking`, projections, server-side evaluation:
  <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying>
- **Compiled queries** — `EF.CompileAsyncQuery`, when to use it:
  <https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries>
- **Modelling for performance** — keys, indexes, the cost of `STRING_AGG`:
  <https://learn.microsoft.com/en-us/ef/core/performance/modeling-for-performance>

### Stephen Toub on `devblogs.microsoft.com/dotnet`

These are the canonical performance blog posts; read them once, return as a reference:

- **Performance Improvements in .NET 8** — covers the EF Core 8 translation-pipeline improvements:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/>
- **Performance Improvements in .NET 7** — preceding context; the JIT improvements that EF Core inherits:
  <https://devblogs.microsoft.com/dotnet/performance_improvements_in_net_7/>
- **What's new in System.Text.Json in .NET 8** — relevant to JSON-column mapping in EF Core 8:
  <https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-8/>

### The `dotnet/efcore` GitHub source — source link these as you read

- **Repository root**:
  <https://github.com/dotnet/efcore>
- **`DbContext.cs`** — the entry point, the lifetime model:
  <https://github.com/dotnet/efcore/blob/main/src/EFCore/DbContext.cs>
- **`ChangeTracker.cs`** — the public surface of the tracker:
  <https://github.com/dotnet/efcore/blob/main/src/EFCore/ChangeTracking/ChangeTracker.cs>
- **`EntityEntry.cs`** — the per-entity tracker entry, the `State` accessor:
  <https://github.com/dotnet/efcore/blob/main/src/EFCore/ChangeTracking/EntityEntry.cs>
- **`InternalEntityEntry.cs`** — the internal entry that holds the snapshot and the original values:
  <https://github.com/dotnet/efcore/blob/main/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs>
- **`EntityFrameworkQueryableExtensions.cs`** — the `Include`, `AsNoTracking`, `AsSplitQuery`, `AsTracking` extension methods:
  <https://github.com/dotnet/efcore/blob/main/src/EFCore/Extensions/EntityFrameworkQueryableExtensions.cs>
- **`RelationalQueryableExtensions.cs`** — `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSqlInterpolatedAsync`:
  <https://github.com/dotnet/efcore/blob/main/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs>
- **EF Core release notes by version**:
  <https://github.com/dotnet/efcore/blob/main/docs/PlanningProcess.md>

### Provider documentation

- **Npgsql Entity Framework Core provider** — the PostgreSQL provider; type mappings, `DateTimeOffset` semantics, JSON columns:
  <https://www.npgsql.org/efcore/>
- **`Microsoft.EntityFrameworkCore.Sqlite`** — the SQLite provider; limitations, the `__EFMigrationsHistory` quirks:
  <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/>
- **`Microsoft.EntityFrameworkCore.SqlServer`** — the SQL Server provider:
  <https://learn.microsoft.com/en-us/ef/core/providers/sql-server/>

### Observability and the SQL log

- **`dotnet-counters` tool** — install, monitor, list known sources:
  <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters>
- **`Microsoft.EntityFrameworkCore.Database.Command` events** — the event-ID table for SQL log entries:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.relationaleventid>
- **`Microsoft.EntityFrameworkCore.Diagnostics` namespace reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics>

## Recommended reading (after the required set)

- **EF Core query pipeline architecture** — the internals, the `SelectExpression`, the `RelationalQueryExpression`:
  <https://learn.microsoft.com/en-us/ef/core/querying/how-query-works>
- **Bulk update and delete** — the EF Core 8 `ExecuteUpdate` / `ExecuteDelete` APIs that bypass the tracker:
  <https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete>
- **EF Core community standups** — the team's monthly recorded calls; archive of design discussions:
  <https://learn.microsoft.com/en-us/shows/on-net/>
- **Andrew Lock's "Practical ASP.NET Core" blog — EF Core posts** — long-form supplementary reading:
  <https://andrewlock.net/series/></content>

## Tools you will install this week

- **`dotnet-ef` global tool.** Once per machine: `dotnet tool install --global dotnet-ef --version 8.0.0`. Verify with `dotnet ef --version`.
- **`dotnet-counters`.** Once per machine: `dotnet tool install --global dotnet-counters`. Verify with `dotnet-counters --version`.
- **PostgreSQL via Docker** (optional, used in the mini-project): `docker run --name pg-week10 -p 5432:5432 -e POSTGRES_PASSWORD=devpass -d postgres:16`. Verify with `docker logs pg-week10`.
- **`BenchmarkDotNet`** (used in Challenge 2): added as a NuGet package per-project, not a global tool: `dotnet add package BenchmarkDotNet --version 0.13.12`.

## Citations policy

This curriculum cites Microsoft Learn URLs, `devblogs.microsoft.com/dotnet` URLs, and `github.com/dotnet/efcore` source URLs as the primary references. Every example in the lecture notes and exercises is traced back to one of these three. When a third-party blog (Andrew Lock, Steve Gordon, Khalid Abuhakmeh) is the clearer reference, it is cited explicitly with a URL — never paraphrased without attribution. The OWASP cheat sheet is cited where SQL injection is the topic; the cheat sheet is the canonical reference. If a citation is missing from a section of these notes, treat it as a bug and open an issue against the C9 curriculum repository.
