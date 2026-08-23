# Tasks: SQL Server-Only Persistence

**Input**: Design documents from `/specs/005-sql-server-migration/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/database-environments.md, quickstart.md

**Tests**: Required by FR-009/FR-019 and Constitution IV. For every behavior-changing pair below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and has no unmet dependency
- **[Story]**: Traceability to US1, US2, or US3

## Phase 1: Setup

**Purpose**: Add the test dependency without removing providers still needed by the compiling pre-migration suite.

- [X] T001 Add `Testcontainers.MsSql` version 4.14.0 while retaining existing packages temporarily in `backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj`
- [X] T002 [P] Verify and retain the `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` image and Docker prerequisite without credentials in `specs/005-sql-server-migration/quickstart.md`

---

## Phase 2: Foundational SQL Server Test Infrastructure

**Purpose**: Provide the migrated, isolated SQL Server database lifecycle required by every database-backed story.

**⚠️ CRITICAL**: User-story database tasks cannot start until this phase passes.

- [X] T003 Add failing lifecycle tests for unique names, migration-before-use, cross-lease isolation, and explicit database cleanup in `backend/tests/LootSingles.IntegrationTests/Infrastructure/SqlServerDatabaseFixtureTests.cs`
- [X] T004 Implement a single-run `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` `MsSqlContainer` lifecycle with non-secret startup diagnostics in `backend/tests/LootSingles.IntegrationTests/Infrastructure/SqlServerContainerFixture.cs` to make the container-start portion of T003 pass
- [X] T005 Implement collision-resistant create/migrate/connection/dispose/drop behavior, including context disposal and SQL client pool clearing, in `backend/tests/LootSingles.IntegrationTests/Infrastructure/SqlServerDatabaseLease.cs` to make the remainder of T003 pass
- [X] T006 Register the shared fixture lifetime and safe test parallelization policy in `backend/tests/LootSingles.IntegrationTests/Infrastructure/SqlServerTestCollection.cs`
- [X] T007 Run `SqlServerDatabaseFixtureTests`, confirm all T003 tests pass, and record any runtime/image prerequisite correction in `specs/005-sql-server-migration/quickstart.md`

**Checkpoint**: Tests can acquire independent, migrated SQL Server databases without Azure access.

---

## Phase 3: User Story 1 - Run Reliably on the Production Database Family (Priority: P1) 🎯 MVP

**Goal**: Make production persistence SQL Server-only, validate migrations and relied-upon SQL Server semantics, and eliminate compatibility-shaped application queries.

**Independent Test**: Apply all migrations to a clean SQL Server lease and exercise authentication, employee, dashboard, import, and order persistence behavior with no SQLite branch in production code.

### Tests for User Story 1

- [X] T008 [P] [US1] Add failing clean-database migration-history and current-model validation tests, including applied migration order, in `backend/tests/LootSingles.IntegrationTests/Persistence/MigrationTests.cs`
- [X] T009 [P] [US1] Add failing SQL Server schema behavior tests for unique indexes, foreign keys, cascade behavior, and behaviorally relevant indexes in `backend/tests/LootSingles.IntegrationTests/Persistence/SqlServerSchemaTests.cs`
- [X] T010 [P] [US1] Add failing transaction rollback and multi-entity atomicity tests using a SQL Server lease in `backend/tests/LootSingles.IntegrationTests/Persistence/SqlServerTransactionTests.cs`
- [X] T011 [P] [US1] Add failing concurrent duplicate-order and normalized-username tests that assert stable application-level outcomes under SQL Server error 2601/2627 behavior in `backend/tests/LootSingles.IntegrationTests/Persistence/SqlServerConcurrencyTests.cs`
- [X] T012 [P] [US1] Add failing SQL Server translation/order tests for `OrderRepository`, `DashboardRepository`, and `EmployeeRepository`, including `DateTimeOffset` values and deterministic tie-breakers, in `backend/tests/LootSingles.IntegrationTests/Persistence/SqlServerQueryTranslationTests.cs`
- [X] T013 [US1] Run T008–T012 before production edits and record each expected Red failure reason in `specs/005-sql-server-migration/validation-results.md`

### Implementation for User Story 1

- [X] T014 [P] [US1] Replace client-side order-list sorting with SQL-translated ordering before projection/materialization in `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs` to pass the order portion of T012
- [X] T015 [P] [US1] Replace client-side Ready-dashboard sorting with SQL-translated deterministic ordering before projection/materialization in `backend/src/LootSingles.Infrastructure/Persistence/DashboardRepository.cs` to pass the dashboard portion of T012
- [X] T016 [P] [US1] Replace client-side audit sorting with SQL-translated deterministic ordering before materialization in `backend/src/LootSingles.Infrastructure/Persistence/EmployeeRepository.cs` to pass the audit portion of T012
- [X] T017 [US1] Remove SQLite reflection/branches and retain typed SQL Server 2601/2627 duplicate detection in `backend/src/LootSingles.Infrastructure/Persistence/DuplicateKeyDetector.cs` to pass T011
- [X] T018 [US1] Correct the existing SQL Server migrations/model snapshot only if T008–T010 prove a mismatch, adding a forward migration under `backend/src/LootSingles.Infrastructure/Persistence/Migrations/` rather than rebasing history
- [X] T019 [US1] Remove SQLite compatibility wording from the application-level persistence exception contract in `backend/src/LootSingles.Application/Persistence/UniqueConstraintViolationException.cs`
- [X] T020 [US1] Run T008–T012 and the affected repository/import/authentication tests against SQL Server and append Green evidence to `specs/005-sql-server-migration/validation-results.md`

**Checkpoint**: The application persistence implementation is SQL Server-specific, migrations create the expected schema, and production queries no longer materialize for SQLite compatibility.

---

## Phase 4: User Story 2 - Validate Against Isolated SQL Server Databases (Priority: P2)

**Goal**: Move every database-backed automated test and the Playwright host onto disposable migrated SQL Server databases, leaving unit tests database-free.

**Independent Test**: Run unit, integration, and Playwright suites without Azure configuration; all relational tests use isolated SQL Server leases and clean up successfully.

### Tests for User Story 2

- [ ] T021 [P] [US2] Extend T003 with a failing cancellation/failure cleanup test that proves a later lease can recreate/use storage without observing abandoned state in `backend/tests/LootSingles.IntegrationTests/Infrastructure/SqlServerDatabaseFixtureTests.cs`
- [ ] T022 [P] [US2] Add a failing HTTP factory isolation test proving two independently live API factories cannot observe each other's employees/orders in `backend/tests/LootSingles.IntegrationTests/Auth/AuthWebApplicationFactoryTests.cs`
- [ ] T023 [P] [US2] Add a failing Playwright-host health/seed persistence test against SQL Server lifecycle in `backend/tests/LootSingles.IntegrationTests/Infrastructure/E2EHostDatabaseTests.cs`

### Implementation for User Story 2

- [ ] T024 [US2] Harden lease cleanup for cancellation/failure and make cleanup errors observable in `backend/tests/LootSingles.IntegrationTests/Infrastructure/SqlServerDatabaseLease.cs` to pass T021
- [ ] T025 [US2] Replace the shared in-memory SQLite connection and `EnsureCreatedAsync` with an injected migrated SQL Server lease in `backend/tests/LootSingles.IntegrationTests/Auth/AuthWebApplicationFactory.cs` to pass T022
- [ ] T026 [US2] Replace direct SQLite contexts/`EnsureCreatedAsync` with the shared SQL Server lease in `backend/tests/LootSingles.IntegrationTests/Auth/EmployeeConfigurationTests.cs`
- [ ] T027 [US2] Replace direct SQLite contexts/`EnsureCreatedAsync` with the shared SQL Server lease in `backend/tests/LootSingles.IntegrationTests/Auth/SessionInvalidationTests.cs`
- [ ] T028 [US2] Replace EF InMemory creation with database-free fakes for pure parsing/orchestration tests and SQL Server leases for persistence assertions in `backend/tests/LootSingles.IntegrationTests/Import/ImportTestSupport.cs` and its callers under `backend/tests/LootSingles.IntegrationTests/Import/`
- [ ] T029 [US2] Convert direct SQLite atomicity and concurrency setups to migrated SQL Server leases in `backend/tests/LootSingles.IntegrationTests/Import/AtomicPersistenceTests.cs` and `backend/tests/LootSingles.IntegrationTests/Import/DuplicateOrderTests.cs`
- [ ] T030 [US2] Replace `EnsureCreatedAsync` test-host seeding with migrated lease seeding in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportUiTestSupport.cs`
- [ ] T031 [US2] Move the EF InMemory model-configuration test to SQL Server schema/model validation or a database-free metadata test in `backend/tests/LootSingles.IntegrationTests/Persistence/OrderConfigurationTests.cs`
- [ ] T032 [US2] Replace E2E-host in-memory SQLite registration and schema creation with a disposable migrated SQL Server database lifecycle in `backend/tests/LootSingles.E2EHost/Program.cs` and `backend/tests/LootSingles.E2EHost/LootSingles.E2EHost.csproj` to pass T023
- [ ] T033 [US2] Remove `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.InMemory`, SQLite audit suppression, and direct SQLite usings after all conversions compile in `backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj` and `backend/tests/LootSingles.E2EHost/LootSingles.E2EHost.csproj`
- [ ] T034 [US2] Add Docker-capable backend build/unit/integration validation with Testcontainers-owned lifecycle and no Azure secrets in `.github/workflows/backend.yml`
- [ ] T035 [US2] Run two independent integration scopes concurrently, the complete integration suite, and Playwright; append isolation/cleanup Green evidence to `specs/005-sql-server-migration/validation-results.md`

**Checkpoint**: All database-backed automation uses isolated SQL Server and all pure unit behavior is database-free.

---

## Phase 5: User Story 3 - Use a Cost-Safe Hosted Development Database (Priority: P3)

**Goal**: Make normal manual/hosted development target `loot-singles-dev` securely and document a separately approved, cost-safe Azure setup without provisioning it.

**Independent Test**: With an externally supplied authorized connection, apply migrations and complete the smoke test; without it, startup fails clearly and never falls back.

### Tests for User Story 3

- [ ] T036 [P] [US3] Add failing configuration tests for a missing `ConnectionStrings:LootSingles` value and SQL Server-only registration in `backend/tests/LootSingles.IntegrationTests/Configuration/DatabaseConfigurationTests.cs`
- [ ] T037 [P] [US3] Add a tracked-secret regression check covering settings/examples/documentation paths in `.github/workflows/backend.yml`

### Implementation for User Story 3

- [ ] T038 [US3] Make the runtime SQL Server registration and explicit missing-configuration failure satisfy T036 without adding fallback logic in `backend/src/LootSingles.Api/Program.cs`
- [ ] T039 [US3] Replace the hard-coded design-time LocalDB assumption with externally supplied SQL Server configuration and a non-secret failure message in `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContextFactory.cs`
- [ ] T040 [US3] Enable local Secret Manager metadata without storing values in `backend/src/LootSingles.Api/LootSingles.Api.csproj` and document `ConnectionStrings:LootSingles` setup in `README.md`
- [ ] T041 [US3] Document the non-provisioning `loot-singles-dev` plan, current free limits, pause-on-exhaustion control, monitoring, default-deny firewall, port 1433, Entra/managed identity preference, and expected unavailable states in `README.md` and `specs/005-sql-server-migration/quickstart.md`
- [ ] T042 [US3] After separately approved Azure provisioning only, execute the `loot-singles-dev` migration/smoke test and record evidence without secrets in `specs/005-sql-server-migration/validation-results.md`; otherwise mark this explicit external approval gate as pending without provisioning

**Checkpoint**: Development configuration is SQL Server-only, secret-safe, cost-safe by design, and ready for separately approved Azure provisioning.

---

## Phase 6: Polish & Cross-Cutting Validation

**Purpose**: Remove stale compatibility guidance and prove repository-wide completion.

- [ ] T043 [P] Rewrite stale SQLite architecture/workaround references without changing completed-task meaning in `specs/001-tcgplayer-order-import/tasks.md`, `specs/003-login-dashboard-ui/plan.md`, `specs/003-login-dashboard-ui/data-model.md`, `specs/003-login-dashboard-ui/tasks.md`, `specs/003-login-dashboard-ui/validation-results.md`, and `specs/004-order-import-ui/tasks.md`
- [ ] T044 [P] Update current database/test instructions and environment matrix in `README.md` and `specs/005-sql-server-migration/contracts/database-environments.md`
- [ ] T045 Run the repository-wide FR-002/SC-001 search from `specs/005-sql-server-migration/quickstart.md`, classify every match, remove all active SQLite package/API/configuration/branch/workaround/test-host/fixture/`.db`/current-guidance matches, and record that any remainder is confined to this feature's removal history or audit definitions in `specs/005-sql-server-migration/validation-results.md`
- [ ] T046 Run backend build, unit tests, SQL Server integration tests, CSharpier, frontend tests/build/lint/Prettier, and Playwright; record commands/counts/results in `specs/005-sql-server-migration/validation-results.md`
- [ ] T047 Review container logs, CI output, docs, fixtures, and tracked configuration for credentials/connection strings and record the FR-014 security review in `specs/005-sql-server-migration/validation-results.md`
- [ ] T048 Perform the constitution Architecture and Changeability Review against the implemented boundaries, capture any Must Fix findings as new tasks in `specs/005-sql-server-migration/tasks.md`, and do not proceed to convergence until resolved

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 has no dependency.
- Phase 2 depends on T001 and blocks all database-backed story tasks.
- US1, US2, and US3 depend on Phase 2. Execute in priority order because US2 removes temporary packages after US1 behavior is green; US3 can otherwise proceed in parallel after Phase 2.
- Phase 6 depends on all selected stories.

### User story dependencies

- **US1**: Uses only the foundational lease and can prove production SQL Server behavior independently.
- **US2**: Uses the same lease to migrate all automation; T033 waits for every conversion.
- **US3**: Uses the production configuration contract and does not depend on Azure provisioning; T042 is explicitly gated by later approval.

### Red-Green ordering

- T003 → T004–T006 → T007
- T008–T013 → T014–T019 → T020
- T021–T023 → T024–T034 → T035
- T036–T037 → T038–T041; T042 only after external approval
- Package removals T033 occur only after all affected code compiles against SQL Server.

### Parallel opportunities

- T002 can proceed beside T001.
- T008–T012 are separate failing-test files and can be authored in parallel.
- T014–T016 change separate repositories after T012 is Red.
- T021–T023 target separate infrastructure behaviors.
- T026–T031 can be divided by test area after T025 establishes the factory pattern.
- US3 documentation/configuration can proceed beside later US2 conversions after Phase 2.
- T043 and T044 touch separate documentation sets.

## Parallel Example: User Story 1

```text
Task T008: migration validation tests
Task T009: schema constraint/index tests
Task T010: transaction/atomicity tests
Task T011: concurrency/duplicate tests
Task T012: query translation/ordering tests

After T012 fails as expected:
Task T014: OrderRepository SQL ordering
Task T015: DashboardRepository SQL ordering
Task T016: EmployeeRepository SQL ordering
```

## Implementation Strategy

### MVP first

1. Complete Setup and Foundational lifecycle.
2. Complete US1 using strict Red-Green-Refactor.
3. Stop and validate clean migrations, SQL Server constraints, transactions, concurrency, and database-side ordering.

### Incremental delivery

1. US1 makes production persistence SQL Server-native.
2. US2 makes all database-backed automation provider-faithful and isolated.
3. US3 makes manual/hosted development ready for cost-safe Azure SQL without provisioning.
4. Polish proves zero SQLite and full regression safety.

## Notes

- Configuration/package/documentation-only changes do not need artificial behavioral tests; T036/T037 cover configuration outcomes that are meaningfully executable.
- Do not execute T042 without separate explicit Azure provisioning approval.
- Do not add `IDatabaseProvider`, SQLite fallback, Azure-dependent tests, or reusable-container state.
- Every task that handles a connection string must avoid logging its credential-bearing value.
