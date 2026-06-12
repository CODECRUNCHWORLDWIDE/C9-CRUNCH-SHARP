# C9 · Crunch Sharp — Brand Guide

> **Voice:** practical, production-shop, enterprise-grade — but never corporate-bland. The voice of a senior engineer at a mid-sized .NET shop who likes their job.
> **Feel:** Visual Studio purple, Roslyn refactor-suggest underline, end-of-quarter ship vibes.

Extends the family brand. C9-specific overrides only.

---

## Identity

- **Full name:** Crunch Sharp — C# & .NET
- **Program code:** C9
- **Full title in copy:** *C9 · Crunch Sharp*
- **Tagline (short):** Production .NET, taught honestly.
- **Tagline (long):** A free, open-source fifteen-week C# and .NET track for engineers who already know one language and want enterprise-grade .NET fluency.
- **Canonical URL:** `codecrunchglobal.vercel.app/course-c9-csharp`
- **License:** GPL-3.0

---

## Where C9 diverges from the family palette

Inherits Ink/Parchment/Gold. Adds **Roslyn Violet** — directly inspired by the .NET ecosystem's long-running brand without copying it pixel-for-pixel:

| Role | Name | Hex | Use |
|------|------|-----|-----|
| Accent | Roslyn Violet | `#512BD4` | Highlights, the C9 mark, "build succeeded" indicators |
| Accent deep | Roslyn Violet deep | `#3B1FA3` | Hover, eyebrows |
| Accent soft | Roslyn Violet soft | `#C7B8FF` | Subtle row backgrounds in package-list tables |

```css
:root {
  --roslyn:       #512BD4;
  --roslyn-deep:  #3B1FA3;
  --roslyn-soft:  #C7B8FF;
}
```

> **Note on color heritage:** the `#512BD4` is close to the longstanding .NET community color. We use a chromatic neighbor (slightly cooler) so we don't trade on Microsoft's exact brand chip while staying readable in context.

### Typography

EB Garamond display, Lora body, JetBrains Mono for code. C# code blocks use mono with explicit visual differentiation for the type system's keywords (`record`, `sealed`, `required`, `init`) — we lean on JetBrains Mono's ligatures for `=>` and `=>` arrow.

---

## Recurring page elements

### The "build succeeded" indicator

A small inline element used at the end of every exercise that culminates in a working program:

```
✓ Build succeeded · 0 warnings · 0 errors · 412 ms
```

Always JetBrains Mono. Always green check + violet for the milliseconds. The aesthetic mirrors `dotnet build` output — a tiny dose of "you shipped."

### The "project structure" tree

C# / .NET solutions have a recognizable structure. We render it consistently:

```
HelloApi/
├── HelloApi.sln
├── src/
│   └── HelloApi/
│       ├── HelloApi.csproj
│       ├── Program.cs
│       └── Endpoints/
└── tests/
    └── HelloApi.Tests/
        ├── HelloApi.Tests.csproj
        └── EndpointTests.cs
```

Mono, hairline border, consistent indent. Don't omit the `.sln`; it confuses new learners.

---

## Voice rules

- **Cite the .NET version.** "Available since .NET 8" — not "in modern .NET."
- **Distinguish framework from runtime.** "ASP.NET Core" (framework) vs ".NET 9" (runtime/SDK). Beginners conflate them; we don't help them.
- **Use full names on first reference.** "Entity Framework Core (EF Core)" then "EF Core."
- **Don't bash Java.** They're peer ecosystems with overlapping audiences. Compare specifically, don't generalize.
- **Don't mock Visual Studio.** It's the IDE most learners will use at work. Critique specific features; never the whole tool.
- **MS marketing claims need fact-checking.** When Microsoft says "X% faster," cite the benchmark conditions or omit.

---

## Course page conventions

The course page for C9 uses a slightly Roslyn-tinted parchment as the hero. The 15-week ladder appears as a "solution explorer" tree — every week is a node, expandable visually to its weekly modules. Capstone deliverables show the "build succeeded" indicator.

---

*GPL-3.0. Fork freely.*
