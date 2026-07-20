# Week 7 — .NET Performance Engineering: BenchmarkDotNet, Spans, and Allocation Reduction

Welcome to **C9 · Crunch Sharp**, Week 7. Week 1 made the language ordinary. Week 2 put it behind an HTTP port. Week 3 gave it a real database. Week 4 made it concurrent. Week 5 made it declarative. Week 6 put a door in front of it. This week we step back from features and ask the question every senior .NET engineer eventually has to answer for themselves: *how fast is this code, why is it that fast, and what would it take to make it ten times faster without making it ten times harder to read?* By Friday you should be able to install BenchmarkDotNet into a clean console project, write a `[MemoryDiagnoser]`-decorated benchmark that produces statistically defensible numbers, read its output the way a senior engineer reads it (mean, error, allocations, gen-0/gen-1/gen-2 collections), rewrite a string-concatenation hot path so it produces *zero* heap allocations, return a buffer to `ArrayPool<byte>.Shared` instead of letting it become garbage, decide between `Task<T>` and `ValueTask<T>` for a method that completes synchronously 95% of the time, and decide between `struct` and `class` for a data type based on the actual cost model rather than the folklore.

This is the first week of Phase 3 — **performance engineering**. Phases 1 and 2 made you fluent in the language and the framework; this phase makes you fluent in the *cost* of what you write. Performance work in .NET is unlike performance work in most other ecosystems. The runtime is sophisticated: a tiered JIT (`Tier-0` for fast startup, `Tier-1` for steady state, `DynamicPMO` for profile-guided re-JIT), three generations of garbage collector with a separate large-object heap, escape analysis that occasionally promotes locals to the stack, and intrinsics for SIMD on every platform Microsoft ships. The result is that *intuition lies, often badly, about which code is faster*. The only honest answer is measurement, and BenchmarkDotNet (BDN, hereafter) is the measurement tool the .NET runtime team itself uses to gate performance regressions in `dotnet/runtime`. We will use it the same way they do.

The first thing to internalize is that **performance engineering is a measurement discipline before it is a coding discipline**. The temptation when an endpoint is slow is to read the code, find something that looks expensive, and rewrite it. This works occasionally and wastes a day the rest of the time. The senior move is the opposite: write a benchmark that *isolates* the suspected hot path, run BDN to get a baseline (mean time per call, allocations per call, GC collections per 1,000 calls), make exactly one change, re-run, compare. If the change does not improve the numbers, revert it and try a different one. Stephen Toub's "Performance Improvements in .NET N" series on the Microsoft DevBlogs is the canonical demonstration of this loop — every paragraph in it pairs a code change with a BDN result, and you can read it as a 200-page tutorial on how to think about .NET performance. We will read parts of it.

The second thing to internalize is that **most .NET performance wins come from reducing allocations, not from reducing CPU cycles**. The CPU is fast. The garbage collector is correct. But every byte you allocate on the managed heap eventually has to be traced, marked, swept, and (sometimes) compacted, and each of those operations is amortized across every future allocation. A method that allocates a 32-byte intermediate object on every call looks free in a microbenchmark of one invocation; at one million invocations per second on a server, it generates 32 MB/s of garbage, which the gen-0 collector has to walk, which adds tail-latency p99 spikes that profile poorly because the work is *not* in your method — it is in the GC running on a different thread. The fix is rarely "rewrite the algorithm" and usually "stop allocating the 32-byte intermediate." Lecture 2 introduces `Span<T>` and `Memory<T>`, the two types the runtime team added to .NET Core 2.1 specifically so you could write fast parsers without allocating. Lecture 3 introduces `ArrayPool<T>`, `ValueTask`, and the struct-vs-class trade-off — the three other tools you reach for once `Span<T>` is in your hands.

The third thing to internalize is that **`Span<T>` is not a smaller `T[]` — it is a stack-only window into memory that may or may not be on the managed heap**. A `Span<byte>` can point at a region of a `byte[]`, at a region of a `stackalloc byte[256]` buffer, at a region of a native pointer obtained from `Marshal.AllocHGlobal`, or at a region of a string (`ReadOnlySpan<char>`). The same `Span<T>` API works against all of them. The cost is one runtime rule you must obey: a `Span<T>` cannot be stored on the heap. It cannot be a field of a class, the type argument of a `Task<T>`, the captured variable of an `async` method, or the type of a `List<T>` element. The C# compiler enforces this at compile time via the `ref struct` annotation. The reward for accepting the rule is the most powerful zero-allocation primitive in the .NET ecosystem: a unified view over heap memory, stack memory, and native memory, with bounds-checked indexing, slicing in O(1), and a JIT that increasingly emits the same machine code it would for the underlying buffer.

The fourth thing to internalize is that **`struct` is a tool for reducing allocation pressure, not a default**. The CLR allocates structs inline — on the stack if they are locals, in the containing object if they are fields, in the array's contiguous backing buffer if they are array elements. A `struct` you pass between methods costs zero allocations and one or more memory copies (which the JIT often elides). The trade-off is the copy cost: large structs (more than ~24 bytes, the value varies with the platform) become slower to pass than the equivalent `class` reference. Defensive copies (a struct returned from a property has to be copied before you can read its fields) compound the cost when structs are misused. Lecture 3 covers the heuristic the runtime team uses: prefer `struct` for short-lived, small (≤ 16 bytes), immutable data with no inheritance hierarchy; prefer `class` for everything else. Use BDN with `[MemoryDiagnoser]` to verify, not to guess.

## Learning objectives

By the end of this week, you will be able to:

- **Install** BenchmarkDotNet into a clean .NET 9 console project, configure it with `[MemoryDiagnoser]`, `[ShortRunJob]` (for iteration) and a `Config` that emits Markdown + CSV results, and run it from the command line.
- **Read** a BenchmarkDotNet result table column by column. Distinguish `Mean`, `Error`, `StdDev`, `Median`, `Gen0`, `Gen1`, `Gen2`, `Allocated`, and the `Ratio` / `Baseline` columns. Know what each value tells you and which you should be most suspicious of.
- **Write** a benchmark that is *honest* — not constant-folded by the JIT, not dead-code-eliminated, not warmed up incorrectly, not sharing state between invocations. Use `[GlobalSetup]`, `[IterationSetup]`, the `Consumer` pattern, and `[Params]` correctly.
- **Distinguish** `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, and `ReadOnlyMemory<T>`. Know which is a `ref struct` (and why it cannot be a field), which can cross `await` boundaries, and which works as a `Task<T>` return type.
- **Allocate** a temporary buffer with `stackalloc` and operate on it through a `Span<T>` without ever touching the managed heap. Recognize the 1 KB-per-frame limit beyond which `stackalloc` is dangerous, and use `ArrayPool<T>.Shared.Rent(size)` for larger temporary buffers.
- **Use** `ArrayPool<T>.Shared.Rent(...)` and `Return(...)` correctly, in a `try / finally` so the buffer is returned even on exception, with `clearArray: true` if the buffer held sensitive data.
- **Decide** between `Task<T>` and `ValueTask<T>` for an async method. Apply the rule "use `ValueTask` only when the method completes synchronously a meaningful fraction of the time AND you control the callers." Read the documented restrictions (consume once, do not block, do not `Task.WhenAll`).
- **Decide** between `struct` and `class` for a new data type, with BDN evidence not folklore. Recognize the four rules: small (≤ 16 bytes), short-lived, immutable, no inheritance.
- **Rewrite** a method that allocates intermediate strings or arrays into one that allocates zero, using `Span<T>` + `stackalloc` + `ArrayPool<T>`. Verify the rewrite with BDN `[MemoryDiagnoser]` showing `Allocated: 0 B`.
- **Profile** a slow LINQ pipeline with BDN, identify the allocations (boxed lambdas, enumerator boxing, intermediate `IEnumerable<T>` allocations), rewrite it as a `for` loop over a `Span<T>` or an `ArrayPool<T>` buffer, and report a 10× or better speedup with numbers.
- **Cite** the `dotnet/runtime` GitHub issues, Microsoft Learn pages, and Stephen Toub DevBlogs posts that justify each technique.

## Prerequisites

- **Weeks 1 through 6** of C9 complete: you can scaffold a multi-project solution, write Minimal API endpoints, model an EF Core context, compose `async`/`await`, read a LINQ chain out loud, and wire cookie + JWT auth without surprises. Performance work assumes you are no longer surprised by syntax.
- **A working `dotnet --version` of `9.0.x` or later** on your PATH. BenchmarkDotNet `0.14.x` or later is the package version we target.
- **An understanding of `async`/`await` mechanics from Week 4.** This week leans on it: `ValueTask` is a refinement, not a replacement, and the refinement only makes sense if you remember how state machines, `IAsyncStateMachine`, and the threadpool interact.
- **Familiarity with `Stopwatch`-based ad-hoc timing.** You will *unlearn* it this week — BDN handles all of `Stopwatch`'s correctness pitfalls (warm-up, JIT tiering, GC noise, statistical confidence) — but you should at least have wielded `Stopwatch.StartNew()` and `sw.ElapsedMilliseconds` once before, to appreciate what BDN automates.
- **A laptop quiet enough to benchmark on.** Close Slack, close Chrome, plug into mains power if you are on battery, disable CPU throttling if you can. BDN warns you about all of these; production benchmarks should run on dedicated hardware, but a quiet laptop is enough for the exercises in this week.
- Nothing else. We start from a clean `dotnet new console`, end at a benchmark suite that produces honest, citable numbers, and never install a paid profiler.

## Topics covered

- **BenchmarkDotNet — the tool.** What BDN is, why the .NET runtime team uses it for regression gating, the `[MemoryDiagnoser]` / `[Params]` / `[Benchmark]` attributes, the `BenchmarkRunner.Run<T>()` entry point, the `BenchmarkDotNet.Configs.IConfig` interface for custom configurations.
- **The benchmark lifecycle.** Pilot, warm-up, actual measurement, overhead correction. Why BDN runs every method multiple times in multiple processes. What `[GlobalSetup]`, `[IterationSetup]`, `[GlobalCleanup]`, `[IterationCleanup]` each do and when each is appropriate. Why mutation inside `[Benchmark]` methods is almost always a bug.
- **Reading BDN output.** The columns: `Mean`, `Error`, `StdDev`, `Median`, `Min`, `Max`, `Gen0`, `Gen1`, `Gen2`, `Allocated`, `Ratio`, `RatioSD`. The default Markdown table format, the CSV exporter, the JSON exporter. The `BenchmarkDotNet.Artifacts/` directory layout.
- **JIT mischief.** Dead-code elimination: BDN's `Consumer` and the simpler "return a value from the benchmark method" pattern. Constant folding: `[Params]` defeats it because the parameter values are not known until runtime. Inlining: `[NoInlining]` on the benchmark method itself if you must isolate a non-inlinable callee.
- **`Span<T>` and `ReadOnlySpan<T>`.** The `ref struct` annotation, why a span cannot be a field, why it cannot be captured by a lambda, why it cannot cross an `await` boundary. The unified view over `T[]`, `stackalloc T[N]`, and pointer memory. Slicing in O(1), bounds checking, and the JIT's increasing ability to elide bounds checks.
- **`Memory<T>` and `ReadOnlyMemory<T>`.** The heap-resident counterparts to spans. Why they exist (you need the API for async pipelines where the buffer outlives a stack frame). `MemoryMarshal.GetReference`, `.Span` property, the pinning model.
- **`stackalloc`.** The C# 8 expression form, the stack-frame size limit, why you must check the size before `stackalloc` for variable-length buffers. The "use `stackalloc` for ≤ 1 KB, `ArrayPool<T>` otherwise" rule.
- **`ArrayPool<T>`.** `ArrayPool<T>.Shared.Rent(min)`, `Return(buffer, clearArray)`, the `try / finally` discipline. Why the returned buffer may be *larger* than the requested size and why your code must read `buffer.Length` only after slicing. The pool's per-bucket size ladder.
- **`ValueTask<T>` and `ValueTask`.** The performance promise (no `Task<T>` allocation when the method completes synchronously), the consumption restrictions (consume exactly once, do not `await` twice, do not pass to `Task.WhenAll`), and the audit rule (every `async ValueTask` method that produces a synchronous result on the hot path).
- **`struct` vs `class` cost model.** Where each lives in memory, the copy cost of large structs, defensive copies on property access, the `readonly struct` annotation. The four rules of when to use `struct`. The case studies: `Span<T>`, `DateTime`, `Guid`, `TimeSpan` — all structs; `string`, `Stream`, `HttpClient` — all classes.
- **Allocation reduction techniques.** Caching the lambda (the boxing of closure variables), `StringBuilder` over `+` for long concatenations, `string.Create<TState>` for fixed-length strings, `Utf8Formatter` for primitive-to-text, `Utf8Parser` for text-to-primitive, `IBufferWriter<T>` for streaming output, custom pooling for domain objects.
- **The cost of LINQ.** Why `Where(...).Select(...).ToList()` allocates three enumerators, two delegates, and a list. When LINQ is "fine" (cold paths, small inputs, code that runs once at startup). When it is "not fine" (per-request paths, request-bound `IEnumerable<T>` returned from a service). The rewrite recipe: replace the LINQ chain with a `foreach` over a `Span<T>` or a `ReadOnlySpan<T>`, accumulate into a pre-sized list or an `ArrayPool` buffer.

## Weekly schedule

The schedule adds up to approximately **34 hours**. Treat it as a target, not a contract. Benchmarks are best run early in the day on a quiet machine; do not save the BDN runs for the last 30 minutes of an evening.

| Day       | Focus                                                  | Lectures | Exercises | Challenges | Quiz/Read | Homework | Mini-Project | Self-Study | Daily Total |
|-----------|--------------------------------------------------------|---------:|----------:|-----------:|----------:|---------:|-------------:|-----------:|------------:|
| Monday    | BenchmarkDotNet — install, run, read the table         |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Tuesday   | Span, ReadOnlySpan, stackalloc, slicing                |    2h    |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5.5h    |
| Wednesday | ArrayPool, ValueTask, struct vs class                  |    1.5h  |    1.5h   |     0h     |    0.5h   |   1h     |     0h       |    0.5h    |     5h      |
| Thursday  | Allocation reduction in practice, challenge #1         |    0.5h  |    0h     |     2h     |    0.5h   |   1h     |     2h       |    0.5h    |     6.5h    |
| Friday    | Mini-project — profile and rewrite the slow LINQ       |    0h    |    0h     |     1h     |    0.5h   |   1h     |     3h       |    0.5h    |     6h      |
| Saturday  | Mini-project deep work, regression test suite          |    0h    |    0h     |     0h     |    0h     |   0h     |     3h       |    0h      |     3h      |
| Sunday    | Quiz, review, polish                                   |    0h    |    0h     |     0h     |    1h     |   0h     |     0.5h     |    0h      |     1.5h    |
| **Total** |                                                        | **6h**   | **4.5h**  | **3h**     | **3.5h**  | **5h**   | **8.5h**     | **2.5h**   | **33h**     |

## How to navigate this week

| File | What's inside |
|------|---------------|
| [README.md](./README.md) | This overview (you are here) |
| [resources.md](./resources.md) | BenchmarkDotNet docs, Microsoft Learn .NET performance docs, Stephen Toub's DevBlogs series, Adam Sitnik's posts, and the `dotnet/runtime` issues to skim |
| [lecture-notes/01-measuring-performance-with-benchmarkdotnet.md](./lecture-notes/01-measuring-performance-with-benchmarkdotnet.md) | BDN end-to-end: install, write the first benchmark, read the table, avoid the three common measurement bugs, use `[Params]` and `[GlobalSetup]` correctly |
| [lecture-notes/02-spans-and-stack-only-buffers.md](./lecture-notes/02-spans-and-stack-only-buffers.md) | `Span<T>` / `ReadOnlySpan<T>` end-to-end: the `ref struct` rules, `stackalloc`, slicing, the relationship to `Memory<T>`, and the JIT's bounds-check elision |
| [lecture-notes/03-allocation-reduction-arraypool-valuetask.md](./lecture-notes/03-allocation-reduction-arraypool-valuetask.md) | `ArrayPool<T>`, `ValueTask<T>`, `struct` vs `class`, the LINQ allocation cost model, and the rewrite recipes |
| [exercises/exercise-01-benchmark-string-concat.cs](./exercises/exercise-01-benchmark-string-concat.cs) | Benchmark four ways to concatenate 1,000 strings: `+`, `string.Concat`, `StringBuilder`, `string.Create<TState>` over a `Span<char>`. Report mean and allocations |
| [exercises/exercise-02-implement-readonlyspan-parser.cs](./exercises/exercise-02-implement-readonlyspan-parser.cs) | Implement a CSV-line parser that takes a `ReadOnlySpan<char>` and returns three values without allocating. Benchmark against `string.Split(',')` |
| [exercises/exercise-03-pool-then-rent-then-return.cs](./exercises/exercise-03-pool-then-rent-then-return.cs) | Implement a `Hex.Encode(ReadOnlySpan<byte>)` that uses `ArrayPool<char>` for the intermediate buffer. Verify zero allocations on the hot path |
| [exercises/SOLUTIONS.md](./exercises/SOLUTIONS.md) | Annotated solutions for the three exercises, with the BDN tables you should reproduce |
| [challenges/challenge-01-rewrite-a-method-to-be-zero-alloc.md](./challenges/challenge-01-rewrite-a-method-to-be-zero-alloc.md) | Take a provided allocation-heavy method (a query-string builder), rewrite it to zero allocations, report a BDN before/after |
| [challenges/challenge-02-design-and-bench-a-custom-collection.md](./challenges/challenge-02-design-and-bench-a-custom-collection.md) | Design a stack-allocated fixed-capacity list (`StackList<T>`, capacity 16), bench it against `List<T>` and `Span<T>` for sum/min/max workloads |
| [quiz.md](./quiz.md) | 10 multiple-choice questions on BDN, spans, pools, value tasks, and the struct/class cost model |
| [homework.md](./homework.md) | Six practice problems for the week |
| [mini-project/README.md](./mini-project/README.md) | Full spec for "Sharp Bench" — take a deliberately-slow LINQ pipeline, profile it with BDN, rewrite to be ≥10× faster, report numbers |

## The "build succeeded" promise — restated

C9 still treats `dotnet build` output as a contract:

```
Build succeeded · 0 warnings · 0 errors · 412 ms
```

For Week 7 we add a second contract: **every benchmark in this week's exercises must produce a `[MemoryDiagnoser]`-annotated result table you can paste into your README**. A benchmark that does not show its allocations is half-evidence; a benchmark that shows allocations but no mean time is the other half. We will always show both.

We add a third contract too: **every "this is faster" claim in your homework must be backed by a BDN run**. The phrase "should be faster" never appears in a senior engineer's PR description; the phrase "is faster: 4.2µs vs 38.7µs, 0 B vs 416 B" does. Practice the second form this week so it becomes the only form you reach for.

## A note on what's not here

Week 7 introduces local performance engineering — BDN, spans, pools, value tasks, allocation reduction. It does **not** introduce:

- **`PerfView`, `dotnet-trace`, `dotnet-counters`, ETW.** All are part of the production .NET performance toolbox; all are platform-specific (PerfView is Windows-first; `dotnet-trace` is cross-platform). They are Week 11 (production telemetry) material. BDN is the right tool for *micro* benchmarking; the tracers are the right tools for *macro* profiling. We start with the smaller, more controllable tool.
- **SIMD intrinsics (`System.Numerics.Vector<T>`, `System.Runtime.Intrinsics.X86.Sse2`, `Avx2`, `Arm.AdvSimd`).** These are an order of magnitude more complex than spans and produce the next order of magnitude of speedup on numeric code. We mention them in resources and leave the hands-on for an elective week.
- **The Hardware Intrinsics for cryptography, image processing, JSON parsing.** Same reason as SIMD — large topic, narrow audience.
- **Native interop with `[DllImport]` and `LibraryImport`.** Performance-relevant when you cross the boundary; we save it for the systems-integration week.
- **JIT-tier inspection with `DOTNET_JitDisasm` or `dotnet-counters` for the JIT counters.** Useful, but the BDN `[DisassemblyDiagnoser]` covers 90% of the same ground for our purposes and we will demo it in passing.
- **Memory allocators other than the default workstation GC.** The server GC, the concurrent GC modes, the `GCSettings.LatencyMode` knob, the LOH compaction setting — all valid. Week 13 (capstone hardening) covers them in production context.

The point of Week 7 is a sharp, narrow tool: write benchmarks the .NET team would respect, write spans the way Stephen Toub writes them, write `ArrayPool` and `ValueTask` code the way Adam Sitnik writes it, and finish the week with a 10× rewrite you can defend with numbers.

## Stretch goals

If you finish the regular work early and want to push further:

- Read the **`dotnet/runtime` performance issues** tagged `tenet-performance`: <https://github.com/dotnet/runtime/issues?q=is%3Aissue+label%3Atenet-performance>. Pick one closed issue from the last year, read the PR that closed it, and reproduce the BDN result locally if the issue includes a benchmark.
- Skim **Stephen Toub's "Performance Improvements in .NET 9"** post on DevBlogs (<https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-9/>). It is ~150 pages long when printed and every paragraph is a case study in BDN-driven engineering.
- Read **the source of `System.Buffers.ArrayPool<T>`** in `dotnet/runtime`: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/TlsOverPerCoreLockedStacksArrayPool.cs>. Note how the pool is per-CPU with thread-local caches.
- Implement **a custom `MemoryPool<T>`** that pools 4 KB buffers and integrates with `System.IO.Pipelines`. This is the recipe used inside Kestrel for HTTP buffer reuse.
- Read **Adam Sitnik's "Span<T> deep dive"** post: <https://adamsitnik.com/Span/>. Sitnik wrote much of BDN and several of the span-related runtime features.
- Skim **the `BenchmarkDotNet` source** for the `[MemoryDiagnoser]` implementation: <https://github.com/dotnet/BenchmarkDotNet/tree/master/src/BenchmarkDotNet.Diagnostics.dotMemory>. Understanding how BDN measures allocations is a small but rewarding exercise.
- Watch **the "Building high performance .NET applications" series** on the .NET YouTube channel (free, no account required). The David Fowler episodes are the canonical material.

## Up next

Continue to **Week 8 — Source Generators and Roslyn Analyzers** once you have shipped Week 7's mini-project with measured numbers. Week 8 takes the "compile-time work over run-time work" idea to its conclusion: instead of reflecting at startup or at first call, generate the equivalent code at compile time, with zero allocations at runtime. The performance habits you build this week — *measure first, allocate less, justify with numbers* — are the habits that make Week 8 land. Source generators only matter because allocations matter, and allocations only matter because measurement says they do.

---

*If you find errors in this material, please open an issue or send a PR. Future learners will thank you.*
