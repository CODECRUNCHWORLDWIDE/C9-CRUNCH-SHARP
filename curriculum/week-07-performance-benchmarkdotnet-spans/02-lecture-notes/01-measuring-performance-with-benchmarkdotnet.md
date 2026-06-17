# Lecture 1 — Measuring Performance with BenchmarkDotNet

> **Reading time:** ~70 minutes. **Hands-on time:** ~60 minutes (you write your first benchmark and read its output).

This is the lecture that earns you the right to make claims about performance. Everything later in the week — spans, pools, value tasks, struct vs class — is downstream of one habit: *measure before you change, measure after you change, and report the comparison*. The tool you use to do that in .NET is **BenchmarkDotNet**, a library written by Andrey Akinshin and Adam Sitnik (and maintained by a small team plus dozens of contributors from inside Microsoft) that the .NET runtime team itself runs against `dotnet/runtime` to catch regressions before they ship. We will use BDN the same way the runtime team does. By the end of this lecture you can install BDN into a fresh console project, write a benchmark that isolates a single hot path, configure `[MemoryDiagnoser]` so allocations show up alongside time, and read the resulting Markdown table with confidence.

## 1.1 — Why we don't time things with `Stopwatch`

The most common way junior .NET engineers measure performance is the wrong way:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < 1_000_000; i++)
{
    DoWork();
}
sw.Stop();
Console.WriteLine($"Elapsed: {sw.ElapsedMilliseconds} ms");
```

This snippet looks reasonable. It is wrong in at least seven different ways, and any one of the seven is enough to invalidate the number it prints:

1. **The first iteration pays the JIT cost.** `DoWork`'s first call goes through the Tier-0 JIT (fast compile, slow code). Subsequent calls may be re-compiled by the Tier-1 JIT (slow compile, fast code) once the method is "hot." The Tier-0 → Tier-1 transition is non-deterministic in timing. The first few thousand calls in the loop run with one code generation; the rest run with another. Averaging across them is wrong.
2. **The first iteration pays the static-initializer cost.** Any `static` field referenced by `DoWork` for the first time forces the type's static constructor to run. That is a one-time cost which the loop amortizes incorrectly.
3. **The garbage collector runs whenever it wants.** A gen-0 collection in the middle of the loop adds latency that has nothing to do with `DoWork`. You will not know it happened unless you inspect the `GC.CollectionCount(0)` before and after, which the snippet does not.
4. **The OS preempts the thread without warning.** A context switch, a hardware interrupt, or another process spinning up a thread can pause your loop for microseconds to milliseconds. Without statistical sampling, you cannot distinguish your code from the OS jitter.
5. **The CPU may throttle.** On battery, on a laptop that is thermally constrained, on a Mac with `pmset` defaults — the CPU frequency drifts. A loop that takes 2 ms on a cool CPU may take 4 ms on a hot one.
6. **The JIT may dead-code-eliminate the call.** If `DoWork` has no side effect the JIT can prove is observable, and you do not use its return value, the entire method body may be elided. The loop becomes a no-op. The `Stopwatch` reports the time of an empty loop.
7. **The `Stopwatch.Frequency` is one tick per CPU cycle on some platforms and one tick per 100 nanoseconds on others.** The unit you print depends on the platform. The default `ElapsedMilliseconds` rounds to whole milliseconds, which is too coarse for any modern microbenchmark.

BenchmarkDotNet addresses every single one of these. It runs your benchmark in **a separate process** (no JIT contamination, no static-init contamination, no GC from your test runner), **after a pilot phase** (which determines how many invocations per iteration are needed to amortize the timer resolution), **after a warm-up phase** (which lets the JIT settle into Tier-1), with **statistical confidence intervals** (so you can see whether the difference between two benchmarks is real or noise), with **a `Consumer` pattern** that defeats dead-code elimination, and with **`[MemoryDiagnoser]`** that reports allocations alongside time. The tool does the right thing by default. Your job is to write the benchmark *body* correctly.

## 1.2 — Installing BenchmarkDotNet

Performance work always starts in a *separate* project from the application. The benchmark project references the application; the application never references the benchmark project. This separation matters: BDN compiles in `Release` configuration with optimizations on, and you do not want to ship the BDN dependency in your application's package.

Create a fresh solution:

```bash
mkdir SharpBench && cd SharpBench
dotnet new sln -n SharpBench
dotnet new console -n SharpBench.Benchmarks -o src/SharpBench.Benchmarks --framework net9.0
dotnet sln add src/SharpBench.Benchmarks/SharpBench.Benchmarks.csproj
cd src/SharpBench.Benchmarks
dotnet add package BenchmarkDotNet --version 0.14.0
```

Open `SharpBench.Benchmarks.csproj` and confirm the file looks like this (BDN ships a `Release`-only validator that warns when you run in `Debug`; we make `Release` the default):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Configuration Condition=" '$(Configuration)' == '' ">Release</Configuration>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
    <TieredCompilation>true</TieredCompilation>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>
</Project>
```

The three GC properties at the bottom are not strictly required but they make the benchmark match what a production ASP.NET Core app uses by default. If you do not set them, BDN will warn you and your numbers will look different from what your colleagues see on their copies. Choose the production-shaped defaults and keep them.

## 1.3 — Your first benchmark

We will benchmark the canonical question every .NET engineer asks at some point: *is `string` concatenation with `+` slower than with `StringBuilder`?* The expected answer is "yes, dramatically, once you concatenate more than ~10 strings"; we will produce numbers that show *how much* slower.

Replace `Program.cs` with:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;

[MemoryDiagnoser]
public class StringConcatBenchmark
{
    private string[] _parts = null!;

    [Params(10, 100, 1_000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _parts = new string[N];
        for (int i = 0; i < N; i++)
        {
            _parts[i] = $"item-{i:000}-";
        }
    }

    [Benchmark(Baseline = true)]
    public string Plus()
    {
        string result = string.Empty;
        for (int i = 0; i < _parts.Length; i++)
        {
            result += _parts[i];
        }
        return result;
    }

    [Benchmark]
    public string StringBuilderConcat()
    {
        var sb = new StringBuilder(capacity: _parts.Length * 12);
        for (int i = 0; i < _parts.Length; i++)
        {
            sb.Append(_parts[i]);
        }
        return sb.ToString();
    }

    [Benchmark]
    public string StringConcatAll()
    {
        return string.Concat(_parts);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<StringConcatBenchmark>(args: args);
    }
}
```

Five things to notice in this file before we run it:

1. **`[MemoryDiagnoser]` on the class.** This is the diagnoser that produces the `Gen0` / `Gen1` / `Gen2` / `Allocated` columns. Without it, the table shows time only. Every benchmark in this course gets `[MemoryDiagnoser]`. There are no exceptions.

2. **`[Params]` on a property.** This tells BDN to run the benchmark three times for each `[Benchmark]` method — once with `N = 10`, once with `N = 100`, once with `N = 1_000`. The result table has one row per `(method, N)` pair, so we get nine rows total. `[Params]` defeats the JIT's constant folding: because the value is read from a property, the JIT cannot specialize the loop bound at compile time, and the benchmark reflects the cost as it appears in a real program.

3. **`[GlobalSetup]` on `Setup()`.** This runs *once per `(method, N)` pair*, before the warm-up. It is the right place to allocate the input data. A common bug is to put the data construction inside the `[Benchmark]` method itself, which then measures the construction cost on every iteration. The `[GlobalSetup]` separates "data" from "the work."

4. **`[Benchmark(Baseline = true)]` on `Plus()`.** Marking one method as the baseline produces a `Ratio` column in the output, showing each other method's mean as a multiple of the baseline. The baseline is conventionally "the obvious code you would write without thinking" — here, the `+=` loop.

5. **Every benchmark *returns* its computed value.** The `return result;`, `return sb.ToString();`, `return string.Concat(_parts);` lines do two jobs. They prevent the JIT from dead-code-eliminating the entire method body (because the result is observable to BDN), and they give BDN's `Consumer` the value to anchor against. If you wrote a `void`-returning benchmark that did the work and threw away the result, the JIT could in principle elide it. Return the result; let BDN handle the consumer logic.

Run it from the project directory:

```bash
dotnet run -c Release
```

You must include `-c Release`. BDN will refuse to run a `Debug` build (it prints a warning and exits), because `Debug` disables optimizations and the resulting numbers are useless.

The run takes a few minutes. BDN does the following, automatically, for each `(method, N)` combination:

1. **Pilot.** Run the method a handful of times to determine how many invocations per *iteration* produce a measurable signal above the timer's resolution. For very fast methods (sub-microsecond), this might be 1,000 or 10,000 invocations per iteration; for slower methods, 1 invocation per iteration.
2. **Warm-up.** Run several iterations (typically 6–8) and discard the results. This lets the JIT settle into Tier-1 and any caches warm up.
3. **Actual run.** Run more iterations (typically 15) and record each one. Each iteration is an aggregate of N invocations, where N is what the pilot determined.
4. **Overhead correction.** Run an empty method the same way and subtract its time from each measurement.
5. **Result.** Report mean, error (half of the 99.9% confidence interval), standard deviation, median, min, max, allocations, GC counts.

The output, printed at the end, is a Markdown table you can paste into a PR description. Mine looks like:

```
| Method              | N    | Mean        | Error      | StdDev    | Ratio   | Gen0     | Allocated   | Alloc Ratio |
|-------------------- |----- |------------:|-----------:|----------:|--------:|---------:|------------:|------------:|
| Plus                | 10   |    310.4 ns |     2.3 ns |   1.97 ns |    1.00 |   0.1564 |       984 B |        1.00 |
| StringBuilderConcat | 10   |    159.2 ns |     1.1 ns |   0.92 ns |    0.51 |   0.0830 |       522 B |        0.53 |
| StringConcatAll     | 10   |     72.5 ns |     0.4 ns |   0.31 ns |    0.23 |   0.0356 |       224 B |        0.23 |
| Plus                | 100  |  10,884.0 ns |   72.0 ns |  60.10 ns |    1.00 |  20.5078 |    128840 B |        1.00 |
| StringBuilderConcat | 100  |   1,201.6 ns |    9.6 ns |   8.97 ns |    0.11 |   0.5894 |      3712 B |        0.03 |
| StringConcatAll     | 100  |     455.9 ns |    3.5 ns |   3.05 ns |    0.04 |   0.3414 |      2144 B |        0.02 |
| Plus                | 1000 | 1,131,232.0 ns | 9,210.0 ns | 8,200.10 ns |  1.00 |2304.6875 |  14516280 B |        1.00 |
| StringBuilderConcat | 1000 |  16,432.1 ns |   88.0 ns |  82.41 ns |   0.01 |   5.6152 |     35248 B |        0.00 |
| StringConcatAll     | 1000 |   5,103.4 ns |   33.0 ns |  29.20 ns |   0.00 |   3.3722 |     21184 B |        0.00 |
```

Read it column by column:

- **`Method`** — the `[Benchmark]` method name.
- **`N`** — the `[Params]` value for this row.
- **`Mean`** — average time per single invocation (BDN divides the total iteration time by the per-iteration invocation count for you). Read it in nanoseconds (`ns`), microseconds (`μs`), milliseconds (`ms`) — BDN auto-scales.
- **`Error`** — half of the 99.9% confidence interval of the mean. If the `Error` is small relative to the `Mean` (say, less than 5%), the number is trustworthy. If it is large (say, 30%+), the run is noisy — close other applications and retry.
- **`StdDev`** — sample standard deviation across the iterations.
- **`Ratio`** — the mean divided by the baseline's mean for the same `N`. A `Ratio` of `0.11` means "this method takes 11% of the time the baseline takes" — i.e., 9× faster.
- **`Gen0`** — count of gen-0 garbage collections per 1,000 invocations. A value of `2304.69` (the `+=` at N=1000 row) means "2,304 gen-0 collections occurred during 1,000 invocations" — i.e., 2.3 gen-0 GCs *per invocation*. That is catastrophic and the reason that benchmark is so much slower than the others.
- **`Gen1` / `Gen2`** — count of gen-1 / gen-2 collections per 1,000 invocations. Omitted here because all three benchmarks produce zero.
- **`Allocated`** — bytes allocated on the managed heap per invocation. The `+=` at N=1000 allocates 14.5 MB *per call* — every intermediate `result += parts[i]` builds a new `string`, the old one becomes garbage, and the GC has to clean up. The `string.Concat(_parts)` path allocates 21 KB — one final string, no intermediates.
- **`Alloc Ratio`** — allocations as a fraction of the baseline's allocations.

The takeaways from this single table:

1. **`+=` in a loop is `O(N²)` in time and `O(N²)` in allocations.** Every step allocates a new string of length `current + next`, copies the current contents, and discards the old string. By `N = 1,000` it allocates 14.5 MB per call.
2. **`StringBuilder` is `O(N)` because it pre-sizes and grows geometrically.** With `capacity: N * 12` we pre-size it correctly and the only allocation is the final `ToString()`.
3. **`string.Concat(string[])` is the *fastest* because it knows the final length up front** (it walks the array once to sum lengths, allocates the result once, copies into it). When the data is already in an array, `string.Concat` is the right tool.

We have not written any "fast" code. We have measured three obvious ways to do the same thing and let the numbers tell us which is best. That is the loop.

## 1.4 — The five most common measurement bugs (and how BDN protects you)

BDN protects you from most measurement bugs by design. There are still five you can introduce yourself if you write the benchmark body wrong. Memorize them.

### Bug 1 — Allocating the input inside the benchmark

```csharp
// WRONG: measures both allocation and the work.
[Benchmark]
public string Plus()
{
    var parts = new string[1000];
    for (int i = 0; i < parts.Length; i++) parts[i] = $"item-{i}-";
    string result = string.Empty;
    for (int i = 0; i < parts.Length; i++) result += parts[i];
    return result;
}
```

The benchmark measures *both* the allocation of `parts` and the concatenation. Whichever dominates wins, and you cannot tell which from the result. **The fix**: move the input construction to `[GlobalSetup]` and read it from a field. We already did this in the example above.

### Bug 2 — Returning `void` instead of a value

```csharp
// WRONG: the JIT may elide the entire body.
[Benchmark]
public void Plus()
{
    string result = string.Empty;
    for (int i = 0; i < _parts.Length; i++) result += _parts[i];
    // No return. `result` is unused. The JIT can prove the loop has no observable
    // side effect (modulo allocations) and may eliminate it.
}
```

The fix is to return the computed value. BDN holds a reference to it so the JIT cannot eliminate the work. **All `[Benchmark]` methods in this course return a value.**

### Bug 3 — Shared mutable state across iterations

```csharp
// WRONG: each iteration mutates _list, so iteration K runs against the state
// left by iteration K-1.
private List<int> _list = new();

[Benchmark]
public int AddAndSum()
{
    _list.Add(42);
    int sum = 0;
    for (int i = 0; i < _list.Count; i++) sum += _list[i];
    return sum;
}
```

The list grows by one each iteration. The 100th invocation runs on a list of 100 items; the 10,000th on a list of 10,000. The "mean" is meaningless because the workload changes. **The fix**: use `[IterationSetup]` to reset state per iteration, or rewrite the benchmark to be stateless. Note that `[IterationSetup]` runs *inside* the timing region by default, so be careful with it — see the BDN "good practices" doc for the `RunStrategy` enum.

### Bug 4 — Constant folding the inputs

```csharp
// WRONG: the JIT can constant-fold the loop bound and unroll the work.
[Benchmark]
public int Sum()
{
    int s = 0;
    for (int i = 0; i < 1000; i++) s += i;
    return s;
}
```

The literal `1000` is a compile-time constant; the JIT may unroll, vectorize, or pre-compute the sum to `499_500`. The benchmark measures essentially nothing. **The fix**: read the loop bound from a `[Params]` property or a field. `[Params]` is the right tool — it also gives you multiple data points.

### Bug 5 — Ignoring the warm-up warning

When BDN prints a warning about the warm-up phase being insufficient — "The minimum observed time was below the threshold" — your benchmark body is too fast to measure reliably. Two fixes: (a) do more work per call (loop the work inside the benchmark, dividing the reported time by the loop count manually), or (b) accept that BDN's resolution is hitting the timer's resolution and trust the measurement only as an upper bound. The threshold is around 50–100 ns on modern hardware. Below that, you are measuring noise.

## 1.5 — Customising the BDN configuration

The defaults are good. Sometimes you need to override them. The `IConfig` interface and `ManualConfig` class let you compose a configuration object that the runner uses.

A common case: you want to run a *quick* benchmark for iteration, not the full statistically-confident run. BDN ships a `ShortRunJob` for exactly this:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
[ShortRunJob]  // 3 warm-ups, 3 iterations — fast but noisier.
public class QuickBenchmark
{
    [Benchmark]
    public int Work() => Enumerable.Range(0, 1000).Sum();
}
```

`[ShortRunJob]` is for iteration. When you commit a result to a PR description, run without it (the default has 6 warm-ups + 15 iterations and is much more reliable). Reserve `[ShortRunJob]` for the "is this change worth a full run?" question.

Another common case: you want to compare runtime versions. Add multiple `[SimpleJob]` attributes, each targeting a different `RuntimeMoniker`:

```csharp
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net80, baseline: true)]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class RuntimeComparisonBenchmark
{
    [Benchmark]
    public int Work() => "hello".AsSpan().IndexOf('l');
}
```

BDN will install both runtimes (if not already present) and run the benchmark against each. The result table grows a `Runtime` column. This is the technique the .NET runtime team uses for the yearly "Performance Improvements in .NET N" posts.

A third common case: you want disassembly. Add `[DisassemblyDiagnoser]`:

```csharp
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 2)]
public class DisassemblyBenchmark
{
    [Benchmark]
    public int Sum()
    {
        ReadOnlySpan<int> data = stackalloc int[] { 1, 2, 3, 4 };
        int s = 0;
        for (int i = 0; i < data.Length; i++) s += data[i];
        return s;
    }
}
```

The diagnoser writes a file under `BenchmarkDotNet.Artifacts/results/...asm.md` containing the JIT-emitted x64 (or arm64) for each benchmark method. This is how you confirm that the JIT vectorized your loop, elided your bounds checks, or unrolled by 4. We will use it sparingly — disassembly is high-information-density but easy to over-read into.

## 1.6 — Exporting results

By default, BDN writes results into a `BenchmarkDotNet.Artifacts/` directory next to your project's `bin/Release/`. The structure looks like:

```text
BenchmarkDotNet.Artifacts/
├── results/
│   ├── StringConcatBenchmark-report.md       ← Markdown table (PR-ready)
│   ├── StringConcatBenchmark-report.csv      ← CSV (spreadsheet-ready)
│   ├── StringConcatBenchmark-report.html     ← HTML (browser-ready)
│   ├── StringConcatBenchmark-report-github.md ← Markdown tuned for GitHub
│   └── StringConcatBenchmark-report.json     ← JSON (programmatic)
└── BenchmarkRun-20260513-101245.log          ← Full run log
```

When you copy a result into a homework or PR, prefer the `-report-github.md` file. It is the cleanest form for GitHub Markdown rendering. Always check the log file too — it contains the warnings BDN emitted, which include hints about measurement reliability.

You can add custom exporters via `ManualConfig`:

```csharp
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;

public class Config : ManualConfig
{
    public Config()
    {
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);
        // ... add diagnosers, jobs, columns as needed.
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<StringConcatBenchmark>(new Config(), args);
    }
}
```

The default config already includes the Markdown exporter, so adding it again is harmless. The point of `ManualConfig` is to add things the defaults do *not* include — a custom CSV with extra columns, an XML exporter, a JSON exporter for a CI pipeline that reads the numbers programmatically.

## 1.7 — The reflexes to internalize this week

You will benchmark a lot of code in the next four lectures. Build the following reflexes:

- **Every benchmark has `[MemoryDiagnoser]`.** No exceptions. The allocation column is half the information.
- **Every benchmark returns a value.** Not `void`. The return value is the JIT's anchor against dead-code elimination.
- **Every variable input goes through `[Params]`.** Literal constants in benchmark bodies are a JIT-folding bug waiting to happen.
- **Every input allocation goes in `[GlobalSetup]`.** Not in the benchmark body.
- **Every "this is faster" claim cites a BDN run.** Not a `Stopwatch`. Not an intuition. A BDN run, with the Markdown table pasted, with `Allocated` shown.
- **You always run in `Release`.** `dotnet run -c Release`. BDN will refuse `Debug`, but you should know why before BDN tells you.
- **You read the warnings.** When BDN prints "outliers were removed (8 outliers from 17)," the run was noisy — close apps, plug in, retry. When it prints "the minimum observed time was below 100 ns," the benchmark body is too small to measure reliably and you should add a small `for` loop inside it.
- **You write the benchmark, then read the table, then change one thing, then read the table again.** The comparison is the work. The numbers in isolation are noise.

These reflexes are the entire methodology of senior .NET performance work. The next two lectures introduce *what* to change once you know how to measure. Without this lecture, the next two are folklore. With this lecture, they are engineering.

## 1.8 — What we did not cover (Week 11 picks it up)

This lecture is *micro*-benchmarking — measure one method in isolation, in a controlled environment. The other half of performance work is *macro*-profiling — capture a trace from a running production-shaped workload and find the hot path post hoc. The tools for that are `dotnet-trace`, `dotnet-counters`, `PerfView` (Windows), `perf` (Linux), and Apple Instruments (macOS). They produce different shapes of evidence: a profiler tells you "this method accounts for 23% of CPU time across all requests," whereas BDN tells you "this specific call costs 4.3 μs and allocates 320 B." Both are useful; they answer different questions.

Week 11 covers the macro-profiling stack. For Week 7, BDN is enough — you can produce defensible numbers for any single hot path you can isolate into a benchmark. The mini-project this week is exactly that exercise.

---

## Lecture 1 — checklist before moving on

- [ ] I can scaffold a BDN benchmark project from `dotnet new console` + `dotnet add package BenchmarkDotNet`.
- [ ] I can write a `[MemoryDiagnoser]`-decorated benchmark with `[Params]`, `[GlobalSetup]`, and a `Baseline = true` method.
- [ ] I can read each column in the BDN result table (Mean, Error, StdDev, Ratio, Gen0/1/2, Allocated, Alloc Ratio).
- [ ] I can identify the five common measurement bugs (input allocation, void return, shared mutable state, constant folding, ignored warmup).
- [ ] I can run BDN in `Release` mode and find the artifacts directory.
- [ ] I have actually run the `StringConcatBenchmark` from this lecture and looked at the result table on my machine.

If any box is unchecked, return to that section. Lecture 2 assumes you have done your first BDN run yourself.

---

**References cited in this lecture**

- BenchmarkDotNet — "Getting started": <https://benchmarkdotnet.org/articles/guides/getting-started.html>
- BenchmarkDotNet — "Good practices": <https://benchmarkdotnet.org/articles/guides/good-practices.html>
- BenchmarkDotNet — "Diagnosers": <https://benchmarkdotnet.org/articles/configs/diagnosers.html>
- Microsoft Learn — "Performance considerations for `string` operations": <https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings>
- Stephen Toub — "Performance Improvements in .NET 9": <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
- Adam Sitnik — "Sample Perf Investigation": <https://adamsitnik.com/Sample-Perf-Investigation/>
