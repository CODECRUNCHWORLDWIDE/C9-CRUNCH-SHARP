# Week 3 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 4. Answer key at the bottom — don't peek.

---

**Q1.** In ASP.NET Core 9, what is the default service lifetime for a `DbContext` registered with `builder.Services.AddDbContext<LedgerContext>(...)`?

- A) Singleton — one `DbContext` for the lifetime of the application.
- B) Scoped — one `DbContext` per HTTP request.
- C) Transient — a new `DbContext` on every injection.
- D) None — `AddDbContext` does not register a lifetime; you must call `AddSingleton<DbContext>` yourself.

---

**Q2.** Which statement most accurately describes the role of `OnModelCreating`?

- A) It is called once per `DbContext` instance, when the constructor runs.
- B) It is called once per process, the first time the model is needed, and the resulting model is cached per `DbContext` type.
- C) It is called on every query to allow dynamic schema changes.
- D) It is called only when `dotnet ef migrations add` runs; at runtime it is ignored.

---

**Q3.** You add a `[Required] string Memo` property to an entity that already has rows in the table. You run `dotnet ef migrations add AddMemo`. What does the generated migration's `Up` method do?

- A) Adds the column as `nullable: true` and silently accepts the existing rows.
- B) Adds the column as `nullable: false` with a `defaultValue: ""` so existing rows get a non-null value.
- C) Throws at migration-add time because adding a NOT NULL column to a non-empty table is impossible.
- D) Adds the column as nullable but emits a warning at the next `dotnet ef database update` until you fix it.

---

**Q4.** What does `AsNoTracking()` change about a query?

- A) It changes the emitted SQL to add a `NOLOCK` hint.
- B) It does not register the returned entities with the change tracker; mutating them does nothing on `SaveChangesAsync`.
- C) It forces the query to materialize on the client side rather than translating to SQL.
- D) It bypasses the `DbContext` entirely and opens a separate connection.

---

**Q5.** Given:

```csharp
var rows = db.Transactions
    .AsEnumerable()
    .Where(t => t.Amount > 1000)
    .ToList();
```

What does EF Core 9 emit?

- A) `SELECT * FROM Transactions WHERE Amount > 1000` — the `AsEnumerable` call is a no-op.
- B) `SELECT * FROM Transactions` — every row is materialized, and the `Where` runs in C# memory.
- C) Nothing — `AsEnumerable` throws `InvalidOperationException` because the query is not fully translatable.
- D) `SELECT * FROM Transactions WHERE Amount > @p0` with `@p0 = 1000` — the predicate translates regardless of the `AsEnumerable` boundary.

---

**Q6.** Which fix correctly eliminates the N+1 query in this loop?

```csharp
var posts = await db.Posts.ToListAsync();
foreach (var p in posts)
    Console.WriteLine(p.Author.Name);
```

- A) Add `lazy loading proxies` so the framework loads `Author` on access.
- B) Change `ToListAsync()` to `ToList()`.
- C) Change the query to `db.Posts.Include(p => p.Author).ToListAsync()`.
- D) Wrap the loop in `using (var tx = db.Database.BeginTransaction()) { ... }`.

---

**Q7.** A migration named `RenameMemoToTitle` is auto-generated when you rename a C# property. By default, what does EF Core's generator produce?

- A) A single `RenameColumn(name: "Memo", newName: "Title")` call.
- B) A `DropColumn("Memo")` followed by an `AddColumn<string>("Title", ...)` — data in the old column is **lost**.
- C) A `Sql("EXEC sp_rename ...")` call that delegates to the database.
- D) Nothing — EF Core refuses to generate a destructive migration and asks for confirmation.

---

**Q8.** What is the difference between `ExecuteUpdateAsync` and `SaveChangesAsync`?

- A) `ExecuteUpdateAsync` runs synchronously despite the name; `SaveChangesAsync` runs asynchronously.
- B) `ExecuteUpdateAsync` issues a single bulk SQL `UPDATE` and bypasses the change tracker entirely; `SaveChangesAsync` emits one `UPDATE` per modified tracked entity.
- C) `ExecuteUpdateAsync` returns the affected rows as entities; `SaveChangesAsync` returns the count.
- D) `ExecuteUpdateAsync` cannot be used with parameters; `SaveChangesAsync` parametrizes its updates.

---

**Q9.** You have a `[Timestamp] byte[] RowVersion` property on `Account`. Two HTTP requests load the same account, both modify `Name`, both call `SaveChangesAsync`. What happens?

- A) Both updates succeed; the later one overwrites the earlier silently.
- B) The first `SaveChangesAsync` succeeds; the second throws `DbUpdateConcurrencyException` because the row version no longer matches.
- C) Both updates fail because EF Core detects the conflict at query time.
- D) The second request's `SaveChangesAsync` blocks until the first commits, then succeeds.

---

**Q10.** In a query like:

```csharp
var t = await db.Transactions
    .Include(t => t.Account)
    .Include(t => t.Tags)
    .FirstOrDefaultAsync(t => t.Id == id);
```

What is the most likely problem and the right fix?

- A) `Include` cannot follow `Include`; remove the second one.
- B) Including two collections in one query can produce a cartesian product; add `.AsSplitQuery()` to split into multiple round trips.
- C) `FirstOrDefaultAsync` does not work with `Include`; use `SingleOrDefaultAsync`.
- D) `Include` requires a tracked query; remove `AsNoTracking()` if you had it.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **B** — `AddDbContext<T>()` registers `T` as scoped by default. The ASP.NET Core request scope is one HTTP request, which matches the unit-of-work lifetime EF Core expects. Singleton is wrong (`DbContext` is not thread-safe and the change tracker grows unbounded). Transient is wrong (the unit-of-work guarantee is broken across operations). The fourth option is plain false — `AddDbContext` does pick the lifetime, and you can override it with `ServiceLifetime.Singleton`/`Transient` parameters if you really want, but the default is scoped.

2. **B** — `OnModelCreating` runs once per process per `DbContext` type, the first time `DbContext.Model` is needed. The resulting `IModel` is cached. That is why mutating model configuration based on instance state does not work; if you need runtime model variations, you derive a new `DbContext` type per variant.

3. **B** — EF Core notices the property is non-nullable in the C# model, looks at the column type, and emits `defaultValue: ""` so the migration is applicable against tables with existing rows. The default value is a one-time fill, not an ongoing column default. If your domain demands a real default ("uncategorized" rather than ""), edit the migration before applying. The migration generator does *not* refuse the operation.

4. **B** — `AsNoTracking()` tells the materializer "do not register results with the change tracker." The SQL is unchanged. Mutating a returned entity has no effect on `SaveChangesAsync`. Use it for any read-only path; mutate only on tracked queries. The performance benefit grows with the result set size.

5. **B** — The `AsEnumerable()` call is the boundary between server (SQL) and client (C#) evaluation. Everything *before* it can translate; everything *after* runs in memory. Here, that means `SELECT * FROM Transactions` runs server-side and pulls every row over the wire; the `Where` runs in C#. This is the classic accidental-client-evaluation footgun.

6. **C** — Eager loading with `Include(p => p.Author)` produces one query with a JOIN. Lazy-loading proxies (option A) would technically work if you installed the `Proxies` package, but lazy loading is off by default in EF Core for good reason — it hides the per-row query at the call site and re-introduces the N+1 in a sneakier form. C9 stays on eager loading explicitly.

7. **B** — EF Core cannot tell intent from code. A renamed property looks identical to "old property deleted, new property added." The generator emits `DropColumn` + `AddColumn`. The fix is to hand-edit the migration to use `RenameColumn`. This is the single most important "read every migration" habit of the week.

8. **B** — `ExecuteUpdateAsync` (introduced EF Core 7, refined in 8/9) emits one bulk SQL `UPDATE` based on the query predicate, bypassing the change tracker. It is the right tool for batch operations where you do not need entity-level semantics. `SaveChangesAsync` is the right tool when you have entities you have read and mutated. They are complementary, not interchangeable.

9. **B** — Optimistic concurrency works by including the `RowVersion` column in the `WHERE` clause of every generated `UPDATE`. When the row version no longer matches (because the first request updated the row), the second request's `UPDATE` affects zero rows; EF Core notices and throws `DbUpdateConcurrencyException`. You handle the exception by deciding "server wins," "client wins," or "merge and retry."

10. **B** — Including two collection navigations produces a single SQL query that joins both, and the result is a cartesian product: every parent row repeated for every combination of its child rows. `AsSplitQuery()` splits this into multiple smaller round trips that EF Core stitches together client-side. The right rule of thumb: a single collection `Include` is fine; two or more collection `Include`s warrant `AsSplitQuery` until measurements say otherwise.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
