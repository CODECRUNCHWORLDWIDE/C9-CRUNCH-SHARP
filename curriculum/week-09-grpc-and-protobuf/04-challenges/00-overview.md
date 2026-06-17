# Week 9 — Challenges

The challenges push past the exercises into the production realities of gRPC: cross-language interoperability and schema evolution. Each one builds on a service you wrote earlier in the week and asks you to make a few of your own design decisions. They're longer than the exercises and worth the investment — these are exactly the situations you'll meet on a real distributed system. Read the premise, clear the acceptance criteria, and write up your reflection answers as you go.

## Ground Rules

- Each challenge has a prerequisite from this week's exercises (Challenge 1 needs the Exercise 2 server runnable; Challenge 2 builds on the Exercise 1 proto). Finish those first.
- Pin the tool and package versions called out in each challenge — mixing major versions of `grpcio`/`grpcio-tools` or the Grpc.* packages produces subtle, hard-to-debug failures.
- Each challenge has clear acceptance criteria plus optional stretch goals. The criteria are the bar to clear; stretch goals are extra credit if you finish early.

## Index

| # | File | What you'll build | Difficulty | Est. time |
|---|------|-------------------|-----------:|----------:|
| 1 | [challenge-01-cross-language-client.md](./challenge-01-cross-language-client.md) | A Python client that exercises all four call types against your C# `NumberService`, generated from the identical `.proto`, proving the cross-language wire guarantee | Advanced | ~2 hours |
| 2 | [challenge-02-schema-evolution.md](./challenge-02-schema-evolution.md) | A two-version (v1/v2) order service with a four-cell cross-version test matrix that proves which schema changes preserve wire compatibility and which break it | Advanced | ~2 hours |

## How to Submit (Self-Check)

1. Confirm every acceptance-criteria checkbox in the challenge is satisfied — all call types or all cross-version tests pass, and the build is clean with 0 warnings and 0 errors.
2. Write the `RESULTS.md` each challenge asks for, answering its reflection questions and noting any gotchas you hit along the way.
3. Commit your work to Git using the commit message suggested at the bottom of each challenge file, keeping the artifacts in the layout the challenge specifies.
