# Week 5 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 6. Answer key at the bottom — don't peek.

---

**Q1.** You write:

```csharp
var q = users.Where(u => u.IsActive);
var count1 = q.Count();
var count2 = q.Count();
```

`users` is `List<User>` with 100,000 elements. How many times does the `u.IsActive` predicate run?

- A) 100,000 — LINQ memoizes the result of the first `Count()`.
- B) 200,000 — the pipeline is deferred; each `Count()` re-enumerates and re-runs the predicate.
- C) 1 — `Count()` is O(1) on `List<T>` so the predicate never runs.
- D) 0 — `Where` does not invoke the predicate until you explicitly call `.Force()`.

---

**Q2.** Two LINQ calls compile and type-check identically at the call site:

```csharp
var a = list.Where(u => u.Email.EndsWith("@example.com"));   // list is List<User>
var b = dbSet.Where(u => u.Email.EndsWith("@example.com"));  // dbSet is DbSet<User>
```

What is the most important difference between `a` and `b`?

- A) `a` is `List<User>` and `b` is `DbSet<User>` — both are concrete types; no semantic difference.
- B) The lambda passed to `a` is a `Func<User, bool>` invoked in process; the lambda passed to `b` is an `Expression<Func<User, bool>>` captured as a tree and translated to SQL by EF Core. The `EndsWith` delegate is never invoked in your process for `b`.
- C) `b` is faster because it runs on the database. There are no other differences.
- D) `a` is type-safe; `b` is dynamically typed.

---

**Q3.** What does `Enumerable.CountBy` (new in .NET 9) replace?

- A) The pattern `xs.GroupBy(keySelector).ToDictionary(g => g.Key, g => g.Count())` — `CountBy` does it in one pass with one dictionary internally, no intermediate `IGrouping<T>` allocations.
- B) The pattern `xs.Count()` — `CountBy` is a faster alternative that uses SIMD on `int[]` inputs.
- C) The pattern `xs.Where(...).Count()` — `CountBy` is a fused version that combines the filter and the count.
- D) The pattern `xs.Aggregate(0, (a, _) => a + 1)` — `CountBy` is a more idiomatic way to count.

---

**Q4.** Given:

```csharp
public sealed record User(string Name, string Email);

var u1 = new User("Ada", "ada@example.com");
var u2 = u1 with { Email = "ada@newdomain.org" };
```

What is true after this code runs?

- A) `u1.Email` is `"ada@newdomain.org"` — `with` mutates the original.
- B) `u1` and `u2` are reference-equal (`ReferenceEquals(u1, u2)` is `true`).
- C) `u1.Email` is unchanged (`"ada@example.com"`); `u2` is a new `User` with the new email. `u1 == u2` is `false` because the `Email`s differ.
- D) `u2` is `null` — `with` requires an inner `init` setter on every property.

---

**Q5.** You declare:

```csharp
public abstract record Result<T>;
public sealed record Ok<T>(T Value)      : Result<T>;
public sealed record Err<T>(string Error): Result<T>;

public static string Format<T>(Result<T> r) => r switch
{
    Ok<T> ok   => $"value: {ok.Value}",
    Err<T> err => $"error: {err.Error}",
};
```

Why is no `_` (discard) case required at the end of the `switch` expression?

- A) Because the compiler does not check exhaustiveness on `switch` expressions over generics.
- B) Because `Result<T>` is `abstract` and `Ok<T>` / `Err<T>` are `sealed`, the compiler can prove the type has exactly those two cases. Exhaustiveness is satisfied.
- C) Because the C# 13 spec automatically generates a `default` arm that throws.
- D) Because `Result<T>` is a `record` and records cannot have unhandled cases.

---

**Q6.** What does this list pattern match?

```csharp
return args switch
{
    [var first, .., var last] => $"first={first}, last={last}",
    [var only]                => $"only={only}",
    []                        => "empty",
};
```

- A) Only arrays of exactly three elements.
- B) An empty array, a single-element array, or an array of two or more elements. The first arm binds `first` to `args[0]` and `last` to `args[args.Length - 1]`; the slice `..` matches the zero or more elements in between.
- C) Only `List<T>` instances — list patterns do not work on arrays.
- D) The match is ambiguous; the compiler emits CS8509.

---

**Q7.** You want to project each element with its index. Which is the .NET 9 idiom?

- A) `for (var i = 0; i < xs.Count; i++)` — LINQ is not the right tool.
- B) `xs.Select((x, i) => (i, x))` — the canonical pre-.NET-9 pattern, still the right answer.
- C) `xs.Index()` — new in .NET 9; yields `IEnumerable<(int Index, T Item)>` with `(Index, Item)` ordering matching `KeyValuePair`-style conventions.
- D) `xs.Zip(Enumerable.Range(0, int.MaxValue))` — wasteful, but it works.

---

**Q8.** Consider:

```csharp
var xs = new int[] { 3, 1, 4, 1, 5, 9, 2, 6 };
var top3 = xs.OrderBy(n => n).Take(3).ToList();
```

How many elements of `xs` are read from the array, and how many are sorted internally?

- A) 3 read; 3 sorted — `Take(3)` short-circuits both the read and the sort.
- B) 8 read; 8 sorted — `OrderBy` is one of the operators that materializes on the first `MoveNext` (you cannot sort a stream), so it consumes the entire input regardless of how many elements the downstream consumes.
- C) 8 read; 3 sorted — the BCL's `OrderBy+Take` is fused into a "partial sort" that only fully sorts the top-K.
- D) 0 read; 0 sorted — the pipeline is deferred until you enumerate, and `.ToList()` does not enumerate.

---

**Q9.** You write a small LINQ extension method:

```csharp
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
```

There is a subtle bug in the argument-checking code. What is it?

- A) `nameof(source)` should be `"source"`. `nameof` is broken on extension methods.
- B) The argument-null checks are inside the iterator method body. Iterator methods do not run their body until the consumer calls `MoveNext`, so the exceptions will be deferred — instead of throwing at the call site, they throw at the first enumeration. The fix is to split into a wrapper method that validates eagerly and a local `Iterator` function that uses `yield return`.
- C) `Action<T>` should be `Func<T, T>` so we can mutate the value.
- D) The method should accept `IEnumerable<T>?` (nullable) so the null check is meaningful.

---

**Q10.** Which combination is the right choice for a "value that may or may not be present, where the absent case carries no extra information"?

- A) `Result<T>` — always; never use `Option<T>` or `T?`.
- B) `Option<T>` (or `Maybe<T>`) when you want the type system to *force* the consumer to handle both cases at compile time; nullable `T?` otherwise (it is shorter, integrates with the BCL, and the C# 13 flow analyzer tracks it).
- C) `T` with sentinel values (`-1` for "no result," `""` for "no string"). Sentinels are cleaner than allocating an option type.
- D) `Task<T>` — wrap the value in an async type to defer the decision.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **B** — Deferred execution means `q` is a `WhereListIterator<User>`, not a materialized collection. Each `Count()` re-runs the iterator pipeline, which means the predicate is invoked once per element per `Count()`. The fix is `var q = users.Where(...).ToList();` — then `Count()` is O(1) and the predicate runs exactly 100,000 times total. Option C is true for the *method dispatch* (LINQ's `Count()` has a fast path for `ICollection<T>` that returns the `Count` property without enumeration), but not when there is a `Where` predicate intervening — the predicate forces enumeration.

2. **B** — This is the most important distinction in LINQ. `Where` on `List<T>` binds to `Enumerable.Where(IEnumerable<T>, Func<T, bool>)`; the lambda is a compiled delegate invoked in process. `Where` on `DbSet<T>` (which is `IQueryable<T>`) binds to `Queryable.Where(IQueryable<T>, Expression<Func<T, bool>>)`; the lambda is captured as an expression tree, and EF Core's translator emits SQL that includes a `WHERE email LIKE '%@example.com'` clause. Your `.EndsWith(...)` method body never runs for the `IQueryable` case. Knowing which one you are holding at every call site is the Week 5 reflex.

3. **A** — `CountBy(keySelector)` replaces the `GroupBy(keySelector).ToDictionary(g => g.Key, g => g.Count())` pattern. The replacement is mechanical and the BCL implementation is one pass with a single internal dictionary — no intermediate `IGrouping<T>` objects. Same for `AggregateBy` (replaces `GroupBy(...).ToDictionary(g => g.Key, g => g.Aggregate(...))`). Use them whenever the old pattern fits.

4. **C** — `with` produces a new instance. The original record is immutable (its properties are `init`-only). The two records are reference-distinct (`ReferenceEquals` is `false`). The value equality compares all properties — `Name` matches but `Email` differs, so `u1 == u2` is `false`. This is the canonical immutable-update idiom in C# 13.

5. **B** — The closed-type-hierarchy idiom. `abstract record` base + `sealed record` cases + a `switch` expression over the base type. The compiler proves at compile time that no other case can exist. If you add a third case `Pending<T>`, the compiler will fail every `switch` over `Result<T>` until you handle `Pending<T>`. This is the C# 13 equivalent of F#'s discriminated unions.

6. **B** — List patterns match by *shape*. `[var first, .., var last]` matches any array of length ≥ 2 (the slice `..` matches zero or more, but the two explicit positions consume two elements). The other arms cover the length-0 and length-1 cases. The `switch` is exhaustive over `int[]` because the compiler knows the three arms together cover lengths 0, 1, and ≥ 2.

7. **C** — `Index()` is new in .NET 9 and yields `IEnumerable<(int Index, T Item)>`. It replaces the `Select((x, i) => (x, i))` idiom with one that is shorter, matches naming conventions in Python's `enumerate` and Rust's `enumerate()`, and exposes the index field first. The pre-.NET-9 form (option B) still works but is no longer the idiom of choice.

8. **B** — `OrderBy` is one of the LINQ operators that materializes on the first `MoveNext`. You cannot sort a stream without reading all of it. So `xs.OrderBy(...).Take(3)` reads all 8 elements, sorts all 8, then yields the first 3 of the sorted result. The BCL does not yet have a "partial top-K" fused operator (option C is wishful thinking; you would have to write it yourself or use `MoreLINQ.PartialSort`). For small inputs the cost is negligible; for very large inputs use a different algorithm (`MinBy`/`MaxBy` for top-1, a heap for top-K).

9. **B** — The argument checks are inside the iterator method body. Iterator methods (any method with `yield return`) are compiled into state-machine classes that do not execute the method body until `MoveNext` is called. So `throw new ArgumentNullException(...)` happens at the first `MoveNext`, not at the call site. The fix is the split-method pattern: an outer wrapper that validates arguments eagerly, then calls an inner `static` local function that contains the `yield return`s. This is exactly the pattern the BCL uses throughout `System.Linq`.

10. **B** — Use `T?` (nullable reference type) for "value or absent, no extra information" cases. C# 13's flow analyzer tracks nullability through your code; if you forget to check, you get a CS8602 warning. For richer absent cases (with a reason, an error message, or a stack trace) graduate to `Result<T>`. `Option<T>` is the heavier-hand answer when you specifically want the type system to *force* the consumer to handle both cases — useful in interop with F# or in domain code where the cost of forgetting is high. Sentinels (option C) are how we used to do it before C# 8; the nullable annotations are strictly better. `Task<T>` (option D) is unrelated — it represents an async value, not an optional one.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
