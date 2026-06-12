# Week 12 — Quiz

Ten multiple-choice questions covering composition, JWT auth across REST/gRPC/SignalR, Serilog, OpenTelemetry, EF Core scoping, configuration layering, and integration testing with `WebApplicationFactory` and Testcontainers. Treat the quiz as a closed-book check; the answer key with reasoning is at the bottom.

## Question 1 — The composition rule

A new ASP.NET Core service wires JWT bearer auth, Serilog, OpenTelemetry, and an EF Core `DbContext` into one `Program.cs`. What is the single most important integration discipline for keeping the four cross-cutting concerns consistent across the REST, gRPC, and SignalR surfaces?

- (A) Register each concern separately on each protocol surface so the surfaces stay decoupled.
- (B) Configure each cross-cutting concern exactly once, in one block of `Program.cs`, and reuse that registration on every surface.
- (C) Put each concern in its own microservice so the host stays small.
- (D) Use a different authentication scheme per protocol to keep blast radius small.

## Question 2 — JWT bearer on the SignalR upgrade

REST returns `200 OK` for a valid bearer token in the `Authorization` header, but the SignalR negotiate returns `401` for the same token supplied as `?access_token=...`. The most likely cause is:

- (A) The token is expired; SignalR validates lifetime more strictly than REST.
- (B) The hub's `[Authorize]` names a scheme that was never registered.
- (C) `app.UseAuthentication()` and `app.UseAuthorization()` are in the wrong order.
- (D) The `JwtBearerEvents.OnMessageReceived` hook that lifts the query-string token into `context.Token` for `/hubs/*` paths is missing.

## Question 3 — Why scope `OnMessageReceived` to `/hubs/*`

The `OnMessageReceived` hook reads the bearer token from the `access_token` query string only when the request path starts with `/hubs`. Removing that path gate and accepting the query-string token on every endpoint is dangerous because:

- (A) It would break REST endpoints that legitimately use the `access_token` query parameter for OAuth.
- (B) URLs are logged in proxy access logs, browser history, and `Referer` headers; accepting a query-string credential on every surface leaks a usable token into logs that never needed query-string auth.
- (C) It would double the auth latency on REST requests.
- (D) The query string has a 2048-byte limit that a JWT would exceed.

## Question 4 — Serilog's structured template

`logger.LogInformation("Project {ProjectId} status changed to {Status}", id, status)` is preferred over `logger.LogInformation($"Project {id} status changed to {status}")` because:

- (A) String interpolation is slower at runtime.
- (B) The message-template form keeps the message and the data separable, so a log aggregator can index and filter on the `ProjectId` field without regex-grepping the rendered message text.
- (C) Interpolation is not supported by `ILogger<T>`.
- (D) The template form is required for the log line to be valid JSON.

## Question 5 — The OpenTelemetry trace ID

A REST `POST /api/projects/{id}/tasks/{taskId}/status` updates a task via EF Core and broadcasts to SignalR. With OpenTelemetry wired correctly, what stitches the inbound HTTP span, the Npgsql `UPDATE` span, and the broadcast span into one trace without the developer threading a trace-ID parameter through every method signature?

- (A) A static `Dictionary<int, string>` keyed by managed thread id.
- (B) A `traceId` field on the `DbContext` that the developer sets manually.
- (C) `Activity.Current` flows through the `async`/`await` continuation chain via `AsyncLocal`, so child spans parent themselves under the ambient activity automatically.
- (D) The Serilog enricher copies the trace id into every log line, and OpenTelemetry reads it back from the logs.

## Question 6 — Console exporter fields

Reading the OpenTelemetry console exporter output, which set of fields identifies a span and its place in the trace tree?

- (A) `TraceId`, `SpanId`, `ParentSpanId`, `OperationName`, `Duration`.
- (B) `ConnectionId`, `RequestId`, `UserId`, `Timestamp`.
- (C) `LogLevel`, `Category`, `EventId`, `Message`.
- (D) `Method`, `Path`, `StatusCode`, `ContentLength`.

## Question 7 — DbContext lifetime

ProjectHub injects `ProjectHubDbContext` into REST handlers, gRPC service methods, and a long-lived singleton broadcaster that periodically writes to the database. Which statement is correct?

- (A) All three can safely capture the same scoped `DbContext` instance in a field.
- (B) REST and gRPC get a per-request scoped (or pooled) context; the singleton broadcaster must **not** capture a `DbContext` directly — it resolves a fresh scope via `IServiceScopeFactory` or uses `IDbContextFactory<T>`.
- (C) The singleton should register the `DbContext` as a singleton so it never has to create a scope.
- (D) `DbContext` is thread-safe, so a singleton holding one shared instance is fine.

## Question 8 — The scoping error

A singleton service captures a scoped `DbContext` and two concurrent requests touch it at once. The runtime throws:

- (A) `ObjectDisposedException: Cannot access a disposed context.`
- (B) `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.`
- (C) `NpgsqlException: too many connections.`
- (D) `AuthenticationException: the DbContext requires a JWT.`

## Question 9 — Configuration precedence

The PostgreSQL connection string is defined in `appsettings.json`, overridden in `appsettings.Production.json`, and an environment variable `ConnectionStrings__ProjectHub` is also set on the production host. Which value wins, and why?

- (A) `appsettings.json`, because it is loaded last.
- (B) `appsettings.Production.json`, because environment-specific files always win.
- (C) The environment variable, because in the default ASP.NET Core configuration order environment variables are added after the JSON files and later providers override earlier ones.
- (D) It is non-deterministic; whichever provider loads first on a given boot wins.

## Question 10 — Testcontainers and `WebApplicationFactory`

An integration test class uses `WebApplicationFactory<Program>` plus a Testcontainers `PostgreSqlContainer`. The standard pattern for pointing the booted app at the ephemeral container's connection string is:

- (A) Hard-code `localhost:5432` and hope the container binds that port.
- (B) Start the container in `IAsyncLifetime.InitializeAsync`, then in a `CustomWebApplicationFactory` override `ConfigureWebHost` / `ConfigureTestServices` to replace the `DbContext` registration's connection string with `container.GetConnectionString()`.
- (C) Edit `appsettings.json` on disk before each test run.
- (D) Set a global static field that `Program.cs` reads at startup.

---

## Answer key

- **Q1: (B).** Integration discipline is "configure the cross-cutting concern once, register it everywhere." Copying each isolated example forward yields three logging configs, two serializer settings, and a `DbContext` that is scoped on one path and singleton on another — the failure surfaces the first time a request crosses two surfaces. The model is the `ServiceConfiguration` static-extension pattern (`AddProjectHubAuth`, `AddProjectHubLogging`, `AddProjectHubTelemetry`, `AddProjectHubPersistence`). Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/web-host>.
- **Q2: (D).** The browser `WebSocket` API cannot set request headers on the upgrade, so the SignalR client puts the token in `?access_token=`. Without the `OnMessageReceived` hook lifting it into `context.Token`, the default handler reads only the `Authorization` header, finds nothing, and returns `401`. (A) is ruled out because the same token authenticates REST; (B) would throw a `500`, not a clean `401`; (C) would break REST too. This is Challenge 2. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication>.
- **Q3: (B).** Query strings live in the URL, and URLs are logged everywhere a header is not. Scoping the hook to `/hubs/*` keeps the query-string-credential blast radius to exactly the surface that has no alternative. This is the same threat model as the Week 11 upgrade. Citation: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>.
- **Q4: (B).** Serilog's message templates keep the structured fields (`ProjectId`, `Status`) as first-class properties on the log event, so the aggregator (Seq, Loki, Datadog, Elastic) can filter `ProjectId = ...` without parsing the rendered string. Interpolation collapses everything into one opaque message. (A) and (C) are false; interpolation is supported and not meaningfully slower here. Citation: <https://github.com/serilog/serilog/wiki/Structured-Data>.
- **Q5: (C).** `Activity.Current` is an `AsyncLocal`; it flows across `await` continuations automatically. The Npgsql and SignalR instrumentations read the ambient activity and parent their spans under it, so one inbound request produces one connected trace with no manual plumbing. Citation: <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing>.
- **Q6: (A).** A span is identified by `TraceId` (the trace it belongs to), `SpanId` (itself), and `ParentSpanId` (its parent in the tree); `OperationName` and `Duration` describe what it did and how long it took. The other option sets are log fields, logging-category fields, and HTTP-request fields — none of which describe a span's position in the trace tree. Citation: <https://opentelemetry.io/docs/concepts/signals/traces/>.
- **Q7: (B).** REST and gRPC are per-request, so a scoped or pooled `DbContext` is correct there. A singleton outlives any request scope; capturing a scoped context in a singleton field is the classic captive-dependency bug. The singleton resolves a fresh scope per unit of work via `IServiceScopeFactory`, or injects `IDbContextFactory<T>` and creates a context per call. Citation: <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues>.
- **Q8: (B).** `DbContext` is not thread-safe and serves one operation at a time. Two concurrent operations on one instance throw `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.` This is exactly what the captive-singleton-context bug produces under load. Citation: <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues>.
- **Q9: (C).** The default `WebApplicationBuilder` configuration order is: `appsettings.json`, then `appsettings.{Environment}.json`, then user secrets (dev), then environment variables, then command-line args. Later providers override earlier ones, so the `ConnectionStrings__ProjectHub` environment variable wins over both JSON files. The order is deterministic, which is why (D) is wrong. Citation: <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/#default-configuration>.
- **Q10: (B).** The container's port is assigned dynamically, so you must read `container.GetConnectionString()` after `StartAsync()` and inject it into the booted app — typically by subclassing `WebApplicationFactory<Program>`, removing the existing `DbContextOptions` registration in `ConfigureTestServices`, and re-registering it against the container's connection string. Hard-coding ports or editing files on disk is brittle and breaks under parallel test runs. Citation: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests#customize-webapplicationfactory> and <https://github.com/testcontainers/testcontainers-dotnet>.

## Self-assessment

- 9-10: you can ship this week's mini-project without further reading.
- 7-8: re-read the lecture notes on the questions you missed; the citations point to the exact Microsoft Learn pages.
- 5-6: re-read the lecture notes end to end and redo the exercises, especially the EF Core scoping and the cross-protocol auth.
- 0-4: rewind to Lecture 1 and read all three lecture notes carefully. The mini-project will not make sense without the conceptual foundation.
