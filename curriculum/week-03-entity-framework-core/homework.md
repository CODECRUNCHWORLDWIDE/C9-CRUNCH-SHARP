# Week 3 Homework

Six practice problems that revisit the week's topics. The full set should take about **6 hours**. Work in your Week 3 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — Reading the emitted SQL for three reads

**Problem statement.** Start with the `BookContext` from Exercise 1 (or scaffold a fresh one). Add `LogTo(Console.WriteLine, LogLevel.Information)` to `OnConfiguring`. Then write three variants of the same logical query:

1. A **tracked** read: `db.Books.Where(b => b.PageCount > 200).ToListAsync()`.
2. The same with `AsNoTracking()`.
3. A **projection**: `db.Books.Where(b => b.PageCount > 200).Select(b => new { b.Id, b.Title }).ToListAsync()`.

Run all three. Copy the three emitted `SELECT` statements into `notes/three-reads.md` and write one paragraph (4–6 sentences) per query explaining:

- What columns it selects.
- Whether it registers entities with the change tracker.
- When you would reach for it in production code.

**Acceptance criteria.**

- File `notes/three-reads.md` exists with three labeled sections.
- Each section quotes the SQL verbatim (in a fenced ` ```sql ` block).
- File is committed.

**Hint.** The projection's SQL should select only `Id` and `Title` — confirm by reading it.

**Estimated time.** 25 minutes.

---

## Problem 2 — Author and Book (a one-to-many model)

**Problem statement.** Refactor the `Book` entity so that its `Author` is no longer a string column but a real `Author` entity. Keep the `Title`, `PageCount`, `Published` properties as before; add an `AuthorId` foreign key and an `Author` navigation property. Add an `Author` entity with `Id`, `Name`, and a `Books` collection.

Configure the relationship via a fluent `IEntityTypeConfiguration<Book>`:
- `Book` has one `Author`, `Author` has many `Books`, FK on `Book.AuthorId`.
- Cascade delete from `Author` to `Books`.
- An index on `Book.AuthorId`.

Generate two migrations: one that introduces the `Authors` table and `Books.AuthorId` column (nullable for now), and one that copies the existing `Author` string into `Author` rows and drops the old `Author` column. The second migration's `Up` will need a hand-written `Sql(...)` block to perform the data move.

**Acceptance criteria.**

- A buildable project with the new schema.
- `dotnet build`: 0 warnings, 0 errors.
- `dotnet ef database update` against a database that already contains rows preserves every author name.
- The migration sequence is committed in chronological order.
- A SQL query `SELECT a.Name, b.Title FROM Authors a JOIN Books b ON b.AuthorId = a.Id;` returns all expected rows.

**Hint.** The data-move migration's `Up` will look roughly like:
```csharp
mb.Sql("INSERT INTO Authors (Name) SELECT DISTINCT Author FROM Books;");
mb.Sql("UPDATE Books SET AuthorId = (SELECT Id FROM Authors WHERE Authors.Name = Books.Author);");
mb.DropColumn("Author", "Books");
```

**Estimated time.** 1 hour.

---

## Problem 3 — Projection, paging, and a typed report

**Problem statement.** Take the seeded `BlogContext` from Exercise 2 (or copy its `Author`/`Post`/`Tag` model into a fresh project). Write a single async function:

```csharp
public static async Task<PagedResult<PostListItem>> ListPostsAsync(
    BlogContext db,
    string? authorNameContains,
    string? tagName,
    int page,
    int size,
    CancellationToken ct);
```

The function should:

- Filter by `authorNameContains` if present (case-insensitive substring match on `Author.Name`).
- Filter by `tagName` if present (post must have a `Tag` with that name).
- Order by `CreatedAt` descending.
- Apply `Skip`/`Take` for paging.
- Project into a `PostListItem` record: `(int Id, string Title, string AuthorName, string[] Tags, DateTimeOffset CreatedAt)`.
- Return a `PagedResult<PostListItem>` containing `Items`, `Page`, `Size`, and `TotalCount` (the total *unpaged* count, fetched in a separate `CountAsync` query).

Then write three integration tests against an in-memory SQLite database (`new SqliteConnection("Filename=:memory:")` + `connection.Open()` + use the connection in `UseSqlite`) seeded with the same fixture as Exercise 2.

**Acceptance criteria.**

- The function compiles with `AsNoTracking()` and translates the entire pipeline to SQL (verify with `LogTo`).
- Three xUnit tests pass: (a) no filters returns all posts paginated; (b) `tagName="history"` returns three posts; (c) `authorNameContains="ada"` returns two posts.
- `dotnet test`: all passing.
- Committed.

**Hint.** For case-insensitive substring matching across SQLite and PostgreSQL portably, use `EF.Functions.Like(a.Name, $"%{authorNameContains}%")` — SQLite's `LIKE` is case-insensitive by default.

**Estimated time.** 1 hour 15 minutes.

---

## Problem 4 — Optimistic concurrency end to end

**Problem statement.** Add a `[Timestamp] byte[] RowVersion { get; set; }` column to the `Account` entity from Lecture 1 (or to the `Author` entity from Exercise 2). Generate a migration. For SQLite specifically, you will need to model concurrency manually: a `Property<int>("Version").IsConcurrencyToken()` plus an `ISaveChangesInterceptor` that bumps `Version` on every modified row. (On a real PostgreSQL or SQL Server target the `[Timestamp]` annotation is enough by itself.)

Write a test that demonstrates the conflict:

1. Open `DbContext` A, load `Account` 1.
2. Open `DbContext` B, load the same `Account` 1, mutate `Name`, `SaveChangesAsync`.
3. Back in `DbContext` A, mutate `Name`, `SaveChangesAsync` — expect `DbUpdateConcurrencyException`.
4. Resolve by refetching, re-applying the user's change, retrying. Expect success.

**Acceptance criteria.**

- Migration applied; column visible in `sqlite3`.
- The interceptor bumps the version on every modified entity (verify with `LogTo`).
- The test passes: the second save fails with `DbUpdateConcurrencyException`; the retry succeeds.
- `dotnet build`: 0 warnings, 0 errors.
- Committed.

**Hint.** The `ISaveChangesInterceptor`'s `SavingChangesAsync` override iterates `eventData.Context.ChangeTracker.Entries()` and increments the shadow `Version` property on every entry whose `State == EntityState.Modified`.

**Estimated time.** 1 hour.

---

## Problem 5 — Bulk operations with `ExecuteUpdateAsync`

**Problem statement.** Using the `BlogContext` seeded with 10,000 posts, write three functions:

1. `MarkOldPostsAsArchivedAsync(BlogContext db, DateTimeOffset cutoff, CancellationToken ct)` — set `Body = "[archived]"` on every post older than `cutoff`. Implement once with a `foreach`/`SaveChangesAsync` loop, once with a single `ExecuteUpdateAsync`. Measure both with `BenchmarkDotNet`.
2. `DeleteTestPostsAsync(BlogContext db, CancellationToken ct)` — delete every post whose `Title` starts with `"[TEST]"`. Implement with `ExecuteDeleteAsync`.
3. `BulkRenameTagAsync(BlogContext db, string oldName, string newName, CancellationToken ct)` — rename a tag with `ExecuteUpdateAsync(setters => setters.SetProperty(t => t.Name, newName))` against the filter `t.Name == oldName`.

Write a one-page `notes/bulk-vs-savechanges.md` summarizing the timing difference and the change-tracker behaviour.

**Acceptance criteria.**

- All three functions exist and pass at least one xUnit test each.
- `BenchmarkDotNet` summary shows `ExecuteUpdateAsync` at least 50x faster than the equivalent `SaveChangesAsync` loop on 10,000 rows.
- The notes file is committed.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** `ExecuteUpdateAsync` returns an `int` — the number of rows affected. Use it as the function's return value when meaningful.

**Estimated time.** 1 hour 30 minutes.

---

## Problem 6 — Mini reflection essay

**Problem statement.** Write a 300–400 word reflection at `notes/week-03-reflection.md` answering:

1. The lecture argues that "change tracking is the magic, and it is also the cost." After a week of writing EF Core code, do you agree? Cite one concrete example from your homework where you felt the tradeoff.
2. If you have used SQLAlchemy, Hibernate, Active Record, or another ORM before, where does EF Core feel familiar and where does it feel different? One example of each.
3. Which loading strategy did you reach for most this week — eager, explicit, projection — and why? Is that the same one you would reach for first in a production codebase?
4. What's one thing you'd want to learn next that this week didn't cover? (Caching? Compiled queries? Multi-tenancy? Sharding? Read replicas?)

**Acceptance criteria.**

- File exists, 300–400 words.
- Each numbered question is addressed in its own paragraph.
- File is committed.

**Hint.** This is for *you*, not for a grade. Be honest. Future-you reading it after Week 8 (when you've shipped a real EF-Core-backed service) will be grateful.

**Estimated time.** 30 minutes.

---

## Time budget recap

| Problem | Estimated time |
|--------:|--------------:|
| 1 | 25 min |
| 2 | 1 h 0 min |
| 3 | 1 h 15 min |
| 4 | 1 h 0 min |
| 5 | 1 h 30 min |
| 6 | 30 min |
| **Total** | **~5 h 40 min** |

When you've finished all six, push your repo and open the [mini-project](./mini-project/README.md). The mini-project takes Week 2's Ledger REST API and replaces its in-memory `ConcurrentDictionary` with a real EF Core 9 `DbContext` against a SQLite file — every concept from this week, applied end-to-end on a domain you already know.
