// Exercise 4 — Raw SQL with FromSqlInterpolated + a Money value converter.
//
// Goal: build a small Products schema where Price is a strongly-typed Money
// value object (struct), apply a value converter so it persists as two
// columns (Amount, Currency), and write a search endpoint that uses
// FromSqlInterpolated. Demonstrate, by reading the SQL log, that the
// interpolated value becomes a parameter (@p0), not concatenation, and is
// safe against SQL injection.
//
// Project layout:
//
//   src/Ex04.RawSqlAndConverters/
//     Ex04.RawSqlAndConverters.csproj
//     Program.cs
//
// Build and run:
//   dotnet build
//   dotnet run
//
// Acceptance criteria:
//   1. The Money struct serializes to two columns: Amount (decimal) and
//      Currency (char(3)). Verify with `EXPLAIN` or by inspecting the
//      generated CREATE TABLE.
//   2. The FromSqlInterpolated search emits SQL with `@p0`, not the literal
//      value. Confirm in the log.
//   3. Passing a malicious string (e.g. "'; DROP TABLE Products; --") as the
//      search term returns zero rows without dropping the table. This proves
//      the parameterization is real.
//   4. The composed LINQ (FromSqlInterpolated + Where + OrderBy) produces a
//      wrapping SELECT around the raw subquery.

#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ex04.RawSqlAndConverters;

public readonly record struct Money(decimal Amount, string Currency)
{
    public override string ToString() => $"{Currency} {Amount:F2}";
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Money Price { get; set; }
}

public sealed class CatalogDb : DbContext
{
    public CatalogDb(DbContextOptions<CatalogDb> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>(eb =>
        {
            eb.Property(p => p.Name).HasMaxLength(128).IsRequired();

            // Map Money to two columns. We use ComplexProperty (EF Core 8)
            // because Money is a value object with no key. For EF Core 7 and
            // earlier, the equivalent is OwnsOne; both are shown here for
            // reference but only one should be active in your model.
            eb.ComplexProperty(p => p.Price, pb =>
            {
                pb.Property(m => m.Amount).HasColumnName("PriceAmount").HasColumnType("decimal(19,4)");
                pb.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsFixedLength();
            });

            // If you prefer the EF Core 7 syntax, uncomment the OwnsOne block
            // and comment out the ComplexProperty block above. Both produce
            // the same two-column shape.
            //
            // eb.OwnsOne(p => p.Price, pb =>
            // {
            //     pb.Property(m => m.Amount).HasColumnName("PriceAmount").HasColumnType("decimal(19,4)");
            //     pb.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsFixedLength();
            // });
        });
    }
}

public static class Program
{
    public static async Task Main()
    {
        var options = new DbContextOptionsBuilder<CatalogDb>()
            .UseSqlite("Data Source=ex04.db")
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;

        using (var seedDb = new CatalogDb(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            if (!await seedDb.Products.AnyAsync())
            {
                seedDb.Products.AddRange(
                    new Product { Name = "Hex Wrench",      Price = new Money(8.50m,  "USD") },
                    new Product { Name = "Allen Key Set",   Price = new Money(14.25m, "USD") },
                    new Product { Name = "Pipe Cutter",     Price = new Money(42.00m, "USD") },
                    new Product { Name = "Crunch T-Shirt",  Price = new Money(19.99m, "USD") },
                    new Product { Name = "Crunch Mug",      Price = new Money(11.50m, "EUR") }
                );
                await seedDb.SaveChangesAsync();
            }
        }

        Console.WriteLine("\n===== SAFE SEARCH: legitimate input =====\n");
        await SearchAsync(options, "wrench");

        Console.WriteLine("\n===== INJECTION ATTEMPT: malicious input =====\n");
        await SearchAsync(options, "'; DROP TABLE Products; --");

        Console.WriteLine("\n===== TABLE STILL EXISTS: lists all products =====\n");
        using (var db = new CatalogDb(options))
        {
            var all = await db.Products.AsNoTracking().ToListAsync();
            foreach (var p in all)
                Console.WriteLine($"  {p.Id} {p.Name} {p.Price}");
        }

        Console.WriteLine("\n===== COMPOSED RAW + LINQ =====\n");
        await ComposedAsync(options, "USD");
    }

    private static async Task SearchAsync(DbContextOptions<CatalogDb> options, string term)
    {
        using var db = new CatalogDb(options);

        // The interpolated string here is the *safe* form. EF captures the
        // template and the value separately; the value becomes @p0 on the
        // wire. The injection attempt below cannot escape the parameter slot.
        var pattern = "%" + term + "%";
        var results = await db.Products
            .FromSqlInterpolated($"SELECT * FROM Products WHERE Name LIKE {pattern}")
            .AsNoTracking()
            .ToListAsync();

        Console.WriteLine($"  Query for term \"{term}\" returned {results.Count} row(s).");
        foreach (var p in results)
            Console.WriteLine($"    {p.Id} {p.Name} {p.Price}");
    }

    private static async Task ComposedAsync(DbContextOptions<CatalogDb> options, string currency)
    {
        using var db = new CatalogDb(options);

        // FromSqlInterpolated returns IQueryable<T>; we can layer LINQ on top.
        // EF wraps the raw query in a subquery and applies the Where/OrderBy
        // around it. Read the SQL log to confirm the wrapping shape.
        var query = db.Products
            .FromSqlInterpolated($"SELECT * FROM Products WHERE PriceCurrency = {currency}")
            .Where(p => p.Price.Amount > 10m)
            .OrderByDescending(p => p.Price.Amount)
            .AsNoTracking();

        Console.WriteLine($"  SQL: {query.ToQueryString()}");
        Console.WriteLine();

        var results = await query.ToListAsync();
        foreach (var p in results)
            Console.WriteLine($"    {p.Id} {p.Name} {p.Price}");
    }
}

// ============================================================================
// EXPECTED SQL LOG (the load-bearing lines)
// ============================================================================
//
// CREATE TABLE on first run shows two columns:
//     CREATE TABLE "Products" (
//         "Id" INTEGER NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY AUTOINCREMENT,
//         "Name" TEXT NOT NULL,
//         "PriceAmount" decimal(19,4) NOT NULL,
//         "PriceCurrency" char(3) NOT NULL
//     )
//
// Safe search:
//     SELECT * FROM Products WHERE Name LIKE @p0    [@p0='%wrench%']
//
// Injection attempt:
//     SELECT * FROM Products WHERE Name LIKE @p0    [@p0='%''; DROP TABLE Products; --%']
//     0 rows returned. Table intact.
//
// Composed:
//     SELECT "p"."Id", "p"."Name", "p"."PriceAmount", "p"."PriceCurrency"
//     FROM (
//         SELECT * FROM Products WHERE PriceCurrency = @p0
//     ) AS "p"
//     WHERE "p"."PriceAmount" > 10
//     ORDER BY "p"."PriceAmount" DESC
//
// ============================================================================
// CHECKLIST
// ============================================================================
//
//   [ ] The Products table has separate PriceAmount and PriceCurrency columns.
//   [ ] The SQL log shows @p0 (or @__pattern_0) — never the literal value
//       inside the SQL text.
//   [ ] The injection attempt returns 0 rows and the table is intact.
//   [ ] The composed query wraps the raw SELECT in a subquery.
//   [ ] You can answer: "Why does FromSqlInterpolated prevent injection but
//       FromSqlRaw with string concatenation does not?" (The interpolated
//       version captures the template and value separately as a
//       FormattableString; the raw concatenation merges them into one string
//       before EF ever sees it.)
//
// Stretch:
//   Add a value converter for a strongly-typed ProductId record struct.
//   Replace `public int Id { get; set; }` with `public ProductId Id { get; set; }`
//   and add `eb.Property(p => p.Id).HasConversion(id => id.Value, v => new ProductId(v));`
//   to OnModelCreating. Confirm the table still has an Id column of type
//   INTEGER and the C# API now refuses to confuse a ProductId with a CategoryId.
