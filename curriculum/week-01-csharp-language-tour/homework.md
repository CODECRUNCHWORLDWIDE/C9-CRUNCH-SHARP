# Week 1 Homework

Six practice problems that revisit the week's topics. The full set should take about **6 hours** in total. Work in your Week 1 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — `dotnet --info` audit

**Problem statement.** Run `dotnet --info` on your machine and write the relevant pieces into a file `notes/dotnet-info.md`. For each of the following, state the value you see and (where applicable) whether it is what you expected:

1. The SDK version that will be used when you run `dotnet build` (the "primary" SDK).
2. The host runtime version.
3. The architecture (`x64`, `arm64`, `arm`).
4. The `RID` (runtime identifier) — e.g. `osx-arm64`, `linux-x64`, `win-x64`.
5. The location of installed SDKs (the path printed after each installed version).

Then state one sentence answering: *if I ran `dotnet publish -c Release` right now on a project with `<TargetFramework>net9.0</TargetFramework>`, what RID would the resulting native artifact target by default?*

**Acceptance criteria.**

- File `notes/dotnet-info.md` exists with the five values and your one-sentence answer.
- Committed.

**Hint.** `dotnet --info` is verbose. Look under "Host" for runtime/architecture, under ".NET SDKs installed" for SDK paths, and under "Runtime Environment" for the RID. The default publish RID is your current machine's RID unless you pass `-r`.

**Estimated time.** 20 minutes.

---

## Problem 2 — Three records, one switch expression

**Problem statement.** In a new file `homework/p2-shapes/Program.cs`, define three records that model 2D shapes:

```csharp
public record Circle(double Radius);
public record Rectangle(double Width, double Height);
public record Triangle(double Base, double Height);
```

Then write a single function `Area(object shape)` that uses a **switch expression** to compute the area of any of the three. Add a discard pattern that throws `ArgumentException` for unknown types.

Drive it from `Main` with three example shapes and print the results.

**Acceptance criteria.**

- A buildable, runnable console project under `homework/p2-shapes/`.
- `dotnet build`: 0 warnings, 0 errors.
- `dotnet run` prints three areas, one per shape.
- The body of `Area` is exactly one switch expression — no `if`/`else`, no separate type checks.
- A passing xUnit test project alongside that tests at least one of each case.
- Committed.

**Hint.** Circle area is `Math.PI * r * r`. Rectangle area is `w * h`. Triangle area is `0.5 * b * h`. The pattern is `Circle c => …`.

**Estimated time.** 45 minutes.

---

## Problem 3 — Nullable refs in a real scenario

**Problem statement.** Take exercise 3 (`Profiles.Cli`) from the exercises folder. Add a new method:

```csharp
public static string PrimaryEmail(Profile profile);
```

The profile gains an optional `Emails` property (a `IReadOnlyList<string>?`). `PrimaryEmail` returns the **first** email if any, or `"(none)"` otherwise. The signature must accurately reflect that `profile` can be `null` and that `Emails` can be `null` or empty.

Write three tests:

1. A profile with two emails returns the first.
2. A profile with `Emails = null` returns `"(none)"`.
3. A `null` profile returns `"(none)"`.

**Acceptance criteria.**

- The new method is implemented in the Profiles project.
- Tests pass: `dotnet test`.
- The implementation contains **zero** `!` operators.
- The build is warning-free.
- Committed.

**Hint.** Pattern: `profile?.Emails is { Count: > 0 } emails ? emails[0] : "(none)"`.

**Estimated time.** 45 minutes.

---

## Problem 4 — A LINQ pipeline you can read in six months

**Problem statement.** Create `homework/p4-linq/Program.cs`. Given a hard-coded `List<Transaction>` of at least 20 entries (mix of credits and debits across at least three different days), compute and print:

1. **Net total** across all transactions.
2. **Daily totals**: one line per day, in chronological order, formatted as `2026-05-13: 17.50`.
3. **Top 3 debits** (the three most-negative amounts), printed largest-loss-first with their memo.
4. **Days with a net loss**: list the dates whose totals are negative.

The pipeline must:

- Use LINQ (`GroupBy`, `Select`, `OrderBy`, `Take`) for all computations.
- Materialize with `.ToList()` exactly **once**, at the boundary where you actually need to enumerate twice.
- Be readable — line breaks per chained operator.

**Acceptance criteria.**

- Project builds and runs, printing all four sections.
- `dotnet build`: 0 warnings, 0 errors.
- Exactly one `.ToList()` (or `.ToArray()`) in the file.
- Committed.

**Hint.** `transactions.GroupBy(t => t.Date).Select(g => (Day: g.Key, Total: g.Sum(x => x.Amount))).OrderBy(x => x.Day)`. For "top 3 debits": `transactions.Where(t => t.Amount < 0).OrderBy(t => t.Amount).Take(3)`.

**Estimated time.** 1 hour.

---

## Problem 5 — Write five `[Theory]` tests

**Problem statement.** Pick a small piece of code you wrote this week — `Greeter.Greet`, `Payments.Classify`, anything with a clear input/output shape. Convert at least **five** of your existing tests (or write five new ones if you have none) into **`[Theory]` + `[InlineData]`** form so that one test method covers multiple cases.

**Acceptance criteria.**

- At least five `[Theory]` tests in your test project.
- Each `[Theory]` has at least three `[InlineData]` rows.
- `dotnet test` shows all of them passing.
- Each test method has one assertion. No "test that does five things."
- Committed.

**Hint.**

```csharp
[Theory]
[InlineData(10, "credit")]
[InlineData(-5, "debit")]
[InlineData(0, "zero")]
public void Classify_categorises_amount_correctly(decimal amount, string expected)
{
    var p = new Payment(DateOnly.MinValue, amount, "memo", PaymentMethod.Cash);
    Assert.Equal(expected, Payments.Classify(p));
}
```

**Estimated time.** 1 hour.

---

## Problem 6 — Mini reflection essay

**Problem statement.** Write a 300–400 word reflection at `notes/week-01-reflection.md` answering:

1. Which felt easiest: the toolchain, records and pattern matching, or nullable references? Which felt hardest? Why?
2. Did anything you previously believed about C# (or .NET, or Microsoft tooling) turn out to be wrong this week? If so, what?
3. If you had to explain "the difference between C# 13, .NET 9, and ASP.NET Core 9" to a Python colleague in one paragraph, what would you say?
4. What's one thing you'd want to learn next that this week didn't cover?

**Acceptance criteria.**

- File exists, 300–400 words.
- Each numbered question is addressed in its own paragraph.
- File is committed.

**Hint.** This is for *you*, not for a grade. Be honest. Future-you reading it after Week 12 will be grateful.

**Estimated time.** 30 minutes.

---

## Time budget recap

| Problem | Estimated time |
|--------:|--------------:|
| 1 | 20 min |
| 2 | 45 min |
| 3 | 45 min |
| 4 | 1 h 0 min |
| 5 | 1 h 0 min |
| 6 | 30 min |
| **Total** | **~4 h 20 min** |

When you've finished all six, push your repo and open the [mini-project](./mini-project/README.md).
