# Research: SQL Server-Only Persistence

## Azure SQL free offer

**Decision**: Plan `loot-singles-dev` under the current free offer, subject to revalidation before separately approved provisioning, and choose **Auto-pause the database until next month** at allowance exhaustion.

**Rationale**: Microsoft currently documents up to 10 free databases per subscription, each with 100,000 vCore seconds, 32 GB data, and 32 GB backup monthly, with no time limit. Pause-on-exhaustion prevents billed overage. The no-SLA development/POC positioning fits development, not production.

**Alternatives considered**: Paid overage (cost risk); SQLite/LocalDB development (provider mismatch); provisioning now (out of scope).

**Sources**: [free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), [FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql)

## Azure networking

**Decision**: Keep default deny. Initially permit only explicit developer IPs; for Azure hosting prefer private endpoint or narrow VNet access. Do not enable broad “Allow Azure services” by default.

**Rationale**: Azure SQL denies connections by default and supports IP firewall, VNet, and Private Link controls. Client networks must allow port 1433.

**Alternatives considered**: Broad Azure access and unfiltered public access (unnecessarily permissive); networking in application code (wrong boundary).

**Sources**: [network controls](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql), [security tutorial](https://learn.microsoft.com/en-us/azure/azure-sql/database/secure-database-tutorial?view=azuresql)

## Secrets and authentication

**Decision**: Retain `ConnectionStrings:LootSingles`, supplied outside tracked files. Prefer Entra `Active Directory Default` for developers and managed identity for hosted Azure. If credentials are temporarily necessary, use local Secret Manager and an approved hosted secret facility.

**Rationale**: ASP.NET configuration already provides the boundary. Microsoft describes user secrets as development-only and recommends passwordless/managed identity for Azure.

**Alternatives considered**: Tracked connection string; preferred SQL password; custom configuration abstraction (all rejected).

**Sources**: [development secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0), [SqlClient Entra authentication](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/azure-active-directory-authentication?view=sql-server-ver17), [managed identity](https://learn.microsoft.com/en-us/azure/app-service/tutorial-connect-msi-sql-database)

## EF Core provider and migrations

**Decision**: Keep only `Microsoft.EntityFrameworkCore.SqlServer`; preserve the two existing SQL Server-shaped migrations and validate clean `MigrateAsync`. Add only a forward correction if evidence requires it.

**Rationale**: The provider supports SQL Server/Azure SQL. Existing migrations use SQL Server types and identity annotations. No second-provider migration set is useful.

**Alternatives considered**: Rebase history, retain SQLite, or maintain multiple provider sets (rejected).

**Sources**: [SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/), [provider migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)

## SQL Server Testcontainers

**Decision**: Add `Testcontainers.MsSql` 4.14.0 only to database-backed test projects, pin `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, share one container per run, and create/migrate/drop a unique logical database per independent scope. Tests never read Azure configuration.

**Rationale**: The official module supplies lifecycle/readiness and an EF-compatible connection string. Server sharing controls startup cost while database leases preserve parallel isolation.

**Alternatives considered**: Container per test (slow); shared reset database (coupled); Azure in CI (prohibited); EF InMemory/SQLite (not provider-faithful).

**Sources**: [Testcontainers.MsSql 4.14.0](https://www.nuget.org/packages/Testcontainers.MsSql/4.14.0), [MSSQL module](https://dotnet.testcontainers.org/modules/mssql/), [Microsoft SQL Server container tags](https://mcr.microsoft.com/en-us/artifact/mar/mssql/server), [ASP.NET example](https://dotnet.testcontainers.org/examples/aspnet/)

## Application cleanup and CI

**Decision**: Order time-based queries in SQL before materialization; detect SQL Server duplicate codes 2601/2627 directly; move relational tests to SQL Server and pure behavior to database-free fakes. Run backend unit/integration validation on a Docker-capable Ubuntu CI runner with Testcontainers owning lifecycle.

**Rationale**: This follows the constitution's EF standards and removes provider compatibility rather than abstracting it.

**Alternatives considered**: Portability materialization, generic provider abstraction, retaining EF InMemory in integration, workflow-managed shared SQL service, or skipping CI integration tests (rejected).
