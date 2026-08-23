# Feature Specification: SQL Server-Only Persistence

**Feature Branch**: `005-sql-server-migration`

**Created**: 2026-08-22

**Status**: Draft

**Input**: Remove SQLite completely and standardize Loot Singles Fulfillment on SQL Server/Azure SQL, with isolated SQL Server integration testing and no Azure resource provisioning during specification or planning.

## Clarifications

### Session 2026-08-22

- Q: Must existing development data be migrated, or may development databases be recreated without preserving data? → A: Development data may be discarded; recreate development databases from migrations with no data-transfer path.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run Reliably on the Production Database Family (Priority: P1)

As a fulfillment operator, I need all existing workflows to use the same relational database family used in production so provider differences do not change behavior or force production queries into less reliable or less efficient shapes.

**Why this priority**: Correct persistence, ordering, constraints, and transactions underpin every fulfillment workflow and prevent environment-specific defects.

**Independent Test**: Run the application against a clean SQL Server-family database, apply its schema, and exercise authentication, employee management, dashboard, order import, and order listing without any SQLite dependency or provider-specific compatibility path.

**Acceptance Scenarios**:

1. **Given** a clean supported SQL Server-family database, **When** the application schema is applied and the application starts, **Then** all existing persistence-backed workflows operate with their approved behavior unchanged.
2. **Given** an order, audit-event, or dashboard query whose results are ordered by time, **When** it executes, **Then** filtering, ordering, projection, and limiting that the database can perform occur before result materialization.
3. **Given** a duplicate key, foreign-key violation, transaction rollback, or concurrent persistence attempt, **When** it occurs, **Then** the application preserves its existing explicit failure behavior without relying on SQLite semantics.

---

### User Story 2 - Validate Against Isolated SQL Server Databases (Priority: P2)

As a developer, I need automated integration tests to use disposable isolated SQL Server databases so tests exercise production-relevant query translation, constraints, transactions, and concurrency without sharing development data or requiring Azure access.

**Why this priority**: Provider-faithful tests are the primary safeguard against the environment differences this feature is intended to remove.

**Independent Test**: Run the complete integration suite from a clean development or CI environment with a supported container runtime; each test collection receives isolated database state, migrations are applied, tests pass without Azure connectivity, and disposable resources are cleaned up.

**Acceptance Scenarios**:

1. **Given** a machine with the documented container prerequisite, **When** integration tests run, **Then** they start SQL Server test infrastructure, apply the real migrations, and exercise the application through the SQL Server provider.
2. **Given** tests that cover `DateTimeOffset`, transactions, unique constraints, foreign keys, relevant indexes, atomicity, or concurrency, **When** they execute, **Then** assertions validate SQL Server behavior rather than an emulation or alternate relational provider.
3. **Given** concurrent or sequential integration test execution, **When** tests create or modify data, **Then** test data is isolated from other tests and from every shared development or hosted database.
4. **Given** completion, failure, or cancellation of the integration suite, **When** test cleanup runs, **Then** its disposable database resources are removed.

---

### User Story 3 - Use a Cost-Safe Hosted Development Database (Priority: P3)

As a developer or deployment operator, I need manual and hosted development runs to target the `loot-singles-dev` Azure SQL database through secure external configuration so realistic validation is available without committed credentials or accidental paid overage.

**Why this priority**: A production-family development target is needed for manual validation, but it must not compromise cost or credential safety.

**Independent Test**: Configure the application using a secret supplied outside source control, connect from an explicitly allowed development client, apply the schema, and complete a documented smoke test while the database is configured to become unavailable rather than incur paid overage when its free limit is exhausted.

**Acceptance Scenarios**:

1. **Given** an authorized developer and an externally supplied `loot-singles-dev` connection configuration, **When** the application starts, **Then** it connects to Azure SQL without any secret stored in tracked files.
2. **Given** the free allowance is exhausted, **When** the configured limit behavior is triggered, **Then** the development database becomes unavailable until the allowance resets rather than continuing with billable usage.
3. **Given** no development database secret is configured, **When** a manual or hosted development run starts, **Then** startup fails clearly instead of silently falling back to SQLite or another local database.

### Edge Cases

- The container runtime is unavailable, cannot download the pinned SQL Server image, or cannot allocate the required resources.
- Database startup succeeds but migrations fail or the schema does not match the current model.
- Multiple test processes or collections run concurrently and attempt to use the same logical database name.
- A test fails or is cancelled before normal cleanup completes.
- Azure SQL is paused, its monthly free allowance is exhausted, a firewall rule blocks the client, or credentials are invalid.
- Existing migrations contain a provider-specific annotation or column definition that does not match the current SQL Server model.
- SQL Server collation, string comparison, precision, transaction, constraint, or concurrency behavior differs from prior SQLite-backed test expectations.
- A developer attempts to configure SQLite, use a `.db` file, or point automated tests at `loot-singles-dev`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The application MUST use SQL Server/Azure SQL as its only relational database family in production, hosted development, manual testing, and database-backed automated tests.
- **FR-002**: The repository MUST contain no active SQLite provider dependencies, connection usage, configuration, provider-specific branches, workaround comments, compatibility code, test hosts/fixtures, or tracked `.db` artifacts. Feature records MAY name SQLite only to document its removal, prevent reintroduction, or define the acceptance audit; they MUST NOT direct current architecture or execution to use it.
- **FR-003**: The application MUST NOT provide SQLite or another database provider as a fallback when SQL Server configuration or connectivity is unavailable.
- **FR-004**: Persistence queries MUST target SQL Server capabilities and MUST perform supported filtering, ordering, projection, aggregation, and limiting in the database before materialization.
- **FR-005**: Existing user-visible and business behavior MUST remain unchanged unless a database-family difference makes a documented correction necessary.
- **FR-006**: Database constraint and transaction failures relied upon by application behavior MUST be detected and represented through existing stable application contracts without alternate-provider compatibility logic.
- **FR-007**: Integration tests MUST run against isolated, disposable SQL Server databases and MUST NOT connect to Azure SQL or any shared development database.
- **FR-008**: Integration tests MUST apply and validate the application's actual migrations rather than create an unversioned schema directly from the current model.
- **FR-009**: Integration coverage MUST validate SQL Server query translation, `DateTimeOffset` storage and ordering, transactions, unique constraints, foreign keys, behaviorally relevant indexes, persistence atomicity, concurrency-sensitive behavior, and any SQL Server-specific behavior the application relies upon.
- **FR-010**: Test infrastructure MUST prevent data leakage between independently executable test scopes and MUST attempt cleanup after success, failure, and cancellation.
- **FR-011**: Unit tests MUST remain database-free unless a test genuinely requires database behavior; tests requiring relational behavior belong in the SQL Server-backed integration suite.
- **FR-012**: The existing migration history MUST be preserved and validated when it accurately represents the SQL Server model; any replacement or corrective migration strategy MUST be explicitly documented, reproducible, and reviewed before implementation.
- **FR-013**: The application MUST support manual and hosted development against an Azure SQL database named `loot-singles-dev` using configuration supplied outside source control.
- **FR-014**: Connection strings, passwords, access tokens, Azure credentials, and other secrets MUST NOT be committed in source, test fixtures, logs, documentation examples, or generated artifacts.
- **FR-015**: The development database guidance MUST use the current Azure SQL free offer when eligible and configure free-limit exhaustion to pause service until the next monthly reset rather than enable paid overage.
- **FR-016**: Development database network access MUST deny clients by default and grant only the minimum documented access needed by authorized development or hosted clients.
- **FR-017**: Azure resource creation, paid-resource enrollment, and production database provisioning are out of scope and MUST require separate explicit approval.
- **FR-018**: Continuous integration MUST provide the supported container capability needed by integration tests and MUST fail when SQL Server-backed integration validation fails.
- **FR-019**: Implementation tasks that modify application or query behavior MUST follow test-first Red-Green-Refactor ordering, and all existing relevant automated suites MUST remain passing.
- **FR-020**: Documentation MUST state prerequisites, secure configuration, migration application, manual validation, free-usage monitoring, expected unavailable states, and test execution/cleanup procedures.
- **FR-021**: Existing development data MAY be discarded; development databases MUST be recreated from migrations without a SQLite-to-SQL Server data-transfer path.

### Key Entities

- **Application Database**: The authoritative relational store for employees, audit events, imports, orders, and order lines; its existing domain data and constraints remain authoritative.
- **Migration History**: The ordered, versioned schema changes used to create or update SQL Server-family databases.
- **Integration Test Database**: An isolated disposable database created for an automated test scope and never shared with development or production environments.
- **Development Database**: The Azure SQL database named `loot-singles-dev`, used only for hosted/manual development validation and configured outside source control.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A repository-wide audit finds zero active SQLite packages, APIs, configuration entries, compatibility branches, workaround comments, test hosts/fixtures, tracked database files, or current guidance to use SQLite; every remaining textual mention is confined to removal history or audit prevention in this feature's records.
- **SC-002**: 100% of database-backed integration tests execute against isolated SQL Server databases and require no Azure connectivity.
- **SC-003**: Automated validation proves all existing persistence-backed workflows retain their approved outcomes across authentication, employee management, dashboard, import, and order browsing.
- **SC-004**: Automated tests demonstrate correct ordering and projection for all three currently identified time-ordered query paths without materializing results before database-supported ordering.
- **SC-005**: Automated tests cover migrations, time-offset values, transactions, unique constraints, foreign keys, relevant indexes, atomicity, and concurrency-sensitive behavior, with all such tests passing.
- **SC-006**: Two independently executable integration test scopes can run without observing or modifying each other's data.
- **SC-007**: A clean environment with documented prerequisites can run the full integration suite and clean up its disposable database resources without a manually maintained database.
- **SC-008**: A documented manual smoke test can connect securely to `loot-singles-dev`, apply the schema, and exercise core persistence-backed workflows without any tracked secret.
- **SC-009**: Cost-safety review confirms the development database is configured to become unavailable rather than incur paid overage when the free monthly allowance is exhausted.
- **SC-010**: Existing unit, integration, frontend, formatting, build, and applicable end-to-end validation remain passing after the migration.

## Assumptions

- Azure SQL Database remains eligible for Microsoft's current free database offer at provisioning time; eligibility and limits must be reverified before any later provisioning action.
- Developers and CI runners have access to a supported container runtime for SQL Server integration tests.
- Existing migrations appear SQL Server-specific and are expected to be preserved unless validation finds a concrete defect.
- Existing development data has no preservation requirement and may be discarded when development databases are recreated.
- Automated integration tests may share a container process for efficiency only if logical database isolation and deterministic cleanup are preserved.
- No abstraction for multiple relational providers is needed or desired; SQL Server is the application database provider.
- Production provisioning and deployment topology remain outside this feature except for documenting compatible configuration requirements.
