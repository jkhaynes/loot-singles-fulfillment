# Quickstart: Automated Stage and Production Deployment

**Feature**: 019 | **Date**: 2026-09-22

Three parts:

1. **Validate the application changes locally** — repeatable, safe, no Azure account needed.
2. **The Azure runbook** — the one-time setup, written for someone who has never provisioned
   anything on Azure.
3. **What only a real deployment proves** — the handful of checks no script or test can stand in for.

Part 2 is done in the Azure portal, by Product Owner decision (2026-09-22): it is run twice, ever,
seeing each resource before it exists is worth more than reproducibility, and it is how you learn
where things live. Nothing in this repository forbids scripting it later. It ends with an optional
operator dashboard (Part G).

---

# Part 1 — Validate the application changes locally

No Azure account needed. Do this before any infrastructure exists.

### Build

```bash
dotnet build backend/LootSingles.sln
cd frontend && npm ci && npm run build && cd ..
```

### Automated tests

```bash
dotnet test backend/tests/LootSingles.UnitTests
dotnet test backend/tests/LootSingles.IntegrationTests   # needs Docker for Testcontainers
```

Expect all green, including the new `ForwardedHeadersTests`, `HealthEndpointTests`,
`DatabaseHealthEndpointTests`, `SpaFallbackTests`, `DataProtectionPersistenceTests` and
`MigrateCommandTests`.

### The container image

```bash
docker build -t loot-singles:local .
docker run --rm loot-singles:local migrate          # must fail WITHOUT printing a connection string
docker run --rm -p 8080:8080 \
  -e "ConnectionStrings__LootSingles=<your local connection string>" \
  loot-singles:local
```

Against the running container:

| Command | Expected |
|---|---|
| `curl -i localhost:8080/health` | `200`, empty body |
| `curl -i localhost:8080/health/database` | `200` with a reachable database |
| `curl -i localhost:8080/api/unknown` | **`404`**, not HTML |
| `curl -i localhost:8080/api/orders` | **`401`**, not HTML |
| `curl -s localhost:8080/ \| grep 'id="root"'` | Matches — the web build is in the image |
| `curl -s localhost:8080/orders/1 \| grep 'id="root"'` | Matches — deep links serve the web app |
| `docker run --rm loot-singles:local whoami` | **Not** `root` |

### The failure that matters most

Stop the database, or point the container at an unreachable one, and check both endpoints:

```bash
curl -i localhost:8080/health           # MUST still be 200 — this is the contract
curl -i localhost:8080/health/database  # MUST be 503
```

If `/health` returns anything but `200` here, Azure would destroy a *working* application because of
a database problem. That is the exact failure FR-023 exists to prevent.

### Sessions survive a restart

Sign in, capture the cookie, restart the container, reuse the cookie. A `401` afterwards means Data
Protection keys are still in memory, and every scale-to-zero would sign out every picker.

---

# Part 2 — The Azure runbook

Run this **twice**: once for `stage`, once for `prod`. Roughly an hour per environment the first
time, less the second.

**Everything is done in the Azure portal** at [portal.azure.com](https://portal.azure.com), by
Product Owner decision (2026-09-22): seeing each resource before it exists is worth more here than
reproducibility, and it is how you learn where things live for the day something breaks.

The one exception is **Part D**, the verification checks, which stay as CLI commands. Those ask
precise questions — "show me every firewall rule on this server" — and confirming an *absence* by
clicking through blades is exactly where a missed setting hides.

## Part A — Before you start

### A1. Sign in and confirm the subscription

Open [portal.azure.com](https://portal.azure.com) and sign in.

Check the subscription you are about to build in: click your account avatar (top right), or search
**Subscriptions**. You should see **`Azure subscription 1`**
(`5bdba28d-1561-47b0-b51a-d93c55793db1`).

**Why this matters more than it looks**: every resource below lands in whichever subscription the
form has selected. Building production in the wrong one is invisible until the bill arrives, and the
portal's create forms always show the subscription — read it each time rather than trusting the
default.

**If sign-in asks for multi-factor authentication**, that is expected: Azure requires MFA for
sign-ins. Complete the prompt. This affects you, not the deployment — GitHub Actions signs in as a
workload identity through the federated credential in C13, and no user MFA policy applies to it.

### A2. Register the resource providers

Azure requires each service to be switched on for a subscription before it can be used. The portal
usually registers a provider for you when you create the first resource of that type, but doing it
up front avoids a create failing several fields into a form.

1. Search **Subscriptions** → open `Azure subscription 1` → **Settings** → **Resource providers**.
2. Search for each of these, select it, and click **Register** if the status is not already
   *Registered*:
   - `Microsoft.App` — Container Apps
   - `Microsoft.OperationalInsights` — Log Analytics
   - `Microsoft.Sql` — Azure SQL
   - `Microsoft.Network` — virtual networks
   - `Microsoft.ManagedIdentity` — managed identities

Registration takes a few minutes and runs in the background. `Microsoft.Sql` and `Microsoft.Network`
are probably already registered, since `loot-singles-dev-sql` exists.

### A3. Optional — set up the CLI for Part D only

Part C needs no command line. Part D's verification checks do, and they are worth running.

Open a **new** PowerShell window (a shell opened before Azure CLI was installed will not find it),
then:

```powershell
az version
az login
az account show --query "{name:name, id:id}" -o table
az extension add --name log-analytics    # needed by Part D's log query
```

**If `az login` reports `AADSTS50076` / `Status_InteractionRequired` and then "No subscriptions
found"**: sign-in worked, but the CLI could not get a token for the directory without MFA. The
enumeration failing is *why* nothing was listed — it does not mean you have no subscription. Sign in
scoped to the tenant the error names, which forces an interactive prompt:

```powershell
az login --tenant <the tenant id from the error>
az account list --query "[].{name:name, id:id, state:state}" -o table
```

**If `az` is not found at all**: it installs to
`C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd`. Use that full path, or add its folder to
your PATH and open another new terminal.

---

## Part B — The names, decided once

Use these exactly. Consistency between the two environments is a requirement, not a preference:
FR-029 says stage must carry every protection production does, and mismatched names are how that
quietly stops being true.

**Shared by both environments** — built once, in `rg-loot-singles-shared`:

| What | Name |
|---|---|
| Resource group | `rg-loot-singles-shared` |
| Virtual network | `vnet-loot-singles` (`10.20.0.0/16`) |
| Subnet | `snet-apps` (`10.20.0.0/27`) |
| Container Apps environment | `cae-loot-singles` |
| Log Analytics workspace | `log-loot-singles` |

**Per environment** — built twice:

| What | `stage` | `prod` |
|---|---|---|
| Resource group | `rg-loot-singles-stage` | `rg-loot-singles-prod` |
| App identity | `id-loot-singles-stage-app` | `id-loot-singles-prod-app` |
| Migrate identity | `id-loot-singles-stage-migrate` | `id-loot-singles-prod-migrate` |
| SQL server | `loot-singles-stage-sql` | `loot-singles-prod-sql` |
| Database | `lootsingles` (free offer) | `lootsingles` (**Basic**) |
| Container App | `ca-loot-singles-stage` | `ca-loot-singles-prod` |
| Migrate job | `caj-loot-singles-stage-migrate` | `caj-loot-singles-prod-migrate` |
| App registration | `github-loot-singles-stage` | `github-loot-singles-prod` |
| GitHub environment | `stage` | **`production`** |

> **Why one Container Apps environment rather than two** (Product Owner decision, 2026-09-23): each
> one carries a Standard static public IPv4 at **$3.65/month**, and two of them plus production's
> database came to $12.20 against the $10 ceiling. Sharing brings it to **$8.55** and keeps stage.
>
> The cost is the **network boundary**: both apps sit in one subnet, so stage can reach production's
> SQL server at the network level. Data isolation is unaffected and is the real control — each app
> authenticates as its own managed identity, and stage's has **no database user** in production's
> database, so it cannot read a row however reachable the server is.
>
> Two knock-on effects: **retained records are shared** (one environment logs to one workspace;
> entries carry the app name so they stay separable), and **the two environments can no longer be
> torn down independently** at the resource-group level (Part E).

Two of those are easy to get wrong:

- **SQL server names are unique across all of Azure**, not just your subscription. These follow the
  convention `loot-singles-dev-sql` already set, so they are likely free. If one is rejected, add a
  short suffix (`loot-singles-prod-sql-jh01`) and use it everywhere.
- **The GitHub environment for production is `production`, not `prod`.** The federated credential in
  C13 matches on that string exactly, and a mismatch fails at deploy time with an unhelpful message.

### The region is West US 2, and it is not a choice

The existing `loot-singles-dev` database uses the SQL free offer, and Azure applies the region of the
**first** free-offer database to **every** free database in the subscription — permanently, with no
way to change it. Stage's free database must therefore be West US 2. Putting everything there keeps
the environments beside dev and avoids cross-region latency between an app and its database.

Region does **not** affect price: SQL Basic is $0.161/day in eastus2, westus2, westus3 and centralus
alike. There was never a cheaper region to find.

### Existing resources this runbook does not touch

`rg-loot-singles-dev` holds `loot-singles-dev-sql` and the `loot-singles-dev` database used for local
development. **Leave it alone.** Nothing below modifies it, and stage and prod each get their own SQL
server rather than sharing that one — logical servers are free (you pay per database), and virtual
network rules are set at *server* level, so one shared server would place stage's subnet and prod's
subnet on the same network boundary. FR-006 requires neither environment to be able to reach the
other's data.

Worth knowing while you are here: dev is configured to **auto-pause on exhaustion**, so if it burns
its 100,000 free vCore-seconds it becomes inaccessible until the 1st of the next month. That is the
same behaviour that ruled the free offer out for production.

---
## Part C — The steps, in the Azure portal

Everything below is done at **[portal.azure.com](https://portal.azure.com)**. Do the steps in order;
later ones need earlier ones.

**Why the portal rather than the CLI**: you see what you are creating before it exists, every blade
has a **Review + create** step that shows the whole configuration, and the subscription is visible on
every form instead of being whatever the shell last selected. It is also how you learn where things
live, which matters the first time something breaks.

**The cost of that choice, stated plainly**: the portal cannot guarantee stage and production are
configured identically the way re-running a command can, and FR-029 requires stage to carry exactly
the protections production does. That is why **Part D is not optional** — it is the check that the
two environments really match. Part D stays as CLI commands because "show me every firewall rule" is
a precise question, and confirming an *absence* by clicking through blades is where mistakes hide.

**Some steps are done once, some twice.** Names come from Part B.

| Steps | How often | Into |
|---|---|---|
| **C1** resource groups | once — creates all three | — |
| **C2** virtual network and subnet, **C7** Log Analytics, **C8** Container Apps environment | **once** | `rg-loot-singles-shared` |
| **C3** identities, **C4** SQL server, **C5** database, **C6** network rule, **C9** container app, **C10** migrate job, **C11** database grants | **twice** — once for `stage`, once for `prod` | `rg-loot-singles-<env>` |
| **C12–C15** GitHub, credentials, budget, domain | see each step | — |

Do the shared steps first, then everything per-environment for `stage`, then the same for `prod`.
The shared Container Apps environment must exist before either container app can be created, and its
subnet is fixed at creation — so C2 and C8 genuinely cannot be reordered.

> **Check the Resource group field on every single create form.** The portal pre-fills it with
> whichever group you used last, across sessions and across resource types. That default is silent,
> it is often wrong, and the consequences are not always recoverable — a virtual network created in
> the wrong group cannot be moved afterwards, because the Container Apps environment stores its
> subnet's full resource ID and that ID is immutable. The fix is deleting and rebuilding the
> environment, the app and the job. Read the field; do not trust it.
>
> **Portal wording drifts, and this runbook has been wrong about it more than once.** Field labels,
> blade layouts and left-nav groupings change between visits, and the steps here were written from
> knowledge rather than from a live portal — so treat a mismatch as expected rather than as your
> mistake.
>
> Two habits cover almost all of it: **search the portal for the thing itself** rather than
> navigating to its parent and hunting (searching "Cost analysis" beats opening Cost Management),
> and read the **Review + create** tab, which always lists what is actually about to be created
> whatever the form called the fields. If something looks materially different from what is
> described here, that is worth reporting rather than guessing past.

---

### C0. Verify the one thing that can stop this feature

**Already done — 2026-09-23.** A subnet delegated to Container Apps accepted a `Microsoft.Sql`
service endpoint, so the design holds and the ~$7–8/month private-endpoint fallback does not arise.
Skip to C1.

*(If you are ever running this in a fresh subscription, redo it: create a throwaway resource group
with a virtual network and a `/27` subnet, set the subnet's delegation to `Microsoft.App/environments`
and its service endpoint to `Microsoft.Sql`, confirm both stuck, then delete the group.)*

---

### C1. Resource group

Everything for one environment lives in one resource group, which is what makes "delete it and start
over" safe (Part E).

Create **three**: `rg-loot-singles-shared`, `rg-loot-singles-stage`, `rg-loot-singles-prod`.

1. In the portal search box, type **Resource groups**, open it, click **+ Create**.
2. **Subscription**: `Azure subscription 1`.
3. **Resource group**: the name you are creating. Repeat for all three.
4. **Region**: **(US) West US 2**.
5. **Review + create** → **Create**.

**Confirm**: the group appears in the Resource groups list, region West US 2.

> **Why West US 2 is not a choice**: the existing `loot-singles-dev` database uses the SQL free
> offer, and Azure applies the region of the first free-offer database to every free database in the
> subscription — permanently. Stage's free database must therefore be West US 2, and keeping
> everything together avoids cross-region latency between an app and its database.

---

### C2. Virtual network and subnet

A private network for this environment. The Container Apps environment will sit inside the subnet,
which is what later lets SQL trust it *by name* instead of by IP address — and Container Apps
outbound IP addresses are documented as changing without warning.

> **Do this before C8, and check the Resource group field.** A Container Apps environment's subnet
> is fixed at creation and cannot be
> changed later, so an environment built before this exists — or pointing at a virtual network in
> the wrong resource group — has to be deleted and rebuilt along with the app and the job.

1. Search **Virtual networks** → **+ Create**. (If a browse blade ever lacks a Create button, use
   **+ Create a resource** at the top left of the portal home and search the resource type there —
   that route always works.)
2. **Basics**: resource group **`rg-loot-singles-shared`**, name `vnet-loot-singles`, region
   **West US 2**.
3. **IP addresses** tab:
   - Set the address space to **`10.20.0.0/16`** (use `10.30.0.0/16` when you do `prod`).
   - Remove the `default` subnet if one is pre-filled, then **+ Add a subnet**:
     - **Name**: `snet-apps`
     - **Starting address / size**: `10.20.0.0` with size **/27 (32 addresses)**
     - **`/27` is the minimum Container Apps accepts.** Anything smaller is rejected later.
   - **Add**.
4. **Review + create** → **Create**.

Now set the two properties that matter, which are not on the create form:

5. Open the new virtual network → **Subnets** → click **`snet-apps`**.
6. **Subnet delegation**: choose **`Microsoft.App/environments`**.
7. **Service endpoints** → **Services**: tick **`Microsoft.Sql`**.
8. **Save**.

**Confirm**: reopen `snet-apps` and check that delegation shows `Microsoft.App/environments` **and**
service endpoints shows `Microsoft.Sql`. Both must be present — this pairing is the whole reason the
database can be locked down for free.

---

### C3. The two identities

A managed identity is an account Azure manages for you, so the application proves who it is without
any password existing. Two are created because the application and the migration job are allowed to
do different things:

- **app** — reads and writes rows. This is the internet-facing component.
- **migrate** — also changes database structure. Runs only during a deployment.

Keeping structural permission away from the internet-facing component is what stops a compromised
application dropping the index that enforces one claim per employee, or wiping the packing-slip
access log.

1. Search **Managed Identities** → **+ Create**.
2. Resource group `rg-loot-singles-stage`, region **West US 2**, name
   **`id-loot-singles-stage-app`** → **Review + create** → **Create**.
3. Repeat for **`id-loot-singles-stage-migrate`**.

**Record the Client ID of each.** Open each identity → **Overview** → copy **Client ID** (a GUID).
You need both in C7, and they are easy to confuse — label them as you paste them somewhere.

---

### C4. SQL server

Created with **Microsoft Entra-only authentication**, which means no SQL admin password exists at
all. There is nothing to store, rotate, or accidentally commit.

1. Search **SQL servers** → **+ Create**.
2. **Basics**: resource group `rg-loot-singles-stage`, server name **`loot-singles-stage-sql`**,
   location **West US 2**.
3. **Authentication method**: choose **Use Microsoft Entra-only authentication**.
4. Click **Set admin**, find **your own account**, select it.
5. **Review + create** → **Create**.

**If the name is rejected**: SQL server names are unique across all of Azure, not just your
subscription. Add a short suffix (`loot-singles-stage-sql-jh01`) and use it consistently.

**Confirm**: the server's **Overview** shows *Microsoft Entra-only authentication: Enabled*, and no
SQL administrator login is listed.

---

### C5. Database

**Stage** uses the free offer. **Production** uses Basic at about $4.90/month — the free offer's
allowance is roughly 55 awake hours a month against the ~176 a shop needs, and when it runs out the
database becomes *inaccessible until the 1st*, which is not survivable for fulfillment work.

1. Search **SQL databases** → **+ Create**.
2. **Basics**: resource group `rg-loot-singles-stage`, server `loot-singles-stage-sql`, database name
   **`lootsingles`**.
3. **Want to use SQL elastic pool?** → **No**.
4. **Compute + storage** → **Configure database**:
   - **For `stage`**: if the **free offer** banner appears, apply it. That gives serverless General
     Purpose with 100,000 vCore-seconds free per month. Leave the exhaustion behaviour on
     **auto-pause** — stage going quiet until the 1st is an inconvenience, not a business problem.
   - **For `prod`**: switch the service tier to **Basic** (DTU-based). It is the cheapest
     always-awake option and the one the cost model assumes.
5. **Backup storage redundancy**: **Locally-redundant** is sufficient and cheapest.
6. **Networking** tab: **Public endpoint**. Set **both** "Allow Azure services…" and "Add current
   client IP address" to **No**. You add the one rule that matters in C6.
7. **Review + create** → **Create**.

**Confirm — production only**: open the database → **Backups** (or *Point-in-time restore*) and check
the retention is **at least 7 days** (FR-031). Basic includes 7. If it shows fewer, stop and raise it.

---

### C6. Lock the database to the subnet

This is the control that replaces a password with a network boundary.

1. Open the **SQL server** (`loot-singles-stage-sql`) → **Security** → **Networking**.
2. **Public network access**: **Selected networks**.
3. Under **Virtual networks**, click **+ Add existing virtual network**:
   - **Name**: `allow-snet-apps`
   - **Virtual network**: `vnet-loot-singles`
   - **Subnet**: `snet-apps`
   - **Save**.
4. **Firewall rules**: confirm the list is **empty**.
5. **Exceptions**: confirm **"Allow Azure services and resources to access this server" is
   UNCHECKED**.
6. **Save**.

> **Step 5 is the one to get right.** That checkbox sounds harmless and is the `0.0.0.0` rule FR-020
> forbids: it admits *every other Azure customer's* resources to your database, not just yours. This
> database holds customer names and addresses.

**Confirm**: the Networking blade shows one virtual network rule, zero firewall rules, and the Azure
services exception unticked.

---

### C7. Log Analytics workspace

Where the application's log output is kept so it can be searched later. Without it you get a live
stream only, and the evidence is gone before anyone looks.

1. Search **Log Analytics workspaces** → **+ Create**.
2. Resource group **`rg-loot-singles-shared`**, name **`log-loot-singles`**, region **West US 2**.
3. **Review + create** → **Create**.

The first 5 GB per month is free, with about 31 days of retention included.

---

### C8. Container Apps environment

The shared space the container runs in, placed inside your subnet.

> **C2 must be finished first, and this is not recoverable later.** A Container Apps environment's
> infrastructure subnet is fixed when the environment is created and **cannot be changed
> afterwards**. Create it without the virtual network and you get Azure-managed networking, C6 has
> no subnet to write a rule against, and the database cannot be locked down at all — the only fix is
> deleting the environment and building it again. Confirm `snet-apps` exists with both its
> delegation and its `Microsoft.Sql` service endpoint before starting this step.

1. Click **+ Create a resource** (top left of the portal home) → search
   **"Container Apps Environment"** → **Create**. The *Container Apps Environments* browse blade
   does not reliably offer a Create button; the Marketplace route always works. You can also reach
   the same form through **Create new** beside the Environment field when creating a Container App.
2. **Basics**: resource group **`rg-loot-singles-shared`**, name **`cae-loot-singles`**, region
   **West US 2**.
3. **Networking** tab:
   - **Use your own virtual network**: **Yes**
   - **Virtual network**: `vnet-loot-singles`
   - **Infrastructure subnet**: `snet-apps`
   - Leave the environment **externally accessible** — the shop reaches it over the internet.
4. **Monitoring** tab: **Logs destination** = **Azure Log Analytics**, workspace
   `log-loot-singles`.
5. **Review + create** → **Create**. This one takes several minutes.

**Confirm**: the environment's **Overview** shows the virtual network and subnet you chose.

> **Come back here about 24 hours after creating the first environment** and check what is actually
> being billed — this is the one cost question documentation could not settle. Go to **Cost
> Management** → **Cost analysis**, scope to the subscription, group by **Meter**.
>
> **Expected**: nothing from Container Apps at all while usage stays inside the free grant, and the
> SQL meter at about $0.16/day for production.
>
> **If an "Environment Management Hour" or "Dedicated Plan Management" meter is accruing**, that is
> roughly $73/month per environment and it breaks FR-028. Stop before creating the second
> environment and raise it — the fix would be a design change, not a setting.

---

### C9. The container app

At this point there is no image in the registry yet, so create the app on a placeholder and let the
first deployment replace it.

1. Search **Container Apps** → **+ Create**.
2. **Basics**: resource group `rg-loot-singles-stage`, name **`ca-loot-singles-stage`**, region
   **West US 2**, environment `cae-loot-singles`.
3. **Container** tab: tick **Use quickstart image** for now.
4. **Ingress** tab:
   - **Ingress**: **Enabled**
   - **Ingress traffic**: **Accepting traffic from anywhere**
   - **Target port**: **8080**
5. **Review + create** → **Create**.

Then set the three things the create form does not cover:

6. **Settings → Identity → User assigned → + Add** → `id-loot-singles-stage-app` → **Add**.
7. **Application → Containers → Edit and deploy**. This opens a *Create and deploy new revision*
   form. Do everything below inside this one form, then deploy once — each deploy creates a new
   revision, so doing it in one pass avoids stacking up revisions for no reason.

8. **Click the container name** to open its settings. **CPU and memory live here, with the
   container — not on the Scale blade**, which only carries replica counts. Set:
   - **CPU cores**: `0.25`
   - **Memory**: `0.5 Gi`
   - **Environment variables** → **+ Add**:
     - **Name**: `ConnectionStrings__LootSingles`
     - **Value**:
       ```
       Server=tcp:loot-singles-stage-sql.database.windows.net,1433;Database=lootsingles;Authentication=Active Directory Managed Identity;User Id=<APP identity Client ID from C3>;Encrypt=True;
       ```
       Note there is **no password** in that string — that is the point of C4.

   > Container Apps only allows **fixed CPU/memory pairs**, at a 1:2 ratio. `0.25` CPU goes with
   > `0.5 Gi`. If `0.5 Gi` is not offered, set the CPU value first — the memory list changes with it.

9. **Scale** section of the same form: **Min replicas 0**, **Max replicas 1**. Min 0 is what keeps
   this inside the free grant; it also means the container shuts down when idle, which is why the
   application persists its session keys to the database rather than memory.

10. **Create** the revision.

**Record the application URL**: **Overview** → **Application Url**. You need it for the GitHub
environment variables in C12.

---

### C10. The migrate job

Eventually this runs the *same image* as the container app, with a `migrate` argument, under the
**migrate** identity. It exists so schema changes are applied by something that is not the
internet-facing application (FR-021).

**It uses the same environment as the container app: `cae-loot-singles`.** Not a new one. The
environment is what places the job inside `snet-apps`, so its outbound traffic comes from the subnet
the SQL virtual network rule trusts. A job in any other environment could not reach the database.

1. **+ Create a resource** → search **"Container Apps Job"** → **Create**.
2. **Basics**:
   - Resource group `rg-loot-singles-stage`
   - Job name **`caj-loot-singles-stage-migrate`**
   - Region **West US 2**
   - **Container Apps Environment**: **`cae-loot-singles`** — the same one as C9
   - **Trigger type**: **Manual**
3. **Container** tab. **Jobs do not offer a quickstart image** — that is a Container *App*
   convenience this form does not have — so give it Microsoft's sample job image as a placeholder:
   - **Image source**: Docker Hub or other registries
   - **Image type**: Public
   - **Registry login server**: `mcr.microsoft.com`
   - **Image and tag**: `k8se/quickstart-jobs:latest`
   - **CPU and Memory**: `0.25` CPU, `0.5 Gi` — same pairing rule as C9
   - **Command override**: leave empty. **Arguments**: `migrate`
   - **Replica timeout**: `600` seconds
   - **Replica retry limit**: `1` — **this field is required**; the create fails without it. One
     retry is right here: `migrate` checks for pending migrations before doing anything, so a retry
     after a transient network blip is a safe no-op rather than a second attempt at the same work.

   > The placeholder is thrown away by the first deployment, which replaces the image with the real
   > one from GHCR. The job only needs to *exist* now, so the workflow has something to update and so
   > you can run the first migration by hand. Running it before the real image is in place just fails
   > harmlessly — the sample image does not understand a `migrate` argument.

4. **Review + create** → **Create**.
5. **Settings → Identity → User assigned → + Add** → **`id-loot-singles-stage-migrate`** → **Add**.
6. **Environment variables** (on the job's container settings, same place as C9): add
   `ConnectionStrings__LootSingles` with the **migrate** identity's Client ID:
   ```
   Server=tcp:loot-singles-stage-sql.database.windows.net,1433;Database=lootsingles;Authentication=Active Directory Managed Identity;User Id=<MIGRATE identity Client ID from C3>;Encrypt=True;
   ```

> **The two connection strings differ only in the `User Id=` GUID**, and swapping them is the
> easiest mistake in this whole runbook. It also fails in a way that does not look like a mistake:
> the application would silently gain permission to change database schema — exactly what FR-021
> forbids — while the migrate job would lose it and fail on the first deployment. Check the GUID
> against C3 before saving, rather than after something breaks.

---

### C11. Database users and permissions

The fiddliest step. Azure cannot grant database roles — that is SQL, not Azure — so this is run as
SQL against the database, signed in as yourself (you are the server administrator from C4).

Use the portal's query editor, which avoids the MFA problems local `sqlcmd` has:

> **Expect the first connection to fail on stage.** The stage database is serverless and pauses
> after 60 minutes idle. The first attempt after a pause triggers the resume and returns:
>
> > *Database 'lootsingles' on server '…' is not currently available. Please retry the connection
> > later.*
>
> That is the wake-up working, not a fault. **Wait about 30 seconds and try again.** Check it with
> `az sql db show -g rg-loot-singles-stage -s loot-singles-stage-sql -n lootsingles --query status -o tsv`
> — you want `Online`.
>
> This is also exactly what a picker would see against a paused database, which is why handling it
> gracefully is its own feature. Production is Basic and always awake, so it does not do this.

1. Open the **database** (`lootsingles`) → **Query editor (preview)**.
2. Sign in with **Microsoft Entra authentication** as yourself.
3. **If it refuses to connect**, your own machine is not in the subnet. Temporarily add your IP:
   SQL **server** → **Networking** → **+ Add your client IPv4 address** → **Save**. **Remember to
   remove it at the end of this step.**
4. Run:

```sql
CREATE USER [id-loot-singles-stage-app] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-loot-singles-stage-app];
ALTER ROLE db_datawriter ADD MEMBER [id-loot-singles-stage-app];

CREATE USER [id-loot-singles-stage-migrate] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-loot-singles-stage-migrate];
ALTER ROLE db_datawriter ADD MEMBER [id-loot-singles-stage-migrate];
ALTER ROLE db_ddladmin  ADD MEMBER [id-loot-singles-stage-migrate];
```

The user names are the **identity resource names** from C3, not their Client IDs.

5. Confirm who has what — and that neither is `db_owner`:

```sql
SELECT p.name AS member, r.name AS role
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals p ON p.principal_id = m.member_principal_id
WHERE p.name LIKE 'id-loot-singles%' ORDER BY p.name, r.name;
```

**Expected**: the app identity in `db_datareader` and `db_datawriter` **only**; the migrate identity
in those two plus `db_ddladmin`. **Neither in `db_owner`.** If the app identity has `db_ddladmin`,
fix it before going on — that is the control FR-021 requires:

```sql
ALTER ROLE db_ddladmin DROP MEMBER [id-loot-singles-stage-app];
```

6. **Remove every firewall rule — including one you did not create.** The portal's query editor adds
   its own rule automatically when you connect, named `QueryEditorClientIPAddress_…`, whether or not
   you added one in step 3. Go to the SQL **server** → **Networking**, delete **all** entries under
   *Firewall rules*, and **Save**. Leaving any of them means a home IP address keeps standing access
   to a database holding customer names and addresses.

   Confirm the list is empty afterwards — this is one of Part D's checks, and it is easier to get
   right now than to discover later.

---

### C12. GitHub environments

In your browser, go to the repository → **Settings** → **Environments** → **New environment**.

**Create `stage`:**

1. Name it `stage` → **Configure environment**.
2. **Deployment branches and tags** → **Selected branches and tags** → add a rule for `main`.
3. Leave **Required reviewers** unchecked — stage deploys unattended.
4. **Environment variables** → add one per row:

| Name | Value |
|---|---|
| `AZURE_SUBSCRIPTION_ID` | `5bdba28d-1561-47b0-b51a-d93c55793db1` |
| `AZURE_TENANT_ID` | from **Microsoft Entra ID → Overview → Tenant ID** |
| `AZURE_CLIENT_ID` | from C13 below |
| `RESOURCE_GROUP` | `rg-loot-singles-stage` |
| `CONTAINER_APP` | `ca-loot-singles-stage` |
| `MIGRATE_JOB` | `caj-loot-singles-stage-migrate` |
| `APP_URL` | the Application Url from C9 |

**These are variables, not secrets** — none of them is a credential.

**Create `production`** the same way, with two differences:

- Tick **Required reviewers** and add yourself.
- **Leave "Prevent self-review" unchecked.** Product Owner decision, 2026-09-22: with two staff, a
  mandatory second approver makes production unfixable whenever one of you is away. The gate is a
  deliberate pause on a named version, not a two-person control.

---

### C13. Let GitHub sign in to Azure without a password

The least intuitive step, so here is what it actually does. GitHub Actions needs to run commands
against your subscription. The old way was storing a credential as a GitHub secret. Instead you
register a *trust*: "when GitHub says a workflow is running in this repository, in this environment,
accept that as proof of identity." No password exists, so none can leak.

The portal's wizard for this is genuinely better than the CLI, because it asks for the GitHub details
field by field instead of making you hand-build a subject string.

**Get GitHub's numeric ids first.** The portal now uses the *immutable id* subject format, which
pins the trust to GitHub's permanent numeric ids rather than to names. Names can be renamed and
later claimed by someone else; the numbers cannot. Run this once — the values are the same for both
environments:

```powershell
gh api repos/jkhaynes/loot-singles-fulfillment --jq '{owner_id: .owner.id, repo_id: .id}'
```

At the time of writing: organization id **`7768504`**, repository id **`1340223419`**.

**Register the application:**

1. Search **Microsoft Entra ID** → **App registrations** → **+ New registration**.
2. Name: **`github-loot-singles-stage`**. Leave the rest default → **Register**.
3. Copy the **Application (client) ID** — that is `AZURE_CLIENT_ID` for C12.

**Add the federated credential:**

4. In that app registration → **Certificates & secrets** → **Federated credentials** → **+ Add
   credential**.
5. **Federated credential scenario**: **GitHub Actions deploying Azure resources**. This dropdown is
   the first field, and it must be set before anything else makes sense. If you see an **Issuer**
   box you can type into, you are on **Other issuer** instead — the GitHub scenario fills the issuer
   itself and leaves it read-only.
6. Fill in — leave **Issuer**, **Subject identifier** and **Audience** alone, they are generated:

   | Field | Stage | Production |
   |---|---|---|
   | Organization | `jkhaynes` | `jkhaynes` |
   | Organization ID | `7768504` | `7768504` |
   | Repository | `loot-singles-fulfillment` | `loot-singles-fulfillment` |
   | Repository ID | `1340223419` | `1340223419` |
   | Entity type | **Environment** | **Environment** |
   | GitHub environment name | `stage` | `production` — *not* `prod` |
   | Name | `github-stage` | `github-production` |

7. Check the generated **Subject identifier** before pressing Add. For stage it must read exactly:

   ```
   repo:jkhaynes@7768504/loot-singles-fulfillment@1340223419:environment:stage
   ```

   If it does not, a field above it is wrong. Do not hand-edit the subject to force it.
8. **Add**.

**Grant it access to the two resources it deploys, and nothing else:**

The deployment does exactly two things: it starts the migrate job, and it updates the container app.
So the credential gets Contributor on those two resources individually — not on the resource group,
which would also hand it the environment's SQL server and managed identities for no reason.

9. Open **`ca-loot-singles-stage`** → **Access control (IAM)** → **+ Add** → **Add role assignment**.
10. **Role**: **Contributor** → **Next**.
11. **Members**: **User, group, or service principal** → **+ Select members** → search
    `github-loot-singles-stage` → select it → **Review + assign**.
12. Repeat steps 9–11 on **`caj-loot-singles-stage-migrate`**.

Per-resource scope is what stops stage's credential from touching production (FR-006). It replaced
resource-group scope when the two environments began sharing one Container Apps environment — with
shared infrastructure, a resource group is no longer the line between them.

> **If a deployment later fails with "no matching federated identity record found"**, the subject did
> not match. Compare the failing run's subject against the string in step 7 character by character.
> The usual causes are the environment name (`production`, not `prod`) and a mistyped numeric id —
> and note that an id typo produces exactly the same unhelpful message as a name typo.

---

### C14. Budget alert

The actual guard on "stay cheap" (FR-028).

1. Search **Cost Management** → **Budgets** → **+ Add**.
2. Scope: the subscription. Amount: **$10** — FR-028's ceiling. Reset period: **Monthly**.
3. Add alerts at **80%** and **90%** of actual cost, with your email address.
4. **Create**.

80% of $10 is $8, just under the expected $8.55, so the first alert arrives every month as a
statement that the model still holds. 90% ($9) is the one that means something changed. Product
Owner decision, 2026-09-23.

> A third alert at **100%** is also live, carried over from an earlier $1 budget. It is harmless and
> worth keeping — it is the one that fires when the ceiling itself is breached.

---

### C15. Custom domain (production only)

Do this after the first successful deployment. It needs no redeploy and can wait.

1. Open the production container app → **Settings** → **Custom domains** → **+ Add custom domain**.
2. Enter your chosen subdomain. The portal shows the **TXT** record it wants for ownership, and the
   **CNAME** target.
3. In **Namecheap**: **Domain List** → **Manage** → **Advanced DNS** → **Add New Record**. Add the
   `TXT` record shown, and a `CNAME` for your subdomain pointing at the container app's URL.
   **You are only adding records.** Leave every row you did not create alone — the storefront's own
   records stay exactly as they are.
4. Back in the portal, **Validate**, then add the binding. Azure issues and renews the certificate
   free.

DNS takes a few minutes to tens of minutes to propagate.

---
## Part D — The checks that matter

Run these after both environments exist. They are the requirements made checkable.

**No blanket firewall rule** (FR-020) — run for both environments:

```powershell
az sql server firewall-rule list -g "rg-loot-singles-stage" -s "loot-singles-stage-sql" -o table
az sql server firewall-rule list -g "rg-loot-singles-prod"  -s "loot-singles-prod-sql"  -o table
```

**Expected**: both empty. Any `0.0.0.0` entry is a finding, not a preference. A leftover
`QueryEditorClientIPAddress_…` is the one you are most likely to actually find — the portal adds it
silently every time you open the query editor (C11 step 6), so re-run this check after any visit to
it, not only once at the end.

**Permissions are split** (FR-021) — run C11's role query against both databases (portal query editor). The app identity
must never hold `db_ddladmin` or `db_owner`.

**Restore window** (FR-031):

```powershell
az sql db str-policy show -g "rg-loot-singles-prod" -s "loot-singles-prod-sql" -n "lootsingles" -o json
```

**Expected**: at least 7 days of point-in-time retention.

**Stage and production are protected identically** (FR-029). Stage may hold real customer data, so
its controls are not allowed to be weaker — only its reliability and cost are. Compare:

```powershell
foreach ($e in @("stage","prod")) {
  $g = "rg-loot-singles-$e"
  "--- $e ---"
  az sql server vnet-rule list -g $g -s "loot-singles-$e-sql" --query "[].name" -o tsv
  az sql server firewall-rule list -g $g -s "loot-singles-$e-sql" --query "[].name" -o tsv
  az identity list -g $g --query "[].name" -o tsv
}
```

**Expected**: the same shape on both sides — one virtual network rule, no firewall rules, two
identities. A difference is a finding.

**Logs are searchable, not just live** (FR-027). Sign in to each environment, then:

```powershell
$wid = az monitor log-analytics workspace show -g "rg-loot-singles-shared" -n "log-loot-singles" --query customerId -o tsv
az monitor log-analytics query --workspace $wid `
  --analytics-query "ContainerAppConsoleLogs_CL | summarize count() by ContainerAppName_s" -o table
```

**Expected**: rows for both `ca-loot-singles-stage` and `ca-loot-singles-prod`. One workspace holds
both, because they share one Container Apps environment — `ContainerAppName_s` is what keeps them
apart, so filter on it whenever you read logs. If the live stream shows lines and this does not, the
environment is not attached to the workspace.

---

### Nothing exists that should not — run this last

Provisioning leaves debris. Half-finished attempts, resources created in the wrong group, and
Azure's own auto-created groups all accumulate quietly, and some of them cost money.

```powershell
az group list --query "sort_by([].{name:name, location:location}, &name)" -o table
az resource list --query "sort_by([].{rg:resourceGroup, name:name, type:type}, &rg)" -o table
```

**What belongs:**

| Resource group | Contains | |
|---|---|---|
| `rg-loot-singles-dev` | `loot-singles-dev-sql` and its database | **Never touch this.** It is the local development database and has nothing to do with this feature. |
| `rg-loot-singles-shared` | virtual network, Container Apps environment, Log Analytics workspace | |
| `rg-loot-singles-stage` | two identities, SQL server + free-offer database, container app, migrate job | |
| `rg-loot-singles-prod` | two identities, SQL server + Basic database, container app, migrate job | |
| `ME_cae-loot-singles_…` | `capp-svc-lb`, `capp-svc-lb-ip` | **Azure-managed, do not delete directly.** It holds the environment's load balancer and its public IP — the $3.65/month meter. It appears and disappears with the Container Apps environment. |
| `NetworkWatcherRG` | `NetworkWatcher_<region>` | Azure creates this automatically with any virtual network. Free, and it comes straight back if deleted. Leave it. |

**Anything else is debris.** Check it is genuinely empty before removing it:

```powershell
az resource list -g "<the group>" -o table          # confirm what is inside
az group delete --name "<the group>" --yes --no-wait
```

`DefaultResourceGroup-<REGION>` is a common one — Azure creates it for default monitoring settings
and it is often empty.

**Then confirm the cost picture matches**, which is the point of all this:

```powershell
az consumption budget list --query "[].{name:name, amount:amount, spend:currentSpend.amount}" -o table
```

Expect exactly **one** `Standard IPv4 Static Public IP` meter across the whole subscription. Two
means a second Container Apps environment exists somewhere and the cost model is broken.

---

## Part E — Starting over

If an environment gets into a state you do not understand, delete it and run Part C again. It is
cheap, it is quick, and nothing outside the resource group is affected. **This is the reason
everything for one environment lives in one resource group.**

1. Search **Resource groups** → open `rg-loot-singles-stage`.
2. **Delete resource group** at the top.
3. It asks you to type the group's name to confirm. Read the resource list it shows you first.

This deletes **everything** in that group: the database and its contents, the app, the identities,
the workspace. It does not touch the other environment, `rg-loot-singles-dev`, your GitHub settings,
or your DNS records.

**Three things live outside the resource group** and need removing separately for a completely fresh
start:

- **The app registration** — Microsoft Entra ID → App registrations → `github-loot-singles-stage` →
  **Delete**.
- **The GitHub environment** — the same Settings page that created it.
- **The budget** — Cost Management → Budgets, if you want it gone too.

**Do not do this to production once the shop is using it.** The database goes with it.

---

## Part F — Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `az: command not found` | Your terminal started before the CLI was installed. Open a new PowerShell window (A3). Only Part D needs the CLI. |
| A portal form rejects a name as already taken | Resource names must be unique in their scope; SQL server names are unique across all of Azure. Add a short suffix and use it consistently (Part B). |
| `'query' is misspelled or not recognized` under `az monitor log-analytics` | The extension is missing. `az extension add --name log-analytics` (A3). |
| Looking for `az containerapp job logs` | It does not exist. Job output goes to Log Analytics — see C11, or use the job's **Execution history** blade in the portal. |
| `The subscription is not registered to use namespace…` | A provider is not registered. Register it in Subscriptions → Resource providers (A2). |
| Resources appear in the wrong place | The wrong subscription is selected. Read the subscription shown on every create form (A1). |
| `Database '...' is not currently available. Please retry the connection later` | The stage database is serverless and paused after 60 minutes idle. Your attempt triggered the resume. Wait ~30 seconds and retry. Production is Basic and always awake. |
| No quickstart image option on a Container Apps **Job** | Jobs do not offer one. Use the public sample `mcr.microsoft.com` / `k8se/quickstart-jobs:latest` as a placeholder; the first deployment replaces it (C10). |
| CPU or memory not on the Scale blade | They live with the container: **Containers → Edit and deploy → click the container**. Scale only carries replica counts. Container Apps allows fixed CPU/memory pairs at a 1:2 ratio (0.25 CPU with 0.5 Gi). |
| A resource landed in the wrong resource group | The portal pre-fills Resource group with whichever you used last. A virtual network cannot be moved out afterwards — a Container Apps environment stores its subnet's full resource ID and that ID is immutable — so the fix is deleting and rebuilding the environment, app and job. Check the field on every create form. |
| A browse blade has no **+ Create** button | Use **+ Create a resource** at the top left of the portal home and search the resource type there. The Marketplace route always works; some browse blades do not offer Create. |
| A create form shows a red validation error you do not understand | The **Review + create** tab lists every setting about to be applied; read it there. Portal labels drift, so a field named differently from this runbook is expected — the search box at the top finds any resource type or setting by name. |
| Subnet rejected as too small | `/27` is the minimum for Container Apps. |
| Service endpoint will not attach to the delegated subnet | **Stop and raise it** (C0). The fallback costs money and is a Product Owner decision. |
| `az login` reports `AADSTS50076` / `Status_InteractionRequired`, then "No subscriptions found" | MFA is required for that directory and a silent token refresh cannot satisfy it. `az login --tenant <id from the error>`, then re-check with `az account list` (A2). The enumeration failing is why nothing was listed — it is not proof you have no subscription. |
| `sqlcmd` cannot sign in with `-G` | Either the temporary firewall rule (C11) is missing or your IP changed — re-add your client IP on the SQL server Networking blade — or your account needs MFA and the ODBC 17 `sqlcmd` cannot prompt for it. C11 uses the portal query editor, which avoids this. |
| Migrate job fails on login | C11's grants did not apply to the migrate identity. Re-run the role query. |
| `/health` fine, `/health/database` returns 503 | The **app** identity lacks its roles, or the virtual network rule is missing. Exactly what that endpoint exists to catch. |
| Deploy fails: "no matching federated identity record found" | The federated credential's subject in C13 does not match what GitHub sent. Compare them character by character against C13 step 7. Usual causes: the environment name (`production`, not `prod`), or a mistyped organization/repository **id** — the subject uses GitHub's numeric ids now, and a wrong digit fails identically to a wrong name. |
| Federated credential form rejects the **Issuer** as "not a valid URI" | You are on the **Other issuer** scenario. Set **Federated credential scenario** to **GitHub Actions deploying Azure resources** (C13 step 5); that form fills the issuer itself and has no editable Issuer box. |
| Endless redirects in a browser | Forwarded headers not registered first in `Program.cs` (`research.md` §2). |
| `/api/...` returns HTML | The `/api` fallback is registered after the web-app fallback (`research.md` §3). |
| Everyone signed out after a quiet period | Data Protection keys are still in memory (`research.md` §5). |
| The dashboard tile gallery has no Cost tile | It never will. The gallery holds only generic tiles; everything specific is pinned **from its own blade** in the opposite direction (Part G). |
| Cost analysis is not where the runbook says | Cost Management has been reorganised repeatedly. Search the portal for **"Cost analysis"** directly, or go **Subscriptions → the subscription → Cost Management → Cost analysis**. If you get cards instead of a chart you are in *smart views*; the chart type and Group by live in *customizable views* (G2). |

---

## Part G — An operator dashboard

Optional, free, and useful once an environment is running. Added at the Product Owner's request
(2026-09-23).

**Scope note, so the boundary stays clear**: the specification puts *"alerting, paging or uptime
monitoring of any kind"* out of scope, deferred to its own feature. A read-only dashboard is neither
— it warns nobody and pages nobody, you have to go and look at it. The cost half serves FR-028's $10
ceiling directly. But it sits next to the deferred work, so it is recorded here rather than in the
task list, and it does not replace the budget alert in C14.

### The thing that trips everyone up first

**You do not build this dashboard from the tile gallery.** The gallery you are shown on creating a
dashboard holds only generic tiles — Markdown, Clock, All resources, Service Health, a blank Metrics
chart. There is no Cost tile in it and there never will be.

Everything useful is pinned in the **opposite direction**: you go to the service's own blade,
configure the view you want, and pin *that* onto a dashboard that already exists. So create the
dashboard empty first, close the gallery, and then go collecting.

### G1. Create the empty dashboard

1. Portal search → **Dashboard** → **+ New dashboard** → **Blank dashboard**.
2. Name it `Loot Singles — Stage`.
3. **Close the tile gallery** without adding anything.
4. **Save**.

### G2. Cost, grouped by meter — the most important tile

1. **Search the portal for "Cost analysis" directly.** Do not go via Cost Management and hunt — the
   page has been reorganised more than once and *Cost analysis* is nested under a grouping such as
   *Reporting + analytics* rather than sitting at the top level. If the search does not find it, the
   path that has been stable longest is **Subscriptions** → `Azure subscription 1` → left nav **Cost
   Management** → **Cost analysis**.
2. Set the scope to **Azure subscription 1**.
3. **If you see a page of cards rather than a chart**, you are in *smart views*. The chart-type
   dropdown and **Group by** exist only in **customizable views** — look for a link or button with
   that name, or an **⋯ / Settings** control offering to switch. Then choose the **Daily costs**
   view and **Group by** → **Meter**.
4. **Pin to dashboard** (in the toolbar; on some portal versions under the **…** menu) → pick
   `Loot Singles — Stage`.

**What to expect**: the SQL meter at about $0.16/day once production exists, and **nothing at all
from Container Apps**. A Container Apps meter appearing means the free grant is exhausted.

> **If the view switch defeats you, pin whatever Cost analysis shows and move on.** This tile is a
> convenience. Grouping by meter only makes the signal easier to spot; the signal itself is that
> *any* Container Apps line item exists at all. The thing that actually protects the cost ceiling is
> the budget alert in C14, which emails you — a dashboard does not.

### G3 and G4. SQL free-offer headroom — the only real gauge

1. Open the `lootsingles` database → **Monitoring** → **Metrics**.
2. Metric **`free_amount_remaining`**, aggregation **Min**, time range **This month** → **Pin to
   dashboard**.
3. Repeat with **`free_amount_consumed`**, aggregation **Max**.

These exist only because stage uses the free offer. Production is Basic and has no such metric,
because it has no allowance to run out of.

### G5 to G7. Container app health

Open `ca-loot-singles-stage` → **Monitoring** → **Metrics**, and pin one tile each:

| Metric | Aggregation | Why |
|---|---|---|
| `Replicas` | Avg, last 30 days | The **free-grant proxy** — see the caveat below. Sustained 1 means it is not scaling to zero. |
| `Requests` | Sum | Whether anyone is actually using it. |
| `RestartCount` | Sum | A climbing count means the container is crash-looping — worth seeing before the shop phones. |

### G8. Recent errors

Open `log-loot-singles` → **Logs**, run this, then **Pin to dashboard**:

```kusto
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(24h)
| where Log_s has_any ("Error", "Exception", "Fail")
| project TimeGenerated, ContainerName_s, Log_s
| order by TimeGenerated desc
| take 50
```

If the column names are rejected, run `ContainerAppConsoleLogs_CL | take 5` first and adjust — the
table's shape is Azure's, not ours.

### G9. The resource group itself

Open `rg-loot-singles-stage` → **Overview** → pin, for an at-a-glance inventory of what exists.

Then **Share** the dashboard if you want it on other devices, and **Set as default** to make it your
portal landing page.

### What this dashboard cannot tell you

**There is no metric for the Container Apps free grant.** Azure exposes `free_amount_remaining` for
the SQL free offer but nothing equivalent for the 180,000 vCPU-seconds. Free usage does not appear on
a bill, so **a cost tile reading $0 does not mean you have headroom — it means you have not exceeded
yet.** You find out by a meter appearing, not by a gauge filling.

That is why `Replicas` is on the dashboard: it is 0 or 1, so time-spent-at-1 × 0.25 vCPU is the burn,
against roughly 200 active hours a month across both environments. It is an eyeball estimate, and it
is the best available.

**It also warns nobody.** A dashboard is passive. The budget alert in C14 is the thing that emails
you, and this does not replace it.

---

# Part 3 — What only a real deployment proves

Once per environment, by hand. No test or script substitutes for these.

1. **Sign in on a phone** over the public address, claim an order, record a pick. The first real
   *write* through the application identity (SC-001).
2. **Deploy again** and confirm you are still signed in (SC-005).
3. **Merge a change while a production release waits for approval**, then approve, and confirm
   production runs the commit that was named — not the newer one. This is the single most valuable
   check in the feature and the easiest to skip because the release "obviously" works (SC-004).
4. **Find a log line from days ago** in the workspace, not the live stream (SC-006).
5. **Read a sample of retained records** and confirm no customer names, addresses, PINs or tokens
   appear (FR-022).
