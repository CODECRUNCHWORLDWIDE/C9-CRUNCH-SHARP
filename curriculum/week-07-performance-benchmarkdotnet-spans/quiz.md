# Week 7 — Quiz

Ten multiple-choice questions. Take it with your lecture notes closed. Aim for 9/10 before moving to Week 8. Answer key at the bottom — don't peek.

---

**Q1.** You run a `Stopwatch`-based microbenchmark of a method called one million times in a `for` loop. The reported mean is 0.0 ms because `ElapsedMilliseconds` rounds down. You switch to `ElapsedTicks` and divide by `Stopwatch.Frequency`. The number is now meaningful but still suspicious. Which of the following is **not** a reason the `Stopwatch` measurement is less reliable than a BenchmarkDotNet measurement?

- A) The first few thousand calls run under Tier-0 JIT; the rest run under Tier-1 JIT. Averaging across them distorts the steady-state mean.
- B) A gen-0 GC may fire in the middle of the loop, adding latency that has nothing to do with the method.
- C) BenchmarkDotNet only supports .NET 9; the `Stopwatch` approach works on every .NET version.
- D) The JIT may dead-code-eliminate a `void` benchmark whose return value is unused.

---

**Q2.** A `[Benchmark]`-decorated method has signature `public void DoWork()`. Inside, the method calls a pure function and discards the result. BenchmarkDotNet reports a mean of 0.3 ns and 0 B allocated. What is the most likely problem?

- A) The reported numbers are correct; the function is just very fast.
- B) The JIT eliminated the entire method body because the result is unused. Return the value from `DoWork` so BDN's consumer can anchor against it.
- C) The function was inlined and counted as zero cost.
- D) `[Benchmark]` requires a `static` method; the instance method was silently skipped.

---

**Q3.** You write:

```csharp
public class BufferOwner
{
    private Span<byte> _buffer;   // CS???? — pick the right error code
}
```

The C# compiler refuses to compile this file. Which compile-error best describes the rule being violated?

- A) `CS0103` — the name `Span` does not exist in the current context.
- B) `CS0246` — type or namespace `Span` cannot be found.
- C) `CS8345` — a field cannot be of a `ref struct` type.
- D) None — `Span<T>` can be a field of a class as long as the class is `sealed`.

---

**Q4.** A method has signature `ParseInt(ReadOnlySpan<char> input, out int value)`. A caller writes:

```csharp
async Task<int> CountAsync(string s, CancellationToken ct)
{
    ReadOnlySpan<char> span = s.AsSpan();
    await Task.Yield();
    if (ParseInt(span, out int v)) return v;
    return -1;
}
```

The C# compiler refuses to compile this. Why?

- A) `ParseInt` is not `async`; cannot call it from an `async` method.
- B) A `ref struct` local (`span`) cannot survive across an `await`. The fix is to derive the span *after* the `await`, or use `ReadOnlyMemory<char>` as the parameter and convert to `Span<char>` inside the synchronous regions only.
- C) `Task<int>` cannot have a `ref struct` in its captured locals.
- D) Both B and C describe the same compile-time rule via different error messages.

---

**Q5.** You write:

```csharp
public static ReadOnlySpan<int> FirstTwo(int x, int y)
{
    Span<int> tmp = stackalloc int[2];
    tmp[0] = x;
    tmp[1] = y;
    return tmp;
}
```

The compiler rejects this. The bug is:

- A) `stackalloc` requires `unsafe` context.
- B) The returned span aliases stack memory that is destroyed when `FirstTwo` returns. The compiler prevents the use-after-free with `CS8175`. The fix is to take a caller-provided `Span<int>` as a parameter and write into it.
- C) `ReadOnlySpan<int>` cannot be returned from any method.
- D) The array size `[2]` must be a `const`.

---

**Q6.** You write:

```csharp
char[] rented = ArrayPool<char>.Shared.Rent(100);
Span<char> chars = rented.AsSpan(0, rented.Length);
// ... write to chars ...
ArrayPool<char>.Shared.Return(rented);
```

What is the most likely correctness bug?

- A) `Return` should be `clearArray: true` to prevent the next renter from reading the chars.
- B) `chars.AsSpan(0, rented.Length)` may exceed the requested 100 chars — the pool may have returned a buffer of length 128 or 256. You should slice `chars = rented.AsSpan(0, 100)` to your actual logical size, and only iterate over the first 100 chars. Treating the pool's actual buffer length as your data length leaks unrelated bytes from previous renters.
- C) `Rent` should be `Rent(100, clearArray: true)`. The clear-on-rent option exists.
- D) The pool was not initialized; you must call `ArrayPool<char>.Create()` first.

---

**Q7.** A method returns `ValueTask<int>`. It completes synchronously 95% of the time. A consumer writes:

```csharp
ValueTask<int> vt = service.GetAsync(key);
int a = await vt;
int b = await vt;   // second await of the same ValueTask
return a + b;
```

What is likely to happen at runtime?

- A) `a` and `b` are both correctly populated; `ValueTask<T>` is reusable.
- B) The second `await` may produce an undefined value, throw, or return the wrong result. `ValueTask<T>` documents that it must be consumed exactly once. The fix is to call `.AsTask()` on the `ValueTask<T>` once at construction (which allocates a `Task<int>` but is reusable) or to call the method twice if you need two values.
- C) The second `await` is a compile-time error.
- D) Both `await` calls return the same cached value at no cost.

---

**Q8.** Consider the four-rule heuristic for choosing `struct` over `class`. Which of the following types is the worst candidate for `struct`?

- A) `Point2D { double X; double Y; }` — 16 bytes, immutable, no inheritance, used as locals. **Good `struct`.**
- B) `Color { byte R; byte G; byte B; byte A; }` — 4 bytes, immutable, no inheritance. **Good `struct`.**
- C) `Person { string FirstName; string LastName; DateOnly DateOfBirth; List<string> Addresses; }` — references, mutable list, often passed across method boundaries. **Bad `struct`.**
- D) `Vector3 { float X; float Y; float Z; }` — 12 bytes, immutable, no inheritance, used in tight loops. **Good `struct`.**

Pick the answer that names the worst candidate and the right reason.

- A) (A) is the worst — 16 bytes is too large.
- B) (C) is the worst — the type holds a `List<string>` (a reference, not value), is essentially mutable through the list, and is typically passed around enough that copy cost dominates. It should be a `class`.
- C) (D) is the worst — `float` requires `unsafe` context.
- D) All four are fine `struct` candidates.

---

**Q9.** A `Where(...).Select(...).ToList()` LINQ chain on a 10,000-element `List<User>` shows 232 B of allocations per call in BDN. You rewrite as a `for` loop, sized list, no lambdas. The BDN table shows 0 B of allocations. Where did the 232 B go?

- A) The 232 B was the final `List<TResult>` — the `for`-loop version also produces a list, so the result must allocate too. The numbers are wrong.
- B) The 232 B included the `Where` iterator (~88 B), the captured lambda closure object (~32 B), the `Select` iterator (~88 B), and small alignment / boxing overhead. The for-loop version captures nothing into a delegate (the `cutoff` is a local) and uses indexed access instead of an enumerator, so all three sources disappear.
- C) The `for` loop is actually allocating 232 B too; BDN is broken.
- D) The 232 B is the pre-sized `List<TResult>` capacity; the rewrite must allocate at least that.

---

**Q10.** You add `[DisassemblyDiagnoser(maxDepth: 2)]` to a benchmark of a simple `for` loop over a `Span<int>`. The disassembly file shows `cmp` + `jae` instructions inside the loop body, before each `mov` that loads `span[i]`. What does this tell you?

- A) The JIT failed to elide the per-iteration bounds check. The loop will pay one comparison per iteration. This is sometimes fine; sometimes the comparison costs you ~15% of the loop time. Investigate whether you can rewrite the loop in the canonical `for (int i = 0; i < span.Length; i++)` form so the JIT recognizes the pattern and elides the check.
- B) The bounds checks are elided; `cmp` + `jae` are unrelated instructions.
- C) `cmp` + `jae` is the loop's increment-and-branch pattern; it has nothing to do with bounds.
- D) Disassembly is unreliable and should not be read.

---

## Answer key

<details>
<summary>Click to reveal answers</summary>

1. **C** — BDN supports every .NET version from .NET Framework 4.6 onward. The other three points (Tier-0/Tier-1 JIT transition, GC interference, dead-code elimination) are real reasons `Stopwatch` is less reliable than BDN.

2. **B** — The JIT may eliminate code with no observable side effect. The fix is to return the result from the benchmark method. BDN's `Consumer` is a sentinel that holds the returned value, which prevents the JIT from concluding the work has no observable effect. Option C is wrong: inlining does not make work disappear from BDN's timing; only dead-code elimination does.

3. **C** — `CS8345` is the precise error: "a field of a `ref struct` type cannot be declared." `Span<T>` is a `ref struct`; a `class` field cannot hold a `ref struct` because a `class` instance is on the heap, and `ref struct` types are stack-only. Option D is wrong: `sealed` has nothing to do with `ref struct` rules.

4. **B** — A `ref struct` local cannot survive across an `await`, `yield`, or `lock`. The compiler error is `CS4012`. The two fixes are (a) derive the span *after* the last `await`, or (b) use `ReadOnlyMemory<char>` as the parameter so the buffer can outlive the stack frame, and convert to `Span<char>` only inside synchronous regions of the method.

5. **B** — `CS8175` is the precise error: "Cannot use a `ref struct` member in a way that captures local state." The span aliases stack memory that is destroyed when the method returns; returning it would be a use-after-free. The standard fix is to invert the API: the caller provides a `Span<T>` parameter for the method to write into.

6. **B** — `Rent(100)` may return a buffer of length 128, 256, or any larger pool bucket. The buffer's full length is its allocated size, but only the first 100 chars are *yours* to write to. The next renter might overwrite the rest. Iterating over `rented.Length` and treating those bytes as "your data" reads stale content. The fix is to slice down to your requested size: `rented.AsSpan(0, 100)`.

7. **B** — `ValueTask<T>` is documented as consume-once: <https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1#remarks>. Awaiting the same `ValueTask<T>` twice produces undefined behavior because the underlying `IValueTaskSource<T>` may have been recycled. To consume multiple times, convert to `Task<T>` once via `.AsTask()` and await the resulting Task as many times as you need.

8. **B** — `Person` is the worst candidate: it holds a `List<string>` (a reference to a mutable object), it is therefore effectively mutable through the list, and it is large enough (24+ bytes) that copy cost is meaningful. The four-rule heuristic — small, short-lived, immutable, no inheritance — fails on three of the four for `Person`. Make it a `class`. The other three types satisfy all four rules and are excellent `struct` candidates.

9. **B** — LINQ allocates an iterator per stage (~88 B each), a closure object for any lambda that captures non-`this` locals (~32 B), and the final result collection (~120 B for the `List<int>`). The for-loop rewrite captures nothing into a delegate (locals are read directly), uses an indexed `for` instead of an enumerator, and pre-sizes the result list, so only the result list allocates. The framework will show that in `Allocated` too; the 0 B in option B is shorthand for "the difference goes away."

10. **A** — `cmp` (compare) followed by `jae` (jump-if-above-or-equal) is the canonical x86 bounds-check pattern: compare the index against the length and jump to a throw-handler if out of range. Seeing these instructions inside the loop means the JIT did **not** elide the bounds check. The fix is usually to use the canonical loop form (`for (int i = 0; i < span.Length; i++)`) and trust the JIT to recognize it. If the JIT still does not elide, you have hit a JIT bug — file an issue at `dotnet/runtime` with a minimal repro.

</details>

---

If you scored under 7, re-read the lectures for the questions you missed. If you scored 9 or 10, you're ready to dive into the [homework](./homework.md).
