# Challenge 2 — Design and bench a custom collection: `StackList<T>`

> **Estimated time:** 90 minutes. This is design work first, benchmark work second; do not skip the API design step.

A common pattern in performance-sensitive code is "I need a small, growable, integer-indexed collection that disappears at the end of this method." `List<T>` works but allocates the list object plus its backing array. `Span<T>` works but is fixed-size. The runtime team's answer is **inline arrays** (a C# 12 feature) wrapped in a `ref struct` "stack list" type — a small struct that lives entirely on the stack, holds up to N elements inline, and supports `Add`, indexing, and `Count`.

In this challenge you design a `StackList<T>` with a fixed capacity of 16, implement it, and benchmark it against `List<T>` and `Span<T>` on three workloads. The point is not to beat `List<T>` on every benchmark — `List<T>` is excellent. The point is to *learn the design space* by building one and measuring it.

## API design (your first task — do this on paper before writing code)

Sketch the API in 5–10 lines of pseudocode. The methods you must support are:

- `Add(T item)` — append to the end. Throws if `Count == Capacity`.
- `this[int index]` — read/write the element at `index`. Throws `IndexOutOfRangeException` on out-of-bounds.
- `Count` — number of elements currently in the list.
- `Capacity` — fixed at 16.
- `Clear()` — set `Count = 0`. Optionally zero the underlying buffer.
- `AsSpan()` — return a `Span<T>` over the populated portion.

The struct should be:

- A `ref struct` (so it can hold a `Span<T>` field over the inline storage).
- Constructible from a `Span<T>` provided by the caller (`StackList<T>(Span<T> backing)`), so the caller chooses where the storage lives — usually `stackalloc T[16]`.
- Zero-allocation for all of its methods. Every method returns without touching the heap.

## Suggested skeleton

```csharp
public ref struct StackList<T>
{
    private readonly Span<T> _buffer;
    private int _count;

    public StackList(Span<T> backing)
    {
        _buffer = backing;
        _count = 0;
    }

    public int Count => _count;
    public int Capacity => _buffer.Length;

    public void Add(T item)
    {
        if (_count == _buffer.Length)
        {
            throw new InvalidOperationException("StackList is at capacity.");
        }
        _buffer[_count++] = item;
    }

    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new IndexOutOfRangeException();
            }
            return ref _buffer[index];
        }
    }

    public void Clear() => _count = 0;

    public Span<T> AsSpan() => _buffer[.._count];
}
```

Notes on the design:

- The indexer returns `ref T` so callers can mutate elements in place (`list[2] = newValue` works because `ref T` permits assignment-into).
- `(uint)index >= (uint)_count` is the standard single-comparison bounds-check trick. A negative `int` cast to `uint` becomes a very large number, which fails the `>=` check just as out-of-range positives do.
- `Clear` does not zero the buffer; reuse of the buffer's storage by the next `Add` overwrites the old values. If `T` contains a managed reference (e.g., `T` is `string`), zeroing matters for GC pressure — older elements are still rooted by the buffer. Add `_buffer[.._count].Clear()` if `T` is a reference type, or be explicit about the constraint (`where T : unmanaged`).

## The three workloads to benchmark

Each workload is a method that builds a 16-element collection of `int`, computes the sum, and returns it. The three implementations all produce the same answer; we measure their cost.

```csharp
[MemoryDiagnoser]
public class StackListBenchmark
{
    [Benchmark(Baseline = true)]
    public int WithList()
    {
        var list = new List<int>(capacity: 16);
        for (int i = 0; i < 16; i++) list.Add(i * i);
        int sum = 0;
        for (int i = 0; i < list.Count; i++) sum += list[i];
        return sum;
    }

    [Benchmark]
    public int WithSpan()
    {
        Span<int> span = stackalloc int[16];
        for (int i = 0; i < 16; i++) span[i] = i * i;
        int sum = 0;
        for (int i = 0; i < span.Length; i++) sum += span[i];
        return sum;
    }

    [Benchmark]
    public int WithStackList()
    {
        Span<int> backing = stackalloc int[16];
        var list = new StackList<int>(backing);
        for (int i = 0; i < 16; i++) list.Add(i * i);
        int sum = 0;
        for (int i = 0; i < list.Count; i++) sum += list[i];
        return sum;
    }
}
```

Run BDN. The expected numbers (Apple M2):

```
| Method        | Mean      | Allocated |
|-------------- |----------:|----------:|
| WithList      | 38.41 ns  |     112 B |
| WithSpan      |  9.83 ns  |       0 B |
| WithStackList | 11.24 ns  |       0 B |
```

The `Span<T>` baseline is the lower bound — direct stack memory, direct indexing, no Add-tracking overhead. `StackList<T>` adds a small Count-tracking cost (~1.5 ns per call here) for the convenience of `Add` semantics. `List<T>` is dramatically slower because of the heap allocation of the list object + its backing array (~112 B per call).

The point of the benchmark is **not** "use `StackList<T>` everywhere." The point is to see the design space:

- For 16 known elements, `Span<int>` + manual indexing is fastest.
- For 16 elements that you `Add` one at a time, `StackList<int>` is essentially free of overhead.
- For an unknown number of elements, or for any number > 16, `List<T>` is the right tool.

## Acceptance criteria

- [ ] `StackList<T>` is a `ref struct` with the API above.
- [ ] All `StackList<T>` methods are zero-allocation (BDN confirms 0 B).
- [ ] The benchmark file runs three rows: `WithList`, `WithSpan`, `WithStackList`.
- [ ] `WithStackList` is within 2× of `WithSpan` in mean time.
- [ ] `WithStackList` is faster than `WithList` by ≥ 2× in mean time and allocates 0 B vs 112 B.
- [ ] `dotnet run -c Release` succeeds with 0 warnings, 0 errors.
- [ ] You write a 300-word `results.md` covering:
  - The before/after BDN table.
  - A short paragraph explaining why `WithList` allocates 112 B (object header + backing array).
  - A short paragraph explaining why `StackList<T>` cannot be a field of a `class`.
  - One paragraph on where `StackList<T>` would *not* be the right tool (size-unbounded inputs, async methods, public API surfaces).

## Going further

- **Variable capacity.** Make `StackList<T>` accept a `Span<T>` of any length, not fixed at 16. Bench it at capacities 4, 16, 64, 256 and see where `stackalloc` becomes risky.
- **`Capacity > 1 KB` rejection.** Throw at construction if `backing.Length * sizeof(T) > 1024` to enforce the stack-safety rule. Better: take the backing array from `ArrayPool<T>.Shared.Rent` inside the constructor and `Return` in a `Dispose` method on the struct (yes, `ref struct` supports `Dispose` via the `using` statement since C# 8).
- **Inline arrays.** C# 12 added inline array struct types. Read <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-12.0/inline-arrays> and consider replacing the `Span<T>` field with an inline `T[16]` field. This makes the struct fully self-contained and removes the "caller provides backing storage" awkwardness. Bench the result.
- **Sum/Min/Max specialisations.** Add `Sum()`, `Min()`, `Max()` methods on `StackList<int>` (via an extension method, since structs cannot have specialised generic methods for a single type without trickery). Compare against `list.Sum()` (LINQ) — the LINQ version allocates an enumerator on `List<int>`. The for-loop form does not.
- **Disassembly.** Add `[DisassemblyDiagnoser(maxDepth: 2)]` and confirm that the JIT inlined the `Add` and indexer calls. Look for `call` instructions inside the loop — there should be very few.

## What you are *not* allowed to do

- **Use `unsafe` or `fixed` keywords.** This challenge is solvable with managed `Span<T>` + `ref T` only. Resist the urge to drop to pointers.
- **Use `Unsafe.Add<T>` from `System.Runtime.CompilerServices.Unsafe`.** Same reason — the managed indexer is fast enough and is type-safe.
- **Use any external NuGet package other than `BenchmarkDotNet`.** No `CommunityToolkit.HighPerformance`, no `ZeroAllocJobScheduler`, no other utility libraries.

## Submission

Commit to your Week 7 GitHub repository at `challenges/challenge-02-stack-list/` containing:

- `src/StackList/StackList.cs` — the `ref struct`.
- `src/StackList/Program.cs` — the benchmark.
- `src/StackList/StackList.csproj` — the project file.
- `src/StackList/results.md` — the BDN table and 300-word writeup.

---

**References**

- Microsoft Learn — `ref struct`: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/ref-struct>
- Microsoft Learn — inline arrays (C# 12): <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-12.0/inline-arrays>
- Microsoft Learn — `List<T>` source-level guide: <https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1>
- `dotnet/runtime` — `List<T>` implementation: <https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs>
- Adam Sitnik — "ValueStringBuilder" (the same pattern applied to a string builder): <https://adamsitnik.com/Span-Performance/>
