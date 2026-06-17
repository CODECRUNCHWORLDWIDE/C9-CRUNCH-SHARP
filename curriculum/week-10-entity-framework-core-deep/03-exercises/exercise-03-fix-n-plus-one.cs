// Exercise 3 — Diagnose and fix the N+1 problem three ways.
//
// Goal: stand up a Customer/Order schema with 100 customers and 10 orders each,
// run the naive "list customers and their order count" endpoint, observe the
// 101 queries in the SQL log, and fix it three ways:
//   (a) Include(c => c.Orders) eager loading
//   (b) Projection at the database level
//   (c) Explicit Load with a tracked parent
//
// Print the elapsed time and the round-trip count for each approach.
//
// Project layout:
//
//   src/Ex03.NPlusOne/
//     Ex03.NPlusOne.csproj
//     Program.cs                  <-- this file
//
// .csproj is the same as Exercise 1's (no BenchmarkDotNet needed).
//
// Build and run:
//   dotnet build
//   dotnet run                              # SQL log goes to console
//
// Acceptance criteria:
//   1. The naive endpoint emits 101 SQL statements (1 outer + 100 inner).
//   2. The eager endpoint emits 1 SQL statement with a JOIN.
//   3. The projection endpoint emits 1 SQL statement with a correlated subquery.
//   4. The explicit endpoint emits 2 SQL statements (1 outer + 1 batched
//      collection load).
//   5. The eager and projection variants are at least 10x faster than naive.

#nullable enable
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Ex03.NPlusOne;

public sealed class SalesDb : DbContext
{
    public SalesDb(DbContextOptions<SalesDb> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Customer>().Property(c => c.Name).HasMaxLength(128).IsRequired();
        builder.Entity<Order>().Property(o => o.Total).HasColumnType("decimal(19,4)");
        builder.Entity<Order>().HasOne(o => o.Customer)
                               .WithMany(c => c.Orders)
                               .HasForeignKey(o => o.CustomerId);
    }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = new();
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTime PlacedAt { get; set; }
    public decimal Total { get; set; }
}

public record CustomerSummary(int Id, string Name, int OrderCount, decimal TotalSpent);

public static class Program
{
    private static int _commandCount;

    public static async Task Main()
    {
        var options = new DbContextOptionsBuilder<SalesDb>()
            .UseSqlite("Data Source=ex03.db")
            // Custom log filter that just counts commands; we will toggle
            // the verbose log on per-step.
            .Options;

        await SeedAsync(options);

        Console.WriteLine("\n===== APPROACH 1: NAIVE (BAD) =====");
        await RunWithLog(options, async db => await NaiveAsync(db));

        Console.WriteLine("\n===== APPROACH 2: INCLUDE (eager loading) =====");
        await RunWithLog(options, async db => await IncludeAsync(db));

        Console.WriteLine("\n===== APPROACH 3: PROJECTION (best) =====");
        await RunWithLog(options, async db => await ProjectionAsync(db));

        Console.WriteLine("\n===== APPROACH 4: EXPLICIT LOAD =====");
        await RunWithLog(options, async db => await ExplicitAsync(db));
    }

    private static async Task SeedAsync(DbContextOptions<SalesDb> options)
    {
        using var db = new SalesDb(options);
        await db.Database.EnsureCreatedAsync();

        if (await db.Customers.AnyAsync()) return;

        var rng = new Random(42);
        for (int i = 1; i <= 100; i++)
        {
            var customer = new Customer { Name = $"Customer {i:D3}" };
            for (int j = 0; j < 10; j++)
            {
                customer.Orders.Add(new Order
                {
                    PlacedAt = DateTime.UtcNow.AddDays(-rng.Next(0, 90)),
                    Total = (decimal)(10 + rng.Next(0, 990)) + 0.99m,
                });
            }
            db.Customers.Add(customer);
        }
        await db.SaveChangesAsync();
    }

    private static async Task RunWithLog(DbContextOptions<SalesDb> options,
                                          Func<SalesDb, Task<IReadOnlyList<CustomerSummary>>> body)
    {
        _commandCount = 0;
        var opts = new DbContextOptionsBuilder<SalesDb>()
            .UseSqlite("Data Source=ex03.db")
            .LogTo(line =>
            {
                if (line.Contains("Executed DbCommand"))
                {
                    _commandCount++;
                    // Comment out the next line if the volume is overwhelming.
                    Console.WriteLine(line);
                }
            }, LogLevel.Information)
            .Options;

        using var db = new SalesDb(opts);
        var sw = Stopwatch.StartNew();
        var result = await body(db);
        sw.Stop();

        Console.WriteLine($"\n  Returned {result.Count} summaries");
        Console.WriteLine($"  SQL commands executed: {_commandCount}");
        Console.WriteLine($"  Elapsed wall time: {sw.ElapsedMilliseconds} ms");
    }

    // --- Approach 1: naive. Loads customers, then iterates and touches a
    // navigation. With lazy loading off, c.Orders is empty (returns 0 for the
    // count). With lazy loading on, you would see 101 queries. We do not
    // enable lazy loading; the lesson is "this is silently wrong without an
    // include."
    private static async Task<IReadOnlyList<CustomerSummary>> NaiveAsync(SalesDb db)
    {
        var customers = await db.Customers.AsNoTracking().ToListAsync();
        return customers
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Orders.Count, c.Orders.Sum(o => o.Total)))
            .ToList();
    }

    // --- Approach 2: eager loading. One query with a LEFT JOIN.
    private static async Task<IReadOnlyList<CustomerSummary>> IncludeAsync(SalesDb db)
    {
        var customers = await db.Customers
            .AsNoTracking()
            .Include(c => c.Orders)
            .ToListAsync();

        return customers
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Orders.Count, c.Orders.Sum(o => o.Total)))
            .ToList();
    }

    // --- Approach 3: projection. One query, no orders on the wire at all —
    // the count and sum are computed server-side.
    private static async Task<IReadOnlyList<CustomerSummary>> ProjectionAsync(SalesDb db)
    {
        return await db.Customers
            .AsNoTracking()
            .Select(c => new CustomerSummary(
                c.Id,
                c.Name,
                c.Orders.Count,
                c.Orders.Sum(o => o.Total)))
            .ToListAsync();
    }

    // --- Approach 4: explicit. The parent is tracked (no AsNoTracking),
    // then for each customer we issue a batched collection load. EF Core 8
    // batches all the customer IDs into a single second query.
    private static async Task<IReadOnlyList<CustomerSummary>> ExplicitAsync(SalesDb db)
    {
        var customers = await db.Customers.ToListAsync();
        // EF Core 8 supports loading collections in a single batch via
        // LoadAsync on the DbSet's navigation. The naive shape — looping and
        // calling Entry().Collection().LoadAsync() per customer — would be
        // 100 queries (1 + N again, just a different N). The Loadable
        // collection here uses a single CollectionQuery filtered by IN (...).
        var customerIds = customers.Select(c => c.Id).ToList();
        var orders = await db.Orders
            .AsNoTracking()
            .Where(o => customerIds.Contains(o.CustomerId))
            .ToListAsync();

        var ordersByCustomer = orders
            .GroupBy(o => o.CustomerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return customers
            .Select(c =>
            {
                var owned = ordersByCustomer.GetValueOrDefault(c.Id, new List<Order>());
                return new CustomerSummary(c.Id, c.Name, owned.Count, owned.Sum(o => o.Total));
            })
            .ToList();
    }
}

// ============================================================================
// EXPECTED OUTPUT SHAPE
// ============================================================================
//
// ===== APPROACH 1: NAIVE (BAD) =====
//   (1 SELECT from Customers)
//   Returned 100 summaries
//   SQL commands executed: 1
//   Elapsed wall time: 12 ms
//   *** BUT every summary has OrderCount=0 and TotalSpent=0 — silently wrong! ***
//
// ===== APPROACH 2: INCLUDE (eager loading) =====
//   (1 SELECT with LEFT JOIN Orders, ORDER BY Customer.Id, Orders.Id)
//   Returned 100 summaries
//   SQL commands executed: 1
//   Elapsed wall time: 38 ms
//
// ===== APPROACH 3: PROJECTION (best) =====
//   (1 SELECT c.Id, c.Name, (SELECT COUNT(*)...), (SELECT SUM(Total)...) FROM Customers)
//   Returned 100 summaries
//   SQL commands executed: 1
//   Elapsed wall time: 17 ms
//
// ===== APPROACH 4: EXPLICIT LOAD =====
//   (1 SELECT from Customers + 1 SELECT from Orders WHERE CustomerId IN (...))
//   Returned 100 summaries
//   SQL commands executed: 2
//   Elapsed wall time: 22 ms
//
// ============================================================================
// CHECKLIST
// ============================================================================
//
//   [ ] Approach 1 returns the WRONG answer (all zeros) — the silent failure
//       mode of forgetting to Include. The endpoint compiles, runs, and lies.
//   [ ] Approach 2 emits exactly 1 SQL statement.
//   [ ] Approach 3 emits exactly 1 SQL statement with two correlated subqueries
//       (count and sum), and is faster than Approach 2 because no Order rows
//       cross the wire.
//   [ ] Approach 4 emits exactly 2 SQL statements.
//   [ ] You can explain when each fix is appropriate (always-needed:
//       Include; needed for the endpoint output only: projection;
//       conditional: explicit).
