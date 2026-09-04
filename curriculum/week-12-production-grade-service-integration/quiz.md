# Week 12 — Quiz

Ten multiple-choice questions covering composition, JWT bearer across three protocols, Serilog structured logging, OpenTelemetry tracing, EF Core scoping, and integration testing with `WebApplicationFactory` and Testcontainers. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — Composition discipline

ProjectHub registers JWT bearer, Serilog, OpenTelemetry, and EF Core. The week's central rule about cross-cutting concerns is:

- (A) Each protocol surface (REST, gRPC, SignalR) should configure its own auth, logging, and tracing so the surfaces stay independent.
- (B) Configure each cross-cutting concern once, in one block of `Program.cs`, and reuse it across every protocol surface.
- (C) Cross-cutting concerns belong in middleware only; never in service registration.
- (D) Logging and tracing should be disabled in development and enabled only in production to avoid noise.

<details>
<summary>Answer</summary>

**(B).** The integration discipline is "configure cross-cutting concerns once, register them everywhere." Per-surface configuration is how a service ends up with three logging configs and a hub whose `[Authorize]` references a different scheme than REST. The README's `ServiceConfiguration` static-extension pattern (`AddProjectHubAuth`, `AddProjectHubLogging`, `AddProjectHubTelemetry`, `AddProjectHubPersistence`) is the concrete embodiment. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>.

</details>

## Question 2 — One auth scheme, three surfaces

A single `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` registration is intended to protect a REST endpoint, a gRPC method, and a SignalR hub. Which statement is correct?

- (A) The same scheme covers `[Authorize]` on a minimal-API endpoint, a gRPC service method, and a `Hub` class, because all three run on the same ASP.NET Core authentication middleware.
- (B) SignalR requires its own separate authentication scheme because WebSockets are not HTTP.
- (C) gRPC cannot use JWT bearer; it requires mTLS.
- (D) Each surface needs a distinct `AddJwtBearer` call with a distinct scheme name.

<details>
<summary>Answer</summary>

**(A).** REST, gRPC, and SignalR all run on the same ASP.NET Core authentication middleware, so one `AddJwtBearer` registration populates `HttpContext.User` for all three. The only subtlety is the `OnMessageReceived` hook for the SignalR query-string upgrade (Q3). Citation: <https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz>.

</details>

## Question 3 — JWT on the SignalR upgrade

The browser cannot set an `Authorization` header on a WebSocket upgrade. The canonical ASP.NET Core pattern that lets the same JWT scheme authenticate the SignalR negotiate is:

- (A) Send the token in a cookie; SignalR reads cookies automatically.
- (B) Send the token in the `access_token` query string and lift it into `context.Token` in the JWT middleware's `OnMessageReceived` event, gated on `path.StartsWithSegments("/hubs")`.
- (C) Send the token as the first hub message after the upgrade and validate it in `OnConnectedAsync`.
- (D) Disable auth on the hub and re-check the token on every hub-method invocation.

<details>
<summary>Answer</summary>

**(B).** The `access_token` query-string parameter plus an `OnMessageReceived` hook gated on `StartsWithSegments("/hubs")` is the canonical pattern, carried forward from Week 11. (A) cookies are one option but not the canonical JWT pattern; (C)/(D) re-implement what the middleware does for free. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>.

</details>

## Question 4 — The message template is the contract

Two log calls:

```csharp
logger.LogInformation($"Project {project.Id} created");                 // call X
logger.LogInformation("Project {ProjectId} created", project.Id);       // call Y
```

Which is correct, and why?

- (A) X — string interpolation is faster and the `@mt` field captures the value either way.
- (B) Y — the message template form keeps `ProjectId` as a structured field the aggregator can query; X renders the string in application code and loses the structured key.
- (C) They are equivalent; Serilog parses the interpolated string and recovers the `ProjectId` key.
- (D) Both are wrong; structured fields must be passed via `BeginScope` only.

<details>
<summary>Answer</summary>

**(B).** The braces in a message template are placeholder names that become JSON keys, not interpolation. Call Y produces `@mt = "Project {ProjectId} created"` plus a top-level `ProjectId` key the aggregator can filter on. Call X builds the string in application code, so `@mt` is the rendered text and there is no `ProjectId` field to query. Citation: <https://github.com/serilog/serilog> message-template docs.

</details>

## Question 5 — Why the trace ID appears in both logs and spans

Serilog log lines and OpenTelemetry spans share the same `TraceId` for a request. The mechanism is:

- (A) Serilog calls into the OpenTelemetry SDK on every log to fetch the current trace ID.
- (B) OpenTelemetry writes the trace ID into every Serilog log line via a custom sink.
- (C) Both libraries independently read `System.Diagnostics.Activity.Current`; OpenTelemetry's instrumentation creates that activity, and Serilog's `Enrich.FromLogContext()` reads its `TraceId`/`SpanId`. They cooperate through the runtime, not through each other.
- (D) The trace ID is generated by Kestrel and injected into both as a request header.

<details>
<summary>Answer</summary>

**(C).** The two libraries cooperate through `System.Diagnostics.Activity.Current` — OpenTelemetry's instrumentation creates the activity at request start; Serilog's `Enrich.FromLogContext()` reads its `TraceId`/`SpanId` into each log line. Neither library calls the other. This is why the correlation is free once both are wired and silently vanishes if `Enrich.FromLogContext()` is dropped. Citation: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs>.

</details>

## Question 6 — A span silently disappears

You add `using var activity = Source.StartActivity("ProjectCreatedBroadcast");` where `Source = new ActivitySource("ProjectHub")`. The span never appears in the console exporter, but the framework HTTP and SQL spans do. The most likely cause is:

- (A) `StartActivity` returns `null` because the parent request already completed.
- (B) `AddSource("ProjectHub")` is missing from `WithTracing(...)`, so the SDK is not subscribed to that `ActivitySource` and drops its activities.
- (C) Custom spans require `ActivityKind.Server`; `Internal` spans are never exported.
- (D) The console exporter only exports framework-instrumented spans, never application spans.

<details>
<summary>Answer</summary>

**(B).** `AddSource("ProjectHub")` in `WithTracing(...)` is what subscribes the SDK to the application's `ActivitySource`. Without it, framework activities still export (the SDK subscribes to their sources via the instrumentation packages) but application activities are dropped silently. (A) is the *opposite* failure mode (no listener → `null`), but the framework spans appearing tells you a listener exists. Citation: the SDK README at <https://github.com/open-telemetry/opentelemetry-dotnet>.

</details>

## Question 7 — The `DbContext` scoping trap

A singleton `ProjectEventsBroadcaster` needs a `DbContext` to read a project before broadcasting. Injecting `ProjectHubDbContext` directly into its constructor:

- (A) Works fine; the framework creates a fresh context per method call.
- (B) Fails at the first resolution with "Cannot consume scoped service ... from singleton ...", because a scoped `DbContext` cannot be captured by a singleton; the fix is `IDbContextFactory<T>` or resolving a fresh scope via `IServiceScopeFactory`.
- (C) Works but leaks connections; the fix is to call `db.Dispose()` manually.
- (D) Throws only under concurrent load; single-threaded use is safe.

<details>
<summary>Answer</summary>

**(B).** A scoped `DbContext` cannot be captured by a singleton; the DI validation throws "Cannot consume scoped service ... from singleton ..." at the first resolution (on startup in Development, lazily in Production). The fix is `IDbContextFactory<T>.CreateDbContextAsync()` or resolving a fresh scope via `IServiceScopeFactory`. Citation: <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>.

</details>

## Question 8 — What `WebApplicationFactory<Program>` substitutes

An integration test boots the host via `WebApplicationFactory<Program>` and calls `factory.CreateClient()`. The `HttpClient` it returns:

- (A) Opens a real TCP socket to `localhost:5000` and requires the app to be running separately.
- (B) Routes requests through an in-memory `TestServer`'s `HttpMessageHandler` directly into the ASP.NET Core pipeline — same middleware, routes, and auth — with no real socket.
- (C) Mocks every endpoint; the handlers do not actually run.
- (D) Bypasses middleware and calls the endpoint delegate directly.

<details>
<summary>Answer</summary>

**(B).** `WebApplicationFactory` substitutes Kestrel with an in-memory `TestServer` whose `HttpMessageHandler` the returned `HttpClient` uses. Requests flow through the full real pipeline — middleware, routing, auth, endpoint — without a real socket. That is what makes the tests exercise production code paths rather than stubs. Citation: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.

</details>

## Question 9 — Why SignalR tests use long polling

The SignalR integration test in Lecture 3 sets `options.Transports = HttpTransportType.LongPolling`. The reason is:

- (A) Long polling is faster than WebSockets in tests.
- (B) The in-memory `TestServer` does not support WebSocket upgrades; long polling works because each poll is a discrete HTTP request the test server can route through its message-handler pipeline.
- (C) WebSockets cannot carry a JWT in tests.
- (D) Long polling is required to capture the trace ID; WebSockets drop it.

<details>
<summary>Answer</summary>

**(B).** The in-memory `TestServer` is a request/response message-handler pipeline; it has no WebSocket upgrade. Long polling reduces every hub message to a discrete HTTP request the test server can route, so the test runs entirely in-process. Production prefers WebSockets; the test overrides the transport preference. Citation: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests> and the SignalR test guidance.

</details>

## Question 10 — `Program` must be public

The integration-test project fails to compile with `Program is inaccessible due to its protection level`. The host uses top-level statements. The fix is:

- (A) Add `[assembly: InternalsVisibleTo("Tests")]` only — there is no other option.
- (B) Add `public partial class Program { }` after the top-level statements (the implicit `Program` class generated for top-level statements is `internal`), or use `InternalsVisibleTo`; the partial-class line is the cleaner fix.
- (C) Rename `Program.cs` to `Startup.cs`.
- (D) Move all of `Program.cs` into the test project.

<details>
<summary>Answer</summary>

**(B).** Top-level statements generate an `internal Program` class. The test assembly cannot reference an `internal` type without `InternalsVisibleTo`; the cleaner, idiomatic fix is one line — `public partial class Program { }` — after the top-level statements, which promotes the generated class to `public`. Citation: the integration-test docs' "Basic tests with the default WebApplicationFactory" section.

</details>

---

## Self-assessment

- 9-10: you can ship this week's mini-project without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact Microsoft Learn pages.
- 5-6: re-read the lecture notes end to end and redo the exercises, paying particular attention to the trace-ID-correlation and DbContext-scoping sections.
- 0-4: rewind to Lecture 1 and read all three lecture notes carefully. The mini-project assembles every pattern the quiz tests; it will not make sense without the conceptual foundation.
