// CrunchCatalog.Api/Program.cs
//
// Starter minimal-API host for the Week 10 mini-project. The seven endpoints
// are declared as stubs; you fill in the bodies. The DbContext registration,
// the SQL log configuration, and the interceptor wiring are complete.

#nullable enable

using System.Data;
using CrunchCatalog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SlowQueryInterceptor>();

builder.Services.AddDbContext<CatalogDb>((sp, options) =>
{
    var conn = builder.Configuration.GetConnectionString("Catalog")
               ?? "Data Source=catalog.db";

    // For Postgres, use UseNpgsql(conn). For SQLite (the default in dev),
    // use UseSqlite(conn). The choice is per-environment; the rest of the
    // model is provider-agnostic except for the case-insensitive collation
    // hint on Category.Name.
    options.UseSqlite(conn);

    if (builder.Environment.IsDevelopment())
    {
        options.LogTo(Console.WriteLine, LogLevel.Information)
               .EnableSensitiveDataLogging()
               .EnableDetailedErrors();
    }

    options.AddInterceptors(sp.GetRequiredService<SlowQueryInterceptor>());
});

var app = builder.Build();

// At app startup, ensure the database has the latest migrations applied.
// In production this would be the idempotent-script workflow, not MigrateAsync.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDb>();
    await db.Database.MigrateAsync();
    await SeedAsync(db);
}

// ============================================================================
// ENDPOINTS
// ============================================================================

// 1. List all categories with their product counts.
app.MapGet("/api/categories", async (CatalogDb db) =>
{
    // TODO: implement using a single SELECT with a correlated subquery.
    // Required SQL shape: SELECT c.Id, c.Name, (SELECT COUNT(*) FROM Products WHERE CategoryId = c.Id) FROM Categories.
    return Results.Ok(Array.Empty<object>());
});

// 2. List products in a category, paginated.
app.MapGet("/api/categories/{id:int}/products", async (CatalogDb db, int id, int page = 1, int pageSize = 20) =>
{
    // TODO: implement.
    // Required SQL shape: SELECT ... FROM Products WHERE CategoryId = @p0 ORDER BY Name LIMIT @p1 OFFSET @p2.
    return Results.Ok(Array.Empty<object>());
});

// 3. Get a product detail with category and reviews.
app.MapGet("/api/products/{id:int}", async (CatalogDb db, int id) =>
{
    // TODO: implement using Include(p => p.Category).Include(p => p.Reviews).AsSplitQuery().AsNoTracking().
    return Results.NotFound();
});

// 4. Search products by name with FromSqlInterpolated.
app.MapGet("/api/products/search", async (CatalogDb db, string q) =>
{
    // TODO: implement.
    // var pattern = "%" + q + "%";
    // var hits = await db.Products
    //     .FromSqlInterpolated($"SELECT * FROM Products WHERE Name LIKE {pattern}")
    //     .AsNoTracking()
    //     .ToListAsync();
    return Results.Ok(Array.Empty<object>());
});

// 5. Create a product.
app.MapPost("/api/products", async (CatalogDb db, CreateProductDto dto) =>
{
    // TODO: implement. Reject duplicate SKU with 409 Conflict.
    return Results.Created("/api/products/0", new { });
});

// 6. Patch a product's price.
app.MapPatch("/api/products/{id:int}/price", async (CatalogDb db, int id, Money newPrice) =>
{
    // TODO: implement using load-then-update. The SQL log should show an
    // UPDATE with only PriceAmount and PriceCurrency in the SET clause.
    return Results.NoContent();
});

// 7. Add a review to a product.
app.MapPost("/api/products/{id:int}/reviews", async (CatalogDb db, int id, AddReviewDto dto) =>
{
    // TODO: implement. Validate 1 <= Stars <= 5 in C# and rely on the DB
    // CHECK constraint as the second line of defense.
    return Results.Created("/api/products/0/reviews/0", new { });
});

// ============================================================================
// THE PERFORMANCE-COMPARISON ENDPOINT
// ============================================================================

app.MapGet("/api/_perf/category-summaries", async (CatalogDb db) =>
{
    // TODO: implement three strategies (naive, include+split, projection)
    // and return their timings + correctness flags as JSON.
    return Results.Ok(new
    {
        naive = new { ms = 0, sqlCount = 0, rowsReturned = 0, correctness = "TODO" },
        include = new { ms = 0, sqlCount = 0, rowsReturned = 0, correctness = "TODO" },
        projection = new { ms = 0, sqlCount = 0, rowsReturned = 0, correctness = "TODO" },
    });
});

app.MapGet("/", () => "Crunch Catalog API. See /api/categories for the entry point.");

app.Run();

// ============================================================================
// DTOs — record types are cheap and JSON-friendly
// ============================================================================

public record CreateProductDto(string Name, string Sku, Money Price, int CategoryId);
public record AddReviewDto(int Stars, string Body);
public record CategoryListItem(int Id, string Name, int ProductCount);
public record ProductListItem(int Id, string Name, string Sku, Money Price);
public record SearchHit(int Id, string Name, Money Price);

// ============================================================================
// SEED
// ============================================================================

static async Task SeedAsync(CatalogDb db)
{
    if (await db.Categories.AnyAsync()) return;

    var rng = new Random(42);
    var tools = new Category { Name = "Tools" };
    var apparel = new Category { Name = "Apparel" };
    var drinkware = new Category { Name = "Drinkware" };
    db.Categories.AddRange(tools, apparel, drinkware);
    await db.SaveChangesAsync();

    var products = new[]
    {
        new Product { Name = "Hex Wrench",  Sku = "HW-001", Price = new Money(8.50m,  "USD"), CategoryId = tools.Id },
        new Product { Name = "Pipe Cutter", Sku = "PC-001", Price = new Money(42.00m, "USD"), CategoryId = tools.Id },
        new Product { Name = "T-Shirt",     Sku = "TS-001", Price = new Money(19.99m, "USD"), CategoryId = apparel.Id },
        new Product { Name = "Hoodie",      Sku = "HD-001", Price = new Money(49.99m, "USD"), CategoryId = apparel.Id },
        new Product { Name = "Crunch Mug",  Sku = "MG-001", Price = new Money(11.50m, "USD"), CategoryId = drinkware.Id },
    };
    db.Products.AddRange(products);
    await db.SaveChangesAsync();

    foreach (var p in products)
    {
        var count = rng.Next(2, 6);
        for (int i = 0; i < count; i++)
        {
            p.Reviews.Add(new Review
            {
                Stars = rng.Next(3, 6),
                Body = $"Review {i + 1} for {p.Name}.",
                PostedAt = DateTimeOffset.UtcNow.AddDays(-rng.Next(0, 60)),
            });
        }
    }
    await db.SaveChangesAsync();
}
