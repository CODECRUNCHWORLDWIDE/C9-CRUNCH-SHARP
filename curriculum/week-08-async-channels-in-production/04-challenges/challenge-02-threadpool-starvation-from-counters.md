# Challenge 2 — Diagnose ThreadPool starvation from `dotnet-counters`

> Build a deliberately starvation-prone ASP.NET Core endpoint, load-test it, capture `dotnet-counters` output, identify the starvation signature, propose two remediations in the correct order, and apply the first one. By the end you have a single report file that reads like a real on-call postmortem.

**Estimated time:** 2 hours.

## Setup

```bash
mkdir StarveLab && cd StarveLab
dotnet new web -n StarveLab -o . --framework net8.0
dotnet tool install -g dotnet-counters     # if not already installed
dotnet tool install -g dotnet-stack        # we'll use this for confirmation
```

Replace `Program.cs` with:

```csharp
#nullable enable
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("downstream", c =>
{
    c.BaseAddress = new Uri("http://localhost:5000/");
});
var app = builder.Build();

// The "fast" endpoint: 100ms simulated work, fully async.
app.MapGet("/fast", async (CancellationToken ct) =>
{
    await Task.Delay(100, ct);
    return Results.Ok(new { ok = true });
});

// The "starvation" endpoint: blocks a ThreadPool thread for 200ms via .Result.
// Every concurrent request consumes one ThreadPool thread for the duration.
app.MapGet("/starve", (IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("downstream");
    // BAD: sync-over-async on a ThreadPool thread.
    var resp = http.GetAsync("/fast").GetAwaiter().GetResult();
    var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    return Results.Ok(new { downstream = body });
});

app.Run("http://localhost:5000");
```

Run it: `dotnet run -c Release`. Confirm `/fast` works in a browser.

## Step 1 — Establish a baseline (the fast endpoint)

In a second terminal:

```bash
# A trivial load test (replace with `bombardier`, `k6`, or `wrk` if you have them)
for i in $(seq 1 200); do
  curl -s http://localhost:5000/fast > /dev/null &
done
wait
```

In a third terminal, attach `dotnet-counters`:

```bash
dotnet-counters monitor --process-id $(pgrep -f StarveLab) --counters System.Runtime
```

Capture the steady-state values for `threadpool-thread-count`, `threadpool-queue-length`, and `threadpool-completed-items-count` during and after the load. Write them to your report.

**Expected baseline:** `threadpool-thread-count` stays at ~8 (the default `MinThreads` on most laptops). `threadpool-queue-length` stays at 0. `threadpool-completed-items-count` increases briskly during the load and plateaus afterwards. No starvation.

## Step 2 — Reproduce starvation (the starving endpoint)

Repeat the load test, but against `/starve`:

```bash
for i in $(seq 1 200); do
  curl -s http://localhost:5000/starve > /dev/null &
done
wait
```

Watch `dotnet-counters` during the load.

**Expected starvation signature:**

- `threadpool-thread-count` *grows slowly* — one thread per ~500 ms — eventually reaching 50–200 if the load is sustained.
- `threadpool-queue-length` is non-zero and grows during the burst.
- `threadpool-completed-items-count` increases more slowly than the request arrival rate.
- The load test feels *much* slower than the `/fast` baseline — many requests take seconds to complete.

Record the exact numbers in your report. If the starvation does not reproduce on the first try, increase the concurrent-request count to 500 or 1000.

## Step 3 — Confirm with `dotnet-stack`

While the load is running:

```bash
dotnet-stack report --process-id $(pgrep -f StarveLab) > stack-report.txt
```

Open `stack-report.txt`. Look for stacks that include `.GetAwaiter().GetResult()` or `Wait()`. There should be many — one per blocked request thread. The presence of these stacks confirms the diagnosis: sync-over-async on `ThreadPool` threads.

Paste two example stacks into your report (anonymise paths if needed).

## Step 4 — Propose two remediations

**Remediation A (durable):** Make `/starve` async. Replace the body with:

```csharp
app.MapGet("/starve", async (IHttpClientFactory factory, CancellationToken ct) =>
{
    var http = factory.CreateClient("downstream");
    var resp = await http.GetAsync("/fast", ct);
    var body = await resp.Content.ReadAsStringAsync(ct);
    return Results.Ok(new { downstream = body });
});
```

**Remediation B (stopgap):** Raise `MinThreads` at startup:

```csharp
System.Threading.ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
```

Adding this line before `app.Run(...)` bypasses the throttling. The starvation symptom largely disappears, *but the underlying bug remains*: every request still consumes a thread for the duration of the downstream call. The process now allocates 200 threads regardless of need, wasting memory.

**Apply remediation A.** Confirm with `dotnet-counters` that the starvation signature is gone.

## Step 5 — Write the postmortem

Submit a Markdown file `starvation-postmortem.md` with this structure:

```markdown
# ThreadPool starvation postmortem — <your name>

## Summary

In one paragraph: what happened, what the diagnosis was, what the fix was.

## Timeline

| Time | Event |
|---|---|
| T+0   | Load test started against /starve (200 concurrent requests) |
| T+1s  | ... |

## Symptom (with counter values)

Paste the dotnet-counters output captured at the worst point. Highlight:
- threadpool-thread-count: <growing slowly>
- threadpool-queue-length: <non-zero and growing>
- threadpool-completed-items-count: <slow growth>

## Confirmation (dotnet-stack)

Paste two example blocked stacks. Annotate the `.GetAwaiter().GetResult()` line in each.

## Diagnosis

In two paragraphs: what the trio of counters means, why this is unambiguously
ThreadPool starvation, what the root cause is (sync-over-async on ThreadPool
threads), and why Lecture 3.7 names this exact pattern.

## Remediations considered

### A. Make the endpoint async (durable)
Code diff. Counter values after applying.

### B. Raise MinThreads (stopgap)
Code diff. Counter values after applying. Why this is not a real fix.

## Decision

We applied A. B is the right tool for "the durable fix ships in 24h and we
need to keep the process up tonight."

## Lessons

Three bullet points on what we will do differently next time.
```

## Grading rubric (10 pts)

| Criterion | Points |
|---|---|
| Baseline `dotnet-counters` values captured for /fast | 1 |
| Starvation `dotnet-counters` values captured for /starve | 2 |
| `dotnet-stack` stacks pasted with annotation | 1 |
| Two remediations described correctly, with code diffs | 2 |
| Remediation A applied; counter values confirm the fix | 2 |
| The report reads like a real postmortem (sober, evidence-driven) | 1 |
| Lecture 3.7 cited correctly | 1 |
| **Total** | **10** |

## Notes

- The /starve endpoint above is deliberately bad. Do not copy this pattern into any real code. The point of the exercise is to *recognise* it in code review and *remove* it.
- If you have access to `bombardier` or `k6` or `wrk`, use one instead of the `curl` loop. The signature is clearer with sustained load.
- On Windows, replace `pgrep` with `Get-Process` in PowerShell or just look up the PID in Task Manager.
- The `MinThreads` stopgap is genuinely used in production. The David Fowler diagnostic scenarios (linked in `resources.md`) document the cases where it is appropriate.
