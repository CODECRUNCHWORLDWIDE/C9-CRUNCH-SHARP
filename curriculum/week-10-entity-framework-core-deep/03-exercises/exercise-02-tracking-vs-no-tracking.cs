// Exercise 2 — Measure the per-row cost of tracking vs no-tracking.
//
// Goal: load a 10,000-row table three ways (AsTracking, AsNoTracking,
// AsNoTrackingWithIdentityResolution) and measure the elapsed time and
// allocations for each. Confirm that AsNoTracking is the highest-leverage
// read-path switch.
//
// Project layout:
//
//   src/Ex02.Tracking/
//     Ex02.Tracking.csproj
//     Program.cs                  <-- this file
//     CatalogDb.cs                <-- one entity, 10,000 rows seeded
//
// .csproj additions on top of Exercise 1's:
//
//   <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
//
// Build:
//   dotnet build -c Release
//   dotnet run   -c Release            # NOT debug; benchmark numbers in debug are meaningless.
//
// Acceptance criteria:
//   1. The benchmark prints three rows: Tracking, NoTracking,
//      NoTrackingWithIdentityResolution.
//   2. NoTracking is faster than Tracking by at least 15% on a 10,000-row read.
//   3. NoTracking allocates less than Tracking by at least 30%.
//   4. You can explain, in one sentence each, why each switch behaves as it does.

#nullable enable
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.EntityFrameworkCore;

namespace Ex02.Tracking;

public sealed class CatalogDb : DbContext
{
    public CatalogDb(DbContextOptions<CatalogDb> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>().Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.Entity<Product>().Property(p => p.Price).HasColumnType("decimal(19,4)");
        builder.Entity<Product>().HasIndex(p => p.CategoryId);
    }
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
}

[MemoryDiagnoser]
public class TrackingBenchmark
{
    private DbContextOptions<CatalogDb> _options = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _options = new DbContextOptionsBuilder<CatalogDb>()
            .UseSqlite("Data Source=ex02.db")
            .Options;

        using var db = new CatalogDb(_options);
        await db.Database.EnsureCreatedAsync();

        if (!await db.Products.AnyAsync())
        {
            var batch = new List<Product>(capacity: 10_000);
            for (int i = 0; i < 10_000; i++)
            {
                batch.Add(new Product
                {
                    Name = $"Product {i:D5}",
                    Price = 10m + (i % 200),
                    CategoryId = 1 + (i % 25),
                });
            }
            db.Products.AddRange(batch);
            await db.SaveChangesAsync();
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Tracking()
    {
        using var db = new CatalogDb(_options);
        var rows = await db.Products.ToListAsync();
        return rows.Count;
    }

    [Benchmark]
    public async Task<int> NoTracking()
    {
        using var db = new CatalogDb(_options);
        var rows = await db.Products.AsNoTracking().ToListAsync();
        return rows.Count;
    }

    [Benchmark]
    public async Task<int> NoTrackingWithIdentityResolution()
    {
        using var db = new CatalogDb(_options);
        var rows = await db.Products.AsNoTrackingWithIdentityResolution().ToListAsync();
        return rows.Count;
    }
}

public static class Program
{
    public static void Main(string[] _)
    {
        // BenchmarkDotNet drives this. Run with `dotnet run -c Release`.
        BenchmarkRunner.Run<TrackingBenchmark>();
    }
}

// ============================================================================
// EXPECTED OUTPUT SHAPE (your absolute numbers will differ; the ratio is the point)
// ============================================================================
//
// |                            Method |      Mean | Ratio |   Allocated | Alloc Ratio |
// |---------------------------------- |----------:|------:|------------:|------------:|
// |                          Tracking |  19.40 ms |  1.00 |     5.92 MB |        1.00 |
// |                        NoTracking |  13.15 ms |  0.68 |     3.55 MB |        0.60 |
// |  NoTrackingWithIdentityResolution |  14.20 ms |  0.73 |     3.86 MB |        0.65 |
//
// Tracking baseline:        100% time, 100% allocation
// NoTracking:               ~68% time, ~60% allocation   <- the win
// NoTrackingWithIdRes:      ~73% time, ~65% allocation   <- the compromise
//
// ============================================================================
// CHECKLIST
// ============================================================================
//
//   [ ] Ran with -c Release (BenchmarkDotNet refuses to run a Debug build).
//   [ ] NoTracking is at least 15% faster than Tracking.
//   [ ] NoTracking allocates at least 30% less than Tracking.
//   [ ] You can answer: "When does AsNoTrackingWithIdentityResolution beat
//       plain AsNoTracking?" Hint: when the result set contains the same row
//       twice via a join, plain NoTracking returns two distinct C# instances
//       and ID resolution returns one shared instance.
//
// Stretch:
//   Add a fourth benchmark that uses `AsNoTracking().Select(p => new
//   ProductDto(p.Id, p.Name, p.Price))` — a projection to a record. Measure
//   how the projection compares. (Expectation: projection is faster again,
//   because the row width on the wire is smaller and the materialization
//   target is a plain record with no tracker plumbing.)
//
//   public record ProductDto(int Id, string Name, decimal Price);
//
//   [Benchmark]
//   public async Task<int> Projection()
//   {
//       using var db = new CatalogDb(_options);
//       var rows = await db.Products
//           .AsNoTracking()
//           .Select(p => new ProductDto(p.Id, p.Name, p.Price))
//           .ToListAsync();
//       return rows.Count;
//   }
