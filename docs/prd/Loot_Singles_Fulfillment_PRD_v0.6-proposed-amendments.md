# PRD v0.6 — Proposed Amendments (for Product Owner review)

**Base**: [`Loot_Singles_Fulfillment_PRD_v0.5.md`](Loot_Singles_Fulfillment_PRD_v0.5.md)
**Drafted**: 2026-09-22
**Source**: [Feature 019 — Automated stage and production deployment](../../specs/019-automated-deployment/spec.md)
**Status**: **Proposed.** Awaiting Product Owner approval. Not a requirement until approved and
folded into a new authoritative PRD version.

One amendment. It changes **where the web application is served from**, and nothing else.

Feature 019's task T001 is blocked on this decision, and the implementation cannot begin until it is
resolved either way. The feature's technical plan records the deviation in its Complexity Tracking
rather than assuming the amendment, because approved-PRD direction is the Product Owner's to change
(constitution Principle I).

---

## A17 — §40.8 Hosting: one origin serves both the API and the web application

**Today** §40.8 approves this hosting direction:

``` text
React + TypeScript PWA
          ↓
Azure Static Web Apps

ASP.NET Core Web API
          ↓
Azure Container Apps

Entity Framework Core
          ↓
Azure SQL Database
```

Azure Static Web Apps serves the built React application from its own origin; Container Apps serves
the API from a different one.

**Change**: the container that runs the API also serves the built web application, from a single
origin per environment. Azure Static Web Apps is not used.

### Why this is a correctness problem, not a preference

The session cookie is configured `SameSite=Strict` — `backend/src/LootSingles.Api/Program.cs:130`,
set by feature 002 (employee authentication). A browser will not attach a `Strict` cookie to a
request that originates from a different site. With the web application on one origin and the API on
another, **every authenticated request fails** — the picker signs in and the next request arrives
with no session.

There are only two ways out:

| Option | Consequence |
|---|---|
| Relax the cookie to `Lax` or `None` | Keeps §40.8's shape, but weakens a credential control to satisfy a hosting arrangement. That is a deviation from constitution Principle VII (Data Minimization and Credential Security), which is a safety principle requiring explicit Product Owner **and** Developer approval to weaken. |
| **Serve both from one origin** | Requires amending §40.8 — a documentation change with no security cost. |

The second is recommended. Amending the approved hosting diagram is cheaper than weakening a cookie
that protects employee sessions against cross-site request forgery.

### What it costs

Almost nothing. Static file serving is built into ASP.NET Core; the container image copies the
frontend build output alongside the API. Static Web Apps' free tier is replaced by serving those
same files from a container that must run anyway, so the hosting cost does not increase — feature
019 estimates $5–8/month in total, against a $10 ceiling.

One consequence worth naming: the web application and the API now deploy together as one artifact.
They cannot be released independently. For a single internal application whose frontend and backend
are versioned in one repository and released together regardless, this is a simplification rather
than a constraint.

**Replace §40.8's diagram with:**

> ``` text
> React + TypeScript PWA  ─┐
>                          ├─→  one container, one origin
> ASP.NET Core Web API    ─┘         ↓
>                             Azure Container Apps
>
> Entity Framework Core
>           ↓
>       Azure SQL Database
> ```

**And add immediately below it:**

> The web application and the API are served from **one origin per environment**, by the same
> container. The session cookie is `SameSite=Strict`, and a browser does not attach a `Strict`
> cookie to a request originating from a different site, so a separate origin for the web
> application would break authentication outright. Serving both from one origin keeps the cookie
> control intact; the alternative — relaxing the cookie — would weaken a credential protection to
> satisfy a hosting arrangement.
>
> This supersedes v0.5's use of Azure Static Web Apps. Container Apps and Azure SQL Database are
> unchanged.
>
> Each environment is isolated: its own database, its own identities, its own retained logs.
> Neither environment can read or modify the other's data.

### Everything else in §40.8 stands

The paragraphs after the diagram are unchanged and still govern:

- internal use, no multi-tenant SaaS architecture
- the infrastructure priorities: security, reliability during fulfillment work, low and predictable
  operating cost, simple maintenance, isolation of Loot's application data
- **"zero-dollar hosting is not a requirement if achieving it would introduce unacceptable
  production downtime or reliability problems"** — which feature 019 relies on directly when it
  chooses a paid database tier for production over a free tier that stops serving partway through
  each month

---

## What this amendment does not change

- **No product behaviour.** Sign-in, order claiming, picking, packing, importing and every screen
  behave exactly as they do today. This changes where files are served from.
- **§40.1–40.7 and §40.9–40.10** are untouched. React, TypeScript, ASP.NET Core, Entity Framework
  Core, Azure SQL, the cookie authentication scheme, the import architecture and the catalog
  provider architecture all stand.
- **The PWA direction (§40.2)** is unaffected. Installability and offline behaviour are out of
  scope for feature 019 but are not withdrawn as goals.
- **Azure Container Apps and Azure SQL Database** remain the approved platforms, exactly as v0.5
  approved them.
- **Customer privacy (§27)** is untouched. Feature 019 adds no customer data and no new access path
  to a packing slip.
- **No multi-location or multi-business capability** is introduced. §40.8's single-business
  statement stands, and feature 019 adds no tenant, site or location concept.

---

## If this is not approved

Feature 019 stops at T001 and returns to `/speckit-plan`. The alternatives, in the order I would
consider them:

1. **Relax the cookie to `Lax`.** Keeps Static Web Apps. Requires a constitution Principle VII
   deviation with documented justification, and weakens CSRF protection on an application holding
   customer addresses. Not recommended.
2. **Put both origins behind one domain** using a reverse proxy or Azure Front Door, so the browser
   sees one site. Restores `Strict` and keeps Static Web Apps, at the cost of another component to
   provision, pay for and keep working. Front Door's entry tier carries a monthly base fee that
   would likely consume or breach the $10/month ceiling on its own — worth pricing before this is
   taken seriously, but it is a paid component either way.
3. **Abandon same-origin and accept re-authentication.** Not viable; the picker would be signed out
   on every request.
