# Challenge 1 — Close the OWASP API Security Top 10, Each Item Proven by a Test, Indexed in THREATMODEL.md

> **Time:** 2 hours. **Prerequisites:** Lecture 1, Exercises 1-2. **Citations:** the OWASP API Security Top 10 (2023) at <https://owasp.org/API-Security/editions/2023/en/0x11-t10/>, the resource-based authorization chapter at <https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased>, and the rate-limiting chapter at <https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit>.

## The premise

You have closed one BOLA hole (Exercise 1) and moved your cross-cutting concerns into a pipeline (Exercise 2). This challenge is the milestone's security spine: drive **every applicable OWASP API Top 10 item** to a *closed* state, where "closed" means there is an integration test that proves the deny path and a row in `THREATMODEL.md` that names the item, the mitigation, and the test. A boundary without a row is a boundary nobody threat-modeled; an item without a test is a claim nobody verified.

By the end you will have: a `THREATMODEL.md` covering the three boundaries with STRIDE; a test class per OWASP item; and a green CI run that fails if any deny path regresses.

## The work, item by item

For each item below, implement the mitigation (most are already sketched in Lecture 1) and write the test that proves it. The workshop's seeded principals are `alice`/`bob` (learners, tenant-1), `carol` (instructor, tenant-1), and `dave` (instructor, tenant-2).

### API1 — Broken Object Level Authorization (BOLA)

- **Mitigation:** resource-based authz on every object-by-id endpoint (HTTP and gRPC).
- **Prove:** `alice` GET `bob`'s submission → 404. `dave` (other tenant) GET `bob`'s submission → 404 even though dave is an instructor. Repeat for every endpoint that names an object by id — enumerate them from your threat model.
- **Test:** `BolaTests` with one `[Theory]` per object-by-id route.

### API2 — Broken Authentication

- **Mitigation:** audit `TokenValidationParameters` — `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` all `true`; clock skew set deliberately (default 5 min is often too generous).
- **Prove:** an expired token → 401. A token with the wrong `aud` → 401. A token signed by the wrong key → 401. A token for the wrong realm/issuer → 401.
- **Test:** `AuthenticationTests` minting deliberately-broken tokens from the Testcontainers Keycloak (or a second signing key).

### API3 — Broken Object Property Level Authorization (BOPLA)

- **Mitigation:** every response is a DTO allow-list (Exercise 3); every request binds to a request DTO with only settable fields. No endpoint returns or binds an entity.
- **Prove (read):** a submission response body contains no `InternalNotes`, no `LearnerEmail`. A profile response from a stranger contains no `EmailVerifiedAtUtc`.
- **Prove (write / mass-assignment):** POST a lesson with `{"title":"x","tenantId":"tenant-2"}` in the body and assert the created lesson's tenant is the caller's tenant-1, not tenant-2.
- **Test:** `BoplaTests` asserting absent fields on reads and ignored fields on writes.

### API4 — Unrestricted Resource Consumption

- **Mitigation:** rate-limiting middleware (per-user token bucket) on the expensive surface; pagination cap (`Math.Clamp(pageSize, 1, 100)`); a request-body size limit.
- **Prove:** the 101st request in a burst from one user → 429 with `Retry-After`. A request for `?pageSize=1000000` returns ≤100 rows. A request body over the limit → 413.
- **Test:** `ResourceConsumptionTests` firing a burst and asserting the 429, and asserting the clamped page size.

### API5 — Broken Function Level Authorization (BFLA)

- **Mitigation:** policy-gated instructor functions; deny-by-default fallback policy so a new endpoint is gated unless opted out.
- **Prove:** `alice` (learner) POST `/api/lessons` → 403. `alice` DELETE a submission → 403. `carol` (instructor) can do both.
- **Test:** `BflaTests` asserting 403 for learners on every instructor-only function and 200/204 for instructors.

### API6 — Unrestricted Access to Sensitive Business Flows

- **Mitigation:** the "submit on behalf" and "bulk grade" flows are throttled per-user beyond the general limit; the enrollment flow rejects more than N enrollments/minute per user.
- **Prove:** a script that tries to enroll 1,000 times in a minute is throttled.
- **Note in THREATMODEL.md:** which flows are "sensitive" and why; this is a judgment call you must write down.

### API7 — Server-Side Request Forgery (SSRF)

- **Mitigation:** the workshop's only outbound-fetch surface is the lesson "import from URL" feature. Allow-list the hosts; reject `localhost`, link-local (169.254.0.0/16), and private ranges; never follow redirects to a blocked host.
- **Prove:** an import of `http://169.254.169.254/latest/meta-data` (the cloud metadata endpoint) → 400. An import of `http://localhost:9090` → 400.
- **Test:** `SsrfTests` asserting blocked hosts are rejected.

### API8 — Security Misconfiguration

- **Mitigation:** HTTPS redirection on; security headers (`X-Content-Type-Options: nosniff`, a restrictive CSP for the Blazor admin, `Strict-Transport-Security`); detailed errors off in production; the dev-only `/dev/token` endpoint absent outside Development.
- **Prove:** a response carries the security headers; the production build does not map `/dev/token`.
- **Test:** `MisconfigurationTests` asserting headers present and `/dev/token` returns 404 in the Production environment.

### API9 — Improper Inventory Management

- **Mitigation:** the `.proto` and the OpenAPI document are the single source of truth; there are no undocumented endpoints; deprecated routes return `Sunset`/`Deprecation` headers.
- **Prove:** a test enumerates the mapped endpoints and asserts each appears in the OpenAPI document (no shadow endpoints).
- **Note in THREATMODEL.md:** the versioning policy and where the contract lives.

### API10 — Unsafe Consumption of APIs

- **Mitigation:** the Keycloak token endpoint and the lesson-import fetch are wrapped in Polly (timeout + retry + circuit breaker); responses are size-capped and content-type-checked before parsing.
- **Prove:** a slow/oversized upstream response is timed out rather than buffered unbounded.
- **Test:** `UnsafeConsumptionTests` with a stub upstream that stalls.

## THREATMODEL.md — the required structure

```markdown
# Polyglot Workshop — Threat Model

## Boundaries
1. Minimal API over HTTP            (Kestrel :443)
2. gRPC service over HTTP/2         (Kestrel :443, gRPC)
3. SignalR hub over the WS upgrade  (/hubs/presence)

## STRIDE per boundary
### Boundary 1 — Minimal API
| STRIDE | Threat | Mitigation | OWASP | Test |
|--------|--------|------------|-------|------|
| S | unauth'd caller | JWT bearer, deny-by-default fallback policy | API2 | AuthenticationTests |
| T/I | read/write others' objects | resource-based authz | API1 | BolaTests |
| I | property over-exposure | DTO allow-lists, ProjectTo | API3 | BoplaTests |
| E | learner calls instructor fn | policy-gated functions | API5 | BflaTests |
| D | resource exhaustion | rate limiting, pagination cap | API4 | ResourceConsumptionTests |
| R | deny having acted | audit log w/ sub + TraceId | — | (observability) |
| I | SSRF via import | host allow-list | API7 | SsrfTests |

### Boundary 2 — gRPC   (same rows, gRPC status codes)
### Boundary 3 — SignalR hub   (token-in-query review, [Authorize] on hub)
```

Every deny-path test in your suite must trace back to a row here, and every row must name a real test.

## Acceptance criteria

1. **Every applicable OWASP item is either closed-with-a-test or explicitly marked N/A in THREATMODEL.md with a one-line justification.** (API7 may be partly N/A if you have no outbound fetch; say so.)
2. **`dotnet test` is green**, and the security test classes run in CI on every PR.
3. **THREATMODEL.md indexes every test** and covers all three boundaries with STRIDE.
4. **A deliberately-introduced regression fails a test.** Remove one resource-based check and watch `BolaTests` go red; restore it. Screenshot both for your write-up.

## Deliverable

`challenges/01-owasp-closed/`: the `THREATMODEL.md`, the security test classes, and a 300-word write-up naming which item was hardest to close in the workshop's domain and why (in most workshop builds it is API1/BOLA, because the object graph is deep — submissions belong to lessons belong to instructors belong to tenants — and every level is a place to leak). Cite the OWASP per-item pages for each mitigation.
