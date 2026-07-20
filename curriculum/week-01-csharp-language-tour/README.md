# Week 1 — The C# Language Tour

Welcome to **C9 · Crunch Sharp**. Week 1 is the language tour. By Friday you should be able to scaffold a .NET 9 project from a blank folder, write a record with pattern matching, enable nullable reference types, run a `dotnet test`, and ship a small command-line tool — all from the terminal, with no Visual Studio license required.

We assume you already know one programming language well. The C1 graduate is the target: comfortable with functions, classes, exceptions, and collections in Python. If that's you, this week will feel less like "learn programming" and more like "learn the C# dialect and the .NET runtime." We move fast.

The first thing to internalize is that **"C#" and ".NET" are not the same thing**. C# is the language. .NET is the runtime, the SDK, and the standard library. ASP.NET Core is a framework that runs on .NET. You can use other languages on .NET (F#, VB), and historically you could run C# on other runtimes (Mono, Unity's). In 2026, the default assumption is **C# 13 on .NET 9**, and that's what every example this week uses.

## Learning objectives

By the end of this week, you will be able to:

- **Distinguish** the C# language, the .NET runtime/SDK, and the ASP.NET Core framework — three things people regularly conflate.
- **Scaffold** a .NET project from scratch with `dotnet new`, build it with `dotnet build`, run it with `dotnet run`, and test it with `dotnet test`.
- **Navigate** the standard C# / .NET solution layout: the `.sln`, the `.csproj`, `src/`, `tests/`, and where artifacts land.
- **Write** modern idiomatic C# 13 — file-scoped namespaces, global usings, primary constructors, collection expressions.
- **Model** small domains with `record` types and `with`-expressions for non-destructive mutation.
- **Use** pattern matching (switch expressions, property patterns, list patterns) as a default — not a curiosity.
- **Enable** nullable reference types (`#nullable enable`) and read a nullable warning correctly without reaching for `!`.
- **Read and write** small LINQ pipelines over `IEnumerable<T>` (`Where`, `Select`, `Aggregate`).
- **Run** an xUnit test from the terminal and read its output.
- **Package** a small typed CLI using `System.CommandLine`.

## Prerequisites

This week assumes you have completed **C1 · Code Crunch Convos** weeks 1–11, or have equivalent Python (or other-language) fluency. Specifically:

- Comfortable in a terminal — you can `cd`, run `python` or `node`, install a package.
- You've written and tested a small project end-to-end at least once.
- You understand functions, classes, generics-or-equivalent, and exceptions.
- You can read and write basic Git (`clone`, `add`, `commit`, `push`).

You do **not** need any prior C# exposure. We start at the type system. If you have learned older C# (.NET Framework 4.x, pre-records), you will need to unlearn a couple of habits; we will flag them as we go.

## Topics covered

- The three things people call ".NET": the language (C# 13), the runtime/SDK (.NET 9), and the framework ecosystem on top (ASP.NET Core, MAUI, EF Core).
- The `dotnet` CLI: `new`, `build`, `run`, `test`, `publish`, `format`, `add package`.
- Solution and project layout: `.sln`, `.csproj`, `src/`, `tests/`, `bin/`, `obj/`.
- NuGet packages, `PackageReference`, and Central Package Management (CPM) with `Directory.Packages.props`.
- The cross-platform editor landscape: VS Code + C# Dev Kit, JetBrains Rider Community, Visual Studio Community.
- Value vs reference types; `struct` vs `class`; where each lives in memory.
- `record` and `record struct` — positional syntax, structural equality, `with`-expressions.
- Pattern matching: `is`, switch expressions, property patterns, list patterns (available since .NET 7).
- Nullable reference types: `#nullable enable`, `?`, `!`, `??`, `??=`.
- Primary constructors on classes and structs (available since C# 12 / .NET 8).
- File-scoped namespaces, `global using`, implicit usings, top-level statements.
- `async`/`await` — the bare minimum to read it; the deep dive is Week 3.
- LINQ basics: `Where`, `Select`, `Aggregate`, `ToList`, deferred execution at a glance.
- `System.CommandLine` (the modern CLI parser the .NET team uses for `dotnet` itself).
- xUnit: `[Fact]`, `[Theory]`, assertions, the `dotnet test` runner.

## Weekly schedule

The schedule below adds up to approximately **36 hours**. Treat it as a target.

| Day       | Focus                                            | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | .NET stack, toolchain, project layout            |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | Records, pattern matching, with-expressions      |    2h    |    2h     |     1h     |    0.5h   |   1h     |     0h       |    0h      |     6.5h    |
| Wednesday | Nullable refs, primary constructors, file-scoped |    1h    |    2h     |     1h     |    0.5h   |   1h     |     0h       |    0.5h    |     6h      |
| Thursday  | LINQ, async, xUnit                               |    1h    |    1h     |     0h     |    0.5h   |   1h     |     2h       |    0.5h    |     6h      |
| Friday    | System.CommandLine; mini-project work            |    0h    |    1h     |     0h     |    0.5h   |   1h     |     3h       |    0.5h    |     6h      |
| Saturday  | Mini-project deep work                           |    0h    |    0h     |     0h     |    0h     |   1h     |     3h       |    0h      |     4h      |
| Sunday    | Quiz, review, polish                             |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                  | **6h**   | **7.5h**  | **2h**     | **3.5h**  | **6h**   | **8.5h**     | **2h**     | **35.5h**   |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | Curated Microsoft Learn, language spec, and open-source links |
| [lecture-notes/01-the-dotnet-stack-and-toolchain.md](./lecture-notes/01-the-dotnet-stack-and-toolchain.md) | What .NET 9, C# 13, and ASP.NET Core actually are; the `dotnet` CLI; project layout; NuGet; editors |
| [lecture-notes/02-modern-csharp-essentials.md](./lecture-notes/02-modern-csharp-essentials.md) | Types, records, pattern matching, nullable refs, primary constructors, LINQ, async |
| [exercises/README.md](./exercises/README.md) | Index of short coding exercises |
| [exercises/exercise-01-hello-dotnet.md](./exercises/exercise-01-hello-dotnet.md) | `dotnet new`, build, run, debug, test |
| [exercises/exercise-02-records-pattern-matching.cs](./exercises/exercise-02-records-pattern-matching.cs) | Fill-in-the-TODO record and pattern-matching drill |
| [exercises/exercise-03-nullable-refs.cs](./exercises/exercise-03-nullable-refs.cs) | Fix a file full of nullability warnings without reaching for `!` |
| [challenges/README.md](./challenges/README.md) | Index of weekly challenges |
| [challenges/challenge-01-fluent-builder.md](./challenges/challenge-01-fluent-builder.md) | Build a fluent builder using records and `with` |
| [quiz.md](./quiz.md) | 10 multiple-choice questions |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for the "Ledger CLI" mini-project |

## The "build succeeded" promise

C9 uses a small recurring marker in every exercise that ends in working code:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

If your `dotnet build` doesn't print zero warnings and zero errors, you are not done. We treat warnings as bugs, especially nullable-reference warnings. The point of Week 1 is to make that line ordinary.

## Stretch goals

If you finish the regular work early and want to push further:

- Read the official **"What's new in C# 13"** page: <https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13>.
- Skim the **C# language specification** for §15 (classes) and §16 (structs): <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/specifications>.
- Browse **dotnet/runtime** on GitHub. Pick one open issue tagged `area-System.Collections` and read the discussion: <https://github.com/dotnet/runtime>.
- Watch Stephen Toub's annual perf-improvements post for the latest .NET release: <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>. (Long; skim section headings.)
- Write a short note for your future self comparing how Python and C# each handle "no value" — `None` vs `null` vs `Nullable<T>` vs nullable references.

## Up next

Continue to **Week 2 — Generics, Collections, and LINQ** once you have pushed the mini-project to your GitHub.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
