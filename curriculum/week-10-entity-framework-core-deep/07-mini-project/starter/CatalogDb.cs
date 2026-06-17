// CrunchCatalog.Data/CatalogDb.cs
//
// Starter DbContext for the Week 10 mini-project. You fill in the model
// configuration, the seed routine, and the value-converter applications.

#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrunchCatalog.Data;

public readonly record struct CategoryId(int Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct ProductId(int Value)
{
    public override string ToString() => Value.ToString();
}

public readonly record struct Money(decimal Amount, string Currency)
{
    public override string ToString() => $"{Currency} {Amount:F2}";
}

public sealed class Category
{
    public CategoryId Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = new();
}

public sealed class Product
{
    public ProductId Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public Money Price { get; set; }
    public CategoryId CategoryId { get; set; }
    public Category? Category { get; set; }
    public List<Review> Reviews { get; set; } = new();
}

public sealed class Review
{
    public int Id { get; set; }
    public ProductId ProductId { get; set; }
    public Product? Product { get; set; }
    public int Stars { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; }
}

public sealed class CatalogDb : DbContext
{
    public CatalogDb(DbContextOptions<CatalogDb> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ConfigureCategory(builder.Entity<Category>());
        ConfigureProduct(builder.Entity<Product>());
        ConfigureReview(builder.Entity<Review>());
    }

    private static void ConfigureCategory(EntityTypeBuilder<Category> e)
    {
        // Strongly-typed ID converter. The wire type is int; the C# type is CategoryId.
        e.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CategoryId(value));

        e.Property(c => c.Name).HasMaxLength(64).IsRequired();
        e.HasIndex(c => c.Name).IsUnique();

        // SQLite hint: case-insensitive collation via column annotation.
        // For Postgres, use `.UseCollation(...)` against a custom collation.
    }

    private static void ConfigureProduct(EntityTypeBuilder<Product> e)
    {
        e.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ProductId(value));

        e.Property(p => p.CategoryId)
            .HasConversion(id => id.Value, value => new CategoryId(value));

        e.Property(p => p.Name).HasMaxLength(128).IsRequired();
        e.Property(p => p.Sku).HasMaxLength(32).IsRequired();
        e.HasIndex(p => p.Sku).IsUnique();

        // Money as a same-row value object. EF Core 8 recommends ComplexProperty.
        e.ComplexProperty(p => p.Price, pb =>
        {
            pb.Property(m => m.Amount).HasColumnName("PriceAmount").HasColumnType("decimal(19,4)");
            pb.Property(m => m.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsFixedLength();
        });

        e.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureReview(EntityTypeBuilder<Review> e)
    {
        e.Property(r => r.ProductId)
            .HasConversion(id => id.Value, value => new ProductId(value));

        e.Property(r => r.Body).HasMaxLength(2000).IsRequired();
        e.Property(r => r.PostedAt).IsRequired();
        e.Property(r => r.Stars).IsRequired();

        e.ToTable(t => t.HasCheckConstraint("CK_Reviews_Stars_Range", "\"Stars\" BETWEEN 1 AND 5"));

        e.HasOne(r => r.Product)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
