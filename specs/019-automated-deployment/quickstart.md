# Quickstart: Automated Stage and Production Deployment

**Feature**: 019 | **Date**: 2026-09-22

Three parts:

1. **Validate the application changes locally** — repeatable, safe, no Azure account needed.
2. **The Azure runbook** — the one-time setup, written for someone who has never provisioned
   anything on Azure.
3. **What only a real deployment proves** — the handful of checks no script or test can stand in for.

Part 2 is a checklist rather than a script by Product Owner decision (2026-09-22): it is run twice,
ever, and a script written against a subscription nobody can test first is harder to trust than
commands you can read. Nothing in this repository forbids scripting it later.

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

Run this **twice**: once for `stage`, once for `prod`. Roughly 45 minutes per environment the first
time.

All commands are **PowerShell**. Open PowerShell, not Git Bash — the variable syntax below is
PowerShell's, and Azure CLI on Windows is a `.cmd` shim that Git Bash does not always resolve.

## Part A — Before you start (once per machine)

### A1. Open a *new* terminal

Azure CLI was installed after your current terminals were opened. An installer updates the system
PATH, but already-running shells keep the PATH they started with, so `az` will appear "not found"
until you open a fresh window. Close PowerShell and open it again.

```powershell
az version
```

**Expected**: JSON showing `"azure-cli": "2.90.0"` or newer.

**If it fails**: `az` is installed at `C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd`. If a
new terminal still cannot find it, use that full path everywhere below, or add
`C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin` to your PATH and open another new terminal.

### A2. Sign in

```powershell
az login
```

**What happens**: a browser window opens asking you to pick an account. Choose the one that owns the
Azure subscription. The browser will say you can close it; the terminal then prints your
subscriptions as a table.

**If no browser opens** (a remote session, for example): `az login --use-device-code` prints a code
and a URL to enter it on another device.

**If it says authentication failed for a tenant and then "No subscriptions found"**, look for
`AADSTS50076` and `Status_InteractionRequired` in the message. That means sign-in worked but the
CLI could not get a token for that directory without multi-factor authentication — either a
Conditional Access policy or Microsoft's mandatory-MFA requirement for Azure sign-ins. The
enumeration failing is *why* no subscriptions were listed; it does not mean you have none.

Sign in again scoped to the tenant the error names, which forces an interactive MFA prompt:

```powershell
az login --tenant <the tenant id from the error>
```

Then confirm what you actually have:

```powershell
az account list --query "[].{name:name, id:id, state:state}" -o table
```

If *that* is empty, the account genuinely has no Azure subscription and one must be created before
any of Part C can run.

This affects you, not the deployment: GitHub Actions signs in as a workload identity through the
federated credential in C12, which no user MFA policy applies to.

### A3. Pick the right subscription

```powershell
az account show --query "{name:name, id:id, tenant:tenantId}" -o table
```

That is the subscription every command below will use. If it is the wrong one:

```powershell
az account list --query "[].{name:name, id:id}" -o table
az account set --subscription "<the name or id you want>"
```

**Why this matters more than it looks**: every command below silently targets whatever is selected
here. Creating production resources in the wrong subscription is the single most common way this
goes wrong, and it is invisible until the bill arrives.

### A4. Register the resource providers

Azure requires each service to be switched on for your subscription before you can use it. A brand
new subscription usually has none of these registered, and the error you get without them
("The subscription is not registered to use namespace…") does not obviously tell you to do this.

```powershell
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights
az provider register --namespace Microsoft.Sql
az provider register --namespace Microsoft.Network
az provider register --namespace Microsoft.ManagedIdentity
```

Registration takes a few minutes and runs in the background. Check it:

```powershell
az provider show --namespace Microsoft.App --query registrationState -o tsv
```

**Expected**: `Registered`. If it says `Registering`, wait a minute and run it again. Do not continue
until all five say `Registered`.

### A5. Add the Log Analytics extension

Azure CLI ships a core set of commands and adds the rest as extensions. Searching stored logs is one
of the extras, and you need it in C9 and in Part D. Installing it now avoids being prompted
mid-troubleshooting.

```powershell
az extension add --name log-analytics
az extension list --query "[].name" -o tsv
```

**Expected**: `log-analytics` appears in the list.

### A6. Get your own Entra object ID

Step C4 makes *you* the SQL server's administrator, which needs your object ID — a GUID identifying
your account, not your email address.

```powershell
az ad signed-in-user show --query "{name:displayName, objectId:id}" -o table
```

Write both values down. You need them in C4.

---

## Part B — Decide every name before you start

Fill this in for the environment you are building. The commands in Part C use these variables, so
getting them right once means never typing a name again.

| What | `stage` | `prod` |
|---|---|---|
| Resource group | `rg-loot-singles-stage` | `rg-loot-singles-prod` |
| Virtual network | `vnet-loot-singles-stage` | `vnet-loot-singles-prod` |
| Address space | `10.20.0.0/16` | `10.30.0.0/16` |
| Subnet | `snet-apps` (`10.20.0.0/27`) | `snet-apps` (`10.30.0.0/27`) |
| App identity | `id-loot-singles-stage-app` | `id-loot-singles-prod-app` |
| Migrate identity | `id-loot-singles-stage-migrate` | `id-loot-singles-prod-migrate` |
| SQL server | `sql-loot-singles-stage-<suffix>` | `sql-loot-singles-prod-<suffix>` |
| Database | `lootsingles` | `lootsingles` |
| Log Analytics | `log-loot-singles-stage` | `log-loot-singles-prod` |
| Container Apps env | `cae-loot-singles-stage` | `cae-loot-singles-prod` |
| Container App | `ca-loot-singles-stage` | `ca-loot-singles-prod` |
| Migrate job | `caj-loot-singles-stage-migrate` | `caj-loot-singles-prod-migrate` |

**`<suffix>` is not optional.** SQL server names must be unique across *all of Azure*, not just your
subscription, so `sql-loot-singles-prod` is almost certainly taken. Add something of your own — your
initials plus a number, for example `sql-loot-singles-prod-jh01`.

**Pick a region** close to the shop and use it everywhere. `eastus2` is used below; replace it if you
choose another. Not every service is in every region, so if a later command complains about the
location, that is why.

> **The region you use for stage's free database is permanent.** Azure applies the region of the
> *first* free-offer database to every free database in the subscription, and it cannot be changed
> afterwards. Choose deliberately.
>
> Region does **not** affect price: SQL Basic is $0.161/day in eastus2, westus2, westus3 and
> centralus alike. Pick for latency to the shop, not for cost.

### The variables block

Paste this at the start of each environment's run, editing the first three lines. If you close the
terminal, paste it again — variables do not survive.

```powershell
$ENVNAME = "stage"                      
$LOCATION = "eastus2"
$SQLSUFFIX = "jh01"                     

$RG      = "rg-loot-singles-$ENVNAME"
$VNET    = "vnet-loot-singles-$ENVNAME"
$SUBNET  = "snet-apps"
$VNETCIDR   = if ($ENVNAME -eq "prod") { "10.30.0.0/16" } else { "10.20.0.0/16" }
$SUBNETCIDR = if ($ENVNAME -eq "prod") { "10.30.0.0/27" } else { "10.20.0.0/27" }
$IDAPP   = "id-loot-singles-$ENVNAME-app"
$IDMIG   = "id-loot-singles-$ENVNAME-migrate"
$SQLSRV  = "sql-loot-singles-$ENVNAME-$SQLSUFFIX"
$SQLDB   = "lootsingles"
$LAW     = "log-loot-singles-$ENVNAME"
$CAE     = "cae-loot-singles-$ENVNAME"
$CAAPP   = "ca-loot-singles-$ENVNAME"
$CAJOB   = "caj-loot-singles-$ENVNAME-migrate"
$IMAGE   = "ghcr.io/jkhaynes/loot-singles-fulfillment:sha-<commit>"
```

Check it took:

```powershell
"$RG / $SQLSRV / $CAE"
```

---

## Part C — The steps

Do these in order. Later steps depend on earlier ones.

> **Before C1 and everything after it, paste Part B's variables block into your terminal.** Every
> step from C1 on uses those variables, and PowerShell forgets them when the window closes — so
> paste the block again at the start of each session, and again when you switch from `stage` to
> `prod`. A command reporting `expected one argument` means a variable is empty, not that the
> command is wrong. C0 below is the exception: it is deliberately self-contained.

### C0. Verify the one thing that can stop this feature

**Do this before creating anything you intend to keep.** The design assumes a subnet delegated to
Container Apps will accept a SQL service endpoint. If it will not, the alternative costs about
$7–8/month and is a decision for the Product Owner, not a workaround to improvise.

> **This step is self-contained on purpose** — it is the first thing anyone runs, and feature 019's
> task T015 points straight at it. You need only A1–A4 done (a new terminal, `az login`, the right
> subscription, the providers registered) plus the one variable below. Part B's full variables block
> is not needed until C1.
>
> If any command below reports `expected one argument`, a variable is empty: PowerShell variables do
> not survive closing the terminal, so set it again.

```powershell
# The only variable C0 needs. Use the region you intend for the real environments.
$LOCATION = "eastus2"

az group create --name "rg-spike-delete-me" --location $LOCATION
az network vnet create --resource-group "rg-spike-delete-me" --name "vnet-spike" `
  --address-prefix "10.99.0.0/16" --subnet-name "snet-spike" --subnet-prefix "10.99.0.0/27"
az network vnet subnet update --resource-group "rg-spike-delete-me" --vnet-name "vnet-spike" `
  --name "snet-spike" --delegations "Microsoft.App/environments" --service-endpoints "Microsoft.Sql"
```

Then read back what actually stuck:

```powershell
az network vnet subnet show --resource-group "rg-spike-delete-me" --vnet-name "vnet-spike" `
  --name "snet-spike" --query "{delegations:delegations[].serviceName, endpoints:serviceEndpoints[].service}" -o json
```

**Expected**: `["Microsoft.App/environments"]` and `["Microsoft.Sql"]` — both present.

**If the service endpoint is missing or the command errored**: **stop and raise it.** Do not continue
and do not improvise. Record what the command said.

Clean up either way — this costs nothing but leaves clutter:

```powershell
az group delete --name "rg-spike-delete-me" --yes --no-wait
```

While you are here, confirm the other figures the design assumes: the Azure SQL Basic price in your
region, the Container Apps free grant, and the Log Analytics free ingestion allowance. The portal's
pricing pages are the authority; the numbers in `research.md` came from documentation and have never
been measured.

### C1. Resource group

Everything for one environment lives in one resource group, which means deleting the group deletes
the whole environment — that is what makes Part E safe.

```powershell
az group create --name $RG --location $LOCATION
```

**Expected**: JSON ending `"provisioningState": "Succeeded"`.

**Check**: `az group show --name $RG --query name -o tsv` prints the name.

### C2. Virtual network and subnet

A private network for this environment. The Container Apps environment will live inside the subnet,
which is what later lets SQL trust it by name instead of by IP address — and IP addresses here are
documented as changing without warning.

`--delegations` hands the subnet to Container Apps. `--service-endpoints` is what makes SQL able to
recognise traffic from it.

```powershell
az network vnet create --resource-group $RG --name $VNET --location $LOCATION `
  --address-prefix $VNETCIDR --subnet-name $SUBNET --subnet-prefix $SUBNETCIDR

az network vnet subnet update --resource-group $RG --vnet-name $VNET --name $SUBNET `
  --delegations "Microsoft.App/environments" --service-endpoints "Microsoft.Sql"
```

**Check** — both must be present, exactly as in C0:

```powershell
az network vnet subnet show --resource-group $RG --vnet-name $VNET --name $SUBNET `
  --query "{delegations:delegations[].serviceName, endpoints:serviceEndpoints[].service}" -o json
```

Capture the subnet's resource ID; C6 and C7 both need it:

```powershell
$SUBNETID = az network vnet subnet show --resource-group $RG --vnet-name $VNET --name $SUBNET --query id -o tsv
$SUBNETID
```

**If it fails**: a `/27` is the smallest subnet Container Apps accepts. Anything smaller is rejected.

### C3. The two identities

A managed identity is an account Azure manages for you — the application proves who it is without a
password existing anywhere. Two are created, because the application and the migration job are
allowed to do different things (`research.md` §7):

- **app** — reads and writes rows. This is the internet-facing component.
- **migrate** — also changes database structure. Runs only during a deployment.

Keeping structural permission away from the internet-facing component is what stops a compromised
application dropping the index that enforces one claim per employee, or wiping the packing-slip
access log.

```powershell
az identity create --resource-group $RG --name $IDAPP --location $LOCATION
az identity create --resource-group $RG --name $IDMIG --location $LOCATION
```

Capture what later steps need:

```powershell
$IDAPP_ID     = az identity show -g $RG -n $IDAPP --query id -o tsv
$IDAPP_CLIENT = az identity show -g $RG -n $IDAPP --query clientId -o tsv
$IDMIG_ID     = az identity show -g $RG -n $IDMIG --query id -o tsv
$IDMIG_CLIENT = az identity show -g $RG -n $IDMIG --query clientId -o tsv
"app client:     $IDAPP_CLIENT"
"migrate client: $IDMIG_CLIENT"
```

Both client IDs must be GUIDs. They go into the connection strings in C7.

### C4. SQL server

`--enable-ad-only-auth` is the important part: the server is created with **no password at all**.
Only Microsoft Entra identities can connect, so there is no SQL admin password to store, rotate or
accidentally commit.

Use the name and object ID from A6:

```powershell
az sql server create --resource-group $RG --name $SQLSRV --location $LOCATION `
  --enable-ad-only-auth --external-admin-principal-type User `
  --external-admin-name "<your display name from A6>" `
  --external-admin-sid "<your object ID from A6>"
```

**Expected**: JSON with `"state": "Ready"`.

**If it fails with a name error**: the name is taken by someone else in the world. Change
`$SQLSUFFIX`, re-run the variables block, and try again.

### C5. Database, network rule, and the firewall check

Basic tier for production; stage can use the free offer if you prefer, accepting that it pauses when
its monthly allowance runs out.

```powershell
az sql db create --resource-group $RG --server $SQLSRV --name $SQLDB --edition Basic
```

Now let the subnet through — and *only* the subnet:

```powershell
az sql server vnet-rule create --resource-group $RG --server $SQLSRV `
  --name "allow-$SUBNET" --subnet $SUBNETID
```

**Check there is no blanket allow rule.** A rule of `0.0.0.0` is labelled "Allow Azure services" and
sounds harmless; it actually admits every other Azure customer's resources to your database. FR-020
forbids it.

```powershell
az sql server firewall-rule list --resource-group $RG --server $SQLSRV -o table
```

**Expected**: empty, or nothing with start address `0.0.0.0`. If one exists, delete it:

```powershell
az sql server firewall-rule delete --resource-group $RG --server $SQLSRV --name "<the rule name>"
```

**Check the restore window** (FR-031 requires at least 7 days):

```powershell
az sql db show --resource-group $RG --server $SQLSRV --name $SQLDB --query earliestRestoreDate -o tsv
az sql db str-policy show --resource-group $RG --server $SQLSRV --name $SQLDB -o json
```

A brand new database has no restore history yet, so `earliestRestoreDate` may be empty — the policy
output is the one to read. Basic tier includes 7 days. If it shows fewer, stop and raise it.

### C6. Log Analytics and the Container Apps environment

The workspace is where the application's log output is kept so it can be searched later. Without it
you get a live stream only, and evidence is gone before anyone looks.

```powershell
az monitor log-analytics workspace create --resource-group $RG --workspace-name $LAW --location $LOCATION
```

The environment creation needs the workspace's ID and key:

```powershell
$LAW_ID  = az monitor log-analytics workspace show -g $RG -n $LAW --query customerId -o tsv
$LAW_KEY = az monitor log-analytics workspace get-shared-keys -g $RG -n $LAW --query primarySharedKey -o tsv
```

> `$LAW_KEY` is a credential. Azure stores it on the environment for you. Do not paste it into a
> file, a commit, or a chat window. It exists only in this terminal session.

Now the environment — the shared space the container runs in, placed inside your subnet:

```powershell
az containerapp env create --resource-group $RG --name $CAE --location $LOCATION `
  --enable-workload-profiles `
  --infrastructure-subnet-resource-id $SUBNETID `
  --logs-destination log-analytics `
  --logs-workspace-id $LAW_ID --logs-workspace-key $LAW_KEY
```

This one takes several minutes. **Expected**: `"provisioningState": "Succeeded"`.

**Check**: `az containerapp env show -g $RG -n $CAE --query "{state:properties.provisioningState}" -o tsv`

> **Come back to this about 24 hours after creating the *first* environment**, and confirm what is
> actually being billed. This is the one cost question documentation cannot settle: the billing
> guide states that private endpoints and planned maintenance incur a $0.10/hour Dedicated Plan
> Management charge — neither of which this design uses — but it also warns that "if you use
> Container Apps with your own virtual network… additional charges might apply", and this design
> does use its own virtual network.
>
> In the portal: **Cost Management** → **Cost analysis**, scope to the subscription, group by
> **Meter**.
>
> **Expected**: nothing from Container Apps at all while usage stays inside the free grant, and the
> SQL database meter at about $0.16/day.
>
> **If you see an "Environment Management Hour" or "Dedicated Plan Management" meter accruing**,
> that is roughly $73/month per environment and it breaks the cost model (FR-028). Stop and raise
> it before creating the second environment — the fix would be a design change, not a setting.



### C7. The container app and the migrate job

Both run the same image; the job runs it with a `migrate` argument. Each uses its own identity.

Build the two connection strings. They differ **only** in which identity they name:

```powershell
$CONN_APP = "Server=tcp:$SQLSRV.database.windows.net,1433;Database=$SQLDB;Authentication=Active Directory Managed Identity;User Id=$IDAPP_CLIENT;Encrypt=True;"
$CONN_MIG = "Server=tcp:$SQLSRV.database.windows.net,1433;Database=$SQLDB;Authentication=Active Directory Managed Identity;User Id=$IDMIG_CLIENT;Encrypt=True;"
```

Neither contains a password — that is the point of C4.

```powershell
az containerapp create --resource-group $RG --name $CAAPP --environment $CAE `
  --image $IMAGE --target-port 8080 --ingress external `
  --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi `
  --user-assigned $IDAPP_ID `
  --env-vars "ConnectionStrings__LootSingles=$CONN_APP"

az containerapp job create --resource-group $RG --name $CAJOB --environment $CAE `
  --image $IMAGE --trigger-type Manual --replica-timeout 600 `
  --mi-user-assigned $IDMIG_ID `
  --args "migrate" `
  --env-vars "ConnectionStrings__LootSingles=$CONN_MIG"
```

Get the public address:

```powershell
$APPURL = az containerapp show -g $RG -n $CAAPP --query properties.configuration.ingress.fqdn -o tsv
"https://$APPURL"
```

**Expect it to fail for now.** The identities have no database permission yet — that is C8. Opening
the URL should reach the application and error on anything touching data. `https://$APPURL/health`
should already return 200, because it deliberately never touches the database.

### C8. Database users and permissions

The fiddliest step, and the one where care matters most.

Azure CLI cannot grant database roles — that is SQL, not Azure. You connect with `sqlcmd` as
yourself (you are the server administrator from C4) and create a database user for each identity.

**First, let your own machine through, temporarily.** Your laptop is not in the subnet, so the rule
from C5 does not cover it.

```powershell
$MYIP = (Invoke-RestMethod "https://api.ipify.org?format=json").ip
az sql server firewall-rule create --resource-group $RG --server $SQLSRV `
  --name "temp-setup-access" --start-ip-address $MYIP --end-ip-address $MYIP
```

> **Remember this rule exists.** You remove it at the end of this step. Leaving it behind means your
> home IP address keeps standing access to a database holding customer addresses.

Run the grants. `-G` means "sign in with Entra"; a browser prompt may appear.

> **If your account requires MFA** (see A2), the `sqlcmd` shipped with the ODBC 17 tools may not be
> able to complete an interactive multi-factor prompt. Two fallbacks, either is fine:
>
> - **The portal's query editor** — open the database in the Azure portal, choose *Query editor*,
>   sign in with Entra, and run the same SQL. The temporary firewall rule above is still required.
> - **`go-sqlcmd`** (`winget install sqlcmd`), which supports interactive MFA. Its flags for this
>   are the same: `sqlcmd -S <server> -d <db> -G -Q "<sql>"`.
>
> The SQL itself is identical whichever route you take.

```powershell
sqlcmd -S "$SQLSRV.database.windows.net" -d $SQLDB -G -Q @"
CREATE USER [$IDAPP] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$IDAPP];
ALTER ROLE db_datawriter ADD MEMBER [$IDAPP];

CREATE USER [$IDMIG] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$IDMIG];
ALTER ROLE db_datawriter ADD MEMBER [$IDMIG];
ALTER ROLE db_ddladmin  ADD MEMBER [$IDMIG];
"@
```

The user names are the *identity resource names* from C3, not the client IDs.

**Check who has what** — and confirm neither is `db_owner`:

```powershell
sqlcmd -S "$SQLSRV.database.windows.net" -d $SQLDB -G -Q @"
SELECT p.name AS member, r.name AS role
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals p ON p.principal_id = m.member_principal_id
WHERE p.name LIKE 'id-loot-singles%' ORDER BY p.name, r.name;
"@
```

**Expected**: the app identity in `db_datareader` and `db_datawriter` **only**; the migrate identity
in those two plus `db_ddladmin`. **Neither in `db_owner`.** If the app identity has `db_ddladmin`,
fix it before going further — that is the control FR-021 requires:

```powershell
# only if needed
sqlcmd -S "$SQLSRV.database.windows.net" -d $SQLDB -G -Q "ALTER ROLE db_ddladmin DROP MEMBER [$IDAPP];"
```

**Now remove the temporary rule. Do not skip this.**

```powershell
az sql server firewall-rule delete --resource-group $RG --server $SQLSRV --name "temp-setup-access"
az sql server firewall-rule list --resource-group $RG --server $SQLSRV -o table   # must be empty
```

### C9. Create the schema

Run the migrate job once. This is the first proof that the migrate identity, the network rule and
the migrations all work together.

```powershell
az containerapp job start --resource-group $RG --name $CAJOB
az containerapp job execution list --resource-group $RG --name $CAJOB `
  --query "[0].{name:name, status:properties.status}" -o table
```

**Expected**: status `Succeeded` after a minute or so. Re-run the list command until it settles.

**If it failed**, read why. There is no `az containerapp job logs` command — a job's output goes to
the Log Analytics workspace, so you query it there (this is why A5 installed the extension):

```powershell
$LAW_CID = az monitor log-analytics workspace show -g $RG -n $LAW --query customerId -o tsv
az monitor log-analytics query --workspace $LAW_CID `
  --analytics-query "ContainerAppConsoleLogs_CL | where ContainerGroupName_s startswith '$CAJOB' | project TimeGenerated, Log_s | order by TimeGenerated desc | take 50" `
  -o table
```

Two things to expect. **Logs take a few minutes to arrive** — an empty result straight after the run
usually means "not yet", not "nothing was logged". And if the query returns an error about an unknown
column, list what the table actually has and adjust the `where` clause:

```powershell
az monitor log-analytics query --workspace $LAW_CID `
  --analytics-query "ContainerAppConsoleLogs_CL | take 5" -o json
```

The portal is often quicker here: the job's **Execution history** blade shows each run's output
without any query.

A login failure means C8's grants did not apply to the *migrate* identity. A network failure means
the C5 virtual network rule did not take.

**This proves nothing about the app identity.** `/health/database` is what proves that:

```powershell
curl.exe -i "https://$APPURL/health/database"
```

**Expected**: `200`. A `503` means the app identity specifically cannot reach the database — go back
to C8 and check its roles. This is exactly the failure that endpoint exists to catch.

### C10. Create the first account

A fresh environment has no users, so nobody can sign in. This creates one manager account, then
deletes the job that knows the PIN.

```powershell
az containerapp job create --resource-group $RG --name "caj-bootstrap-temp" --environment $CAE `
  --image $IMAGE --trigger-type Manual --replica-timeout 600 `
  --mi-user-assigned $IDMIG_ID `
  --args "bootstrap-admin" `
  --env-vars "ConnectionStrings__LootSingles=$CONN_MIG" "BOOTSTRAP_PIN=<a throwaway PIN>"

az containerapp job start --resource-group $RG --name "caj-bootstrap-temp"
```

Once it succeeds, delete it — this is what removes the PIN from Azure:

```powershell
az containerapp job delete --resource-group $RG --name "caj-bootstrap-temp" --yes
```

**Then sign in and change that PIN immediately.** Until you do, a throwaway value is a real
credential, and it was visible in your shell history.

### C11. GitHub — use the web interface

Azure steps are CLI because the commands are copy-pasteable. GitHub environment settings are the
opposite: doing this with `gh api` means hand-writing JSON for reviewers and branch policies, which
is more error-prone than clicking.

In your browser, go to the repository → **Settings** → **Environments** → **New environment**.

**Create `stage`:**
1. Name it `stage`, click **Configure environment**.
2. Under **Deployment branches and tags**, choose **Selected branches and tags**, add a rule for
   `main`. *(Why: production trusts that anything on stage came from `main`.)*
3. Leave **Required reviewers** unchecked — stage deploys unattended.
4. Under **Environment variables**, add one per row: `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`,
   `AZURE_CLIENT_ID`, `RESOURCE_GROUP`, `CONTAINER_APP`, `MIGRATE_JOB`, `APP_URL`. Values come from
   Part B and A3. **These are variables, not secrets** — none of them is a credential.

**Create `production`** the same way, with two differences:
- Tick **Required reviewers** and add yourself.
- **Leave "Prevent self-review" unchecked.** Product Owner decision, 2026-09-22: with two staff, a
  mandatory second approver makes production unfixable whenever one of you is away. The gate is a
  deliberate pause on a named version, not a two-person control.

### C12. Let GitHub sign in to Azure without a password

The least intuitive step in the setup, so here is what it actually does.

GitHub Actions needs to run `az` commands against your subscription. The old way was storing a
credential as a GitHub secret. Instead, you register a trust: *"when GitHub says a workflow is
running in this repository, in this environment, accept that as proof of identity."* No password
exists, so none can leak.

Create an app registration and give it access to **this environment's resource group only** — so
stage's credential cannot touch production:

```powershell
$APPREG = az ad app create --display-name "github-loot-singles-$ENVNAME" --query appId -o tsv
$SPID   = az ad sp create --id $APPREG --query id -o tsv
$SUBID  = az account show --query id -o tsv

az role assignment create --assignee $APPREG --role "Contributor" `
  --scope "/subscriptions/$SUBID/resourceGroups/$RG"
```

Now the trust itself. `subject` must match **exactly** what GitHub sends — the environment name here
must be the one from C11 (`stage` or `production`, which is not the same string as `prod`):

```powershell
$GHENV = if ($ENVNAME -eq "prod") { "production" } else { "stage" }

az ad app federated-credential create --id $APPREG --parameters (@{
  name = "github-$GHENV"
  issuer = "https://token.actions.githubusercontent.com"
  subject = "repo:jkhaynes/loot-singles-fulfillment:environment:$GHENV"
  audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Compress)
```

The value GitHub needs as `AZURE_CLIENT_ID` in C11:

```powershell
"AZURE_CLIENT_ID for $GHENV : $APPREG"
```

**If a deployment later fails with "no matching federated identity record found"**, the `subject`
string does not match. Check the environment name and the `owner/repo` spelling — those are the two
things that are usually wrong.

### C13. Custom domain (production only)

Do this after the first successful deployment. It needs no redeploy and can wait.

```powershell
az containerapp hostname add --resource-group $RG --name $CAAPP --hostname "<your.subdomain.com>"
```

That command tells you which `TXT` record Azure wants in order to prove you own the name. In
Namecheap: **Domain List** → **Manage** → **Advanced DNS** → **Add New Record**. Add the `TXT` record
it asked for, and a `CNAME` for your chosen subdomain pointing at the value of `$APPURL`.

**You are only adding records, not changing existing ones.** The storefront's own records stay
exactly as they are — leave every row you did not create alone.

DNS takes a few minutes to tens of minutes to propagate. Then bind the certificate:

```powershell
az containerapp hostname bind --resource-group $RG --name $CAAPP --hostname "<your.subdomain.com>" `
  --environment $CAE --validation-method CNAME
```

Azure issues and renews the certificate free.

---

## Part D — The checks that matter

Run these after both environments exist. They are the requirements made checkable.

**No blanket firewall rule** (FR-020) — run for both environments:

```powershell
az sql server firewall-rule list -g "rg-loot-singles-stage" -s "<stage server>" -o table
az sql server firewall-rule list -g "rg-loot-singles-prod"  -s "<prod server>"  -o table
```

**Expected**: both empty. Any `0.0.0.0` entry is a finding, not a preference.

**Permissions are split** (FR-021) — run C8's role query against both databases. The app identity
must never hold `db_ddladmin` or `db_owner`.

**Restore window** (FR-031):

```powershell
az sql db str-policy show -g "rg-loot-singles-prod" -s "<prod server>" -n "lootsingles" -o json
```

**Expected**: at least 7 days of point-in-time retention.

**Stage and production are protected identically** (FR-029). Stage may hold real customer data, so
its controls are not allowed to be weaker — only its reliability and cost are. Compare:

```powershell
foreach ($e in @("stage","prod")) {
  $g = "rg-loot-singles-$e"
  "--- $e ---"
  az sql server vnet-rule list -g $g -s "<$e server>" --query "[].name" -o tsv
  az sql server firewall-rule list -g $g -s "<$e server>" --query "[].name" -o tsv
  az identity list -g $g --query "[].name" -o tsv
}
```

**Expected**: the same shape on both sides — one virtual network rule, no firewall rules, two
identities. A difference is a finding.

**Logs are searchable, not just live** (FR-027). Sign in to each environment, then:

```powershell
$wid = az monitor log-analytics workspace show -g "rg-loot-singles-prod" -n "log-loot-singles-prod" --query customerId -o tsv
az monitor log-analytics query --workspace $wid `
  --analytics-query "ContainerAppConsoleLogs_CL | take 20" -o table
```

**Expected**: rows, including your application's own log lines. If the live stream shows lines and
this does not, the environment is not attached to the workspace.

---

## Part E — Starting over

If an environment gets into a state you do not understand, delete it and run Part C again. It is
cheap, it is quick, and nothing outside the resource group is affected.

```powershell
az group delete --name $RG --yes --no-wait
```

This deletes **everything** in that group: the database and its contents, the app, the identities,
the workspace. It does not touch the other environment, your GitHub settings, or your DNS records.

Two things survive a group delete and need removing separately if you are starting completely fresh:

```powershell
az ad app list --display-name "github-loot-singles-$ENVNAME" --query "[].{name:displayName,id:appId}" -o table
az ad app delete --id "<the appId>"
```

...and the GitHub environment, deleted from the same Settings page that created it.

**Do not do this to production once the shop is using it.** The database goes with it.

---

## Part F — Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `az: command not found` | Your terminal started before the CLI was installed. Open a new one (A1). |
| `argument --location/-l: expected one argument` (or any other `expected one argument`) | A PowerShell variable is empty. They do not survive closing the terminal — paste Part B's variables block again. For C0, set `$LOCATION` alone. Check with `"$RG / $SQLSRV / $LOCATION"`. |
| `'query' is misspelled or not recognized` under `az monitor log-analytics` | The extension is missing. `az extension add --name log-analytics` (A5). |
| Looking for `az containerapp job logs` | It does not exist. Job output goes to Log Analytics — see C9, or use the job's **Execution history** blade in the portal. |
| `The subscription is not registered to use namespace…` | A provider is not registered. Re-run A4 and wait for `Registered`. |
| Resources appear in the wrong place | The wrong subscription is selected. Check A3 before every session. |
| `Specified server name is already used` | SQL server names are globally unique. Change `$SQLSUFFIX` and re-run the variables block. |
| Subnet rejected as too small | `/27` is the minimum for Container Apps. |
| Service endpoint will not attach to the delegated subnet | **Stop and raise it** (C0). The fallback costs money and is a Product Owner decision. |
| `az login` reports `AADSTS50076` / `Status_InteractionRequired`, then "No subscriptions found" | MFA is required for that directory and a silent token refresh cannot satisfy it. `az login --tenant <id from the error>`, then re-check with `az account list` (A2). The enumeration failing is why nothing was listed — it is not proof you have no subscription. |
| `sqlcmd` cannot sign in with `-G` | Either the temporary firewall rule (C8) is missing or your IP changed — re-run the `$MYIP` step — or your account needs MFA and the ODBC 17 `sqlcmd` cannot prompt for it. See the fallbacks in C8. |
| Migrate job fails on login | C8's grants did not apply to the migrate identity. Re-run the role query. |
| `/health` fine, `/health/database` returns 503 | The **app** identity lacks its roles, or the virtual network rule is missing. Exactly what that endpoint exists to catch. |
| Deploy fails: "no matching federated identity record found" | The `subject` in C12 does not match. Check the environment name (`production`, not `prod`) and the `owner/repo` spelling. |
| Endless redirects in a browser | Forwarded headers not registered first in `Program.cs` (`research.md` §2). |
| `/api/...` returns HTML | The `/api` fallback is registered after the web-app fallback (`research.md` §3). |
| Everyone signed out after a quiet period | Data Protection keys are still in memory (`research.md` §5). |

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
