# Exercise 1 — Hello, .NET

**Goal:** Scaffold a real .NET solution from a blank folder, build it, run it, write a test, and see `0 warnings · 0 errors` from the terminal. No IDE wizards. No template repos. Just you and the `dotnet` CLI.

**Estimated time:** 35 minutes.

---

## Setup

You need the .NET 9 SDK installed. Verify:

```bash
dotnet --info
```

You should see `9.0.x` listed under "Host" and "SDKs installed." If you don't, install from <https://dotnet.microsoft.com/en-us/download/dotnet/9.0> and come back.

You also need Git. `git --version` should print a real version.

---

## Step 1 — Make a folder and a solution

```bash
mkdir HelloDotnet && cd HelloDotnet
dotnet new sln -n HelloDotnet
dotnet new gitignore
git init
```

You now have `HelloDotnet.sln`, `.gitignore`, and a fresh Git repo. Commit:

```bash
git add .
git commit -m "Initial solution"
```

---

## Step 2 — Add a console project

```bash
dotnet new console -n HelloDotnet.Cli -o src/HelloDotnet.Cli
dotnet sln add src/HelloDotnet.Cli/HelloDotnet.Cli.csproj
```

Open `src/HelloDotnet.Cli/Program.cs`. It contains a single line:

```csharp
Console.WriteLine("Hello, World!");
```

Replace it with:

```csharp
string name = args.Length > 0 ? args[0] : "stranger";
Console.WriteLine($"Hello, {name}! You are running on .NET {Environment.Version}.");
```

Build and run:

```bash
dotnet build
dotnet run --project src/HelloDotnet.Cli -- Ada
```

You should see:

```
Hello, Ada! You are running on .NET 9.0.x.
```

The `--` separates `dotnet run` arguments from the arguments passed to *your* program.

---

## Step 3 — Add a class library

```bash
dotnet new classlib -n HelloDotnet.Greetings -o src/HelloDotnet.Greetings
dotnet sln add src/HelloDotnet.Greetings/HelloDotnet.Greetings.csproj
dotnet add src/HelloDotnet.Cli/HelloDotnet.Cli.csproj reference src/HelloDotnet.Greetings/HelloDotnet.Greetings.csproj
```

Replace the contents of `src/HelloDotnet.Greetings/Class1.cs` with `src/HelloDotnet.Greetings/Greeter.cs` (rename the file too):

```csharp
namespace HelloDotnet.Greetings;

public static class Greeter
{
    public static string Greet(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? "Hello, stranger!"
            : $"Hello, {name}!";
}
```

Update `Program.cs`:

```csharp
using HelloDotnet.Greetings;

string name = args.Length > 0 ? args[0] : "";
Console.WriteLine(Greeter.Greet(name));
Console.WriteLine($"Running on .NET {Environment.Version}.");
```

Build:

```bash
dotnet build
```

Expect zero warnings, zero errors.

---

## Step 4 — Add an xUnit test project

```bash
dotnet new xunit -n HelloDotnet.Greetings.Tests -o tests/HelloDotnet.Greetings.Tests
dotnet sln add tests/HelloDotnet.Greetings.Tests/HelloDotnet.Greetings.Tests.csproj
dotnet add tests/HelloDotnet.Greetings.Tests/HelloDotnet.Greetings.Tests.csproj reference src/HelloDotnet.Greetings/HelloDotnet.Greetings.csproj
```

Rename the generated `UnitTest1.cs` to `GreeterTests.cs` and replace its contents:

```csharp
using HelloDotnet.Greetings;

namespace HelloDotnet.Greetings.Tests;

public class GreeterTests
{
    [Fact]
    public void GreetsByName()
    {
        Assert.Equal("Hello, Ada!", Greeter.Greet("Ada"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void GreetsStrangerWhenNameIsBlank(string blank)
    {
        Assert.Equal("Hello, stranger!", Greeter.Greet(blank));
    }
}
```

Run the tests:

```bash
dotnet test
```

You should see something like:

```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: ...
```

---

## Step 5 — Watch tests

Open a second terminal in the same folder. Run:

```bash
dotnet watch test --project tests/HelloDotnet.Greetings.Tests
```

In the first terminal, edit `Greeter.cs` and break the implementation deliberately — change `"Hello, stranger!"` to `"Hi, stranger."` and save. Watch the second terminal: a build and test cycle runs automatically, and you see a failure. Fix it; the next save brings the suite back to green.

Stop the watcher with `Ctrl+C` once you have seen the loop work.

---

## Step 6 — Publish

Produce a release binary:

```bash
dotnet publish src/HelloDotnet.Cli -c Release -o out
./out/HelloDotnet.Cli Ada
```

You should see the greeting print. Look in `out/`: there is a `HelloDotnet.Cli.dll`, a small launcher executable, and a `runtimeconfig.json`. This is what a deployable .NET 9 app looks like.

---

## Acceptance criteria

You can mark this exercise done when:

- [ ] You have a `HelloDotnet/` folder with `HelloDotnet.sln`, `src/`, `tests/`, and a `.gitignore`.
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet run --project src/HelloDotnet.Cli -- Ada` prints `Hello, Ada!` and the runtime version.
- [ ] `dotnet test` reports 4 passing tests.
- [ ] You have at least 3 Git commits with sensible messages.
- [ ] You can describe, in your own words, what the `.sln`, the `.csproj`, and the `obj/` and `bin/` folders are for.

---

## Stretch

- Add a `--upper` option to the CLI that uppercases the greeting. Test it.
- Add a second public method to `Greeter` and a test for it.
- Add a `Directory.Build.props` at the solution root that sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` for every project. Rebuild and confirm nothing breaks.

---

## Hints

<details>
<summary>If `dotnet new` says a template is not installed</summary>

The `console`, `classlib`, and `xunit` templates ship with the SDK. If you see "template not installed," your SDK is broken or partial — reinstall .NET 9 SDK from <https://dotnet.microsoft.com/en-us/download/dotnet/9.0>.

</details>

<details>
<summary>If `dotnet run` can't find your project</summary>

Use the explicit `--project` flag with the path to the `.csproj`. `dotnet run` from a folder with multiple projects is ambiguous and the CLI will refuse.

</details>

<details>
<summary>If a test won't be discovered</summary>

Make sure the test class is `public`, the test method is `public`, and you have `[Fact]` or `[Theory]` from `Xunit`. The xUnit template adds `using Xunit;` for you, but check it is present.

</details>

---

When this exercise feels comfortable, move to [Exercise 2 — Records and pattern matching](exercise-02-records-pattern-matching.cs).
