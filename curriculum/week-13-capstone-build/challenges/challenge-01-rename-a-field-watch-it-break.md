# Challenge 1 — Prove the Contract Is Load-Bearing: Rename a `.proto` Field and Watch Every Consumer Break

> **Time:** 2 hours. **Prerequisites:** Exercises 1–3 (you need a contract, a service, and at least the first client/test referencing `Workshop.Contracts`). **Citations:** the Protocol Buffers proto3 guide at <https://protobuf.dev/programming-guides/proto3/>, the `Grpc.Tools` reference at <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>, the proto field-number rules at <https://protobuf.dev/programming-guides/proto3/#assigning>, and the gRPC versioning guidance at <https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning>.

## The premise

The whole architectural bet of the Polyglot Workshop is that **the contract is the single source of truth** and that a change to it forces every consumer to recompile rather than silently breaking at runtime. This challenge makes you *prove* that bet — and then experience the difference between a safe change and an unsafe one. You will rename a `.proto` field, watch the service and the first client both fail to compile, fix them, and then make a *wire-breaking* change (renumbering a field) and observe that it compiles fine but corrupts data — the failure the field-number discipline exists to prevent.

The deeper point underneath the mechanics: there are exactly three kinds of change you can make to a shipped message, and they have three completely different blast radii. A **rename** (same number, different name) is invisible on the wire and loud at compile time — it forces a coordinated source change across every consumer but cannot corrupt data. A **renumber** (same name, different number) is invisible at compile time and catastrophic on the wire — nothing stops you, and old and new peers silently transpose values. An **add** (new name, new number) is invisible at compile time *and* safe on the wire — old peers ignore the unknown field, new peers read it, and nobody is forced to recompile. This challenge makes you feel all three in your hands so that the field-number-is-forever rule stops being a slogan and becomes a reflex.

By the end you will have produced: (a) a captured build failure showing every consumer of the renamed field failing to compile, (b) the minimal diff that makes them all green again, and (c) a written contrast of "rename — caught at compile time" vs "renumber — not caught, corrupts the wire" with the rule that prevents the second.

## Setup

Start from your green Exercise-3 repo: `Workshop.Contracts` with `workshop.proto`, `Workshop.Api` implementing `Enroll`, and at least one project (the console prover or the test project) that reads `Enrollment.LearnerId`. Confirm a clean baseline first:

```bash
dotnet build
# Build succeeded · 0 warnings · 0 errors
```

## Part 1 — The safe-but-breaking change: rename a field

In `workshop.proto`, rename `Enrollment.learner_id` to `Enrollment.learner_id`, keeping the field number:

```proto
message Enrollment {
  string id = 1;
  string lesson_id = 2;
  string learner_id = 3;     // was: string learner_id = 3;  (number UNCHANGED)
  google.protobuf.Timestamp enrolled_at = 4;
}
```

Rebuild and capture the failure. Every place that referenced `.LearnerId` now fails, because the generated property is `LearnerId`:

```bash
dotnet build
```

```
src/Workshop.Api/Services/WorkshopService.cs(58,13): error CS0117:
  'Enrollment' does not contain a definition for 'LearnerId'
tests/Workshop.IntegrationTests/EnrollSliceTests.cs(31,24): error CS0117:
  'Enrollment' does not contain a definition for 'LearnerId'

Build FAILED.  2 Error(s)
```

**This is the win, not the loss.** The rename did not silently ship; the compiler stopped you at every consumer. Now fix them — update `ToContract` in the service and the assertion in the test to use `LearnerId` — and confirm the build goes green again. The fixing diff is small and mechanical, which is exactly the point:

```diff
  // src/Workshop.Api/Services/WorkshopService.cs — in ToContract
- LearnerId = e.LearnerId.ToString(),
+ LearnerId = e.LearnerId.ToString(),     // entity field name unchanged; only the contract property moved

  // tests/Workshop.IntegrationTests/EnrollSliceTests.cs
- enrollment.LearnerId.Should().Be("00000000-0000-0000-0000-000000000007");
+ enrollment.LearnerId.Should().Be("00000000-0000-0000-0000-000000000007");
```

Note that the *entity* property `e.LearnerId` did not change — only the generated *contract* property did, because only the `.proto` field name changed. The compiler walked you to all two (or four, with the stretch goal) call sites; you changed each; the build went green. No grepping, no guessing, no "did I get them all." Capture the diff and the clean `Build succeeded`.

## Part 2 — The unsafe change that the compiler does *not* catch: renumber a field

Revert Part 1. Now make a change that *looks* harmless and compiles cleanly but breaks the wire. Swap two field *numbers*:

```proto
message Enrollment {
  string id = 1;
  string lesson_id = 3;      // was 2
  string learner_id = 2;     // was 3   <-- numbers swapped, names unchanged
  google.protobuf.Timestamp enrolled_at = 4;
}
```

The C# names are unchanged, so `.LessonId` and `.LearnerId` still resolve and **the build succeeds**. But the wire is now incompatible with any peer built against the old numbering: a server that serialized `lesson_id` as field 2 and a client that now reads field 2 as `learner_id` will silently swap the two values. If you have an old binary (or a captured byte payload) from before the change, decode it against the new contract and observe the lesson id and learner id are transposed.

Demonstrate it by serializing with the old numbering and parsing with the new. The cleanest way to do it in-process: build the message, serialize to bytes with the old contract, then parse those bytes with the new contract and print both fields.

```csharp
// With the OLD numbering (lesson_id=2, learner_id=3), serialize:
var old = new Enrollment { LessonId = "LESSON-AAA", LearnerId = "LEARNER-BBB" };
byte[] wire = old.ToByteArray();          // capture these bytes

// Rebuild against the NEW numbering (lesson_id=3, learner_id=2) and parse:
var roundTripped = Enrollment.Parser.ParseFrom(wire);
Console.WriteLine($"LessonId={roundTripped.LessonId}  LearnerId={roundTripped.LearnerId}");
// Prints:  LessonId=LEARNER-BBB  LearnerId=LESSON-AAA   <-- TRANSPOSED, no error
```

The bytes that were written for field 2 (the lesson id) are now read as field 2 (the learner id, under the new numbering), and vice versa — the values swap with no exception, no log, no hint. You can confirm it at the byte level too: the wire is tag-length-value, where the tag encodes `(field_number << 3) | wire_type`. For a string field, field 2 produces tag byte `0x12` (`2 << 3 | 2`) and field 3 produces `0x1A` (`3 << 3 | 2`); the renumber means the parser matches your `0x12` bytes to whatever field it now calls 2. The rule, from <https://protobuf.dev/programming-guides/proto3/#assigning>: **field numbers are the wire identity and are forever — you never reuse or renumber a shipped field; to remove one you `reserved` its number.** Renaming is safe (names are compile-time only); renumbering is not (numbers are the wire).

## The measurement plan

Capture each of these in a `CONTRACT-BREAK.md` so the contrast is concrete, not asserted:

1. **The clean baseline.** `dotnet build` → `Build succeeded · 0 errors`, before any change.
2. **Part 1 — the rename, broken.** The `dotnet build` failure with every `CS0117` line; count the distinct projects that fail.
3. **Part 1 — the rename, fixed.** The diff (the `ToContract` change and the test/client change) and a clean `Build succeeded`.
4. **Part 2 — the renumber, compiling.** `dotnet build` → `Build succeeded` *despite* the swap, plus the transposed-output capture above.
5. **Part 3 — the add, no recompile forced.** `dotnet build` → success with field 5 added and zero changes to any consumer that does not read it.

## Part 3 — Do it the right way: add, don't mutate

Show the additive, non-breaking path. Instead of renaming `learner_id`, *add* a new field and `reserved` the old number if you ever retire it:

```proto
message Enrollment {
  string id = 1;
  string lesson_id = 2;
  string learner_id = 3;             // keep the old field for old clients
  google.protobuf.Timestamp enrolled_at = 4;
  string learner_display_name = 5;   // NEW: additive, old clients ignore it
}
```

Old clients that do not know field 5 ignore it; new clients read it. No recompile is *forced* on consumers that do not need the new field — additive changes are forward- and backward-compatible. Contrast this with Part 1's rename, which forced every consumer to update. Both are legitimate; the difference is whether you are willing to break consumers (a coordinated rename across the monorepo) or must stay compatible (an additive change for independently-deployed clients). The versioning guidance is at <https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning>.

## Why this distinction is worth two hours

In the Polyglot Workshop monorepo, the three clients and the service compile together, so a rename is *caught* — that is the safety this challenge demonstrates. But the reason the field-number rule exists is the case the monorepo hides: **independently deployed peers.** The instant the MAUI app ships to phones (Week 15), it is no longer recompiled in lockstep with the backend. A phone in someone's pocket is running last month's generated client; the backend is running today's. If you renumber a field, that phone reads the wrong bytes into the wrong property and the bug ships to production with no compile error anywhere, because the two binaries were never compiled together. The monorepo's all-at-once recompile is a *development-time* convenience; the wire compatibility rules are what keep you safe at *deployment time*, when the convenience is gone.

This is also why the Buf breaking-change detector (stretch goal 3) is not optional rigor for a serious team — it is the automated version of the reflex this challenge builds. `buf breaking --against main` compares your `.proto` to the merged baseline and fails the PR on a renumber, a removed field, a changed type, or a reused reserved number, *before* the incompatible contract can reach a deployed client. The rule lives in your head after this challenge; Buf puts it in CI so it does not depend on anyone remembering. Cite <https://buf.build/docs/breaking/overview>.

A short table to keep on hand — the three changes and their blast radius:

| Change | Compiles? | Wire-compatible? | Forces consumer recompile? | Verdict |
|--------|-----------|------------------|----------------------------|---------|
| Rename field (same number) | No (CS0117) | Yes | Yes — caught at build | Safe, coordinated |
| Renumber field (same name) | Yes | **No** — data transposes | No — silently broken | Never do this |
| Add field (new number) | Yes | Yes | No | Safe, additive |
| Remove + `reserved` the number | Yes | Yes | Only where read | Safe retirement |

## Acceptance criteria

1. A captured `dotnet build` failure from Part 1 showing **at least two** distinct consumers (the service and the test/client) failing to compile on the renamed field, with the `CS0117` error text.
2. The minimal diff that makes Part 1 green again (the `ToContract` change and the test/client change), and a clean `Build succeeded` after it.
3. A demonstration or written-and-justified explanation from Part 2 that the field renumber **compiles** but transposes `lesson_id` and `learner_id` on the wire, with the field-number rule stated and cited.
4. The Part 3 additive change, with a one-paragraph explanation of why adding field 5 forces no recompile while Part 1's rename forced two.
5. A 150–200 word write-up, "what the single source of truth bought us," naming the exact failure mode (silent runtime divergence across three clients) that writing the contract first prevents.
6. The blast-radius table reproduced in your write-up, with one sentence on why the renumber row is the only one marked "never do this."

## Stretch goals

1. **Three real consumers.** Add the `Workshop.Mobile` and `Workshop.Admin` projects as `<ProjectReference>` consumers that each read `Enrollment.LearnerId` in one line, then redo Part 1 and confirm the build now fails in *four* projects at once. This is the multi-client property made literal. Cite <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb> for the Blazor/gRPC-Web client.
2. **`reserved` the retired number.** After Part 3, retire `learner_id` properly: delete the field and add `reserved 3; reserved "learner_id";`. Try to re-add a field at number 3 and observe `protoc` rejects it. Explain why `reserved` exists. Cite <https://protobuf.dev/programming-guides/proto3/#fieldreserved>.
3. **Buf lint/breaking-change detection.** Add the `buf` CLI and run `buf breaking --against '.git#branch=main'` to catch Part 2's renumber *before* it merges, automatically, in CI. Wire it as a step in the Actions workflow and show it failing the PR. Cite <https://buf.build/docs/breaking/overview>.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/aspnet/core/grpc/basics>, <https://learn.microsoft.com/en-us/aspnet/core/grpc/versioning>, <https://learn.microsoft.com/en-us/aspnet/core/grpc/grpcweb>. External: the Protocol Buffers proto3 guide at <https://protobuf.dev/programming-guides/proto3/>, the field-assignment rules at <https://protobuf.dev/programming-guides/proto3/#assigning>, the `reserved` reference at <https://protobuf.dev/programming-guides/proto3/#fieldreserved>, and the Buf breaking-change detector at <https://buf.build/docs/breaking/overview>.
