# Lecture 2 — Dependency Injection and Services

> **Duration:** ~2 hours of reading + hands-on.
> **Outcome:** You can register a service with the right lifetime, inject it into a Minimal API endpoint via parameter injection or into a handler class via constructor injection, explain what `IServiceProvider` and `IServiceScopeFactory` are for, and recognize the three classic DI bugs on sight.

If you only remember one thing from this lecture, remember this:

> **Dependency injection in .NET is a library, not a framework.** `Microsoft.Extensions.DependencyInjection` is one NuGet package, less than 50 KB compiled, and it is the same container used by ASP.NET Core, MAUI, Worker Services, Azure Functions, Blazor, and every line-of-business app written since .NET Core 1.0. If you understand `AddSingleton`, `AddScoped`, `AddTransient`, and `IServiceProvider`, you understand DI everywhere in modern .NET.

This lecture is half conceptual and half mechanical. The mechanical part takes thirty minutes to learn. The conceptual part — when to pick which lifetime, and why — takes the rest of your career to get reflexively right.

---

## 1. Why DI exists

A service is a chunk of behavior — a database wrapper, an HTTP client, a logger, a clock. **Dependency injection** is the principle that a class should not *construct* its services; it should *receive* them.

The motivating example is testability. Compare:

```csharp
// No DI — TodoService can't be tested without a real SQL Server running somewhere.
public sealed class TodoService
{
    private readonly SqlConnection _db = new("Server=prod-db;...");

    public Todo? Find(int id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM todos WHERE id = @id";
        // ...
    }
}
```

```csharp
// With DI — TodoService takes its dependency. Tests can pass an in-memory fake.
public sealed class TodoService(ITodoRepository repo)
{
    public Task<Todo?> FindAsync(int id, CancellationToken ct) => repo.FindAsync(id, ct);
}
```

Three things changed in the second version:

1. **The collaborator is named by an interface** (`ITodoRepository`). The service doesn't care whether the repository talks to SQL Server, an in-memory dictionary, or a remote HTTP API.
2. **The collaborator is passed in**, not created inside. `TodoService` cannot be instantiated without a repository — and that fact shows up at compile time, not at the first failing test.
3. **The compiler enforces that** `TodoService` only uses the methods declared on `ITodoRepository`. No accidental reach into `SqlConnection`-specific behavior.

That's it. DI is "don't `new` your collaborators" plus "name them by an interface." Everything else is plumbing.

> **Aside on the terminology.** Some teams say "inversion of control (IoC)" instead of "dependency injection." They are not exactly the same thing — IoC is the broader principle (the framework calls you, not the other way around), DI is one specific way to do IoC — but in .NET shops the two are used interchangeably. The library name is `Microsoft.Extensions.DependencyInjection`; we will call it DI.

The plumbing in .NET is `Microsoft.Extensions.DependencyInjection`. It is a **container**: a registry of "this interface maps to this implementation, constructed with these arguments, with this lifetime." Once you have populated the registry, you ask the container for a service and it gives you one — constructing it on demand and supplying any dependencies it needs, transitively.

---

## 2. The two surfaces — `IServiceCollection` and `IServiceProvider`

The container has exactly two phases.

**Registration phase** — you tell the container what's available:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ITodoRepository, EfTodoRepository>();
builder.Services.AddTransient<INotifier, EmailNotifier>();
```

`builder.Services` is an `IServiceCollection` — basically `List<ServiceDescriptor>`. You add descriptors. The list is mutable up to the moment you call `builder.Build()`.

**Resolution phase** — the container constructs services on demand:

```csharp
var app = builder.Build();   // <-- container is now frozen
// The container is hidden behind app.Services (an IServiceProvider).

todos.MapGet("/{id}", async (int id, ITodoRepository repo, CancellationToken ct) =>
{
    /* ... */
});
```

Behind the scenes, every time that endpoint is invoked, the framework asks the container for an `ITodoRepository`. The container constructs one (or returns a cached instance, depending on lifetime), calls the handler, then disposes anything it owns.

You will almost never call `IServiceProvider.GetService` yourself in application code. The framework does it for you, on every request, transparently. The only places you reach for the provider directly are:

1. Background work that needs to escape the request's scope (`IServiceScopeFactory`, section 7).
2. Generic-typed factory scenarios (`IServiceProvider.GetRequiredService<T>()`).
3. Unit tests that build a tiny container by hand to exercise one service.

Otherwise: register at startup, declare your dependencies as parameters, let the framework do the rest.

---

## 3. The three lifetimes

Every service registration carries a **lifetime**. The lifetime answers one question: *how often does the container construct a fresh instance?*

| Lifetime | Method | Instance per | Test in your head |
|----------|--------|--------------|-------------------|
| **Singleton** | `AddSingleton<I, T>()` | The whole application. | One instance from app start to app stop, shared across every request. |
| **Scoped** | `AddScoped<I, T>()` | An `IServiceScope` — in ASP.NET Core, one per HTTP request. | A fresh instance for each request; the same instance everywhere within that request. |
| **Transient** | `AddTransient<I, T>()` | Every resolution call. | Every time anyone asks for the service, you get a brand-new one. |

The lifetime is not a property of the service — it is a property of how you registered it. The same class could be registered singleton in one app and scoped in another. The class itself has no opinion.

### Worked semantics — one request, three lifetimes

Imagine a request handler that depends on three services:

```csharp
public sealed class OrderProcessor(
    ITimeProvider clock,         // singleton — same instance forever
    IOrderRepository repo,       // scoped — same instance for this request
    INotifier notifier)          // transient — fresh instance every constructor call
{
    /* ... */
}
```

For request #1, the container builds:

- `clock` — already exists; return it.
- `repo` — does not yet exist in this request's scope; construct one, cache it.
- `notifier` — construct a fresh one, do not cache.

If `OrderProcessor` itself depends on another service, say a `Validator`, that *also* depends on `INotifier`, then **transient** means `Validator` gets a different `INotifier` than `OrderProcessor` does. Within the same request. That is what "transient" buys and costs.

For request #2:

- `clock` — already exists; return it.
- `repo` — request #2 has its own scope; a different cached instance from request #1.
- `notifier` — every call: a new one.

This is the model. Internalize it.

### Picking a lifetime — the heuristic

A short decision tree:

1. **Does the service hold state that should be shared across all requests?** (in-memory cache, message bus, connection pool, configuration snapshot) → **Singleton**.
2. **Does the service hold state that's specific to one request?** (database `DbContext`, the current user, a request-scoped logger correlation id) → **Scoped**.
3. **Is the service genuinely stateless, or so cheap that "fresh every time" doesn't cost anything?** (small validators, small mappers) → **Transient**.

The most common mistake is **registering everything as transient by default**. Transient is rarely wrong (you can always afford a few extra allocations), but it can hide state-leak bugs that scoped lifetime would have caught — and for some types (e.g. `HttpClient`, `DbContext`) transient is actively harmful.

The second most common mistake is **registering a stateful service as a singleton without realizing it**. A stateless validator as a singleton is fine. A "user context" service as a singleton is a security bug.

In C9 the default ordering is **Scoped → Singleton → Transient**. Pick scoped unless you have a specific reason to widen or narrow.

### Constructor injection — the only kind you should write

For services-that-use-services (i.e. nearly everything), the idiomatic pattern is **constructor injection** with a primary constructor:

```csharp
public sealed class OrderProcessor(
    ITimeProvider clock,
    IOrderRepository repo,
    INotifier notifier)
{
    public async Task ProcessAsync(int orderId, CancellationToken ct)
    {
        var order = await repo.FindAsync(orderId, ct);
        if (order is null) return;

        await repo.MarkProcessedAsync(orderId, clock.GetUtcNow(), ct);
        await notifier.NotifyAsync(order.CustomerEmail, "Order processed", ct);
    }
}
```

There is no separate field declaration. There is no `this.clock = clock` ceremony. Primary constructors (C# 12 / .NET 8) make this the cleanest pattern for DI. Use them for any class that exists to be DI-managed.

You will also occasionally see **property injection** (a public property the framework sets) and **method injection** (a method parameter the framework supplies). Both exist. Both are wrong defaults. Constructor injection is the only kind C9 teaches.

---

## 4. Registration — the full surface

The four methods you'll actually use:

```csharp
// 1. Concrete-to-interface — the common case.
services.AddSingleton<IClock, SystemClock>();

// 2. Concrete-only — when there's no interface (rare in production, common in tests).
services.AddScoped<TodoService>();

// 3. Factory function — when construction needs more than just other services.
services.AddSingleton<IOrderRepository>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new EfOrderRepository(config.GetConnectionString("Orders") ?? throw new InvalidOperationException());
});

// 4. Instance — when you already have one constructed.
var clock = new SystemClock();
services.AddSingleton<IClock>(clock);
```

For *every* registration there are three variants — `AddSingleton`, `AddScoped`, `AddTransient` — taking the same four shapes. Twelve overloads total; you'll memorize them in a week.

### `TryAdd*` for libraries

When you write a library that other apps will consume, register services with `TryAdd*` so you do not clobber a user override:

```csharp
public static class FooServiceCollectionExtensions
{
    public static IServiceCollection AddFoo(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();   // only if not already registered
        return services;
    }
}
```

Inside a single application's `Program.cs` you use `Add*`; inside library extension methods you use `TryAdd*`. We will follow this convention from Week 4 when we start extracting reusable libraries.

### Keyed services (since .NET 8)

When you need *two* implementations of the same interface, distinguished by a key:

```csharp
services.AddKeyedScoped<ITodoRepository, SqlTodoRepository>("sql");
services.AddKeyedScoped<ITodoRepository, MongoTodoRepository>("mongo");
```

Inject with `[FromKeyedServices]`:

```csharp
todos.MapGet("/{id}",
    ([FromKeyedServices("sql")] ITodoRepository repo, int id, CancellationToken ct) => /* ... */);
```

This solved a long-standing pain point. We will use it sparingly in C9 — keys are a code smell if you have more than two or three.

### `IOptions<T>` — the configuration-binding pattern

A separate but related primitive: `IOptions<T>` binds a section of `appsettings.json` to a strongly-typed C# class and registers it in the container.

```csharp
public sealed class EmailOptions
{
    public string SmtpHost { get; init; } = "";
    public int SmtpPort { get; init; } = 587;
    public string From { get; init; } = "noreply@example.com";
}

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

// in a service:
public sealed class EmailNotifier(IOptions<EmailOptions> options) : INotifier
{
    private readonly EmailOptions _config = options.Value;
    /* ... */
}
```

Mention it now; cover it in depth in Week 4. The pattern is **read configuration at startup, never at request time**.

---

## 5. Injection into Minimal API endpoints

Minimal APIs offer two injection surfaces.

### Parameter injection (the default)

Any registered service is injectable as a handler parameter:

```csharp
todos.MapGet("/", async (ITodoStore store, CancellationToken ct) =>
    TypedResults.Ok(await store.ListAsync(ct)));
```

The framework sees `ITodoStore`, asks the container for one, and passes it in. No attribute needed — service injection is the default for any parameter whose type is registered. (`[FromServices]` exists for the ambiguous case where the type might also be parsed from the query string; in practice you almost never need it.)

The lifetime of the resolved service is whatever it was registered as. A scoped `ITodoStore` lives until the response is sent; a singleton one lives forever; a transient one is freshly constructed for this handler.

### Handler classes with constructor injection

For an endpoint with significant logic, extract a class:

```csharp
public sealed class TodoEndpoints(ITodoStore store, IClock clock)
{
    public async Task<Results<Created<Todo>, ValidationProblem>> Create(
        CreateTodoRequest body, CancellationToken ct)
    {
        var created = await store.AddAsync(body, clock.GetUtcNow(), ct);
        return TypedResults.Created($"/api/v1/todos/{created.Id}", created);
    }
}

// In Program.cs:
builder.Services.AddScoped<TodoEndpoints>();
todos.MapPost("/", (TodoEndpoints e, CreateTodoRequest body, CancellationToken ct) => e.Create(body, ct))
     .WithParameterValidation();
```

This is the "endpoint class" pattern. It gets you class-level dependencies plus the testability of plain class methods. We use it in this week's mini-project.

> **Why scoped, not singleton, for endpoint classes?** Because they probably hold a scoped `DbContext` or repository transitively. Registering an endpoint class as a singleton while it depends on scoped services is a **captive dependency** — exactly the bug section 8 warns about. Default to scoped; widen if you have a specific reason.

---

## 6. `IServiceProvider` — the resolver

The container is exposed as `IServiceProvider`. Two methods do most of the work:

```csharp
T?   GetService<T>();         // returns null if not registered
T    GetRequiredService<T>();  // throws if not registered
```

You will rarely call these in application code. You will see them everywhere in framework code and in unit-test setup:

```csharp
// In a test:
var services = new ServiceCollection();
services.AddSingleton<IClock, FakeClock>();
services.AddScoped<TodoService>();

await using var provider = services.BuildServiceProvider();
var todos = provider.GetRequiredService<TodoService>();
```

That is the entire DI container in five lines. No frameworks, no XML, no attributes. Useful to remember when you debug a registration problem: you can always reconstruct the failing scenario in a unit test.

> **`GetRequiredService` over `GetService`.** Production code should treat a missing registration as a fail-fast bug, not a `null` to handle silently. `GetRequiredService` throws an `InvalidOperationException` with a useful message ("No service for type 'X' has been registered"). `GetService` returns `null` and lets the bug travel. We use `GetRequiredService` everywhere in C9.

---

## 7. `IServiceScopeFactory` — escaping the request scope

A subtle point: **`IServiceProvider` itself is scoped in ASP.NET Core**. The `app.Services` you get from `WebApplication` is the *root* provider — using it directly cannot resolve scoped services. The provider that *can* resolve scoped services is the one created per-request by the framework.

So how do you resolve a scoped service from a singleton, or from background work that runs outside any request? Answer: **`IServiceScopeFactory`**.

```csharp
public sealed class TodoCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<TodoCleanupWorker> log)
{
    public async Task RunOnceAsync(CancellationToken ct)
    {
        // Create a scope explicitly — we are not in an HTTP request.
        await using var scope = scopeFactory.CreateAsyncScope();

        // Resolve scoped services from THIS scope.
        var store = scope.ServiceProvider.GetRequiredService<ITodoStore>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var cutoff = clock.GetUtcNow().AddDays(-30);
        var removed = await store.RemoveOlderThanAsync(cutoff, ct);

        log.LogInformation("Removed {Count} stale todos.", removed);
        // scope disposed at end of `using` — any scoped IDisposable services are disposed too.
    }
}
```

The pattern is:

1. Register the singleton/background worker that *needs* scoped services.
2. Inject `IServiceScopeFactory` into it.
3. Create a scope explicitly per unit-of-work.
4. Resolve scoped services from `scope.ServiceProvider`.
5. Dispose the scope (`using` / `await using`) to clean up scoped `IDisposable`s.

This shows up in Week 8 background workers, in `IHostedService` implementations, in domain-event dispatchers, in cache-warming jobs, in cron tasks. It is the single most common "advanced" DI pattern. Internalize it now and you will recognize it for the rest of the course.

---

## 8. The captive-dependency pitfall

The classic DI bug. The one every junior engineer hits exactly once, then never again.

```csharp
// Bug: SINGLETON service depends on a SCOPED service.
public sealed class CachedTodoService(ITodoRepository repo)  // repo is scoped
{
    private Dictionary<int, Todo>? _cache;
    public async Task<Todo?> FindAsync(int id, CancellationToken ct)
    {
        _cache ??= (await repo.ListAsync(ct)).ToDictionary(t => t.Id);
        return _cache.GetValueOrDefault(id);
    }
}

services.AddSingleton<CachedTodoService>();
services.AddScoped<ITodoRepository, EfTodoRepository>();
```

The `CachedTodoService` is singleton — one instance for the entire app. It is constructed once, on the *first* request, with the *first* request's `ITodoRepository`. That repository captures the first request's `DbContext` (which is scoped). The repository (and its `DbContext`) live forever, captive inside the singleton.

Symptoms:

- The `DbContext` is never disposed. Connection-pool exhaustion under load.
- After the first request, the `DbContext` reuses tracked entities across all subsequent requests — concurrent reads see stale data.
- Under enough load, `DbContext` (which is not thread-safe) crashes with an `InvalidOperationException`.

Cure:

- Inject `IServiceScopeFactory` and create a scope per call. (Section 7.)
- *Or* make the cached service scoped if the cache really is per-request.
- *Or* make the repository singleton if it really is stateless and thread-safe (rare for repositories).

### The container's safety net

The container detects *some* captive dependencies at startup and crashes loudly. When `ASPNETCORE_ENVIRONMENT=Development`, `WebApplication.CreateBuilder` turns on **scope validation** automatically. If you wire `AddSingleton<A>` with `A` taking a scoped `B` in its constructor, you get an exception like:

```
System.InvalidOperationException: Cannot consume scoped service 'B'
from singleton 'A'.
```

…on the first resolution attempt, in dev. In production, scope validation is off by default for performance, so the bug only shows up under load. **C9 turns scope validation on in production too**, because we prefer a deterministic crash to a heisenbug. Add this to your `Program.cs` from Week 2 onward:

```csharp
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
```

`ValidateOnBuild` goes one step further: it validates *every* registration at `builder.Build()` time, not lazily on first resolve. Startup is slightly slower; bugs surface immediately. Worth it.

### The harder case — capturing scoped state in transient

The container only catches singleton-holding-scoped. It does **not** catch transient-holding-scoped, because that combination is usually fine. It also does not catch the case where a singleton resolves a scoped service via `IServiceProvider.GetRequiredService<T>()` from inside a method — the validator only checks constructors.

Internalize the rule: **a long-lived service must never hold a reference to a shorter-lived one**. The container helps; it doesn't save you.

---

## 9. The other pitfalls

A short tour of the bugs you will eventually meet.

### Circular dependencies

```csharp
public sealed class A(B b) { /* ... */ }
public sealed class B(A a) { /* ... */ }

services.AddScoped<A>();
services.AddScoped<B>();
```

The container throws `InvalidOperationException: A circular dependency was detected for the service of type 'A'`. Fix: factor out a third service, or invert one direction with an event/callback.

### Resolving a service that wasn't registered

```csharp
var foo = sp.GetRequiredService<IFoo>();
// InvalidOperationException: No service for type 'IFoo' has been registered.
```

You forgot the `AddScoped<IFoo, Foo>()` line. The error message is good — read it.

### `IDisposable` services and lifetime

The container disposes scoped and transient `IDisposable` services automatically:

- **Scoped `IDisposable`** — disposed when its scope ends (end of HTTP request, end of `using var scope`).
- **Transient `IDisposable`** — disposed when its *containing scope* ends. **Transient is not "fire and forget"** for `IDisposable`s — the container holds a reference for cleanup. If you register `HttpClient` as transient and resolve it on every request, you create a memory leak the size of every `HttpClient` you've ever resolved, freed only at app shutdown. (For `HttpClient` specifically, use `IHttpClientFactory`; that's Week 5.)
- **Singleton `IDisposable`** — disposed when the application shuts down.

### Resolving from the wrong provider

```csharp
// In a background task started from Program.cs, BEFORE the request pipeline exists.
var store = app.Services.GetRequiredService<ITodoStore>();   // scoped — throws or warns
```

`app.Services` is the root provider, which has no active scope. Use a scope factory (section 7) or restructure so the resolution happens within a request.

### Constructor parameter injection in a singleton at app startup

Some teams reach for `IConfiguration` in a constructor and read the entire config tree eagerly. That's fine. Some teams reach for `IServiceProvider` itself in a constructor and resolve services *inside* business methods. That works but is a service-locator anti-pattern — it hides dependencies. Prefer to declare dependencies explicitly in the constructor; reach for `IServiceProvider` only when the type itself is genuinely dynamic (e.g. a strategy chosen by a key the caller knows).

---

## 10. A complete example — Minimal API plus three lifetimes

Here is a single file that exercises every concept in this lecture: registration, all three lifetimes, parameter injection, an endpoint class, `IServiceScopeFactory`, and scope validation.

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Turn on strict validation in every environment, not just dev.
builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton<IClock, SystemClock>();           // app-wide
builder.Services.AddSingleton<ITodoStore, InMemoryTodoStore>(); // app-wide cache
builder.Services.AddScoped<RequestContext>();                   // per-request
builder.Services.AddTransient<ITodoIdGenerator, SequentialIdGenerator>(); // fresh each time
builder.Services.AddScoped<TodoEndpoints>();                    // endpoint class

var app = builder.Build();

app.MapOpenApi();

var todos = app.MapGroup("/api/v1/todos").WithTags("Todos");

todos.MapGet ("/",     (TodoEndpoints e, CancellationToken ct) => e.GetAll(ct));
todos.MapGet ("/{id}", (TodoEndpoints e, int id, CancellationToken ct) => e.GetById(id, ct));
todos.MapPost("/",     (TodoEndpoints e, CreateTodoRequest body, CancellationToken ct) => e.Create(body, ct));

// A diagnostic endpoint that proves the three lifetimes.
app.MapGet("/diag/lifetimes", (
    IClock a, IClock b,                          // singleton — a == b across all requests
    RequestContext rc1, RequestContext rc2,      // scoped — rc1 == rc2 within this request
    ITodoIdGenerator g1, ITodoIdGenerator g2) => // transient — g1 != g2
{
    return TypedResults.Ok(new
    {
        clockSame = ReferenceEquals(a, b),
        scopeSame = ReferenceEquals(rc1, rc2),
        transientSame = ReferenceEquals(g1, g2),
        requestId = rc1.Id
    });
});

app.Run();

// ---- services ----

public interface IClock { DateTimeOffset GetUtcNow(); }
public sealed class SystemClock : IClock { public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow; }

public sealed class RequestContext
{
    public Guid Id { get; } = Guid.NewGuid();
}

public interface ITodoIdGenerator { int Next(); }
public sealed class SequentialIdGenerator : ITodoIdGenerator
{
    private static int _seed;
    public int Next() => Interlocked.Increment(ref _seed);
}

public interface ITodoStore
{
    Task<IReadOnlyList<Todo>> ListAsync(CancellationToken ct);
    Task<Todo?>               FindAsync(int id, CancellationToken ct);
    Task<Todo>                AddAsync(CreateTodoRequest body, int id, CancellationToken ct);
}

public sealed class InMemoryTodoStore : ITodoStore
{
    private readonly ConcurrentDictionary<int, Todo> _byId = new();

    public Task<IReadOnlyList<Todo>> ListAsync(CancellationToken _) =>
        Task.FromResult<IReadOnlyList<Todo>>(_byId.Values.OrderBy(t => t.Id).ToList());

    public Task<Todo?> FindAsync(int id, CancellationToken _) =>
        Task.FromResult(_byId.TryGetValue(id, out var t) ? t : null);

    public Task<Todo> AddAsync(CreateTodoRequest body, int id, CancellationToken _)
    {
        var todo = new Todo(id, body.Title, body.Notes, body.DueDate, Done: false);
        _byId[id] = todo;
        return Task.FromResult(todo);
    }
}

public sealed class TodoEndpoints(ITodoStore store, ITodoIdGenerator ids, IClock clock)
{
    public async Task<Ok<IReadOnlyList<Todo>>> GetAll(CancellationToken ct) =>
        TypedResults.Ok(await store.ListAsync(ct));

    public async Task<Results<Ok<Todo>, NotFound>> GetById(int id, CancellationToken ct)
    {
        var t = await store.FindAsync(id, ct);
        return t is null ? TypedResults.NotFound() : TypedResults.Ok(t);
    }

    public async Task<Created<Todo>> Create(CreateTodoRequest body, CancellationToken ct)
    {
        var created = await store.AddAsync(body, ids.Next(), ct);
        _ = clock.GetUtcNow(); // illustrative
        return TypedResults.Created($"/api/v1/todos/{created.Id}", created);
    }
}

public sealed record Todo(int Id, string Title, string? Notes, DateOnly? DueDate, bool Done);
public sealed record CreateTodoRequest(string Title, string? Notes, DateOnly? DueDate);
```

Run it. Hit `/diag/lifetimes` twice and compare the JSON. The `requestId` changes between requests but is identical inside a single request — proving scoped semantics. `clockSame` is always `true`. `transientSame` is always `false`.

That output is the lifetime story made empirical. When in doubt about a lifetime question, add a similar endpoint to your scratch project and ask the runtime.

---

## 11. Recap — the lifetime decision tree

When you sit down to write a new service this week, walk through this list:

1. **Will it hold state shared across all requests?** → Singleton. Thread-safety becomes your responsibility.
2. **Will it hold state that belongs to one request?** → Scoped. The default. Most services land here.
3. **Is it stateless and cheap?** → Transient (or scoped — both are fine; scoped is one less allocation in a hot path).
4. **Does it implement `IDisposable`?** → Whatever lifetime you pick, the container will dispose it. Avoid transient `IDisposable`s unless you understand the lifetime arithmetic.
5. **Will it be consumed by something with a longer lifetime?** → Restructure. Either narrow the consumer or widen the dependency. Or use `IServiceScopeFactory`.
6. **Are you reaching for `IServiceProvider.GetService` inside a method?** → Stop. Declare the dependency in the constructor. The only time you genuinely need the provider in application code is for explicit scope creation (section 7).

That checklist resolves 95% of the lifetime decisions you will make this year. The other 5% are interesting, you will meet them in Week 4 (the options pattern), Week 6 (EF Core's `DbContext` lifetime), and Week 8 (background services). For now: register your services, declare your dependencies, run the diagnostic endpoint, and move on.

---

## 12. Build-succeeded checklist

After this lecture you should be able to:

- Register a service with the right lifetime and justify the choice.
- Inject services into Minimal API endpoint parameters and into endpoint classes via constructors.
- Use `IServiceScopeFactory` to escape the request scope for background work.
- Recognize a captive-dependency bug and propose a fix.
- Turn on `ValidateScopes` and `ValidateOnBuild` and explain what each one catches.
- Use keyed services for the two-implementations-of-one-interface case.

Next, do the exercises — three short drills that exercise each of those skills in isolation.

---

## References

- *Dependency injection in ASP.NET Core* — Microsoft Learn: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection>
- *Dependency injection in .NET (general)* — Microsoft Learn: <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection>
- *Service lifetimes* (deep): <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#service-lifetimes>
- *Keyed services*: <https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#keyed-services>
- *`IServiceCollection` reference*: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.iservicecollection>
- *`IServiceProvider` reference*: <https://learn.microsoft.com/en-us/dotnet/api/system.iserviceprovider>
- *Options pattern in .NET*: <https://learn.microsoft.com/en-us/dotnet/core/extensions/options>
- *`Microsoft.Extensions.DependencyInjection` source*: <https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.DependencyInjection>
- *David Fowler — DI guidelines*: <https://github.com/davidfowl/DotNetCodingPatterns>
