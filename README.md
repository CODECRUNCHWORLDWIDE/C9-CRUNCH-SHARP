# C9 · Crunch Sharp — C# & .NET Engineering

> A 15-week intensive that turns a working Python engineer into a production-ready C# and .NET engineer. We treat C# as what it is in 2026 — a modern, cross-platform, open-source language with a runtime that competes head-to-head with the JVM and Go on backend, edges out Kotlin and Swift in cross-platform mobile work via .NET MAUI, and remains the de facto scripting layer for Unity. You finish able to ship an ASP.NET Core service, a MAUI client, a Blazor admin, and a small Unity gameplay layer — and to talk about the runtime with the people who actually maintain it.

The Sharp sub-brand (amethyst, `#7C3AED`) is the Microsoft-stack track of the Code Crunch academy. It is open-source-first by design: VS Code with the C# Dev Kit and JetBrains Rider Community are the default editors, `dotnet` CLI is the default build tool, and the curriculum runs end-to-end on macOS and Linux. Visual Studio is acknowledged but never required.

---

## Standards & equivalency

> C9 stands in for a university's second programming course — the object-oriented one.

**University equivalent.** Programming II / Object-Oriented Programming — `COP 3337`, `CS 106B`, `CS 61B`, `EECS 280`. Coverage: full. The outcome set those sections share — classes and objects, interfaces, deriving and overriding, generics, exceptions, the standard collections and what they cost, unit testing, and one substantial multi-file program the learner designs and defends — is covered here in full, taught in C# 13 on .NET 9. The one thing that does not carry across is Java's own syntax, and that is declared as a gap below.

C9 carries no credit, no transcript entry, no accreditation and no proctored exam. The equivalence is one of **content and skill**: the outcomes an accredited section of that course assesses are assessed here, at the same depth or deeper, with the work visible in a repository. What a registrar records is not something an open repository can give you.

| University outcome | Where this course teaches it | Depth |
| --- | --- | --- |
| Define classes and objects, encapsulate state behind behaviour, and reason about where an instance actually lives | [Week 01](curriculum/week-01-csharp-language-tour/) | same |
| Implement an interface that other code depends on, and derive from a base type to override its members | [Week 03](curriculum/week-03-entity-framework-core/), [Week 06](curriculum/week-06-auth-and-identity/) | same |
| Model a closed hierarchy of related types and dispatch over every case in it | [Week 05](curriculum/week-05-linq-and-functional/) | deeper |
| Write and consume generic types and methods, including a generic collection of the learner's own design | [Week 05](curriculum/week-05-linq-and-functional/), [Week 07](curriculum/week-07-performance-benchmarkdotnet-spans/) | deeper |
| Raise, catch and translate exceptions at a boundary instead of letting the program crash | [Week 02](curriculum/week-02-aspnet-core-minimal-apis/), [Week 04](curriculum/week-04-async-channels-cancellation/), [Week 09](curriculum/week-09-grpc-and-protobuf/) | same |
| Choose among the standard collections and reason about what each one costs | [Week 05](curriculum/week-05-linq-and-functional/), [Week 07](curriculum/week-07-performance-benchmarkdotnet-spans/) | deeper |
| Write unit tests for your own code and run them from the command line | [Week 01](curriculum/week-01-csharp-language-tour/), [Week 12](curriculum/week-12-production-grade-service-integration/) | deeper |
| Build and navigate a multi-file, multi-project program — the solution file, the project file, the dependency graph | [Week 01](curriculum/week-01-csharp-language-tour/), [Week 13](curriculum/week-13-capstone-build/) | deeper |
| Write code that does more than one thing at a time without deadlocking or corrupting shared state | [Week 04](curriculum/week-04-async-channels-cancellation/), [Week 08](curriculum/week-08-async-channels-in-production/) | deeper |
| Complete a substantial program of the learner's own design, and defend it | [Week 13](curriculum/week-13-capstone-build/), [Week 14](curriculum/week-14-capstone-harden/), [Week 15](curriculum/week-15-capstone-deploy-present/) | deeper |
| Write those programs in Java, where a section grades Java syntax | [Week 01](curriculum/week-01-csharp-language-tour/) teaches every one of these constructs in C# 13; no week of C9 uses Java | lighter |

Every row above points at a week that **assigns work** on that outcome — an exercise, a challenge, homework, a quiz item or a mini-project — not a week that merely mentions it.

**The industry bar.** What an employer expects of somebody paid to write C# on .NET, and where this course makes the learner do it.

| What the job expects | Where this course does it |
| --- | --- |
| Work lands as commits in a repository, not as files on a desktop | every homework set opens by telling the learner to work in that week's repository so each problem leaves at least one commit — [`curriculum/week-01-csharp-language-tour/homework.md`](curriculum/week-01-csharp-language-tour/homework.md) |
| The build is clean before you call it done | the zero-warnings promise restated in every week's README, with nullable-reference warnings treated as bugs — [`curriculum/week-01-csharp-language-tour/README.md`](curriculum/week-01-csharp-language-tour/README.md) |
| You diagnose a fault in a service somebody else wrote and that is mostly correct | [`curriculum/week-08-async-channels-in-production/challenges/challenge-01-diagnose-the-deadlock.md`](curriculum/week-08-async-channels-in-production/challenges/challenge-01-diagnose-the-deadlock.md) and [`curriculum/week-12-production-grade-service-integration/challenges/challenge-02-401-on-the-hub-only.md`](curriculum/week-12-production-grade-service-integration/challenges/challenge-02-401-on-the-hub-only.md) |
| You measure before you claim something is faster | [`curriculum/week-07-performance-benchmarkdotnet-spans/lecture-notes/01-measuring-performance-with-benchmarkdotnet.md`](curriculum/week-07-performance-benchmarkdotnet-spans/lecture-notes/01-measuring-performance-with-benchmarkdotnet.md) |
| Tests pass on a machine you cannot log into | [`curriculum/week-13-capstone-build/challenges/challenge-02-green-in-ci-from-clean.md`](curriculum/week-13-capstone-build/challenges/challenge-02-green-in-ci-from-clean.md) |
| One request stays traceable through every protocol it touches | [`curriculum/week-12-production-grade-service-integration/challenges/challenge-01-cross-protocol-trace.md`](curriculum/week-12-production-grade-service-integration/challenges/challenge-01-cross-protocol-trace.md) |
| A schema changes without breaking the clients already deployed against it | [`curriculum/week-09-grpc-and-protobuf/challenges/challenge-02-schema-evolution.md`](curriculum/week-09-grpc-and-protobuf/challenges/challenge-02-schema-evolution.md) |
| The service ships from a pipeline and can be rolled back | [`curriculum/week-15-capstone-deploy-present/lecture-notes/02-github-actions-build-test-publish-deploy.md`](curriculum/week-15-capstone-deploy-present/lecture-notes/02-github-actions-build-test-publish-deploy.md) |
| Somebody who is not you can operate it in the middle of the night | [`curriculum/week-15-capstone-deploy-present/lecture-notes/03-runbook-on-call-and-the-live-demo.md`](curriculum/week-15-capstone-deploy-present/lecture-notes/03-runbook-on-call-and-the-live-demo.md) |

**Beyond both bars.** Clearing the two floors is entry, not success. Open any of these and check in under a minute.

| What we add | Which bar it beats | Where it lives |
| --- | --- | --- |
| The contract is proven across languages, not asserted: a Python client generated from the identical `.proto` calls the C# server on all four gRPC call types | university | [`curriculum/week-09-grpc-and-protobuf/challenges/challenge-01-cross-language-client.md`](curriculum/week-09-grpc-and-protobuf/challenges/challenge-01-cross-language-client.md) |
| From Week 7 on, every exercise set publishes annotated reference solutions beside the prompts, including the benchmark tables the learner is expected to reproduce | both | [`curriculum/week-07-performance-benchmarkdotnet-spans/exercises/SOLUTIONS.md`](curriculum/week-07-performance-benchmarkdotnet-spans/exercises/SOLUTIONS.md) |
| A cross-tenant data leak planted in a working codebase, to be proven, closed at the structural layer so it cannot be reintroduced, and pinned by a multi-tenant test | both | [`curriculum/week-14-capstone-harden/challenges/challenge-01-prove-and-close-the-cross-tenant-leak.md`](curriculum/week-14-capstone-harden/challenges/challenge-01-prove-and-close-the-cross-tenant-leak.md) |
| Green on a cold machine: the integration baseline is deliberately broken three ways on a continuous-integration runner and repaired, so the failures are seen before they ambush anybody | industry | [`curriculum/week-13-capstone-build/challenges/challenge-02-green-in-ci-from-clean.md`](curriculum/week-13-capstone-build/challenges/challenge-02-green-in-ci-from-clean.md) |
| A latency spike on a dashboard that links straight to the trace that caused it, through an OpenTelemetry exemplar | industry | [`curriculum/week-14-capstone-harden/challenges/challenge-02-exemplar-spike-to-trace.md`](curriculum/week-14-capstone-harden/challenges/challenge-02-exemplar-spike-to-trace.md) |
| A zero-downtime rollout and a one-command rollback, watched by a load generator counting dropped requests | industry | [`curriculum/week-15-capstone-deploy-present/challenges/challenge-02-zero-downtime-deploy-and-rollback.md`](curriculum/week-15-capstone-deploy-present/challenges/challenge-02-zero-downtime-deploy-and-rollback.md) |
| Every week ends with a quiz that carries its own answer key in the same file, so nothing is withheld until a deadline | both | [`curriculum/week-01-csharp-language-tour/quiz.md`](curriculum/week-01-csharp-language-tour/quiz.md) |

**Gaps we declare.** Java. Every program in C9 is written in C# 13 on .NET 9, so a section that grades Java syntax — package declarations, checked exceptions, `ArrayList<E>`, `Comparable<T>` — will find none of it here; the concepts transfer, the keystrokes do not. Two smaller limits, stated plainly rather than glossed. First, published worked answers begin at Week 7: the exercise indexes for Weeks 1 to 6 say outright that no solutions are checked in, so those weeks rest on the quiz answer keys and the acceptance criteria in each homework problem instead. Second, C9 does not implement the classical data structures from scratch or analyse their asymptotic cost — that work belongs to [C2 · CrunchTime](https://github.com/CODECRUNCHWORLDWIDE/C2-CrunchTime-The-Code) and C13 · Hack the Interview, and C9 does not claim it.

---

## Who this is for

**Persona 1 — The Python engineer who needs a second language.**
You shipped Flask, FastAPI, or Django at work. You can read async code. You want a statically typed language with first-class tooling, and your shortlist is C#, Go, or Kotlin. You picked C# because the runtime story is cleaner and EF Core is one of the best ORMs ever written.

**Persona 2 — The bootcamp graduate targeting Microsoft-stack employers.**
Insurance, banking, healthcare, defense, and a large fraction of mid-market SaaS run on .NET. You finished C1 (or an equivalent Python intro) and you want a credential that opens those doors without spending eight months on JavaScript bootcamps that train you for a saturated market.

**Persona 3 — The Unity gameplay programmer who never learned modern .NET.**
You write `MonoBehaviour` scripts every day. You have never touched async/await, never used LINQ deliberately, never written a unit test, and never deployed a backend. C9 closes that gap so you can build the server your game has always needed and bring the C# you already know into the modern runtime.

**Persona 4 — The backend engineer building a cross-platform mobile app.**
You ship a REST API in Node, Python, or Go today, and you need a single mobile codebase for iOS and Android without the React Native tax. MAUI plus a shared ASP.NET Core backend is the most efficient stack on the market for a small team, and you want to learn it deliberately.

---

## What you can do at the end

1. Design and ship an ASP.NET Core 9 Minimal API with EF Core, dependency injection, and a real authentication layer (ASP.NET Identity plus JWT or OIDC).
2. Write idiomatic modern C#: records, pattern matching, nullable reference types, `required` members, primary constructors, collection expressions.
3. Use async/await correctly — including `ValueTask`, `IAsyncEnumerable`, `Channel<T>`, and cancellation tokens — without deadlocking on `.Result`.
4. Model a domain with LINQ over both `IEnumerable<T>` and `IQueryable<T>`, and know which one you are in at any given moment.
5. Build cross-platform mobile and desktop clients with .NET MAUI, sharing a typed contract with the backend.
6. Write tests that actually catch regressions — xUnit, FluentAssertions, NSubstitute, and full integration tests over `WebApplicationFactory<T>`.
7. Profile and optimize hot paths using `Span<T>`, `Memory<T>`, `ArrayPool<T>`, and Native AOT publish targets.
8. Stand up gRPC services (and gRPC-Web for browser clients), and consume them from MAUI and Blazor.
9. Containerize a .NET service with multi-stage Docker builds and deploy it to Azure Container Apps free tier (or to Fly.io, or Linode, or anywhere a Linux container runs) using a GitHub Actions pipeline.
10. Drop into a Unity gameplay codebase and contribute idiomatic, testable scripts that respect the engine's component model.

---

## Prerequisites

- **C1 · Code Crunch Convos** or equivalent Python fluency. You should be comfortable with functions, classes, generators, exceptions, and basic data structures.
- A laptop running macOS, Linux, or Windows that can run the `.NET 9 SDK` and Docker Desktop (or `colima` / `podman`).
- A GitHub account and basic Git fluency.
- **You do not need any prior C# exposure.** Week 1 starts at the type system.

---

## Program at a glance

| Phase | Weeks | Theme | Anchor deliverables |
|------|------|------|---------------------|
| 1 — Foundations | 1–4 | The language, the runtime, and the standard library | Console tools, unit-tested library, async pipeline |
| 2 — Backend & Data | 5–8 | ASP.NET Core, EF Core, auth, real-time | Minimal API + EF Core service with auth and SignalR |
| 3 — Cross-platform & Performance | 9–12 | MAUI, Blazor, gRPC, perf tuning, source generators | MAUI mobile client + Blazor admin, both on gRPC |
| 4 — Capstone | 13–15 | Polyglot Workshop integration, deploy, harden | One deployed system, three clients, one contract |

---

## Weekly cadence

Each week of C9 follows the same rhythm so that learners can plan their lives around it.

- **Mon — Lecture (2 h).** Concept-first. The lecture frames the week's question. Recorded.
- **Tue — Lab (3 h).** Guided, paired exercise. The lab teaches you the mechanics.
- **Wed — Reading + quiz (2 h).** Primary sources — Microsoft Learn, `dotnet/runtime` issues, Stephen Toub posts, library author blogs.
- **Thu — Mini-project work (4 h).** You build the week's deliverable.
- **Fri — Review + critique (2 h).** Code review with a peer and an instructor. You read other people's code as much as you write your own.
- **Weekend — Stretch (optional, 4 h).** Extension challenges, open-source contribution time, or interview prep for learners on the C13 track.

That is roughly 17 hours of structured work per week. The full-time path adds independent reading, side challenges, and the capstone block in phase 4 — bringing the total to ~540 hours over 15 weeks.

---

## Recommended pre/post tracks

- **Pre:** C1 · Code Crunch Convos (Python fundamentals) is the assumed prerequisite. C8 · Crunch Labs Web Dev is a useful but optional companion if you want HTML/CSS context for the Blazor and MAUI Blazor Hybrid weeks.
- **Post (interview path):** C13 · Hack the Interview — pairs naturally with C9, since most C#/.NET shops still run a classical algorithms + system design loop.
- **Post (backend path):** C16 · Crunch Pro Web Backend covers the Python equivalent stack; many graduates go on to C16 to be polyglot, or directly into industry.
- **Post (game-dev path):** C11 · Crunch Arcade and C12 · Crunch 3D pick up where C9's Unity intro leaves off — same language, completely different discipline.
- **Sibling:** C17 · Crunch Pro Python Advanced is the closest analog if you ever want to compare the two ecosystems on the same problems.

---

## License

This curriculum is published under **GPL-3.0**. See `LICENSE`. Contributions are welcomed under the same terms; see the org-level `CONTRIBUTING.md`.

## Maintainers

- Track lead: TBD (Code Crunch Worldwide — Sharp)
- Founding contributor: Code Crunch Club
- Open issues, PRs, and curriculum proposals against this repository directly.
