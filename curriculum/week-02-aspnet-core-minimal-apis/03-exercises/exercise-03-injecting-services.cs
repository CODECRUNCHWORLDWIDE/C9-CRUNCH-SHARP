// Exercise 3 — Injecting services (and fixing a captive dependency)
//
// Goal: Register three services with three different lifetimes (Singleton,
//       Scoped, Transient), prove what each lifetime means at request time
//       with a diagnostic endpoint, then find and fix a captive-dependency
//       bug the container will catch for you when scope validation is on.
//
// Estimated time: 30 minutes.
//
// HOW TO USE THIS FILE
//
//   mkdir Injecting && cd Injecting
//   dotnet new web -n Injecting -o src/Injecting
//
// Replace src/Injecting/Program.cs with the contents of THIS FILE.
//
// You will work the file in three passes:
//
//   Pass 1 — fill in the three TODO registrations and run /diag/lifetimes.
//            Confirm the JSON matches the EXPECTED OUTPUT at the bottom.
//
//   Pass 2 — uncomment the BROKEN registration block at the bottom of the
//            file (around line 200). Run the app. The container should
//            CRASH AT STARTUP with a "Cannot consume scoped service" error.
//            Read the message carefully. THIS IS THE POINT OF THE EXERCISE.
//
//   Pass 3 — fix the captive-dependency bug WITHOUT widening any lifetime.
//            Use IServiceScopeFactory (Lecture 2, section 7). Re-run; the
//            crash goes away; the /diag/cleanup endpoint works.
//
// ACCEPTANCE CRITERIA
//
//   [ ] All three registrations done; /diag/lifetimes returns the expected JSON.
//   [ ] You hit the captive-dependency crash at least once and read the message.
//   [ ] You fixed the bug using IServiceScopeFactory, not by widening a lifetime.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] /diag/cleanup returns Ok with a removed count.
//
// Hints at the bottom. Read them only if stuck > 15 min.

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Turn on strict validation. This is the C9 default from Week 2 onward.
builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes  = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// -- PASS 1 --------------------------------------------------------------
// TODO #1: Register IClock as a SINGLETON, implemented by SystemClock.
//          One instance, app-wide. The clock is stateless and thread-safe.

// TODO #2: Register RequestContext as SCOPED.
//          (No interface — the class is itself the service.)
//          One instance per HTTP request. Gives every request a unique Id
//          that any service in the same request can read.

// TODO #3: Register ITodoIdGenerator as TRANSIENT, implemented by
//          SequentialIdGenerator. Every resolution returns a fresh
//          generator object; the seed itself is a private static int
//          and is shared.

// Helper for Pass 3 — registered for you. (See the BROKEN block below.)
builder.Services.AddScoped<ITodoStore, InMemoryTodoStore>();

// -- PASS 2 / PASS 3 -----------------------------------------------------
// At the bottom of this file there is a `BROKEN` block. Leave it commented
// out for Pass 1. Uncomment it for Pass 2 to see the crash. Then implement
// the fix (Pass 3) and re-run.

var app = builder.Build();

app.MapOpenApi();

// ------------------------------------------------------------------------
// Diagnostic endpoint — proves the three lifetimes empirically.
// ------------------------------------------------------------------------

app.MapGet("/diag/lifetimes", (
    IClock              a,   IClock              b,   // singleton — a == b across all requests
    RequestContext      rc1, RequestContext      rc2, // scoped    — rc1 == rc2 within ONE request
    ITodoIdGenerator    g1,  ITodoIdGenerator    g2)  // transient — g1 != g2 (fresh each time)
    =>
{
    return TypedResults.Ok(new LifetimeReport(
        ClockSame:     ReferenceEquals(a,   b),
        ScopeSame:     ReferenceEquals(rc1, rc2),
        TransientSame: ReferenceEquals(g1,  g2),
        RequestId:     rc1.Id,
        ClockNow:      a.GetUtcNow()));
})
.WithTags("Diagnostics");

// ------------------------------------------------------------------------
// PASS 3 endpoint — runs the cleanup once, manually.
// ------------------------------------------------------------------------

app.MapPost("/diag/cleanup", async (
    BackgroundCleaner cleaner, CancellationToken ct) =>
{
    var removed = await cleaner.RunOnceAsync(ct);
    return TypedResults.Ok(new CleanupResult(removed));
})
.WithTags("Diagnostics");

app.Run();

// ------------------------------------------------------------------------
// Domain
// ------------------------------------------------------------------------

public interface IClock
{
    DateTimeOffset GetUtcNow();
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}

public sealed class RequestContext
{
    public Guid Id { get; } = Guid.NewGuid();
}

public interface ITodoIdGenerator
{
    int Next();
}

public sealed class SequentialIdGenerator : ITodoIdGenerator
{
    private static int _seed;
    public int Next() => Interlocked.Increment(ref _seed);
}

public interface ITodoStore
{
    Task<int> RemoveOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
}

public sealed class InMemoryTodoStore : ITodoStore
{
    public Task<int> RemoveOlderThanAsync(DateTimeOffset _, CancellationToken __) =>
        Task.FromResult(3);  // pretend we deleted 3 rows
}

// ------------------------------------------------------------------------
// PASS 2 (BROKEN) — uncomment and run to see the captive-dependency crash.
// PASS 3 (FIX)    — replace with the IServiceScopeFactory implementation.
// ------------------------------------------------------------------------

// Uncomment to see the crash:
//
// public sealed class BackgroundCleaner(IClock clock, ITodoStore store)
// {
//     // Bug: BackgroundCleaner is registered SINGLETON (see below) and
//     // holds a SCOPED ITodoStore in a constructor parameter. The
//     // container catches this at startup with ValidateOnBuild = true.
//
//     public async Task<int> RunOnceAsync(CancellationToken ct)
//     {
//         var cutoff = clock.GetUtcNow().AddDays(-30);
//         return await store.RemoveOlderThanAsync(cutoff, ct);
//     }
// }
//
// // And in your registrations above:
// // builder.Services.AddSingleton<BackgroundCleaner>();

// ------------------------------------------------------------------------
// PASS 3 (FIX) — write the corrected version here.
// ------------------------------------------------------------------------
//
// Rules:
//   1. BackgroundCleaner stays SINGLETON. Background workers usually do.
//   2. ITodoStore stays SCOPED. The store may eventually hold a DbContext.
//   3. The fix is in BackgroundCleaner itself: inject IServiceScopeFactory,
//      create a scope per call, resolve ITodoStore from THAT scope, dispose
//      the scope when done.
//
// TODO #4: Write the corrected BackgroundCleaner class below.

public sealed class BackgroundCleaner /* (TODO — primary constructor) */
{
    // TODO: inject IServiceScopeFactory and IClock here.
    // TODO: implement RunOnceAsync using `await using var scope = ...CreateAsyncScope();`
    //       Resolve ITodoStore from scope.ServiceProvider; call RemoveOlderThanAsync.

    public Task<int> RunOnceAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

// And remember to register BackgroundCleaner (singleton) in the registrations
// block at the top of the file:
//
//   builder.Services.AddSingleton<BackgroundCleaner>();

// ------------------------------------------------------------------------
// DTOs
// ------------------------------------------------------------------------

public sealed record LifetimeReport(
    bool ClockSame,
    bool ScopeSame,
    bool TransientSame,
    Guid RequestId,
    DateTimeOffset ClockNow);

public sealed record CleanupResult(int Removed);

// ------------------------------------------------------------------------
// EXPECTED OUTPUTS
// ------------------------------------------------------------------------
//
// Pass 1:
//   curl -s http://localhost:5099/diag/lifetimes | jq .
//   {
//     "clockSame":     true,
//     "scopeSame":     true,
//     "transientSame": false,
//     "requestId":     "<a fresh guid each request>",
//     "clockNow":      "<an iso timestamp>"
//   }
//
// Pass 2 (after uncommenting the BROKEN block):
//   At `dotnet run`:
//     Unhandled exception. System.InvalidOperationException:
//     Cannot consume scoped service 'ITodoStore' from singleton
//     'BackgroundCleaner'.
//
// Pass 3 (after writing the fix):
//   curl -s -X POST http://localhost:5099/diag/cleanup | jq .
//   { "removed": 3 }
//
// ------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ------------------------------------------------------------------------
//
// Pass 1 registrations:
//   builder.Services.AddSingleton<IClock, SystemClock>();
//   builder.Services.AddScoped<RequestContext>();
//   builder.Services.AddTransient<ITodoIdGenerator, SequentialIdGenerator>();
//
// Pass 3 — the fixed BackgroundCleaner:
//
//   public sealed class BackgroundCleaner(IServiceScopeFactory scopeFactory, IClock clock)
//   {
//       public async Task<int> RunOnceAsync(CancellationToken ct)
//       {
//           await using var scope = scopeFactory.CreateAsyncScope();
//           var store = scope.ServiceProvider.GetRequiredService<ITodoStore>();
//           var cutoff = clock.GetUtcNow().AddDays(-30);
//           return await store.RemoveOlderThanAsync(cutoff, ct);
//       }
//   }
//
//   // And register the cleaner once:
//   builder.Services.AddSingleton<BackgroundCleaner>();
//
// ------------------------------------------------------------------------
// WHY THIS MATTERS
// ------------------------------------------------------------------------
//
// You will hit this exact bug in real code — usually when an `IHostedService`
// in Week 8 tries to inject a `DbContext` (scoped) directly. The container's
// ValidateOnBuild flag means you find out at app startup, not at 3 AM under
// production load. That's worth the few extra milliseconds at boot.
//
// `IServiceScopeFactory` is THE pattern for resolving scoped services from
// long-lived components. Internalize it now; you'll write this code three
// times this course.
//
// ------------------------------------------------------------------------
