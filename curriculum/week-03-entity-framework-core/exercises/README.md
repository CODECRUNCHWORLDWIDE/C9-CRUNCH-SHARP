# Week 3 — Exercises

Short, focused drills. Each one should take 30–45 minutes. Do them in order; later ones assume earlier ones.

## Index

1. **[Exercise 1 — Your first context](exercise-01-first-context.md)** — scaffold an EF Core 9 project, model a single `Book` entity, generate and apply the first migration, inspect the `.db` with `sqlite3`. (~40 min)
2. **[Exercise 2 — Relationships](exercise-02-relationships.cs)** — fill-in-the-TODO entity graph that exercises 1-to-1, 1-to-many, and many-to-many; configure the fluent API; query across relationships with `Include` and projection. (~45 min)
3. **[Exercise 3 — Migrations workflow](exercise-03-migrations.cs)** — add an `Author.Bio` column, generate a second migration, write the rollback `Down` by hand, verify with `sqlite3`, and emit an idempotent script. (~35 min)

## How to work the exercises

- Read the prompt. Skim, don't memorize.
- **Type the code yourself.** Do not copy-paste. Muscle memory is the entire point of these drills.
- Run every `dotnet ef` command from the project directory; the CLI is sensitive to where it is invoked.
- Open the resulting `.db` file in `sqlite3` and *look at the schema EF Core generated*. Half the learning is reading the output.
- Every exercise must end with `dotnet build` printing **0 warnings, 0 errors**. A migration warning is still a bug this week.

There are no solutions checked in. Solutions live in your own fork — search GitHub for `c9-week-03` after you finish to compare.
