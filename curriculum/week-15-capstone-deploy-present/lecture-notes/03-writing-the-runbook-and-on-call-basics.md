# Lecture 3 — Writing the RUNBOOK and On-Call Basics: Procedures a Stranger Can Execute at 2am

> **Time:** 2 hours. The first hour is the runbook structure and the five procedures; the second is on-call basics and the MAUI Android sideload that the demo needs. **Prerequisites:** Lectures 1 and 2 (you can build the images and you have a pipeline that deploys and rolls back). **Citations:** the Google SRE Book chapters at <https://sre.google/sre-book/being-on-call/> and <https://sre.google/sre-book/emergency-response/>, the Atlassian runbook guide at <https://www.atlassian.com/incident-management/devops/runbooks>, the MAUI Android publish guide at <https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli>, and the ACA logs/secrets docs cited inline.

## 1. What a runbook is — and what it is not

A **runbook** is a flat, numbered, copy-pasteable set of procedures for the operations a human will actually have to perform against a running system, under stress, possibly at 2am, possibly someone who is not the author. That last clause is the design constraint that decides everything: the audience is a tired engineer who did not write the code, who is paged because something is on fire, and who needs the *command that fixes it* and a way to *know it worked* — not prose, not architecture, not philosophy.

A runbook is therefore explicitly **not**:

- **A README.** The README explains what the project is and how to build it. The runbook explains how to operate it when it misbehaves. They have different audiences (a new contributor vs. an on-call operator) and different stakes (a slow build vs. an outage).
- **An architecture document.** The architecture doc explains *why* the system is shaped the way it is. The runbook does not care why; it cares what command rolls it back. Link to the architecture doc; do not inline it.
- **A wiki of tribal knowledge.** A runbook is tested. If a procedure has not been executed by someone other than its author, it is a draft, not a runbook. Thursday's exercise has a teammate execute yours cold; that is the test.

The single rule that makes a runbook good: **every procedure is a sequence of exact commands, each with its expected output, ending in a verification step.** No "redeploy the service" — instead, the literal `az containerapp update ...` line and the `curl /health` that confirms it took. At 2am, prose is friction; a command you can paste is mercy. Citation: <https://www.atlassian.com/incident-management/devops/runbooks>.

## 2. The shape of `RUNBOOK.md`

The Polyglot Workshop's `RUNBOOK.md` lives at the repo root (a tired operator should find it without searching) and has a fixed shape:

```markdown
# RUNBOOK — Polyglot Workshop

## At a glance
- Live API:    https://workshop-api.<region>.azurecontainerapps.io
- Live admin:  https://workshop-admin.<region>.azurecontainerapps.io
- Resource group: rg-workshop-capstone
- Registry:    ghcr.io/your-org/polyglot-workshop
- Logs:        `az containerapp logs show --name workshop-api -g rg-workshop-capstone --follow`
- Dashboards:  <Grafana URL>   Alerts: <link>   On-call: <who>

## Severity levels
| Sev | Meaning                                  | Response               |
|-----|------------------------------------------|------------------------|
| 1   | Whole platform down / data loss risk     | Roll back NOW (§Rollback) |
| 2   | One client broken; others work           | Triage, fix within hour|
| 3   | Degraded (slow, one feature)             | Next business day      |

## Procedures
1. Deploy a new version
2. Roll back to the previous version
3. Find the logs for a failing request
4. Rotate the OIDC client secret
5. The database is full
6. Put the app in maintenance mode
7. Tear the whole thing down (post-grading)
```

The "At a glance" block is the most-read part of any real runbook: when you are paged, the first thing you need is *the URLs, the resource group, and the one command that shows you logs* — before you read a single procedure. Put it first, keep it current.

## 3. Procedure 1 — Deploy a new version

The deploy procedure is mostly "push and watch the pipeline," but the runbook spells out the manual path too, because the pipeline can itself be broken and the operator needs the escape hatch:

```markdown
### Procedure 1 — Deploy a new version

Normal path (pipeline):
1. Merge the change to `main`. The `cd` workflow runs automatically.
2. Watch it:  `gh run watch`
3. On green, confirm:  `curl -fsS https://workshop-api.<region>.azurecontainerapps.io/health`
   Expected: `{"status":"Healthy"}`

Manual path (pipeline is broken, deploy is urgent):
1. Find the SHA you want:  `git rev-parse HEAD`
2. Verify the image exists:
   `docker manifest inspect ghcr.io/your-org/polyglot-workshop:sha-<SHA>`
   Expected: a JSON manifest (not "manifest unknown").
3. Deploy it:
   az containerapp update --name workshop-api -g rg-workshop-capstone \
     --image ghcr.io/your-org/polyglot-workshop:sha-<SHA> \
     --revision-suffix sha<short>
4. Confirm:  `curl -fsS https://workshop-api.<region>.azurecontainerapps.io/health`
   Expected: HTTP 200, `{"status":"Healthy"}`.
```

Note the verification step at the end of both paths. A deploy you cannot confirm is not a deploy; it is hope.

## 4. Procedure 2 — Roll back (the most important procedure in the book)

Rollback is the first instinct in any incident, and so it is the procedure that must be the most boringly reliable. It is the one-command path from Lecture 2:

```markdown
### Procedure 2 — Roll back to the previous version

When: a deploy made things worse, or any Sev-1. Roll back FIRST, diagnose second.

1. List revisions, newest first:
   az containerapp revision list --name workshop-api -g rg-workshop-capstone \
     --query "[].{name:name, active:properties.active, created:properties.createdTime}" -o table
2. Identify the last KNOWN-GOOD revision (the one active before the bad deploy).
3. Activate it and give it all traffic:
   az containerapp revision activate --name workshop-api -g rg-workshop-capstone \
     --revision <known-good-revision>
   az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone \
     --revision-weight <known-good-revision>=100
4. Confirm:  `curl -fsS https://workshop-api.<region>.azurecontainerapps.io/health`
   Expected: HTTP 200. Verify the user-visible symptom is gone.
5. THEN open an incident note and diagnose what the bad revision did. Do not
   diagnose before rolling back; the users are waiting.
```

The ordering — **roll back first, diagnose second** — is the single most valuable on-call instinct, and it is only possible because the previous revision is already-built bytes the platform retained (Lecture 2 §5). The whole reason we deploy immutable SHA-tagged images is so this procedure is trustworthy. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/revisions>.

## 5. Procedure 3 — Find the logs for a failing request

The chiseled image has no shell, so "find the logs" is an ACA / Log Analytics procedure, and Week 14's structured logging is what makes it work:

```markdown
### Procedure 3 — Find the logs for a failing request

You have a failing request. You may have a trace ID (the API returns it in the
`traceparent` response header and in the Problem Details `extensions.traceId`).

1. Live tail (catch it as it happens):
   az containerapp logs show --name workshop-api -g rg-workshop-capstone --follow
2. By trace ID (Serilog writes structured JSON to Log Analytics):
   az monitor log-analytics query --workspace "$WORKSPACE_ID" --analytics-query \
     "ContainerAppConsoleLogs_CL
      | where Log_s has '<TRACE_ID>'
      | order by TimeGenerated asc"
3. The matching log line includes the level, the message template, the trace ID,
   and the exception (if any). Follow the trace ID into Tempo for the full span
   waterfall:  <Tempo URL>/trace/<TRACE_ID>
```

The reason this is short and the reason it works is the Week 14 investment: every log line carries the trace ID, so "find the logs for *this* request" is a filter, not a hunt. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/log-monitoring>.

## 6. Procedure 4 — Rotate the OIDC client secret

This is the procedure people skip until they have leaked a secret, and then they wish they had it. Rotation must be doable *without downtime* and *without a code change*:

```markdown
### Procedure 4 — Rotate the OIDC (Keycloak) client secret

When: the client secret leaked, or on a scheduled rotation (every 90 days).

1. In Keycloak admin, regenerate the client secret for `workshop-api`:
   Realm > Clients > workshop-api > Credentials > Regenerate Secret. Copy it.
2. Update the secret IN the Container App (this does NOT redeploy the image):
   az containerapp secret set --name workshop-api -g rg-workshop-capstone \
     --secrets "keycloak-secret=<NEW_SECRET>"
3. Restart the active revision so it re-reads the secret:
   az containerapp revision restart --name workshop-api -g rg-workshop-capstone \
     --revision <active-revision>
4. Confirm sign-in still works: complete an OIDC login on the live admin URL.
5. In Keycloak, INVALIDATE the old secret (it is replaced by step 1; confirm no
   client is still using it by watching the auth logs for 5 minutes).
```

The key property: the secret lives in the Container App's secret store (referenced by `secretref:`, never inlined), so rotating it is an `az containerapp secret set` plus a revision restart — no rebuild, no redeploy, no code change. Citation: <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>.

## 7. Procedure 5 — The database is full

The syllabus calls this one out by name, because "the database filled up" is the most common boring outage and the one most likely to happen during a demo when a load test left rows behind:

```markdown
### Procedure 5 — The database (PostgreSQL) is full

Symptom: writes fail with `53100 disk full` / `could not extend file`; `/health`
goes red because the readiness check writes a heartbeat row.

Immediate (buy time):
1. Confirm it is disk, not connections:
   psql "$PG_CONN" -c "SELECT pg_size_pretty(pg_database_size(current_database()));"
2. Find the biggest tables:
   psql "$PG_CONN" -c "SELECT relname, pg_size_pretty(pg_total_relation_size(relid))
     FROM pg_catalog.pg_statio_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 10;"
3. The usual culprit is the OUTBOX table (Week 8) not being drained, or the
   audit log (Week 6). Drain processed outbox rows:
   psql "$PG_CONN" -c "DELETE FROM outbox_messages WHERE processed_at IS NOT NULL
     AND processed_at < now() - interval '7 days';"
4. VACUUM to reclaim the space (DELETE alone does not return it to the OS):
   psql "$PG_CONN" -c "VACUUM (VERBOSE, ANALYZE) outbox_messages;"

Durable fix:
5. Grow the managed Postgres storage one size:
   az postgres flexible-server update -g rg-workshop-capstone -n workshop-pg --storage-size 64
6. Add the missing retention job: schedule the outbox-drain DELETE nightly so
   this never recurs. (This is a code change; open a PR, do not leave it manual.)
```

Two lessons embedded here: the *immediate* steps buy time (drain + vacuum), the *durable* fix removes the cause (retention job), and the runbook makes you write down the durable fix as a PR so the same page does not fire next week. Citation: <https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/how-to-scale-compute-storage-portal>.

## 7b. Procedure 6 — Maintenance mode and the static fallback

Sometimes the right move is not to fix-forward and not to roll back, but to take the app down *gracefully* — a migration that cannot run online, a dependency outage you are waiting on, a security incident you are containing. "Take it down gracefully" means: stop serving errors, start serving a clear maintenance page, and stop the background workers from doing half a job. The runbook spells out both the quick way and the clean way:

```markdown
### Procedure 6 — Maintenance mode

Quick (take the app fully offline):
1. Scale to zero so nothing serves and nothing errors:
   az containerapp update --name workshop-api -g rg-workshop-capstone \
     --min-replicas 0 --max-replicas 0
2. Announce in <channel> with an ETA.

Clean (serve a 503 maintenance page, keep the URL alive):
1. Point ingress at a tiny `maintenance` revision (an image that returns 503 with
   a Retry-After header on every path):
   az containerapp ingress traffic set --name workshop-api -g rg-workshop-capstone \
     --revision-weight maintenance-rev=100
2. Drain the background workers FIRST so no outbox row is half-processed:
   the workers honor a `MAINTENANCE=1` env var and finish their current item
   before idling. Set it, wait for the "workers idle" log line, THEN cut traffic.

Restore:
3. Re-point traffic to the known-good app revision (Procedure 2), unset MAINTENANCE,
   confirm /health and the worker "resumed" log line.
```

The non-obvious part is **draining the workers before cutting traffic**: the outbox pattern (Week 8) means a worker may be mid-flight on a side effect (sending an email, calling a webhook). Yanking the app mid-item risks a partially-applied effect. The `MAINTENANCE=1` flag lets a worker finish its current item and stop pulling new ones — graceful, not abrupt. A 503 with `Retry-After` is also the correct HTTP contract: clients and crawlers back off instead of hammering. Citation: <https://developer.mozilla.org/en-US/docs/Web/HTTP/Status/503>.

## 7c. What makes a procedure *testable* — the cold-execution rule

A procedure you wrote and never ran is a hypothesis. The single most valuable thing you can do to a runbook is have **someone other than the author execute it cold** — no Slack, no "oh you also have to…", just the document. Every place they hesitate is a defect in the runbook, and you fix the document, not the person. Concretely, the cold-execution test surfaces four recurring defects:

1. **Implicit context.** "Restart the service" assumes the reader knows *which* service, *which* resource group, *which* subscription is active in their `az` session. The fix: every command carries `--name` and `-g` explicitly, and the "At a glance" block names the subscription.
2. **Missing preconditions.** A procedure that starts at step 1 with `az containerapp ...` assumes the reader is logged in and has the `containerapp` extension. The fix: a one-line "Before you start: `az login` and `az account show` confirms subscription `<id>`."
3. **No expected output.** "Run the migration" without "you should see `Applied migration 20250612_AddTenant`" leaves the reader unsure whether it worked. The fix: every command states what success looks like.
4. **No failure branch.** "Activate the previous revision" without "if `revision list` is empty, the app was created in single-revision mode; switch it with `revision set-mode --mode multiple` first" abandons the reader at the exact moment they need help. The fix: name the likely failure and its remedy inline.

Thursday's exercise is this test, run for real, on your runbook, by a teammate. The grading bar in the homework is the same. A runbook that has not survived a cold execution is a draft; the test is what promotes it. Citation: <https://www.atlassian.com/incident-management/devops/runbooks>.

## 8. On-call basics — the practice around the artifact

The runbook is the artifact. The practice around it is what makes a service operable, and even a capstone deserves the small version:

1. **Severity levels, agreed in advance.** Decide what Sev-1 / Sev-2 / Sev-3 mean *before* you are in one (the table in §2). The value is not the taxonomy; it is having agreed on "data loss is Sev-1" while calm, so nobody debates severity while the data is being lost. Citation: <https://sre.google/sre-book/emergency-response/>.

2. **Roll back first, diagnose second.** Restated because it is the whole game: the user does not care *why* it broke while it is broken. Restore service, then find the cause. The rollback procedure exists to make this instinct cheap.

3. **Every alert links to a runbook section.** An alert that says "API error rate high" and nothing else makes the operator start from zero at 2am. An alert that says "API error rate high — see RUNBOOK §Procedure 3, then §Procedure 2 if a recent deploy" gives them the first two moves. Wire the Week-14 burn-rate alerts to link to runbook sections. Citation: <https://sre.google/workbook/alerting-on-slos/>.

4. **The blameless postmortem.** After an incident, write down what happened, when, what the impact was, and what will stop it recurring — without naming a person to blame. The goal is a *fixed system*, not a *blamed human*; people who fear blame hide information, and hidden information causes the next outage. The postmortem's action items are the durable fixes (like §7's retention job). Citation: <https://sre.google/sre-book/postmortem-culture/>.

5. **"Done deploying" has a definition.** A deploy is done when the new revision is healthy, the smoke check passed, *and* the dashboards show normal error/latency for five minutes — not when the pipeline turned green. The green pipeline means "rolled out"; the five quiet minutes mean "safe."

These five are not a certification; they are the difference between a service you can sleep next to and one that wakes you up with no idea what to do. For the capstone, the runbook plus these habits *are* the on-call deliverable.

## 9. The MAUI Android sideload — the last client the demo needs

The recorded demo (Saturday) traces a lesson across all three clients, which means the MAUI app must run on a real (or emulated) Android device, signed in via OIDC against the *deployed* Keycloak — not against `localhost`. The release publish is one command after you create a signing keystore:

```bash
# 1) Create a signing keystore (once). Keep the keystore and passwords OUT of
# the repo — they go in your password manager, not in git.
keytool -genkeypair -v -keystore workshop.keystore \
  -alias workshop -keyalg RSA -keysize 2048 -validity 10000 \
  -storepass "$KS_PASS" -keypass "$KEY_PASS" \
  -dname "CN=Polyglot Workshop, OU=C9, O=CodeCrunch, C=US"

# 2) Publish a signed Release APK targeting the deployed backend. The OIDC
# authority and the API base URL are Release-config app settings pointing at the
# LIVE URLs, not localhost.
dotnet publish src/Workshop.Maui/Workshop.Maui.csproj \
  -f net9.0-android -c Release \
  /p:AndroidPackageFormat=apk \
  /p:AndroidKeyStore=true \
  /p:AndroidSigningKeyStore=workshop.keystore \
  /p:AndroidSigningKeyAlias=workshop \
  /p:AndroidSigningStorePass="$KS_PASS" \
  /p:AndroidSigningKeyPass="$KEY_PASS"

# 3) Sideload onto a connected device (USB debugging on) or a running emulator.
adb devices                                  # confirm the device is listed
adb install -r \
  src/Workshop.Maui/bin/Release/net9.0-android/com.codecrunch.workshop-Signed.apk
```

The thing that bites students here is **OIDC redirect URIs**: the MAUI app uses a custom-scheme redirect (e.g. `workshopapp://callback`), and that exact URI must be registered as a valid redirect in the deployed Keycloak client, or the sign-in dead-ends after the browser hands control back. The runbook's "At a glance" should list the registered redirect URIs so a teammate can check them. Citation: <https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli> and the OIDC native-app redirect guidance at <https://datatracker.ietf.org/doc/html/rfc8252>.

## 10. What this lecture earns you for the capstone

You can now write the document that turns a deployed system into an *operable* one: a `RUNBOOK.md` with deploy, rollback, log-retrieval, secret-rotation, and database-full procedures, each a paste-able command sequence ending in a verification step, plus the on-call habits — severity levels, rollback-first, alert-to-runbook linkage, blameless postmortems — that make the runbook part of a practice rather than a dead file. And you can ship the last client the demo needs: a signed Android APK that sideloads and signs in against the live deployment. The mini-project assembles all of it into the Week 15 capstone milestone.

> **Citations recap.** SRE on-call: <https://sre.google/sre-book/being-on-call/>. SRE emergency response: <https://sre.google/sre-book/emergency-response/>. SRE postmortem culture: <https://sre.google/sre-book/postmortem-culture/>. Atlassian runbooks: <https://www.atlassian.com/incident-management/devops/runbooks>. ACA secrets: <https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets>. ACA logs: <https://learn.microsoft.com/en-us/azure/container-apps/log-monitoring>. MAUI Android publish: <https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli>. OAuth for native apps: <https://datatracker.ietf.org/doc/html/rfc8252>.
