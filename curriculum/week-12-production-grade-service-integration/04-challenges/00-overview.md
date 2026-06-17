# Week 12 — Challenges

The challenges are where the week's separate techniques stop being isolated drills and start behaving like a real production service under your hands. The first one asks you to make a single request observable end to end — one trace ID stitched through REST, EF Core, and SignalR and rendered as a waterfall in Jaeger. The second hands you a service that is already broken and asks you to find the bug by disciplined elimination, the way an on-call engineer narrows a search space instead of guessing and recompiling. Both build directly on the exercises and on the ProjectHub host you have been assembling, and both reward reading output carefully over typing quickly.

## Ground Rules

- **Do the exercises first.** Each challenge names its prerequisites in the header; Challenge 1 needs Exercises 1-3, Challenge 2 needs Exercises 1-4. The challenges assume you already have the ProjectHub host wired and will not re-explain the registration code.
- **Read before you change.** These are diagnostic and instrumentation tasks, not feature sprints. The deliverable is as much your written analysis — which span owns the latency, which hypothesis the evidence kills — as it is working code. Form a hypothesis, find the cheap observation that confirms or rules it out, then act.
- **Keep it to the org standard.** Builds come back clean (`0 warnings · 0 errors`), cross-protocol paths produce a single end-to-end trace, and anything you ship has at least one check that would catch a regression. Lean on the community's shared patterns and cite the docs your work relies on, just as the exercise files do.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|------------|-----------|
| 1 | [challenge-01-cross-protocol-trace.md](./challenge-01-cross-protocol-trace.md) | Swap the console exporter for an OTLP exporter pointed at a local Jaeger, drive a REST status-change that writes to Postgres and broadcasts over SignalR, and prove one trace ID spans the inbound HTTP, EF Core `UPDATE`, hand-written application, and broadcast spans in a parent-child waterfall | Advanced | 2 hours |
| 2 | [challenge-02-401-on-the-hub-only.md](./challenge-02-401-on-the-hub-only.md) | Diagnose a seeded cross-protocol auth bug where REST returns 200 but the SignalR negotiate returns 401; produce a written four-hypotheses log that arrives at the missing `OnMessageReceived` query-string hook by elimination, then fix and verify it | Advanced | 2 hours |

## How to Submit (Self-Check)

1. **Meet every acceptance criterion.** Each challenge ends with a numbered acceptance list — the Jaeger trace with four-plus correctly nested spans and the matching log `traceId` for Challenge 1, the four-hypotheses log and all four passing `curl` checks for Challenge 2. Walk the list and confirm each item before you consider yourself done.
2. **Capture the evidence.** Save the artifact the challenge asks for: a Jaeger screenshot of the single trace and a short note on which span owns the latency (Challenge 1), or the written hypotheses log plus the before/after `curl` transcripts (Challenge 2). The evidence is the submission.
3. **Prove it can't silently regress.** Where the challenge offers it (Challenge 2's CI stretch goal, Challenge 1's sampling and slow-span experiments), add the integration test or instrumentation that would catch the failure next time, then re-introduce the fault and confirm the check goes red. "I fixed it" becomes "it cannot break unnoticed."
