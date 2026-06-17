// Exercise 2 — Typed routes and binding
//
// Goal: Exercise every parameter-binding source in ASP.NET Core 9's Minimal
//       API model — route, query, body, header, services — and return
//       strongly-typed responses with TypedResults. Build an OpenAPI 3.1
//       document the framework can describe accurately.
//
// Estimated time: 40 minutes.
//
// HOW TO USE THIS FILE
//
// 1. Scaffold a fresh web project:
//
//      mkdir TypedRoutes && cd TypedRoutes
//      dotnet new web -n TypedRoutes -o src/TypedRoutes
//
//    Replace the generated src/TypedRoutes/Program.cs with the contents of
//    THIS FILE.
//
// 2. Fill in the bodies marked `// TODO`. Do not change the public route
//    templates or the handler return types — those are the contract.
//
// 3. Build with zero warnings: `dotnet build`. The exercise is not done
//    until both the build and the smoke requests below pass.
//
// ACCEPTANCE CRITERIA
//
//   [ ] All TODOs implemented.
//   [ ] `dotnet build`: 0 Warning(s), 0 Error(s).
//   [ ] All five endpoints respond correctly to the smoke commands below.
//   [ ] Every endpoint returns a TypedResults<...> (or a single typed result).
//   [ ] No `Results.Ok(...)` (lowercase) anywhere in the file — TypedResults only.
//   [ ] The OpenAPI document at /openapi/v1.json contains all five paths.
//
// SMOKE COMMANDS (after `dotnet run --project src/TypedRoutes`)
//
//   curl -s http://localhost:5099/items/42 | jq .
//   curl -s "http://localhost:5099/items?page=2&size=5" | jq .
//   curl -s -X POST http://localhost:5099/items \
//     -H 'Content-Type: application/json' \
//     -d '{"title":"Buy milk","priority":3}' | jq .
//   curl -s http://localhost:5099/whoami \
//     -H 'X-User-Id: u-123' \
//     -H 'X-Tenant: acme' | jq .
//   curl -s http://localhost:5099/clock | jq .
//
// Inline hints are at the bottom of the file.

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// TODO: Register IClock as a singleton, implemented by SystemClock.
//       This is the first DI line you'll write; section 4 of Lecture 2
//       walks the registration surface in full.


var app = builder.Build();

app.MapOpenApi();

// ---------------------------------------------------------------------------
// Endpoint 1 — route binding
// ---------------------------------------------------------------------------
// GET /items/{id}
//   - `id` comes from the route template.
//   - Return Ok<Item> if id > 0, NotFound if id == 0, BadRequest<string> if id < 0.

app.MapGet("/items/{id:int}", GetItem)
   .WithTags("Items");

static Results<Ok<Item>, NotFound, BadRequest<string>> GetItem(int id)
{
    // TODO: implement the three-way pattern-match against id.
    //       Hint: a switch expression with relational patterns is idiomatic here.
    throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Endpoint 2 — query binding (parameter object)
// ---------------------------------------------------------------------------
// GET /items?page=N&size=M
//   - Use a "parameter object" record whose properties carry [FromQuery].
//   - Default page = 1, size = 20.
//   - Return Ok<PagedResult> echoing the parameters back as a sanity check.

app.MapGet("/items", ListItems)
   .WithTags("Items");

static Ok<PagedResult> ListItems(Pagination paging)
{
    // TODO: build a PagedResult from `paging.Page` and `paging.Size`.
    //       The Items list can be a small placeholder array — the point of
    //       this exercise is the BINDING, not the data.
    throw new NotImplementedException();
}

public sealed record Pagination(
    [FromQuery] int Page = 1,
    [FromQuery] int Size = 20);

public sealed record PagedResult(int Page, int Size, IReadOnlyList<Item> Items);

// ---------------------------------------------------------------------------
// Endpoint 3 — body binding
// ---------------------------------------------------------------------------
// POST /items
//   - `body` is a CreateItemRequest, bound from the JSON request body.
//   - Return Created<Item> with a Location header pointing at /items/{newId}.
//   - For this exercise, use Random.Shared.Next(1, 1000) for the id.

app.MapPost("/items", CreateItem)
   .WithTags("Items");

static Created<Item> CreateItem(CreateItemRequest body)
{
    // TODO: construct an Item from the request, give it a random id, and
    //       return TypedResults.Created with the proper Location URL.
    throw new NotImplementedException();
}

public sealed record CreateItemRequest(string Title, int Priority);

// ---------------------------------------------------------------------------
// Endpoint 4 — header binding
// ---------------------------------------------------------------------------
// GET /whoami
//   - Read X-User-Id (required) and X-Tenant (optional) from the request headers.
//   - If X-User-Id is missing, return BadRequest<string> "X-User-Id header is required."
//   - Otherwise return Ok<WhoAmI> echoing both values back.

app.MapGet("/whoami", WhoAmI)
   .WithTags("Identity");

static Results<Ok<WhoAmI>, BadRequest<string>> WhoAmI(
    [FromHeader(Name = "X-User-Id")] string? userId,
    [FromHeader(Name = "X-Tenant")]  string? tenant)
{
    // TODO: validate userId is present, then return TypedResults.Ok(new WhoAmI(...)).
    throw new NotImplementedException();
}

public sealed record WhoAmI(string UserId, string? Tenant);

// ---------------------------------------------------------------------------
// Endpoint 5 — service injection
// ---------------------------------------------------------------------------
// GET /clock
//   - Inject IClock from the DI container.
//   - Return Ok<ClockTick> { Now = clock.GetUtcNow() }.
//   - The whole point of this endpoint is to prove that registered services
//     are injected as endpoint parameters with NO extra ceremony.

app.MapGet("/clock", Clock)
   .WithTags("Diagnostics");

static Ok<ClockTick> Clock(IClock clock)
{
    // TODO: read clock.GetUtcNow() and return it inside a ClockTick record.
    throw new NotImplementedException();
}

public sealed record ClockTick(DateTimeOffset Now);

// ---------------------------------------------------------------------------
// Domain types and services
// ---------------------------------------------------------------------------

public sealed record Item(int Id, string Title, int Priority);

public interface IClock { DateTimeOffset GetUtcNow(); }

public sealed class SystemClock : IClock
{
    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}

app.Run();

// ---------------------------------------------------------------------------
// EXPECTED OUTPUTS (modulo dates, random ids, exact Kestrel port)
// ---------------------------------------------------------------------------
//
// GET /items/42
//   200 {"id":42,"title":"item-42","priority":1}
//
// GET /items/0
//   404
//
// GET /items/-1
//   400 "id must be a non-negative integer."
//
// GET /items?page=2&size=5
//   200 {"page":2,"size":5,"items":[{"id":1,"title":"sample","priority":1}, ...]}
//
// POST /items  {"title":"Buy milk","priority":3}
//   201 Location: /items/874   body: {"id":874,"title":"Buy milk","priority":3}
//
// GET /whoami  (X-User-Id: u-123, X-Tenant: acme)
//   200 {"userId":"u-123","tenant":"acme"}
//
// GET /whoami  (no headers)
//   400 "X-User-Id header is required."
//
// GET /clock
//   200 {"now":"2026-05-13T18:42:11.123Z"}
//
// ---------------------------------------------------------------------------
// HINTS (read only if stuck >15 min)
// ---------------------------------------------------------------------------
//
// IClock registration:
//   builder.Services.AddSingleton<IClock, SystemClock>();
//
// GetItem:
//   public static Results<Ok<Item>, NotFound, BadRequest<string>> GetItem(int id) => id switch
//   {
//       < 0 => TypedResults.BadRequest("id must be a non-negative integer."),
//       0   => TypedResults.NotFound(),
//       _   => TypedResults.Ok(new Item(id, $"item-{id}", 1))
//   };
//
// ListItems:
//   public static Ok<PagedResult> ListItems(Pagination paging) =>
//       TypedResults.Ok(new PagedResult(paging.Page, paging.Size,
//           [new Item(1, "sample", 1)]));
//
// CreateItem:
//   public static Created<Item> CreateItem(CreateItemRequest body)
//   {
//       var id = Random.Shared.Next(1, 1000);
//       var item = new Item(id, body.Title, body.Priority);
//       return TypedResults.Created($"/items/{id}", item);
//   }
//
// WhoAmI:
//   public static Results<Ok<WhoAmI>, BadRequest<string>> WhoAmI(...) =>
//       string.IsNullOrWhiteSpace(userId)
//           ? TypedResults.BadRequest("X-User-Id header is required.")
//           : TypedResults.Ok(new WhoAmI(userId, tenant));
//
// Clock:
//   public static Ok<ClockTick> Clock(IClock clock) =>
//       TypedResults.Ok(new ClockTick(clock.GetUtcNow()));
//
// ---------------------------------------------------------------------------
// WHY THIS MATTERS
// ---------------------------------------------------------------------------
//
// Every binding source you exercised here shows up on the real APIs you'll
// build in Week 5–8. The parameter-object pattern (Pagination) keeps query
// strings tidy when there are 4+ filters. [FromHeader] is the only [From*]
// attribute that is REQUIRED — every other source has sensible inference.
// `Created<T>` carrying the Location header is the standard 201 response in
// modern REST, and it gives you accurate OpenAPI metadata for free.
//
// ---------------------------------------------------------------------------
