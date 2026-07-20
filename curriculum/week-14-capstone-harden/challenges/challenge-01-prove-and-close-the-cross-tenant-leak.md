# Challenge 1 — Prove a Cross-Tenant Data Leak Exists, Close It, and Add the Test That Catches It Forever

> **Time:** 2 hours. **Prerequisites:** Exercises 1 and 2; Milestone 1 (Week 13) complete. **Citations:** OWASP API1 at <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>, EF Core global query filters at <https://learn.microsoft.com/en-us/ef/core/querying/filters>, the integration-test chapter at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, and Testcontainers for .NET at <https://github.com/testcontainers/testcontainers-dotnet>.

## The premise

You inherit the `PolyglotWorkshop` repo at the end of Milestone 1. It works: instructors create lessons, learners enroll, learners submit exercises, and the analytics surface aggregates progress. Every endpoint requires a valid bearer token from Keycloak. The team is proud of the green Testcontainers baseline.

There is a cross-tenant data leak in it, and the baseline tests do not catch it — because every baseline test uses a *single* tenant. The leak is **broken object-level authorization (OWASP API1)**: at least one read path loads a row by id and returns it without checking that the row belongs to the caller's tenant. A learner in the "Northside Bootcamp" tenant can read a submission, a review, or an enrollment that belongs to the "Riverside Academy" tenant by supplying its id.

This challenge is not "write a new feature." It is the skill the harden week exists to build: find the boundary that leaks, close it at the *structural* layer so it cannot be reintroduced, and write the multi-tenant integration test that makes the leak impossible to ship again. You will spend more of your career proving a boundary holds than building a new one.

By the end you will have produced: (a) a failing test that *demonstrates* the leak, (b) the structural fix, and (c) the same test now passing, plus a sweep proving no sibling endpoint leaks the same way.

## Setup

Stand up the stack from your Milestone 1 repo with the integration-test harness (Keycloak + PostgreSQL via Testcontainers). Seed two tenants and two learners:

```bash
dotnet run --project src/Workshop.Api
# in another terminal, mint tokens for two tenants from the dev Keycloak realm
export TOKEN_A=$(./scripts/mint-token.sh --tenant northside --sub learner-a)
export TOKEN_B=$(./scripts/mint-token.sh --tenant riverside --sub learner-b)

# learner B creates a submission; capture its id
SUB_B=$(curl -s -X POST http://localhost:8080/api/submissions \
  -H "Authorization: Bearer $TOKEN_B" -H 'Content-Type: application/json' \
  -d '{"exerciseId":"...","content":"riverside-only"}' | jq -r .id)

# learner A reads it — THIS SHOULD FAIL, but on the un-hardened branch it returns 200
curl -s -i http://localhost:8080/api/submissions/$SUB_B -H "Authorization: Bearer $TOKEN_A" | head -1
```

If the last command prints `HTTP/1.1 200 OK` and a body containing `riverside-only`, you have reproduced the leak. Confirm the same on the gRPC surface:

```bash
grpcurl -plaintext -H "authorization: Bearer $TOKEN_A" \
  -d "{\"id\":\"$SUB_B\"}" localhost:8080 workshop.v1.SubmissionService/GetSubmission
```

## The diagnostic plan

### Step 1 — find every "load by id" read path

Grep the codebase for the leak's signature — a query that filters by id alone:

```bash
grep -rn "FindAsync\|\.FirstOrDefaultAsync\|SingleOrDefaultAsync" src/Workshop.Api \
  | grep -iv "tenant"
```

Every hit that does not also constrain `TenantId` is a candidate. The capstone has several: `GetSubmission`, `GetReview`, `GetEnrollment`. Enumerate them before fixing one — the leak is rarely a single endpoint.

### Step 2 — write the demonstrating test first (red)

A bug that reached a branch lacked a test. Write the cross-tenant test against `WebApplicationFactory<Program>` + Testcontainers, with the `Testcontainers.Keycloak` realm the harness seeds. It must fail on the un-hardened branch:

```csharp
[Fact]
public async Task GetSubmission_returns_404_for_another_tenants_row()
{
    var tenantA = await _factory.IssueTokenAsync(tenant: "northside", sub: "learner-a");
    var tenantB = await _factory.IssueTokenAsync(tenant: "riverside", sub: "learner-b");

    // tenant B creates a submission
    var clientB = _factory.CreateAuthenticatedClient(tenantB);
    var created = await clientB.PostAsJsonAsync("/api/submissions",
        new CreateSubmissionRequest(SeededExerciseId, "riverside-only"));
    var id = (await created.Content.ReadFromJsonAsync<SubmissionDto>())!.Id;

    // tenant A tries to read it
    var clientA = _factory.CreateAuthenticatedClient(tenantA);
    var response = await clientA.GetAsync($"/api/submissions/{id}");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);   // 404, not 200, not 403
}
```

Run it. On the un-hardened branch it fails with `Expected 404 but found 200`. That failure is the proof the test has teeth.

### Step 3 — close it structurally (green)

Do not fix it only at the one handler — fix it where it cannot be reintroduced. Add the EF Core global query filter for every tenant-owned entity:

```csharp
modelBuilder.Entity<Submission>().HasQueryFilter(s => s.TenantId == _tenantProvider.TenantId);
modelBuilder.Entity<Review>().HasQueryFilter(r => r.TenantId == _tenantProvider.TenantId);
modelBuilder.Entity<Enrollment>().HasQueryFilter(e => e.TenantId == _tenantProvider.TenantId);
```

Now `db.Submissions.FindAsync(id)` is automatically scoped; the cross-tenant read returns `404` even where a handler forgot the `Where`. The test goes green. Re-run the gRPC reproduction from setup — it now returns `NOT_FOUND` too, because gRPC shares the `DbContext`.

### Step 4 — sweep the siblings

For each "load by id" path you found in Step 1, write the same cross-tenant test. They should all pass now (the global filter covers them) — but the *tests* are the deliverable, because the filter could be removed and only the tests would catch it. Add one negative test that *proves the filter is load-bearing*: a test that calls `IgnoreQueryFilters()` and asserts the row *is* visible cross-tenant, documenting exactly what the filter is protecting.

## Acceptance criteria

1. `LEAK.md` enumerates every "load by id" read path found in Step 1 and marks which leaked (returned another tenant's row) before the fix.
2. The demonstrating test (Step 2) is committed in its failing form first (captured output in `LEAK.md`), then shown passing after the fix.
3. The EF Core global query filter is applied to `Submission`, `Review`, and `Enrollment`, and the cross-tenant read returns `404` on both REST and gRPC.
4. A cross-tenant test exists for *every* sibling read path, not just the one in the premise.
5. The "filter is load-bearing" negative test (using `IgnoreQueryFilters()`) is present and documents what the filter protects.
6. The within-tenant happy path still returns `200` — you closed the leak without breaking legitimate access.

## Stretch goals

1. **Tenant-scoped at the database, too.** Add a PostgreSQL row-level security policy on the `submissions` table keyed on a session variable the app sets per request, so even a raw `SELECT` outside EF cannot cross tenants. Compare the defense-in-depth to the app-layer filter and write 150 words on when you would want both. Cite the Npgsql provider at <https://github.com/npgsql/efcore.pg> and PostgreSQL RLS at <https://www.postgresql.org/docs/current/ddl-rowsecurity.html>.
2. **The audit escape hatch, tested.** The outbox drainer uses `IgnoreQueryFilters()` deliberately (Lecture 2). Write a test proving the drainer *does* cross tenants (it must, to broadcast for all of them) while the request path *does not*. Two tests, opposite expectations, same filter. Cite <https://learn.microsoft.com/en-us/ef/core/querying/filters>.
3. **Mass-assignment sibling (API3).** Prove that `POST /api/submissions` with a body containing `"grade": "A+"` or `"tenantId": "<other>"` does not set those server-owned fields, then add the test. Cite <https://owasp.org/API-Security/editions/2023/en/0xa3-broken-object-property-level-authorization/>.

Cited pages: OWASP API1 at <https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/>, EF Core query filters at <https://learn.microsoft.com/en-us/ef/core/querying/filters>, the integration-test chapter at <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>, Testcontainers for .NET at <https://github.com/testcontainers/testcontainers-dotnet>, and the EF Core security guidance at <https://learn.microsoft.com/en-us/ef/core/miscellaneous/security>.
