# Challenge 1 — Keycloak as a Test Dependency: Import a Realm, Mint a Real Token, Validate It Against the Running Backend

> **Time:** 2.5 hours. **Prerequisites:** Exercises 1–3, and Week 7 of C9 (OIDC against Keycloak). **Citations:** the Testcontainers Keycloak module at <https://dotnet.testcontainers.org/modules/keycloak/>, Keycloak realm import/export at <https://www.keycloak.org/server/importExport>, securing apps with Keycloak at <https://www.keycloak.org/docs/latest/securing_apps/>, and JWT bearer auth in ASP.NET Core at <https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication>.

## The premise

Exercise 3 left `SliceHarness.TokenForAsync` as a `NotImplementedException`. That stub is doing real work in the design: it is the seam where the integration test gets a *real* bearer token from a *real* identity provider, so the test validates the actual auth path rather than a stub. This challenge fills it in honestly — a Keycloak realm imported into the Testcontainer at startup, a seeded client and two seeded users, a token minted against the container's token endpoint, and an assertion that the backend's JWT middleware accepts it (and rejects a forged one).

By the end you will have: a `workshop-realm.json` that seeds everything the slice needs, a `TokenForAsync` that mints real tokens, and a negative test proving the backend rejects a token from the wrong issuer.

## Why a real Keycloak and not a stub

The cheap path is to register a fake authentication handler in the test factory that always produces a `ClaimsPrincipal` with the right `sub`. It is fast, and it tests nothing about auth: it does not exercise the OIDC discovery, the signing-key fetch, the issuer/audience validation, the expiry check, or the `sub` and `tenant` claim extraction the service depends on. A capstone whose whole story includes "OIDC via Keycloak" must test the real thing, or the "via Keycloak" is a claim with no green behind it. Testcontainers makes a real Keycloak cheap enough to run on every push, so there is no excuse for the stub.

## Part 1 — The realm JSON

Keycloak imports a realm from a JSON file mounted into `/opt/keycloak/data/import/` when started with `--import-realm`. The realm declares: the realm name (`workshop`), a confidential client (`workshop-api`) that the test uses for the password grant, and two users with passwords. Create `tests/Workshop.IntegrationTests/Realms/workshop-realm.json`:

```json
{
  "realm": "workshop",
  "enabled": true,
  "sslRequired": "none",
  "accessTokenLifespan": 3600,
  "clients": [
    {
      "clientId": "workshop-api",
      "enabled": true,
      "publicClient": false,
      "secret": "test-secret",
      "directAccessGrantsEnabled": true,
      "standardFlowEnabled": true,
      "protocol": "openid-connect",
      "defaultClientScopes": ["openid", "profile"],
      "attributes": { "access.token.lifespan": "3600" }
    }
  ],
  "users": [
    {
      "username": "instructor-1",
      "enabled": true,
      "emailVerified": true,
      "credentials": [{ "type": "password", "value": "test-password", "temporary": false }],
      "attributes": { "tenant": ["acme"] },
      "realmRoles": ["instructor"]
    },
    {
      "username": "learner-1",
      "enabled": true,
      "emailVerified": true,
      "credentials": [{ "type": "password", "value": "test-password", "temporary": false }],
      "attributes": { "tenant": ["acme"] },
      "realmRoles": ["learner"]
    }
  ],
  "roles": {
    "realm": [{ "name": "instructor" }, { "name": "learner" }]
  }
}
```

Two configuration choices matter. `"sslRequired": "none"` lets the container serve plaintext, which is correct for an ephemeral test container and would be wrong in production. `"directAccessGrantsEnabled": true` enables the password (Resource Owner Password Credentials) grant — the simplest way for a *test* to obtain a token without a browser redirect. The password grant is a test convenience; production clients use the authorization-code-with-PKCE flow the MAUI and Blazor clients use, and the realm leaves `standardFlowEnabled` on for them too.

The `tenant` user attribute is what becomes the `tenant` claim the `WorkshopService.TenantOf` reads. To make Keycloak put it in the access token, you also need a protocol mapper; the simplest realm-JSON form adds it to the client's `protocolMappers`. Add this to the `workshop-api` client:

```json
"protocolMappers": [
  {
    "name": "tenant",
    "protocol": "openid-connect",
    "protocolMapper": "oidc-usermodel-attribute-mapper",
    "config": {
      "user.attribute": "tenant",
      "claim.name": "tenant",
      "jsonType.label": "String",
      "access.token.claim": "true",
      "id.token.claim": "false"
    }
  }
]
```

## Part 2 — Mint the token in `TokenForAsync`

With the realm imported, `TokenForAsync` does a direct-grant POST to the realm's token endpoint and returns the `access_token`:

```csharp
public async Task<string> TokenForAsync(string username, string role)
{
    using var http = new HttpClient();
    var form = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"]    = "password",
        ["client_id"]     = "workshop-api",
        ["client_secret"] = "test-secret",
        ["username"]      = username,
        ["password"]      = "test-password",
        ["scope"]         = "openid",
    });

    var resp = await http.PostAsync(
        $"{_fixture.Issuer}/protocol/openid-connect/token", form);
    resp.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("access_token").GetString()!;
}
```

`_fixture.Issuer` is `{Keycloak.GetBaseAddress()}realms/workshop` — the same value flowed into the backend's `Oidc:Authority`, so the token the test mints and the issuer the backend trusts are the *same realm*. That alignment is the whole point: the backend fetches `{authority}/.well-known/openid-configuration`, gets the signing keys, and validates the token the test minted, with zero stubbing.

## Part 3 — The positive and negative assertions

The positive path is the exercise-3 slice test, now green because the token is real. Add the negative path — a forged token from the *wrong* issuer must be rejected:

```csharp
[Fact]
public async Task Token_from_wrong_issuer_is_rejected()
{
    await using var harness = await new SliceHarness(_fixture).BuildAsync();

    // A hand-rolled JWT signed with a key the backend does not trust.
    var forged = ForgeToken(issuer: "https://evil.example/realms/workshop", sub: "instructor-1");
    var client = harness.GrpcClient(forged);

    var ex = await Assert.ThrowsAsync<RpcException>(() =>
        client.CreateLessonAsync(new CreateLessonRequest { Title = "x", Body = "y" }).ResponseAsync);

    Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
}
```

`ForgeToken` builds a JWT with `System.IdentityModel.Tokens.Jwt` signed with a random key. The backend rejects it because (a) the issuer does not match the configured authority and (b) the signature does not verify against Keycloak's published keys. The test proves the rejection is real, not incidental.

## Deliverables

1. `Realms/workshop-realm.json` with the `workshop-api` client (with the `tenant` protocol mapper), the two seeded users, and the realm roles. Confirm import with `docker logs <keycloak-id>` showing the `Imported realm workshop` line.
2. A working `TokenForAsync` that mints a real token; the exercise-3 slice test is green.
3. The `Token_from_wrong_issuer_is_rejected` negative test, green.
4. A short `CHALLENGE-01.md` write-up answering: (a) why the password grant is acceptable for tests but not production; (b) what the backend fetches from the issuer's discovery document and why; (c) what would change in the realm JSON to also test a `RequireRole("instructor")` authorization policy.

## Stretch goals

- **Authorization, not just authentication.** Add a `RequireRole("instructor")` policy to `CreateLesson` and prove a `learner-1` token is rejected with `PermissionDenied` (status `7`), while `instructor-1` succeeds. This requires the realm role to land in the token as a claim the ASP.NET Core role-mapping reads.
- **Token expiry.** Set `accessTokenLifespan` to `2` seconds in a dedicated test realm, mint a token, wait it out, and prove the backend rejects the expired token. (Use a clock the test controls rather than a real `Task.Delay` if you want it fast.)
- **Container reuse.** Add `.WithReuse(true)` to the Keycloak builder and measure the test-suite startup saving across two consecutive runs. Document the trade-off (faster local loop vs. the reused container persisting state between runs) and why CI should *not* reuse. Cite <https://dotnet.testcontainers.org/api/resource_reuse/>.
