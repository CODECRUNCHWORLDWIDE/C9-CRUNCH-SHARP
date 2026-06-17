# Week 4 — Exercises

Three short coding exercises that drill the week's mechanical skills. Each is a `.cs` file you can drop into a fresh console project and complete by filling in the `TODO`s. None should take more than 60 minutes; if you spend longer, read the hints at the bottom of the file.

| Exercise | Time | What you'll exercise |
|----------|-----:|----------------------|
| [exercise-01-async-basics.cs](./exercise-01-async-basics.cs) | 45 min | `Task.WhenAll`, `Task.WhenEach`, `ValueTask`, `ConfigureAwait`, `IAsyncEnumerable<T>` |
| [exercise-02-channels-producer-consumer.cs](./exercise-02-channels-producer-consumer.cs) | 60 min | `Channel.CreateBounded<T>`, `BoundedChannelFullMode.Wait`, fan-out to multiple consumers, graceful completion |
| [exercise-03-cancellation-and-timeout.cs](./exercise-03-cancellation-and-timeout.cs) | 45 min | `CancellationToken`, `Console.CancelKeyPress`, `CreateLinkedTokenSource`, `CancelAfter`, `catch ... when` for distinguishing reasons |

## Acceptance criteria (all three)

- `dotnet build`: 0 warnings, 0 errors.
- The smoke output in each file matches (modulo timing).
- No `Task.Result` or `Task.Wait()` anywhere.
- No `async void` outside of an event handler (none here).
- Every async API accepts and passes through a `CancellationToken`.
- Every `Channel<T>` writer is completed in a `finally` block.
- Every `IAsyncEnumerable<T>` generator has `[EnumeratorCancellation]` on its `CancellationToken` parameter.

## Setup

For each exercise:

```bash
mkdir Exercise01 && cd Exercise01
dotnet new console -n Exercise01 -o src/Exercise01
# Replace src/Exercise01/Program.cs with the contents of the exercise file.
dotnet run --project src/Exercise01
```

(Exercises 2 and 3 do not need any additional NuGet packages — everything is in the BCL.)

## What you'll have when you're done

A small notebook of three working programs that, together, exercise every async API in the BCL you'll reach for in the rest of the curriculum. Commit each exercise to your Week 4 Git repository. Future-you in Week 8 (when you build a hosted `BackgroundService`) will be glad you have the reference.
