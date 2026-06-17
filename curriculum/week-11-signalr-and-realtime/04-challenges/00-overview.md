# Week 11 — Challenges

These challenges take the chat hub you built in the exercises and push it toward production: first scaling it across two SignalR instances behind nginx with a Redis backplane and MessagePack on the wire, then making the client resilient enough that a user's message arrives exactly once across any disconnect. They are longer than the exercises and ask you to make real architectural decisions, measure what you built, and write down the numbers. Budget about two hours each.

## Ground Rules

- Build on your exercise work — Challenge 1 extends the Exercise 2 hub, and Challenge 2 reuses the `SendToRoomIdempotent` method and `DedupeCache` from Exercise 3.
- Measure and write it down. Each challenge expects captured numbers (latency, byte savings, dedupe counts) and a short `PERF.md`-style write-up, not just "it works on my machine."
- Honor the week's contracts: every hub carries `[Authorize]` (or a commented justification), and you can produce the wire format of every endpoint you ship from the browser Frames view or `wscat`.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|-----------:|----------:|
| 1 | [challenge-01-redis-backplane-and-messagepack.md](./challenge-01-redis-backplane-and-messagepack.md) | A two-instance chat topology behind nginx with a Redis backplane and MessagePack, measuring cross-instance latency, wire-byte savings, and Redis-outage behavior | Advanced | 2 hours |
| 2 | [challenge-02-resilient-reconnect-with-replay.md](./challenge-02-resilient-reconnect-with-replay.md) | A client that never loses a user's intent: a persisted outbound queue with idempotency keys, server-side dedupe, exponential backoff, and an abandon-after-N-retries terminal state | Advanced | 2 hours |

## How to Submit (Self-Check)

There is no central grader. For each challenge, verify that:

1. Every acceptance criterion in the challenge file is met — work down its numbered list and confirm each behavior yourself (cross-instance broadcast, the 401/200 negotiate split, the dedupe-on-replay result, the four queue states).
2. You captured the required measurements and wrote them up (latency, MessagePack byte ratio, Redis-outage observations for Challenge 1; the exactly-once and terminal-state behavior for Challenge 2).
3. You have at least one clear Git commit per challenge, and you can demonstrate the running behavior to a peer in the org community.
