# Quickstart: Validate SQL Server-Only Persistence

No Azure resources are created by this feature. Azure validation applies only after separate provisioning approval.

## Prerequisites

- .NET SDK 10.0.x
- Docker-compatible runtime able to run `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`
- For later Azure validation: authorized `loot-singles-dev` access, outbound TCP 1433, and an allowed network path

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

1. Confirm current free-offer enrollment and **Auto-pause the database until next month**.
2. Confirm default-deny networking with only the authorized client IP or scoped private/VNet path.
3. Prefer Entra/passwordless access. Supply `ConnectionStrings:LootSingles` through Secret Manager locally or approved hosted configuration/managed identity; do not edit tracked settings with a secret.
4. Apply migrations using the externally supplied configuration.
5. Start the normal API/frontend and test login, employee administration, dashboard, PDF import, and order browsing.
6. Remove temporary firewall access and disconnect query tools afterward.

Paused allowance, firewall denial, or invalid identity must fail clearly without fallback.

References: [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), [network controls](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql), [development secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).
