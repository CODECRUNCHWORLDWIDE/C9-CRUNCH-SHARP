// Exercise 4 — Write the RUNBOOK Deploy + Rollback Procedures and Execute a REAL Rollback.
//
// Goal: write the two most-used runbook procedures (deploy, rollback), then do a
// real revision rollback against your LIVE Azure Container Apps deployment while
// nothing is on fire — because the moment you discover your rollback does not work
// should never be the moment you need it. The deliverable is RUNBOOK.md plus the
// captured terminal session proving the rollback round-tripped.
//
// This is a capstone milestone, and it is the rollback CONTRACT from the README:
// every deployed revision is reversible in one command, and you have run it once.
// Citation: https://learn.microsoft.com/en-us/azure/container-apps/revisions
//
// The C# below is a SMOKE CHECKER you run before and after the rollback to prove
// the service stayed (or returned to) healthy. It is the "verify it worked" half
// of every runbook procedure, expressed as code you can paste into a console app:
//
//   dotnet new console -n Workshop.Smoke -f net9.0 && cd Workshop.Smoke
//   # replace Program.cs with this file's PART 1
//   dotnet run -- https://workshop-api.<region>.azurecontainerapps.io

// ============================================================================
// PART 1 — Program.cs: a deploy/rollback smoke checker.
// Polls /health, asserts 200 + {"status":"Healthy"}, reports the served
// revision (the API echoes it from an env var ACA injects), and exits non-zero
// if the service is not healthy within the timeout.
// ============================================================================

using System.Diagnostics;
using System.Net;

var baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:8080";
var timeout = TimeSpan.FromSeconds(60);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
var sw = Stopwatch.StartNew();

Console.WriteLine($"smoke-checking {baseUrl}/health for up to {timeout.TotalSeconds}s ...");

while (sw.Elapsed < timeout)
{
    try
    {
        using var resp = await http.GetAsync($"{baseUrl}/health");
        var body = await resp.Content.ReadAsStringAsync();

        if (resp.StatusCode == HttpStatusCode.OK && body.Contains("\"Healthy\"", StringComparison.OrdinalIgnoreCase))
        {
            // ACA injects CONTAINER_APP_REVISION; the API surfaces it on /health so
            // the operator can SEE which revision answered — essential after a
            // rollback, to confirm the KNOWN-GOOD revision is the one serving.
            Console.WriteLine($"HEALTHY in {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"served body: {body}");
            return 0;
        }

        Console.WriteLine($"not healthy yet: {(int)resp.StatusCode} {body}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        // A scaled-to-zero app may be cold-starting, or a rollback may be mid-flight.
        // Transient failures during the window are expected; keep polling.
        Console.WriteLine($"transient: {ex.GetType().Name} — retrying");
    }

    await Task.Delay(TimeSpan.FromSeconds(3));
}

Console.Error.WriteLine($"NEVER went healthy within {timeout.TotalSeconds}s — investigate before declaring the deploy/rollback done.");
return 1;

// ============================================================================
// PART 2 — RUNBOOK.md procedures to write (the prose deliverable).
//
// Add these two procedures to RUNBOOK.md at the repo root. Use the EXACT-command
// + EXPECTED-output style from Lecture 3. Fill in <region>, <revision>, <org>.
// ============================================================================
/*
### Procedure 1 — Deploy a new version
1. Merge to `main`; the `cd` workflow runs.   Watch: `gh run watch`
2. Verify: `dotnet run --project Workshop.Smoke -- https://workshop-api.<region>.azurecontainerapps.io`
   Expected: "HEALTHY in <ms> ms" and the served revision is the NEW one.

### Procedure 2 — Roll back to the previous version
1. List revisions newest-first:
   az containerapp revision list --name workshop-api -g rg-workshop-capstone \
     --query "[].{name:name,active:properties.active,created:properties.createdTime}" -o table
2. Note the last KNOWN-GOOD revision name.
3. Activate it and route 100% traffic:
   az containerapp revision activate  --name workshop-api -g rg-workshop-capstone --revision <known-good>
   az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone --revision-weight <known-good>=100
4. Verify with the smoke checker; confirm the served revision == <known-good>.
5. THEN diagnose the bad revision. Roll back first, diagnose second.
*/

// ============================================================================
// PART 3 — The real rollback drill (run it, capture the terminal session).
// ============================================================================
/*
# 0) Baseline: the current revision is healthy.
dotnet run --project Workshop.Smoke -- https://workshop-api.<region>.azurecontainerapps.io

# 1) Deploy a deliberately-bad new revision (e.g. a build that returns 500 on
#    /health, OR just a new revision so you have two to toggle between).
az containerapp update --name workshop-api -g rg-workshop-capstone \
  --image ghcr.io/<org>/polyglot-workshop:sha-<new> --revision-suffix bad

# 2) Observe the symptom (smoke checker fails OR you see the bad behavior).
dotnet run --project Workshop.Smoke -- https://workshop-api.<region>.azurecontainerapps.io

# 3) ROLL BACK using Procedure 2 above. Time it.
az containerapp revision list --name workshop-api -g rg-workshop-capstone -o table
az containerapp revision activate --name workshop-api -g rg-workshop-capstone --revision <known-good>
az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone --revision-weight <known-good>=100

# 4) Verify recovery and the served revision.
dotnet run --project Workshop.Smoke -- https://workshop-api.<region>.azurecontainerapps.io
*/

// ============================================================================
// ACCEPTANCE CRITERIA
//   1. RUNBOOK.md contains Procedure 1 (deploy) and Procedure 2 (rollback), each
//      with exact commands and an explicit expected-output / verification step.
//   2. You executed a REAL rollback against the live deployment: captured the
//      `revision list`, the `activate`, the `traffic set`, and the smoke checker
//      showing the service healthy again on the known-good revision.
//   3. The smoke checker exits 0 when healthy and non-zero when not — proven by
//      running it against the bad revision (non-zero) and after rollback (zero).
//   4. You recorded the rollback wall-clock time (target: under ~60 seconds from
//      `activate` to a healthy smoke check).
//   5. /health surfaces the served revision so the rollback's "which revision is
//      answering" is verifiable, not assumed.
//
// STRETCH
//   A. Make the rollback a single script (`rollback.sh <known-good>`) that runs
//      activate + traffic set + the smoke checker and exits non-zero if recovery
//      fails. This is the "one command" the README's rollback contract demands.
//   B. Add a "served revision" assertion to the smoke checker: pass the expected
//      revision as a second arg and FAIL if a different revision answers.
//   C. Write the third procedure — "find the logs for a failing request" — using
//      `az containerapp logs show` + a Log Analytics trace-ID query (Lecture 3 §5).
// ============================================================================
