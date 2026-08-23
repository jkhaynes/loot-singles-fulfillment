# Quickstart: Validate SQL Server-Only Persistence

No Azure resources are created by this feature. Azure validation applies only after separate provisioning approval.

## Prerequisites

- .NET SDK 10.0.x
- Docker-compatible runtime able to run `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`
- For later Azure validation: authorized `loot-singles-dev` access, outbound TCP 1433, and an allowed network path

Verify the local container prerequisite and pinned image before running database-backed tests:

```powershell
docker version
docker manifest inspect mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04
```

Both commands must succeed. If `docker` is not found, install and start a Docker-compatible runtime before Phase 2; do not substitute another database provider.

## Disposable SQL Server validation

```powershell
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
```

Expected: one disposable server starts; test scopes create unique databases and apply all migrations; migration, translation, offset, constraint, transaction, index, atomicity, isolation, and concurrency tests pass; resources clean up; Azure is not contacted.

If startup fails, verify the container runtime and pinned image. Never substitute SQLite or Azure.

## Full validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
npm.cmd --prefix frontend test -- --run
npm.cmd --prefix frontend run build
npm.cmd --prefix frontend run lint
npm.cmd --prefix frontend run format:check
npm.cmd --prefix frontend run test:e2e
```

Implementation must correct the solution command if repository inspection finds a different solution path.

## Query execution

Integration tests prove SQL Server orders before materialization for order list (newest first), Ready dashboard (oldest first), and audit history (newest first), each with deterministic tie-breakers.

## Removal audit

```powershell
rg -n -i --hidden --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/node_modules/**' "Microsoft\.EntityFrameworkCore\.Sqlite|Microsoft\.Data\.Sqlite|UseSqlite|SqliteConnection|SQLite|sqlite|\.db\b" .
```

Expected: no active dependency, configuration, compatibility branch, test path, workaround, or current-use guidance remains. Classify every textual match; only this feature's removal history and regression-audit definitions may remain.

## Later `loot-singles-dev` smoke test

After separate approval:

1. Reverify offer eligibility in the intended subscription/region. As of 2026-08-22 the documented monthly allowance is 100,000 serverless vCore seconds, 32 GB data, and 32 GB backup per eligible database. Confirm **Auto-pause the database until next month** and that continued paid usage is disabled.
2. Monitor the portal's free-amount remaining/consumed metrics. Treat allowance exhaustion and month-long auto-pause as expected development unavailability.
3. Confirm default-deny networking with only the authorized current client IP or scoped private/VNet path; broad Azure-services access is not a default. Confirm outbound TCP 1433.
4. Prefer developer Entra identity locally and managed identity when hosted. Supply `ConnectionStrings:LootSingles` through Secret Manager locally or approved hosted configuration; do not edit tracked settings with a secret.
5. Apply migrations using the externally supplied configuration.
6. If the employee table is empty, temporarily set `BootstrapAdmin:Username`,
   `BootstrapAdmin:DisplayName`, and `BootstrapAdmin:Pin` with Secret Manager, run
   `dotnet run --project backend/src/LootSingles.Api -- bootstrap-admin`, and immediately remove all
   three bootstrap values. Expect a successful first creation or a safe refusal when any employee
   already exists; the command never starts the web host.
7. Start the normal API/frontend and test login, authenticated employee administration, dashboard,
   PDF import, and order browsing.
8. Remove temporary firewall access and disconnect query tools afterward.

Paused allowance, firewall denial, or invalid identity must fail clearly without fallback.

References: [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), [free-offer FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql), [network controls](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql), [Microsoft Entra authentication](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview?view=azuresql), [development secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).
