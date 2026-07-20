# Week 6 — Challenges

The exercises drill basics. **Challenges stretch you.** This week's challenge takes ~2 hours and produces something you can commit to your portfolio: a multi-tenant authorization layer where every request carries a `tenant_id` claim, every resource carries a `TenantId` column, and the authorization handler refuses any cross-tenant access — with an integration test that proves a user on tenant `acme` *cannot* read a note belonging to tenant `globex`, even with a forged URL.

## Index

1. **[Challenge 1 — Multi-tenant authorization](challenge-01-multi-tenant-authorization.md)** — build a `TenantClaimRequirement` + handler, decorate the `Note` entity with `TenantId`, write a regression test that proves tenant isolation, and explain in 200 words why the `tenant_id` claim must come from the JWT or the database (never the request body). (~120 min)

Challenges are optional. If you skip them, you can still pass the week. If you do them, you'll be measurably ahead — multi-tenant authorization is the most-asked question in ASP.NET Core security code reviews and the most-leaked surface in production SaaS. The tenant-isolation reflex you build in step 4 is the reflex that prevents one customer from reading another customer's data in every SaaS app you ship from this week on.
