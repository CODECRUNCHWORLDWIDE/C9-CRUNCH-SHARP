# Exercise 1 — Hello, API

**Goal:** Scaffold a real ASP.NET Core 9 Minimal API from a blank folder, map four endpoints, hit them with `curl`, and read the OpenAPI document the framework emits — all from the terminal.

**Estimated time:** 35 minutes.

---

## Setup

You need the .NET 9 SDK installed. Verify:

```bash
dotnet --info
```

You should see `9.0.x` listed under "Host" and "SDKs installed." If you do not, install from <https://dotnet.microsoft.com/en-us/download/dotnet/9.0> before going further.

You also need `curl` (preinstalled on macOS and Linux; available on Windows). Optional but recommended: `jq` for pretty-printing JSON.

---

## Step 1 — Scaffold the solution

```bash
mkdir HelloApi && cd HelloApi
dotnet new sln -n HelloApi
dotnet new gitignore
git init
dotnet new web -n HelloApi -o src/HelloApi
dotnet sln add src/HelloApi/HelloApi.csproj
```

You now have:

```
HelloApi/
├── HelloApi.sln
├── .gitignore
└── src/
    └── HelloApi/
        ├── HelloApi.csproj
        ├── Program.cs
        ├── appsettings.json
        └── appsettings.Development.json
```

Commit:

```bash
git add .
git commit -m "Initial solution with Minimal API"
```

---

## Step 2 — Read the generated `Program.cs`

Open `src/HelloApi/Program.cs`. It is short:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
```

Run it:

```bash
dotnet run --project src/HelloApi
```

You should see something close to:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5099
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

The exact port varies. Note it down.

In a second terminal:

```bash
curl http://localhost:5099/
```

You should see `Hello World!`.

`Ctrl+C` to stop.

---

## Step 3 — Add OpenAPI and Swagger UI

Open `Program.cs` and replace its contents with:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "HelloApi v1");
    });
}

app.MapGet("/", () => "Hello World!");

app.Run();
```

Add the Swagger UI viewer package:

```bash
dotnet add src/HelloApi package Swashbuckle.AspNetCore.SwaggerUI
```

(`Microsoft.AspNetCore.OpenApi` is included transitively by the web SDK in .NET 9 — `AddOpenApi()` is available without an extra package reference in modern templates. If your build complains it can't find `AddOpenApi`, run `dotnet add src/HelloApi package Microsoft.AspNetCore.OpenApi` and re-run.)

Build and run:

```bash
dotnet run --project src/HelloApi
```

In a second terminal:

```bash
curl http://localhost:5099/openapi/v1.json | jq .
```

You should see an OpenAPI 3.1 document with one path — `GET /` — and nothing else. Open `http://localhost:5099/swagger` in a browser; you should see the Swagger UI rendering the same document.

`Ctrl+C` to stop.

---

## Step 4 — Map four endpoints

Replace `Program.cs` with this longer version:

```csharp
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "HelloApi v1");
    });
}

// 1. GET / — plain text
app.MapGet("/", () => "Hello World!")
   .WithTags("Greeting");

// 2. GET /greet/{name} — typed JSON response
app.MapGet("/greet/{name}", Greet)
   .WithTags("Greeting")
   .WithName("Greet");

// 3. GET /math/{a}/{op}/{b} — typed result with possible 400
app.MapGet("/math/{a:int}/{op}/{b:int}", Math)
   .WithTags("Math");

// 4. POST /echo — body binding
app.MapPost("/echo", Echo)
   .WithTags("Echo");

app.Run();

static Ok<Greeting> Greet(string name) =>
    TypedResults.Ok(new Greeting($"Hello, {name}!", DateTimeOffset.UtcNow));

static Results<Ok<MathResult>, BadRequest<string>> Math(int a, string op, int b) =>
    op switch
    {
        "add" => TypedResults.Ok(new MathResult(a + b)),
        "sub" => TypedResults.Ok(new MathResult(a - b)),
        "mul" => TypedResults.Ok(new MathResult(a * b)),
        "div" when b == 0 => TypedResults.BadRequest("Division by zero is not allowed."),
        "div" => TypedResults.Ok(new MathResult(a / b)),
        _     => TypedResults.BadRequest($"Unknown op '{op}'. Expected one of: add, sub, mul, div.")
    };

static Ok<EchoResponse> Echo(EchoRequest body) =>
    TypedResults.Ok(new EchoResponse(body.Message, body.Message.Length, DateTimeOffset.UtcNow));

public sealed record Greeting(string Message, DateTimeOffset At);
public sealed record MathResult(int Value);
public sealed record EchoRequest(string Message);
public sealed record EchoResponse(string Echoed, int Length, DateTimeOffset At);
```

Build and run:

```bash
dotnet build
dotnet run --project src/HelloApi
```

In a second terminal, hit every endpoint:

```bash
curl -s http://localhost:5099/                          # → Hello World!
curl -s http://localhost:5099/greet/Ada | jq .          # → {"message":"Hello, Ada!", ...}
curl -s http://localhost:5099/math/3/add/4 | jq .       # → {"value":7}
curl -s http://localhost:5099/math/3/div/0              # → "Division by zero is not allowed."  (400)
curl -s -X POST http://localhost:5099/echo \
        -H 'Content-Type: application/json' \
        -d '{"message":"hello"}' | jq .                 # → {"echoed":"hello","length":5, ...}
```

Open `http://localhost:5099/swagger` in a browser. You should see four endpoints under two tags. Click on `GET /math/{a}/{op}/{b}` — the OpenAPI document records both the 200 response shape (`MathResult`) and the 400 response shape (`string`) because the handler's return type is `Results<Ok<MathResult>, BadRequest<string>>`. That precision is the whole point of `TypedResults`.

`Ctrl+C` to stop.

Commit:

```bash
git add .
git commit -m "Four endpoints with TypedResults and OpenAPI"
```

---

## Step 5 — Use an `.http` file

Both VS Code (with the REST Client extension or the C# Dev Kit) and JetBrains Rider can execute `*.http` files. They are plain text, live in Git next to your source, and replace a Postman collection for 95% of what you need.

Create `src/HelloApi/HelloApi.http`:

```http
@HelloApi_HostAddress = http://localhost:5099

### Greet by name
GET {{HelloApi_HostAddress}}/greet/Ada
Accept: application/json

### Math: 3 + 4
GET {{HelloApi_HostAddress}}/math/3/add/4
Accept: application/json

### Math: divide by zero (expect 400)
GET {{HelloApi_HostAddress}}/math/3/div/0
Accept: application/json

### Echo
POST {{HelloApi_HostAddress}}/echo
Content-Type: application/json
Accept: application/json

{
  "message": "hello, http file"
}

### OpenAPI document
GET {{HelloApi_HostAddress}}/openapi/v1.json
Accept: application/json
```

Open this file in your editor and click the "Send Request" code lens next to each `###` block. (In VS Code with REST Client, the lens reads "Send Request"; in Rider, there's a green arrow in the gutter.) Each request and its response shows up inline.

`*.http` files are how we will exercise APIs through the rest of C9. Get used to them now.

Commit:

```bash
git add .
git commit -m "Add HelloApi.http with example requests"
```

---

## Step 6 — Read the OpenAPI document

```bash
curl -s http://localhost:5099/openapi/v1.json | jq '.paths'
```

You should see something like:

```json
{
  "/": { "get": { ... } },
  "/greet/{name}": { "get": { ... } },
  "/math/{a}/{op}/{b}": { "get": { ... } },
  "/echo": { "post": { ... } }
}
```

Dig into one entry:

```bash
curl -s http://localhost:5099/openapi/v1.json | jq '.paths."/math/{a}/{op}/{b}".get.responses'
```

You should see both the `200` and `400` responses, with their schemas. The schemas are also defined in `$.components.schemas`. **This document is what your clients will generate from.** When you reach Week 9, gRPC will give you the same kind of contract through a different format; the principle is the same.

---

## Acceptance criteria

You can mark this exercise done when:

- [ ] You have a `HelloApi/` folder with `HelloApi.sln`, `src/HelloApi/`, and a `.gitignore`.
- [ ] `dotnet build` from the root prints `0 Warning(s)` and `0 Error(s)`.
- [ ] `dotnet run --project src/HelloApi` starts Kestrel and prints its listening URL.
- [ ] All four `curl` commands above produce the expected response.
- [ ] Swagger UI at `/swagger` renders four endpoints under two tags.
- [ ] `HelloApi.http` is committed and every request in it produces the expected response when run from your editor.
- [ ] You have at least 3 Git commits with sensible messages.

---

## Stretch

- Add a `GET /math/{a}/{op}/{b}` endpoint that uses `decimal` instead of `int`. What happens to OpenAPI's number type?
- Add an `--port` option to the launch settings (`Properties/launchSettings.json`) so the app always listens on `5099`. Useful when the random port keeps changing on every `dotnet run`.
- Add a `Directory.Build.props` at the solution root that turns `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on. Rebuild and confirm nothing breaks.
- Open the generated `obj/Debug/net9.0/HelloApi.GeneratedRequestDelegate.g.cs` file (if your build is configured to surface it) — that's the source generator for Minimal API delegates. Skim it. It will make sense by Week 12.

---

## Hints

<details>
<summary>If <code>AddOpenApi()</code> is not recognized</summary>

You probably need the `Microsoft.AspNetCore.OpenApi` package explicitly. Run `dotnet add src/HelloApi package Microsoft.AspNetCore.OpenApi`. In .NET 9 the package is included by default with the web SDK, but older preview builds occasionally need it explicit.

</details>

<details>
<summary>If <code>dotnet run</code> picks a different port every time</summary>

Edit `src/HelloApi/Properties/launchSettings.json`. Find the `http` profile and set `"applicationUrl": "http://localhost:5099"`. Same idea for `https` if you want that profile fixed too.

</details>

<details>
<summary>If <code>curl</code> on POST returns 415 Unsupported Media Type</summary>

You forgot the `-H 'Content-Type: application/json'` header. Minimal APIs only attempt JSON body binding when the request advertises `application/json`.

</details>

<details>
<summary>If <code>curl</code> returns a problem-details JSON with status 400</summary>

That's `AddProblemDetails()` doing its job: a bad route value (e.g. non-integer `{a}`) is rejected before your handler runs, with a problem-details body explaining what failed. Good. Read the response to see the format.

</details>

---

When this exercise feels comfortable, move to [Exercise 2 — Typed routes and binding](exercise-02-typed-routes.cs).
