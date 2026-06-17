# Lecture 1 — `DbContext`, the Migrations Workflow, and the Change Tracker

> **Time:** 2 hours. Take the `DbContext` and migrations material in one sitting and the change-tracker material in a second sitting. **Prerequisites:** Week 2 (`.csproj` literacy, DI registration) and Week 5 (LINQ fluency). **Citations:** the EF Core overview at <https://learn.microsoft.com/en-us/ef/core/>, the migrations chapter at <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/>, and the change-tracking chapter at <https://learn.microsoft.com/en-us/ef/core/change-tracking/>.

## 1. Why EF Core, and why this lecture first

Every backend system in 2026 talks to a relational database for the data that has to survive a process restart. The options for that conversation are, in increasing order of abstraction: hand-written `ADO.NET` (`SqlConnection`, `SqlCommand`, `DataReader`); a micro-ORM such as `Dapper` (parameterized SQL plus typed materialization, but you write the SQL); and a full ORM such as Entity Framework Core (you write LINQ, the framework writes the SQL, materializes the rows, and tracks the changes for you). EF Core is the option you reach for when the gain from the framework writing the boilerplate outweighs the cost of the framework occasionally surprising you with a query you would not have written by hand. For the kinds of CRUD-heavy services that dominate working programmer time — order management, billing, content management, anything that looks like "rows and forms" — the trade is overwhelmingly favourable.

This lecture is first because **everything else in the week depends on you being fluent with the `DbContext`'s lifecycle and reading the SQL log it emits**. The N+1 problem in Lecture 2 is invisible without a SQL log. The raw-SQL escape hatches in Lecture 3 are dangerous without an understanding of what the safe path looks like. The change tracker is the engine that produces the diffs that become `UPDATE` statements at `SaveChangesAsync` time. We start at the foundation and build up.

Read this lecture alongside three Microsoft Learn pages, open in browser tabs: the EF Core overview, the migrations chapter, and the change-tracking chapter. The lecture's job is to give you a single 90-minute path through the material that those three pages, read independently, would have taken three hours to cover.

## 2. A minimum-viable `DbContext`

A `DbContext` is the unit of work in EF Core. It owns a database connection, the change tracker, the model metadata, and the query pipeline. The minimum viable example is approximately twenty lines:

```csharp
#nullable enable
using Microsoft.EntityFrameworkCore;

namespace Crunch.Catalog;

public sealed class CatalogDb : DbContext
{
    public CatalogDb(DbContextOptions<CatalogDb> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = new();
}
```

Five constructs are worth naming:

1. The class derives from `DbContext`. That base class brings the connection, the tracker, and the query pipeline.
2. The constructor takes a `DbContextOptions<TContext>`. That is the configuration object that carries the provider choice (`UseSqlite`, `UseNpgsql`, `UseSqlServer`), the connection string, and the diagnostic hooks. It is passed by the DI container, not constructed inline.
3. Each entity type lives behind a `DbSet<T>` property. The property is the root of a LINQ query.
4. Entity properties are simple CLR properties. EF Core's default convention is that a property called `Id` (or `<TypeName>Id`) is the primary key, integer keys are auto-incrementing, and a navigation property of type `T` matched with a `TId` foreign-key property establishes the relationship without any further configuration.
5. Reference and collection navigations are nullable-aware. `Product.Category` is `Category?` because the navigation may not be loaded; `Category.Products` is non-null because the collection is initialized to empty.

Citation for the conventions: <https://learn.microsoft.com/en-us/ef/core/modeling/relationships/conventions>. The conventions are the productivity dividend; you can override every one of them via the fluent API in `OnModelCreating`, but for the first two lectures we let them run.

## 3. Registering the `DbContext` in DI

In an ASP.NET Core app, the registration looks like this:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CatalogDb>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Catalog"));
    if (builder.Environment.IsDevelopment())
    {
        options.LogTo(Console.WriteLine, LogLevel.Information);
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

var app = builder.Build();
```

Four things are worth naming:

1. **`AddDbContext<>` registers the context as `Scoped` by default.** Scoped means "one instance per HTTP request" in ASP.NET Core. That is almost always the right answer; a `DbContext` is not thread-safe and is not designed to outlive a unit of work. Read the lifetime discussion at <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues> before you reach for `Singleton`.
2. **`UseNpgsql` (or `UseSqlite`, or `UseSqlServer`) is provider-specific.** Each provider package ships its own `UseXxx` extension method. Swapping providers is a one-line change in this method, but only if you have stayed within the LINQ surface that all providers support; the moment you call `EF.Functions.PostgresJsonContains(...)`, the swap stops being free.
3. **`LogTo(Console.WriteLine, LogLevel.Information)` is the SQL log.** Every command EF emits is printed to your console at `Information` level. The cost is non-trivial in production (it serialises every query into a string), so guard it on `IsDevelopment()`. Citation: <https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/extensions-logging>.
4. **`EnableSensitiveDataLogging()` includes parameter values in the log.** Without it, the log shows `WHERE Email = @p0`; with it, the log shows `WHERE Email = @p0 [@p0='alice@example.com']`. The flag is named "sensitive" for a reason — in production it would surface credentials and PII into your log pipeline. Development only.

## 4. The migrations workflow

A migration is a pair of methods — `Up` (apply the change) and `Down` (revert it) — that EF generates by diffing your current `DbContext` model against the previous migration's snapshot.

### 4.1 The four commands

The four commands you will run every week:

```bash
# Add a new migration. The name describes the *intent* of the change.
dotnet ef migrations add InitialCreate

# Apply pending migrations to the development database.
dotnet ef database update

# List all migrations and which are applied.
dotnet ef migrations list

# Emit a SQL script suitable for production application.
dotnet ef migrations script --idempotent --output deploy.sql
```

Citation for the full reference: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing>.

### 4.2 What `dotnet ef migrations add` produces

After you run `dotnet ef migrations add InitialCreate`, three files appear in the project's `Migrations/` directory:

- `20260514120000_InitialCreate.cs` — the migration itself. Contains `Up` and `Down` methods that call into `MigrationBuilder` to create tables, add columns, etc. The timestamp prefix orders migrations.
- `20260514120000_InitialCreate.Designer.cs` — a partial class that captures the model state at the moment of this migration, used by the diff engine when you add the next migration.
- `CatalogDbModelSnapshot.cs` — the single per-context snapshot file that always represents the *current* model state, used by every new migration's diff.

The rule that matters: **never edit a migration after it has shipped to a non-development database**. Once `20260514120000_InitialCreate` has been applied to staging or production, its `Up` is part of the database's history. If you edit it later, the snapshot will not match what the database actually contains, and the next migration will be wrong. If you need to fix something, write a new migration that fixes it forward.

### 4.3 The `Up` and `Down` shape

A typical `Up`:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "Categories",
        columns: table => new
        {
            Id = table.Column<int>(nullable: false)
                       .Annotation("Npgsql:ValueGenerationStrategy",
                                   NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            Name = table.Column<string>(maxLength: 64, nullable: false)
        },
        constraints: table => table.PrimaryKey("PK_Categories", x => x.Id));

    migrationBuilder.CreateTable(
        name: "Products",
        columns: table => new
        {
            Id = table.Column<int>(nullable: false)
                       .Annotation("Npgsql:ValueGenerationStrategy",
                                   NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            Name = table.Column<string>(maxLength: 128, nullable: false),
            Price = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
            CategoryId = table.Column<int>(nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_Products", x => x.Id);
            table.ForeignKey(
                name: "FK_Products_Categories_CategoryId",
                column: x => x.CategoryId,
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        });

    migrationBuilder.CreateIndex(
        name: "IX_Products_CategoryId",
        table: "Products",
        column: "CategoryId");
}
```

The migration C# is the same shape regardless of the provider. The annotations (`Npgsql:ValueGenerationStrategy`, `SqlServer:Identity`, etc.) carry the provider-specific bits. The `Down` is the literal inverse — drop the foreign key, drop the index, drop the table, drop the other table.

### 4.4 Applying migrations: dev vs production

In development, `dotnet ef database update` does exactly what it sounds like: it opens a connection, reads the `__EFMigrationsHistory` table to find the last applied migration, runs every `Up` after that, and inserts a row into `__EFMigrationsHistory` for each. This is convenient and dangerous in exactly the same way: it works fine against the developer's local database and would be a catastrophe against production.

In production, you generate an **idempotent SQL script**:

```bash
dotnet ef migrations script --idempotent --output deploy.sql
```

The script is "idempotent" in the strict sense: every `Up` is wrapped in an `IF NOT EXISTS` check against `__EFMigrationsHistory`. Applying the script to a database that already has all migrations is a no-op; applying it to a database missing the last three migrations runs exactly those three; applying it to a fresh database runs all of them. Hand the script to whatever process owns production schema changes — a CI step that runs `psql -f deploy.sql`, a release-pipeline gate, a DBA's manual review and `\i`.

Citation: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying>. Read the section titled "SQL scripts" carefully; the difference between the idempotent and non-idempotent shapes matters.

### 4.5 The `Database.MigrateAsync` runtime API

EF Core also exposes `db.Database.MigrateAsync()`, which applies pending migrations at app startup. This is sometimes the right answer for small, single-instance deployments where there is no separate DBA process and the app owns the database exclusively. It is **almost never** the right answer for multi-instance deployments: two instances starting simultaneously will race, two instances applying different migrations because they ship at different versions will fight. The default rule: emit the script, hand it to the deployment process, do not run `MigrateAsync()` at boot. Cite <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime>.

### 4.6 Removing a migration

`dotnet ef migrations remove` removes the most recent migration *if it has not been applied to the database*. If the migration has been applied, the command refuses and tells you to first `dotnet ef database update <PreviousMigrationName>` to revert it, then remove. This is a guardrail; respect it.

## 5. Reading the SQL log

With `LogTo(Console.WriteLine, LogLevel.Information)` configured, every query produces output like:

```
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (4ms) [Parameters=[@__id_0='?' (DbType = Int32)], CommandType='Text', CommandTimeout='30']
      SELECT p."Id", p."CategoryId", p."Name", p."Price"
      FROM "Products" AS p
      WHERE p."Id" = @__id_0
      LIMIT 1
```

Five fields are worth naming:

1. **Event ID `20101`** is `RelationalEventId.CommandExecuted`. The full event-ID table is at <https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.relationaleventid>. The most useful entries: `20100` (`CommandExecuting`), `20101` (`CommandExecuted`), `20102` (`CommandError`).
2. **The elapsed time** in parentheses is the *server-reported* execution time, not the round-trip time. For a query taking 4ms server-side over a 50ms network the log will show 4ms; the real wall time is 54ms.
3. **The parameter list** uses `?` as the value placeholder *unless* you enabled `EnableSensitiveDataLogging`, in which case the actual value is printed.
4. **The SQL** is provider-specific (note the `LIMIT 1` for PostgreSQL; SQL Server emits `SELECT TOP 1`). Read it as the literal text the database executed.
5. **`LIMIT 1`** is what `FirstOrDefaultAsync` translates to. `SingleOrDefaultAsync` translates to `LIMIT 2` (so the materializer can throw if there is more than one). This is a small but illustrative example of how the LINQ method you choose changes the SQL EF emits.

The non-execution alternative — useful when you want to inspect the SQL of a query without running it — is `IQueryable<T>.ToQueryString()`:

```csharp
var query = db.Products.Where(p => p.Price > 100m).OrderBy(p => p.Name);
Console.WriteLine(query.ToQueryString());
```

`ToQueryString` returns the SQL as a string with placeholder values. It is the same translation pipeline that runs at execution, but no I/O happens. Use it in unit tests to assert that a query has the SQL shape you intended. Citation: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toquerystring>.

## 6. The change tracker — the heart of EF Core

The change tracker is the dictionary EF Core keeps inside each `DbContext` instance that maps every loaded entity to one of five states. Read the chapter at <https://learn.microsoft.com/en-us/ef/core/change-tracking/> end-to-end; this section summarises the lessons.

### 6.1 The five states

```csharp
public enum EntityState
{
    Detached,
    Unchanged,
    Deleted,
    Modified,
    Added
}
```

Citation: the source at <https://github.com/dotnet/efcore/blob/main/src/EFCore/EntityState.cs>.

- **`Detached`** — the entity exists in C# memory but is not tracked. `new Product { Name = "X" }` is `Detached` until you call `db.Products.Add(...)`. A query result with `.AsNoTracking()` stays `Detached`.
- **`Unchanged`** — tracked, but with no modifications since it was loaded or attached. `await db.Products.FindAsync(7)` produces an `Unchanged` entity.
- **`Modified`** — tracked, with at least one property different from the snapshot taken at load time. `product.Name = "Y"` moves the entity to `Modified` (after `DetectChanges` runs; see below).
- **`Added`** — tracked, with no row in the database yet. `db.Products.Add(new Product { Name = "Z" })` produces an `Added` entity. The next `SaveChangesAsync` emits an `INSERT`.
- **`Deleted`** — tracked, with the database row present but slated for removal. `db.Products.Remove(p)` moves a previously-tracked entity to `Deleted`. The next `SaveChangesAsync` emits a `DELETE`.

### 6.2 The snapshot

When EF tracks an entity, it takes a **snapshot** — a copy of every scalar property — and stores it alongside the entity. At `SaveChangesAsync` time, EF compares the current property values to the snapshot. Anything different is a candidate for the `UPDATE`'s `SET` clause; anything equal is omitted. The result is that `UPDATE` statements EF emits update only the columns that actually changed.

The snapshot lives in the `InternalEntityEntry` for the entity. Source link: <https://github.com/dotnet/efcore/blob/main/src/EFCore/ChangeTracking/Internal/InternalEntityEntry.cs>. The fields named `_originalValues` and `_currentValues` are the relevant ones.

### 6.3 When the snapshot is taken — and the proxy alternative

By default, EF takes the snapshot at load time using a generated "shadow" copy. This is the **snapshot change-tracking model**. It is the default, it is reliable, and its only cost is the memory of the duplicate per-property storage. For most workloads, the cost is invisible.

There is an alternative — **proxy change tracking** — where EF generates a runtime subclass of your entity with overridden setters that notify the tracker on every assignment. Proxies are enabled via `UseChangeTrackingProxies` in `optionsBuilder` and require every property to be `virtual`. The dividend is that `DetectChanges` becomes a no-op (the proxy has already notified the tracker), which matters when you call `SaveChangesAsync` on a context with thousands of tracked entities. The cost is that your entities are no longer plain classes — they are subclassed at runtime, which interferes with serialization, equality, and any code that uses `GetType()`. The standing advice: **start with snapshot tracking; move to proxies only if profiling shows `DetectChanges` is a measurable problem**. Citation: <https://learn.microsoft.com/en-us/ef/core/change-tracking/change-detection>.

### 6.4 `DetectChanges`

The snapshot model needs a moment to compare the current values to the originals. That moment is `ChangeTracker.DetectChanges`. It runs automatically:

- Before `SaveChangesAsync` (so the diff is current).
- Before queries that need to see committed in-memory state (most don't).
- Before `Entry(entity).State` is read.

`DetectChanges` is O(n) in the number of tracked entities times the number of properties per entity. For a context with 10,000 tracked entities each with 20 properties, that is 200,000 reference comparisons every time the tracker decides it needs a refresh. For 99% of workloads, that is microseconds and you ignore it. For the remaining 1% — bulk-load scenarios, ETL jobs — you can disable auto-detection:

```csharp
db.ChangeTracker.AutoDetectChangesEnabled = false;
// load 10,000 entities, mutate them, call db.ChangeTracker.DetectChanges() exactly once
await db.SaveChangesAsync();
db.ChangeTracker.AutoDetectChangesEnabled = true;
```

This is a performance lever, not a correctness lever — turning it off does not change what gets saved, it changes *when EF looks*. Cite the discussion at <https://learn.microsoft.com/en-us/ef/core/change-tracking/change-detection#disabling-automatic-changes-detection>.

### 6.5 A walk through one round trip

The single best way to internalise the tracker is to print its state between every step. The following six lines do that:

```csharp
var p = await db.Products.FindAsync(7);
Console.WriteLine($"after FindAsync: {db.Entry(p!).State}");          // Unchanged
p!.Price = 99.99m;
Console.WriteLine($"after assignment: {db.Entry(p).State}");          // Modified
await db.SaveChangesAsync();
Console.WriteLine($"after SaveChanges: {db.Entry(p).State}");         // Unchanged
```

Three lines of state output. Three states, in order: `Unchanged`, `Modified`, `Unchanged`. The `SaveChangesAsync` call emits an `UPDATE Products SET Price = @p0 WHERE Id = @p1` (the SQL log proves it), the row's snapshot is replaced by the new values, and the state returns to `Unchanged`. The entity is *still tracked*; it could be modified again and saved again in the same `DbContext` lifetime.

### 6.6 Inspecting the whole tracker

`db.ChangeTracker.Entries()` returns one `EntityEntry` per tracked entity. Each entry has `Entity`, `State`, `OriginalValues`, `CurrentValues`, and per-property navigators. The debug view, which prints the entire tracker in a human-readable form, is:

```csharp
Console.WriteLine(db.ChangeTracker.DebugView.LongView);
```

Output looks like:

```
Product {Id: 7} Modified
  Id: 7 PK
  CategoryId: 3 FK
  Name: 'Hex Wrench'
  Price: 99.99 Modified Originally 89.99
  Category: <null>
```

Cite the debug-view chapter: <https://learn.microsoft.com/en-us/ef/core/change-tracking/debug-views>.

## 7. Tracking vs no-tracking: the single biggest read-path lever

For any query whose result you do not intend to modify and call `SaveChangesAsync` on, **the tracker is pure overhead**. `AsNoTracking()` is the switch that skips the tracker:

```csharp
var report = await db.Products
    .AsNoTracking()
    .Where(p => p.Price > 100m)
    .OrderByDescending(p => p.Price)
    .Select(p => new { p.Id, p.Name, p.Price })
    .ToListAsync();
```

Three things change when `AsNoTracking` is in the chain:

1. **No tracker insertion.** Each materialized row does not enter the change tracker. The dictionary lookup, the snapshot copy, and the identity-resolution check are all skipped.
2. **No identity resolution.** If the same row appears twice in the result set (which can happen with `Include` of a many-to-many), no-tracking returns two distinct C# instances. The tracking variant would have returned the same instance twice. For most read paths this is fine; for graphs with shared references it can be surprising.
3. **The result is `Detached`.** You can mutate the returned objects in C# all you want; calling `SaveChangesAsync` after doing so will save *nothing*, because the tracker never saw the mutation. This is the contract: no-tracking is for reads.

The intermediate option, `AsNoTrackingWithIdentityResolution()`, **keeps** identity resolution but **skips** the tracker. The implementation is a per-query (not per-context) dictionary that exists only for the duration of the materialization. The result is that the two-instances surprise from item 2 goes away, but you still get the read-path performance benefit. Citation: <https://learn.microsoft.com/en-us/ef/core/querying/tracking#identity-resolution>.

The performance numbers from the EF team's perf guide (<https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#tracking-no-tracking-and-identity-resolution>), reproduced on a 1,000-row Customer table on SQL Server:

| Method                                    | Mean    | Allocated |
|-------------------------------------------|--------:|----------:|
| AsTracking                                | 1.6 ms  | 1.9 MB    |
| AsNoTracking                              | 1.2 ms  | 1.2 MB    |
| AsNoTrackingWithIdentityResolution        | 1.3 ms  | 1.3 MB    |

The takeaway is not "AsNoTracking is 25% faster"; the takeaway is "the tracker costs 30-50% more allocation per row, and for read-only paths that allocation is wasted." On a list endpoint that runs 1,000 times a second, the difference is half a gigabyte per second of allocation pressure that the GC has to clean up.

The endpoint-design rule that follows: **every read-only path uses `AsNoTracking`. Every write path does not.** Tag your endpoints in your head with "Q" (query, read-only) or "C" (command, mutates). Q's get `AsNoTracking`; C's do not. The exercise for Tuesday measures this on your machine.

## 8. The detached-entity write path

For a write path that arrives over HTTP — a `PUT /api/products/{id}` that takes a JSON body and updates the row — there is a decision to make. The entity has been deserialized from JSON into a C# object; that object is `Detached` because no `DbContext` has seen it. To save the change, you have two patterns.

### 8.1 Load-then-update

```csharp
var existing = await db.Products.FindAsync(id);
if (existing is null) return Results.NotFound();
existing.Name = dto.Name;
existing.Price = dto.Price;
await db.SaveChangesAsync();
```

`FindAsync` loads the row and tracks it as `Unchanged`. The two assignments move it to `Modified` (after the next `DetectChanges`). `SaveChangesAsync` emits an `UPDATE` with only the columns that actually changed (because the snapshot lets EF compute the minimum diff). This is the **correct default**. It costs one extra `SELECT` round-trip per write, which is almost always a price worth paying.

### 8.2 Attach-and-overwrite

```csharp
var product = new Product { Id = id, Name = dto.Name, Price = dto.Price };
db.Products.Update(product);
await db.SaveChangesAsync();
```

`Update` attaches the detached entity as `Modified` for every property and emits an `UPDATE` that overwrites every column. No `SELECT` round trip. This is faster, and it is correct *only* when the DTO you received contains every column you want to keep. If the DTO omits a property — because the client did not send it, or because you have a column the API does not expose — the `UPDATE` will set that column to its default value, silently. The attach pattern is a foot-gun unless you understand the full-overwrite semantics.

Use Pattern 8.1 by default; reach for 8.2 only when you have profiled and the extra `SELECT` is the bottleneck and you can demonstrate that the DTO is total.

## 9. Identity resolution and the dictionary inside the tracker

The change tracker is implemented (in part) as a dictionary keyed by `(entity type, primary-key tuple)`, mapping to the `EntityEntry`. This is **identity resolution**: when you do something that would re-load a row already in the tracker, EF returns the existing instance instead of creating a duplicate.

```csharp
var p1 = await db.Products.FindAsync(7);
var p2 = await db.Products.FirstAsync(p => p.Id == 7);
Console.WriteLine(ReferenceEquals(p1, p2));   // True
```

This is **correct behaviour** and it is **why the tracker exists**. Without identity resolution, your code would have to manually compare `Id` values to know whether two references point to "the same" entity. With it, reference equality is meaningful within a `DbContext` lifetime.

The cost is that the dictionary lookup happens on every materialized row. For 1,000-row reads where every row is fresh, the lookup is 1,000 cache-friendly probes — cheap. For 100,000-row bulk loads, the dictionary fills, the lookups slow down, and the memory footprint grows. The mitigations: `AsNoTracking` (skip the dictionary entirely), `AsNoTrackingWithIdentityResolution` (per-query dictionary that is freed after materialization), or bulk APIs that bypass the tracker (`ExecuteUpdateAsync` and `ExecuteDeleteAsync`; covered in Lecture 3).

## 10. Lecture-1 summary checklist

After this lecture you should be able to, without notes:

1. Write a minimum-viable `DbContext` with two entities and a navigation between them, plus the `AddDbContext<>` registration that wires it into ASP.NET Core DI.
2. Configure `LogTo(Console.WriteLine, LogLevel.Information)` and `EnableSensitiveDataLogging()` in development, and explain why both are off in production.
3. Run `dotnet ef migrations add`, `dotnet ef database update`, and `dotnet ef migrations script --idempotent`, and explain the difference between dev and prod migration application.
4. Read a SQL log line, identify the event ID, the elapsed time, the parameter set, and the literal SQL.
5. Use `ToQueryString()` to inspect the SQL of a query without executing it.
6. Name the five `EntityState` values and describe what causes each transition.
7. Explain when the snapshot is taken, when `DetectChanges` runs, and when you would turn off `AutoDetectChangesEnabled`.
8. Use `AsNoTracking` on a read-only query, `AsNoTrackingWithIdentityResolution` on a read-only query over a graph with shared references, and neither on a write path.
9. Choose between the load-then-update and attach-and-overwrite write patterns, defending the choice in one sentence.
10. Explain identity resolution and why it is a feature, not a coincidence.

Lecture 2 builds on this foundation to cover the loading strategies and the N+1 problem — the highest-leverage performance pathology you will diagnose this year.
