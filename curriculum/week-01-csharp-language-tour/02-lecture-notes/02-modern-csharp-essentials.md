# Lecture 2 — Modern C# Essentials

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can read modern, idiomatic C# 13 — records, pattern matching, nullable refs, primary constructors, file-scoped namespaces, `async`/`await`, LINQ — and write small examples in each idiom without copying from a tutorial.

If you only remember one thing from this lecture, remember this:

> **Modern C# is record-first, pattern-matching-first, nullable-aware by default.** If you write a class with mutable properties and explicit equality, you are writing 2015 C#. If you reach for an `if`/`else` ladder where a switch expression would do, you are leaving safety on the table. Write 2026 C# from day one.

This lecture covers a lot of surface area on purpose: the rest of the course assumes you have seen all of it once. We go deeper on each topic in later weeks.

---

## 1. Top-level statements and the smallest possible program

Open the `Program.cs` your `dotnet new console` template produced. In .NET 9 it is exactly one line:

```csharp
Console.WriteLine("Hello, World!");
```

That is a **top-level program**. There is no `class Program`, no `static void Main`, no namespace. Top-level statements were added in C# 9 (.NET 5) and made the default template starting with .NET 6. Behind the scenes the compiler synthesizes a `Program` class with a `Main` method that holds those statements; you just don't write the ceremony.

You can still write the long form when you need to:

```csharp
namespace Hello;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}
```

Use top-level statements for entry points; use named classes for everything else. We use both in this course.

---

## 2. File-scoped namespaces and `global using`

Older C# wraps every file's contents in `namespace Foo { ... }`. Modern C# uses **file-scoped namespaces** (C# 10, .NET 6) — one less level of indentation across an entire codebase:

```csharp
namespace Ledger.Cli;

public sealed class Transaction
{
    public decimal Amount { get; init; }
}
```

The `namespace Ledger.Cli;` line applies to the rest of the file. There is no closing `}` at the bottom.

**`global using`** (C# 10) lets you declare `using` directives once per project rather than once per file:

```csharp
// file: GlobalUsings.cs
global using System;
global using System.Collections.Generic;
global using System.Linq;
```

Combined with `<ImplicitUsings>enable</ImplicitUsings>` in the `.csproj`, the SDK already adds a tuned set of `global using` directives for you. You only add `global using` for project-specific namespaces (your own domain, common third-party libs you use everywhere).

Read your `obj/Debug/net9.0/Ledger.Cli.GlobalUsings.g.cs` to see exactly what `ImplicitUsings` added.

---

## 3. Value types vs reference types

This is the type-system distinction that trips up Python and JavaScript developers most.

| | Reference type | Value type |
|--|----------------|-----------|
| Keyword | `class`, `interface`, `record` (default) | `struct`, `enum`, `record struct` |
| Lives | On the managed heap | On the stack (or inline in its container) |
| Variable holds | A pointer to the object | The bytes of the object itself |
| Copy on assignment | Copies the pointer (two refs, one object) | Copies the bytes (two independent values) |
| Default value | `null` | All-zero bits of the type |
| Equality (default) | Reference equality | Bitwise equality |

```csharp
class Point { public int X; public int Y; }

var a = new Point { X = 1, Y = 2 };
var b = a;        // both variables point to the SAME object
b.X = 99;
Console.WriteLine(a.X); // 99 — surprise (or not, if you're used to refs)
```

```csharp
struct Point2 { public int X; public int Y; }

var a = new Point2 { X = 1, Y = 2 };
var b = a;        // b is an independent COPY
b.X = 99;
Console.WriteLine(a.X); // 1
```

Use `class` for entities and services. Use `struct` for small immutable value-like things (a 2D coordinate, a money amount, a timestamp interval). When in doubt, **use a class**, or better yet a **record** (next section). Structs have sharp edges — they get copied a lot, they cannot be `null`, they have surprising behavior in `List<T>` mutation — and you should reach for them only when the value-type semantics are what you want.

> **The string exception.** `string` is a reference type but acts like a value type because it is **immutable**. Two strings with the same characters compare equal with `==`, behave like values in assignments (copying a `string` reference is harmless because nobody can change the bytes), and you almost never think about their heap-ness. This is one of the design choices the .NET team got exactly right.

The heap/stack distinction matters less than it used to: the JIT will sometimes promote local-only objects to the stack (escape analysis) and value types of certain shapes can end up on the heap (boxed). Treat the table above as the conceptual model, not as a hardware guarantee.

---

## 4. Records — the default modeling tool

Before C# 9 (.NET 5), defining an immutable data type required ceremony: a class, several properties with `{ get; init; }`, an `Equals` override, a `GetHashCode` override, a constructor, a `ToString`. Pages of code.

With records, the same type is one line:

```csharp
public record Transaction(DateOnly Date, decimal Amount, string Memo);
```

That gives you:

- An immutable type with three properties: `Date`, `Amount`, `Memo`.
- A constructor: `new Transaction(date, amount, memo)`.
- Structural equality: two `Transaction` values with the same field values are `==`.
- A useful `ToString`: `"Transaction { Date = 2026-05-13, Amount = 10.00, Memo = Coffee }"`.
- A **`with`-expression** for non-destructive mutation:

```csharp
var t1 = new Transaction(DateOnly.Parse("2026-05-13"), 10m, "Coffee");
var t2 = t1 with { Amount = 12m };          // a new record, same except Amount
```

`with` is the operation Python developers reach for `dataclasses.replace` for, and Java developers reach for a Lombok-style builder for. In C# it is part of the language.

Records come in two flavors:

```csharp
public record class Transaction(DateOnly Date, decimal Amount, string Memo); // reference type (default)
public record struct Money(decimal Amount, string Currency);                  // value type
```

The bare `record` keyword means `record class`. Use `record struct` when you want value semantics and the type is small (typically <= 16 bytes; bigger ones cost more to copy than they save in heap pressure).

You can also write a record with a longer body when you need members:

```csharp
public record Transaction(DateOnly Date, decimal Amount, string Memo)
{
    public bool IsRefund => Amount < 0m;

    public string Format() => $"{Date:yyyy-MM-dd}  {Amount,10:N2}  {Memo}";
}
```

The properties from the positional syntax are still there; the body adds derived members.

> **Use records by default.** If your type is mostly data with little behavior, it should be a `record`. If it has significant behavior and identity that is not its values (a service, a repository, a connection), it should be a `class`. Plain `class` with public getters and setters is rarely the right answer in 2026 — usually you wanted a `record` and didn't realize it.

---

## 5. Pattern matching

C# has accumulated one of the most expressive pattern-matching systems of any mainstream statically-typed language. The pieces you should know on sight:

### The `is` pattern

```csharp
object o = "hello";

if (o is string s)
{
    Console.WriteLine(s.Length); // 5
}
```

`is` both tests the type and binds a new variable (`s`) in the truthy branch. Always prefer this to a separate cast.

### Switch expressions

```csharp
public string Describe(Transaction t) => t.Amount switch
{
    > 0m  => "credit",
    < 0m  => "debit",
    0m    => "zero",
    _     => "impossible" // exhaustiveness fallback
};
```

Note this is an **expression**, not a statement — it returns a value. Compare to the older `switch` statement which only does control flow. Use switch expressions wherever you would otherwise write an `if`/`elif`/`else` ladder that produces a value.

### Property patterns

```csharp
public string Describe(Transaction t) => t switch
{
    { Amount: > 0m, Memo: "refund" } => "refund (positive)",
    { Amount: > 0m }                 => "credit",
    { Amount: < 0m }                 => "debit",
    _                                => "zero"
};
```

You match against the *shape* of an object — its property values — in a single, readable expression.

### List patterns (available since C# 11 / .NET 7)

```csharp
public string Classify(int[] xs) => xs switch
{
    []                  => "empty",
    [var only]          => $"single: {only}",
    [var first, .., var last] => $"first={first}, last={last}",
    _                   => "other"
};
```

`..` is the "slice" pattern; it matches zero or more elements without binding them.

### Type + property combined

```csharp
public string Render(object o) => o switch
{
    Transaction { Amount: > 0m } t => $"+{t.Amount}",
    Transaction t                  => $"{t.Amount}",
    string s                       => $"\"{s}\"",
    null                           => "(null)",
    _                              => o.ToString() ?? "?"
};
```

**Coding standard for C9: prefer switch expressions to `if`/`else if` chains when computing a value.** They are exhaustive (the compiler will warn if you forget a case in some setups) and they communicate intent better than scattered conditions.

---

## 6. Nullable reference types

The single most consequential C# feature of the last decade. Available since C# 8 (.NET Core 3.0), enabled by default in `dotnet new` templates since .NET 6.

The idea: the compiler tracks which reference variables *can* be `null` and warns when you might dereference one without checking. The runtime is unchanged — at run time a `string?` and a `string` are the same bytes. The difference is the compile-time analysis.

### Turning it on

Project-wide in your `.csproj`:

```xml
<Nullable>enable</Nullable>
```

Or per file:

```csharp
#nullable enable
```

The default `dotnet new console` template has it enabled.

### The syntax

```csharp
string a = "always a value";   // non-nullable
string? b = null;              // nullable — explicitly opted in
```

The `?` says "this reference can legally be null." Without `?`, the compiler treats the variable as non-null and yells at you if you try to assign `null` to it.

### Reading nullability warnings

```csharp
string? maybeName = GetName();

Console.WriteLine(maybeName.Length); // CS8602: Dereference of a possibly null reference.

if (maybeName is not null)
{
    Console.WriteLine(maybeName.Length); // fine — the compiler narrowed the type
}
```

The compiler does flow analysis. After an `is not null` check, an `is { } x` pattern, or assignment from a non-null source, the variable's nullability narrows. Lean on this rather than reaching for `!`.

### The `!` operator (null-forgiving)

```csharp
string? maybeName = GetName();
Console.WriteLine(maybeName!.Length); // "I know better — don't warn me."
```

`!` tells the compiler "trust me, this is not null." Use it sparingly, only when you have an invariant the compiler cannot see. **Treating `!` as a warning silencer is a code smell.** Code review should challenge every `!`.

### The `??` and `??=` operators

```csharp
string display = maybeName ?? "(anonymous)";          // null-coalescing: use right if left is null
maybeName ??= "(anonymous)";                          // null-coalescing assignment: assign only if null
```

These are the right way to provide defaults. They are clearer than `maybeName == null ? "(anonymous)" : maybeName`.

### Required members (since C# 11 / .NET 7)

```csharp
public class Person
{
    public required string Name { get; init; }
    public string? Nickname { get; init; }
}
```

`required` says the property *must* be set in an object initializer. Without it, the compiler warns at the call site. This is the modern alternative to constructor parameter lists for data shapes that need to set 5+ fields.

```csharp
var p = new Person { Name = "Ada" };           // ok
var bad = new Person { };                       // CS9035: required 'Name' not set
```

---

## 7. Primary constructors (since C# 12 / .NET 8)

Primary constructors let you declare constructor parameters in the type declaration and use them anywhere in the type body. Records have always had them (positional syntax = primary constructor); C# 12 extended the feature to classes and structs.

```csharp
public class TransactionParser(IClock clock, ILogger<TransactionParser> log)
{
    public Transaction Parse(string line)
    {
        log.LogDebug("Parsing line at {When}", clock.Now);
        // ...
        return new Transaction(/* ... */);
    }
}
```

`clock` and `log` are in scope throughout the class body. There is no separate field declaration, no `this.clock = clock` ceremony.

A few things to know:

- The parameters are **not** automatically public properties. (Records do auto-expose them; classes do not.)
- If you want to expose one, add an explicit property: `public IClock Clock { get; } = clock;`.
- The parameters are captured per instance — you can read them in any method.
- For dependency injection (Week 4), primary constructors are now the idiomatic way to declare class-level dependencies.

---

## 8. Collection expressions (since C# 12 / .NET 8)

A small but pleasant feature. The unified syntax `[a, b, c]` initializes any collection the target type can accept:

```csharp
int[] arr = [1, 2, 3];
List<int> list = [1, 2, 3];
HashSet<int> set = [1, 2, 3];
Span<int> span = [1, 2, 3];

// spread
int[] more = [..arr, 4, 5];   // [1, 2, 3, 4, 5]
```

Use this instead of `new[] { 1, 2, 3 }` or `new List<int> { 1, 2, 3 }`. The compiler emits the most efficient construction for the target type.

---

## 9. `async`/`await` — the bare minimum

Asynchronous code is the entire focus of Week 3. For Week 1, you need enough to read it, not enough to fully reason about it.

```csharp
public async Task<string> FetchAsync(Uri uri, CancellationToken ct)
{
    using var http = new HttpClient();
    HttpResponseMessage response = await http.GetAsync(uri, ct);
    return await response.Content.ReadAsStringAsync(ct);
}
```

Read this in three parts:

1. **`async Task<string>`** — the method is asynchronous and eventually returns a `string`. The `Task<T>` is .NET's "promise of a future value."
2. **`await`** — pauses this method until the awaited operation completes, releasing the calling thread to do other work in the meantime. Crucially, **`await` does not block a thread**; the runtime resumes the method when the work is ready.
3. **`CancellationToken ct`** — a cooperative cancellation signal. Idiomatic .NET passes one as the last parameter to every async method.

Three pitfalls we will cover deeply in Week 3 but should flag now:

- **Don't `.Result` or `.Wait()` on a `Task`** to get the value synchronously. It can deadlock and it defeats the entire purpose of async.
- **Don't write `async void`** except for event handlers. `async void` swallows exceptions and makes errors invisible.
- **Don't call sync I/O methods inside an `async` method** when an async variant exists. `File.ReadAllText` should be `File.ReadAllTextAsync`. Stephen Toub has written extensively on why.

---

## 10. LINQ basics

**Language-Integrated Query (LINQ)** is .NET's standard data-pipeline API. It is the single most useful feature for a Python developer to adopt: where Python reaches for list comprehensions, generator expressions, and the `itertools` module, C# reaches for LINQ.

### Method syntax

The most common form in modern C# is method syntax:

```csharp
using System.Linq;

int[] amounts = [10, -5, 20, -3, 8];

decimal total = amounts.Sum();
int positive = amounts.Count(x => x > 0);
int[] doubled = amounts.Select(x => x * 2).ToArray();
int[] big = amounts.Where(x => Math.Abs(x) > 5).ToArray();
```

The pattern is **start with a sequence → chain transformations → terminate with a materializing call (`.ToList()`, `.ToArray()`, `.Sum()`, etc.)**.

### Common operators

| Operator | What it does |
|----------|--------------|
| `Where` | Filter |
| `Select` | Project (Python: `map` / list comprehension) |
| `SelectMany` | Flatten (Python: chain.from_iterable) |
| `OrderBy` / `OrderByDescending` / `ThenBy` | Sort |
| `GroupBy` | Group |
| `Distinct` | Dedupe |
| `Take` / `Skip` | Pagination |
| `First` / `FirstOrDefault` | Pick one |
| `Any` / `All` | Existence checks |
| `Count` | Counts (use the predicate overload: `Count(x => …)`) |
| `Sum` / `Min` / `Max` / `Average` | Aggregations |
| `Aggregate` | General fold |
| `ToList` / `ToArray` / `ToDictionary` | Materialize |

### A small pipeline

```csharp
var transactions = new List<Transaction>
{
    new(DateOnly.Parse("2026-05-13"), 10m,  "Coffee"),
    new(DateOnly.Parse("2026-05-13"), 22m,  "Lunch"),
    new(DateOnly.Parse("2026-05-14"), -3m,  "Refund"),
    new(DateOnly.Parse("2026-05-14"), 5m,   "Tip"),
};

decimal totalSpentMay13 = transactions
    .Where(t => t.Date == DateOnly.Parse("2026-05-13"))
    .Where(t => t.Amount > 0)
    .Sum(t => t.Amount);

// 32m
```

### Query syntax (you will see it; rarely use it)

LINQ also has a SQL-like query expression form:

```csharp
var byDay = from t in transactions
            group t by t.Date into g
            select new { Day = g.Key, Total = g.Sum(x => x.Amount) };
```

You will encounter this in older codebases. Most modern .NET teams use method syntax everywhere and only fall back to query syntax for complex `join`s and `group`s. We will use method syntax in C9 unless the query form is genuinely clearer.

### Deferred execution — preview

```csharp
var positive = transactions.Where(t => t.Amount > 0);  // NO query has run yet
var first = positive.First();                          // NOW the Where runs, until it finds one
```

Most LINQ operators are **lazy** — they don't do work until you iterate. We dig into deferred execution in Week 2. For Week 1, just be aware: putting a `.ToList()` in the wrong place can change correctness and performance.

---

## 11. Putting it together — a small example

Here is a single file that uses every Week 1 idea: records, switch expressions, nullable references, LINQ, file-scoped namespace, top-level statements.

```csharp
using System.Globalization;

string[] rawLines =
[
    "2026-05-13,10.00,Coffee",
    "2026-05-13,-22.50,Lunch refund",
    "2026-05-14,5.00,Tip",
    "",                       // empty line — we should skip
    "garbage row",            // malformed — we should report
];

Transaction?[] parsed = [.. rawLines.Select(Parse)];

IEnumerable<Transaction> good = parsed.OfType<Transaction>();

decimal net = good.Sum(t => t.Amount);
int credits = good.Count(t => t.Amount > 0);
int debits  = good.Count(t => t.Amount < 0);

Console.WriteLine($"Parsed {good.Count()} of {rawLines.Length} lines.");
Console.WriteLine($"Credits: {credits}  Debits: {debits}  Net: {net:N2}");

static Transaction? Parse(string line) =>
    line.Split(',') switch
    {
        ["", _, _] or [_, "", _] or [_, _, ""] => null,
        [string d, string a, string memo]
            when DateOnly.TryParse(d, CultureInfo.InvariantCulture, out var date)
              && decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt)
            => new Transaction(date, amt, memo),
        _ => null
    };

public record Transaction(DateOnly Date, decimal Amount, string Memo);
```

Read it slowly. There are five features in twenty-five lines:

- A `record` for the data shape.
- A switch expression with **list patterns** and `when` clauses on the parser.
- **Nullable references** as the return type of `Parse` — `null` for malformed lines.
- A **collection expression** to materialize the parsed array.
- **LINQ** (`OfType`, `Sum`, `Count`) over the parsed sequence.

That is the modern C# 13 baseline. It compiles on .NET 9 with zero warnings. **No `if`/`else` ladder. No null checks. No mutable state.** The mini-project this week extends this exact pattern.

---

## 12. Recap

You should now be able to:

- Read and write records, including `with`-expressions and `record struct`.
- Use switch expressions, property patterns, and list patterns as defaults.
- Enable nullable references and respond to a warning without reaching for `!`.
- Read a primary constructor on a class.
- Recognize a file-scoped namespace and a global using.
- Read an `async` method and know what `await` does.
- Write a small LINQ pipeline and reason about whether you've materialized it.

Next, do the exercises — three short drills that exercise each of those skills in isolation.

---

## References

- *A tour of C#* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/>
- *Records* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record>
- *Pattern matching* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns>
- *Nullable reference types* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references>
- *Primary constructors* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors>
- *Collection expressions* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions>
- *LINQ* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/linq/>
- *Async programming in C#* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/>
