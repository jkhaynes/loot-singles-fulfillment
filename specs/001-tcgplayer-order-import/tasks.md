---

description: "Task list for TCGplayer Packing Slip Order Import"
---

# Tasks: TCGplayer Packing Slip Order Import

**Input**: Design documents from `/specs/001-tcgplayer-order-import/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/import-service.md, quickstart.md

**Tests**: Included and REQUIRED, not optional — constitution Principle IV requires Red → Green → Refactor test-first coverage for all new or modified application behavior, not only a named list of high-risk behaviors. Write each test, watch it fail, then implement. Tasks T001-T010 (Setup and foundational scaffolding/POCOs) fall under Principle IV's stated exception for work with no meaningful executable behavior to test — see the Task Status Reconciliation note below.

**Organization**: Tasks are grouped by user story (spec.md priorities: US1 = P1, US2 = P1, US3 = P2) so each can be built and validated as an increment. This feature is one linear pipeline (parse → validate → persist), so stories layer on the same code rather than touching disjoint files — later stories mostly *add* new code paths (rejection handling, PII/file-retention guarantees) to what US1 establishes, and are still independently testable per their own acceptance scenarios in spec.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, or US3 — user-story phase tasks only

## Path Conventions

Per plan.md's Project Structure — this is the first feature with any code, so Phase 1 creates the layout:

```text
backend/src/LootSingles.Domain/Orders/
backend/src/LootSingles.Application/Import/
backend/src/LootSingles.Infrastructure/Import/
backend/src/LootSingles.Infrastructure/Persistence/
backend/src/LootSingles.Api/
backend/tests/LootSingles.UnitTests/Import/
backend/tests/LootSingles.IntegrationTests/Import/
backend/tests/LootSingles.Fixtures/PackingSlips/
```

---

## Task Status Reconciliation (2026-08-20 Spec-Kit-only migration)

Tasks T001-T010 were implemented and independently re-verified against real evidence (file contents, `dotnet build`, `dotnet test`) during this migration — not merely trusted from the prior (Superpowers-era, since-removed) execution process's own conclusions — and are marked `[X]` below. Commit ranges: T001 `fdda8c4`, T002 `7fc0fa1`, T003 `e7ac3b5`, T004 `9e80f33`, T005 `87fc51c`, T006 `873495f`+fix `26e4118`, T007 `57c7ce3`, T008 `c2bc3e9`, T009 `9146e95`, T010 `e4be3a3`.

No test-remediation tasks are added for T001-T010, for the following reasons, stated explicitly per constitution Principle IV's exception clause rather than silently applied:

- **T001-T004** (solution/project scaffolding, package references, `.editorconfig`, fixtures folder structure): static configuration/repository-metadata — an explicit Principle IV exception, since no executable behavior exists to test.
- **T005-T009** (`Order`, `OrderLine`, `FailureType`, `ImportAttempt`, `ImportOrderResult`): plain data-holding POCOs/enum with no conditional logic, computed properties, or invariant enforcement — there is no behavior yet to write a meaningful Red→Green→Refactor test against. Their real behavior (validation, persistence) is exercised by T017+ and T029+'s tests once that logic exists.
- **T010** (`LootSinglesDbContext`): declares `DbSet`s and calls `ApplyConfigurationsFromAssembly`; it has no testable behavior of its own until T011-T013 add entity configurations and a real migration — those tasks' own already-correct test-before-implementation ordering covers it.

T011 onward were **not** implemented as of this migration (verified via `git log` and `dotnet build`/`dotnet test` against the working tree — no commits or code exist past T010) and retain their original test-before-implementation ordering, which already satisfies the constitution's universal (not high-risk-behavior-scoped) Principle IV. No task reordering was needed for T011-T053 as part of this migration.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Stand up the solution — no code from this repo exists yet.

- [X] T001 Create `backend/` solution `LootSingles.sln` and the five projects (`LootSingles.Domain`, `LootSingles.Application`, `LootSingles.Infrastructure`, `LootSingles.Api`, and under `backend/tests/`: `LootSingles.UnitTests`, `LootSingles.IntegrationTests`), targeting .NET 8, wired with correct project references (Domain ← Application ← Infrastructure ← Api; test projects reference Application/Infrastructure)
- [X] T002 Add NuGet packages: `UglyToad.PdfPig` to `LootSingles.Infrastructure`; `Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.EntityFrameworkCore.Design` to `LootSingles.Infrastructure`; `xunit`, `xunit.runner.visualstudio`, `Microsoft.EntityFrameworkCore.InMemory` (or a SQL Server test container package) to both test projects
- [X] T003 [P] Add `.editorconfig` and enable nullable reference types + warnings-as-errors for all `backend/src/*` projects
- [X] T004 [P] Create `backend/tests/LootSingles.Fixtures/` project/folder structure with a `PackingSlips/` subfolder and a README noting fixtures must be sanitized (no real customer data)

**Checkpoint**: `dotnet build` succeeds on an empty solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, entities, and the low-level parsing engine every user story's tests exercise. No user story work starts until this is done.

**⚠️ CRITICAL**: Blocks all of Phase 3+.

- [X] T005 [P] Create `Order` entity in `backend/src/LootSingles.Domain/Orders/Order.cs` per data-model.md (TcgplayerOrderId, Status, ImportedAt; no customer-PII fields exist on this type at all — FR-009)
- [X] T006 [P] Create `OrderLine` entity in `backend/src/LootSingles.Domain/Orders/OrderLine.cs` per data-model.md (RawDescription, ProductLine, ProductName, Set, CollectorNumber, Rarity, Condition, Variant, Quantity)
- [X] T007 [P] Create `FailureType` enum in `backend/src/LootSingles.Application/Import/FailureType.cs` with all 8 codes from spec FR-006 (`MissingOrderIdentifier`, `NoProductLines`, `InvalidQuantity`, `MissingProductName`, `MissingSet`, `MissingCollectorNumber`, `MissingCondition`, `DuplicateOrder`, `SummaryMismatch`, `UnreadablePdf`)
- [X] T008 [P] Create `ImportAttempt` entity in `backend/src/LootSingles.Application/Import/ImportAttempt.cs` per data-model.md (StartedAt, CompletedAt, AttemptFailureCode, AttemptFailureMessage)
- [X] T009 [P] Create `ImportOrderResult` entity in `backend/src/LootSingles.Application/Import/ImportOrderResult.cs` per data-model.md (ImportAttemptId, SourceOrderIdentifier, Outcome, FailureCode, FailureMessage, ResultingOrderId)
- [X] T010 Create `LootSinglesDbContext` in `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs` with `DbSet`s for all four entities
- [X] T011 [P] Create EF Core entity configuration in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/OrderConfiguration.cs`, including a **unique index on `TcgplayerOrderId`** (FR-008 database-enforced guarantee)
- [X] T012 [P] Create EF Core entity configurations for `OrderLine`, `ImportAttempt`, `ImportOrderResult` in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/`
- [X] T013 Generate the initial EF Core migration and verify `dotnet ef database update` succeeds against a local SQL Server instance
- [X] T014 Define `IPackingSlipParser` in `backend/src/LootSingles.Application/Import/IPackingSlipParser.cs` — the replaceable-integration seam (Constitution IX): given a PDF stream, yields raw per-order page data (order-identifier line, line-item table rows, and whether a trailing summary/index page was found)
- [X] T015 Implement `PdfPigPackingSlipParser` in `backend/src/LootSingles.Infrastructure/Import/PdfPigPackingSlipParser.cs` implementing `IPackingSlipParser` — per-page text extraction, `Order Number:` anchor detection (research.md §2), line-item table row extraction, and summary/index page detection
- [X] T016 [P] Add the real example packing slip (13 orders + summary page) as `backend/tests/LootSingles.Fixtures/PackingSlips/valid-multi-order-batch.pdf`, sanitized if any field still resembles real customer data
- [X] T017 Write a failing unit test in `backend/tests/LootSingles.UnitTests/Import/PdfPigPackingSlipParserTests.cs` asserting the parser splits `valid-multi-order-batch.pdf` into exactly 13 per-order page blocks plus the detected summary/index list, then implement T015 to make it pass

**Checkpoint**: Schema exists, migrations apply, and the parser can split a real multi-order file into per-order raw blocks. No field-level validation or persistence yet — that's what the user stories add.

---

## Phase 3: User Story 1 - Import a Valid Packing Slip (Priority: P1) 🎯 MVP

**Goal**: A valid packing slip's orders and order lines are parsed, validated, and persisted exactly as described, with quantity preserved exactly and condition/variant correctly disambiguated.

**Independent Test**: Import `valid-multi-order-batch.pdf` end-to-end; assert 13 `Order`s exist, each with correct `OrderLine`s (fields, quantities, raw description) matching the source.

### Tests for User Story 1

> Write these first; confirm each fails for the right reason before implementing.

- [X] T018 [P] [US1] Failing unit test for condition/variant disambiguation in `backend/tests/LootSingles.UnitTests/Import/ConditionVariantParserTests.cs` covering all FR-018 examples ("Near Mint Holofoil", "Near Mint Foil", "Lightly Played Foil", "Near Mint Reverse Holofoil", "Near Mint" alone, and a parenthetical marker like "(Showcase)" combined with a condition suffix)
- [X] T019 [P] [US1] Failing unit tests for field extraction in `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs` — given representative real description lines (including colon-heavy set names, hyphenated product names, parenthetical variants, and multiple games), correct ProductLine/ProductName/Set/CollectorNumber/Rarity/Condition/Variant/Quantity are extracted
- [X] T020 [P] [US1] Failing unit test asserting `RawDescription` exactly matches the parser's extracted text for that line (no truncation/substitution), alongside the separately-parsed fields (FR-017), in `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`
- [X] T021 [P] [US1] Add a `duplicate-product-line-same-order.pdf` fixture alongside `valid-multi-order-batch.pdf`, then write a failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/ValidImportTests.cs`: import both fixtures and compare every persisted order line against independently authored expected fixture data field-by-field (RawDescription, ProductLine, ProductName, Set, CollectorNumber, Rarity, Condition, Variant, Quantity), while also asserting all `Order`s have `Status = Ready`, no lines are added or dropped, and duplicate product lines remain distinct (FR-002, FR-003, FR-004, FR-017, FR-018)

### Implementation for User Story 1

- [X] T022 [US1] Implement `ConditionVariantParser` in `backend/src/LootSingles.Application/Import/ConditionVariantParser.cs` (research.md §3: known condition-prefix list, remainder + parenthetical markers → Variant) to pass T018
- [X] T023 [US1] Implement field-extraction logic (product line/game, product name, set, collector number, rarity) in `backend/src/LootSingles.Application/Import/OrderLineExtractor.cs`, using `ConditionVariantParser`, to pass T019/T020
- [X] T024 [US1] Define `IPackingSlipImportService` in `backend/src/LootSingles.Application/Import/IPackingSlipImportService.cs` and `ImportProgressUpdate` per contracts/import-service.md
- [X] T025 [US1] Implement `PackingSlipImportService` in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`: for each order block from `IPackingSlipParser`, extract fields, and — for this story's happy path — persist `Order` + all its `OrderLine`s in one `SaveChangesAsync()` call per order (FR-016 atomicity boundary), yielding an `ImportProgressUpdate` after each order
- [X] T026 [US1] Wire DI registration for `IPackingSlipParser` → `PdfPigPackingSlipParser`, `IPackingSlipImportService` → `PackingSlipImportService`, and `LootSinglesDbContext` in `backend/src/LootSingles.Api/Program.cs`
- [X] T027 [US1] Run T021 against the full wired pipeline (T022–T026) and confirm it passes

**Checkpoint**: A clean, valid packing slip round-trips end-to-end. This is the deployable MVP slice — US2 and US3 add safety guarantees on top.

---

## Phase 4: User Story 2 - Reject an Unparseable Packing Slip (Priority: P1)

**Goal**: Malformed data — at the order, line, or whole-file level — is rejected with a specific reason and never produces a partial `Order`; duplicate imports are rejected, including under a concurrent race.

**Independent Test**: Import fixtures engineered per spec.md's malformed cases individually; assert no `Order`/`OrderLine` is created for any of them and each rejection carries the correct `FailureCode`.

### Tests for User Story 2

- [X] T028 [P] [US2] Add malformed fixtures to `backend/tests/LootSingles.Fixtures/PackingSlips/`: `missing-order-identifier.pdf`, `zero-product-lines.pdf`, `invalid-quantity.pdf` (blank/zero/negative/non-numeric variants), `missing-product-name.pdf`, `missing-set.pdf`, `missing-collector-number.pdf`, `missing-condition.pdf`, `corrupted-file.pdf` (not a valid PDF), `partial-batch-one-bad-order.pdf` (multiple orders, one with a missing identifier)
- [X] T029 [P] [US2] Failing unit tests in `backend/tests/LootSingles.UnitTests/Import/OrderValidationTests.cs` — each required-field-missing case maps to its FR-006 `FailureType`; rarity/variant absence does **not** fail validation (FR-005)
- [X] T030 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/RejectionTests.cs` covering each malformed per-order fixture: no `Order`/`OrderLine` created, `ImportOrderResult.Outcome = Rejected` with the correct `FailureCode` and a specific (non-generic) message (FR-006, FR-007)
- [X] T031 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/RejectionTests.cs` for `corrupted-file.pdf`: `ImportAttempt.AttemptFailureCode = UnreadablePdf`, zero `ImportOrderResult`s (FR-015)
- [X] T032 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/PartialImportTests.cs` for `partial-batch-one-bad-order.pdf`: valid orders succeed and persist while only the bad order is rejected (FR-005)
- [X] T033 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/DuplicateOrderTests.cs`: import `valid-multi-order-batch.pdf` twice; second import's matching orders report `Rejected`/`DuplicateOrder`, first import's `Order`/`OrderLine` rows unchanged (FR-008)
- [X] T034 [US2] Failing concurrency integration test in `backend/tests/LootSingles.IntegrationTests/Import/DuplicateOrderTests.cs`: fire two concurrent `ImportAsync` calls for the same new order identifier; assert exactly one `Order` row exists afterward and the loser reports `DuplicateOrder` (not an unhandled exception) — proves the DB unique constraint, not just the pre-check, is what closes the race (FR-008, SC-007)
- [X] T035 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/RejectionTests.cs` using a manually-edited copy of the valid fixture with an altered summary/index page (an order id added or removed from the list): `ImportAttempt.AttemptFailureCode = SummaryMismatch` (FR-013)

### Implementation for User Story 2

- [X] T036 [US2] Add required-field validation (product name, set, collector number, condition, quantity-is-positive-whole-number) to the extraction/validation path from US1, returning a `FailureType` instead of throwing, to pass T029/T030
- [X] T037 [US2] Add whole-file-unreadable handling to `PdfPigPackingSlipParser`/`PackingSlipImportService`: catch unreadable/non-PDF input, set `ImportAttempt.AttemptFailureCode = UnreadablePdf`, short-circuit with zero `ImportOrderResult`s, to pass T031
- [X] T038 [US2] Confirm/adjust `PackingSlipImportService`'s per-order loop already satisfies partial import (T032) — each order's failure must not affect sibling orders in the same batch
- [X] T039 [US2] Add duplicate-order handling to `PackingSlipImportService`: pre-check by `TcgplayerOrderId` before insert, and catch the unique-constraint `DbUpdateException` on `SaveChangesAsync()` as the race-loss signal, both mapping to `DuplicateOrder` (research.md §6), to pass T033/T034
- [X] T040 [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/AtomicPersistenceTests.cs`: using a test double/interceptor that throws after an `Order` row is inserted but before its `OrderLine` rows are saved, import a valid order and assert zero rows exist for that order afterward and its `ImportOrderResult` reflects the failure — proving the guarantee holds for a genuine mid-write failure, not just "validation rejected it before any write was attempted" (FR-016)
- [X] T041 [US2] Add the fault-injection seam `PackingSlipImportService` needs for T040 (e.g., a test-only `SaveChanges` interceptor), and confirm the single-`SaveChangesAsync()`-per-order design (T025) already makes the failure atomic via EF Core's implicit transaction — adjust only if T040 reveals otherwise
- [X] T042 [US2] Add summary/index cross-check to `PdfPigPackingSlipParser`/`PackingSlipImportService`: compare the parsed order-identifier set against the detected summary page list, setting `AttemptFailureCode = SummaryMismatch` on discrepancy, to pass T035
- [X] T043 [US2] Ensure `FailureMessage` text is specific per `FailureType` (identifies the affected order and/or product line, per FR-006) across all rejection paths

**Checkpoint**: The feature is now safe to point at real, messy packing slips — US1's happy path plus US2's defensive rejection together satisfy spec.md's "must exist alongside the happy path before real fulfillment" requirement for US2.

---

## Phase 5: User Story 3 - Keep Customer Shipping Information Out of Imported Records (Priority: P2)

**Goal**: No customer shipping PII, and no copy of the source PDF itself, survives an import — success or failure.

**Independent Test**: Import a fixture containing a customer's shipping name/address; inspect persisted data and any temp/durable storage afterward.

### Tests for User Story 3

- [x] T044 [P] [US3] Failing unit test in `backend/tests/LootSingles.UnitTests/Import/PiiExclusionTests.cs` asserting no type in `LootSingles.Domain`/`LootSingles.Application` exposes a settable field for customer name/address/contact (a structural/reflection-based guard, so a future accidental addition fails loudly)
- [x] T045 [P] [US3] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/PiiRetentionTests.cs`: import `valid-multi-order-batch.pdf` (contains real-shaped shipping names/addresses per order), then query all persisted `Order`/`OrderLine` rows and assert none contain any of the source file's customer names or address fragments (FR-009, SC-004)
- [x] T046 [P] [US3] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/PiiRetentionTests.cs`: after both a successful import and a rejected import (`corrupted-file.pdf`), scan the process's temp directory and any configured blob/file storage for a copy of the source PDF bytes — assert none exists (FR-019)

### Implementation for User Story 3

- [x] T047 [US3] Audit `PdfPigPackingSlipParser` and `PackingSlipImportService` to confirm the "Ship To"/customer block is never mapped into any DTO or entity — parsing only ever reads the order-identifier and line-item table regions; adjust if T044/T045 reveal otherwise
- [x] T048 [US3] Confirm the import pipeline holds the PDF only as an in-memory `Stream`/byte buffer passed directly to the parser, with no `File.Write`/blob-upload call anywhere in this feature's code, to pass T046 (research.md §7)

**Checkpoint**: All three user stories pass together — spec.md's full acceptance-scenario set for this feature is satisfied.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation against the spec as a whole, not tied to one story.

- [x] T049 [P] Progress-reporting integration test in `backend/tests/LootSingles.IntegrationTests/Import/ProgressReportingTests.cs`: enumerate `ImportAsync` updates for `valid-multi-order-batch.pdf` and assert `OrdersProcessed`/`SucceededCount`/`FailedCount` increase monotonically and the final update has `IsComplete = true` (FR-014, SC-006)
- [x] T050 [P] Run quickstart.md's full validation-scenario list end-to-end and record results
- [x] T051 [P] Review all `FailureMessage` strings for clarity (a human unfamiliar with the code can tell what to fix from the message alone)
- [x] T052 Confirm no NuGet package pulled in during T002 introduces a licensing conflict (research.md §1 — PdfPig is Apache-2.0; verify no transitive AGPL/commercial dependency was added)
- [x] T053 Re-run the full test suite (`dotnet test`) and confirm 0 failures, output pristine

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. **Blocks all of Phase 3+.**
- **US1 (Phase 3)**: Depends on Phase 2 only.
- **US2 (Phase 4)**: Depends on Phase 2; in practice also builds on US1's extraction/persistence code (T022–T026), since US2 adds rejection branches to that same pipeline rather than separate files.
- **US3 (Phase 5)**: Depends on Phase 2; builds on US1's pipeline (audits/confirms it, per T047–T048) and is independent of US2's rejection-path additions.
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 all being complete.

### Parallel Opportunities

- T005–T009 (entity/enum creation) are all `[P]` — different files.
- T011–T012 (EF configurations) are `[P]` once T010 exists.
- Within US2, T028–T035 (fixtures + tests) are `[P]` — write them together before starting T036.
- Within US3, T044–T046 are `[P]`.
- US2 and US3 implementation could proceed in parallel by different developers once US1 (Phase 3) is checkpointed, since they touch mostly-disjoint concerns (rejection logic vs. PII/file-retention audit) — though both read the same `PackingSlipImportService` file, so coordinate if working simultaneously.

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1).
2. **STOP and VALIDATE**: run T021, confirm the valid multi-order fixture round-trips correctly.
3. This is a real MVP in the sense of "the pipeline works," but per spec.md's own framing, US2's defensive rejection is required "before the feature can be used for real fulfillment" — don't treat Phase 3 alone as production-ready.

### Incremental Delivery

1. Setup + Foundational → schema and parser exist.
2. US1 → happy-path import works, demoable against real fixtures.
3. US2 → safe to run against real, messy packing slips without risk of bad data.
4. US3 → privacy guarantee verified, safe to run against files containing real customer data.
5. Polish → full spec validated end-to-end.

## Phase 7: Convergence

- [x] T054 CRITICAL Replace duplicate-key detection based on `DbUpdateException.ToString()` in `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs` with provider-specific typed error-code inspection for SQL Server and SQLite, retaining the `DuplicateOrder` race outcome and adding regression coverage per Constitution IX/XII (contradicts)
- [x] T055 Add a valid-PDF/non-packing-slip fixture and a failing integration test, then reject a parsed document with zero recognizable order blocks as an attempt-wide `UnreadablePdf` result with a specific message and no persisted Orders per FR-015 and US2/AC1 (missing)
- [x] T056 Refactor parsing/import orchestration so `ImportAsync` yields discovery/processing progress before the entire document has been scanned, with `OrdersDetected` growing as pages are read and a ~200-order synthetic regression test proving updates remain observable throughout processing per FR-014, SC-006, and `contracts/import-service.md` (partial)
- [x] T057 Correct the PdfPig license attribution from MIT to Apache-2.0 in the feature research and task-validation documentation while preserving the verified no-conflict conclusion per T052 (partial)
- [x] T058 Remove the empty template `Class1.cs` production types and always-passing `UnitTest1.cs` test placeholders, then run the real test suite to confirm no coverage or behavior depended on them per plan: project structure and Constitution II (unrequested)

## Phase 8: Convergence

- [X] T059 Reconcile the persistence-failure exception-handling discrepancy between `contracts/import-service.md` (which states exceptions are reserved for genuine, non-duplicate infrastructure failures) and the implemented behavior in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`/`backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs`, which catches every `OrderPersistenceException` — duplicate or not — and reports it as a per-order `Rejected`/`PersistenceFailure` `ImportOrderResult` instead of letting non-duplicate failures propagate; update the contract doc to match the tested behavior, or change the code to match the documented contract, per contracts/import-service.md (contradicts) — resolved by changing the code: `LootSinglesDbContext.SaveChangesAsync` now only catches `DbUpdateException` when `IsDuplicateKeyViolation` is true (via an exception filter), wrapping it as `OrderPersistenceException` solely for the duplicate case; any other `DbUpdateException` now propagates unmodified out of `ImportAsync`, matching the existing contract text. `FailureType.PersistenceFailure` and `OrderPersistenceException.IsDuplicate` were removed as now-dead/redundant. `AtomicPersistenceTests.cs` updated (Red→Green) to assert exception propagation instead of a typed rejection; full suite green (46/46)
- [X] T060 Correct the functional-requirement references in `backend/src/LootSingles.Application/Import/FailureType.cs` XML doc comments — `NoProductLines`, `InvalidQuantity`, `MissingProductName`, `MissingSet`, `MissingCollectorNumber`, `MissingCondition` are documented as "(FR-008)" but are defined by FR-005, and `DuplicateOrder` was documented as "(FR-005)" but is defined by FR-008 (duplicate detection) — per FR-006 (partial)

## Phase 9: Code and Design Review (`/code-design-review`)

- [X] T061 MUST FIX: Revert non-duplicate persistence failures to a typed per-order `FailureType.PersistenceFailure` rejection instead of a propagated exception, matching the now-corrected `contracts/import-service.md`, `research.md` §5, and `data-model.md` (which T059 left inconsistent with — see plan.md's "Post-convergence correction" note). In `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs`, catch every `DbUpdateException` from `SaveChangesAsync` (not only duplicate-key ones via the `IsDuplicateKeyViolation` filter) and wrap it in `OrderPersistenceException`, restoring a way to distinguish the duplicate case from other persistence failures (e.g. reinstate `OrderPersistenceException.IsDuplicate`). In `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`, map the duplicate case to `FailureType.DuplicateOrder` and any other case to a restored `FailureType.PersistenceFailure`, continuing the per-order loop in both cases. Revert `backend/tests/LootSingles.IntegrationTests/Import/AtomicPersistenceTests.cs`'s two tests to assert a typed rejection again (Red→Green). Add a new regression test that combines a multi-order fixture with a persistence-failure interceptor on one specific order and asserts sibling orders still succeed and persist — the coverage gap that let this regression through undetected — per FR-016 (contradicts) — resolved: `OrderPersistenceException.IsDuplicate` and `FailureType.PersistenceFailure` restored; `LootSinglesDbContext.SaveChangesAsync` again catches every `DbUpdateException` and classifies it; `PackingSlipImportService` maps duplicate → `DuplicateOrder`, other → `PersistenceFailure`, both continuing the loop. Added `ImportAsync_PersistenceFailureOnOneOrder_StillPersistsSiblingOrdersInSameBatch` using a new `FailSpecificOrderLineInsertInterceptor` targeting one order ("Orim's Chant"/F0000010) within the 13-order `valid-multi-order-batch.pdf` fixture — asserts the other 12 orders still succeed and persist while only the targeted order is rejected. Full suite green (47/47, up from 46 — one new test)
- [X] T062 ADVISORY: Assess whether `OrderLineExtractor`'s `setAndProduct.LastIndexOf(':')` "last colon wins" heuristic (`backend/src/LootSingles.Application/Import/OrderLineExtractor.cs`) could silently misparse a product name that itself contains a colon (no current fixture evidence this occurs); if confirmed against real TCGplayer data, add a defensive check that rejects rather than guesses, per Constitution V (advisory) — assessed: no evidence in any current fixture (Pokémon/Magic/Lorcana) of a real card/product name containing a colon; per the task's own conditional framing, no code change is warranted without that confirmation. No action taken; revisit if a real occurrence is observed
- [X] T063 ADVISORY: Consider extracting the parse-consumption loop (`ParseAsync` enumeration/attempt-completion tracking) out of `PackingSlipImportService.ImportAsync` (`backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`) into a separate helper for readability — the method currently mixes that concern with per-order validation/persistence orchestration in one ~120-line method (advisory) — assessed: `ImportAsync` is a C# async iterator (`yield return` throughout); extracting the parse-consumption segment would require the helper to itself be an `IAsyncEnumerable` iterator, splitting cancellation-token and enumerator-disposal handling across two coupled methods for a purely cosmetic gain in an already well-tested method. Per Constitution XIII (no refactor without a concrete benefit), left as-is
