# Week 1 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The C# language specification is published openly. Open-source repos are public on GitHub. No paywalled books are linked.

## Required reading (work it into your week)

- **What is .NET?** — the canonical Microsoft Learn overview:
  <https://learn.microsoft.com/en-us/dotnet/core/introduction>
- **A tour of C#** — the language tour kept current per release:
  <https://learn.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/>
- **What's new in C# 13** — the changes you should recognize on sight:
  <https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-13>
- **`dotnet` CLI overview** — every command you will use this week:
  <https://learn.microsoft.com/en-us/dotnet/core/tools/>
- **Nullable reference types**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references>

## The specification (skim, don't memorize)

The C# language specification is the normative reference, published under the Microsoft Open Specification Promise. You will not read it cover to cover, but the first time someone in a code review writes "per §11.2.7, an `is` pattern…" you will know what they mean.

- **C# language specification (current draft)**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/specifications>
- **C# 13 feature specifications** (per-feature design notes):
  <https://github.com/dotnet/csharplang/tree/main/proposals/csharp-13.0>

## Official .NET docs

- **.NET 9 release notes**: <https://github.com/dotnet/core/tree/main/release-notes/9.0>
- **.NET SDK and runtime download**: <https://dotnet.microsoft.com/en-us/download/dotnet/9.0>
- **`dotnet new` template catalogue**: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates>
- **NuGet — the package manager**: <https://learn.microsoft.com/en-us/nuget/what-is-nuget>
- **Central Package Management**:
  <https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management>

## Editors

- **VS Code + C# Dev Kit** (primary): <https://code.visualstudio.com/docs/csharp/get-started>
- **JetBrains Rider Community** (secondary, also free): <https://www.jetbrains.com/rider/>
- **Visual Studio Community** (Windows only; acknowledged, not required): <https://visualstudio.microsoft.com/vs/community/>

## Libraries we touch this week

- **xUnit** — the default test framework for .NET in 2026:
  <https://xunit.net/>
- **System.CommandLine** — the modern CLI parser, used by `dotnet` itself:
  <https://learn.microsoft.com/en-us/dotnet/standard/commandline/>

## Free books (chapter-level, not whole books)

- **"Fundamentals of .NET"** — free Microsoft Learn module set:
  <https://learn.microsoft.com/en-us/training/paths/dotnet-fundamentals/>
- **"C# for beginners"** video series (free, Microsoft Learn TV — short episodes, watch only what you need):
  <https://learn.microsoft.com/en-us/shows/csharp-for-beginners/>

## Open courseware

- **freeCodeCamp — C# certifications via Microsoft Learn** (free):
  <https://www.freecodecamp.org/learn/foundational-c-sharp-with-microsoft/>

## Tools you'll use this week

- **`dotnet` CLI** — installed with the .NET SDK. Verify with `dotnet --info`.
- **Git** — version control. `git --version` to confirm.
- **`curl`** — preinstalled on macOS and Linux; available on Windows.
- **`watch` / `entr`** (optional) — for re-running tests on save; `dotnet watch test` does it natively.

## Videos (free, no signup)

- **"What is .NET?"** — official, 8 min: <https://www.youtube.com/watch?v=eIHKZfgddLM>
  *(If the link rots, search "What is .NET Microsoft" on YouTube; the official channel reposts.)*
- **".NET Conf"** — annual conference, every talk is on the Microsoft Developer YouTube channel:
  <https://www.youtube.com/@dotnet>
- **Nick Chapsas** — community channel that consistently explains C# features clearly:
  <https://www.youtube.com/@nickchapsas>

## Open-source projects to read this week

You learn more from one hour reading well-written C# than from three hours of tutorials. Pick one and just scroll through:

- **`dotnet/runtime`** — the runtime, the BCL, garbage collector, JIT:
  <https://github.com/dotnet/runtime>
- **`dotnet/aspnetcore`** — the web framework, all C#, all readable:
  <https://github.com/dotnet/aspnetcore>
- **`dotnet/efcore`** — Entity Framework Core; great LINQ source:
  <https://github.com/dotnet/efcore>
- **`xunit/xunit`** — the test framework you'll use all year:
  <https://github.com/xunit/xunit>

## Glossary cheat sheet

Keep this open in a tab.

| Term | Plain English |
|------|---------------|
| **C# 13** | The language version. Released with .NET 9, late 2024. |
| **.NET 9** | The runtime and SDK. The thing `dotnet --info` reports. |
| **CLR** | Common Language Runtime — the part of .NET that runs IL code. |
| **BCL** | Base Class Library — the standard library (`System.*` namespaces). |
| **JIT** | Just-In-Time compiler — turns IL into machine code at run time. |
| **AOT** | Ahead-Of-Time — Native AOT publishes to a native binary; no JIT at run time. |
| **NuGet** | The package manager. Like `pip` for Python, `npm` for Node. |
| **`.csproj`** | XML file describing a single project (its dependencies, target framework). |
| **`.sln`** | "Solution" — a container grouping multiple projects. |
| **SDK** | The dev-time toolkit: `dotnet` CLI, compilers, MSBuild. |
| **Runtime** | The thing your compiled code runs on. Ships separately from the SDK. |
| **TFM** | Target Framework Moniker — `net9.0`, `net8.0`, etc. The `<TargetFramework>` value in your `.csproj`. |
| **xUnit** | The default unit-test framework for .NET in 2026. |

---

*If a link 404s, please open an issue so we can replace it.*
