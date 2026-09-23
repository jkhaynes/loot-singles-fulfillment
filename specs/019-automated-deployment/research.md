# Research: Automated Stage and Production Deployment

**Feature**: 019 | **Date**: 2026-09-22

Sixteen decisions. Each records what was chosen, why, and what was rejected. Several were reached by
adversarial review of an earlier draft that had grown more machinery than this business needs; where
something was removed, the risk accepted is recorded with it (§10, §16).

---

## 1. One container serves the API and the web app

**Decision**: Publish a single image containing the ASP.NET Core API with the built React bundle in
`wwwroot`, served from one origin per environment.

**Rationale**: The session cookie is configured `SameSite=Strict` with `SecurePolicy.Always`
(`Program.cs`). A browser will not attach a `Strict` cookie to a request originating from a
different site, so splitting the web app onto its own origin breaks authentication outright. Serving
both from one origin also keeps the frontend's existing relative `/api` fetches working unchanged.

**Alternatives considered**: Azure Static Web Apps for the frontend plus a separate API origin — the
arrangement PRD §40.8 originally approved. Rejected because it cannot carry the `Strict` cookie
without relaxing it to `Lax` or `None`, which weakens a credential control to satisfy a hosting
preference. Deviating from §40.8 needs a PRD amendment; weakening the cookie would need a
constitution deviation under Principle VII. The amendment is the cheaper of the two.

---

## 2. Forwarded headers, first in the pipeline

**Decision**: `UseForwardedHeaders` accepting `X-Forwarded-Proto` only, with `KnownNetworks` and
`KnownProxies` cleared, registered before `UseHttpsRedirection()`.

**Rationale**: Container Apps terminates TLS at its ingress and forwards plain HTTP to the container.
`app.UseHttpsRedirection()` (already present) sees `http` and issues a redirect; the ingress serves
it back over HTTPS; the loop repeats. Clearing the known-networks lists is required because the
ingress does not appear on a recognised private network, and without clearing them the header is
ignored.

**Why trusting the header is safe here**: the container port is reachable only from the environment's
own ingress — nothing else can route to it. This reasoning goes in a code comment, because
"trust any caller's `X-Forwarded-Proto`" is exactly the kind of line a reviewer should stop at.

**Alternatives considered**: removing `UseHttpsRedirection()` — rejected; it is the control that
stops a plain-HTTP request being served, and the cookie is `Secure`. Restricting to a known proxy
range — rejected; Container Apps does not document a stable ingress range to pin.

---

## 3. Static files and fallback ordering

**Decision**: `UseDefaultFiles()`, `UseStaticFiles()`, then `MapControllers()`, then
`MapFallback("/api/{**path}", () => Results.NotFound())`, then `MapFallbackToFile("index.html")`.

**Rationale**: A single-page app needs unmatched paths to return `index.html` so client-side routes
deep-link. Without the `/api` fallback registered first, a typo'd or removed API route returns the
web app's HTML with status 200. A client expecting JSON then fails on parse rather than on status,
and an integration test asserting 404 silently passes for the wrong reason. FR-004 makes this a
requirement rather than a convention.

**Alternatives considered**: a middleware branch on `context.Request.Path.StartsWithSegments("/api")`
— equivalent behaviour, more code, and it duplicates routing knowledge the router already has.

---

## 4. Two health endpoints with different jobs

**Decision**: `GET /health` is anonymous and never touches the database; it is the container probe.
`GET /health/database` is anonymous, performs one lightweight read, and is called only by
production's post-deployment check.

**Rationale**: These endpoints answer different questions and conflating them causes harm in both
directions.

- A probe that queries the database will fail when the database is unreachable, and the platform's
  response to a failing probe is to replace the container. Restarting an application cannot fix a
  database problem; it removes a working application that could still serve its sign-in page and log
  a useful error. FR-023 forbids this.
- A deployment check that only calls `/health` proves the process started, which a container that
  cannot reach its database also does. Since the application identity and the migration identity
  became separate (§7), a successful migration no longer implies the *application* can read. FR-024
  requires proving it.

**Exposure**: an anonymous caller learns whether the database is reachable. That is already
observable — a broken database produces a 500 on the sign-in page. The body carries a fixed string;
the reason goes to `ILogger<T>`. FR-025.

**Why production only**: stage's database is free-tier and auto-pauses. Each call on a paused
database wakes it and consumes at least an hour of a roughly 55-hour monthly allowance. Production's
database is Basic and always awake, so the check costs nothing and needs no retry loop. A broken
stage database announces itself the moment anyone opens stage.

**Alternatives considered**: `Microsoft.Extensions.Diagnostics.HealthChecks` with tagged checks —
rejected under Principle XIII; two endpoints returning 200/503 is the whole requirement, and the
package brings a registry, tag filtering and a JSON response format nothing here consumes.

---

## 5. Data Protection keys persisted to SQL

**Decision**: `PersistKeysToDbContext<LootSinglesDbContext>()` with a fixed application name.
`LootSinglesDbContext` implements `IDataProtectionKeyContext`; one new migration adds the table.
Package: `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`.

**Rationale**: This is not a deploy-time nicety. The container scales to zero when idle, which is
what keeps it inside the free grant (§13). Data Protection keys default to memory when no
persistence is configured, so every scale-to-zero invalidates every session cookie — a picker who
takes a break is signed out, not just one who is working during a release. FR-026.

**Why this is not "a logging/telemetry package"**: Principle XI bans logging packages. This is
key management for the framework's own cookie protection, and the alternative (Azure Key Vault) is a
paid service. Keys live in application rows in a database encrypted at rest.

**Alternatives considered**: `minReplicas: 1` to avoid the restart — rejected on cost; an
always-running container at 0.25 vCPU consumes roughly 648,000 vCPU-seconds a month against a
180,000 free grant. Persisting keys to a file — rejected; container filesystems are ephemeral.

---

## 6. `migrate` as a one-shot command, run as a separate job

**Decision**: Add a `migrate` argument to `Program.cs`, dispatched exactly like the existing
`bootstrap-admin` branch (before the HTTP pipeline is configured, setting `Environment.ExitCode` and
returning). It runs as a Container Apps Job, not in the application's startup path.

**Rationale**: Three properties fall out of this and none of them do if the application migrates
itself at startup:

1. The application never needs schema permission (§7, FR-021).
2. A failed migration fails a job, visibly, instead of crash-looping a container.
3. Migration happens before the new version serves any request (FR-015), rather than racing it.

The one-shot pattern already exists in this codebase for `bootstrap-admin`, so this is the
established shape rather than a new one.

**Alternatives considered**: `context.Database.MigrateAsync()` during startup — rejected for the
three reasons above. Running `dotnet ef database update` from the deployment workflow — rejected
because the runner is outside the virtual network and SQL refuses it (§8); moving the database's
network boundary to accommodate a build agent is the wrong trade.

---

## 7. Two managed identities per environment

**Decision**:

| Identity | Roles | Used by |
|---|---|---|
| `id-loot-singles-<env>-app` | `db_datareader`, `db_datawriter` | The Container App |
| `id-loot-singles-<env>-migrate` | `db_ddladmin`, `db_datareader`, `db_datawriter` | The migrate job and the one-time bootstrap job |

Neither receives `db_owner`.

**Rationale**: The running application is the component exposed to the internet and therefore the
one an attacker reaches first. It needs to read and write rows; it never needs to change schema —
verified: `MigrateAsync` is reached only from the one-shot commands, and there is no
`ExecuteDeleteAsync` or `.Remove(` anywhere in `backend/src`.

Schema permission is what turns a SQL-injection or code-execution foothold into something durable.
With `db_ddladmin` an attacker can drop the filtered unique index that enforces one active claim per
employee (defeating Principle VI's server-enforced rule), add a trigger that copies packing-slip
addresses or PIN hashes on every read, or `TRUNCATE` the `PackingSlipAccesses` log that PRD §27
depends on for auditability. Without it, they can read and corrupt rows — unavoidable for an
application that serves this data — but cannot disable the rules, persist a foothold, or destroy the
audit trail.

`db_datareader`/`db_datawriter` on the migrate identity is not redundant: EF writes
`__EFMigrationsHistory`, and the bootstrap job inserts the first employee row.

**Cost**: zero. Managed identities are free; the only ongoing difference is two connection strings
that vary by `User Id=<clientId>`.

**Alternatives considered**: one identity with `db_ddladmin` — simpler by four lines of setup, and
the reason this was reviewed adversarially. Rejected because this database holds the only customer
PII in the product. `db_owner` — rejected outright.

---

## 8. Virtual network rule, not an IP rule

**Decision**: A `/27` subnet delegated to `Microsoft.App/environments` with the `Microsoft.Sql`
service endpoint enabled, and a SQL virtual network rule naming that subnet. Public network access
stays enabled but default-deny. No `0.0.0.0` "allow Azure services" rule ever exists.

**Rationale**: Microsoft's Container Apps documentation states that an environment's "outbound IPs
might change over time". An IP-based firewall rule therefore fails at an unpredictable moment with
no deployment running — the application simply stops reaching its database mid-shift, and because
`/health` is deliberately database-free (§4) the container stays "healthy" while every request
fails. A service endpoint identifies the subnet, not an address, so it does not drift.

The `0.0.0.0` rule is never acceptable: it admits every Azure tenant's resources, not just ours.

**Cost**: free. Microsoft documents "There's no extra charge for using service endpoints."

**VERIFIED 2026-09-23.** A subnet delegated to `Microsoft.App/environments` **does** accept a
`Microsoft.Sql` service endpoint. Confirmed against a live subscription with the throwaway spike in
quickstart.md C0, reading the values back off the created subnet rather than trusting the create
command's exit code:

```json
{ "delegations": ["Microsoft.App/environments"], "endpoints": ["Microsoft.Sql"] }
```

This was the one item that could have stopped the feature. The private-endpoint fallback at roughly
$7–8/month does not arise, and the cost model in §13 holds.

**Alternatives considered**: private endpoint (works, costs money, only needed if the check fails);
IP rule (unstable, above); NAT Gateway for a stable egress IP (~$32/month, defeats the cost ceiling).

---

## 9. Log Analytics for both environments

**Decision**: Each environment's Container Apps environment sends its log stream to a Log Analytics
workspace in that environment's own resource group. The application code is unchanged: `ILogger<T>`,
structured, to stdout.

**Rationale**: FR-027 requires records retained and searchable for at least 30 days, because this
feature ships without alerting by deliberate decision — a report from the shop is how problems
surface, which only works if the evidence outlives the report. With the log destination set to
`none`, Container Apps offers a live stream only; by the time someone looks, the evidence is gone.

**Constitution**: Principle XI was amended to v3.5.0 on 2026-09-22, before this plan, to state that
it governs how the *application* emits logs rather than what the platform does with stdout
afterwards. Feature 006's own spec had already deferred retention as "operational concerns outside
this feature". No application dependency is added.

**Cost**: the first 5 GB/month per billing account is free, with ~31 days' retention included. This
application logs attempt- and outcome-level events only (006 FR-009), so a two-person shop produces
megabytes. The allowance is per *billing account* and platform logs count toward it, so the $5 budget
alert is the backstop.

**Alternatives considered**: destination `none` (free, but fails FR-027); Application Insights
(a paid APM product and an application dependency, banned by Principle XI as amended); diagnostic
settings to blob storage (retains, but no query surface worth having).

---

## 10. Promotion by named commit, not by resolved digest

**Decision**: `deploy-production.yml` takes a **required** `commit` input with no default, one
environment-gated job whose `name` interpolates that input, and deploys `ghcr.io/…:sha-<commit>`.

**Rationale**: FR-013 requires the version being approved to be identifiable at approval time and
unchanged afterwards. A GitHub job carrying `environment:` does not start until a reviewer approves,
so any step inside it runs *after* approval. Resolving "whatever stage is running" there would mean
approving a lookup, not an image — and if a pull request merges while the run waits, production ships
a build nobody reviewed. A required input is a literal value fixed when the run starts and displayed
in the job list beside the *Review deployments* button, which is where the approval decision is made.

**What was removed, and the risk accepted**: an earlier draft added an ungated `resolve` job, digest
pinning, an `org.opencontainers.image.revision` cross-check and a query against stage's GitHub
deployment record to prove the image had been tested — roughly forty lines of workflow. Cut: in a
two-person shop the person approving is the person who merged it and watched stage, so it told them
something they already knew. **Risk accepted**: a tag is mutable where a digest is not. Nothing
except this workflow can push to that repository path and it never reuses a `sha-` tag, so the
window is theoretical.

Also removed: refusing a commit that is "not an ancestor of `main`". It is near-vacuous (stage only
builds from `main`), requires `fetch-depth: 0` or it errors on a shallow clone, and it **refuses
legitimate rollbacks** after any rebase of `main` — actively harmful at the moment it would matter.

---

## 11. Concurrency groups on both deploy workflows

**Decision**: `concurrency: { group: deploy-<env>, cancel-in-progress: false }`.

**Rationale**: FR-009. Two overlapping runs are safe at the migration step — since EF Core 9,
`MigrateAsync` acquires a database-wide lock first (on SQL Server, `sp_getapplock` named
`__EFMigrationsLock`, held for the whole migration), so a second job blocks rather than corrupting.
The container update and the rollback have no such lock: run A updates to image A, run B updates to
image B, A's checks fail, and A's rollback replaces B. Both runs finish green while the environment
runs an old image. Two lines of YAML remove the situation entirely.

**Known consequence**: GitHub keeps one run in progress and one pending; a third arrival replaces the
pending one. Three merges in quick succession means the middle commit never reaches stage. Correct
for a deployment — newest wins — and it is why production promotes a commit somebody named rather
than one a workflow inferred (§10).

**Alternatives considered**: `cancel-in-progress: true` — rejected; cancelling mid-migration or
mid-rollback is worse than queueing. `queue: max` — unnecessary at this scale.

---

## 12. Rollback by reactivating the previous revision

**Decision**: On post-deployment check failure, reactivate the Container App's previous revision.

**Rationale**: FR-017. Container Apps retains revisions, so the previous working version is already
present and addressable. Capturing the running image before each update and restoring it afterwards
is state we would have to track and get right; the platform already tracks it.

**Constraint this places on migrations**: rollback restores the *image*, never the schema. FR-016
therefore requires migrations to be backward-compatible with the immediately preceding version. This
is sharper than it first appears: EF Core 9 wrapped all pending migrations in a single transaction
and **EF Core 10 reverted that**, so on this project's version a migration failing partway can leave
some migrations applied and others not. Additive-only is what makes that survivable.

---

## 13. Cost model and the free grant

**Decision**: Azure SQL Basic for production (~$5/month); free offer for stage; everything else on
free tiers. Budget alert at $5. Expected total **$5–8/month** against the $10 ceiling of FR-028.

**Rationale**: The production database is the only resource that must be paid for.

**Re-examined 2026-09-23** at the Product Owner's request, after asking whether a different region
would be free. It would not: SQL Basic is **$0.161/day in eastus2, westus2, westus3 and centralus
alike** (Retail Prices API). No region makes a paid SKU free.

The free offer is real and was re-checked against its documentation. **Correction to an earlier
draft of this document**: a subscription gets **up to 10** free databases, not one. Each carries
100,000 vCore-seconds, 32 GB data and 32 GB backup, free with no time limit. Production *could*
therefore have had one.

It still should not, and the reason is the exhaustion behaviour rather than the allowance:

- The free offer is serverless General Purpose at a 0.5 vCore minimum, so 100,000 vCore-seconds is
  about **55 awake hours a month**. A shop open 8h/day × 22 days needs roughly 176. Light use does
  not rescue it either — serverless auto-pause has a **60-minute minimum delay**, so every burst of
  activity costs at least an hour, and two picking sessions a day already exceeds the allowance.
- When it runs out, the documented default is that **"the database is inaccessible until the start
  of the next calendar month"**. Not throttled — inaccessible, potentially for three weeks, with no
  alerting in this feature to warn anyone beforehand.
- The escape hatch is **one-way**: "once you have chosen Continue using database for additional
  charges, it's not possible to go back to the free amount with auto-pause", and it bills at
  serverless General Purpose rates, which exceed Basic's $4.90.

So the trade is **$59 a year against the shop being unable to fulfil orders for up to three weeks**,
discovered mid-month. Constitution Principle XI settles it: cost optimisation must not knowingly
make the production picking workflow unreliable. **Product Owner decision reaffirmed 2026-09-23:
Basic for production.**

Stage keeps the free offer, where being inaccessible until the 1st is an inconvenience rather than a
business problem.

**Irreversible setting to know about**: once a region is chosen for the first free database in a
subscription, **the same region applies to every free database in that subscription and cannot be
changed**. Stage's region choice is therefore permanent for any future free database.

**The container grant is tighter than it looks**: 180,000 vCPU-seconds per subscription per month, at
0.25 vCPU, is **200 active hours across both environments combined**. A shop open 8h/day × 22 days is
about 176 hours and fits; 10h/day × 26 days is about 260 and does not. Overage is single-digit
dollars, not tens, but "free container" is not unconditional.

**VERIFIED 2026-09-23** against Azure's public Retail Prices API and the Container Apps billing
documentation:

| Figure | Verified value | How |
|---|---|---|
| Azure SQL Basic, `eastus2` | **$0.161/day** (`SQL Database Single Basic`, meter `B DTU`) = **$4.90/month** | Retail Prices API |
| Container Apps vCPU overage | **$0.000024 / vCPU-second** | Retail Prices API |
| Container Apps memory overage | **$0.000003 / GiB-second** | Retail Prices API |
| Free grant | **180,000 vCPU-s, 360,000 GiB-s, 2M requests**, per subscription per calendar month | Billing documentation |

**The $0.10/hour environment management charge does not apply to this design.** The billing guide
attaches it to "private endpoints and planned maintenance… regardless of whether you use the
Consumption or Dedicated plans". This design uses a *service* endpoint (§8), configures no
maintenance window, and runs Consumption only. Had it applied it would have been roughly $73/month
per environment, so it was worth resolving rather than assuming.

**The cost question documentation could not settle — now answered, and the answer cost money.** The
guide warns that "if you use Container Apps with your own virtual network… additional charges might
apply". The empirical check placed in quickstart C6 for exactly this reason found it within hours:

```
0.0251   Virtual Network   IP Addresses   Standard IPv4 Static Public IP
```

**Every Container Apps environment with external ingress carries a Standard static public IPv4 at
$0.005/hour — $3.65/month, per environment.** This document priced the compute and the database and
never asked what the ingress costs. Two environments plus production's database would have been
**$12.20/month against FR-028's $10 ceiling**.

The Product Owner resolved it on 2026-09-23 by **sharing one Container Apps environment between both
logical environments** (spec.md Clarifications): one public IP, $8.55/month, stage and its automatic
deployment both kept. What is given up is the network boundary between stage and production. Data
isolation is unaffected, because it rests on identity — each app authenticates as its own managed
identity and stage's holds no database user in production's database — so this trades
defence-in-depth rather than the defence itself.

**The lesson worth keeping**: this was found by an empirical check written specifically because the
documentation hedged. Every other figure in §13 was verified against the Retail Prices API and was
correct. The one that was wrong was the one nobody thought to price at all, and only running it
surfaced it.

**A related correction.** This document claimed free usage "does not appear on your bill", so a cost
view could not show free-tier activity. That is wrong: free meters appear as line items at $0 with
`- Free` in the name — `General Purpose - Serverless - Compute Gen5 1 vCore - Free`,
`Standard Data Processed - Free`. A cost view can therefore show *which* free meters are active. It
still cannot show how much allowance remains, which is the limitation that matters.

---

## 14. Database restore posture

**Decision**: Rely on Azure SQL's included point-in-time restore, and **verify at creation** that it
covers at least 7 days (FR-031).

**Rationale**: This feature creates the production database, so its durability posture is decided
here or nowhere. Imported orders can be re-imported because TCGplayer remains authoritative (PRD);
what the retention window actually protects is pick history and the stored packing slips of PRD §27.

**Scope boundary**: FR-031 requires restore to be *possible*, verified by reading the policy off the
created database (SC-012). This feature does not perform a restore, automate one,
rehearse it, or monitor backup health.

**Alternatives considered**: long-term retention — a paid add-on that would breach FR-028 and needs a
separate cost decision.

---

## 15. Stage carries production's protections

**Decision**: Stage receives identical privacy and security configuration to production: default-deny
SQL with a virtual network rule, the two-identity split, Entra-only authentication, and its own
isolated resource group and workspace.

**Rationale**: FR-029 — the Product Owner confirmed on 2026-09-22 that stage may hold real customer
data. Reliability and cost are the only axes on which stage may be weaker. The earlier design
happened to give stage the same treatment; this makes it a requirement, so a later "it's only stage"
simplification is a specification violation rather than a judgement call.

---

## 16. Three text assertions, not nine

**Decision**: Assert against the new configuration files only where a mistake would be silent or
would breach a no-compromise control: no `0.0.0.0` firewall rule in any tracked file; production's
`commit` input is `required: true` with no `default`; both deploy workflows declare a `concurrency`
group. The precedent is the existing `DatabaseConfigurationTests`.

**Rationale**: Each of these three fails invisibly. A `default` on the commit input silently restores
the approve-a-lookup bug (§10); a missing concurrency group shows up only when two merges land a
minute apart (§11); a `0.0.0.0` rule breaches FR-020 while everything still works.

**What was removed, and the risk accepted**: six further assertions — Dockerfile non-root, the
approval gate's presence, tag-not-`latest`, the quality-gate call, and the `/health/database`
asymmetry. They assert that YAML written the same day says what it says, which is tautological when
written and only earns value on a later careless edit. **Risk accepted**: a later edit could run the
container as root or drop the quality-gate call without a test failing. Both are plain in a pull
request diff, and neither fails silently in production the way the three kept ones would.

---

## Figures to verify before provisioning

Every number in this document is from documentation, not measurement. Confirm against the portal
**before** creating anything, per §5 of the setup checklist:

1. ~~A subnet delegated to `Microsoft.App/environments` accepts a `Microsoft.Sql` service
   endpoint (§8).~~ **Verified 2026-09-23 — it does.** The riskiest assumption in the plan, cleared.
2. ~~Azure SQL Basic tier price in the target region (§13).~~ **Verified 2026-09-23 — $0.161/day in
   `eastus2`, about $4.90/month.** The free-offer allowance for *stage* is still unconfirmed.
3. ~~Container Apps free grant and overage rate (§13).~~ **Verified 2026-09-23** — grant and both
   overage rates exact. The "own virtual network" caveat is settled empirically at C6, not here.
4. Log Analytics free ingestion allowance and included retention (§9). Documented as 5 GB/month per
   billing account with ~31 days retention; not independently re-checked.
5. Azure SQL Basic point-in-time restore window ≥ 7 days (§14).
