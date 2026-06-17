// Exercise 1 — Migrations workflow + reading the SQL log.
//
// Goal: stand up a DbContext against SQLite, add two migrations (one to create
// the schema, one to add a column), apply them, and observe the SQL log for
// every CRUD shape. By the end you can identify the SQL EF emits for each
// LINQ shape and can produce both a non-idempotent and an idempotent script.
//
// Project layout (you create this — there is no template to copy from):
//
//   src/Ex01.Migrations/
//     Ex01.Migrations.csproj
//     Program.cs                  <-- this file
//     CatalogDb.cs                <-- the DbContext + entities (also this file)
//     Migrations/                 <-- created by `dotnet ef migrations add`
//
// .csproj contents you need:
//
//   <Project Sdk="Microsoft.NET.Sdk">
//     <PropertyGroup>
//       <OutputType>Exe</OutputType>
//       <TargetFramework>net8.0</TargetFramework>
//       <Nullable>enable</Nullable>
//       <ImplicitUsings>enable</ImplicitUsings>
//     </PropertyGroup>
//     <ItemGroup>
//       <PackageReference Include="Microsoft.EntityFrameworkCore"        Version="8.0.0" />
//       <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.0" />
//       <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.0" />
//     </ItemGroup>
//   </Project>
//
// Commands you run (in this order):
//
//   dotnet new console -n Ex01.Migrations -f net8.0
//   cd Ex01.Migrations
//   # paste the .csproj contents shown above
//   # paste this file's contents into Program.cs and CatalogDb.cs (split below)
//   dotnet tool install --global dotnet-ef --version 8.0.0  # once per machine
//   dotnet ef migrations add InitialCreate
//   dotnet ef database update
//   dotnet run
//   dotnet ef migrations add AddProductSkuColumn
//   dotnet ef database update
//   dotnet run
//   dotnet ef migrations script --idempotent --output deploy.sql
//   cat deploy.sql                                # read it; the IF NOT EXISTS guards are visible
//
// Acceptance criteria:
//   1. The first `dotnet run` produces the SQL log of an INSERT, a SELECT-by-key,
//      an UPDATE (only the changed columns), and a DELETE.
//   2. After AddProductSkuColumn, the SQL log shows the additional column on
//      every SELECT and INSERT.
//   3. The idempotent script (`deploy.sql`) contains `IF NOT EXISTS` guards
//      keyed off __EFMigrationsHistory. (SQLite emits these as SELECT 1 FROM
//      __EFMigrationsHistory WHERE MigrationId='...'; the wrapping is provider-
//      specific but always present.)

// ============================================================================
// PART 1 — CatalogDb.cs (paste into its own file in the project)
// ============================================================================

#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ex01.Migrations;

public sealed class CatalogDb : DbContext
{
    public CatalogDb(DbContextOptions<CatalogDb> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>().Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.Entity<Product>().Property(p => p.Price).HasColumnType("decimal(19,4)");
        builder.Entity<Category>().Property(c => c.Name).HasMaxLength(64).IsRequired();
    }
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Add this property AFTER the first migration has been generated.
    // It will be picked up by the second migration (AddProductSkuColumn).
    public string? Sku { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = new();
}

// ============================================================================
// PART 2 — Program.cs
// ============================================================================
//
// using Ex01.Migrations;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
//
// var optionsBuilder = new DbContextOptionsBuilder<CatalogDb>();
// optionsBuilder
//     .UseSqlite("Data Source=catalog.db")
//     .LogTo(Console.WriteLine, LogLevel.Information)
//     .EnableSensitiveDataLogging();
//
// using var db = new CatalogDb(optionsBuilder.Options);
//
// // Step 1: apply migrations at startup. In real apps you would prefer the
// // idempotent-script workflow, but for this exercise we keep it inline so the
// // SQL log shows the migration application.
// await db.Database.MigrateAsync();
//
// // Step 2: seed one category and one product if the DB is empty.
// if (!await db.Categories.AnyAsync())
// {
//     var cat = new Category { Name = "Tools" };
//     db.Categories.Add(cat);
//     await db.SaveChangesAsync();
//
//     db.Products.Add(new Product { Name = "Hex Wrench", Price = 8.50m, CategoryId = cat.Id });
//     await db.SaveChangesAsync();
// }
//
// // Step 3: run the four CRUD shapes and read the SQL log between them.
// Console.WriteLine("\n=== READ-BY-KEY ===\n");
// var p = await db.Products.FirstAsync();
// Console.WriteLine($"  Loaded: {p.Name} @ {p.Price:C}");
//
// Console.WriteLine("\n=== UPDATE ===\n");
// p.Price *= 1.10m;
// await db.SaveChangesAsync();
//
// Console.WriteLine("\n=== INSERT ===\n");
// db.Products.Add(new Product { Name = "Allen Key", Price = 4.25m, CategoryId = p.CategoryId });
// await db.SaveChangesAsync();
//
// Console.WriteLine("\n=== DELETE ===\n");
// var toRemove = await db.Products.OrderBy(x => x.Id).LastAsync();
// db.Products.Remove(toRemove);
// await db.SaveChangesAsync();
//
// Console.WriteLine("\n=== LIST ===\n");
// var all = await db.Products.AsNoTracking().ToListAsync();
// foreach (var prod in all) Console.WriteLine($"  {prod.Id} {prod.Name} {prod.Price:C}");

// ============================================================================
// CHECKLIST AFTER YOU RUN IT
// ============================================================================
//
//   [ ] First `dotnet ef migrations add InitialCreate` produces:
//         Migrations/20XXXXXX_InitialCreate.cs
//         Migrations/20XXXXXX_InitialCreate.Designer.cs
//         Migrations/CatalogDbModelSnapshot.cs
//
//   [ ] `dotnet run` SQL log shows, for the UPDATE step, that only the
//       `Price` column appears in the SET clause (not Name, not CategoryId).
//       That is the change tracker computing a minimal diff from the snapshot.
//
//   [ ] After adding the `Sku` property and running `dotnet ef migrations add
//       AddProductSkuColumn`, the generated migration's Up() contains
//       `migrationBuilder.AddColumn<string>(name: "Sku", ...)`.
//
//   [ ] After `dotnet ef database update`, subsequent SELECTs include the
//       `Sku` column in the projection list.
//
//   [ ] `dotnet ef migrations script --idempotent --output deploy.sql`
//       produces a script that, when re-applied to the same database, does
//       nothing (the IF NOT EXISTS guards stop both Up() bodies from running
//       a second time).
//
// Stretch (counted toward Exercise 1 if you finish the above with time left):
//   Add a third migration that creates an index on Product.CategoryId.
//   Verify the SELECT-by-CategoryId now uses the index by reading the EXPLAIN
//   output (SQLite: `EXPLAIN QUERY PLAN SELECT ...`).
