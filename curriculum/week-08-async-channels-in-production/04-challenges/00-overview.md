# Week 8 — Challenges

This week's challenges move from "make it run" to "make it fail, then explain why." You will reproduce the canonical async deadlocks and a live ThreadPool starvation incident, diagnose each from the evidence (stack traces, `dotnet-counters` output), and write up the fix the way a senior engineer writes a postmortem. Each one combines several of the week's concepts and runs 90 minutes to a couple of hours — budget real, unhurried time, because async debugging rewards a fresh mind.

## Ground Rules

- Every program is a single-file .NET 8 console or web app; scaffold each with `dotnet new` exactly as the challenge's Setup section describes, and run in Release.
- Honor the week's third contract — no `.Result`, no `.Wait()`, no `.GetAwaiter().GetResult()` in any code you keep; the only place those appear is the deliberately broken samples you are diagnosing.
- Cite your evidence: every "why" should point at the relevant Lecture section by number and, where the challenge asks, the linked source in `resources.md`. The write-up should read like a sober incident report, not a tutorial.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|-----------:|----------:|
| 1 | [challenge-01-diagnose-the-deadlock.md](./challenge-01-diagnose-the-deadlock.md) | Four small programs, each containing one of the canonical async deadlocks (`.Result` under a custom SyncCtx, `.Wait()` in a request-pinned context, a context-capturing library, and `lock` + `await`); predict, reproduce, explain, and apply the minimum fix to each, then write a `deadlocks-report.md` | Advanced | 90–120 min |
| 2 | [challenge-02-threadpool-starvation-from-counters.md](./challenge-02-threadpool-starvation-from-counters.md) | A starvation-prone ASP.NET Core endpoint you load-test, diagnose from the `threadpool-thread-count` / `queue-length` / `completed-items-count` trio plus `dotnet-stack`, and fix two ways (async rewrite vs. raising `MinThreads`), captured in a `starvation-postmortem.md` | Advanced | 90–120 min |

## How to Submit (Self-Check)

1. Each broken program reproduces the predicted symptom, and your fix makes it behave correctly — verified by re-running, not by reasoning alone (the deadlock samples print their "got" line; the starvation fix clears the counter signature).
2. Your report file (`deadlocks-report.md` or `starvation-postmortem.md`) follows the structure given in the challenge and cites each diagnosis against the named Lecture section.
3. You can defend the ordering of your remediations — why the durable fix is the real one and the stopgap is only ever a "keep the process up tonight" measure.
