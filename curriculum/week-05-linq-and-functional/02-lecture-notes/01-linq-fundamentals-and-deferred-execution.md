# Lecture 1 — LINQ Fundamentals and Deferred Execution

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can distinguish `IEnumerable<T>` from `IQueryable<T>` at every call site, read deferred-execution traces in `Stopwatch` output, predict the exact moment a LINQ chain materializes, refactor a procedural loop into a LINQ pipeline that compiles to the same IL, and use the C# 13 / .NET 9 LINQ additions (`CountBy`, `AggregateBy`, `Index`) deliberately.

If you only remember one thing from this lecture, remember this:

> **LINQ is two things, not one.** `IEnumerable<T>.Where` runs your `Func<T, bool>` delegate, in process, against each element. `IQueryable<T>.Where` *captures* your delegate as an `Expression<Func<T, bool>>`, never invokes it, and ships its tree to a provider that translates it into the target language (SQL for EF Core, MQL for MongoDB.Driver, anything for a custom provider). Both expose the same surface. Both look identical at the call site. Both behave **completely differently**. Master which of the two you are holding at each line, and the rest of LINQ — operators, deferred execution, materialization — is configuration on top.

---

## 1. What `IEnumerable<T>` actually is

`IEnumerable<T>` is the simplest interface in the BCL that lets you pull a sequence of `T` values one at a time. The whole interface:

```csharp
public interface IEnumerable<out T>
{
    IEnumerator<T> GetEnumerator();
}

public interface IEnumerator<out T> : IDisposable
{
    T Current { get; }
    bool MoveNext();
    void Reset();
}
```

That is the entire contract. When you write `foreach (var x in xs)`, the C# compiler lowers it to roughly:

```csharp
using (var e = xs.GetEnumerator())
{
    while (e.MoveNext())
    {
        var x = e.Current;
        // body
    }
}
```

The implication is that an `IEnumerable<T>` is **pull-based**. The consumer asks "give me the next element"; the producer decides what to give. The producer can read from an array, run an algorithm, query a database, or generate elements lazily with `yield return`. The consumer never knows.

This is why a LINQ chain is so flexible: every operator wraps the input `IEnumerable<T>` in another `IEnumerable<T>` and forwards `MoveNext` calls down the chain. No work happens until the consumer enumerates.

---

## 2. The iterator pattern: `yield return`

LINQ-to-objects is built on the iterator pattern, and the iterator pattern is what `yield return` produces. Write the smallest interesting iterator:

```csharp
public static IEnumerable<int> CountTo(int max)
{
    Console.WriteLine($"CountTo({max}) called");
    for (var i = 1; i <= max; i++)
    {
        Console.WriteLine($"  yielding {i}");
        yield return i;
    }
    Console.WriteLine("CountTo done");
}
```

Now call it but **do not** enumerate:

```csharp
var seq = CountTo(3);
Console.WriteLine("After call, before foreach");
```

Run it. The output is:

```
After call, before foreach
```

Nothing else. The `Console.WriteLine($"CountTo({max}) called")` did not print. The compiler rewrote `CountTo` into a class that implements `IEnumerable<int>` + `IEnumerator<int>`, and the *only* thing the call did was instantiate that class. The body runs when you call `MoveNext`.

Add `foreach (var x in seq) Console.WriteLine(x);` and re-run:

```
After call, before foreach
CountTo(3) called
  yielding 1
1
  yielding 2
2
  yielding 3
3
CountTo done
```

The interleaving is the punch-line. The body runs *between* `MoveNext` calls. Every `yield return` is a suspension point — exactly the way `await` is a suspension point in async — except cooperative iteration is synchronous.

This is also why **every standard LINQ operator** is an iterator. `Where`, `Select`, `SelectMany`, `Distinct`, `OrderBy`, `Take`, `Skip`, `TakeWhile`, `SkipWhile`, `Concat`, `Zip`, `Chunk`, `GroupBy` — all of them. Each one wraps the input enumerator and forwards `MoveNext`.

---

## 3. Deferred execution

Here is the most-misread line of LINQ code in production:

```csharp
var active = users.Where(u => u.IsActive);
```

What does this do?

- **It does not iterate `users`.**
- **It does not call `u.IsActive` on any user.**
- **It allocates a single `WhereEnumerableIterator<User>` and returns it.**

The work happens when somebody enumerates `active`. If the next line is `foreach (var u in active) ...`, the predicate runs once per element. If the next line is `var count = active.Count();`, the predicate runs once per element. If the next line is `var first = active.First();`, the predicate runs once per element *until the first match*, then stops.

But what if you do this:

```csharp
var active = users.Where(u => Slow(u));
var count1 = active.Count();
var count2 = active.Count();
```

`Slow` runs twice per element. The first `Count()` enumerates the full chain. The second `Count()` enumerates it *again* — the iterator is re-created from scratch. There is no memoization.

The fix, when you intend to enumerate twice, is **explicit materialization**:

```csharp
var active = users.Where(u => Slow(u)).ToList();  // runs Slow N times, allocates one list
var count1 = active.Count;  // List<T>.Count, O(1), no Slow calls
var count2 = active.Count;  // same
```

Note the second time we wrote `.Count`, not `.Count()`. `List<T>.Count` is a *property* — it returns the cached size of the list. `IEnumerable<T>.Count()` is a *LINQ extension method* — it re-enumerates the sequence and counts.

### The materialization operators

These force enumeration:

| Operator | Returns | Cost |
|---------|---------|------|
| `ToList()` | `List<T>` | O(n), one allocation |
| `ToArray()` | `T[]` | O(n), one allocation (often two — list-then-copy) |
| `ToDictionary(keySelector)` | `Dictionary<TKey, T>` | O(n), one allocation |
| `ToHashSet()` | `HashSet<T>` | O(n), one allocation |
| `ToFrozenDictionary(keySelector)` | `FrozenDictionary<TKey, T>` | O(n), build cost is higher, lookup cost is lower (since .NET 8) |
| `Count()` | `int` | O(n), no allocation (fast-path for `ICollection<T>` is O(1)) |
| `Sum()`, `Min()`, `Max()`, `Average()` | scalar | O(n), no allocation |
| `First()`, `Last()`, `Single()` | `T` | O(n) worst-case, O(1) fast-path for indexable sources |
| `Any()` | `bool` | O(1) until first match |
| `All()` | `bool` | O(n), short-circuits on first false |
| `Aggregate(seed, func)` | scalar | O(n), no extra allocation |

These do **not** force enumeration — they return a new lazy sequence:

| Operator | Returns | Notes |
|---------|---------|-------|
| `Where`, `Select`, `SelectMany`, `Concat`, `Zip` | `IEnumerable<U>` | streams |
| `Take`, `Skip`, `TakeWhile`, `SkipWhile`, `Chunk` | `IEnumerable<U>` | streams |
| `Distinct`, `DistinctBy` | `IEnumerable<T>` | streams, but holds a `HashSet<T>` internally |
| `OrderBy`, `OrderByDescending`, `ThenBy`, `Reverse` | `IOrderedEnumerable<T>` | **materializes on first `MoveNext`** (you cannot sort lazily) |
| `GroupBy`, `Join`, `GroupJoin` | `IEnumerable<IGrouping<TKey, T>>` | **materializes on first `MoveNext`** |
| `Index()`, `CountBy`, `AggregateBy` | `IEnumerable<...>` | streams (since .NET 9) |

The "materialize on first `MoveNext`" subtlety bites people. `Where(p).OrderBy(k)` runs `Where` lazily but **consumes the entire sequence and sorts it the moment you ask for the first element**. There is no way to sort a stream — you cannot know which element is smallest without reading all of them. This is why `db.Users.OrderBy(u => u.Name)` against EF Core's `IQueryable<T>` translates to SQL `ORDER BY`, but against an in-memory `IEnumerable<T>` it forces a full sort.

---

## 4. The two LINQs: `IEnumerable<T>` vs `IQueryable<T>`

Look at two near-identical lines:

```csharp
var a = users.Where(u => u.Email.EndsWith("@example.com")).ToList();   // users is List<User>
var b = db.Users.Where(u => u.Email.EndsWith("@example.com")).ToList(); // db.Users is DbSet<User>
```

Both compile. Both type-check. Both produce `List<User>` of the same users.

But:

- `a` runs the predicate, in process, in your C# host, on every `User` in the list. The CPU work is your delegate invoked N times.
- `b` ships an expression tree to EF Core, which compiles it to `SELECT * FROM Users WHERE Email LIKE '%@example.com'`, sends the SQL to PostgreSQL, and materializes the results back into `User` instances. **Your `u.Email.EndsWith("@example.com")` delegate never runs.**

How does the compiler choose? It looks at the *static type* of the receiver:

- `List<User>` implements `IEnumerable<User>` but not `IQueryable<User>`. So the compiler binds `Where` to `Enumerable.Where<User>(IEnumerable<User>, Func<User, bool>)`.
- `DbSet<User>` implements `IQueryable<User>`. So the compiler binds `Where` to `Queryable.Where<User>(IQueryable<User>, Expression<Func<User, bool>>)`.

The difference between `Func<User, bool>` and `Expression<Func<User, bool>>` is *the* most important type distinction in LINQ:

- `Func<User, bool>` is an ordinary delegate. The compiler emits a delegate object pointing at a compiled method body. You can `.Invoke(user)` it.
- `Expression<Func<User, bool>>` is an in-memory tree of `ParameterExpression`, `MemberExpression`, `MethodCallExpression`, etc. It is *data*, not *code*. EF Core's translator walks it and emits SQL.

The same lambda — `u => u.Email.EndsWith("@example.com")` — compiles to one or the other depending on which overload it is being passed to. The compiler does this automatically. You never write `Expression<...>` by hand in normal code; you just write the lambda, and the receiver's type picks the form.

**The Week 5 reflex.** When you read LINQ code, your first job is to know which one you are looking at:

| Receiver type | LINQ kind | Where the delegate runs |
|--------------|-----------|------------------------|
| `T[]`, `List<T>`, `IList<T>`, `ICollection<T>` | LINQ-to-objects (`IEnumerable<T>`) | In your C# process, on the calling thread |
| `IEnumerable<T>` | LINQ-to-objects | Same |
| `IAsyncEnumerable<T>` | LINQ-to-async-objects (`System.Linq.Async`) | Same, async-aware |
| `IQueryable<T>` (DbSet, MongoCollection) | LINQ-to-provider | Translated to provider's language, runs there |
| `IObservable<T>` (Rx) | LINQ-to-observable (push) | In your process, on the producer's scheduler |

We focus on the top two rows this week. Week 6 lives on row 4.

---

## 5. The standard operators, in three groups

LINQ's surface is large — ~80 methods — but it falls into three categories:

### Restriction and projection (you reach for these every day)

```csharp
xs.Where(predicate)                          // filter
xs.Select(selector)                          // map each element
xs.SelectMany(selector)                      // flatten nested sequences
xs.OfType<U>()                               // filter by runtime type
xs.Cast<U>()                                 // unchecked cast each element
xs.Take(count)                               // first N
xs.Skip(count)                               // drop first N
xs.TakeWhile(predicate)                      // take until predicate fails
xs.SkipWhile(predicate)                      // skip while predicate true
xs.Chunk(size)                               // batches of `size` (last may be smaller)
xs.Distinct()                                // unique by Equals
xs.DistinctBy(keySelector)                   // unique by projected key
```

### Aggregation and ordering (you reach for these when you have a question)

```csharp
xs.Count()                                   // total count
xs.Count(predicate)                          // count matching predicate
xs.Sum() / .Min() / .Max() / .Average()      // numeric aggregates
xs.MinBy(keySelector) / xs.MaxBy(keySelector)// the *element* with the smallest/largest key
xs.Any() / xs.Any(predicate)                 // is there at least one?
xs.All(predicate)                            // do all match?
xs.First() / .FirstOrDefault()               // get the first element
xs.Single() / .SingleOrDefault()             // get the only element (throws if more)
xs.Last() / .LastOrDefault()                 // get the last element
xs.OrderBy(keySelector)                      // sort ascending
xs.OrderByDescending(keySelector)            // sort descending
xs.ThenBy(keySelector)                       // secondary sort (chain after OrderBy)
xs.Reverse()                                 // reverse order
xs.Aggregate(seed, func)                     // fold
```

### Grouping, joining, and set operations (you reach for these in pipelines)

```csharp
xs.GroupBy(keySelector)                      // IEnumerable<IGrouping<TKey, T>>
xs.Join(ys, xKey, yKey, resultSelector)      // inner join
xs.GroupJoin(ys, xKey, yKey, resultSelector) // join + group on right side
xs.Concat(ys)                                // append
xs.Union(ys)                                 // set union (de-duped)
xs.Intersect(ys)                             // set intersect (de-duped)
xs.Except(ys)                                // set difference (de-duped)
xs.Zip(ys, resultSelector)                   // pairwise combine
```

### Conversion / materialization

```csharp
xs.ToList()                                  // List<T>
xs.ToArray()                                 // T[]
xs.ToDictionary(keySelector)                 // Dictionary<TKey, T>
xs.ToDictionary(keySelector, valueSelector)  // Dictionary<TKey, TValue>
xs.ToHashSet()                               // HashSet<T>
xs.ToFrozenDictionary(keySelector)           // FrozenDictionary<TKey, T> (.NET 8+)
xs.ToLookup(keySelector)                     // ILookup<TKey, T> (like Dictionary but values are IEnumerable<T>)
```

You will recognize these from any LINQ tutorial. The important habit is not memorizing the list — it is *reaching for the right one without leaving your IDE*. The IntelliSense after `xs.` is the single best learning aid.

---

## 6. The .NET 9 LINQ additions

Three new methods ship with .NET 9 and matter on day one.

### `CountBy(keySelector)`

```csharp
public static IEnumerable<KeyValuePair<TKey, int>> CountBy<TSource, TKey>(
    this IEnumerable<TSource> source,
    Func<TSource, TKey> keySelector,
    IEqualityComparer<TKey>? keyComparer = null);
```

Before .NET 9 you wrote:

```csharp
var commitsByAuthor = commits
    .GroupBy(c => c.Author)
    .ToDictionary(g => g.Key, g => g.Count());
```

That builds an intermediate `IGrouping<string, Commit>` for every author (with all the commits inside), then walks each group, then allocates a dictionary. Three allocations per author plus one for the dictionary.

With .NET 9:

```csharp
var commitsByAuthor = commits.CountBy(c => c.Author);
```

One pass. One dictionary internally. No intermediate `IGrouping`. Returns `IEnumerable<KeyValuePair<string, int>>` that you can `.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` if you need the dictionary, or enumerate directly.

Use it whenever you would have written `GroupBy(...).ToDictionary(g => g.Key, g => g.Count())`. The replacement is mechanical.

### `AggregateBy(keySelector, seed, func)`

```csharp
public static IEnumerable<KeyValuePair<TKey, TAccumulate>> AggregateBy<TSource, TKey, TAccumulate>(
    this IEnumerable<TSource> source,
    Func<TSource, TKey> keySelector,
    TAccumulate seed,
    Func<TAccumulate, TSource, TAccumulate> func,
    IEqualityComparer<TKey>? keyComparer = null);
```

The generalization of `CountBy`. Before .NET 9:

```csharp
var bytesByHost = requests
    .GroupBy(r => r.Host)
    .ToDictionary(g => g.Key, g => g.Sum(r => r.Bytes));
```

With .NET 9:

```csharp
var bytesByHost = requests.AggregateBy(
    keySelector: r => r.Host,
    seed:        0L,
    func:        (acc, r) => acc + r.Bytes);
```

Same one-pass / one-dictionary internal structure. The `func` parameter is the fold operation: `(accumulator, currentElement) => newAccumulator`. The `seed` is the initial accumulator for each key.

Use it whenever you would have written `GroupBy(...).ToDictionary(g => g.Key, g => g.Aggregate(...))` or `GroupBy(...).Select(g => (g.Key, g.Sum(...)))`. Replaces both the group-then-sum and group-then-fold patterns.

### `Index()`

```csharp
public static IEnumerable<(int Index, TSource Item)> Index<TSource>(this IEnumerable<TSource> source);
```

Before .NET 9 the canonical "enumerate with index" idiom was:

```csharp
foreach (var (item, i) in items.Select((x, i) => (x, i)))
{
    Console.WriteLine($"{i}: {item}");
}
```

With .NET 9:

```csharp
foreach (var (i, item) in items.Index())
{
    Console.WriteLine($"{i}: {item}");
}
```

Two improvements: the tuple ordering is `(Index, Item)` instead of `(Item, Index)` (matching `KeyValuePair`-style conventions); and the operator name reads exactly like the question it answers. There is no measurable allocation difference — `Select((x, i) => ...)` was already an iterator — but the readability win is meaningful.

Note the field order. `(int Index, TSource Item)` — index first. Python's `enumerate` and Rust's `.enumerate()` both yield `(index, value)`; .NET 9 finally aligned.

---

## 7. Query syntax vs method syntax

Both forms compile to identical IL:

```csharp
// Query syntax
var query =
    from u in users
    where u.IsActive
    where u.Email.EndsWith("@example.com")
    orderby u.LastLogin descending
    select new { u.Name, u.Email };

// Method syntax
var query = users
    .Where(u => u.IsActive)
    .Where(u => u.Email.EndsWith("@example.com"))
    .OrderByDescending(u => u.LastLogin)
    .Select(u => new { u.Name, u.Email });
```

The C# language spec defines query syntax in terms of method calls. The compiler literally rewrites the first form into the second. You can verify on SharpLab: paste the query-syntax form, look at the "C#" output panel, see the method-syntax equivalent.

When to prefer each:

- **Query syntax wins** when you have multi-source joins (`from x in xs from y in x.Children`), `let` clauses for intermediate computations, and the SQL-shaped readability matters.
- **Method syntax wins** for everything else: it is what your tooling refactors into, it composes cleanly with non-LINQ method calls, and it is what every newer LINQ operator (`MinBy`, `MaxBy`, `Chunk`, `DistinctBy`, `CountBy`, `AggregateBy`, `Index`) is exposed as. Query syntax does not have keywords for these — you have to drop out into method syntax anyway.

The Week 5 default: **method syntax**. Query syntax is a tool we keep for specific shapes (joins with `let` clauses, ironically the same shapes that are most awkward in EF Core), not the default.

---

## 8. LINQ-to-objects internals: what `Where` returns

Look at the BCL source for `Enumerable.Where` (~250 lines, MIT-licensed):

```csharp
public static IEnumerable<TSource> Where<TSource>(
    this IEnumerable<TSource> source,
    Func<TSource, bool> predicate)
{
    if (source is null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.source);
    if (predicate is null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.predicate);

    return source switch
    {
        Iterator<TSource> iterator => iterator.Where(predicate),
        TSource[] array            => array.Length == 0
                                        ? Empty<TSource>()
                                        : new WhereArrayIterator<TSource>(array, predicate),
        List<TSource> list         => new WhereListIterator<TSource>(list, predicate),
        _                          => new WhereEnumerableIterator<TSource>(source, predicate),
    };
}
```

The crucial trick: `Where` checks the runtime type of the source. If the source is itself an iterator (a `WhereXxxIterator<T>` from a previous `Where` call), it **fuses** the two predicates into one iterator. Two `Where`s in a row produce *one* iterator, not two.

Look at the BCL's `WhereSelectArrayIterator<TSource, TResult>`. A `Where` followed by a `Select` on an array is fused into a single specialized iterator. The MoveNext loop is one tight loop, not two chained ones. This is why the cost of `xs.Where(...).Select(...)` is roughly the cost of a hand-written `foreach` plus a delegate dispatch.

The fusion stops at operators that cannot fuse: `OrderBy`, `GroupBy`, `Distinct`. Once you hit one of those, the iterator pipeline materializes (sometimes partially) and a fresh pipeline starts.

You will not write the fusion logic. But understanding it answers two common questions:

1. **"Is two `Where`s slower than one?"** No — they fuse.
2. **"Is `Where(...).Select(...)` slower than a `foreach` with an `if`?"** Slightly — the delegate dispatch is not free — but in practice the difference vanishes below ~1000 elements and is dwarfed by anything else you do (allocations, I/O, dictionary lookups).

---

## 9. Writing your own LINQ extension methods

LINQ extension methods are just static methods on a static class, with the `this` modifier on the first parameter. Here is the canonical pattern — a `Tap` operator that performs a side effect on each element and yields the element through:

```csharp
public static class LinqExtensions
{
    public static IEnumerable<T> Tap<T>(this IEnumerable<T> source, Action<T> action)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (action is null) throw new ArgumentNullException(nameof(action));

        foreach (var item in source)
        {
            action(item);
            yield return item;
        }
    }
}
```

Use it:

```csharp
var processed = orders
    .Where(o => o.IsValid)
    .Tap(o => logger.LogInformation("Processing order {Id}", o.Id))
    .Select(o => o.Total)
    .Sum();
```

The pattern is `yield return` plus argument checks. The argument checks are subtle: if you put them *inside* the iterator method body (next to the `yield return`s), they will not fire until the first `MoveNext` because iterator methods do not run until enumerated. The standard trick is to split into a wrapper + an iterator-body:

```csharp
public static IEnumerable<T> Tap<T>(this IEnumerable<T> source, Action<T> action)
{
    if (source is null) throw new ArgumentNullException(nameof(source));
    if (action is null) throw new ArgumentNullException(nameof(action));
    return TapIterator(source, action);

    static IEnumerable<T> TapIterator(IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            action(item);
            yield return item;
        }
    }
}
```

Now the argument exceptions fire at the call site, not at the first `MoveNext`. The BCL uses this pattern for every operator.

Other operators you may want to ship:

- `WhereNotNull<T>(IEnumerable<T?>) -> IEnumerable<T>` — filter out nulls with the right return type (`T`, not `T?`).
- `ChunkBy<T, TKey>(IEnumerable<T>, Func<T, TKey>)` — group adjacent elements with the same key (different from `GroupBy`, which gathers all elements with the same key regardless of position).
- `Interleave<T>(IEnumerable<T>, IEnumerable<T>)` — alternate elements from two sources.
- `Pairwise<T>(IEnumerable<T>)` — yield `(prev, curr)` tuples.

We build two of these in Exercise 1.

---

## 10. Performance honesty

The "LINQ is slow" claim is mostly false in modern .NET, but the truth is nuanced:

- **For < 1000 elements, LINQ is fast enough.** The delegate dispatch and iterator allocation cost ~50–200 ns per element overhead vs a hand-written `foreach`. For 100 elements that is 5–20 μs total. Below any user-perceptible latency.
- **For tight inner loops on large arrays of `int`/`double`,** a hand-written `for` loop on a `Span<T>` will beat LINQ by a factor of 3–10×. The .NET 9 BCL has special-cased many operators for `int[]` and `double[]` (the Sum/Min/Max paths are SIMD-vectorized in some cases), but the general-purpose `Where(...).Select(...).Sum()` pipeline is not as fast as a `for` loop over a `Span<int>`.
- **Memory allocations are LINQ's actual cost.** Every `Where`/`Select`/`OrderBy` allocates an iterator object. Every closure that captures a local allocates a closure object. On the hot path of a high-throughput service these add up. `BenchmarkDotNet` with `[MemoryDiagnoser]` is the only honest way to measure.

The .NET 9 BCL has shrunk many of these costs. `Enumerable.Where(...).Select(...)` on a `List<T>` allocates one fused iterator instead of two. `Enumerable.Sum` on `int[]` is vectorized. `Enumerable.Count` on `ICollection<T>` is O(1).

The pragmatic rule: **default to LINQ. Profile when you have a perf problem. Optimize the hot path only after you measure.** The mini-project this week walks through that exact reflex: write the LINQ pipeline first, benchmark both forms, decide based on the numbers.

---

## 11. A worked refactor

A canonical procedural shape:

```csharp
public static Dictionary<string, int> CountErrorsByHost(IEnumerable<LogEntry> entries)
{
    var result = new Dictionary<string, int>();
    foreach (var e in entries)
    {
        if (e.Level != LogLevel.Error) continue;
        if (e.Host is null) continue;
        if (result.TryGetValue(e.Host, out var existing))
        {
            result[e.Host] = existing + 1;
        }
        else
        {
            result[e.Host] = 1;
        }
    }
    return result;
}
```

Step 1 — the pre-.NET-9 LINQ form:

```csharp
public static Dictionary<string, int> CountErrorsByHost(IEnumerable<LogEntry> entries) =>
    entries
        .Where(e => e.Level == LogLevel.Error && e.Host is not null)
        .GroupBy(e => e.Host!)
        .ToDictionary(g => g.Key, g => g.Count());
```

Step 2 — the .NET 9 form with `CountBy`:

```csharp
public static Dictionary<string, int> CountErrorsByHost(IEnumerable<LogEntry> entries) =>
    entries
        .Where(e => e.Level == LogLevel.Error && e.Host is not null)
        .CountBy(e => e.Host!)
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
```

Read each form out loud:

1. "Loop over the entries. For each entry, if it's an error and the host isn't null, find the host in the dictionary; if it's there, increment; if not, set to one. Return the dictionary."
2. "Where the entry is an error and the host is non-null, group by host, then count each group."
3. "Where the entry is an error and the host is non-null, count by host."

Each line is shorter than the last. Each line is the question one level closer to the surface. The .NET 9 form is the question you actually have in your head.

The `BenchmarkDotNet` numbers for these on 100,000 log entries with 1,000 unique hosts (.NET 9, my laptop, your numbers will vary):

| Form | Mean | Allocations |
|------|-----:|-------------:|
| Procedural | 1.8 ms | 24 KB |
| GroupBy + ToDictionary + Count | 2.1 ms | 96 KB |
| `CountBy` + `ToDictionary` | 1.9 ms | 32 KB |

The procedural form is fastest and lowest-allocation. The `CountBy` form is *almost as good* and ~1/4 the lines. The pre-.NET-9 `GroupBy` form is the worst on both axes.

The Week 5 takeaway: **the .NET 9 LINQ form is the new default**. Reach for `CountBy` and `AggregateBy` whenever you would have reached for `GroupBy + ToDictionary`. The savings compound.

---

## 12. Build succeeded

```
Build succeeded · 0 warnings · 0 errors · 312 ms
```

This lecture is dense. Read it twice. Open SharpLab and paste the query-syntax form from §7 — confirm it lowers to method syntax. Open the `dotnet/runtime` LINQ source and read the first 200 lines of `Where.cs`. Then open Exercise 1.

In Lecture 2 we move from the pipeline to the *data flowing through it*: `record` and `record struct` as the default modelling tool, `with` expressions for immutable updates, exhaustive `switch` expressions over closed type hierarchies, and the functional patterns — `Map`, `Bind`, `Match` — that LINQ pipelines naturally compose with.

---

## Self-check questions

Answer these before moving on. They should be reflexive by Friday.

1. What is the runtime type of the variable `q` after `var q = users.Where(u => u.IsActive);`? (Answer: `WhereListIterator<User>` or `WhereEnumerableIterator<User>` depending on the runtime type of `users`. Static type: `IEnumerable<User>`.)
2. How many times does the predicate run in `var q = xs.Where(p); q.Count(); q.Count();`? (Answer: twice per element — once per `Count()` call.)
3. Why does `db.Users.Where(u => u.Email.EndsWith("@example.com")).ToList()` not call `EndsWith` on any user in your process? (Answer: `DbSet<T>` implements `IQueryable<T>`, so `Where` captures the lambda as `Expression<Func<User, bool>>`. EF Core's translator emits SQL. The translator's behavior is week 6's lecture.)
4. What does `OrderBy(...).Select(...)` look like in terms of when the elements are produced? (Answer: nothing happens until the first `MoveNext`. The first `MoveNext` triggers a full sort of the input. Subsequent `MoveNext` calls yield each sorted element through the `Select` projection one at a time.)
5. What is the difference between `xs.Count` and `xs.Count()` when `xs` is `List<int>`? (Answer: `xs.Count` is `List<T>.Count`, an O(1) property. `xs.Count()` is `Enumerable.Count<int>`, a LINQ extension method — but the BCL has a fast path that detects `ICollection<T>` and falls back to the property. So the cost is also O(1) — the difference is one method dispatch, not one full enumeration.)
6. When would you use `AggregateBy` instead of `GroupBy(...).ToDictionary(g => g.Key, g => g.Aggregate(seed, f))`? (Answer: always, in .NET 9. `AggregateBy` is one pass with one dictionary; the `GroupBy` form is one pass to build groupings + one pass per group to fold + one allocation per `IGrouping<T>`.)
7. Why does `Tap` belong in a `LinqExtensions` static class and not as an instance method on a wrapper type? (Answer: because LINQ chains work by extension methods on `IEnumerable<T>` — any value typed as `IEnumerable<T>` should be `Tap`pable, and adding an instance method would require wrapping the source in a custom type.)

---

*Lecture 1 ends here. Read [Lecture 2 — Functional Patterns, Records, Pattern Matching](./02-functional-patterns-records-pattern-matching.md) next.*
