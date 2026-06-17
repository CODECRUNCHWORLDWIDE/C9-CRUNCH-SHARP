# Lecture 1 — The .NET Stack and the Toolchain

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can describe what .NET 9, C# 13, and ASP.NET Core are without confusing them, scaffold a project from a blank folder with the `dotnet` CLI, and explain what every file in a stock solution layout does.

If you only remember one thing from this lecture, remember this:

> **.NET is a runtime. C# is a language. ASP.NET Core is a framework.** They ship together, they are versioned together, and they are easily confused — but they are three different things. Beginners conflate them; we will not help them.

---

## 1. The three things people call ".NET"

Walk into a `.NET` shop and ask "what version of .NET are you on?" You will get three different answers from three different engineers, and all three will be right. They are talking about different layers of the stack.

| Layer | Example | What it is | Released |
|------|---------|-----------|----------|
| Language | **C# 13** | The syntax and semantics you type. The compiler reads `.cs` files. | Nov 2024 |
| Runtime/SDK | **.NET 9** | The thing your compiled code runs on. The CLR, the JIT, the garbage collector, the BCL. | Nov 2024 |
| Framework | **ASP.NET Core 9** | A web framework that runs on .NET. One of several frameworks. | Nov 2024 |

**Other languages on the same runtime:** F# (functional), VB.NET (legacy), PowerShell (scripting), IronPython (mostly historical). They all compile to the same intermediate language (IL) and run on the same CLR.

**Other frameworks on the same runtime:** ASP.NET Core (web), MAUI (cross-platform UI), Blazor (web UI in C#), WPF / WinForms (Windows desktop), Avalonia (cross-platform desktop, open-source), Unity's runtime (game engine — a special case; see Week 12).

When the official Microsoft docs say "modern .NET" they mean .NET 5 and onward — the unified, cross-platform line that replaced both .NET Framework (Windows-only, frozen at 4.8.1) and .NET Core (the original cross-platform fork). In 2026, **".NET" with no number means .NET 9**.

> **Why "Core" disappeared.** From 2016 to 2020 the cross-platform line was called ".NET Core" to distinguish it from the legacy ".NET Framework." When .NET 5 shipped in 2020 the team dropped the suffix because the unification was complete. The framework name "ASP.NET Core" kept the suffix to avoid colliding with the legacy "ASP.NET" name. That's it; there is no deeper reason. You will still see "Core" in ASP.NET Core, EF Core, and some library names.

---

## 2. The runtime, briefly

You will spend Week 3 on async and Week 12 on performance. Here is the bare minimum to know in Week 1.

A C# program is compiled by the `csc` compiler (invoked by `dotnet build`) into **Intermediate Language (IL)** — a stack-machine bytecode — and packaged into a `.dll`. When you run that `.dll`, the **CLR (Common Language Runtime)** loads it, the **JIT (Just-In-Time)** compiler translates IL into native machine code lazily as methods are called, and the **garbage collector** manages heap allocations.

```
your.cs ──[csc]──> your.dll  (IL)
                       │
                       ▼
              dotnet your.dll
                       │
                       ▼
          ┌───────────────────────────┐
          │   CLR — loads the dll     │
          │   JIT — compiles methods  │
          │   GC  — manages heap      │
          │   BCL — System.* APIs     │
          └───────────────────────────┘
                       │
                       ▼
                Native machine code
```

That is the default. There is one alternative worth knowing about up front: **Native AOT**, where you publish your app as a single native binary with no JIT at run time, no IL, and a much smaller startup cost. We cover Native AOT in Week 12; you do not need it in Week 1.

The runtime is **open source**, MIT-licensed, and developed in the open at <https://github.com/dotnet/runtime>. The garbage collector, the JIT, the BCL — all of it. This is not the .NET of 2010.

---

## 3. What "managed" means

You will hear the phrase "managed code" everywhere. In .NET it means three concrete things:

1. **Memory is garbage-collected.** You do not call `free()`. Reference types live on a managed heap; the GC reclaims unreachable objects across generations.
2. **The runtime enforces type safety.** You cannot reinterpret a `string`'s bytes as an `int` without explicit, narrowly scoped escape hatches (`unsafe`, `Span<byte>`, `MemoryMarshal`).
3. **Cross-language interop is verifiable.** The runtime can verify IL is well-formed before running it.

Compare to **unmanaged code** (C, C++, Rust) where you own the lifetime of every byte. C# does have unmanaged escape hatches — `unsafe` blocks, pointers, `stackalloc`, `Span<T>` — and they are first-class in modern .NET. But the default is managed, and you will spend almost all of Week 1 in managed code.

---

## 4. Installing the SDK

You install **the SDK**, not "the runtime." The SDK includes the runtime; the runtime alone does not include the compiler.

Download .NET 9 SDK from <https://dotnet.microsoft.com/en-us/download/dotnet/9.0>. There are installers for macOS (`.pkg`), Windows (`.exe`), Linux (`apt`, `dnf`, `tar.gz`).

Verify:

```bash
dotnet --info
```

You should see something like:

```
.NET SDK:
 Version:           9.0.x
 Commit:            ...

Runtime Environment:
 OS Name:           Darwin
 OS Version:        25.x
 OS Platform:       Darwin
 RID:               osx-arm64
 Base Path:         /usr/local/share/dotnet/sdk/9.0.x/

.NET workloads installed:
 ...

Host:
  Version:      9.0.x
  Architecture: arm64

.NET SDKs installed:
  9.0.x [/usr/local/share/dotnet/sdk]
```

If `dotnet --info` does not print 9.0.x as an installed SDK, stop and fix that before going further. Every example this week assumes `dotnet --info` shows .NET 9.

---

## 5. The `dotnet` CLI — your daily driver

The `dotnet` CLI is the one tool you will use every day. Everything you can do in Visual Studio you can do in the terminal with `dotnet`. Below is the working set for Week 1.

```bash
# Create a new project from a template.
dotnet new <template> -n <name>

# Build.
dotnet build

# Run.
dotnet run

# Test.
dotnet test

# Publish (produces a deployable artifact).
dotnet publish -c Release

# Add a NuGet package.
dotnet add package <PackageName>

# Format code (uses Roslyn's formatter; respects .editorconfig).
dotnet format

# List installed templates.
dotnet new list
```

Run `dotnet new list` once and skim. The templates you will use in Week 1:

- `console` — a console application
- `classlib` — a class library (a `.dll` consumed by other projects)
- `xunit` — an xUnit test project
- `sln` — a solution file
- `gitignore` — a `.gitignore` tuned for .NET projects
- `editorconfig` — an `.editorconfig` tuned for .NET

---

## 6. Scaffold a real solution from scratch

Let's make one now. This is the canonical layout you will use for every mini-project in C9.

```bash
mkdir Ledger && cd Ledger

# Create the solution file at the root.
dotnet new sln -n Ledger

# Create the source project under src/.
dotnet new console -n Ledger.Cli -o src/Ledger.Cli

# Create the test project under tests/.
dotnet new xunit -n Ledger.Cli.Tests -o tests/Ledger.Cli.Tests

# Add both projects to the solution.
dotnet sln add src/Ledger.Cli/Ledger.Cli.csproj
dotnet sln add tests/Ledger.Cli.Tests/Ledger.Cli.Tests.csproj

# Wire the test project to reference the source project.
dotnet add tests/Ledger.Cli.Tests/Ledger.Cli.Tests.csproj reference src/Ledger.Cli/Ledger.Cli.csproj

# Drop in a tuned .gitignore at the root.
dotnet new gitignore
```

You now have:

```
Ledger/
├── Ledger.sln
├── .gitignore
├── src/
│   └── Ledger.Cli/
│       ├── Ledger.Cli.csproj
│       └── Program.cs
└── tests/
    └── Ledger.Cli.Tests/
        ├── Ledger.Cli.Tests.csproj
        └── UnitTest1.cs
```

That tree is the "Project Structure" pattern from the C9 brand. Notice the `.sln` at the root — do not omit it. Newcomers sometimes do, then they're confused why `dotnet build` from the root doesn't build everything. The solution is the build coordinator.

Build and run:

```bash
dotnet build
dotnet run --project src/Ledger.Cli
```

You should see:

```
Hello, World!
Build succeeded · 0 warnings · 0 errors · 412 ms
```

(The exact "Build succeeded" line is the C9 convention; the real `dotnet build` output is wordier. Get used to looking for that 0/0 in the actual output.)

Run the tests:

```bash
dotnet test
```

You should see one passing dummy test from the xUnit template.

---

## 7. Reading a `.csproj`

Open `src/Ledger.Cli/Ledger.Cli.csproj`. It will look something like:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

Every element matters; nothing here is decoration.

- **`Sdk="Microsoft.NET.Sdk"`** — picks the project SDK. There are alternatives (`Microsoft.NET.Sdk.Web` for ASP.NET, `Microsoft.NET.Sdk.Razor`) but `Microsoft.NET.Sdk` is the base.
- **`<OutputType>Exe</OutputType>`** — produce an executable, not a library. Omit this and you get a `.dll` that other projects reference.
- **`<TargetFramework>net9.0</TargetFramework>`** — the **TFM**, target framework moniker. `net9.0` means ".NET 9, any OS." We will see `net9.0-android`, `net9.0-ios`, etc. in Week 10 (MAUI).
- **`<ImplicitUsings>enable</ImplicitUsings>`** — automatically `using` a curated list of common namespaces (`System`, `System.Collections.Generic`, `System.Linq`, etc.) in every file. We cover this in Lecture 2.
- **`<Nullable>enable</Nullable>`** — enable nullable reference types project-wide. We cover this in Lecture 2 too.

To add a dependency:

```bash
dotnet add src/Ledger.Cli package System.CommandLine --prerelease
```

This rewrites the `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="System.CommandLine" Version="2.0.0-..." />
</ItemGroup>
```

That's it. NuGet is just XML and a restore step.

---

## 8. NuGet and Central Package Management

A small project lists its package versions inline in each `.csproj`. A real solution with five or fifteen projects ends up duplicating those version numbers across every file, and that gets out of hand.

**Central Package Management (CPM)** moves all version numbers to a single `Directory.Packages.props` at the solution root. Available since .NET 6. We will adopt it in Week 4 when our solutions are big enough to need it. Microsoft Learn covers the details: <https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management>.

The two patterns are:

```xml
<!-- Direct: version in each .csproj -->
<PackageReference Include="System.CommandLine" Version="2.0.0-..." />

<!-- CPM: version in Directory.Packages.props, name in each .csproj -->
<PackageReference Include="System.CommandLine" />
```

For Week 1 we use the direct pattern. Both work; CPM scales better.

---

## 9. The editor landscape

C9 is **open-source-first by design** (see the charter). You have three reasonable editors; you do **not** need a Visual Studio license to complete any week.

### VS Code + C# Dev Kit (primary)

- Free. Cross-platform. Same UI on macOS, Linux, Windows.
- Install VS Code: <https://code.visualstudio.com/>.
- Install the **C# Dev Kit** extension (Microsoft, free for individual learners and OSS, license terms inside): <https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit>.
- Gives you IntelliSense, debugging, test explorer, solution explorer.

### JetBrains Rider Community (secondary)

- Free as of October 2024 for non-commercial use.
- Cross-platform.
- Heavier than VS Code but with deeper refactoring tools.
- Download: <https://www.jetbrains.com/rider/>.

### Visual Studio Community (Windows only, acknowledged)

- Free for individuals, OSS, and small teams under specific terms.
- Only on Windows. There is no "Visual Studio for Mac" anymore — Microsoft retired it in 2024.
- Still the most polished Windows desktop IDE for .NET.
- Download: <https://visualstudio.microsoft.com/vs/community/>.

The C9 curriculum **does not depend on any IDE feature**. Every project compiles, tests, and ships from the `dotnet` CLI. If you can use only the terminal and `vim`/`emacs`, you can complete this course.

---

## 10. The build graph at a glance

When you run `dotnet build` at the solution root:

1. `dotnet` invokes **MSBuild**, the underlying build engine.
2. MSBuild reads the `.sln`, finds every `.csproj`, and orders them by dependency.
3. For each project: NuGet restore (download missing packages), Roslyn compile (C# → IL), copy outputs into `bin/<Configuration>/<TFM>/`.
4. Build artifacts land in `bin/` (the binaries) and `obj/` (intermediate files — caches, generated source, the dependency graph).

```
src/Ledger.Cli/
├── Ledger.Cli.csproj
├── Program.cs
├── bin/
│   └── Debug/
│       └── net9.0/
│           ├── Ledger.Cli.dll
│           ├── Ledger.Cli.pdb    (debug symbols)
│           └── Ledger.Cli.deps.json
└── obj/
    └── Debug/
        └── net9.0/
            └── ...               (intermediate files)
```

Add `bin/` and `obj/` to your `.gitignore` (the `dotnet new gitignore` template already does). Never commit them.

---

## 11. Configurations: Debug vs Release

By default `dotnet build` produces a **Debug** build: optimizations off, debug symbols on, full inlining disabled. For shipping, you want **Release**:

```bash
dotnet build -c Release
dotnet publish -c Release -o ./out
```

The `-c` flag is `--configuration`. There are only two stock configurations; you can define more in `.csproj` if you need to, but you almost never do. For Week 1 you can ignore the distinction; for the mini-project you will use Release once at the end.

---

## 12. `dotnet watch` — re-run on save

The single most useful command you will not learn in tutorials:

```bash
dotnet watch run --project src/Ledger.Cli
dotnet watch test
```

This rebuilds and re-runs (or re-tests) whenever a source file changes. It is built into the SDK; no extra install. Once you start using `dotnet watch test` while writing code against tests, you will not go back.

---

## 13. A word on Visual Studio shortcuts you will not see here

Some C# resources online assume you are inside Visual Studio. They will tell you to "press F5" or "right-click and Add Reference." We are not doing that. Every action in C9 has a `dotnet` CLI equivalent:

| Visual Studio action | CLI equivalent |
|---------------------|----------------|
| F5 (Run with debugger) | `dotnet run` (no debugger) or attach VS Code's debugger |
| Ctrl+Shift+B (Build) | `dotnet build` |
| Test Explorer → Run All | `dotnet test` |
| Right-click → Add Reference | `dotnet add reference` |
| Right-click → Add Package | `dotnet add package` |
| New Project Wizard | `dotnet new <template> -n <name> -o <path>` |

If you prefer the GUI, use it — we are not anti-IDE. We are pro-portability. The CLI commands will work on every developer's machine on every OS forever.

---

## 14. A glance at what's not in Week 1

- **ASP.NET Core.** No web yet. That is Week 5–8.
- **MAUI.** No mobile yet. That is Week 10.
- **Unity.** Not until Week 12, and even then only briefly.
- **EF Core.** No database yet. That is Week 6.

Week 1 is the language and the runtime. We will not build "a real app" — instead, by Friday, you will be able to *scaffold* one without referring to a tutorial.

---

## 15. Recap

You should now be able to:

- State what C# 13, .NET 9, and ASP.NET Core each are without conflating them.
- Run `dotnet --info` and read its output.
- Create a solution with one source project and one test project from a blank folder.
- Read a `.csproj` line by line.
- Add a NuGet package from the command line.
- Use `dotnet build`, `dotnet run`, `dotnet test`, and `dotnet watch test`.

Next up: the C# language itself. Continue to [Lecture 2 — Modern C# Essentials](./02-modern-csharp-essentials.md).

---

## References

- *.NET introduction* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/core/introduction>
- *.NET CLI overview* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/core/tools/>
- *Project SDK overview* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview>
- *NuGet — what is NuGet*: <https://learn.microsoft.com/en-us/nuget/what-is-nuget>
- *Central Package Management*: <https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management>
- *.NET runtime source*: <https://github.com/dotnet/runtime>
