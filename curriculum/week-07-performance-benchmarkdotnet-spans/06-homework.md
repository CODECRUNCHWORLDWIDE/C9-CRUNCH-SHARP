# Week 7 Homework

Six practice problems that revisit the week's topics. The full set should take about **5 hours**. Work in your Week 7 Git repository so each problem produces at least one commit you can point to later.

Each problem includes:

- A short **problem statement**.
- **Acceptance criteria** so you know when you're done.
- A **hint** if you get stuck.
- An **estimated time**.

---

## Problem 1 — Read the BenchmarkDotNet source

**Problem statement.** Open the BDN source on GitHub: <https://github.com/dotnet/BenchmarkDotNet>. Find the file that implements the `[MemoryDiagnoser]` attribute behavior — specifically, the diagnoser class that hooks into the engine and records allocations. Save a 200-word note at `notes/memory-diagnoser.md` explaining:

1. The file you found (path within the repo).
2. The .NET API it uses to read allocation counts (hint: `GC.GetAllocatedBytesForCurrentThread`).
3. Why the diagnoser is *opt-in* (via the `[MemoryDiagnoser]` attribute) rather than always-on.

Then add a code snippet in the note showing the rough shape of the measurement loop (~10 lines of pseudocode).

**Acceptance criteria.**

- `notes/memory-diagnoser.md` exists, is 180–220 words, and cites at least two specific filenames or class names from the BDN repo.
- The note correctly names `GC.GetAllocatedBytesForCurrentThread` (or `GC.GetTotalAllocatedBytes`) as the underlying measurement primitive.
- The note explains the opt-in rationale (measurement overhead — calling `GC.GetAllocatedBytesForCurrentThread` is not free).
- File is committed.

**Hint.** Search the BDN repo for `GetAllocatedBytesForCurrentThread`. The diagnoser is in `src/BenchmarkDotNet/Diagnosers/MemoryDiagnoser.cs`.

**Estimated time.** 30 minutes.

---

## Problem 2 — Bench three integer formatters

**Problem statement.** Bench three ways to format an integer (`1234567`) to a UTF-8 byte sequence:

1. **`i.ToString()` + `Encoding.UTF8.GetBytes(string)`.** The naive form. Two allocations: the string and the byte array.
2. **`i.ToString()` + `Encoding.UTF8.GetBytes(string, Span<byte>)`.** One allocation: the string. The bytes go into a caller-provided buffer.
3. **`Utf8Formatter.TryFormat(i, Span<byte> destination, out int written)`.** No allocations. The integer is formatted directly to UTF-8 bytes.

Set up a BDN benchmark with `[MemoryDiagnoser]`. Report the table.

**Acceptance criteria.**

- BDN run with three rows, with `i.ToString() + GetBytes(string)` as the baseline.
- `Utf8Formatter.TryFormat` row shows `Allocated: 0 B`.
- The table is saved at `notes/integer-format.md` along with a 100-word writeup explaining the differences.
- `dotnet build`: 0 warnings, 0 errors.

**Hint.** `Utf8Formatter.TryFormat` requires a `Span<byte>` destination of sufficient length. For an `int`, 11 bytes is always enough (10 digits + optional minus). Use `stackalloc byte[16]` for the destination.

**Estimated time.** 45 minutes.

---

## Problem 3 — Audit a service for `ValueTask` candidates

**Problem statement.** Take any class library you have written in C9 — your Week 4 background-service code, your Week 5 LINQ-heavy services, or your Week 6 auth handlers. Audit every `public` async method and decide, for each one:

- "Should be `Task<T>`" — because callers may need to combine it with `Task.WhenAll`, because the synchronous-completion path is rare, or because the API surface is widely used and `ValueTask<T>` is a footgun for unknown callers.
- "Should be `ValueTask<T>`" — because the synchronous path is common (cache hit, fast lookup, zero-item case) AND the callers are internal and disciplined.

Write the audit at `notes/valuetask-audit.md` as a Markdown table with columns: `Method`, `Signature today`, `Decision`, `Rationale`. Aim for 5–10 rows.

**Acceptance criteria.**

- `notes/valuetask-audit.md` exists with at least 5 rows in the audit table.
- Each row's rationale is one sentence or less.
- Each "ValueTask candidate" row identifies the specific synchronous path that justifies the change.
- File is committed.

**Hint.** The Stephen Toub post "Understanding the Whys, Whats, and Whens of ValueTask" is the canonical reference for this kind of audit: <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>.

**Estimated time.** 45 minutes.

---

## Problem 4 — Rewrite `string.Split` with `Span<char>`

**Problem statement.** Implement a method `SplitOnce(ReadOnlySpan<char> input, char separator, out ReadOnlySpan<char> head, out ReadOnlySpan<char> tail)` that splits `input` at the first occurrence of `separator`. Return `true` if the separator was found, `false` otherwise. Both `head` and `tail` must be `ReadOnlySpan<char>` slices into the original `input` — no allocations.

Then benchmark it against `input.Split(separator, 2)` on a 50-character input.

**Acceptance criteria.**

- `SplitOnce` signature is exactly as specified.
- BDN run reports `Allocated: 0 B` for `SplitOnce` and ~80 B for the string-based version.
- `SplitOnce` is faster than `string.Split` by ≥ 3×.
- A small XUnit test or `Console.Assert` confirms correctness for at least 3 inputs (no separator, separator at start, separator at end, normal case).

**Hint.** `input.IndexOf(separator)` returns the position, or -1. From there, `head = input[..pos]` and `tail = input[(pos + 1)..]`.

**Estimated time.** 30 minutes.

---

## Problem 5 — Profile and rewrite a LINQ hot path

**Problem statement.** Given the following LINQ chain, profile its allocations with BDN, then rewrite as a `for` loop with no allocations beyond the result list. Report the before/after table.

```csharp
public static List<string> ActiveUserEmails(List<User> users, DateTime cutoff)
{
    return users
        .Where(u => u.IsActive)
        .Where(u => u.CreatedAt < cutoff)
        .Select(u => u.Email)
        .ToList();
}

public sealed record User(string Email, bool IsActive, DateTime CreatedAt);
```

Generate 1,000 test users (alternating active/inactive, half meeting the cutoff). Run BDN. Then rewrite.

**Acceptance criteria.**

- The LINQ form is the baseline. BDN shows ≥ 250 B of intermediate allocations beyond the result list.
- The for-loop form is faster by ≥ 2× and allocates only the result list (no enumerator boxing, no closure objects).
- Both forms return the same number of users (assert this before the benchmark).
- The before/after table and a 100-word writeup live at `notes/linq-rewrite.md`.

**Hint.** The for-loop form indexes the input list, applies both conditions inline (no `Where` calls), and appends matching emails to a pre-sized `List<string>`. The `cutoff` is a local — not a captured closure.

**Estimated time.** 60 minutes.

---

## Problem 6 — Read one `dotnet/runtime` performance issue end-to-end

**Problem statement.** Browse <https://github.com/dotnet/runtime/issues?q=is%3Aissue+label%3Atenet-performance+is%3Aclosed> and pick one closed performance issue from the last 12 months that includes a BDN before/after table in either the issue body or the linked PR. Read the issue, the PR, and at least one of the BDN tables it cites. Write a 250-word summary at `notes/runtime-issue-summary.md` covering:

1. The issue's title and URL.
2. The performance problem in one sentence.
3. The technique used to fix it (span, pool, struct, JIT improvement, etc.).
4. The before/after numbers (paste the BDN table or a subset).
5. Why this is interesting to *you* — one sentence.

**Acceptance criteria.**

- `notes/runtime-issue-summary.md` exists, is 220–280 words, and cites the specific issue URL.
- The summary identifies which of the techniques from Week 7 the fix used.
- The pasted BDN table is real (matches the issue's actual numbers).
- File is committed.

**Hint.** Filter by milestone (`9.0.0` is a recent one with many performance issues) and label (`tenet-performance`). Sort by "most commented" if you want to start with the most-discussed issues.

**Estimated time.** 30 minutes.

---

## Submission

Push the entire `notes/` directory and any benchmark code to your Week 7 Git repository. The instructor reviews by:

1. Reading each note in `notes/`.
2. Re-running any BDN benchmarks attached and verifying the numbers reproduce within ~30% on the reviewer's machine.
3. Cross-checking the cited URLs are real and the claims in the notes are consistent with the source.

A submission whose `notes/` are present and whose BDN runs reproduce is a pass. The most common review-fail is "the note claims X but the linked BDN table shows Y"; double-check before submitting.

If anything is unclear, post the question in the Week 7 channel before the homework deadline.

---

**References**

- BenchmarkDotNet — Diagnosers: <https://benchmarkdotnet.org/articles/configs/diagnosers.html>
- Microsoft Learn — `Utf8Formatter`: <https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.utf8formatter>
- Microsoft Learn — `GC.GetAllocatedBytesForCurrentThread`: <https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread>
- Stephen Toub — "Understanding the Whys, Whats, and Whens of ValueTask": <https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/>
- `dotnet/runtime` — `tenet-performance` issues: <https://github.com/dotnet/runtime/issues?q=is%3Aissue+label%3Atenet-performance>
