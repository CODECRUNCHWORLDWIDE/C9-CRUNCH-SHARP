// Exercise 2 — Deferred vs Immediate Execution
//
// Goal: See deferred execution misbehave, then fix it. By the end you should
//       be able to identify a re-enumeration bug at code-review time and know
//       exactly where to drop the .ToList() that resolves it.
//
// Estimated time: 45 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh console project:
//
//      mkdir DeferredVsImmediate && cd DeferredVsImmediate
//      dotnet new console -n DeferredVsImmediate -o src/DeferredVsImmediate
//
//    Replace src/DeferredVsImmediate/Program.cs with the contents of THIS FILE.
//
// 2. Run the code AS-IS first (no edits). Note the OBSERVED OUTPUT below.
//    The pipeline re-enumerates the source three times — you will see the
//    "expensive transform" log print 30 times when it should print 10.
//
// 3. Find the right place to insert ONE .ToList() so the transform runs
//    exactly 10 times (one per source element) while still producing the
//    same final output.
//
// 4. Run again. Confirm the transform log prints 10 times. Confirm the
//    wall-clock total drops from ~3 seconds to ~1 second. Confirm the
//    output is unchanged.
//
// 5. Then: there are TWO other bugs in this file related to deferred
//    execution. Find and fix them. (Hints at the bottom.)
//
// ACCEPTANCE CRITERIA
//
//   [ ] The "expensive transform" line appears exactly 10 times in the output.
//   [ ] Section A's wall-clock total is < 1.5 seconds.
//   [ ] Section B's "ToList re-enumeration bug" actually produces correct output.
//   [ ] Section C's "OrderBy + materialize" pattern is correct.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] You added at most 3 .ToList() / .ToArray() calls across the whole file.
//
// SMOKE OUTPUT (target, after the fix)
//
//   == Section A: Re-enumeration cost ==
//   expensive transform: 1
//   expensive transform: 2
//   expensive transform: 3
//   expensive transform: 4
//   expensive transform: 5
//   expensive transform: 6
//   expensive transform: 7
//   expensive transform: 8
//   expensive transform: 9
//   expensive transform: 10
//   count: 10
//   sum:   550
//   any:   True
//   Wall-clock: ~1000ms  (was ~3000ms before the fix)
//
//   == Section B: ToList does not memoize an IEnumerable ==
//   Materialized first: 1,4,9,16,25
//   Materialized second: 1,4,9,16,25
//
//   == Section C: OrderBy materializes on first MoveNext ==
//   Sorted: 1,2,3,4,5,6,7,8,9,10
//
//   Build succeeded · 0 warnings · 0 errors
//
// Inline hints at the bottom.

using System.Diagnostics;

Console.WriteLine("== Section A: Re-enumeration cost ==");

// The source: integers 1..10.
IEnumerable<int> source = Enumerable.Range(1, 10);

// The "expensive transform" — multiplies each element by 10, with a 100ms
// delay simulating database I/O or HTTP latency. Each call logs to stdout.
static int ExpensiveTransform(int n)
{
    Console.WriteLine($"expensive transform: {n}");
    Thread.Sleep(100);
    return n * 10;
}

var sw = Stopwatch.StartNew();

// THIS IS THE BUG. The pipeline `source.Select(ExpensiveTransform)` is
// deferred. The three calls below (.Count(), .Sum(), .Any()) each re-enumerate
// the pipeline, which means ExpensiveTransform runs 30 times.
//
// TODO: insert ONE materialization call into the pipeline below so that
//       ExpensiveTransform runs exactly 10 times — once per source element.
//       Hint: the right shape is
//           var transformed = source.Select(ExpensiveTransform).??????();
//
//       Then `count = transformed.Count()`, `sum = transformed.Sum()`,
//       `any = transformed.Any()` should each be O(1) over the materialized
//       collection.

var transformed = source.Select(ExpensiveTransform);

var count = transformed.Count();
var sum   = transformed.Sum();
var any   = transformed.Any();

Console.WriteLine($"count: {count}");
Console.WriteLine($"sum:   {sum}");
Console.WriteLine($"any:   {any}");
Console.WriteLine($"Wall-clock: ~{sw.ElapsedMilliseconds}ms  (was ~3000ms before the fix)");

// ---------------------------------------------------------------------------
// Section B: ToList does not memoize an IEnumerable
// ---------------------------------------------------------------------------
//
// A common misconception: that .ToList() on a lazy pipeline somehow makes the
// *pipeline* idempotent. It does not — it materializes ONCE; if you call
// .ToList() again on the same pipeline you get a fresh list and the pipeline
// runs again.
//
// THIS IS THE BUG. Both lines below should print the same five numbers, but
// the SECOND line should NOT re-run the transform.
//
// TODO: change the code so that the transform runs exactly five times total
//       (once per element), even though we print the result twice.
//       Hint: assign the materialized result to a local. Print the local.

Console.WriteLine();
Console.WriteLine("== Section B: ToList does not memoize an IEnumerable ==");

IEnumerable<int> squares = Enumerable.Range(1, 5).Select(n => n * n);

// FIX HERE.
Console.WriteLine("Materialized first: "  + string.Join(",", squares.ToList()));
Console.WriteLine("Materialized second: " + string.Join(",", squares.ToList()));

// ---------------------------------------------------------------------------
// Section C: OrderBy materializes on first MoveNext
// ---------------------------------------------------------------------------
//
// OrderBy looks lazy (it returns IOrderedEnumerable<T>), but it cannot be
// lazy — you cannot sort a stream without reading all of it. The whole input
// is consumed on the first MoveNext, and the entire sorted result is held
// in an internal array until the IOrderedEnumerable<T> is GC'd.
//
// THIS IS THE BUG (subtle). The pipeline below uses a shuffled source and
// `OrderBy` to produce a sorted output. The .Where(...) clause AFTER the
// OrderBy is fine — but the .Select(...) clause BEFORE it forces every
// element through the projection even though we only print 5.
//
// TODO: re-arrange the pipeline so the .Where(...) runs BEFORE OrderBy.
//       Hint: filtering before sorting is a universal speed-up. The .Where
//       removes elements; OrderBy then has fewer to sort.

Console.WriteLine();
Console.WriteLine("== Section C: OrderBy materializes on first MoveNext ==");

int[] shuffled = [7, 2, 9, 4, 1, 8, 3, 6, 5, 10];

// FIX HERE: move .Where(n => n <= 10) so it runs before .OrderBy.
var sorted = shuffled
    .Select(n => { Console.Write("."); return n; })  // intentional side-effect for visibility
    .OrderBy(n => n)
    .Where(n => n <= 10)
    .ToArray();

Console.WriteLine();
Console.WriteLine("Sorted: " + string.Join(",", sorted));

Console.WriteLine();
Console.WriteLine("Build succeeded · 0 warnings · 0 errors");

// ---------------------------------------------------------------------------
// HINTS — read only after you've spent at least 10 minutes on each section.
// ---------------------------------------------------------------------------
//
// Section A fix
//   Insert .ToList() (or .ToArray()) at the end of the pipeline so the
//   transform runs once, then .Count/.Sum/.Any operate on the materialized
//   collection:
//       var transformed = source.Select(ExpensiveTransform).ToList();
//
//   Better: use the List<T> properties directly:
//       var count = transformed.Count;          // List<T>.Count, not LINQ Count()
//       var sum   = transformed.Sum();           // LINQ Sum is O(n); we accept that
//       var any   = transformed.Count > 0;       // even cheaper than .Any()
//
//   But the smallest change that satisfies the acceptance criterion is just
//   the one .ToList() on the pipeline.
//
// Section B fix
//   Assign the materialized list to a local:
//       var squaresList = Enumerable.Range(1, 5).Select(n => n * n).ToList();
//       Console.WriteLine("Materialized first: "  + string.Join(",", squaresList));
//       Console.WriteLine("Materialized second: " + string.Join(",", squaresList));
//
//   Or change the variable's static type at the declaration site:
//       List<int> squares = Enumerable.Range(1, 5).Select(n => n * n).ToList();
//       (Same idea, just expresses the intent through the type.)
//
// Section C fix
//   Move the .Where BEFORE the .OrderBy. The Select stays (for visibility).
//       var sorted = shuffled
//           .Select(n => { Console.Write("."); return n; })
//           .Where(n => n <= 10)
//           .OrderBy(n => n)
//           .ToArray();
//
//   On this input all 10 elements pass the filter, so you'll still see 10
//   dots — that's expected. On a different input where Where eliminates
//   half the elements, OrderBy would only sort what was left.
//
//   For a more dramatic improvement: filter at the source. If `shuffled`
//   was an IQueryable<int> backed by a database, this re-ordering would
//   push the Where clause into a SQL WHERE — saving a round-trip's worth
//   of data.
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// The three bugs in this file are the three deferred-execution bugs that
// show up in production code:
//
//   1. Re-enumeration of an unmaterialized pipeline (Section A). You see
//      this when someone writes a Func<T, IEnumerable<U>> and the caller
//      calls .Count() and .ToList() and .First() on the result without
//      realizing each call re-runs the pipeline.
//
//   2. Misunderstanding that ToList() materializes the *call*, not the
//      *pipeline* (Section B). You see this when someone caches an
//      IEnumerable<T> in a field expecting subsequent reads to be fast.
//
//   3. Filtering after a materializing operator (Section C). You see this
//      when someone composes a pipeline left-to-right ("select, sort, then
//      filter") without realizing that the order matters for performance
//      — sometimes correctness, in the IQueryable<T> case.
//
// In Week 6 these same bugs cost orders of magnitude: re-enumerating an
// IQueryable<T> hits the database every time; calling .ToList() inside a
// loop materializes a full table per iteration; and a .Where after a
// .ToList() forces client-side evaluation on the entire query result. The
// reflex you build today — "where is this pipeline materialized, exactly
// once?" — is what makes EF Core code performant.
//
// ---------------------------------------------------------------------------
