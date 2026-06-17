# Challenge 1 — Diagnose the four canonical async deadlocks

> Four small programs are described below. Each contains exactly one of the canonical async deadlocks from Lecture 1.8. For each program: (a) predict the symptom from reading the code, (b) run it and confirm the symptom, (c) explain in one paragraph *why* the deadlock occurs, citing the relevant section of the lecture, (d) fix the program with the minimum change.

**Estimated time:** 2 hours.

## Setup

Create one directory per program. Each is a single-file console app on `net8.0`:

```bash
for n in deadlock-1 deadlock-2 deadlock-3 deadlock-4; do
  mkdir $n && cd $n
  dotnet new console -n App -o . --framework net8.0
  cd ..
done
```

Replace each `Program.cs` with the corresponding code below. Build and run each in turn.

## Program 1 — `.Result` with a custom SyncCtx

```csharp
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class PumpCtx : SynchronizationContext
{
    private readonly BlockingCollection<Action> _q = new();
    public PumpCtx() { new Thread(Pump) { IsBackground = true }.Start(); }
    public override void Post(SendOrPostCallback d, object? s) => _q.Add(() => d(s));
    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var a in _q.GetConsumingEnumerable()) a();
    }
    public void RunOn(Action a) => _q.Add(a);
}

public static class Program
{
    public static void Main()
    {
        var ctx = new PumpCtx();
        ctx.RunOn(() =>
        {
            int n = ComputeAsync().Result;            // (A) what happens here?
            Console.WriteLine($"got {n}");
        });
        Thread.Sleep(3_000);
        Console.WriteLine("3 seconds elapsed.");
    }

    private static async Task<int> ComputeAsync()
    {
        await Task.Delay(100);
        return 42;
    }
}
```

**Expected output:** `3 seconds elapsed.` and nothing else. The "got 42" never prints.

**Your task:** (a) predict that, then run, then (b) explain why `ComputeAsync().Result` deadlocks here, (c) cite the section of Lecture 1.8 that names this case, (d) fix with the minimum change (one line).

**Minimum fix:** add `.ConfigureAwait(false)` inside `ComputeAsync` *or* rewrite the `RunOn` lambda to be `async () => { ... await ComputeAsync(); ... }` and remove `.Result`. Both work; the second is the senior-correct fix because it removes the sync-over-async entirely.

## Program 2 — `.Wait()` in an ASP.NET-Classic-style request handler

```csharp
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class RequestCtx : SynchronizationContext
{
    private readonly BlockingCollection<Action> _q = new();
    public RequestCtx() { new Thread(Pump) { IsBackground = true }.Start(); }
    public override void Post(SendOrPostCallback d, object? s) => _q.Add(() => d(s));
    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var a in _q.GetConsumingEnumerable()) a();
    }
    public void RunRequest(Action handler) => _q.Add(handler);
}

public static class Program
{
    public static void Main()
    {
        var ctx = new RequestCtx();
        ctx.RunRequest(() =>
        {
            // Simulates a legacy ASP.NET Classic synchronous handler.
            var t = LoadUserAsync(42);
            t.Wait();                                  // (B) what happens here?
            Console.WriteLine($"got user {t.Result}");
        });
        Thread.Sleep(3_000);
        Console.WriteLine("3 seconds elapsed.");
    }

    private static async Task<string> LoadUserAsync(int id)
    {
        await Task.Delay(100);
        return $"user-{id}";
    }
}
```

**Expected output:** same as program 1 — the handler hangs, only `3 seconds elapsed.` prints.

**Your task:** show this is structurally identical to program 1 (same captured-context deadlock; the SyncCtx subclass is what changes). Cite Lecture 1.8, deadlock case 2. The minimum fix is again "add `.ConfigureAwait(false)` inside `LoadUserAsync`" — but the *senior* fix is "delete the synchronous handler shape entirely and make the entire request pipeline async."

## Program 3 — Library code that captures the caller's context

```csharp
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public static class DataLibrary
{
    // (C) This library author forgot ConfigureAwait(false). What is the cost?
    public static async Task<string> GetAsync(string url)
    {
        await Task.Delay(100);                          // captures caller ctx
        await Task.Delay(100);                          // captures caller ctx
        return $"data:{url}";
    }

    // A "convenience" sync wrapper. Always wrong; the library author wrote it
    // because "some callers can't be made async." This is the trap.
    public static string GetSync(string url)
    {
        return GetAsync(url).Result;
    }
}

public sealed class UICtx : SynchronizationContext
{
    private readonly BlockingCollection<Action> _q = new();
    public UICtx() { new Thread(Pump) { IsBackground = true }.Start(); }
    public override void Post(SendOrPostCallback d, object? s) => _q.Add(() => d(s));
    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var a in _q.GetConsumingEnumerable()) a();
    }
    public void RunOnUI(Action a) => _q.Add(a);
}

public static class Program
{
    public static void Main()
    {
        var ctx = new UICtx();
        ctx.RunOnUI(() =>
        {
            string data = DataLibrary.GetSync("https://example.com");  // hangs
            Console.WriteLine($"got: {data}");
        });
        Thread.Sleep(3_000);
        Console.WriteLine("3 seconds elapsed.");
    }
}
```

**Expected output:** the "got" line never prints.

**Your task:** identify which fix the library author should ship. Two options:

- **Option L1 (defensive library):** add `.ConfigureAwait(false)` to every `await` in `GetAsync`. Now `GetSync` works (the callbacks no longer require the UI context). But `GetSync` is still terrible — it blocks a thread.
- **Option L2 (correct library):** delete `GetSync` entirely. Document that the library is async-only.

Both are correct; the L2 fix is canonical. Cite the third deadlock case in Lecture 1.8 and Stephen Toub's "ConfigureAwait FAQ" (linked in `resources.md`).

## Program 4 — `lock` + `await` reentrancy (won't compile; explain why)

```csharp
#nullable enable
using System;
using System.Threading.Tasks;

public sealed class Wallet
{
    private readonly object _lock = new();
    private int _balance = 100;

    public async Task TransferAsync(int amount)
    {
        lock (_lock)                                    // (D) why does this fail?
        {
            _balance -= amount;
            await PersistAsync(amount);                 // CS1996
        }
    }

    private async Task PersistAsync(int amount)
    {
        await Task.Delay(10);
    }
}

public static class Program
{
    public static async Task Main()
    {
        var w = new Wallet();
        await w.TransferAsync(10);
    }
}
```

**Expected:** `error CS1996: Cannot await in the body of a lock statement`.

**Your task:** (a) note the compile error code, (b) explain in one paragraph why the language rule exists (continuations may run on a different thread; `Monitor` requires the releasing thread to be the acquiring thread), (c) fix using `SemaphoreSlim(1, 1)` and explain why `SemaphoreSlim.WaitAsync()` is the async-friendly equivalent.

**Minimum fix:**

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);

public async Task TransferAsync(int amount)
{
    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        _balance -= amount;
        await PersistAsync(amount).ConfigureAwait(false);
    }
    finally
    {
        _gate.Release();
    }
}
```

## Submission

Submit a single Markdown file `deadlocks-report.md` with the following structure:

```markdown
# Deadlocks report — <your name>

## Program 1: .Result with custom SyncCtx
- Symptom observed: ...
- Why: ...  (cite Lecture 1.8 case 1)
- Minimum fix: ...
- Senior-correct fix: ...

## Program 2: .Wait() in request-pinned context
- Symptom observed: ...
- Why: ...  (cite Lecture 1.8 case 2)
- Minimum fix: ...
- Senior-correct fix: ...

## Program 3: Library captures caller context
- Symptom observed: ...
- Why: ...  (cite Lecture 1.8 case 3)
- Library-author fix L1: ...
- Library-author fix L2 (recommended): ...

## Program 4: lock + await
- Compile error: CS????
- Why: ...  (cite Lecture 1.8 case 4)
- Fix using SemaphoreSlim: <paste your code>
```

## Grading rubric (10 pts)

| Criterion | Points |
|---|---|
| All four programs reproduce the predicted symptom | 2 |
| Each program's "why" cites Lecture 1.8 by case number | 2 |
| Each program's minimum fix is one line or one structural rewrite | 2 |
| Program 3 distinguishes L1 (defensive) from L2 (senior-correct) | 2 |
| Program 4's SemaphoreSlim fix uses `try / finally` and `ConfigureAwait(false)` | 1 |
| Report is editorially sober and reads like a postmortem | 1 |
| **Total** | **10** |
