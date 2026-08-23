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

## Phase 6 zero-SQLite audit (FR-002/SC-001)

- Ran the quickstart.md removal-audit search repository-wide. Zero matches in any `.cs`, `.csproj`,
  `.json`, or other source/config file — no active SQLite package reference, API usage, connection
  branch, or tracked `.db` artifact remains anywhere in `backend/` or `frontend/`.
- Sixteen textual matches remained, all in documentation, and were individually classified:
  - Six were stale current-guidance statements in earlier features' spec artifacts describing SQLite
    as the present integration-test/E2E provider or explaining a SQLite-driven workaround as if still
    active: `specs/001-tcgplayer-order-import/tasks.md` (T054), `specs/003-login-dashboard-ui/plan.md`,
    `specs/003-login-dashboard-ui/data-model.md`, `specs/003-login-dashboard-ui/tasks.md` (T016, T030),
    `specs/003-login-dashboard-ui/validation-results.md`, and `specs/004-order-import-ui/tasks.md`
    (T026). Each was rewritten to state SQLite as the historical, at-the-time provider and name
    005-sql-server-migration as the feature that superseded it (restoring SQL-translated ordering
    where the original workaround affected behavior), without altering what the original completed
    task actually did.
  - Nine were this feature's own removal-decision records (`specs/005-sql-server-migration/spec.md`,
    `plan.md`, `research.md`, `tasks.md`, `validation-results.md`, `quickstart.md`, and
    `contracts/database-environments.md`), which FR-002 explicitly permits for documenting the
    removal, preventing reintroduction, and defining this audit — left unchanged.
  - One was a false positive: `.gitignore`'s `Thumbs.db` (Windows OS artifact ignore pattern,
    unrelated to any database provider) and its identical mention inside the vendored Spec Kit
    `speckit-implement` skill files (`.claude/skills/...`, `.agents/skills/...`) — not project
    architecture guidance, left unchanged.
- README.md and `contracts/database-environments.md` (T055) were reviewed against current behavior;
  both already accurately describe the SQL Server-only environment matrix, configuration contract,
  and bootstrap flow from earlier Phase 5 work, so no further edit was needed.
- Result: zero active SQLite usage remains; every remaining textual mention is confined to this
  feature's removal history/audit definitions or unrelated OS-artifact ignore patterns, satisfying
  SC-001.

## Phase 6 full regression evidence

Commands run from the repository root:

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
npm --prefix frontend test -- --run
npm --prefix frontend run build
npm --prefix frontend run lint
npm --prefix frontend run format:check
npm --prefix frontend run test:e2e
```

Results:

- Backend solution build: 0 warnings, 0 errors.
- Backend unit suite: 74 passed, 0 failed.
- Backend SQL Server integration suite: 92 passed, 0 failed (disposable Testcontainers SQL Server;
  no Azure contact).
- CSharpier: 128 files checked, all already formatted.
- Frontend unit/component suite (Vitest): 6 test files, 37 tests passed, 0 failed.
- Frontend production build (`tsc -b && vite build`): succeeded.
- Frontend lint (oxlint): 0 errors; one pre-existing `react(only-export-components)` warning in
  `frontend/src/features/auth/AuthContext.tsx`, unrelated to this feature (this feature made no
  frontend changes).
- Frontend format check (Prettier): all matched files already conform.
- Playwright E2E (against the SQL Server-backed `LootSingles.E2EHost`): 7 passed, 0 failed
  (login, wrong-PIN, order-import guard/cancellation, mobile-viewport responsiveness, mixed
  packing-slip import at two viewports).

## Phase 6 FR-014 security review

- Re-ran the CI tracked-secret regression pattern
  (`(Password|Pwd|User ID|AccountKey|SharedAccessSignature)\s*=`) against all tracked `*.json`,
  `*.md`, `*.yml`, `*.yaml`, and `*.cs` files: zero matches.
- Searched all tracked files for connection-string shapes (`Server=`, `Data Source=`,
  `Initial Catalog=`, `*.database.windows.net`): every match is a synthetic test literal
  (`Server=configuration-test.invalid`, `Server=unused`, or an assertion that a real value is
  absent from command output) in `DatabaseConfigurationTests.cs`, `OrderConfigurationTests.cs`, and
  `BootstrapAdminCommandTests.cs` — no real server name, credential, or usable connection string is
  committed anywhere in the repository.
- Reviewed `backend/src/LootSingles.Api/appsettings.json` (the only tracked `appsettings*.json`):
  contains no `ConnectionStrings` section and no secret.
- Advisory, not a repository finding: this development machine has a local, git-ignored
  `backend/src/LootSingles.Api/appsettings.Development.json` (matched by `.gitignore` line 22, never
  tracked) containing a passwordless `(localdb)\MSSQLLocalDB` connection string. It commits nothing
  and contains no credential, so it does not violate FR-014, but because ASP.NET Core loads
  `appsettings.{Environment}.json` before Secret Manager, it would supply a working connection string
  locally on this machine even when `ConnectionStrings:LootSingles` is unset in Secret Manager,
  masking the "missing configuration fails startup" behavior only in that local scenario. No code or
  repository change is required; flagged for the developer's awareness only.
- No container logs, CI output, or fixture files contain a PIN, PIN hash, or credential; the
  bootstrap process tests (T044, T052) and the tracked-secret CI step provide ongoing regression
  coverage.

## Phase 6 constitution Architecture and Changeability Review (post-implementation)

Re-evaluated every significant component against the plan's original architecture table and the
constitution's ten review points, including the components added after planning to finish US3
(`BootstrapAdminService`, `BootstrapAdminCommand`, `EmployeeRepository.TryAddFirstEmployeeAsync`):

- **API registration / design-time factory / migrations / test fixture / database lease /
  WebApplicationFactory / E2E host / repositories / Azure dev configuration**: implementation matches
  the plan's boundaries and dependency directions with no drift; SQL Server specifics remain confined
  to Infrastructure and test infrastructure, Domain/Application stay provider-free, and no
  `IDatabaseProvider`, SQLite fallback, or reusable-container state was introduced.
- **`BootstrapAdminService`** (Application): single responsibility (validate, build the candidate
  employee, delegate the atomic first-employee race), depends only on `IEmployeeRepository`/
  `IPinHasher` abstractions, returns a typed `BootstrapAdminOutcome` rather than exposing exception
  text, and is unit-testable with a fake repository — no infrastructure dependency leaked into
  Application.
- **`BootstrapAdminCommand`** (Api): depends directly on `LootSinglesDbContext` for `MigrateAsync`,
  consistent with the already-established pattern of `Program.cs` composing Infrastructure directly
  at the Api/composition-root boundary; the broad catch that emits only a sanitized message is a
  deliberate, documented boundary enforcing FR-014, not an accidental swallow.
- **`EmployeeRepository.TryAddFirstEmployeeAsync`**: confines the SQL Server-specific
  `sp_getapplock` mechanism to Infrastructure behind the existing repository abstraction; reuses the
  existing `SaveChangesAsync`/`DuplicateKeyDetector` duplicate-translation path rather than
  duplicating that catch logic (Principle XIII's duplication-consolidation rule), and is verified by
  a real two-process SQL Server concurrency test (T050) because the guarantee cannot be meaningfully
  faked at the unit level.
- **No Must Fix findings.** No new task is required. Convergence verification may proceed.
