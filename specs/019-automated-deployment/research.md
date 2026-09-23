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

**To verify before provisioning**: that a subnet delegated to `Microsoft.App/environments` accepts a
`Microsoft.Sql` service endpoint. If it does not, the fallback is a private endpoint at roughly
$7–8/month, which is a Product Owner cost decision rather than an implementation detail — stop and
raise it.

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

**Rationale**: The production database is the only resource that must be paid for. The free SQL offer
provides roughly 100,000 vCore-seconds — about 55 awake hours a month — which a shop picking all day
exhausts in roughly a week, after which the database stops until the 1st. That is acceptable for
stage and unacceptable for production.

**The container grant is tighter than it looks**: 180,000 vCPU-seconds per subscription per month, at
0.25 vCPU, is **200 active hours across both environments combined**. A shop open 8h/day × 22 days is
about 176 hours and fits; 10h/day × 26 days is about 260 and does not. Overage is single-digit
dollars, not tens, but "free container" is not unconditional. Verify the grant and the overage rate
at provisioning along with every other figure here.

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

1. A subnet delegated to `Microsoft.App/environments` accepts a `Microsoft.Sql` service endpoint (§8).
2. Azure SQL free offer allowance and Basic tier price in the target region (§13).
3. Container Apps free grant and overage rate (§13).
4. Log Analytics free ingestion allowance and included retention (§9).
5. Azure SQL Basic point-in-time restore window ≥ 7 days (§14).
