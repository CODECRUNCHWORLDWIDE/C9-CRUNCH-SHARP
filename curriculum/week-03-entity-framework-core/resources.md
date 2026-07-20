# Week 3 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The EF Core source is MIT-licensed and public on GitHub. SQLite is public domain. No paywalled books are linked.

## Required reading (work it into your week)

- **EF Core overview** — the canonical Microsoft Learn entry point:
  <https://learn.microsoft.com/en-us/ef/core/>
- **Getting started with EF Core** — the official end-to-end walkthrough; ~45 minutes:
  <https://learn.microsoft.com/en-us/ef/core/get-started/overview/first-app>
- **Creating and configuring a model** — conventions, data annotations, fluent API:
  <https://learn.microsoft.com/en-us/ef/core/modeling/>
- **Relationships in EF Core** — one-to-one, one-to-many, many-to-many:
  <https://learn.microsoft.com/en-us/ef/core/modeling/relationships>
- **EF Core migrations**:
  <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>
- **Querying data with EF Core** — `IQueryable<T>`, `Include`, projection:
  <https://learn.microsoft.com/en-us/ef/core/querying/>
- **EF Core 9 release notes** — what changed since EF Core 8:
  <https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew>

## Authoritative deep dives

- **Change tracking in EF Core** — `EntityState`, `Attach`, `Update`, `ChangeTracker`:
  <https://learn.microsoft.com/en-us/ef/core/change-tracking/>
- **Performance considerations** — when EF is slow and why:
  <https://learn.microsoft.com/en-us/ef/core/performance/>
- **`IQueryable<T>` translation rules** — what does and does not translate to SQL:
  <https://learn.microsoft.com/en-us/ef/core/querying/how-query-works>
- **Concurrency conflicts** — optimistic concurrency with `[Timestamp]`:
  <https://learn.microsoft.com/en-us/ef/core/saving/concurrency>
- **Bulk operations** — `ExecuteUpdateAsync` and `ExecuteDeleteAsync`:
  <https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete>
- **`DbContext` lifetime, configuration, and initialization**:
  <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>

## Official EF Core docs

- **EF Core CLI reference** — every `dotnet ef` subcommand:
  <https://learn.microsoft.com/en-us/ef/core/cli/dotnet>
- **`Microsoft.EntityFrameworkCore` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore>
- **SQLite provider**:
  <https://learn.microsoft.com/en-us/ef/core/providers/sqlite/>
- **PostgreSQL provider (Npgsql)**:
  <https://www.npgsql.org/efcore/>
- **Logging in EF Core** — see what SQL the provider emits:
  <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/>

## Open-source projects to read this week

You learn more from one hour reading well-written C# than from three hours of tutorials.

- **`dotnet/efcore`** — the full source of EF Core; readable, MIT-licensed:
  <https://github.com/dotnet/efcore>
- **`Microsoft.EntityFrameworkCore.Relational`** — the abstract relational provider; the most reusable part of the codebase:
  <https://github.com/dotnet/efcore/tree/main/src/EFCore.Relational>
- **`Microsoft.EntityFrameworkCore.Sqlite`** — the SQLite-specific provider:
  <https://github.com/dotnet/efcore/tree/main/src/EFCore.Sqlite.Core>
- **`Npgsql.EntityFrameworkCore.PostgreSQL`** — the community-maintained PostgreSQL provider:
  <https://github.com/npgsql/efcore.pg>

## Community deep-dives

- **Jon P Smith's blog** — author of "EF Core in Action"; the single best independent EF writer:
  <https://www.thereformedprogrammer.net/>
- **Shay Rojansky's blog** — lead engineer on Npgsql and a frequent EF Core contributor:
  <https://www.roji.org/>
- **Andrew Lock — "Exploring EF Core"** posts: <https://andrewlock.net/>
- **Nick Chapsas — EF Core deep dives** on YouTube (community, very clear): <https://www.youtube.com/@nickchapsas>

## Libraries we touch this week

- **`Microsoft.EntityFrameworkCore`** — the abstract package; pulled in by every provider.
- **`Microsoft.EntityFrameworkCore.Sqlite`** — the SQLite provider; our default this week.
- **`Microsoft.EntityFrameworkCore.Design`** — required to use `dotnet ef migrations`; design-time only, never a runtime dependency.
- **`Microsoft.EntityFrameworkCore.Tools`** — the package that lets `dotnet ef ...` find your `DbContext`.

## Editors

Unchanged from Weeks 1 and 2.

- **VS Code + C# Dev Kit** (primary): <https://code.visualstudio.com/docs/csharp/get-started>
- **JetBrains Rider Community** (secondary, free for non-commercial): <https://www.jetbrains.com/rider/>
- The new bit this week: a **SQLite viewer**. VS Code has the **"SQLite Viewer"** extension (free); Rider has **"Database tools"** built-in (free in Community for SQLite). Both let you open the `.db` file your migrations produced and inspect tables, rows, and the schema EF Core generated.

## Free books and chapters

- **"Get started with EF Core"** — free Microsoft Learn module path:
  <https://learn.microsoft.com/en-us/training/paths/build-data-driven-net-applications/>
- **"Persist and retrieve relational data with EF Core"** — the free Learn module that pairs with this week:
  <https://learn.microsoft.com/en-us/training/modules/persist-data-ef-core/>
- **"EF Core in Action, 3rd Edition" — sample chapter**: Jon P Smith hosts a free chapter on his blog covering relationships.

## Videos (free, no signup)

- **"What's new in EF Core 9"** — official, ~30 min: search the **dotnet** YouTube channel for the most recent "EF Core 9" talk: <https://www.youtube.com/@dotnet>
- **"EF Core Community Standup"** — monthly archive on the .NET channel; pick any recent episode: <https://www.youtube.com/playlist?list=PL1rZQsJPBU2StolNg0aqvQswETPcYnNKL>
- **Nick Chapsas — "Stop using ToList in EF Core"** (community): <https://www.youtube.com/@nickchapsas>

## Tools you'll use this week

- **`dotnet` CLI** — same as before.
- **`dotnet ef` CLI** — install once with `dotnet tool install --global dotnet-ef`. Required for `migrations add`, `database update`, `migrations script`. Reinstall after every major .NET SDK upgrade.
- **`sqlite3`** — preinstalled on macOS and most Linux. Hits the `.db` file directly. We use `.schema`, `.tables`, and a few `SELECT`s in the exercises.
- **An `.http` file** — yes, even this week. EF Core changes the storage but not the HTTP surface.

## The spec — when you need to be exact

- **`Microsoft.EntityFrameworkCore` 9.0 release notes**:
  <https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew>
- **SQLite syntax reference** — what SQL the provider emits against SQLite:
  <https://sqlite.org/lang.html>
- **ANSI SQL-92 quick reference** — when you want to know what's portable across providers:
  <https://en.wikibooks.org/wiki/SQL/SQL-92>

## Glossary cheat sheet

Keep this open in a tab.

| Term | Plain English |
|------|---------------|
| **`DbContext`** | The EF Core unit of work + change tracker. One instance per logical operation; usually scoped per HTTP request. Not thread-safe. |
| **`DbSet<T>`** | A queryable, mutable collection of entities of type `T`. Acts as both a `Repository` (queries) and an insertion point (`Add`). |
| **Entity** | A C# class mapped to a table. Identified by a primary key; tracked by the `DbContext`. |
| **Convention** | A rule EF Core applies automatically — e.g. "a property called `Id` is the primary key." |
| **Data annotation** | A C# attribute like `[Required]` or `[MaxLength(120)]` that informs the EF Core model. |
| **Fluent API** | The chained-method configuration syntax in `OnModelCreating` — strictly more powerful than annotations. |
| **Migration** | A pair of `Up`/`Down` methods that describe a schema change. Each migration is a C# file plus a corresponding model snapshot. |
| **Model snapshot** | A single C# file (`ModelSnapshot.cs`) that records the *current* model state. EF Core diffs the model against this file to generate the next migration. |
| **Change tracker** | The `DbContext` component that records every property mutation on every loaded entity until `SaveChangesAsync`. |
| **`AsNoTracking()`** | Tells the query "do not register results with the change tracker." Faster, read-only. |
| **Eager loading** | Loading related entities up-front with `Include` / `ThenInclude`. |
| **Lazy loading** | Loading related entities on first property access. Off by default in EF Core; requires the `Proxies` package. |
| **`IQueryable<T>`** | A LINQ source whose operators *translate* to SQL — not execute in process. |
| **Client evaluation** | LINQ operators executed in C# after the SQL has run. Almost always a bug; EF Core 3+ throws on most accidental cases. |
| **N+1 query** | Loading a parent list and then issuing one separate query per parent's children. A classic bug; fix with `Include` or projection. |
| **Optimistic concurrency** | "Read, modify, write — but check the row hasn't changed underneath you." Implemented with a row-version column. |
| **`DbUpdateConcurrencyException`** | What `SaveChangesAsync` throws when the row version on disk does not match the row version you read. |
| **Connection string** | The text blob that tells the provider how to find the database. SQLite's is just `Data Source=ledger.db`. |
| **Compiled query** | A LINQ expression you cache once and execute repeatedly without re-compiling. Useful for very hot paths. |
| **`FromSqlInterpolated`** | The escape hatch — write raw SQL with C# string interpolation that the provider parametrizes safely. |

---

*If a link 404s, please open an issue so we can replace it.*
