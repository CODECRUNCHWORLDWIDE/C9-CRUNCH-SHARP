# Week 14 — Challenges

The exercises harden one face of the workshop each; these two challenges ask you to harden the whole boundary and then prove you can *operate* it. Challenge 1 is the milestone's security spine: drive every applicable item of the OWASP API Security Top 10 (2023) to a *closed* state — a deny-path integration test plus a row in `THREATMODEL.md` that names the item, the mitigation, and the test — across all three boundaries of the Polyglot Workshop. Challenge 2 is the milestone's observability spine: a peer injects one of three realistic production faults, and you diagnose it cold from Grafana alone — metric to trace to logs, no SSH and no debugger — then write the post-incident note. Together they are the capstone question made concrete: can I trust this system, and can I operate it. Both are project-based and produce something you can put in front of a reviewer.

## Ground Rules

- **A claim without a test is just a hope.** Every mitigation you assert in Challenge 1 needs an integration test that proves the deny path, and every row in `THREATMODEL.md` must name a real test; in Challenge 2, every line of the post-incident note must be something the dashboard told you, with a real trace id, a real PromQL query, and a real Loki line — no placeholders.
- **Diagnose from the signals, not the source.** Challenge 2 is binary: if you reach for the source code before you have a `TraceId` from Grafana, you have not exercised the capability — start over and follow the thread (metric that alarmed → exemplar → trace → logs by `TraceId`).
- **Hardening is editing.** Prefer the structural fix that removes the whole class of bug (an EF Core global query filter, a deny-by-default fallback policy, a DTO allow-list) over a one-off patch, and keep your net diff small — these challenges should leave the codebase tighter, not larger.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|-----------:|----------:|
| 1 | [challenge-01-close-the-owasp-api-top-10.md](./challenge-01-close-the-owasp-api-top-10.md) | A `THREATMODEL.md` covering all three boundaries with STRIDE, a security test class per applicable OWASP API Top 10 item (BOLA, broken auth, BOPLA, resource consumption, BFLA, sensitive flows, SSRF, misconfiguration, inventory, unsafe consumption), and a green CI run that fails if any deny path regresses — plus a 300-word write-up on which item was hardest to close | Advanced | 120 min |
| 2 | [challenge-02-debug-an-incident-from-the-dashboard.md](./challenge-02-debug-an-incident-from-the-dashboard.md) | A cold diagnosis, from Grafana only, of a peer-injected fault (`nplus1`, `poolexhaust`, or `tenantleak`), and a real post-incident note for each fault you diagnose — detection signal with PromQL, trace id, root cause from the `db.statement` evidence, corroborating Loki line, the one-line fix, and the guardrail that would have caught it earlier | Advanced | 120 min |

## How to Submit (Self-Check)

1. **Produce the deliverable in its folder.** Challenge 1 lands in `challenges/01-owasp-closed/` (the `THREATMODEL.md`, the security test classes, the 300-word write-up); Challenge 2 lands in `challenges/02-incident/` (one post-incident note per fault you diagnosed, each with its captured trace id, PromQL, and Loki line, plus a Tempo flame-graph screenshot and the 200-word reflection).
2. **Run the acceptance checks and confirm green.** For Challenge 1, `dotnet test` is green, every applicable OWASP item is closed-with-a-test or explicitly marked N/A with a justification, and a deliberately-introduced regression (remove one resource-based check) actually turns a test red before you restore it. For Challenge 2, your "Detected" and "Trace" lines were obtained before you opened the source, you named the active fault correctly, and you walked at least two of the three faults.
3. **Self-review against the rubric, then share for community feedback.** Reread each challenge's Acceptance Criteria line by line and fix any gap; the hardest-graded lines are Challenge 1's "every row maps to a real test" and Challenge 2's "Guardrail" (the structural fix that beats any single test). When it passes, post the write-up and screenshots for an org peer review — a second pair of eyes on a threat model is exactly the review these artifacts are built for.
