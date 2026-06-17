# Mini-Project — Ledger CLI

> Build a small, typed command-line tool that ingests a CSV of transactions, normalizes them into immutable `record` types, computes summaries, and prints results — using only the .NET 9 base class library and `System.CommandLine`. No web. No database. Just C# 13 on .NET 9.

This is the only mini-project where you are explicitly forbidden from reaching outside the BCL. The point is to internalize the modern .NET surface — records, pattern matching, LINQ, nullable refs, `System.CommandLine`, and xUnit — without any framework wallpaper hiding what is happening.

**Estimated time:** ~8 hours (split across Thursday, Friday, Saturday in the suggested schedule).

---

## What you will build

A console app called `ledger` that supports three commands:

```bash
# Read a CSV and summarize.
ledger summary --file transactions.csv

# Show transactions filtered by a date range.
ledger list --file transactions.csv --from 2026-05-01 --to 2026-05-31

# Show top N debits or credits.
ledger top --file transactions.csv --kind debit --count 5
```

Input format: a comma-separated file, one transaction per line, header row present:

```
date,amount,memo,category
2026-05-13,10.00,Coffee,food
2026-05-13,22.50,Lunch,food
2026-05-14,-5.00,Refund (lunch),food
2026-05-14,9.99,Stream subscription,subscriptions
2026-05-15,150.00,Freelance income,income
```

By the end you'll have a public GitHub repo of ~300–400 lines of C# (excluding tests) that handles malformed input, reports useful error messages, and ships as a single deployable folder.

---

## Rules

- **You may** read Microsoft Learn, the C# language reference, lecture notes, and the source of the libraries listed below.
- **You may NOT** depend on any third-party NuGet package other than:
  - `System.CommandLine` (still in pre-release as of .NET 9 — explicitly allowed).
  - `xUnit` and `Microsoft.NET.Test.Sdk` (the xUnit template's defaults).
- No `Newtonsoft.Json`. No `CsvHelper`. No `Spectre.Console`. Write your own CSV parsing this week; you only need to handle the simple shape above.
- **You must** use a virtual `Directory.Build.props` or per-project setting that turns `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on. Treat warnings as bugs.
- Target framework: `net9.0`. C# language version: the default for the SDK (`13.0`).

---

## Acceptance criteria

- [ ] A new public GitHub repo named `c9-week-01-ledger-<yourhandle>`.
- [ ] Solution layout matches the C9 standard:
  ```
  Ledger/
  ├── Ledger.sln
  ├── .gitignore
  ├── Directory.Build.props        (treats warnings as errors)
  ├── src/
  │   ├── Ledger.Core/
  │   │   ├── Ledger.Core.csproj
  │   │   ├── Transaction.cs
  │   │   ├── CsvLoader.cs
  │   │   └── Reports.cs
  │   └── Ledger.Cli/
  │       ├── Ledger.Cli.csproj
  │       └── Program.cs
  └── tests/
      └── Ledger.Core.Tests/
          ├── Ledger.Core.Tests.csproj
          ├── CsvLoaderTests.cs
          └── ReportsTests.cs
  ```
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet test` reports **at least 15** passing tests covering both `CsvLoader` and `Reports`.
- [ ] `dotnet run --project src/Ledger.Cli -- summary --file <path>` works against the example CSV included under `samples/`.
- [ ] All three commands print sensible, deterministic output. (See "expected output" below.)
- [ ] Malformed CSV lines do not crash the program. They produce a single warning line on `stderr` like:
  ```
  warning: skipping line 7: amount 'twelve' is not a number
  ```
- [ ] The `Transaction` type is a `record`. The CSV loader returns an `IReadOnlyList<Transaction>`. There is no mutable state anywhere in `Ledger.Core`.
- [ ] **Zero `!` (null-forgiving) operators** in the source.
- [ ] `dotnet publish src/Ledger.Cli -c Release -o out` produces a runnable artifact you can execute as `dotnet out/Ledger.Cli.dll summary --file samples/sample.csv`.
- [ ] Your `README.md` includes:
  - One paragraph describing the project.
  - The exact commands to set it up and run each subcommand from a fresh clone.
  - The example CSV and the expected output for each subcommand.
  - A "Things I learned" section with at least 3 specific items.

---

## Suggested order of operations

You'll find it easier if you build incrementally rather than trying to write the whole thing at once.

### Phase 1 — Bare skeleton (~1h)

1. `mkdir Ledger && cd Ledger`
2. `dotnet new sln -n Ledger`
3. `dotnet new gitignore` and `git init`.
4. Scaffold the three projects:
   ```bash
   dotnet new classlib -n Ledger.Core    -o src/Ledger.Core
   dotnet new console  -n Ledger.Cli     -o src/Ledger.Cli
   dotnet new xunit    -n Ledger.Core.Tests -o tests/Ledger.Core.Tests
   ```
5. Wire references:
   ```bash
   dotnet sln add src/Ledger.Core/Ledger.Core.csproj
   dotnet sln add src/Ledger.Cli/Ledger.Cli.csproj
   dotnet sln add tests/Ledger.Core.Tests/Ledger.Core.Tests.csproj
   dotnet add src/Ledger.Cli/Ledger.Cli.csproj reference src/Ledger.Core/Ledger.Core.csproj
   dotnet add tests/Ledger.Core.Tests/Ledger.Core.Tests.csproj reference src/Ledger.Core/Ledger.Core.csproj
   ```
6. Add a `Directory.Build.props` at the root:
   ```xml
   <Project>
     <PropertyGroup>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <LangVersion>latest</LangVersion>
     </PropertyGroup>
   </Project>
   ```
7. First commit: `Initial solution skeleton`.

### Phase 2 — The domain types (~1h)

Inside `src/Ledger.Core/Transaction.cs`:

```csharp
namespace Ledger.Core;

public enum TransactionKind { Credit, Debit, Zero }

public record Transaction(DateOnly Date, decimal Amount, string Memo, string Category)
{
    public TransactionKind Kind => Amount switch
    {
        > 0m => TransactionKind.Credit,
        < 0m => TransactionKind.Debit,
        _    => TransactionKind.Zero
    };
}
```

Commit: `Transaction record`.

### Phase 3 — The CSV loader (~2h)

In `src/Ledger.Core/CsvLoader.cs`, define a static class with one method:

```csharp
public static (IReadOnlyList<Transaction> Loaded, IReadOnlyList<string> Warnings) Load(string path);
```

Implementation rules:

- Read the file line by line (`File.ReadLines` or `StreamReader`).
- Treat the first line as a header — validate it has `date,amount,memo,category` and skip it.
- For each subsequent line, parse with `string.Split(',')` (we only handle the simple shape this week).
- Parse `date` with `DateOnly.TryParseExact("yyyy-MM-dd", …)`. Use `CultureInfo.InvariantCulture`.
- Parse `amount` with `decimal.TryParse(…, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt)`.
- On parse failure, **do not throw**. Add a string to the warnings list ("skipping line N: ..."), continue.
- On a totally absent file: throw `FileNotFoundException` (let the caller handle).

Write tests against a series of in-memory CSV strings. Use `Path.GetTempFileName()` if you want a real file path, or refactor the loader to accept a `TextReader` for easier testing — that refactor is worth doing.

Commit: `CSV loader + tests`.

### Phase 4 — Reports (~1.5h)

In `src/Ledger.Core/Reports.cs`:

```csharp
public static class Reports
{
    public static decimal Net(IEnumerable<Transaction> transactions);
    public static IReadOnlyDictionary<DateOnly, decimal> DailyTotals(IEnumerable<Transaction> transactions);
    public static IReadOnlyDictionary<string, decimal> CategoryTotals(IEnumerable<Transaction> transactions);
    public static IReadOnlyList<Transaction> InRange(IEnumerable<Transaction> transactions, DateOnly from, DateOnly to);
    public static IReadOnlyList<Transaction> TopByKind(IEnumerable<Transaction> transactions, TransactionKind kind, int count);
}
```

Implement each with a single LINQ pipeline. Test each report against a fixed `Transaction[]` defined once at the top of `ReportsTests.cs`.

Commit: `Reports + tests`.

### Phase 5 — The CLI surface (~1.5h)

Install `System.CommandLine`:

```bash
dotnet add src/Ledger.Cli package System.CommandLine --prerelease
```

In `Program.cs`, build a root command with three subcommands. The skeleton:

```csharp
using System.CommandLine;
using Ledger.Core;

var fileOption = new Option<FileInfo>("--file") { Description = "Path to the CSV.", Required = true };

var summary = new Command("summary", "Print a net total, daily totals, and category totals.");
summary.Options.Add(fileOption);
summary.SetAction(parse => RunSummary(parse.GetValue(fileOption)!));

// ... similar for list and top ...

var root = new RootCommand("Ledger CLI — read a CSV, summarize transactions.");
root.Subcommands.Add(summary);

return await root.Parse(args).InvokeAsync();
```

The `System.CommandLine` API has shifted in pre-release. Read the current docs to confirm the exact shape: <https://learn.microsoft.com/en-us/dotnet/standard/commandline/>. The naming above matches the current pre-release at the time of writing — adjust to whatever is shipping when you do this.

For each subcommand:

- Call `CsvLoader.Load`.
- Print warnings to `stderr` (one per malformed line).
- Compute the requested report and write to `stdout`.

Commit: `CLI surface with three subcommands`.

### Phase 6 — Sample data + smoke (~0.5h)

Create `samples/sample.csv` with 20-ish transactions across at least three days and three categories. Confirm:

```bash
dotnet run --project src/Ledger.Cli -- summary --file samples/sample.csv
dotnet run --project src/Ledger.Cli -- list --file samples/sample.csv --from 2026-05-13 --to 2026-05-15
dotnet run --project src/Ledger.Cli -- top --file samples/sample.csv --kind debit --count 3
```

Each produces deterministic output. Paste expected outputs into the README under "Examples." Commit: `Sample data + README examples`.

### Phase 7 — Polish (~0.5h)

- Run `dotnet format` and commit any changes.
- Run `dotnet publish src/Ledger.Cli -c Release -o out` and confirm `dotnet out/Ledger.Cli.dll summary --file samples/sample.csv` works.
- Push to GitHub.
- Add a one-line CI: `.github/workflows/ci.yml` that runs `dotnet build` + `dotnet test` on push. (Optional for this week; required from Week 4 onward.)

---

## Example expected output

For the sample CSV at the top of this file:

```
$ ledger summary --file transactions.csv
Net total:        187.51
Days observed:    3
Categories:       3

Daily totals:
  2026-05-13:        32.50
  2026-05-14:         4.99
  2026-05-15:       150.00

Category totals:
  food:              27.50
  subscriptions:      9.99
  income:           150.00
```

```
$ ledger top --file transactions.csv --kind credit --count 2
1.  +150.00  2026-05-15  Freelance income           [income]
2.   +22.50  2026-05-13  Lunch                      [food]
```

Adjust formatting to taste, but it should be deterministic and aligned.

---

## Rubric

| Criterion | Weight | What "great" looks like |
|----------|-------:|-------------------------|
| Builds and runs | 25% | `dotnet build`, `dotnet test`, `dotnet run` all clean on a fresh clone |
| Code clarity | 20% | Files are short, each has one job, no dead code, no commented-out blocks |
| Nullability hygiene | 10% | Zero `!` operators; warnings treated as errors and there are none |
| Test coverage | 20% | At least 15 tests, mix of `[Fact]` and `[Theory]`, both happy and error paths |
| Reports correctness | 15% | All four report methods produce the right numbers on the sample |
| README quality | 10% | Someone unfamiliar can clone and run in <5 minutes |

---

## Stretch (optional)

- Add a `--format json` option that emits machine-readable output instead of the table. Define a small `record` for the JSON shape and serialize with `System.Text.Json`.
- Add a `--from-stdin` flag that reads CSV from `Console.In` instead of a file path.
- Add a `ledger init` subcommand that writes a sample CSV to a given path so a new user can try the tool with zero setup.
- Profile `CsvLoader.Load` against a 1-million-line file. (We give you `Span<T>` and `BenchmarkDotNet` in Week 12 — for now, just see how the naive `string.Split` version holds up.)
- Add a Native AOT publish: `dotnet publish -c Release -r osx-arm64 -p:PublishAot=true`. See what shrinks. See what breaks.

---

## What this prepares you for

- **Week 2** dives deep into LINQ and collections. The reports you wrote here will be the warm-up for richer LINQ pipelines.
- **Week 3** introduces async. The CSV loader you wrote synchronously will become a streaming, cancellable, async pipeline.
- **Week 4** adds dependency injection. You'll refactor `CsvLoader` and `Reports` into interfaces and inject them through MS.DI.
- The mini-project's solution layout — `Ledger.sln`, `src/`, `tests/`, `Directory.Build.props` — is the **same layout** the capstone uses. By Week 15 your reflexes will be: "new project → that tree."

---

## Resources

- *.NET CLI overview*: <https://learn.microsoft.com/en-us/dotnet/core/tools/>
- *`System.CommandLine` docs*: <https://learn.microsoft.com/en-us/dotnet/standard/commandline/>
- *Records (C# reference)*: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record>
- *Pattern matching*: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns>
- *xUnit documentation*: <https://xunit.net/docs/getting-started/netcore/cmdline>

---

## Submission

When done:

1. Push your repo to GitHub with a public URL.
2. Make sure `README.md` includes the setup commands and the example output for each subcommand.
3. Make sure `dotnet build`, `dotnet test`, and at least one `dotnet run --project src/Ledger.Cli -- summary --file samples/sample.csv` invocation are green on a freshly cloned copy.
4. Post the repo URL in your cohort tracker. You did real work; show it.
