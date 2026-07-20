# Week 7 — Resources

Every resource on this page is **free**. Microsoft Learn is free without an account. The `dotnet/runtime`, `dotnet/BenchmarkDotNet`, and `dotnet/performance` source repositories are MIT-licensed and public on GitHub. The Stephen Toub and Adam Sitnik blog posts are free. The relevant chapters of Konrad Kokosa's "Pro .NET Memory Management" that are linked from the author's blog are excerpts shared freely by the author. No paywalled books are linked.

## Required reading (work it into your week)

- **BenchmarkDotNet — official documentation entry point**:
  <https://benchmarkdotnet.org/>
- **BenchmarkDotNet — "Getting started"**:
  <https://benchmarkdotnet.org/articles/guides/getting-started.html>
- **BenchmarkDotNet — "Diagnosers" (the `[MemoryDiagnoser]` reference)**:
  <https://benchmarkdotnet.org/articles/configs/diagnosers.html>
- **BenchmarkDotNet — "Good practices" (read this twice)**:
  <https://benchmarkdotnet.org/articles/guides/good-practices.html>
- **BenchmarkDotNet — "Avoiding the JIT mischief" (the `Consumer`, `[NoInlining]`, dead-code-elimination notes)**:
  <https://benchmarkdotnet.org/articles/samples/IntroBasic.html>
- **Microsoft Learn — `Span<T>` overview**:
  <https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/>
- **Microsoft Learn — Memory and span usage guidelines**:
  <https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines>
- **Microsoft Learn — `ref struct` types**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct>
- **Microsoft Learn — `stackalloc` expression**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc>
- **Microsoft Learn — `ArrayPool<T>` reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1>
- **Microsoft Learn — `ValueTask<T>` overview and usage guidance**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1>
- **Microsoft Learn — "Understanding the cost of async operations"**:
  <https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios>
- **Microsoft Learn — `struct` design guidelines**:
  <https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/struct>
- **Microsoft Learn — "Performance considerations for `string` operations"**:
  <https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings>
- **Microsoft Learn — "What's new in .NET 9 performance"** (the official release notes):
  <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-9/runtime>

## Authoritative deep dives

- **Stephen Toub — "Performance Improvements in .NET 9"** — the canonical, ~150-page yearly review of every performance change shipped in the runtime. Each section pairs a code change with a BDN result. Read at least the introduction, the GC section, and one specialty section (JSON or LINQ are the most accessible):
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>
- **Stephen Toub — "Performance Improvements in .NET 8"** — the prior year's review, useful when the .NET 9 post references "since .NET 8" deltas:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-8/>
- **Stephen Toub — "Performance Improvements in .NET 7"** — the year that introduced many `ref struct` features and the LINQ optimizer:
  <https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-7/>
- **Stephen Toub — "Understanding the Whys, Whats, and Whens of ValueTask"**:
  <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>
- **Stephen Toub — "An Introduction to System.Threading.Channels"** — included here because the Channels post is also the canonical demonstration of `ValueTask` use in a high-throughput producer/consumer:
  <https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/>
- **Adam Sitnik — "Span"** (the original Span deep dive):
  <https://adamsitnik.com/Span/>
- **Adam Sitnik — "Array Pool"** (the original ArrayPool walk-through):
  <https://adamsitnik.com/Array-Pool/>
- **Adam Sitnik — "ValueStringBuilder — a stack-allocated string builder"**:
  <https://adamsitnik.com/Span-Performance/>
- **Adam Sitnik — "BenchmarkDotNet — How to design accurate and reliable benchmarks?"** — Sitnik is one of the BDN authors; this is the closest thing to an "official" methodology guide for the tool:
  <https://adamsitnik.com/Sample-Perf-Investigation/>
- **Konrad Kokosa — `tooslowexception.com`** (Konrad is the author of "Pro .NET Memory Management"; his blog mirrors the practical chapters of the book):
  <https://tooslowexception.com/>
- **David Fowler — "ASP.NET Core performance tips"** (a Gist; foundational reading for understanding why Kestrel is shaped the way it is):
  <https://github.com/davidfowl/AspNetCoreDiagnosticScenarios>
- **Brennan Conroy — "Span deep dives"** on the .NET team blog:
  <https://devblogs.microsoft.com/dotnet/?s=span>

## Official .NET docs

- **`System.Buffers.ArrayPool<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1>
- **`System.Buffers.MemoryPool<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.memorypool-1>
- **`System.Buffers.IBufferWriter<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.ibufferwriter-1>
- **`System.Span<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.span-1>
- **`System.ReadOnlySpan<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.readonlyspan-1>
- **`System.Memory<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.memory-1>
- **`System.ReadOnlyMemory<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.readonlymemory-1>
- **`System.Runtime.InteropServices.MemoryMarshal` API reference** — for `MemoryMarshal.GetReference`, `MemoryMarshal.Cast`, and the lower-level span primitives:
  <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal>
- **`System.Buffers.Text.Utf8Formatter` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.utf8formatter>
- **`System.Buffers.Text.Utf8Parser` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.utf8parser>
- **`System.Threading.Tasks.ValueTask<T>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1>
- **`string.Create<TState>` API reference**:
  <https://learn.microsoft.com/en-us/dotnet/api/system.string.create>

## Source repos worth skimming

- **`dotnet/BenchmarkDotNet`** — the BDN repo. Look at `src/BenchmarkDotNet/Attributes/` for the attribute surface and `src/BenchmarkDotNet.Core/Engines/` for the measurement engine:
  <https://github.com/dotnet/BenchmarkDotNet>
- **`dotnet/runtime`** — the .NET runtime. The performance-sensitive pieces live in `src/libraries/System.Private.CoreLib/src/System/`:
  <https://github.com/dotnet/runtime>
- **`dotnet/runtime` — `ArrayPool<T>` implementation**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/TlsOverPerCoreLockedStacksArrayPool.cs>
- **`dotnet/runtime` — `string.Concat` and related**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/String.Manipulation.cs>
- **`dotnet/runtime` — `ValueTask<T>` source**:
  <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/ValueTask.cs>
- **`dotnet/performance`** — the cross-runtime performance test suite. The benchmarks here are the same ones the runtime team uses for regression gating:
  <https://github.com/dotnet/performance>

## Selected `dotnet/runtime` issues to skim

These are closed performance issues with PRs that include BDN before/after tables. Each is a self-contained case study in BDN-driven engineering:

- **`Span<T>.IndexOf` SIMD-acceleration**: <https://github.com/dotnet/runtime/issues/61491>
- **`string.Contains(ReadOnlySpan<char>)` allocation reduction**: <https://github.com/dotnet/runtime/issues/76639>
- **`Dictionary<TKey, TValue>` allocation profile** (the canonical "small struct keys are cheap" demonstration): <https://github.com/dotnet/runtime/issues/61135>
- **LINQ `Where(...).Count()` enumerator boxing fix**: <https://github.com/dotnet/runtime/issues/81388>
- **`Utf8JsonWriter` `IBufferWriter<T>` integration**: <https://github.com/dotnet/runtime/pull/63097>

## "Pro .NET Memory Management" — the freely available excerpts

Konrad Kokosa's book is paid; the author maintains a blog at `tooslowexception.com` where he reproduces and expands several chapters. The chapters worth reading for this week:

- **"The cost of a method call"**: <https://tooslowexception.com/the-cost-of-a-method-call/>
- **"Pinning, fixed, and the GC"**: <https://tooslowexception.com/pinning/>
- **"Span<T>, what's the catch?"**: <https://tooslowexception.com/span-whats-the-catch/>
- **"ValueTask deep dive"**: <https://tooslowexception.com/valuetask-vs-task/>

If you find the blog excerpts illuminating and want the full text, the book is the best single resource on .NET memory in print, but the blog gets you most of what you need for the exercises here.

## Talks worth watching (all free, no account)

- **Stephen Toub — "Deep Dive Async"** (.NET Conf, on YouTube). The canonical 90-minute walk through `async`, `Task`, `ValueTask`, and the state machine:
  search YouTube for "Stephen Toub deep dive async".
- **David Fowler — "Diagnosing performance problems in ASP.NET Core"** (.NET Conf, on YouTube):
  search YouTube for "David Fowler ASP.NET Core diagnostics".
- **Adam Sitnik — "Performance is a Feature"** (NDC, on YouTube):
  search YouTube for "Adam Sitnik performance is a feature".
- **Maoni Stephens — "Garbage Collection deep dive"** — Maoni is the .NET GC architect:
  search YouTube for "Maoni Stephens GC".

## How to use this resource list

The lectures cite specific URLs from this page at decision points. When a lecture says "see Microsoft Learn's Span<T> overview," you can find the URL above. Do not feel obligated to read every link in week one — even senior .NET engineers re-read these references when they touch the relevant code. The links you should read end-to-end this week are:

1. **BenchmarkDotNet — "Good practices"** (Required reading section). Foundational; do not skip.
2. **Microsoft Learn — `Span<T>` overview**. Foundational; do not skip.
3. **Stephen Toub — "Understanding the Whys, Whats, and Whens of ValueTask"**. ~30 minutes, decisive for one of the homework problems.
4. **Adam Sitnik — "Span" (the deep dive)**. ~45 minutes, the best companion to lecture 02.

The rest are reference material — bookmark and return to them when a specific question arises.

---

*Bookmarks decay. If a link rots, search the title — these are all canonical pieces and they reappear on the same authors' new homes.*
