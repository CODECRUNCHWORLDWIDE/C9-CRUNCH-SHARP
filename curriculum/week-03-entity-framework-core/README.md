# Week 3 — Entity Framework Core

Welcome to **C9 · Crunch Sharp**, Week 3. Week 1 made the language ordinary. Week 2 put it behind an HTTP port. This week puts it on top of a real database. By Friday you should be able to scaffold an EF Core 9 `DbContext`, model six related entities with both data annotations and the fluent API, generate and apply migrations against a SQLite file, write `IQueryable<T>` queries that translate cleanly to SQL, and explain *exactly* what SQL the provider emitted — straight from the terminal, without ever opening Visual Studio or a separate database GUI.

This is still Phase 1 of the syllabus — the foundations phase — but Week 3 is the bridge into Phase 2's data layer. Microsoft Learn's official sequencing puts EF Core in Week 6, after async and DI. We pull it forward in C9 because every mini-project from Week 4 onward benefits from real persistence rather than a `ConcurrentDictionary`, and because the *easiest* way to internalize LINQ semantics is to watch `IQueryable<T>` translate to SQL and compare two providers (SQLite for local, PostgreSQL for prod) emitting subtly different statements for the same expression tree.

The first thing to internalize is that **EF Core is a query builder you don't have to write**. It is not an ORM in the 2005 sense — there is no XML mapping, no `HibernateTemplate`, no per-method SQL strings sprayed across the codebase. You write a LINQ expression against `DbSet<T>`; EF Core parses the expression tree, picks the right SQL dialect from your registered provider, opens a connection, runs the parametrized SQL, materializes the results into your entity types, and tracks every change you make until you call `SaveChangesAsync`. That round trip is the entire mental model. Master it and the rest of EF Core — migrations, change tracking, concurrency tokens, value converters — is configuration on top.

The second thing to internalize is that **change tracking is the magic, and it is also the cost**. The `DbContext` keeps a reference to every entity it has loaded, watches for property mutations through proxies or shadow state, computes the difference at `SaveChangesAsync` time, and emits the corresponding `INSERT`/`UPDATE`/`DELETE`. That is incredible ergonomics for write paths. It is also the single biggest performance footgun in EF Core — if you load 50,000 rows into a tracked query that you never intend to mutate, you have paid for change-tracking machinery for nothing. We will spend Lecture 2 making sure you know when to reach for `AsNoTracking()`, when to project into a DTO, and when to drop down to raw SQL with `FromSqlInterpolated`.

## Learning objectives

By the end of this week, you will be able to:

- **Scaffold** an EF Core 9 `DbContext` from `dotnet new` plus `dotnet add package`, register it with `AddDbContext<T>()` against SQLite, and explain every line of the connection-string-and-provider wiring.
- **Model** an entity graph using both `[Required]`/`[MaxLength]` data annotations and `IEntityTypeConfiguration<T>` fluent configuration — and explain which to reach for when.
- **Express** one-to-one, one-to-many, and many-to-many relationships in code; let EF Core's conventions infer foreign keys; and override the conventions when the inference is wrong.
- **Configure** value converters, owned types, table splitting, and shadow properties for the cases where the default mapping is not what you want.
- **Generate** a migration with `dotnet ef migrations add`, inspect its generated `Up`/`Down` methods, and apply it with `dotnet ef database update` against a SQLite file.
- **Diff** a migration against the live database with `dotnet ef migrations script` and explain what a CI/CD pipeline would do with the output.
- **Query** with `IQueryable<T>` using `Where`, `Select`, `Include`, `ThenInclude`, `GroupBy`, `Join`, and projection into DTOs — and read the emitted SQL with `LogTo(Console.WriteLine)`.
- **Recognize** the three classic EF Core performance traps — the N+1 query, the silent client-side evaluation, and the unbounded tracked load — and know the fix for each.
- **Choose** between `AsNoTracking()`, projection queries, compiled queries, and `FromSqlInterpolated` based on what the workload actually demands.
- **Reason** about transactions, optimistic concurrency tokens (`[Timestamp]` / `IsRowVersion()`), and `SaveChangesAsync` behaviour — including what happens when two requests update the same row.

## Prerequisites

- **Weeks 1 and 2** of C9 complete: you can scaffold a multi-project solution from the `dotnet` CLI, write Minimal API endpoints with `TypedResults`, register services with the right lifetime, and your `dotnet build` reflexively prints `Build succeeded · 0 warnings · 0 errors`.
- **SQL fluency** at the level of "I have written a JOIN with a GROUP BY and I know what a primary key is." We do not teach SQL from scratch; if you've shipped a feature against PostgreSQL or SQLite in any language, you are exactly the audience.
- A working `sqlite3` command-line tool on your PATH (`sqlite3 --version` should print something). On macOS it is preinstalled. On Linux: `apt install sqlite3` or equivalent. On Windows: download from <https://sqlite.org/download.html>.
- Nothing else. We start from `dotnet new console`, end at a working migrations workflow against the Week 2 Ledger domain, and never install a paid database tool.

## Topics covered

- The `DbContext` and `DbSet<T>` types: what they are, what they track, when they get disposed, and why `DbContext` is *not* thread-safe.
- The two configuration styles: data annotations (`[Key]`, `[Required]`, `[MaxLength]`, `[Column]`) and the fluent API in `OnModelCreating` or in `IEntityTypeConfiguration<T>` classes.
- Conventions: how EF Core infers primary keys, foreign keys, navigation properties, table names, and column types from your C# code — and the override points when the inference is wrong.
- Relationship modeling: one-to-one, one-to-many, many-to-many (with and without an explicit join entity), self-referencing relationships, cascade delete behaviour.
- Value converters, owned types, table splitting, shadow properties, and computed columns — the four hatches you reach for when the default isn't right.
- Migrations: `dotnet ef migrations add`, `dotnet ef migrations remove`, `dotnet ef migrations script`, `dotnet ef database update`, and the migrations history table.
- Provider differences: SQLite for local dev, PostgreSQL for production, what *moves* between them and what doesn't (column types, identity strategies, JSON columns).
- `IQueryable<T>` translation: which LINQ operators translate to SQL, which throw `InvalidOperationException`, which silently client-evaluate (and in EF Core 3+ they don't silently any more — they throw).
- Eager loading (`Include`/`ThenInclude`), explicit loading (`Entry(...).Reference(...).LoadAsync()`), and lazy loading (off by default, requires `Microsoft.EntityFrameworkCore.Proxies`).
- Change tracking: `EntityState`, `Attach`, `Update`, `Remove`, `SaveChangesAsync`, and the `ChangeTracker` API for debugging.
- Performance hatches: `AsNoTracking`, `AsSplitQuery`, compiled queries, `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (introduced EF Core 7, refined in EF Core 8 and 9), `FromSqlInterpolated`, `SqlQuery<T>`.
- Concurrency: optimistic concurrency tokens, `DbUpdateConcurrencyException`, the resolve-and-retry pattern.

## Weekly schedule

The schedule adds up to approximately **36 hours**. Treat it as a target, not a contract.

| Day       | Focus                                                | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | DbContext, DbSet, conventions, the first migration   |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | Relationships, fluent API, value converters          |    1h    |    2h     |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5h      |
| Wednesday | Migrations workflow, schema diffs, seeding           |    1h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     4.5h    |
| Thursday  | IQueryable translation, change tracking, performance |    2h    |    1.5h   |     1h     |    0.5h   |   1h     |     2h       |    0.5h    |     8.5h    |
| Friday    | Mini-project — Ledger on EF Core + SQLite            |    0h    |    0h     |     0h     |    0.5h   |   1h     |     3h       |    0.5h    |     5h      |
| Saturday  | Mini-project deep work, integration tests            |    0h    |    0h     |     0h     |    0h     |   1h     |     3h       |    0h      |     4h      |
| Sunday    | Quiz, review, polish                                 |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                      | **6h**   | **6.5h**  | **1h**     | **3.5h**  | **6h**   | **8.5h**     | **2.5h**   | **34h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | Curated Microsoft Learn, EF Core source, and open-source links |
| [lecture-notes/01-dbcontext-and-models.md](./lecture-notes/01-dbcontext-and-models.md) | `DbContext`, `DbSet<T>`, conventions, data annotations, fluent API, relationships, the first migration |
| [lecture-notes/02-migrations-and-queries.md](./lecture-notes/02-migrations-and-queries.md) | Migrations workflow, `IQueryable<T>` translation, change tracking, performance, raw SQL, concurrency |
| [exercises/README.md](./exercises/README.md) | Index of short coding exercises |
| [exercises/exercise-01-first-context.md](./exercises/exercise-01-first-context.md) | Scaffold a `DbContext`, add a single entity, run `migrations add` and `database update`, inspect the `.db` with `sqlite3` |
| [exercises/exercise-02-relationships.cs](./exercises/exercise-02-relationships.cs) | Fill-in-the-TODO entity graph that exercises 1-to-1, 1-to-many, and many-to-many relationships |
| [exercises/exercise-03-migrations.cs](./exercises/exercise-03-migrations.cs) | Add an `Author.Bio` column, generate a second migration, write the rollback `Down` by hand, verify with `sqlite3` |
| [challenges/README.md](./challenges/README.md) | Index of weekly challenges |
| [challenges/challenge-01-complex-query-performance.md](./challenges/challenge-01-complex-query-performance.md) | Diagnose three EF Core performance traps and fix each with the right tool |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on EF Core 9 |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for "Ledger Persistence" — Week 2's Ledger API, now backed by EF Core + SQLite with real migrations |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 612 ms
```

A nullable-reference warning is a bug. A `MIGRATIONS001` warning ("the model has changes since the last migration") is a bug. A pending model snapshot that does not match `OnModelCreating` is a bug. By the end of Week 3 you will have an ASP.NET Core 9 service that compiles clean, applies its migrations on startup against a SQLite file, exposes typed REST endpoints over the persisted Ledger domain, and survives 50 integration tests against a fresh database — all from ~700 lines of C# you wrote yourself.

## A note on what's not here

Week 3 introduces EF Core, but it does **not** introduce:

- **PostgreSQL deployment.** We mention `Npgsql.EntityFrameworkCore.PostgreSQL` and we point at the migration-compatibility story, but every example runs against SQLite. PostgreSQL becomes the production target in Phase 2.
- **Dapper.** EF Core handles every query this week. Dapper is the topic of a side-by-side comparison in Week 6 of the canonical syllabus; here we stay inside the EF Core surface so you internalize the *one* tool.
- **Repository and Unit-of-Work wrapper patterns.** EF Core's `DbContext` *is* a Unit of Work, and `DbSet<T>` *is* a Repository. Wrapping them again is almost always wrong; we'll discuss why in Lecture 2.
- **Compiled models and AOT.** EF Core 9 ships a compiled-model option for cold-start scenarios and a partial AOT story. Both belong in Week 12 (Performance).
- **CQRS, MediatR, or any architectural pattern beyond "your endpoints take a `DbContext` and use it."** That deliberate simplicity is the right starting point.

The point of Week 3 is a sharp, narrow tool: a relational schema modeled in C# that you can read, query, migrate, and version-control with confidence.

## Stretch goals

If you finish the regular work early and want to push further:

- Read the official **"EF Core overview"** end to end: <https://learn.microsoft.com/en-us/ef/core/>.
- Skim the **EF Core source** — `Microsoft.EntityFrameworkCore.Relational` is the most readable: <https://github.com/dotnet/efcore>.
- Read **Jon P Smith's "EF Core in Action, 3rd Edition"** sample chapter on relationships (free PDF on his blog).
- Watch the **EF Core community standup** archive on the .NET YouTube channel — every episode covers one EF Core feature in 45 minutes: <https://www.youtube.com/@dotnet>.
- Build a parallel PostgreSQL project that runs the *same* migrations the SQLite project does. Note which migrations behave identically and which need a provider-specific tweak.

## Up next

Continue to **Week 4 — Async, Channels, and Cancellation** once you have pushed the mini-project to your GitHub. The mini-project's `LedgerDbContext` becomes the substrate of Week 4's async streaming endpoint — `IAsyncEnumerable<Transaction>` straight off `DbSet<Transaction>`, no buffering, with a `CancellationToken` that actually cancels the in-flight SQL.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
