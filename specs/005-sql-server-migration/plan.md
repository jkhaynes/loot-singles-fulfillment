# Implementation Plan: SQL Server-Only Persistence

**Branch**: `005-sql-server-migration` | **Date**: 2026-08-22 | **Spec**: [spec.md](spec.md)

## Summary

Remove SQLite and EF Core InMemory from database-backed paths, retain SQL Server as the sole relational provider, and restore database-side ordering blocked by SQLite. Preserve and validate the existing SQL Server-shaped migrations. Use a pinned SQL Server Testcontainer shared per test run with uniquely named migrated databases per isolated test scope. Configure normal manual/hosted development for external Azure SQL database `loot-singles-dev` through untracked configuration, preferring passwordless identity and pause-on-free-limit exhaustion.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400; TypeScript frontend unchanged

**Primary Dependencies**: ASP.NET Core 10, EF Core 10 SQL Server, Testcontainers.MsSql 4.14.0, xUnit 2.9.3, WebApplicationFactory, Playwright

**Storage**: SQL Server 2022 container for automated integration/E2E; Azure SQL `loot-singles-dev` for manual/hosted development; Azure SQL production

**Testing**: xUnit, SQL Server Testcontainers, Vitest/React Testing Library, Playwright, CSharpier, Prettier

**Target Platform**: .NET 10 host; Docker-capable GitHub Ubuntu runner; Azure SQL deployed environments

**Project Type**: React web application plus layered ASP.NET Core API

**Performance Goals**: Supported filtering, ordering, projection, aggregation, and limiting occur before materialization; one SQL Server container startup per test run; no production query materializes solely for alternate-provider translation

**Constraints**: SQL Server only; no Azure dependency in tests; no committed secrets or Azure provisioning; development data disposable; free-limit exhaustion pauses rather than bills

**Scale/Scope**: Existing persisted employee, audit, import, order, and line data; three known early-materialized time-order queries; all backend integration and Playwright persistence paths

## Constitution Check

*GATE: Passed before research and re-checked after design.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Storage architecture only; behavior preserved; FR-021 explicitly approves discarding development data. | PASS |
| III Reviewability | Limited to provider/configuration, provider-faithful tests, affected queries, CI, and directly stale documentation. | PASS |
| IV TDD | SQL Server migration/query/constraint/transaction/concurrency tests precede behavior edits; static package/config changes are explicit non-behavior exceptions. | PASS |
| V/VI/XI Correctness | Real constraints, transactions, atomicity, and concurrency are tested; missing configuration fails explicitly without fallback. | PASS |
| VII Security/cost | No tracked secrets; passwordless identity preferred; free-limit behavior pauses instead of billing. | PASS |
| XII Dependencies | SQL Server stays in Infrastructure/composition; Testcontainers stays in tests; Domain/Application remain provider-free. | PASS |
| XIII Simplicity | No provider abstraction. One shared test database lifecycle consolidates repeated SQLite fixture logic. | PASS |
| EF Core standards | Restore server-side ordering/projection, read-only no-tracking, async I/O, migration-created schemas, and cancellation. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| API registration | Require `ConnectionStrings:LootSingles` and directly register SQL Server. API composition depends on Infrastructure; Domain/Application remain provider-free. | Endpoints/identity vary through configuration. Missing/invalid settings fail explicitly. No provider factory or fallback. |
| Design-time factory | Supply SQL Server options to EF tooling without embedded usable credentials. | Connection input varies externally; tooling errors are explicit. One concrete factory is sufficient. |
| Migrations | Version SQL Server/Azure SQL schema; no seeding or alternate provider. | Existing files already have SQL Server types/identity. Validate clean apply; add only a proven forward correction. Preserving history is simpler than rebasing. |
| Test-run fixture | Own one `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` container lifecycle and create/drop logical databases; never seed scenarios. Tests depend on Testcontainers/SqlClient only. | Runtime/image failures fail tests with non-secret diagnostics. Sharing startup is efficient and remains isolated through leases. |
| Database lease | Generate unique name, create, migrate, provide connection, then dispose pools and drop. | Supports parallel test scopes without provider variation. A small lease consolidates repeated lifecycle responsibilities. |
| WebApplicationFactory | Replace DbContext registration with one lease and seed synthetic scenario data. | Existing clock/seed variations remain. Extend the current factory instead of duplicating application startup. |
| E2E host | Host real API behavior with an isolated migrated SQL Server database and deterministic seed; not the normal Azure dev host. | Playwright pacing/seed are test concerns. Swap persistence lifecycle without leaking test behavior into production. |
| Repositories | Express SQL Server-translatable queries and translate typed SQL Server duplicate errors. | Query behavior varies, provider does not. Restore `OrderBy` before `ToListAsync`; remove SQLite reflection and portability abstraction. |
| Azure dev configuration | Document endpoint, identity, firewall, migrations, monitoring, and smoke test; provision nothing. | Developer IP/host identity vary operationally. Paused/firewall/auth failures are explicit. Existing ASP.NET configuration is enough. |

**Post-design gate**: Boundaries remain inward-facing, new lifecycle types solve repeated test responsibilities, provider variation is removed, and application changes are test-first. No deviation is required.

## Project Structure

```text
specs/005-sql-server-migration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/database-environments.md
└── tasks.md

backend/
├── src/LootSingles.Api/{Program.cs,appsettings.json}
├── src/LootSingles.Infrastructure/Persistence/{LootSinglesDbContextFactory.cs,*Repository.cs,DuplicateKeyDetector.cs,Migrations/}
├── tests/LootSingles.IntegrationTests/{Infrastructure/,Auth/,Import/,ImportUi/,Dashboard/,Orders/,Persistence/}
├── tests/LootSingles.E2EHost/
└── tests/LootSingles.UnitTests/

.github/workflows/
README.md
specs/{001-tcgplayer-order-import,003-login-dashboard-ui,004-order-import-ui}/
```

**Structure Decision**: Keep existing layered projects. Add shared SQL Server lifecycle helpers only to integration tests. The E2E host owns equivalent lifecycle or references a narrowly shared helper only if dependencies remain acyclic. No production or frontend layer is added.

## Complexity Tracking

No constitution violations require justification.

## Implementation Phases

1. Establish pinned Testcontainers dependencies and shared SQL Server lifecycle/isolation infrastructure.
2. Write failing provider-faithful migration, translation, constraint, transaction, atomicity, index, and concurrency tests.
3. Convert HTTP, repository, import, authentication, and E2E persistence tests to migrated isolated SQL Server; remove SQLite and EF InMemory usage.
4. Write failing ordering tests, then restore server-side time ordering and simplify SQL Server-only duplicate detection.
5. Harden application/design-time/Azure configuration and document safe manual development without provisioning.
6. Add container-capable CI, remove stale SQLite guidance, run the zero-usage audit and all validation.

## Migration and Rollback Strategy

- Preserve `20260820212459_InitialCreate`, `20260821170809_AddEmployeeAuthentication`, designers, and snapshot because they contain SQL Server types and `SqlServer:Identity` annotations.
- Test an empty isolated SQL Server database with `MigrateAsync`, compare applied and assembly migrations, and validate behaviorally important schema constraints/indexes.
- Do not use `EnsureCreated` for relational application, integration, or E2E databases. Add a forward corrective migration only for a proven mismatch; do not rebase history.
- Do not export SQLite data; development data may be discarded.
- Code rollback retains compatible migration history. A destructive schema rollback would require separate backup/restore or forward-fix review.

## Risks

- **Container runtime/cold start**: mitigate with one pinned container per run, documented prerequisites, and CI.
- **Parallel cleanup**: dispose contexts, clear pools, connect to `master`, force rollback when needed, and surface cleanup failures.
- **Migration drift**: validate clean apply plus real constraint/index behavior before provider removal.
- **Collation differences**: document/pin only if approved behavior depends on collation; do not silently normalize behavior.
- **Azure eligibility**: reverify offer before provisioning; stop if pause-on-limit is unavailable.
- **Networking/auth**: document port 1433, least-privilege firewall, Entra setup, and explicit failures.
- **Historical docs**: carefully replace stale active architecture claims because FR-002/SC-001 require a repository-wide zero-SQLite audit.
