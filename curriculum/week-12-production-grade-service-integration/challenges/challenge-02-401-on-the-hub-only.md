# Challenge 2 — REST Returns 200, the SignalR Negotiate Returns 401: Find the One Misconfiguration

> **Time:** 2 hours. **Prerequisites:** Exercises 1, 2, 3. Challenge 1 is helpful but not required. **Citations:** the JWT bearer chapter at <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn>, the SignalR auth chapter at <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, the middleware-order chapter at <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>, and `JwtBearerEvents.cs` at <https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/JwtBearer/src/JwtBearerEvents.cs>.

## The premise

You inherit a branch of ProjectHub. The REST surface works: `GET /api/whoami` with a bearer token returns `200` and a claims array, and `POST /api/projects` creates a project and returns `201`. The gRPC surface works: `grpcurl ... projecthub.Projects/WhoAmI` with the bearer token returns the subject and org. But the SignalR hub is broken — every browser that tries to connect to `/hubs/events` fails, and the Network tab shows the negotiate request returning `401 Unauthorized` even though the same token works everywhere else.

The bug is real and it is the single most common cross-protocol auth failure in the wild. This challenge is not "write new code." It is "diagnose a misconfiguration in a service that is 95% correct, using the evidence the observability layer already produces." That is the skill the integration week exists to build: you will spend more of your career finding the one wrong line in a working service than writing a service from scratch.

By the end you will have produced: (a) a four-hypotheses diagnostic log that enumerates every way a token valid on REST can fail on the SignalR upgrade, (b) the evidence that isolates the real cause, and (c) the one-line fix plus an integration test that would have caught it.

## Setup

A starter branch ships in `challenges/starter-ch2/` with a deliberately-broken `AddProjectHubAuth`. It compiles, boots, and serves REST and gRPC correctly. Run it with the console exporter:

```bash
dotnet run --project src/ProjectHub
# in another terminal, mint a token and exercise all three surfaces
export TOKEN=$(curl -s -X POST http://localhost:5000/dev/mint-token | jq -r .access_token)

curl -s http://localhost:5000/api/whoami -H "Authorization: Bearer $TOKEN" | jq        # expect 200
grpcurl -plaintext -H "authorization: Bearer $TOKEN" localhost:5000 projecthub.Projects/WhoAmI   # expect OK
curl -s -i "http://localhost:5000/hubs/events/negotiate?access_token=$TOKEN" -X POST | head -1   # observe 401
```

The third command is the failure. Your job is to explain why the first two pass and the third fails, with the same token.

## The four hypotheses

There are exactly four ways a JWT that authenticates on REST can fail on the SignalR negotiate. The starter branch contains exactly one of them. Enumerate all four before you start grepping; the discipline of writing them down is what separates a five-minute fix from a two-hour flail.

### Hypothesis A — the `OnMessageReceived` hook is missing or its path predicate is wrong

A browser cannot set an `Authorization` header on a WebSocket upgrade, so SignalR's canonical pattern (from Week 11) is to send the token in the `access_token` query string and lift it into the request in the JWT bearer middleware's `OnMessageReceived` event:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) &&
            path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```

If this hook is absent, the negotiate request has no `Authorization` header and no `context.Token`, so the bearer handler finds no token and returns `401`. The REST surface is unaffected because REST clients *do* send the header. **Symptom:** the negotiate is `401`; a `Console.WriteLine(context.HttpContext.Request.Path)` inside the hook never prints (because the hook does not exist), or prints but the `StartsWithSegments` predicate is `/hub` (missing the `s`) or `/signalr` (wrong segment) and never matches `/hubs/events/negotiate`.

### Hypothesis B — middleware order: `UseAuthentication` is after `MapHub`, or `UseRouting` is misplaced

The pipeline order is load-bearing (Lecture 1). The authentication middleware must run before the endpoint that needs the identity. If `app.UseAuthentication()` is registered *after* `app.MapHub<ProjectEventsHub>(...)` — or after a terminal middleware that short-circuits — the hub endpoint runs without a populated `HttpContext.User` and the hub's `[Authorize]` attribute rejects it. **Symptom:** the negotiate is `401`; the `OnMessageReceived` hook *does* print the path and *does* set `context.Token`, but `HttpContext.User.Identity.IsAuthenticated` is `false` at the hub. The give-away is that REST works only because its endpoints happen to sit after `UseAuthentication` in the route table while the hub does not — order matters per-endpoint when the call sites are split.

### Hypothesis C — the token's `aud`/`iss` is validated differently for the hub path

If someone added a second named scheme (the `InternalRpc` pattern from the Exercise 1 stretch) and accidentally pointed the hub's `[Authorize(AuthenticationSchemes = "InternalRpc")]` at it, the hub validates the token against a *different* `TokenValidationParameters` — a different signing key, issuer, or audience. The token minted by `/dev/mint-token` is valid for the default scheme but not for `InternalRpc`. **Symptom:** the negotiate is `401`; the log line reads `IDX10214: Audience validation failed` or `IDX10501: Signature validation failed`, naming a different audience or key than the REST surface used. This is subtle because everything *looks* wired — there really is an `OnMessageReceived`, the order really is correct — but the scheme the hub authorizes against is the wrong one.

### Hypothesis D — `MapHub` is missing `.RequireAuthorization()` semantics, masking a different 401

The inverse failure: the hub is anonymous (no `[Authorize]`), the negotiate returns `200`, but the *first invocation* returns `401` or the connection drops. This is not the starter's bug (its negotiate is `401`), but enumerate it anyway — a negotiate that succeeds and a connection that immediately dies is a different fault tree, and confusing the two costs an hour.

## The diagnostic plan

### Step 1 — confirm the token is good

Decode the token at <https://jwt.io/> (or `echo $TOKEN | cut -d. -f2 | base64 -d | jq`). Confirm `iss`, `aud`, `exp`, and the `org_id` claim. If the token itself is malformed, stop — that is a token-minting bug, not a cross-protocol bug, and it would also fail REST. Since REST passes, the token is good; this step rules out the trivial explanation in thirty seconds.

### Step 2 — turn on the bearer-handler diagnostics

The JWT bearer handler logs its decision at `Information` and the *reason* for a failure at `Information`/`Warning`. Make sure Serilog is at `Information` for `Microsoft.AspNetCore.Authentication` and re-issue the negotiate. Read the structured log line:

```bash
docker logs projecthub-ch2 2>&1 | grep -i "Microsoft.AspNetCore.Authentication" | jq -r '."@mt"' | tail -5
```

The handler emits one of: `No SecurityTokenValidator available for token` (no token reached the handler → Hypothesis A), `Bearer was not authenticated. Failure message: IDX10...` (a token reached the handler but failed validation → Hypothesis C), or nothing at all for the `/hubs` path while emitting lines for `/api` (the auth middleware never ran on that path → Hypothesis B).

### Step 3 — instrument the hook

Add the temporary `Console.WriteLine` inside `OnMessageReceived` (or, better, `context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>().LogInformation("OnMessageReceived path={Path} hasToken={HasToken}", path, !string.IsNullOrEmpty(accessToken))`). Re-issue the negotiate. The line either prints (hook present, evaluate the predicate) or does not (Hypothesis A: hook absent).

### Step 4 — read the trace

The OpenTelemetry console exporter produces a `Server` span for the negotiate even when it returns `401`. The span carries `http.response.status_code: 401` and, if the auth middleware ran, an event recording the challenge. Compare the negotiate span's tags to the `GET /api/whoami` span's tags. The difference between a span that authenticated and one that did not is visible in the events list — the failing one has an `AuthenticationFailed` event the passing one lacks. This is the observability layer paying for itself: the evidence is already captured; you only have to read it.

## The fix

Once you have isolated the hypothesis, the fix is one to three lines:

- **Hypothesis A:** add the `OnMessageReceived` hook, or correct the `StartsWithSegments("/hubs")` predicate.
- **Hypothesis B:** move `app.UseAuthentication()` (and `app.UseAuthorization()`) above the `MapHub`/`MapControllers` calls — concretely, `UseRouting` → `UseAuthentication` → `UseAuthorization` → endpoint mapping, in that order.
- **Hypothesis C:** point the hub's `[Authorize]` at the default scheme (drop the `AuthenticationSchemes = "InternalRpc"`), or align the two schemes' validation parameters.

The starter's bug is **Hypothesis A with a typo**: the predicate reads `path.StartsWithSegments("/hub")` (singular), which never matches `/hubs/events/negotiate`. The token is dropped, the handler sees no token, the negotiate returns `401`. The fix is a single character. The point of the challenge is that you found it from the logs in fifteen minutes instead of staring at `Program.cs` for two hours.

## The regression test

A bug that reached a branch is a bug that lacked a test. Write the integration test (Lecture 3 pattern) that would have caught it — a SignalR client that connects with a valid token and asserts the connection reaches `Connected`:

```csharp
[Fact]
public async Task Hub_accepts_a_valid_bearer_token_via_query_string()
{
    var orgId = Guid.NewGuid();
    var token = TestTokenIssuer.IssueToken("test-user", orgId, _factory);
    var hubUrl = new Uri(_factory.Server.BaseAddress, "/hubs/events");
    var handler = _factory.Server.CreateHandler();

    await using var connection = new HubConnectionBuilder()
        .WithUrl(hubUrl, options =>
        {
            options.HttpMessageHandlerFactory = _ => handler;
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            options.Transports = HttpTransportType.LongPolling;
        })
        .Build();

    await connection.StartAsync();
    connection.State.Should().Be(HubConnectionState.Connected);
}
```

Run it against the broken branch: it fails with a `401` on `StartAsync`. Apply the fix; it passes. That is the proof the test has teeth — a test that passes before and after the fix would not have caught the bug.

## Acceptance criteria

1. `DIAGNOSIS.md` enumerates all four hypotheses (A–D) with the symptom each produces, written *before* the root cause is identified.
2. The bearer-handler log evidence (Step 2) is captured and pasted into `DIAGNOSIS.md`, with the specific line that isolates the cause highlighted.
3. The OpenTelemetry trace comparison (Step 4) between the passing `/api/whoami` span and the failing negotiate span is included, naming the tag or event that differs.
4. The one-line fix is applied and the negotiate now returns `200` with a JSON body containing `connectionId` and `availableTransports`.
5. The regression test (above) is added, demonstrably fails on the broken branch and passes on the fixed branch, and the failing-then-passing output is captured in `DIAGNOSIS.md`.

## Stretch goals

1. **Reproduce Hypothesis B.** Deliberately move `app.UseAuthentication()` below `app.MapHub(...)`. Observe that the negotiate now fails *differently* — the `OnMessageReceived` hook prints and sets the token, but the hub still rejects because the identity was never built. Capture both the working-A-fix and the broken-B states side by side and write 150 words on why the symptoms differ. Cite <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>.
2. **Reproduce Hypothesis C.** Add the `InternalRpc` second scheme from the Exercise 1 stretch, point the hub at it, and observe the `IDX10214` audience-validation log line. Then write the test that distinguishes "the token was missing" from "the token was present but invalid for this scheme" — two different `401`s with two different log signatures. Cite `JwtBearerEvents.cs`.
3. **A health probe for auth wiring.** Add a `/health/auth` endpoint that mints an internal token and exercises all three surfaces in-process at startup, failing the readiness probe if any surface rejects a token the others accept. Discuss why a smoke test of cross-protocol auth belongs in the readiness check, not just the integration suite. Cite the health-checks chapter at <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks>.

Cited Microsoft Learn pages: <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn>, <https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz>, <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/>. Source-link references: `JwtBearerHandler.cs` and `JwtBearerEvents.cs` in `dotnet/aspnetcore`'s `Security/Authentication/JwtBearer`, and `HttpConnectionDispatcher.cs` for the negotiate path. External: the `Microsoft.IdentityModel.Tokens` repository at <https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet> for the `IDX10*` error catalogue.
