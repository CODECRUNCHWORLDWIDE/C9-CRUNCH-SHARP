// Exercise 1 — LINQ Puzzles
//
// Goal: Drill the ten LINQ operators you reach for the most, plus the three
//       new-in-.NET-9 helpers (CountBy, AggregateBy, Index). By the end you
//       should be able to read each puzzle's prompt and type the pipeline
//       without looking anything up.
//
// Estimated time: 60 minutes (~6 minutes per puzzle).
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir LinqPuzzles && cd LinqPuzzles
//      dotnet new console -n LinqPuzzles -o src/LinqPuzzles
//
//    Replace src/LinqPuzzles/Program.cs with the contents of THIS FILE.
//
// 2. Each puzzle is a `static` method with a `// TODO` body. The expected
//    return value is documented in the comment above the method. Fill in
//    the body with a single LINQ pipeline.
//
// 3. Run:
//
//      dotnet run --project src/LinqPuzzles
//
// 4. The output should match the SMOKE OUTPUT below.
//
// ACCEPTANCE CRITERIA
//
//   [ ] All 10 puzzles return the expected value.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] Each puzzle body is a single LINQ pipeline (one assignment, one return,
//       method-syntax preferred). No `for`/`foreach`. No intermediate variables.
//   [ ] Puzzles 7, 8, 9 use the .NET 9 helpers CountBy, AggregateBy, Index
//       respectively — not the pre-.NET-9 GroupBy(...).ToDictionary(...) form.
//   [ ] No `.Result`, no `.Wait()`, no `async void`.
//
// SMOKE OUTPUT (target)
//
//   == LINQ Puzzles ==
//   Puzzle 1 — adult names               : Ada,Linus,Margaret
//   Puzzle 2 — sum of squares of evens   : 220
//   Puzzle 3 — flatten and distinct      : red,green,blue,yellow,purple
//   Puzzle 4 — top-3 highest score       : Margaret(98),Ada(95),Linus(92)
//   Puzzle 5 — chunks of 3               : [1,2,3] [4,5,6] [7,8,9] [10]
//   Puzzle 6 — products by team          : alpha: lex,raptor / beta: griffin,phoenix
//   Puzzle 7 — count by department       : Engineering:3, Sales:2, Marketing:1
//   Puzzle 8 — total revenue by region   : EMEA:1450, AMER:1180, APAC:730
//   Puzzle 9 — indexed lines             : 0:alpha 1:beta 2:gamma 3:delta
//   Puzzle 10 — first product over $1000 : Phoenix($1450)
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints are at the bottom of the file.

using System.Globalization;

Console.WriteLine("== LINQ Puzzles ==");

// ---------------------------------------------------------------------------
// Test data (used across the puzzles).
// ---------------------------------------------------------------------------

People people =
[
    new("Ada",      Age: 38, Score: 95, Department: "Engineering"),
    new("Linus",    Age: 54, Score: 92, Department: "Engineering"),
    new("Margaret", Age: 41, Score: 98, Department: "Engineering"),
    new("Grace",    Age: 17, Score: 88, Department: "Sales"),
    new("Hedy",     Age: 16, Score: 91, Department: "Sales"),
    new("Alan",     Age: 13, Score: 85, Department: "Marketing"),
];

Teams teams =
[
    new("alpha", ["lex", "raptor"]),
    new("beta",  ["griffin", "phoenix"]),
];

Products products =
[
    new("Lex",     Team: "alpha", Price:  250m),
    new("Raptor",  Team: "alpha", Price:  900m),
    new("Griffin", Team: "beta",  Price:  500m),
    new("Phoenix", Team: "beta",  Price: 1450m),
];

Sales sales =
[
    new("EMEA", Bytes:  500),
    new("EMEA", Bytes:  950),
    new("AMER", Bytes:  430),
    new("AMER", Bytes:  750),
    new("APAC", Bytes:  300),
    new("APAC", Bytes:  430),
];

string[] words = ["alpha", "beta", "gamma", "delta"];
string[][] tags = [["red", "green"], ["blue", "red"], ["green", "yellow", "purple"]];

// ---------------------------------------------------------------------------
// Puzzle 1 — Adult names
// ---------------------------------------------------------------------------
// Return the names of every person aged 18 or older, comma-separated, in the
// order they appear in `people`. Expected: "Ada,Linus,Margaret".
static string AdultNames(IEnumerable<Person> source)
{
    // TODO: One pipeline.
    //   Hint: .Where(...).Select(...).{join with ","}
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 1 — adult names               : {AdultNames(people)}");

// ---------------------------------------------------------------------------
// Puzzle 2 — Sum of squares of evens
// ---------------------------------------------------------------------------
// Given the ints 1..10, return the sum of the squares of the even ones.
// Expected: 4 + 16 + 36 + 64 + 100 = 220.
static int SumOfSquaresOfEvens(IEnumerable<int> source)
{
    // TODO: One pipeline.
    //   Hint: .Where(n => n % 2 == 0).Sum(n => n * n)
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 2 — sum of squares of evens   : {SumOfSquaresOfEvens(Enumerable.Range(1, 10))}");

// ---------------------------------------------------------------------------
// Puzzle 3 — Flatten and distinct
// ---------------------------------------------------------------------------
// Flatten the nested `tags` array, take the distinct values, comma-separate.
// Order matters — preserve first-seen order from the input.
// Expected: "red,green,blue,yellow,purple".
static string FlattenAndDistinct(IEnumerable<string[]> source)
{
    // TODO: One pipeline.
    //   Hint: .SelectMany(t => t).Distinct() preserves first-seen order.
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 3 — flatten and distinct      : {FlattenAndDistinct(tags)}");

// ---------------------------------------------------------------------------
// Puzzle 4 — Top-3 highest scoring people
// ---------------------------------------------------------------------------
// Sort by score descending, take 3, format as "Name(Score)" with comma sep.
// Expected: "Margaret(98),Ada(95),Linus(92)".
static string TopThreeByScore(IEnumerable<Person> source)
{
    // TODO: One pipeline.
    //   Hint: .OrderByDescending(p => p.Score).Take(3).Select(p => $"{p.Name}({p.Score})")
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 4 — top-3 highest score       : {TopThreeByScore(people)}");

// ---------------------------------------------------------------------------
// Puzzle 5 — Chunks of 3
// ---------------------------------------------------------------------------
// Group the ints 1..10 into chunks of 3 (last chunk may be smaller).
// Format each chunk as "[a,b,c]"; space-separate. Expected:
//   "[1,2,3] [4,5,6] [7,8,9] [10]"
static string ChunksOfThree(IEnumerable<int> source)
{
    // TODO: One pipeline.
    //   Hint: .Chunk(3).Select(c => "[" + string.Join(",", c) + "]")
    //         (Chunk is .NET 6+. Last chunk has size <= 3, not == 3.)
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 5 — chunks of 3               : {ChunksOfThree(Enumerable.Range(1, 10))}");

// ---------------------------------------------------------------------------
// Puzzle 6 — Products by team
// ---------------------------------------------------------------------------
// Given the `teams` collection (each team has a name and a list of product
// codes), format as "team1: a,b / team2: c,d". Expected:
//   "alpha: lex,raptor / beta: griffin,phoenix".
static string ProductsByTeam(IEnumerable<Team> source)
{
    // TODO: One pipeline.
    //   Hint: .Select(t => $"{t.Name}: {string.Join(\",\", t.Products)}")
    //         then string.Join(" / ", ...)
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 6 — products by team          : {ProductsByTeam(teams)}");

// ---------------------------------------------------------------------------
// Puzzle 7 — Count by department  (.NET 9 CountBy)
// ---------------------------------------------------------------------------
// Count how many people are in each department, format as
//   "Engineering:3, Sales:2, Marketing:1"
// Ordered by count descending. Use Enumerable.CountBy — NOT GroupBy + Count.
static string CountByDepartment(IEnumerable<Person> source)
{
    // TODO: One pipeline using .CountBy(...).
    //   Hint: source.CountBy(p => p.Department) returns IEnumerable<KeyValuePair<string, int>>.
    //         Then OrderByDescending(kvp => kvp.Value), then format.
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 7 — count by department       : {CountByDepartment(people)}");

// ---------------------------------------------------------------------------
// Puzzle 8 — Total bytes by region (.NET 9 AggregateBy)
// ---------------------------------------------------------------------------
// Sum the `Bytes` field of each Sale, grouped by region. Format as
//   "EMEA:1450, AMER:1180, APAC:730"
// Order by total descending. Use Enumerable.AggregateBy — NOT GroupBy + Sum.
static string TotalBytesByRegion(IEnumerable<Sale> source)
{
    // TODO: One pipeline using .AggregateBy(...).
    //   Hint: source.AggregateBy(
    //             keySelector: s => s.Region,
    //             seed:        0,
    //             func:        (acc, s) => acc + s.Bytes)
    //         returns IEnumerable<KeyValuePair<string, int>>.
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 8 — total revenue by region   : {TotalBytesByRegion(sales)}");

// ---------------------------------------------------------------------------
// Puzzle 9 — Indexed lines (.NET 9 Index)
// ---------------------------------------------------------------------------
// Format each word with its index: "0:alpha 1:beta 2:gamma 3:delta".
// Use Enumerable.Index() — NOT Select((w, i) => ...).
static string IndexedLines(IEnumerable<string> source)
{
    // TODO: One pipeline using .Index().
    //   Hint: source.Index() returns IEnumerable<(int Index, string Item)>.
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 9 — indexed lines             : {IndexedLines(words)}");

// ---------------------------------------------------------------------------
// Puzzle 10 — First product over $1000
// ---------------------------------------------------------------------------
// Find the first product whose price exceeds $1000. Return as "Name($price)".
// If none, return "none". Expected: "Phoenix($1450)".
static string FirstExpensiveProduct(IEnumerable<Product> source)
{
    // TODO: One pipeline using .FirstOrDefault(...).
    //   Hint: source.FirstOrDefault(p => p.Price > 1000m) returns Product?.
    //         Then the formatting is a switch expression or null-coalesce.
    throw new NotImplementedException();
}

Console.WriteLine($"Puzzle 10 — first product over $1000 : {FirstExpensiveProduct(products)}");

Console.WriteLine();
Console.WriteLine("Build succeeded · 0 warnings · 0 errors");

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

public sealed record Person(string Name, int Age, int Score, string Department);

public sealed record Team(string Name, IReadOnlyList<string> Products);

public sealed record Product(string Name, string Team, decimal Price);

public sealed record Sale(string Region, int Bytes);

// Aliases for readability.
public sealed class People  : List<Person>   { }
public sealed class Teams   : List<Team>     { }
public sealed class Products : List<Product> { }
public sealed class Sales   : List<Sale>     { }

// ---------------------------------------------------------------------------
// HINTS — read only after you've spent at least 5 minutes on the puzzle.
// ---------------------------------------------------------------------------
//
// Puzzle 1 — AdultNames
//   return string.Join(",", source.Where(p => p.Age >= 18).Select(p => p.Name));
//
// Puzzle 2 — SumOfSquaresOfEvens
//   return source.Where(n => n % 2 == 0).Sum(n => n * n);
//
// Puzzle 3 — FlattenAndDistinct
//   return string.Join(",", source.SelectMany(t => t).Distinct());
//
// Puzzle 4 — TopThreeByScore
//   return string.Join(",",
//       source.OrderByDescending(p => p.Score).Take(3).Select(p => $"{p.Name}({p.Score})"));
//
// Puzzle 5 — ChunksOfThree
//   return string.Join(" ",
//       source.Chunk(3).Select(c => $"[{string.Join(",", c)}]"));
//
// Puzzle 6 — ProductsByTeam
//   return string.Join(" / ",
//       source.Select(t => $"{t.Name}: {string.Join(",", t.Products)}"));
//
// Puzzle 7 — CountByDepartment
//   return string.Join(", ",
//       source.CountBy(p => p.Department)
//             .OrderByDescending(kvp => kvp.Value)
//             .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
//
// Puzzle 8 — TotalBytesByRegion
//   return string.Join(", ",
//       source.AggregateBy(
//                 keySelector: s => s.Region,
//                 seed:        0,
//                 func:        (acc, s) => acc + s.Bytes)
//             .OrderByDescending(kvp => kvp.Value)
//             .Select(kvp => $"{kvp.Key}:{kvp.Value}"));
//
// Puzzle 9 — IndexedLines
//   return string.Join(" ", source.Index().Select(t => $"{t.Index}:{t.Item}"));
//
// Puzzle 10 — FirstExpensiveProduct
//   var p = source.FirstOrDefault(x => x.Price > 1000m);
//   return p is null ? "none"
//                    : $"{p.Name}(${p.Price.ToString("F0", CultureInfo.InvariantCulture)})";
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// Every puzzle here is a pattern you'll write a thousand times in the rest of
// the curriculum:
//
//   - Puzzle 1, 4 → "give me X about my users" (Where + Select + format).
//   - Puzzle 2    → "fold a numeric result" (Sum/Min/Max/Average/Aggregate).
//   - Puzzle 3, 6 → "flatten and present a join" (SelectMany).
//   - Puzzle 5    → "batch for downstream processing" (Chunk).
//   - Puzzle 7    → "histogram by key" (CountBy).
//   - Puzzle 8    → "totals by key" (AggregateBy).
//   - Puzzle 9    → "iterate with index" (Index).
//   - Puzzle 10   → "find one" (First, FirstOrDefault).
//
// When you reach Week 6 the operators are the same; the receiver becomes an
// IQueryable<T> and EF Core translates each one to SQL. The reflex of writing
// the pipeline in *English shape* first — then trusting the translator — is
// what separates "EF queries that scale" from "EF queries that load every
// row into memory."
//
// ---------------------------------------------------------------------------
