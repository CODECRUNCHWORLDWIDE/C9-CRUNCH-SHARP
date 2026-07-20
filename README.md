# C9 · Crunch Sharp — C# & .NET Engineering

> A 15-week intensive that turns a working Python engineer into a production-ready C# and .NET engineer. We treat C# as what it is in 2026 — a modern, cross-platform, open-source language with a runtime that competes head-to-head with the JVM and Go on backend, edges out Kotlin and Swift in cross-platform mobile work via .NET MAUI, and remains the de facto scripting layer for Unity. You finish able to ship an ASP.NET Core service, a MAUI client, a Blazor admin, and a small Unity gameplay layer — and to talk about the runtime with the people who actually maintain it.

The Sharp sub-brand (amethyst, `#7C3AED`) is the Microsoft-stack track of the Code Crunch academy. It is open-source-first by design: VS Code with the C# Dev Kit and JetBrains Rider Community are the default editors, `dotnet` CLI is the default build tool, and the curriculum runs end-to-end on macOS and Linux. Visual Studio is acknowledged but never required.

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
