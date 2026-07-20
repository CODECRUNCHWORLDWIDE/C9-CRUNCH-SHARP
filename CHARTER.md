# C9 · Crunch Sharp — Charter

This charter exists to record *why* the C9 track exists, *why* it is shaped the way it is, and *what* relationship it has to its siblings inside the Code Crunch academy. It is the document a future maintainer should read before proposing changes to the syllabus.

---

## Why C# now

C# in 2026 is not the language a Visual-Studio-on-Windows engineer learned in 2010. Three changes in the last decade make the case for teaching it as a first-class track:

1. **The runtime is cross-platform and open-source.** .NET 9 runs on macOS and Linux as a first-class citizen. The runtime, the SDK, the BCL, and the ASP.NET Core stack are all developed in the open on GitHub under the MIT license. A learner with a $300 Linux laptop and zero Microsoft software installed can complete every week of C9.
2. **The language has compounded.** Records, pattern matching, nullable reference types, `required` members, primary constructors, collection expressions, and source generators have turned C# into the most ergonomic statically-typed language a backend engineer can pick up today. The improvements were additive — nothing the learner knows from older C# tutorials has been deprecated underfoot.
3. **Employer demand is durable, not trendy.** Finance, insurance, healthcare, defense, manufacturing, and a large slice of mid-market SaaS run on .NET. The hiring market is less hype-driven than the JavaScript market and less narrow than the Go market. For a Code Crunch learner who wants stable, well-compensated work without learning three frameworks a year, C# is the most efficient bet.

There is also a Code-Crunch-specific reason: our existing tracks have a Python center of gravity (C1, C5, C16, C17). Adding a serious C# track meaningfully widens the kinds of jobs our graduates can pursue without forcing every learner to specialize in JavaScript.

---

## Why we resolved the C11 duplicate this way

The Code Crunch academy uses a flat `C{n}` numbering scheme as a stable, citation-friendly identifier. Two tracks were inadvertently assigned to C11:

- `C11-C-Sharp-Foundations` — a small C# starter that pre-dated the academy's current shape.
- `C11-CRUNCH-ARCADE` — the planned game-development sub-brand, built on Unity, which itself uses C#.

A clean fix needed three properties: (a) preserve C11 for one of the two tracks so we do not break inbound links, (b) give the other track a number that is not already taken, and (c) align numbers with sub-brand identity wherever practical.

C9 was an open slot. **Crunch Arcade** is the natural occupant of C11 because the Arcade sub-brand is the visible game-development line, and the prior `C11-C-Sharp-Foundations` directory was the smaller and less-developed of the two. Moving the C# track to C9 — and renaming it to **Crunch Sharp** to match the academy's sub-brand convention — frees C11 for Arcade without losing any work; the prior directory remains in place with a forwarding `NOTE.md` and any reference material there stays accessible until the C9 weekly content rollout is complete.

This is the cheapest fix that keeps the numbering principle (one number, one track, one sub-brand) intact going forward.

---

## Topic ordering rationale

Three deliberate choices govern the order of the 15 weeks.

**Backend before mobile.** EF Core, ASP.NET Core, and dependency injection are reused in MAUI and Blazor. A learner who has internalized DI lifetimes from the backend phase will pick up MAUI's container in an afternoon. The reverse is not true: starting from MAUI would force us to teach the same primitives twice, once with caveats. The capstone also needs a backend before it needs a client, so the build order matches the dependency order.

**Performance late, not early.** `Span<T>`, `Memory<T>`, `ArrayPool<T>`, and Native AOT only pay off when a learner can already write a correct, idiomatic program. Teaching them early produces "premature `stackalloc`" — clever code that solves a problem the learner does not yet have. Performance comes in week 12, after the learner has shipped working code that gives them something concrete to measure and improve.

**Source generators and analyzers as a brief intro, not a deep dive.** Roslyn is a deep ocean. C9 introduces source generators in the context of `System.Text.Json` and `[GeneratedRegex]` so learners recognize them in the wild and can author small ones. The advanced Roslyn track belongs in a future C# elective, not here.

---

## Open-source-first stance in a Microsoft ecosystem

The .NET ecosystem is anchored by a large vendor. That is a strength — it funds a runtime team that very few open-source projects could sustain — but it is not the right *default* for a teaching curriculum. C9's stance is explicit:

- **Editors.** **VS Code + C# Dev Kit** is the primary editor. **JetBrains Rider Community** is named as the secondary path. Visual Studio is acknowledged when it is genuinely the best tool for a specific task (some Windows-only profiling scenarios), but it is never required to complete a week.
- **Build system.** The **`dotnet` CLI** is the build system. Every project compiles, tests, and runs from a terminal. We never depend on an IDE-specific build configuration.
- **Hosting.** The capstone deploy target is **Azure Container Apps free tier**, chosen because it is genuinely free and because it is the lowest-friction managed container host for a .NET app. The capstone is structured so that it can be redeployed to **Fly.io**, **Linode**, **Hetzner**, or any Linux container host without code changes.
- **Libraries.** We name specific open-source libraries — **Polly**, **MediatR**, **Serilog**, **AutoMapper**, **Refit**, **OpenTelemetry**, **CommunityToolkit.Mvvm**, **MudBlazor**, **Testcontainers for .NET**, **BenchmarkDotNet**, **xUnit**, **FluentAssertions**, **NSubstitute**, **Dapper** — and we teach them by name so that the learner's mental model maps onto the ecosystem they will actually meet at work.
- **License.** All C9 curriculum content ships under **GPL-3.0**.

We do not pretend the Microsoft ecosystem is something other than what it is. We just refuse to teach it in a way that locks a learner into paid Microsoft tooling.

---

## Relationship to C11 (Arcade — Unity) and C16 (Pro Backend — Python)

**C11 · Crunch Arcade** is the sibling that picks up where C9's week-12 Unity intro stops. Unity uses C#, but it uses a constrained subset and a very different program model (the engine drives the loop; you do not). C9 teaches the language and the runtime; C11 teaches the engine. A learner who completes C9 and then C11 has both halves of the modern Unity story — many learners take exactly this sequence.

**C16 · Crunch Pro Web Backend** is the Python equivalent of C9's backend phase. The two tracks are deliberately parallel: a learner can take C9 and C16 to be a credible polyglot backend engineer, or take just one. C9's gRPC week and EF Core week have direct analogs in C16 (gRPC over Python and SQLAlchemy 2.0), and the two are designed so that a learner who has taken both can compare them on the same problems.

**C13 · Hack the Interview** is the natural post-track for the interview-bound C9 graduate. C9 produces engineers who can ship; C13 produces engineers who can pass an algorithms loop. Most .NET shops still run a classical loop, so the pairing matters.

**C17 · Crunch Pro Python Advanced** is the further-along Python sibling. Anyone considering C9 should also look at C17 to decide which deep specialization fits their goals.

---

## Sign-off

This charter is approved as the founding document for the C9 · Crunch Sharp track. It will be revisited at the end of the first full cohort. Material changes to the syllabus, the toolchain stance, or the relationship to sibling tracks require an amendment to this charter and a note in the academy's `MASTER-CURRICULUM.md`.

— Code Crunch Worldwide · Sharp sub-brand · 2026-05-13
