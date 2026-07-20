# Week 10 — Homework

Six practice problems. Allocate roughly 1 hour per problem; problems 5 and 6 are longer and may need 90 minutes. Submit one `.zip` of code plus a single `homework.md` write-up. Rubric at the bottom.

---

## Problem 1 — Read a SQL log and identify the LINQ that produced it (45 min)

You are given the following SQL log fragments, captured from a working `DbContext` with `LogTo(Console.WriteLine, LogLevel.Information)` enabled. For each, write the **C# LINQ expression** that produced it, including any `Include`, `AsNoTracking`, `Where`, `OrderBy`, `Skip`, `Take`, or projection.

### Fragment A

```sql
SELECT "p"."Id", "p"."CategoryId", "p"."Name", "p"."Price"
FROM "Products" AS "p"
WHERE "p"."Price" > 100.0
ORDER BY "p"."Name"
LIMIT 10 OFFSET 20
```

### Fragment B

```sql
SELECT "c"."Id", "c"."Name",
       "p"."Id", "p"."CategoryId", "p"."Name", "p"."Price"
FROM "Categories" AS "c"
LEFT JOIN "Products" AS "p" ON "c"."Id" = "p"."CategoryId"
WHERE "c"."Name" = @__name_0
ORDER BY "c"."Id"
```

### Fragment C

```sql
SELECT "c"."Id", "c"."Name",
       (SELECT COUNT(*) FROM "Orders" AS "o" WHERE "c"."Id" = "o"."CustomerId") AS "OrderCount"
FROM "Customers" AS "c"
```

**Deliverable:** Three C# expressions in a Markdown file with one-paragraph explanations of each LINQ-to-SQL mapping decision (paging, joining, projection-with-subquery).

---

## Problem 2 — Build a migration for a many-to-many (60 min)

Given the following entity types, write the `DbContext` (including `OnModelCreating` if needed), run `dotnet ef migrations add InitialCreate`, and inspect the generated migration:

```csharp
public sealed class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<Tag> Tags { get; set; } = new();
}

public sealed class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Post> Posts { get; set; } = new();
}
```

**Required of your deliverable:**

- The generated migration file (`*_InitialCreate.cs`) verbatim.
- A one-paragraph explanation of the **join table** EF emits (default name `PostTag`, two `int` columns, composite primary key).
- A second `Down()` migration that you write by hand to revert the schema, even if EF generated one — confirm yours matches.

Cite <https://learn.microsoft.com/en-us/ef/core/modeling/relationships/many-to-many>.

---

## Problem 3 — Diagnose three N+1 patterns in one log (60 min)

You are given a 400-line SQL log captured from a production-shaped endpoint that is reported as slow. The log shows three distinct N+1 patterns interleaved. Your job is to:

1. **Identify** each N+1 pattern from the log (one outer query + N inner queries with the same SQL).
2. **Write** the LINQ that likely produced each pattern.
3. **Propose** the cure for each (`Include`, projection, explicit, or `AsSplitQuery` depending on shape).

The log is in `homework/problem-03-log.txt` (your instructor provides it; if you are working through this independently, generate one yourself by writing an endpoint that intentionally has three N+1s — one customer-to-orders, one order-to-lines, one line-to-product).

**Deliverable:** A Markdown file with three sections, each containing:

- The log fragment (cut to ~10 lines).
- The proposed LINQ that produced it.
- The proposed cure with the fixed LINQ.

The implicit lesson: real production logs are noisy and patterns overlap. Reading them is a skill.

---

## Problem 4 — Strongly-typed IDs and a tracker walk-through (60 min)

Add a strongly-typed ID converter to two entities, then walk the change tracker through a write path.

```csharp
public readonly record struct CustomerId(int Value);
public readonly record struct OrderId(int Value);

public sealed class Customer
{
    public CustomerId Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
}

public sealed class Order
{
    public OrderId Id { get; set; }
    public CustomerId CustomerId { get; set; }
    public decimal Total { get; set; }
}
```

**Required:**

- Apply value converters in `OnModelCreating` for both `Id` and `CustomerId`.
- Demonstrate that `void ProcessOrder(CustomerId, OrderId)` cannot be called with the arguments swapped (the compiler refuses).
- Write a small console program that loads a customer, modifies a property, prints `db.ChangeTracker.DebugView.LongView` *before* `SaveChangesAsync`, calls `SaveChangesAsync`, prints the debug view again, and explains the state transitions in comments.

**Deliverable:** the `.cs` file, the captured output of `DebugView.LongView` before and after, and a one-paragraph explanation of why strongly-typed IDs prevent a class of bug that plain `int` IDs do not.

Cite <https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions> and <https://learn.microsoft.com/en-us/ef/core/change-tracking/debug-views>.

---

## Problem 5 — `FromSqlInterpolated` with composition + a parameterization audit (90 min)

Build a small search endpoint that takes a JSON body with optional fields:

```json
{ "name": "wrench", "minPrice": 10.00, "maxPrice": 50.00, "currency": "USD" }
```

The endpoint should:

1. Use `FromSqlInterpolated` for the base query (text-search-style `WHERE Name LIKE`).
2. Compose further LINQ on top for the price range and currency filters.
3. Project the result to a `record SearchHit(int Id, string Name, decimal Price, string Currency)`.

**Required deliverables:**

- The endpoint code, with the SQL log captured for one representative request.
- An audit document (`AUDIT.md`) that walks through the endpoint and confirms, line by line, that no user input ever reaches the SQL template as a substring — every dynamic value is a parameter, not a fragment.
- A second test that sends a payload with `"name": "'; DROP TABLE Products; --"` and confirms the table is intact.

Cite the OWASP SQL-injection cheat sheet at <https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html> and the EF Core raw-SQL chapter at <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries>.

---

## Problem 6 — A measured comparison of four loading strategies (90 min)

Take the schema from Exercise 3 (`Customer`, `Order`) and write **four** versions of the "list customers with their order totals" endpoint:

1. Naive (no `Include`, lazy off — silently wrong).
2. Eager (`Include(c => c.Orders)`).
3. Projection (server-side `Sum` in a `Select`).
4. Two-step (`AsNoTracking` parent load + `AsNoTracking` child load via `WHERE CustomerId IN (...)`).

Measure each on the **same** seeded dataset (100 customers, 10 orders each) and on a **larger** dataset (1,000 customers, 100 orders each). Capture wall-clock time and SQL statement count for each.

Write up the results as a table and a recommendation. Specifically:

- Which strategy wins at 100 customers? At 1,000?
- Does the relative ordering change with scale?
- Which strategy would you ship to production, and why?

**Deliverable:** the `.cs` file (with the four endpoints), `RESULTS.md` with the measurement tables, and a recommendation paragraph. Cite the EF performance guide at <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying>.

---

## Rubric

| Problem | Weight | Criteria                                                                   |
|---------|-------:|----------------------------------------------------------------------------|
| 1       |    10% | Three correct LINQ expressions, each with a one-paragraph explanation.     |
| 2       |    15% | Working migration; `PostTag` join table; manual `Down()` matches generated.|
| 3       |    15% | Three N+1 patterns correctly identified, each with a working cure.         |
| 4       |    15% | Strongly-typed IDs apply correctly; tracker walk-through is correct.       |
| 5       |    20% | Endpoint works; AUDIT.md is correct; injection test passes.                |
| 6       |    25% | Four strategies measured; table is correct; recommendation is defensible.  |

A 70% score is passing; an 85% score is solid; a 95%+ score is the level you should be at before starting the mini-project.

### A note on what counts as "correct"

The lectures are precise about behaviour but not always about wording. If your write-up correctly explains **why** something behaves the way it does — citing the snapshot, the tracker, the cartesian factor, the parameterization — it counts as correct even if the wording differs from the lecture. If your write-up parrots the lecture but does not demonstrate understanding (e.g. "the tracker is faster than no-tracking" — that is the inverse of correct), it does not. Read your own write-up out loud before submitting; if you cannot defend a sentence under a follow-up question, rewrite it.
