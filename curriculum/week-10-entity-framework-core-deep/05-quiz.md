# Week 10 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 11. Answer key at the bottom — do not peek.

---

**Q1.** In EF Core 8, what is the **default** lifetime of a `DbContext` when registered via `services.AddDbContext<TContext>(...)` in ASP.NET Core?

- A) `Singleton` — one instance for the lifetime of the application.
- B) `Transient` — a new instance per injection.
- C) `Scoped` — one instance per HTTP request (per scope).
- D) `Pooled` — reused from a pool, with `AddDbContextPool` as the explicit opt-in.

---

**Q2.** A `Product` entity has its `Price` updated in C# and `SaveChangesAsync` is called. What `UPDATE` statement does EF emit, by default?

- A) `UPDATE Products SET Id=@p0, Name=@p1, Price=@p2, CategoryId=@p3 WHERE Id=@p4` — every column is overwritten.
- B) `UPDATE Products SET Price=@p0 WHERE Id=@p1` — only the changed column is in the `SET` clause, computed from the snapshot diff.
- C) `UPDATE Products SET Price=@p0, RowVersion=@p1 WHERE Id=@p2` — every column with a value type is included.
- D) `DELETE FROM Products WHERE Id=@p0; INSERT INTO Products (...) VALUES (...)` — EF deletes and re-inserts.

---

**Q3.** Which method should you reach for to read 10,000 rows for a report endpoint that does not modify any of them?

- A) `db.Products.ToListAsync()` — default tracking, EF will figure it out.
- B) `db.Products.AsTracking().ToListAsync()` — explicit tracking.
- C) `db.Products.AsNoTracking().ToListAsync()` — skip the change tracker entirely.
- D) `db.Products.Local.ToList()` — read from the local cache.

---

**Q4.** A SQL log shows:

```
SELECT * FROM Customers
SELECT * FROM Orders WHERE CustomerId = @p0  [@p0=1]
SELECT * FROM Orders WHERE CustomerId = @p0  [@p0=2]
SELECT * FROM Orders WHERE CustomerId = @p0  [@p0=3]
... (97 more identical statements)
```

What is this and what is the fix?

- A) Normal eager-loading output. No fix needed.
- B) The N+1 problem. Fix with `Include(c => c.Orders)` or a projection that does the aggregation server-side.
- C) A bug in EF Core. File an issue at `dotnet/efcore`.
- D) Cartesian explosion. Fix with `AsSplitQuery()`.

---

**Q5.** A query is `db.Customers.Include(c => c.Orders).Include(c => c.Addresses)`. On the seeded dataset (100 customers, 10 orders each, 3 addresses each), how many rows cross the wire from the database to the .NET process by default?

- A) 100 rows (just the customers).
- B) 1,300 rows (100 customers + 1,000 orders + 300 addresses).
- C) 3,000 rows (100 customers x 10 orders x 3 addresses, cartesian).
- D) 13 rows (10 + 3 per customer, averaged).

---

**Q6.** What is the safest way to write a search query with a user-supplied term against raw SQL?

- A) `db.Products.FromSqlRaw("SELECT * FROM Products WHERE Name LIKE '%" + term + "%'")` — concatenate the term into the SQL.
- B) `db.Products.FromSqlInterpolated($"SELECT * FROM Products WHERE Name LIKE {pattern}")` where `pattern = "%" + term + "%"` — interpolation parameterizes automatically.
- C) `db.Database.ExecuteSqlRawAsync($"SELECT * FROM Products WHERE Name LIKE '%{term}%'")` — `ExecuteSqlRaw` is safe.
- D) `db.Products.Where(p => p.Name.Contains(term)).ToList()` — but only after escaping `term` with `Regex.Escape`.

---

**Q7.** What is **cartesian explosion** in the context of EF Core eager loading?

- A) The phenomenon where EF Core's query translator runs out of memory on deeply-nested includes.
- B) The phenomenon where joining a parent table to two child collections produces a row count equal to the *product* of the two child cardinalities per parent, leading to multiplicative wire bytes.
- C) The phenomenon where lazy loading issues exponentially many queries.
- D) A bug in EF Core 8 fixed in EF Core 9.

---

**Q8.** Which command produces a SQL script suitable for applying schema changes to a production database?

- A) `dotnet ef database update` — applies migrations directly.
- B) `dotnet ef migrations script --idempotent --output deploy.sql` — emits an idempotent SQL script.
- C) `dotnet ef dbcontext scaffold` — reverse-engineers the schema.
- D) `dotnet ef migrations remove` — removes the last migration.

---

**Q9.** A query is `EF.CompileAsyncQuery((CatalogDb db, int id) => db.Products.Find(id))`. The delegate is stored in a `static` field. What is the per-call performance benefit?

- A) The query bypasses the database entirely.
- B) The query reads from a memory cache instead of issuing a `SELECT`.
- C) The LINQ-to-SQL translation step is cached, saving approximately 20-30 microseconds per call.
- D) The query runs in parallel across multiple connections.

---

**Q10.** A `Money` value object (`record struct Money(decimal Amount, string Currency)`) should be persisted on a `Product` entity. Which modelling construct is the **EF Core 8 recommended choice** for a same-row value object?

- A) `OwnsOne` — the legacy owned-entity API.
- B) `HasMany` — a navigation to a `Money` table.
- C) `ComplexProperty` — the EF Core 8 value-object API designed for same-row mapping without entity-tracking baggage.
- D) `HasConversion<string>` — serialize the whole struct to a single string column.

---

## Answer key

1. **C.** `AddDbContext` registers the context as `Scoped` by default. `AddDbContextPool` opts into pooling on top of that. Cite <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues>.

2. **B.** The change tracker takes a snapshot at load time and compares against current values at `SaveChanges` time. Only modified columns appear in the `SET` clause. Cite <https://learn.microsoft.com/en-us/ef/core/change-tracking/>.

3. **C.** `AsNoTracking` skips the tracker insert and the snapshot copy, saving 30-50% allocation and 15-30% time on read-only paths. Cite <https://learn.microsoft.com/en-us/ef/core/querying/tracking>.

4. **B.** The N+1 problem. One outer query plus N inner queries with identical SQL and only the parameter changing. Cure with `Include`, projection, or explicit batched loading. Cite <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying#use-eager-loading-when-appropriate>.

5. **C.** Cartesian explosion. The `LEFT JOIN` of two collections produces every order paired with every address, multiplicatively. 100 x 10 x 3 = 3,000 rows. Cure with `AsSplitQuery`. Cite <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries>.

6. **B.** `FromSqlInterpolated` captures the template and the values separately as a `FormattableString`; each interpolation slot becomes a parameter. Concatenation (A) is the classic SQL-injection vulnerability. Cite <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#passing-parameters>.

7. **B.** Cartesian explosion is the multiplicative-row-count problem from joining a parent against two sibling collections in one query. Cite <https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries>.

8. **B.** `dotnet ef migrations script --idempotent` emits a SQL script that is safe to apply against any schema state — the `IF NOT EXISTS` guards keyed off `__EFMigrationsHistory` ensure each migration body runs at most once. Cite <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#sql-scripts>.

9. **C.** The LINQ-to-SQL translation step costs 20-100 microseconds per uncompiled call; the compiled delegate caches the translation. Cite <https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries>.

10. **C.** EF Core 8 introduced `ComplexProperty` specifically for same-row value objects without the entity-tracking machinery of `OwnsOne`. `OwnsOne` still works for existing code; `ComplexProperty` is the new-code recommendation. Cite <https://learn.microsoft.com/en-us/ef/core/modeling/complex-types>.

---

## Scoring

- **10/10** — Production-ready. You will diagnose N+1 in someone else's code on first read.
- **8-9/10** — Strong. Re-read the lecture sections corresponding to the questions you missed.
- **6-7/10** — Adequate. Plan a re-read of all three lectures before starting the mini-project.
- **0-5/10** — Re-read the lectures, redo the exercises, retake the quiz. The mini-project assumes this material is fluent.
