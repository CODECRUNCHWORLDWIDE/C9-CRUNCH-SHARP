# Week 2 — Exercises

Short, focused drills. Each one should take 30–45 minutes. Do them in order; later ones assume earlier ones.

## Index

1. **[Exercise 1 — Hello, API](exercise-01-hello-api.md)** — scaffold a `dotnet new web` project, map four endpoints, hit them with `curl`, and read the OpenAPI document. (~35 min)
2. **[Exercise 2 — Typed routes and binding](exercise-02-typed-routes.cs)** — fill in TODOs in a Minimal API file that exercises every parameter-binding source and `TypedResults`. (~40 min)
3. **[Exercise 3 — Injecting services](exercise-03-injecting-services.cs)** — register three services with three lifetimes, observe what each one means at request time, and fix a captive-dependency bug. (~30 min)

## How to work the exercises

- Read the prompt. Skim, don't memorize.
- **Type the code yourself.** Do not copy-paste. Muscle memory is the entire point of these drills.
- Run it. `curl` it. Read the response. Read the OpenAPI document when relevant.
- If you get stuck for more than 10 minutes, peek at the inline hints at the bottom of each file.
- Every exercise must end with `dotnet build` printing **0 warnings, 0 errors**. A warning is still a bug this week.

There are no solutions checked in. Solutions live in your own fork — search GitHub for `c9-week-02` after you finish to compare.
