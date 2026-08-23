# Tasks: Basic Production Logging

**Input**: Design documents from `/specs/006-production-logging/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Required by constitution Principle IV (TDD is non-negotiable for new/modified application behavior). For every behavior-changing task below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file and has no unmet dependency
- **[Story]**: Traceability to US1, US2, or US3

---

## Phase 1: Foundational — Shared Test Infrastructure

**Purpose**: Give every user story's tests a single, shared way to capture log level and structured field values, instead of a second private duplicate of the one that already exists.

**⚠️ CRITICAL**: This is a non-behavioral refactor (same assertions, same pass/fail outcome as today) — it does not require a Red phase, but the full existing suite must stay green throughout.

- [X] T001 Promote the private `CapturingLogger<T>`/`LogEntry` nested in `backend/tests/LootSingles.IntegrationTests/Import/ParserExceptionSafetyTests.cs` into a shared `internal` type in `backend/tests/LootSingles.IntegrationTests/Import/ImportTestSupport.cs`, extending it to also record `LogLevel` and the structured `state` key/value pairs (not just the formatted message and exception), per research.md §6
- [X] T002 Update `backend/tests/LootSingles.IntegrationTests/Import/ParserExceptionSafetyTests.cs` to use the shared `ImportTestSupport.CapturingLogger<T>` instead of its own nested copy, keeping its existing assertions and passing behavior unchanged
- [X] T003 Run the full backend test suite and confirm it is unchanged and green (no behavior change from T001–T002)

**Checkpoint**: A shared, level-and-structure-aware capturing logger exists for every subsequent test task to reuse.

---

## Phase 2: User Story 1 - Diagnose a Failed or Problematic Import from Production Logs (Priority: P1) 🎯 MVP

**Goal**: Make the console log output identify, for any import attempt that could not fully succeed, what happened and why — without database access.

**Independent Test**: Trigger an unreadable-file import, an import with a rejected order, an import with an unexpected persistence failure, and a cancelled import; confirm each produces (or, for cancellation, does not produce) a distinct, correctly-leveled, correctly-identified log entry.

### Tests for User Story 1

- [X] T004 [P] [US1] Add a failing test asserting an unreadable/unparseable-file import (existing `ThrowingParser`/`corrupted-file.pdf`/`not-a-packing-slip.pdf` fixtures) produces exactly one `Warning`-level log entry, asserting the *structured state* key/value pairs on the captured entry (not the formatted message text) contain a non-zero `ImportId` and `FailureType.UnreadablePdf`, in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs` (new file)
- [X] T005 [P] [US1] Add a failing test asserting an import batch containing a rejected order (e.g., a duplicate-order re-import) produces exactly one `Warning`-level completion log entry, asserting the *structured state* key/value pairs contain a non-zero `ImportId`, detected/succeeded/failed counts, and a failure-type breakdown that includes a `DuplicateOrder` count, in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs`
- [X] T006 [P] [US1] Add a failing test that injects an `OrderPersistenceException` (reusing the `AtomicPersistenceTests` interceptor pattern) and asserts exactly one `Error`-level log entry, asserting the *structured state* key/value pairs contain a non-zero `ImportId`, the affected order's source identifier, and `FailureType.PersistenceFailure`, in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs`
- [X] T007 [P] [US1] Add a test asserting that cancelling an in-progress import (cancel the `CancellationToken` mid-batch, e.g. after the first `yield return`) produces **no** `Warning`- or `Error`-level log entry, per spec.md's cancellation edge case, in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs`. This is expected to pass immediately once T004–T006's test scaffolding exists, since `LogCompletion` (T010) is only ever reached at normal, non-exceptional completion points and an `OperationCanceledException` propagates past it — it exists to guard against a future regression, not to drive new production code
- [X] T008 [US1] Run T004–T007 and confirm T004–T006 fail for the expected reason (no log entry today for the duplicate/persistence-failure cases; the existing unreadable-file log lacks `ImportId` and uses exception-type text instead of `FailureType`) and T007 passes immediately (no regression to guard against yet)

### Implementation for User Story 1

- [X] T009 [US1] Add an eager `persistence.SaveChangesAsync(cancellationToken)` call immediately after `persistence.AddImportAttempt(attempt)` at the start of `ImportAsync` in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`, so `attempt.Id` is always a valid, non-zero identifier before any log statement, per research.md §3
- [X] T010 [US1] Add a private `LogCompletion(ImportAttempt attempt)` helper to `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs` implementing the unified decision rule from research.md §4/data-model.md (Warning with `FailureType` when `AttemptFailureCode` is set and no orders were evaluated; Warning with counts and a per-`FailureType` breakdown when any order failed or `AttemptFailureCode` is set; Information with counts otherwise), and call it after `CompleteAttemptAsync` at every terminal point in `ImportAsync` (the unreadable-file/no-order-blocks early returns and the normal end of the per-order loop), removing the old ad hoc `logger.LogWarning` call it replaces, to make T004 pass
- [X] T011 [US1] Add a `logger.LogError` call in the existing `catch (OrderPersistenceException)` branch of `ImportAsync` in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`, containing `ImportId` (`attempt.Id`), `block.OrderIdentifier`, and `FailureType.PersistenceFailure`, per research.md §5, to make T006 pass
- [X] T012 [US1] Update the one content assertion in `backend/tests/LootSingles.IntegrationTests/Import/ParserExceptionSafetyTests.cs` that referenced the old exception-type-name message text to instead assert `LogLevel.Warning`, the attempt's `ImportId`, and `FailureType.UnreadablePdf` from the captured entry's structured state, per research.md §6, keeping its existing "no secret content" assertion
- [X] T013 [US1] Run T004–T007 and T012, confirm all pass, and run the full backend test suite to confirm no other existing test regressed

**Checkpoint**: All three failure/diagnosis scenarios plus the cancellation non-log guarantee in User Story 1 are covered by passing tests.

---

## Phase 3: User Story 2 - Confirm the System Is Healthy from Steady-State Logs (Priority: P2)

**Goal**: A fully successful import also produces a concise, positive log entry.

**Independent Test**: Perform a fully successful import and confirm exactly one Information-level entry appears, with no other log noise from this feature.

### Tests for User Story 2

- [ ] T014 [US2] Add a test asserting a fully successful multi-order import (e.g., `valid-multi-order-batch.pdf`) produces exactly one `Information`-level log entry, asserting the structured state contains a non-zero `ImportId` and the detected/succeeded counts, and that no `Warning`/`Error` entry is produced, in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs`. This test is expected to pass immediately once Phase 2 (T009–T011) lands, since `LogCompletion`'s Information branch is implemented as part of that same helper — it exists to give User Story 2 its own independent acceptance verification, not to drive additional production code

**Checkpoint**: Both the failure-diagnosis (US1) and steady-state-confirmation (US2) log behaviors are covered by passing tests.

---

## Phase 4: User Story 3 - Trust That Production Logs Never Leak Sensitive Data (Priority: P1)

**Goal**: Every log entry this feature adds is verified to contain only safe, structured identifiers — never customer PII, packing-slip content, or secrets.

**Independent Test**: Inspect every log entry captured across the US1/US2 scenarios and confirm none contains a customer name, address, packing-slip content, password, PIN, token, or connection string.

### Tests for User Story 3

- [ ] T015 [US3] Extend the T004–T007 and T014 tests in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs` with a shared, distinctive fixture-content marker (a product/customer-like string that would appear in raw packing-slip text but never in a safe identifier) and assert it never appears in any captured log entry's message or structured values, alongside the existing PIN/connection-string safety coverage already provided by `DatabaseConfigurationTests`/`BootstrapAdminCommandTests` for unrelated log paths

**Checkpoint**: All three user stories are implemented and independently verified.

---

## Phase 5: Polish & Cross-Cutting Validation

**Purpose**: Confirm proportionality and delivery-mechanism correctness, run full regression, and complete manual validation.

- [X] T016 [P] Document the production-logging standard as a durable practice for all future features: extend constitution Principle XI (`.specify/memory/constitution.md`, now v3.4.0) and add a Definition of Done checkpoint to `CLAUDE.md` — completed 2026-08-23, ahead of this task breakdown, at the Developer's request during this feature's planning
- [ ] T017 [P] Confirm no new logging package or alternate log provider was introduced: grep every `backend/**/*.csproj` for a disallowed logging package name (Serilog, Application Insights/`Microsoft.ApplicationInsights`, Log Analytics, Seq, Datadog) and confirm zero matches, and confirm `backend/src/LootSingles.Api/Program.cs` contains no `ClearProviders()`/`AddProvider()`/third-party logging registration beyond the ASP.NET Core defaults, recording the result in `specs/006-production-logging/validation-results.md`, per FR-001/FR-002
- [ ] T018 [P] Add a test that resolves the real, DI-configured `ILogger<PackingSlipImportService>` from a `WebApplicationFactory`-hosted instance of `LootSingles.Api` (reusing the `AuthWebApplicationFactory` pattern) and asserts `IsEnabled(LogLevel.Information)`, `IsEnabled(LogLevel.Warning)`, and `IsEnabled(LogLevel.Error)` are all `true` under the application's actual `appsettings.json` configuration, in `backend/tests/LootSingles.IntegrationTests/Import/ImportLoggingTests.cs`, per FR-011
- [ ] T019 Import a 200-order synthetic batch (reusing the `SyntheticProgressiveParser` pattern from `ProgressReportingTests`) and confirm exactly one attempt-level log entry is produced regardless of batch size, satisfying SC-003 and FR-009's proportionality requirement
- [ ] T020 Run backend build, full unit suite, full SQL Server integration suite, and CSharpier; confirm all existing tests remain green (SC-004) and record results in `specs/006-production-logging/validation-results.md`
- [ ] T021 Perform the quickstart.md manual validation (run the API locally, exercise the three logged outcomes through the frontend or direct HTTP calls, confirm console output matches expectations and contains no unsafe content) and record observations in `specs/006-production-logging/validation-results.md`
- [ ] T022 Perform the constitution Architecture and Changeability Review against the implemented boundaries, capture any Must Fix findings as new tasks in `specs/006-production-logging/tasks.md`, and do not proceed to convergence until resolved

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (Foundational) has no dependency and blocks all user story test tasks (they all use the shared capturing logger).
- US1 (Phase 2) depends only on Phase 1.
- US2 (Phase 3) depends on Phase 1 and, in practice, on US1's implementation (T009–T011) existing, since they share one `LogCompletion` helper — but US2's test is written and run independently, per its own acceptance scenario.
- US3 (Phase 4) depends on the log entries already produced by US1 (Phase 2) and US2 (Phase 3), since it only adds assertions to those same entries.
- Phase 5 (Polish) depends on all three user stories being complete.

### Red-Green ordering

- T001–T003 → (non-behavioral, no Red phase)
- T004–T008 → T009–T012 → T013
- T014 → (expected Green immediately; see task note)
- T015 → (extends existing passing tests with additional assertions)
- T018 → (a new, independent check against the real DI/configuration pipeline; not coupled to T004–T015's fake-logger tests)

### Parallel opportunities

- T004, T005, T006, and T007 are separate failing-test additions to the same new file and can be authored in parallel, then run together.
- T016, T017, and T018 are independent of each other and of T019–T022.

## Implementation Strategy

### MVP first

1. Complete Phase 1 (shared test infrastructure).
2. Complete Phase 2 (User Story 1) — this alone delivers the feature's primary value: diagnosing failed imports from console logs.
3. Stop and validate: the three failure/diagnosis scenarios (plus the cancellation non-log guarantee) are logged correctly.

### Incremental delivery

1. US1 makes failed/problematic imports diagnosable from logs alone.
2. US2 adds steady-state confirmation for successful imports (mostly free, from US1's shared implementation).
3. US3 verifies the safety property that governs every log statement added by US1 and US2.
4. Polish proves proportionality, real delivery-mechanism correctness, zero regressions, and manual validation.

## Notes

- Do not add a custom logging abstraction (e.g. `ILoggingService`), a third-party or paid logging package, or any new configuration section — none is needed (research.md §1–2; verified by T017).
- Every task that touches `ImportAsync` must avoid logging customer name/address, raw packing-slip content, or secrets (constitution Principle XI, as amended; FR-008).
- Assert against captured log entries' *structured state* (the named key/value pairs `ImportTestSupport.CapturingLogger<T>` exposes per T001), not the formatted message string — this is what actually proves FR-007's structured-logging requirement, not just that a value appears somewhere in free text.
