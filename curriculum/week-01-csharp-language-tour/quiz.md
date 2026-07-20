# Week 1 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 2. Answer key at the bottom — don't peek.

---

**Q1.** Which of the following best describes the relationship between C# 13, .NET 9, and ASP.NET Core 9?

- A) They are three names for the same product, shipped together for marketing reasons.
- B) C# 13 is the language, .NET 9 is the runtime/SDK, and ASP.NET Core 9 is a framework that runs on .NET 9.
- C) .NET 9 is a subset of ASP.NET Core 9; you need ASP.NET Core to run any C# program.
- D) C# 13 is the runtime, .NET 9 is the language version, and ASP.NET Core 9 is the IDE.

---

**Q2.** Given:

```csharp
public record Money(decimal Amount, string Currency);
var a = new Money(10m, "USD");
var b = new Money(10m, "USD");
Console.WriteLine(a == b);
```

What does this print?

- A) `False` — records compare by reference like other classes.
- B) `True` — records implement structural (value) equality automatically.
- C) It does not compile; `record` types cannot use `==`.
- D) `False` — `decimal` does not support `==` reliably.

---

**Q3.** Which expression idiomatically returns `"unknown"` when `name` is `null`?

- A) `name == null ? "unknown" : name`
- B) `name ?? "unknown"`
- C) `name?.ToString() ?? "unknown"`
- D) `name!`

---

**Q4.** You have `string? maybeName = GetName();`. Which of the following is the C9-preferred way to safely call `.Length` on it?

- A) `maybeName!.Length`
- B) `(maybeName ?? "").Length`
- C) `maybeName is not null ? maybeName.Length : 0`
- D) `maybeName?.Length ?? 0`

(Choose the **best** answer, not just one that compiles.)

---

**Q5.** Which command scaffolds a new console project named `Foo` inside a folder `src/Foo/`?

- A) `dotnet create console Foo`
- B) `dotnet new console -n Foo -o src/Foo`
- C) `dotnet init console --name Foo --path src/Foo`
- D) `dotnet template console Foo src/Foo`

---

**Q6.** Given:

```csharp
int[] xs = [1, 2, 3, 4, 5];
var ys = xs.Where(x => x > 2);
xs[2] = 99;
foreach (var y in ys) Console.Write($"{y} ");
```

What does this print?

- A) `3 4 5`
- B) `99 4 5`
- C) `4 5`
- D) It throws an exception because `xs` was mutated mid-iteration.

---

**Q7.** Which statement about `await` is correct?

- A) `await` blocks the calling thread until the task completes.
- B) `await` suspends the method and releases the thread; the runtime resumes the method when the task completes.
- C) `await` only works inside a `lock` block.
- D) `await` is equivalent to `.Result`; the two are interchangeable.

---

**Q8.** What does this expression evaluate to?

```csharp
string Classify(int[] xs) => xs switch
{
    []           => "empty",
    [var only]   => "one",
    [_, _]       => "two",
    _            => "many"
};

Classify([10, 20, 30]);
```

- A) `"empty"`
- B) `"one"`
- C) `"two"`
- D) `"many"`

---

**Q9.** You enable nullable reference types and the compiler warns: `CS8602: Dereference of a possibly null reference`. Which response is the **worst** habit to adopt?

- A) Add an `is not null` guard before the dereference.
- B) Use `??` to provide a fallback value.
- C) Use `?.` to short-circuit on null.
- D) Add `!` to silence the warning.

---

**Q10.** Where in a stock .NET solution do build artifacts (compiled `.dll`s) land by default?

- A) In a top-level `build/` folder at the solution root.
- B) In each project's `bin/<Configuration>/<TFM>/` folder.
- C) In `~/.dotnet/builds/`, a global per-user cache.
- D) In `obj/Debug/`; the `bin/` folder is for published artifacts only.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **B** — C# is the language, .NET is the runtime/SDK, ASP.NET Core is one of several frameworks that target .NET. The three ship aligned but are distinct concepts.
2. **B** — `record` types get a compiler-generated `Equals`, `GetHashCode`, and `==`/`!=` based on the values of their fields. This is the entire point of records.
3. **B** — `??` is the null-coalescing operator. C is fine on a different type but redundant for a `string?`. A works but is verbose. D introduces a runtime crash hazard.
4. **D** — `?.Length` returns `int?`; `?? 0` defaults the null case. This is the idiomatic single-line pattern. C is correct but unnecessarily verbose. A trains a dangerous habit. B does an unnecessary allocation.
5. **B** — The flags are `-n` (name) and `-o` (output path). The verb is `dotnet new <template>`.
6. **B** — LINQ's `Where` is deferred. The query runs at iteration time, when `xs[2]` is already `99`. (Week 2 covers this in depth, but the principle is from Week 1.)
7. **B** — `await` is the entire point: suspend and release, rather than block. A is the behavior of `.Result`/`.Wait()` — the deadlock-prone way.
8. **D** — The first three patterns match arrays of size 0, 1, and 2. A 3-element array falls through to the discard pattern `_`.
9. **D** — `!` is the null-forgiving operator. It promises the compiler "trust me, this isn't null." When you're wrong, you get a `NullReferenceException` at runtime — exactly what nullable refs were introduced to prevent.
10. **B** — Each project has its own `bin/<Configuration>/<TFM>/`. The `obj/` folder is for intermediate files (caches, generated code), not the deployable artifact. There is no top-level build folder by default.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
