# Lecture 1 — DbContext and Models

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can scaffold an EF Core 9 `DbContext` against SQLite, model six related entities (with both data annotations and the fluent API), generate your first migration, apply it, and explain every line of `OnModelCreating` without referring to a tutorial.

If you only remember one thing from this lecture, remember this:

> **EF Core is a query builder you don't have to write.** You write a LINQ expression against `DbSet<T>`. EF Core walks the expression tree, picks the right SQL dialect from your registered provider, opens a connection, runs parametrized SQL, materializes the results into your entity types, and tracks every change you make until you call `SaveChangesAsync`. That round trip is the entire mental model. The rest — migrations, change tracking, value converters — is configuration on top.

---

## 1. The two configuration models

EF Core in 2026 ships **two** ways to describe your schema in the same library:

| Style | Where it lives | Best for |
|-------|----------------|----------|
| **Data annotations** | C# attributes on properties (`[Required]`, `[MaxLength(120)]`, `[Column("memo_text")]`) | Quick, obvious constraints close to the property |
| **Fluent API** | Method chains in `OnModelCreating` or in `IEntityTypeConfiguration<T>` classes | Anything an annotation cannot express, plus every relationship configuration |

The two coexist. You can have a single `DbContext` that uses annotations for "this column is required" and the fluent API for "this entity has a composite key with a cascade-delete behaviour." The choice between them is not technical; it is stylistic with one rule of thumb: **start with annotations, escalate to fluent when annotations cannot do the job**.

In C9 we follow that rule strictly. Lecture 1's first model is annotated; Lecture 1's second model — the one with relationships — moves the relationship configuration into a fluent `IEntityTypeConfiguration<T>`. We will see why in Section 5.

---

## 2. The smallest possible EF Core 9 app

Scaffold a fresh project. We want a console app to start, no HTTP surface yet, because EF Core has nothing to do with the web.

```bash
mkdir LedgerDb && cd LedgerDb
dotnet new console -n LedgerDb -o src/LedgerDb
cd src/LedgerDb
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
```

A note on those three packages:

- **`Microsoft.EntityFrameworkCore`** is the abstract package. It defines `DbContext`, `DbSet<T>`, `IQueryable<T>` extensions, and the change tracker. It does *not* know about any database.
- **`Microsoft.EntityFrameworkCore.Sqlite`** is the provider. It plugs into the abstract package and knows how to translate `IQueryable<T>` to SQLite SQL, open SQLite connections, and read SQLite types.
- **`Microsoft.EntityFrameworkCore.Design`** is design-time only. The `dotnet ef` CLI needs to load it to generate migrations. Mark it `PrivateAssets="all"` in production projects so it does not flow as a transitive dependency.

Then install the CLI tool (once per machine, not once per project):

```bash
dotnet tool install --global dotnet-ef
dotnet ef --version
```

You should see `9.0.x`. If you see `8.0.x`, run `dotnet tool update --global dotnet-ef` until the version matches your SDK.

Now write the smallest interesting model. Open `Program.cs` and replace its contents:

```csharp
using Microsoft.EntityFrameworkCore;

await using var db = new LedgerContext();
await db.Database.EnsureCreatedAsync();

db.Transactions.Add(new Transaction
{
    Date = DateOnly.FromDateTime(DateTime.UtcNow),
    Amount = 12.50m,
    Memo = "Coffee",
    Category = "food"
});
await db.SaveChangesAsync();

foreach (var t in await db.Transactions.AsNoTracking().ToListAsync())
{
    Console.WriteLine($"{t.Id,4} {t.Date} {t.Amount,8:F2} {t.Memo}");
}

public sealed class Transaction
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Memo { get; set; } = "";
    public string Category { get; set; } = "";
}

public sealed class LedgerContext : DbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite("Data Source=ledger.db");
}
```

Run it:

```bash
dotnet run
```

You should see something close to:

```
   1 2026-05-13    12.50 Coffee
```

That is a complete EF Core app. Forty-some lines of source. One file. No `appsettings.json`. No migration. The `EnsureCreatedAsync` call creates the database file and the table on first run; subsequent runs see the file already exists and skip creation.

Read those lines carefully. They are the entire shape of every EF Core app you will write this year:

1. **A class that derives from `DbContext`.** It exposes one or more `DbSet<T>` properties — one per entity type you want to track.
2. **An override of `OnConfiguring`** (or, in real apps, a constructor that takes `DbContextOptions<T>` and registers with DI). This is where you pick a provider and a connection string.
3. **POCO entity classes** — plain old C# objects. No base class, no attribute strictly required, just public read/write properties. EF Core finds them via convention.
4. **A `using` (or `await using`) block around the `DbContext`** so it disposes deterministically.

`EnsureCreatedAsync` is a development-only escape hatch. It is great for the first 30 seconds; it is terrible for week-100 of a real product because it does not version your schema. The rest of this lecture moves to **migrations**, which version every schema change in a file you commit to Git.

---

## 3. The `DbContext` lifecycle

A `DbContext` does four things:

1. **Holds a database connection.** It does not open one eagerly; it opens lazily on the first query or `SaveChanges`. By default the connection is closed and returned to the pool between operations.
2. **Tracks entities.** Every entity returned by a query (unless you opt out with `AsNoTracking`) is registered in the `ChangeTracker`. Mutating a tracked entity's property is enough to generate an `UPDATE` on the next `SaveChangesAsync`.
3. **Configures the model.** The first time `DbContext.Model` is accessed, EF Core walks every `DbSet<T>` property, applies every convention, runs `OnModelCreating`, and freezes the model. The frozen model is cached per-context-type for the life of the process.
4. **Disposes the connection and detaches all tracked entities** when `Dispose` or `DisposeAsync` is called.

Three rules follow from this:

- **A `DbContext` is not thread-safe.** Two threads calling `SaveChangesAsync` on the same instance is a race. Always: one context per logical operation.
- **A `DbContext` should be short-lived.** Long-lived contexts grow their change tracker indefinitely and eventually slow `SaveChangesAsync` to a crawl. In ASP.NET Core, the lifetime is one HTTP request — `AddDbContext<T>` registers it as scoped.
- **A `DbContext` should never be a singleton.** Captive-dependency-style bugs (the singleton scenario in last week's Lecture 2) are *especially* nasty with `DbContext` because of the change-tracker accumulation problem.

In ASP.NET Core you do not write `OnConfiguring`; you register the context with DI:

```csharp
builder.Services.AddDbContext<LedgerContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Ledger")));
```

…and the `DbContext` constructor takes `DbContextOptions<LedgerContext>` so the container can supply them:

```csharp
public sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
}
```

Note the primary-constructor syntax — clean in C# 13, and the most idiomatic way to write a `DbContext` in 2026.

`AddDbContext` registers the context as **scoped** by default. That is the correct lifetime in 99% of ASP.NET Core scenarios. The other 1% — background workers — uses `AddDbContextFactory<T>` and an explicit `using` block in the worker, the same pattern we saw with `IServiceScopeFactory` in Week 2.

---

## 4. Conventions: what EF Core figures out for free

Open the auto-generated SQLite database in `sqlite3`:

```bash
sqlite3 ledger.db ".schema Transactions"
```

You should see:

```sql
CREATE TABLE "Transactions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Transactions" PRIMARY KEY AUTOINCREMENT,
    "Date" TEXT NOT NULL,
    "Amount" TEXT NOT NULL,
    "Memo" TEXT NOT NULL,
    "Category" TEXT NOT NULL
);
```

Five things were inferred from the C# class:

1. **The table name.** `DbSet<Transaction> Transactions` was pluralized to `Transactions`. EF Core's convention is to use the `DbSet<T>` property name verbatim.
2. **The primary key.** A property named `Id` (or `<EntityName>Id`) is the primary key by convention.
3. **`INTEGER` and `AUTOINCREMENT`.** Because `Id` is an `int` and is the primary key, the SQLite provider added `AUTOINCREMENT`. (PostgreSQL would generate a `BIGSERIAL` or `IDENTITY` column here instead — the *convention* is "this is a server-generated id"; the *implementation* is provider-specific.)
4. **`NOT NULL` everywhere.** `int`, `DateOnly`, `decimal`, and `string` (with nullable reference types enabled) are all non-nullable in the C# model. EF Core respects that and emits `NOT NULL` constraints. If you wanted `Memo` to be nullable, you would declare it `string?` and the column would be nullable too.
5. **The column types.** `DateOnly` maps to `TEXT` in SQLite (ISO-8601). `decimal` maps to `TEXT` in SQLite (lossless string). `string` maps to `TEXT`. SQLite has only five storage classes, so the mapping is coarse. On PostgreSQL the same model would emit `date`, `numeric(18,2)`, and `text`.

If a convention is wrong for your case, override it with annotations or the fluent API. Here are the four overrides you will need most often:

```csharp
public sealed class Transaction
{
    [Key]                                              // explicit PK
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    [Column(TypeName = "decimal(18,2)")]               // exact column type
    public decimal Amount { get; set; }

    [Required, MaxLength(200)]                         // NOT NULL + length
    public string Memo { get; set; } = "";

    [Required, MaxLength(80)]
    public string Category { get; set; } = "";
}
```

Or equivalently, in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var tx = modelBuilder.Entity<Transaction>();
    tx.HasKey(t => t.Id);
    tx.Property(t => t.Amount).HasColumnType("decimal(18,2)");
    tx.Property(t => t.Memo).IsRequired().HasMaxLength(200);
    tx.Property(t => t.Category).IsRequired().HasMaxLength(80);
}
```

Two styles, same outcome. Use whichever is closer to where the property lives.

---

## 5. Relationships: where the fluent API earns its keep

Take the Ledger domain a step further. A real ledger has not just transactions but **accounts** and **categories**, and each transaction belongs to exactly one account and exactly one category. Each account can have many transactions; each category can have many transactions. Two one-to-many relationships from `Account` and `Category` into `Transaction`.

Model it:

```csharp
public sealed class Account
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Name { get; set; } = "";
    [Required, MaxLength(3)]   public string Currency { get; set; } = "USD";

    public List<Transaction> Transactions { get; set; } = [];
}

public sealed class Category
{
    public int Id { get; set; }
    [Required, MaxLength(80)] public string Name { get; set; } = "";

    public List<Transaction> Transactions { get; set; } = [];
}

public sealed class Transaction
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, MaxLength(200)] public string Memo { get; set; } = "";

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
```

The fluent configuration goes in a dedicated `IEntityTypeConfiguration<T>` class — one per entity. This keeps `OnModelCreating` clean as the model grows:

```csharp
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.HasOne(t => t.Account)
         .WithMany(a => a.Transactions)
         .HasForeignKey(t => t.AccountId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(t => t.Category)
         .WithMany(c => c.Transactions)
         .HasForeignKey(t => t.CategoryId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(t => t.Date);
        b.HasIndex(t => new { t.AccountId, t.Date });
    }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerContext).Assembly);
}
```

Three deliberate choices in that fluent block:

- **`OnDelete(DeleteBehavior.Restrict)`** rather than the default cascade. Cascading deletes from an account to all its transactions is almost always a bug in financial software. Restrict means "you must explicitly delete the transactions first; the database will refuse the account delete otherwise."
- **`HasIndex(t => t.Date)`** because every report we will write filters or sorts by `Date`. EF Core does not infer indexes from queries; you declare them.
- **`HasIndex(t => new { t.AccountId, t.Date })`** because account-scoped date queries are the common report shape ("all transactions for account 7 between January 1 and March 31"). A composite index serves that filter without a full table scan.

The `null!` on the navigation properties is a deliberate compromise. EF Core fills those in after construction, so they cannot be non-nullable in the strict sense; but you don't want to deal with `?` everywhere. `null!` tells the compiler "trust me." If that bothers you, the alternative is to mark the navigation properties `required` and let the C# 11+ `required` keyword enforce them at construction time.

---

## 6. Many-to-many — with and without the explicit join entity

The third relationship type is many-to-many. Imagine each transaction can carry multiple **tags** ("recurring", "tax-deductible", "shared"). EF Core 5 made this almost transparent — you just write two navigation collections:

```csharp
public sealed class Tag
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string Name { get; set; } = "";

    public List<Transaction> Transactions { get; set; } = [];
}

public sealed class Transaction
{
    // ...existing properties...
    public List<Tag> Tags { get; set; } = [];
}
```

EF Core sees the two collections, generates a hidden join table `TagTransaction` with `TagsId` and `TransactionsId` columns, and lets you write `tx.Tags.Add(tag)` to associate the two. This "skip-navigation" style is enough for 80% of many-to-many cases.

The other 20% need a property on the join itself — a timestamp, a user id, a count. For those you declare the join entity explicitly:

```csharp
public sealed class TransactionTag
{
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public DateTimeOffset AddedAt { get; set; }
    public string? AddedBy { get; set; }
}

// In TransactionConfiguration:
b.HasMany(t => t.Tags)
 .WithMany(tag => tag.Transactions)
 .UsingEntity<TransactionTag>(
     j => j.HasOne(tt => tt.Tag).WithMany().HasForeignKey(tt => tt.TagId),
     j => j.HasOne(tt => tt.Transaction).WithMany().HasForeignKey(tt => tt.TransactionId),
     j =>
     {
         j.HasKey(tt => new { tt.TransactionId, tt.TagId });
         j.Property(tt => tt.AddedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
     });
```

The same `tx.Tags.Add(tag)` shorthand still works; EF Core inserts a `TransactionTag` row with `AddedAt` defaulted from the database side. You only need to touch `TransactionTag` directly when you want to query its payload columns.

---

## 7. Value converters, owned types, and shadow properties

Three less-common modeling tools every learner should *recognize* but not over-use:

**Value converters.** When the C# representation does not match the database representation. `DateOnly` → `string` (ISO-8601) is a built-in. A C# `enum` → an `int` in SQL is automatic. But a C# `enum` → a `string` in SQL — sometimes nicer for human-readable dumps — needs an explicit converter:

```csharp
public enum TransactionKind { Credit, Debit }

b.Property(t => t.Kind).HasConversion<string>().HasMaxLength(8);
```

**Owned types.** When a child object should be embedded *inside* the parent's table rather than getting its own. A common case is a `Money` value object that always lives with its containing entity:

```csharp
public sealed record Money(decimal Amount, string Currency);

public sealed class Account
{
    public int Id { get; set; }
    public Money Balance { get; set; } = new(0m, "USD");
}

// Configuration:
b.OwnsOne(a => a.Balance, m =>
{
    m.Property(p => p.Amount).HasColumnName("BalanceAmount").HasColumnType("decimal(18,2)");
    m.Property(p => p.Currency).HasColumnName("BalanceCurrency").HasMaxLength(3);
});
```

EF Core emits `BalanceAmount` and `BalanceCurrency` columns on the `Accounts` table — no separate `Money` table, no join.

**Shadow properties.** Columns that exist in the database but not on the C# class. The classic use case is a `CreatedAt` audit column you do not want to expose to handlers:

```csharp
b.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("CURRENT_TIMESTAMP");
b.HasIndex("CreatedAt");
```

Query the shadow value with `EF.Property<T>(...)`:

```csharp
var recent = await db.Transactions
    .OrderByDescending(t => EF.Property<DateTimeOffset>(t, "CreatedAt"))
    .Take(10)
    .ToListAsync();
```

Shadow properties are powerful but easy to abuse. Use them for cross-cutting metadata (audit columns, soft-delete flags, multi-tenant filters). Do not use them for domain concerns; those belong on the entity.

---

## 8. The first migration

You have a model. Generate the migration:

```bash
dotnet ef migrations add InitialCreate --output-dir Migrations
```

EF Core compares your current model against the most recent model snapshot (there is none yet, so it compares against "empty") and produces three files:

```
Migrations/
├── 20260513120000_InitialCreate.cs        ← the migration: Up + Down methods
├── 20260513120000_InitialCreate.Designer.cs ← metadata for the migration
└── LedgerContextModelSnapshot.cs           ← the current model state
```

Open `20260513120000_InitialCreate.cs` and read it end to end. It is plain C# that calls `migrationBuilder.CreateTable(...)`, `migrationBuilder.CreateIndex(...)`, and `migrationBuilder.AddForeignKey(...)`. There is no SQL in it. The provider — SQLite, here — translates these calls into SQL at apply time.

Apply the migration:

```bash
dotnet ef database update
```

EF Core opens the SQLite connection, sees the `__EFMigrationsHistory` table does not exist, creates it, runs every pending migration in timestamp order, and records each one in `__EFMigrationsHistory`. Subsequent `dotnet ef database update` calls will apply only the migrations that are not already recorded.

Verify:

```bash
sqlite3 ledger.db ".tables"
```

You should see:

```
Accounts          Categories        Tags
TransactionTag    Transactions      __EFMigrationsHistory
```

That is your schema. Versioned in the `Migrations/` folder. Committed to Git. Reproducible on any machine with the same SDK and provider.

---

## 9. A note on the model snapshot

The `LedgerContextModelSnapshot.cs` file is the single most important file in the migrations workflow. EF Core compares your current C# model against this file (not against the live database) to determine what changed since the last migration. Two implications:

- **Never edit the snapshot by hand.** EF Core regenerates it on every `migrations add`. Your edits will be lost. If the snapshot disagrees with the live database, you have a divergent state to repair, but the fix is *not* a hand edit.
- **Always commit the snapshot together with the migration.** A migration without its corresponding snapshot is useless on a teammate's machine. EF Core will compute the wrong diff against an out-of-date snapshot.

If you delete a migration that has not yet been applied to any database, use `dotnet ef migrations remove`. It will regenerate the snapshot for the previous migration. Never `git rm` a migration file by itself.

---

## 10. Build succeeded

Run:

```bash
dotnet build
```

You should see something close to:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.61
```

That is the contract for Week 3. Zero warnings. The migration applies cleanly. The schema in `ledger.db` matches the model. You can hit the database from `sqlite3` and see real tables, real columns, real indexes. Lecture 2 takes this same model and shows what `IQueryable<T>` does with it — and what *not* to do.

---

## Self-check

Before moving on, you should be able to answer the following without looking anything up:

1. What is the lifetime of a `DbContext` in an ASP.NET Core 9 app, and why?
2. Name three properties of an entity that EF Core infers from convention alone.
3. When would you reach for an `IEntityTypeConfiguration<T>` class instead of `OnModelCreating`?
4. What is the difference between `EnsureCreatedAsync` and `Database.MigrateAsync`?
5. Why does the model snapshot exist, and why must it never be hand-edited?

If any of these is fuzzy, re-read the relevant section before continuing to Lecture 2.

---

*Next: [Lecture 2 — Migrations and Queries](./02-migrations-and-queries.md).*
