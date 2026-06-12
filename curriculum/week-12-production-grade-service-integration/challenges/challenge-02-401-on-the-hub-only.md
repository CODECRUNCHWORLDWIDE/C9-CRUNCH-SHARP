# Challenge 2 — 401 on the Hub Only: Diagnose a Service Where REST Returns 200 but the SignalR Negotiate Returns 401

> **Time:** 2 hours. **Prerequisites:** Exercises 1, 2, 3, 4. Challenge 1 is helpful but not required. **Citations:** the SignalR auth chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, the JWT bearer chapter at <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn>, the `JwtBearerEvents` source-link at <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/JwtBearer/src/JwtBearerEvents.cs>, and the middleware-ordering chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/#middleware-order>.

## The premise

This challenge is not "write a feature." It is "find the bug." You will be handed a ProjectHub host that boots cleanly, passes its REST smoke test, and serves `GET /api/whoami` with a `200 OK` for a valid token — and yet every SignalR client that connects with the *same token* gets a `401 Unauthorized` on the negotiate request and never reaches the hub. The symptom is the single most common cross-protocol auth failure in the field: the auth pipeline works for one protocol surface and silently fails for another, because the two surfaces feed the token to the middleware differently.

This is a deliberately seeded bug. The point is the **diagnostic flow** — the discipline of forming a small set of hypotheses, ruling each one in or out with a cheap observation, and arriving at the fix by elimination rather than by guess-and-recompile. Senior engineers are not faster typists; they are faster at narrowing the search space. By the end you will have written the four-hypotheses log the README's diagnostic-flow objective demands and identified the one misconfiguration that applies.

## The broken host

Start from your Exercise 1 host (REST + gRPC + SignalR behind one JWT scheme) and apply this seeded change. The JWT registration below is **wrong on purpose** — read it, do not fix it yet:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        // NOTE: no Events block. The OnMessageReceived hook that pulls the
        // token from the access_token query string is missing.
    });
```

The REST surface works because a REST client sends `Authorization: Bearer <token>` in a header, and the default `JwtBearerHandler` reads the `Authorization` header out of the box. The SignalR negotiate works *over HTTP*, so in principle it could read the same header — but the browser `WebSocket` API cannot set request headers on the upgrade, so the canonical SignalR client puts the token in the `access_token` **query string** instead. With no `OnMessageReceived` hook to lift the query-string token into `context.Token`, the handler finds no header, finds no token, and returns `401`. REST is green; the hub is red. That is the exact shape of the failure you are diagnosing.

## The reproduction

1. Run the broken host: `dotnet run`.
2. Mint a token:
   ```bash
   TOKEN=$(curl -ks -X POST https://localhost:5001/dev/mint-token \
     -H "content-type: application/json" -d '{}' | jq -r .access_token)
   ```
3. Confirm REST is healthy (this should be `200`):
   ```bash
   curl -ks -i -H "authorization: bearer $TOKEN" https://localhost:5001/api/whoami | head -1
   # HTTP/2 200
   ```
4. Confirm the hub negotiate fails with the query-string token (this is the bug — `401`):
   ```bash
   curl -ks -i -X POST \
     "https://localhost:5001/hubs/events/negotiate?negotiateVersion=1&access_token=$TOKEN" | head -1
   # HTTP/2 401   <-- the symptom
   ```
5. As a control, confirm the negotiate *would* pass if the token were in the header (proving the token itself is valid and the validation parameters are correct):
   ```bash
   curl -ks -i -X POST -H "authorization: bearer $TOKEN" \
     "https://localhost:5001/hubs/events/negotiate?negotiateVersion=1" | head -1
   # HTTP/2 200   <-- header works; query string does not
   ```

Step 5 is the single most valuable observation in the whole challenge. It collapses the search space immediately: the token is valid, the signing key is right, the issuer and audience are right, the clock skew is fine — because the same token in a *header* authenticates the negotiate. The only thing different between the failing call and the passing call is **where the token lives on the request**. That is the entire bug, and step 5 found it in one `curl`.

## The four hypotheses

The README asks you to "reason through the four possible misconfigurations." Here they are, each with the cheap observation that rules it in or out. Write these up as your deliverable — a real on-call engineer keeps exactly this kind of log.

### Hypothesis 1 — The token is invalid or expired

**Test:** put the token in the `Authorization` header on the negotiate (step 5 above). **Result:** `200`. **Verdict:** ruled out. A token that authenticates one request is not invalid; if it were, the header path would also `401`. The clock-skew, issuer, audience, and signing-key parameters are all correct. Stop suspecting the token.

### Hypothesis 2 — The hub's `[Authorize]` references a different scheme than REST

**Test:** read the `[Authorize]` attribute on `EventsHub` and on the REST `RequireOrg` policy. **Result:** both default to `JwtBearerDefaults.AuthenticationScheme`; there is only one `AddJwtBearer` registration. **Verdict:** ruled out. If the hub named a scheme that was never registered, the error would be a `500`/`InvalidOperationException` ("No authenticationScheme was specified"), not a clean `401`. A clean `401` means the scheme *ran* and rejected the request — which means the scheme exists and is wired to the hub.

### Hypothesis 3 — Middleware ordering: `UseAuthentication`/`UseAuthorization` are reversed or missing

**Test:** read `Program.cs`; confirm `app.UseAuthentication()` precedes `app.UseAuthorization()` and both precede the endpoint mapping. **Result:** the order is correct (REST proves it — a misordered pipeline would `401` the REST call too, but REST is `200`). **Verdict:** ruled out. Ordering bugs are *symmetric* across protocols: they break REST and the hub together. A failure that is asymmetric — green on one surface, red on another — is almost never an ordering bug.

### Hypothesis 4 — The query-string token is never lifted into `context.Token`

**Test:** the negotiate passes with a header token and fails with a query-string token (steps 4 and 5). **Result:** asymmetric by token *location*. **Verdict:** confirmed. The `JwtBearerHandler` reads the `Authorization` header by default and nothing else. The browser cannot set that header on a WebSocket upgrade, so the SignalR client puts the token in `access_token`. Without an `OnMessageReceived` event that copies the query-string value into `context.Token` for `/hubs/*` paths, the handler never sees the token and returns `401`. This is the seeded bug.

The discipline to internalize: **hypotheses 1, 2, and 3 were each killed by a single cheap observation before any code was touched.** Three of the four were ruled out without a recompile. That is what "fast at debugging" actually means.

## The fix

Restore the `Events` block. The hook lifts the query-string token into `context.Token`, but only for hub paths — REST and gRPC keep using the `Authorization` header, so a stray `?access_token=` on a REST URL is ignored:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken)
            && path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```

The `path.StartsWithSegments("/hubs")` gate is not cosmetic. Without it, you would accept a bearer token from the query string on *every* endpoint, including REST — which means a logged URL (proxy access logs, browser history, the `Referer` header) leaks a usable credential on a surface that never needed query-string auth. Scoping the hook to `/hubs/*` keeps the query-string-token blast radius to exactly the surface that requires it. This is the same threat-model reasoning we applied to the Week 11 SignalR upgrade; the citation is the SignalR auth chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz#bearer-token-authentication>.

Re-run step 4 after the fix. The negotiate now returns `200`, and a `HubConnection` built with `.WithUrl(url, o => o.AccessTokenProvider = () => Task.FromResult(token))` connects and reaches `OnConnectedAsync`.

## Acceptance criteria

1. You produce a written **four-hypotheses log** (the four headings above, each with its test, result, and verdict) that arrives at Hypothesis 4 by elimination, not by guessing.
2. The log explicitly notes that step 5 (header token on the negotiate) is the observation that ruled out hypotheses 1–3 in one call, and explains *why* an asymmetric, token-location-dependent failure points at the query-string hook and away from ordering and scheme bugs.
3. After your fix, all four `curl` checks pass: REST `200` with header, negotiate `401` removed, negotiate `200` with query-string token, negotiate `200` with header token.
4. A real `HubConnection` client (use the .NET `Microsoft.AspNetCore.SignalR.Client` package, or a `wscat`/browser client) connects with the query-string token, invokes `BroadcastTest`, and receives the echoed broadcast.
5. You add a one-line comment above the restored `Events` block citing the SignalR auth chapter, so the next reader understands why the hook exists and why it is scoped to `/hubs/*`.

## Stretch goals

1. **Catch it in CI.** Add an xUnit integration test (using the `CustomWebApplicationFactory` from Exercise 4) that asserts the negotiate returns `200` for a query-string token. Re-introduce the seeded bug and confirm the test goes red. This is the difference between "we fixed it" and "it can never silently regress" — a 401-on-the-hub bug that ships to production is one an integration test would have caught for the cost of fifteen lines. Cite <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>.
2. **Make the failure loud, not silent.** A `401` with an empty body tells the client nothing. Add an `OnChallenge` event that, in development only, writes a diagnostic header (`WWW-Authenticate: Bearer error="invalid_token", error_description="no token found on request"`) so the next developer who hits this sees *why* the request was rejected, not just *that* it was. Discuss why this must be development-only (you do not want to hand an attacker a description of your auth internals). Cite the `JwtBearerEvents.OnChallenge` source at <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/JwtBearer/src/JwtBearerEvents.cs>.
3. **Trace the rejection.** With OpenTelemetry wired (Exercise 2), confirm that the failing negotiate still produces a span — a `401` is a completed request, not a dropped one — and that the span's `http.response.status_code` is `401`. Write 150 words on how a metrics dashboard that alarms on "negotiate 401 rate > 1%" would have surfaced this bug in production within minutes of deploy, long before a user filed a ticket. This is the observability payoff: the trace makes the silent failure visible.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn>, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/#middleware-order>, <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>. Source-link references: `JwtBearerEvents.cs` and `JwtBearerHandler.cs` in `dotnet/aspnetcore` under `src/Security/Authentication/JwtBearer/src/`.
