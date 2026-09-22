# Quickstart: Automated Stage and Production Deployment

**Feature**: 019 | **Date**: 2026-09-22

Two parts: validating the application changes locally (repeatable, safe), and the one-time Azure
setup you run by hand (not automated — this repo does not provision from code).

---

## Part 1 — Validate the application changes locally

No Azure account needed. Run these before any infrastructure exists.

### Prerequisites

```bash
# From the repo root
dotnet build backend/LootSingles.sln
cd frontend && npm ci && npm run build && cd ..
```

### Automated tests

```bash
dotnet test backend/tests/LootSingles.UnitTests
dotnet test backend/tests/LootSingles.IntegrationTests   # requires Docker for Testcontainers
```

Expected: all green, including the new `ForwardedHeadersTests`, `HealthEndpointTests`,
`DatabaseHealthEndpointTests`, `SpaFallbackTests`, `DataProtectionPersistenceTests` and
`MigrateCommandTests`.

### The container image

```bash
docker build -t loot-singles:local .
docker run --rm loot-singles:local migrate          # fails: no connection string. Must NOT echo one.
docker run --rm -e ASPNETCORE_HTTP_PORTS=8080 -p 8080:8080 \
  -e ConnectionStrings__LootSingles="<your LocalDB or test connection string>" \
  loot-singles:local
```

Then, against the running container:

| Command | Expected |
|---|---|
| `curl -i localhost:8080/health` | `200`, empty body |
| `curl -i localhost:8080/health/database` | `200` with a reachable database; `503` without |
| `curl -i localhost:8080/api/unknown` | **`404`**, not HTML |
| `curl -i localhost:8080/api/orders` | **`401`**, not HTML |
| `curl -s localhost:8080/ \| grep id=\"root\"` | Matches — the web build is in the image |
| `curl -s localhost:8080/orders/1 \| grep id=\"root\"` | Matches — deep links serve the web app |
| `docker run --rm loot-singles:local whoami` | **Not** `root` |

### The failure that matters most

Stop the database (or point the container at an unreachable one) while it runs:

```bash
curl -i localhost:8080/health           # MUST still be 200 — this is the contract
curl -i localhost:8080/health/database  # MUST be 503
```

If `/health` returns anything but `200` here, the container platform would destroy a running
application because of a database problem. That is the exact failure FR-023 exists to prevent.

### Sessions survive a restart

```bash
# Sign in, capture the cookie, restart the container, reuse the cookie
curl -i -c jar.txt -X POST localhost:8080/api/auth/login -d '{...}'
docker restart <container>
curl -i -b jar.txt localhost:8080/api/orders   # MUST be 200, not 401
```

A `401` means Data Protection keys are still in memory (research.md §5), and every scale-to-zero
would sign out every picker.

---

## Part 2 — One-time Azure setup (per environment)

Run once for `stage`, once for `prod`. Azure CLI is not installed locally — use the portal's Cloud
Shell or install it.

### Step 0 — Verify before creating anything

**Do not skip this.** Every figure in this plan comes from documentation, not measurement, and two
of these can change the cost decision:

1. A subnet delegated to `Microsoft.App/environments` accepts a `Microsoft.Sql` service endpoint.
   **If it does not, stop and raise it** — the fallback is a private endpoint at roughly $7–8/month,
   which is a Product Owner cost decision.
2. Azure SQL free offer allowance, and Basic tier price in the target region.
3. Container Apps free grant (expected 180,000 vCPU-s/month) and the overage rate.
4. Log Analytics free ingestion allowance and included retention.
5. Azure SQL Basic point-in-time restore window is **at least 7 days** (FR-031).

### Step 1 — Resource group, network, identities

Resource group; virtual network with a `/27` subnet delegated to `Microsoft.App/environments` and the
`Microsoft.Sql` service endpoint enabled; **both** user-assigned managed identities
(`id-loot-singles-<env>-app`, `id-loot-singles-<env>-migrate`).

### Step 2 — SQL

SQL server with **Entra-only authentication** (so no password exists anywhere) and the database.
Add the **virtual network rule** naming the subnet. Then confirm:

- No `0.0.0.0` "allow Azure services" rule exists.
- Public network access is default-deny.
- Point-in-time restore is enabled with a window of at least 7 days.

### Step 3 — Log Analytics and Container Apps

Create the Log Analytics workspace first, then the Container Apps environment against the subnet with
its logs pointed at that workspace. Then the Container App on the **app** identity and the migrate
job on the **migrate** identity. The two connection strings differ only in `User Id=<clientId>`.

### Step 4 — Database users and roles

Add a temporary firewall rule for your own machine, run the grants, then **remove that rule**:

```sql
CREATE USER [id-loot-singles-<env>-app] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-loot-singles-<env>-app];
ALTER ROLE db_datawriter ADD MEMBER [id-loot-singles-<env>-app];

CREATE USER [id-loot-singles-<env>-migrate] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-loot-singles-<env>-migrate];
ALTER ROLE db_datawriter ADD MEMBER [id-loot-singles-<env>-migrate];
ALTER ROLE db_ddladmin  ADD MEMBER [id-loot-singles-<env>-migrate];
```

Neither identity gets `db_owner`. Then run the migrate job once — the first proof that the migrate
identity, the subnet rule and the migrations work together. It proves **nothing** about the app
identity; `/health/database` does that.

### Step 5 — First account

Run a throwaway bootstrap job on the **migrate** identity, then delete the job, which deletes the PIN
with it. Use a throwaway PIN and **change it at first sign-in** — until the job is deleted the value
sits in the job definition and your shell history (FR-032).

### Step 6 — GitHub

Two environments (`stage`, `production`) with their plain variables and OIDC federated credentials
scoped to each environment's own resource group. Restrict **both** environments' deployment branches
to `main`. Add the production reviewer, and **leave "Prevent self-review" unchecked** — Product Owner
decision, 2026-09-22: with two staff, a mandatory second approver makes production unfixable whenever
one of them is away. Add branch protection on `main` and the $5 budget alert.

### Step 7 — Custom domain (production only)

In Namecheap's Advanced DNS for the shop's domain, add a `CNAME` for the chosen subdomain pointing at
the Container App, plus the `TXT` record Azure asks for to prove ownership. Azure issues the
certificate free. The storefront's own records are untouched. This can be done any time after the
first release, with no redeploy.

---

## Part 3 — What only a real deployment proves

Once per environment, by hand:

1. **Sign in on a phone** over the public address, claim an order, record a pick. This is the first
   real *write* through the application identity.
2. **Deploy again** and confirm you are still signed in (FR-026).
3. **Confirm records are retained**: find that sign-in in the environment's Log Analytics workspace,
   not just in the live stream (FR-027).
4. **Confirm the privacy parity**: stage and production deny database access by default, both use the
   two-identity split, both log packing-slip access (FR-029, SC-010).

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| Endless redirects | Forwarded headers not registered first, or `KnownNetworks`/`KnownProxies` not cleared (research.md §2) |
| `/api/...` returns HTML | `MapFallback("/api/{**path}")` is registered after `MapFallbackToFile` (research.md §3) |
| Everyone signed out after a quiet period | Data Protection keys still in memory (research.md §5) |
| App healthy, every request fails | The app identity's grant or the virtual network rule. Query the workspace for `SqlException`; `az containerapp logs show` for live |
| Release succeeded, app cannot reach the database | The migrate identity works and the app identity does not — exactly what `/health/database` exists to catch |
