# RUNBOOK — Polyglot Workshop

> The document a tired operator follows at 2am. Every procedure is exact commands
> with expected output, ending in a verification step. Replace every `<...>`
> placeholder with your real values, then EXECUTE each procedure once (a teammate
> executes the deploy + rollback cold — that is the test).
> See lecture-notes/03-writing-the-runbook-and-on-call-basics.md.

## At a glance

- **Live API:**    `https://workshop-api.<region>.azurecontainerapps.io`
- **Live admin:**  `https://workshop-admin.<region>.azurecontainerapps.io`
- **Resource group:** `rg-workshop-capstone`
- **Registry:** `ghcr.io/<org>/polyglot-workshop`
- **Logs:** `az containerapp logs show --name workshop-api -g rg-workshop-capstone --follow`
- **Dashboards:** `<Grafana URL>`   **Alerts:** `<link>`   **On-call:** `<who>`
- **MAUI OIDC redirect URIs (registered in Keycloak):** `workshopapp://callback`

## Severity levels

| Sev | Meaning                                | Response                      |
|-----|----------------------------------------|-------------------------------|
| 1   | Whole platform down / data-loss risk   | Roll back NOW (Procedure 2)   |
| 2   | One client broken; others work         | Triage; fix within the hour   |
| 3   | Degraded (slow / one feature)          | Next business day             |

---

## Procedure 1 — Deploy a new version

Normal path (pipeline):
1. Merge to `main`. The `cd` workflow runs.   Watch: `gh run watch`
2. Verify: `curl -fsS https://workshop-api.<region>.azurecontainerapps.io/health`
   **Expected:** `{"status":"Healthy"}`

Manual path (pipeline broken, deploy urgent):
1. `git rev-parse HEAD`  → the SHA you want.
2. Confirm the image exists: `docker manifest inspect ghcr.io/<org>/polyglot-workshop:sha-<SHA>`
   **Expected:** a JSON manifest (not "manifest unknown").
3. `az containerapp update --name workshop-api -g rg-workshop-capstone --image ghcr.io/<org>/polyglot-workshop:sha-<SHA> --revision-suffix sha<short>`
4. Verify: `curl -fsS https://workshop-api.<region>.azurecontainerapps.io/health` → HTTP 200.

---

## Procedure 2 — Roll back to the previous version

**Roll back FIRST, diagnose second.** Use for any Sev-1 or a bad deploy.

1. List revisions, newest-first:
   `az containerapp revision list --name workshop-api -g rg-workshop-capstone --query "[].{name:name,active:properties.active,created:properties.createdTime}" -o table`
2. Identify the last KNOWN-GOOD revision.
3. Activate it and route all traffic:
   `az containerapp revision activate --name workshop-api -g rg-workshop-capstone --revision <known-good>`
   `az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone --revision-weight <known-good>=100`
4. Verify: `curl -fsS https://workshop-api.<region>.azurecontainerapps.io/health` → HTTP 200, and the user-visible symptom is gone.
5. THEN open an incident note and diagnose the bad revision.

---

## Procedure 3 — Find the logs for a failing request

You may have a trace ID (response `traceparent` header / Problem Details `extensions.traceId`).

1. Live tail: `az containerapp logs show --name workshop-api -g rg-workshop-capstone --follow`
2. By trace ID:
   `az monitor log-analytics query --workspace "<WORKSPACE_ID>" --analytics-query "ContainerAppConsoleLogs_CL | where Log_s has '<TRACE_ID>' | order by TimeGenerated asc"`
3. Follow the trace ID into Tempo: `<Tempo URL>/trace/<TRACE_ID>`

---

## Procedure 4 — Rotate the OIDC (Keycloak) client secret

1. Keycloak admin → Realm → Clients → `workshop-api` → Credentials → Regenerate Secret. Copy it.
2. Update the secret in the app (no redeploy):
   `az containerapp secret set --name workshop-api -g rg-workshop-capstone --secrets "keycloak-secret=<NEW_SECRET>"`
3. Restart the active revision so it re-reads the secret:
   `az containerapp revision restart --name workshop-api -g rg-workshop-capstone --revision <active>`
4. Verify: complete an OIDC login on the live admin URL.
5. Confirm no client still uses the old secret (watch auth logs for 5 min).

---

## Procedure 5 — The database (PostgreSQL) is full

Symptom: writes fail with `53100 disk full`; `/health` red (the heartbeat write fails).

Immediate (buy time):
1. `psql "<PG_CONN>" -c "SELECT pg_size_pretty(pg_database_size(current_database()));"`
2. Biggest tables: `psql "<PG_CONN>" -c "SELECT relname, pg_size_pretty(pg_total_relation_size(relid)) FROM pg_catalog.pg_statio_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 10;"`
3. Drain processed outbox rows (the usual culprit):
   `psql "<PG_CONN>" -c "DELETE FROM outbox_messages WHERE processed_at IS NOT NULL AND processed_at < now() - interval '7 days';"`
4. Reclaim space: `psql "<PG_CONN>" -c "VACUUM (VERBOSE, ANALYZE) outbox_messages;"`

Durable fix:
5. Grow storage: `az postgres flexible-server update -g rg-workshop-capstone -n workshop-pg --storage-size 64`
6. Add a nightly outbox-drain job (this is a PR, not a manual step).

---

## Procedure 6 — Maintenance mode

1. Point ingress at a static maintenance revision, OR set `--min-replicas 0 --max-replicas 0` to take the app offline gracefully.
2. Announce in `<channel>`. Restore by re-pointing traffic / restoring replicas.

---

## Procedure 7 — Tear down (after grading)

So the free tier stays free:
1. Scale to zero: `az containerapp update --name workshop-api -g rg-workshop-capstone --min-replicas 0`
2. Full teardown when the grade is in: `az group delete --name rg-workshop-capstone --yes`
3. Verify nothing remains: `az resource list -g rg-workshop-capstone -o table` (expect an error / empty — the group is gone).
