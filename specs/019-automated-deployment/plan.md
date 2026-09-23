# Implementation Plan: Automated Stage and Production Deployment

**Branch**: `019-automated-deployment` | **Date**: 2026-09-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/019-automated-deployment/spec.md`

## Summary

The application runs only on a developer laptop, so it cannot be used for fulfillment work at the
shop. This feature packages it as a container image and stands up two Azure environments: **stage**,
which deploys itself when a pull request merges to `main`, and **production**, which deploys only
when someone starts a release, names the exact commit, and approves it.

Five application changes make the application hostable — forwarded headers, static-file serving with
correct fallback ordering, two health endpoints with deliberately different jobs, Data Protection
keys persisted to SQL, and a `migrate` command. Everything else is a Dockerfile, two workflows, and
a one-time setup checklist.

The design was reviewed adversarially four times and deliberately shrunk: promotion is a named commit
rather than resolved digests, and there are three configuration assertions rather than nine. Each
removal names the risk accepted (research.md §10, §16).

### Shape, amended 2026-09-23

The two environments **share one Container Apps environment**, and therefore one virtual network, one
public IP and one Log Analytics workspace. They keep their own SQL servers, databases, managed
identities and deployment credentials.

This was forced by a meter the original cost model missed: every Container Apps environment with
external ingress carries a Standard static public IPv4 at **$3.65/month**, so two of them plus
production's database came to $12.20 against FR-028's $10 ceiling. Found by the empirical cost check
in quickstart C6, which exists precisely because the billing documentation hedged (research.md §13).

**What it costs**: the network boundary between stage and production. Both apps sit in one subnet,
so stage can reach production's SQL server at the network level. **Data isolation is unaffected**,
because it rests on identity rather than reachability — each app authenticates as its own managed
identity, and stage's holds no database user in production's database (FR-006). Retained records are
shared too, separable by application name but not isolated (FR-005, FR-027).

```text
rg-loot-singles-shared     vnet-loot-singles / snet-apps, cae-loot-singles, log-loot-singles
rg-loot-singles-stage      identities, SQL (free offer), ca- and caj-loot-singles-stage
rg-loot-singles-prod       identities, SQL (Basic), ca- and caj-loot-singles-prod
```

**Expected cost: $8.55/month** against FR-028's $10 ceiling — one public IP at $3.65 and production's
Basic database at $4.90. Everything else sits inside a free tier.

Two corrections this forced, recorded because both were stated confidently and wrongly:

- **Rollback is not "a platform call rather than tracked state."** In single-revision mode only one
  revision is active, so rolling back means deploying the previous image again. Both workflows record
  the running image before changing anything (research.md §12).
- **Free usage does appear on a bill**, as $0 line items with `- Free` in the meter name. What a cost
  view still cannot show is how much of an allowance remains.

## Technical Context

**Language/Version**: C# / .NET 10 (`LootSingles.Api`); TypeScript 5 / React 19 (`frontend`)

**Primary Dependencies**: One new package —
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`. No other runtime dependency is added. No
logging, telemetry or health-check package (research.md §4, §9).

**Storage**: Azure SQL. One new table, `DataProtectionKeys`, added by one additive migration
(data-model.md). No existing entity changes.

**Testing**: xUnit via `LootSingles.UnitTests` and `LootSingles.IntegrationTests`
(`WebApplicationFactory<Program>`, Testcontainers SQL Server). Configuration assertions follow the
existing `DatabaseConfigurationTests` precedent.

**Target Platform**: Azure Container Apps (Consumption, workload-profile environment, scale to zero),
Linux container on `mcr.microsoft.com/dotnet/aspnet:10.0`, behind the environment's TLS ingress.

**Project Type**: Web application — existing `backend/` + `frontend/` layout, now published as one
image serving both from a single origin (research.md §1).

**Performance Goals**: None beyond today's. Scale is one instance per environment, single-digit
concurrent users. The only timing property that matters is a cold start after scale-to-zero, which is
a container start against an always-awake production database.

**Constraints**:
- Recurring cost ≤ $10/month with a budget alert (FR-028)
- No committed secrets of any kind (FR-019); Entra-only auth means no password exists to commit
- Database default-deny, never a `0.0.0.0` rule (FR-020)
- The running application MUST NOT hold schema permission (FR-021)
- Stage carries production's protections exactly (FR-029)
- Migrations additive-only, because rollback restores the image and never the schema (FR-016)

**Scale/Scope**: Two Azure environments; three files changed or added under
`backend/src/LootSingles.Api/` (`Program.cs`, `MigrateCommand.cs`, the `.csproj`) plus the DbContext
and one migration under `LootSingles.Infrastructure/`; one Dockerfile; two workflows plus one line in
an existing one; **seven new test classes** — six behavioural, one configuration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

Constitution v3.5.0. Re-checked after Phase 1 — no change in any verdict.

| Principle | Assessment |
|---|---|
| **I. Product Owner Authority** | Every decision traces to a confirmed decision of 2026-09-22, recorded either in the spec's Clarifications (self-approval, stage data, release timing, restore) or in the approved design session (two environments, Basic for production, promotion by approval, custom domain). PASS |
| **II. No Invented Requirements** | No product functionality is added. The feature changes where the application runs, not what it does. Requirements trace to the spec and to the approved PRD. Same-origin hosting was a deviation from PRD §40.8 when this plan was written; it was **approved as amendment A17 on 2026-09-22** and folded into PRD v0.6, which now specifies one origin per environment. No deviation remains. PASS |
| **III. Small, Reviewable Changes** | Three independently reviewable groups: application changes with their tests, the Dockerfile, and the workflows. The spec's user stories map to them in that order. PASS |
| **IV. Test-Driven Development (NON-NEGOTIABLE)** | Red → Green for every behavioural change. Six behavioural test classes are written first and fail against today's code: no forwarded-header handling, neither health endpoint, no static files, in-memory keys, no `migrate` command. Each has a task, and each task precedes the implementation that makes it pass — verified by `/speckit-analyze` on 2026-09-22, which caught that `DatabaseHealthEndpointTests` and the `/health/database` endpoint had no tasks at all despite this gate claiming otherwise (now T047/T048). The infrastructure itself cannot be unit-tested, which is why §6 of the spec separates what tests prove from what only a deployment proves. PASS |
| **V. Safe Failure Over Silent Corruption** | The feature's core argument. `/health` stays database-free so a database fault cannot destroy a working container (FR-023); `/health/database` fails a release rather than letting a broken application look deployed (FR-024); the `/api` fallback stops a 404 masquerading as HTML (FR-004); rollback restores the previous revision on any check failure (FR-017). PASS |
| **VI. Server-Enforced Critical Business Rules** | Unchanged — no rule moves. Strengthened operationally: withholding `db_ddladmin` from the application means the filtered unique index enforcing one active claim cannot be dropped through the application (research.md §7). PASS |
| **VII. Data Minimization and Credential Security** | No new customer data. No secret exists to commit — Entra-only auth removes the password entirely (contracts/deployment.md). Two identities limit blast radius on the database holding PRD §27's packing slips. Stage carries identical protections (FR-029). The Data Protection `Xml` column holds key material and is never logged or returned. PASS |
| **VIII. One Responsive Product** | One image, one origin, one application. No behaviour differs between environments except reliability and cost. PASS |
| **IX. Replaceable Integrations** | The application gains no dependency on Azure. It reads a connection string and writes to stdout; hosting specifics live in the Dockerfile and workflows, not in application code. PASS |
| **X. Single-Business Scope** | No tenant, site or location concept anywhere (data-model.md). Workflows read resource names from environment variables because that is how they would be written regardless, not to enable future stores. PASS |
| **XI. Reliability During Fulfillment** | Cost optimisation is explicitly subordinated: production uses paid Basic rather than the free offer precisely because the free tier stops the database mid-month. Logging remains `ILogger<T>` to stdout with no package; platform-side retention is permitted under v3.5.0, amended 2026-09-22 for this feature. PASS |
| **XII. Maintainable and Extensible Design** | The `migrate` command mirrors the existing `bootstrap-admin` shape rather than inventing a second one. No abstraction is introduced for a single implementation. PASS |
| **XIII. Simplicity and Proportional Abstraction** | Actively enforced. Rejected: a health-check package for two endpoints; digest resolution and deployment-record queries for promotion; tracked image state for rollback; six of nine configuration assertions. Each rejection and its accepted risk is recorded (research.md §4, §10, §12, §16). PASS |
| **EF Core Standards** | One additive migration creating a framework-owned table. No query, no `AsNoTracking` consideration, no changed access pattern. PASS |

### Gate result

**PASS.** One item had to close before implementation — the PRD §40.8 amendment for same-origin
hosting, a documentation change to an approved artifact and therefore the Product Owner's to make,
not this plan's (constitution Principle I). It was **approved on 2026-09-22 as amendment A17** and
folded into PRD v0.6. Nothing now blocks `/speckit-implement`.

## Project Structure

### Documentation (this feature)

```text
specs/019-automated-deployment/
├── plan.md              # This file
├── spec.md              # 32 FRs, 12 SCs, 4 clarifications
├── research.md          # Sixteen decisions, with what was rejected and why
├── data-model.md        # One new table; nothing else changes
├── quickstart.md        # Local validation, then the one-time Azure checklist
├── contracts/
│   ├── health-api.md    # The two health endpoints and the routing contract
│   └── deployment.md    # Image command line, both workflows, the secrets contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (16/16)
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
backend/src/LootSingles.Api/
├── Program.cs                    # forwarded headers, static files + fallbacks,
│                                 # /health, /health/database, data protection,
│                                 # `migrate` dispatch beside `bootstrap-admin`
├── MigrateCommand.cs             # new; mirrors BootstrapAdminCommand.cs
└── LootSingles.Api.csproj        # + Microsoft.AspNetCore.DataProtection.EntityFrameworkCore

backend/src/LootSingles.Infrastructure/Persistence/
├── LootSinglesDbContext.cs       # implements IDataProtectionKeyContext
└── Migrations/                   # one additive migration: DataProtectionKeys

backend/tests/LootSingles.IntegrationTests/
├── Health/HealthEndpointTests.cs
├── Health/DatabaseHealthEndpointTests.cs
├── Hosting/ForwardedHeadersTests.cs
├── Hosting/SpaFallbackTests.cs
├── Hosting/DataProtectionPersistenceTests.cs
├── Hosting/MigrateCommandTests.cs
└── Configuration/DeploymentConfigurationTests.cs   # the three text assertions

Dockerfile                        # new, repo root; multi-stage, non-root
.dockerignore                     # exists; already excludes node_modules, bin, obj, dist

.github/workflows/
├── pr-quality-gate.yml           # + workflow_call: trigger (one line)
├── deploy-stage.yml              # new
└── deploy-production.yml         # new
```

**Structure Decision**: The existing web-application layout is kept. All application changes are in
`LootSingles.Api` beside the code they modify, with the single infrastructure change confined to the
DbContext and one migration. The Dockerfile sits at the repo root because it needs both `backend/`
and `frontend/` in its build context. Tests go in `LootSingles.IntegrationTests` rather than
`LootSingles.UnitTests` because every one of them needs a hosted application
(`WebApplicationFactory<Program>`) or a real database.

## Complexity Tracking

> Filled only where the Constitution Check needs justification.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| ~~Deviates from PRD §40.8 (Static Web Apps for the frontend)~~ — **resolved 2026-09-22**: approved as amendment A17 and folded into PRD v0.6, which now specifies one origin per environment. Kept here as the record of why | The session cookie is `SameSite=Strict`; a browser will not attach it to a request from a different site, so a separate web origin breaks authentication outright | Relaxing the cookie to `Lax` or `None` would keep §40.8's shape, but weakens a credential control to satisfy a hosting preference — a Principle VII deviation, which is worse than a documentation amendment. The amendment was taken; no deviation remains |
| Two managed identities per environment rather than one | The application is the internet-facing component; schema permission is what turns a foothold into a persistent one against the database holding PRD §27's packing slips (research.md §7) | One identity with `db_ddladmin` is four lines simpler in setup and zero lines simpler in the application. The saving is not worth the ability to drop the claim-uniqueness index, add a PII-copying trigger, or truncate the access log |
| A second health endpoint rather than one | `/health` must not touch the database (a fault would destroy a working container) and a release must prove the database is reachable (FR-024). One endpoint cannot do both | A single database-touching endpoint reintroduces exactly the failure FR-023 forbids; a single database-free endpoint lets a release pass while the application cannot read anything |

## Next

`/speckit-tasks`, then `/speckit-analyze`, then `/speckit-implement`. The PRD §40.8 amendment that
gated implementation was approved on 2026-09-22 (A17, PRD v0.6), so nothing blocks it.
