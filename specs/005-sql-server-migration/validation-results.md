# Validation Results: SQL Server-Only Persistence

## Phase 3 Red evidence

Command:

```powershell
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj --filter "FullyQualifiedName~MigrationTests|FullyQualifiedName~SqlServerSchemaTests|FullyQualifiedName~SqlServerTransactionTests|FullyQualifiedName~SqlServerConcurrencyTests|FullyQualifiedName~SqlServerQueryTranslationTests" --no-restore
```

Result before production edits: 11 total, 8 passed, 3 failed as expected.

- `Order_repository_orders_offsets_and_ties_in_sql`: the existing client-side tie ordering differed from the new deterministic assertion and the executed SQL had no database ordering.
- `Dashboard_repository_orders_offsets_and_ties_in_sql`: the executed SQL contained no `ORDER BY`; ordering occurred after materialization.
- `Employee_repository_orders_audit_offsets_and_ties_in_sql`: equal timestamps had no deterministic tie-breaker and ordering occurred after materialization.

The migration/model, schema/index, transaction/atomicity, and concurrent SQL Server duplicate tests were already Green. This proved that the checked-in migrations match the current model, so T018 required no forward corrective migration.

## Phase 3 Green evidence

- Phase 3 SQL Server persistence suite: 11 passed, 0 failed. This includes repository query translation, import and employee duplicate translation, migrations, schema constraints/indexes, transactions, atomicity, and concurrency.
- Foundational SQL Server fixture suite: 4 passed, 0 failed.
- Backend unit suite: 68 passed, 0 failed.
- Backend solution build: succeeded with 0 errors.
- CSharpier was applied to all changed C# files and rechecked after formatting.

The integration project still temporarily references SQLite for tests scheduled for conversion in Phase 4 (T025-T033); the package audit warning therefore remains expected until that phase.

## Phase 4 SQL Server isolation evidence

- Red contract run: T021 initially failed to compile because the lease had no post-create failure seam; the HTTP isolation test observed SQLite rather than SQL Server; the E2E health endpoint did not expose the migrated-provider/seed contract.
- Two independently live HTTP factories created and seeded separate migrated SQL Server databases concurrently. Employee and order records remained mutually invisible; 1 isolation test passed.
- Cleanup failure injection proved the partially created database was dropped and a later migrated lease remained usable.
- Complete integration suite: 82 passed, 0 failed, including HTTP, authentication, dashboard, imports, schema, concurrency, transaction, cleanup, and E2E-host health coverage.
- Database-free backend unit suite: 68 passed, 0 failed.
- Playwright against the disposable migrated SQL Server E2E host: 7 passed, 0 failed.
- Backend solution build completed with 0 warnings and 0 errors after removing the SQLite and EF InMemory packages.
- CSharpier formatted the converted backend test files.

No Azure configuration or credentials were used. Container and database connection details were not written to validation output.

## Phase 5 development configuration evidence

- Red configuration run: null, empty, and whitespace `ConnectionStrings:LootSingles` cases did not fail at the required application-composition boundary, and the backend workflow had no tracked-secret regression step. SQL Server-only provider registration was already Green.
- Green configuration run: 5 passed, 0 failed. Missing/blank configuration now fails with a non-secret key-based message; configured runtime resolution uses `Microsoft.EntityFrameworkCore.SqlServer` only.
- The design-time factory reads the same Secret Manager/environment configuration key and contains no LocalDB or other fallback.
- The backend CI tracked-secret command completed with no matches in tracked JSON, Markdown, YAML, or C# files.
- Azure SQL free-offer, networking, identity, monitoring, and unavailable-state guidance was reverified against Microsoft Learn on 2026-08-22 and documented without provisioning.
- Backend solution build: 0 warnings, 0 errors; unit tests: 68 passed; integration tests: 87 passed; CSharpier check passed.

## Phase 5 initial Manager/Admin bootstrap evidence

- Red unit run failed to compile because `BootstrapAdminService` and its typed outcomes did not yet
  exist. Red integration run failed to compile because `BootstrapAdminCommand` did not yet exist.
- Focused Green unit suite: 6 passed, 0 failed. Invalid inputs do not write, a non-empty employee
  store is refused without modification, and an empty store receives exactly one active, unlocked
  `ManagerAdmin` with a verifiable PBKDF2 PIN hash.
- Focused Green SQL Server integration suite: 4 passed, 0 failed. Coverage includes applying
  migrations to a deleted schema, first creation, repeat/non-empty refusal, sanitized output, and a
  child-process invocation of the compiled `bootstrap-admin` CLI with temporary environment values.
- The process smoke test used only a disposable Testcontainers database and synthetic temporary
  values. It verified the CLI exits without hosting and that neither PIN nor connection string is
  written to standard output or standard error. No Azure database or real employee credential was
  created or changed.
- Backend solution build: 0 warnings, 0 errors; full unit suite: 74 passed; full SQL Server
  integration suite: 91 passed; CSharpier was applied to all Phase 5 C# and project files.
- Documentation now covers Secret Manager and hosted environment keys, first-run/refusal behavior,
  immediate bootstrap-secret removal, and authenticated employee management for every later account.

T042 external gate: **PENDING — NOT EXECUTED**. No Azure resource was provisioned, no firewall was changed, no Azure connection was attempted, and no smoke test was run because separate provisioning approval has not been granted.

## Phase 5 bootstrap concurrency remediation evidence

- Red run: a new `ExecuteAsync_ConcurrentBootstrapAttemptsOnSameEmptyDatabase_ExactlyOneSucceeds`
  test launched two `BootstrapAdminCommand` invocations with different valid usernames against the
  same empty SQL Server lease. Both succeeded (2 `SuccessExitCode`, 0 `RefusedExitCode`) and two
  employees were persisted, proving the prior `AnyAsync` check plus separate `Add`/`SaveChangesAsync`
  call was a non-atomic check-then-act race between processes/connections.
- Implementation: replaced that sequence with one typed `IEmployeeRepository.TryAddFirstEmployeeAsync`
  operation. `EmployeeRepository` now opens a transaction, acquires an exclusive, transaction-scoped
  SQL Server application lock (`sp_getapplock` on resource `LootSingles.Employees.Bootstrap`), checks
  for an existing employee, inserts and commits only if the store was empty, and otherwise rolls back
  without modification by disposing the uncommitted transaction. `BootstrapAdminService` now builds
  the candidate employee and delegates the empty-store race entirely to this one repository call.
  `AnyAsync` was removed from `IEmployeeRepository` (it had no other caller) and from the production
  repository and unit-test fakes in `BootstrapAdminServiceTests`, `AuthenticationServiceTests`, and
  `EmployeeManagementServiceTests`, which now implement `TryAddFirstEmployeeAsync` directly.
- Green run: the concurrency test now observes exactly 1 `SuccessExitCode`, 1 `RefusedExitCode`, and
  exactly one persisted employee. Full `BootstrapAdminCommandTests` suite: 5 passed, 0 failed.
- Hardened the child-process smoke test (`CliProcess_WithTemporaryEnvironment_...`) to start draining
  standard output and standard error concurrently before awaiting process exit (avoiding an OS pipe-
  buffer deadlock), bound `WaitForExitAsync` with a 60-second `CancellationTokenSource`, kill the
  entire process tree (`Process.Kill(entireProcessTree: true)`) on timeout, and fail with an explicit,
  non-secret timeout message rather than hanging.
- Full regression: backend solution build 0 warnings/0 errors; full unit suite 74 passed, 0 failed;
  full SQL Server integration suite 92 passed, 0 failed; `dotnet csharpier check .` reported all 128
  files already formatted.
- Secret-safe review: the concurrency and hardened process tests assert (and re-verified by inspection)
  that the initial PIN, PIN hash, and connection string never appear in captured process/command
  output; no PIN, hash, or connection string was written to this file or any tracked file.
