---

description: "Task list for TCGplayer Packing Slip Order Import"
---

# Tasks: TCGplayer Packing Slip Order Import

**Input**: Design documents from `/specs/001-tcgplayer-order-import/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/import-service.md, quickstart.md

**Tests**: Included and REQUIRED, not optional — the constitution (Principle IV) names quantity preservation and safe/defensive parser failure, both central to this feature, as high-risk behaviors requiring test-first coverage. Write each test, watch it fail, then implement.

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

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Stand up the solution — no code from this repo exists yet.

- [ ] T001 Create `backend/` solution `LootSingles.sln` and the five projects (`LootSingles.Domain`, `LootSingles.Application`, `LootSingles.Infrastructure`, `LootSingles.Api`, and under `backend/tests/`: `LootSingles.UnitTests`, `LootSingles.IntegrationTests`), targeting .NET 8, wired with correct project references (Domain ← Application ← Infrastructure ← Api; test projects reference Application/Infrastructure)
- [ ] T002 Add NuGet packages: `UglyToad.PdfPig` to `LootSingles.Infrastructure`; `Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.EntityFrameworkCore.Design` to `LootSingles.Infrastructure`; `xunit`, `xunit.runner.visualstudio`, `Microsoft.EntityFrameworkCore.InMemory` (or a SQL Server test container package) to both test projects
- [ ] T003 [P] Add `.editorconfig` and enable nullable reference types + warnings-as-errors for all `backend/src/*` projects
- [ ] T004 [P] Create `backend/tests/LootSingles.Fixtures/` project/folder structure with a `PackingSlips/` subfolder and a README noting fixtures must be sanitized (no real customer data)

**Checkpoint**: `dotnet build` succeeds on an empty solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, entities, and the low-level parsing engine every user story's tests exercise. No user story work starts until this is done.

**⚠️ CRITICAL**: Blocks all of Phase 3+.

- [ ] T005 [P] Create `Order` entity in `backend/src/LootSingles.Domain/Orders/Order.cs` per data-model.md (TcgplayerOrderId, Status, ImportedAt; no customer-PII fields exist on this type at all — FR-009)
- [ ] T006 [P] Create `OrderLine` entity in `backend/src/LootSingles.Domain/Orders/OrderLine.cs` per data-model.md (RawDescription, ProductLine, ProductName, Set, CollectorNumber, Rarity, Condition, Variant, Quantity)
- [ ] T007 [P] Create `FailureType` enum in `backend/src/LootSingles.Application/Import/FailureType.cs` with all 8 codes from spec FR-006 (`MissingOrderIdentifier`, `NoProductLines`, `InvalidQuantity`, `MissingProductName`, `MissingSet`, `MissingCollectorNumber`, `MissingCondition`, `DuplicateOrder`, `SummaryMismatch`, `UnreadablePdf`)
- [ ] T008 [P] Create `ImportAttempt` entity in `backend/src/LootSingles.Application/Import/ImportAttempt.cs` per data-model.md (StartedAt, CompletedAt, AttemptFailureCode, AttemptFailureMessage)
- [ ] T009 [P] Create `ImportOrderResult` entity in `backend/src/LootSingles.Application/Import/ImportOrderResult.cs` per data-model.md (ImportAttemptId, SourceOrderIdentifier, Outcome, FailureCode, FailureMessage, ResultingOrderId)
- [ ] T010 Create `LootSinglesDbContext` in `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs` with `DbSet`s for all four entities
- [ ] T011 [P] Create EF Core entity configuration in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/OrderConfiguration.cs`, including a **unique index on `TcgplayerOrderId`** (FR-008 database-enforced guarantee)
- [ ] T012 [P] Create EF Core entity configurations for `OrderLine`, `ImportAttempt`, `ImportOrderResult` in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/`
- [ ] T013 Generate the initial EF Core migration and verify `dotnet ef database update` succeeds against a local SQL Server instance
- [ ] T014 Define `IPackingSlipParser` in `backend/src/LootSingles.Application/Import/IPackingSlipParser.cs` — the replaceable-integration seam (Constitution IX): given a PDF stream, yields raw per-order page data (order-identifier line, line-item table rows, and whether a trailing summary/index page was found)
- [ ] T015 Implement `PdfPigPackingSlipParser` in `backend/src/LootSingles.Infrastructure/Import/PdfPigPackingSlipParser.cs` implementing `IPackingSlipParser` — per-page text extraction, `Order Number:` anchor detection (research.md §2), line-item table row extraction, and summary/index page detection
- [ ] T016 [P] Add the real example packing slip (13 orders + summary page) as `backend/tests/LootSingles.Fixtures/PackingSlips/valid-multi-order-batch.pdf`, sanitized if any field still resembles real customer data
- [ ] T017 Write a failing unit test in `backend/tests/LootSingles.UnitTests/Import/PdfPigPackingSlipParserTests.cs` asserting the parser splits `valid-multi-order-batch.pdf` into exactly 13 per-order page blocks plus the detected summary/index list, then implement T015 to make it pass

**Checkpoint**: Schema exists, migrations apply, and the parser can split a real multi-order file into per-order raw blocks. No field-level validation or persistence yet — that's what the user stories add.

---

## Phase 3: User Story 1 - Import a Valid Packing Slip (Priority: P1) 🎯 MVP

**Goal**: A valid packing slip's orders and order lines are parsed, validated, and persisted exactly as described, with quantity preserved exactly and condition/variant correctly disambiguated.

**Independent Test**: Import `valid-multi-order-batch.pdf` end-to-end; assert 13 `Order`s exist, each with correct `OrderLine`s (fields, quantities, raw description) matching the source.

### Tests for User Story 1

> Write these first; confirm each fails for the right reason before implementing.

- [ ] T018 [P] [US1] Failing unit test for condition/variant disambiguation in `backend/tests/LootSingles.UnitTests/Import/ConditionVariantParserTests.cs` covering all FR-018 examples ("Near Mint Holofoil", "Near Mint Foil", "Lightly Played Foil", "Near Mint Reverse Holofoil", "Near Mint" alone, and a parenthetical marker like "(Showcase)" combined with a condition suffix)
- [ ] T019 [P] [US1] Failing unit test for field extraction in `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs` — given a raw description line, correct ProductLine/ProductName/Set/CollectorNumber/Rarity/Quantity are extracted, using real lines from the fixture (e.g., `"Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint Holofoil"`, qty 2)
- [ ] T020 [P] [US1] Failing unit test asserting `RawDescription` is retained byte-for-byte alongside parsed fields (FR-017) in `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`
- [ ] T021 [P] [US1] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/ValidImportTests.cs`: import `valid-multi-order-batch.pdf`, assert all 13 `Order`s persisted with `Status = Ready`, correct `TcgplayerOrderId`s, and every `OrderLine`'s quantity exactly matching the source (including multi-quantity lines) — no lines added, dropped, or merged (FR-003, FR-004)

### Implementation for User Story 1

- [ ] T022 [US1] Implement `ConditionVariantParser` in `backend/src/LootSingles.Application/Import/ConditionVariantParser.cs` (research.md §3: known condition-prefix list, remainder + parenthetical markers → Variant) to pass T018
- [ ] T023 [US1] Implement field-extraction logic (product line/game, product name, set, collector number, rarity) in `backend/src/LootSingles.Application/Import/OrderLineExtractor.cs`, using `ConditionVariantParser`, to pass T019/T020
- [ ] T024 [US1] Define `IPackingSlipImportService` in `backend/src/LootSingles.Application/Import/IPackingSlipImportService.cs` and `ImportProgressUpdate` per contracts/import-service.md
- [ ] T025 [US1] Implement `PackingSlipImportService` in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`: for each order block from `IPackingSlipParser`, extract fields, and — for this story's happy path — persist `Order` + all its `OrderLine`s in one `SaveChangesAsync()` call per order (FR-016 atomicity boundary), yielding an `ImportProgressUpdate` after each order
- [ ] T026 [US1] Wire DI registration for `IPackingSlipParser` → `PdfPigPackingSlipParser`, `IPackingSlipImportService` → `PackingSlipImportService`, and `LootSinglesDbContext` in `backend/src/LootSingles.Api/Program.cs`
- [ ] T027 [US1] Run T021 against the full wired pipeline (T022–T026) and confirm it passes

**Checkpoint**: A clean, valid packing slip round-trips end-to-end. This is the deployable MVP slice — US2 and US3 add safety guarantees on top.

---

## Phase 4: User Story 2 - Reject an Unparseable Packing Slip (Priority: P1)

**Goal**: Malformed data — at the order, line, or whole-file level — is rejected with a specific reason and never produces a partial `Order`; duplicate imports are rejected, including under a concurrent race.

**Independent Test**: Import fixtures engineered per spec.md's malformed cases individually; assert no `Order`/`OrderLine` is created for any of them and each rejection carries the correct `FailureCode`.

### Tests for User Story 2

- [ ] T028 [P] [US2] Add malformed fixtures to `backend/tests/LootSingles.Fixtures/PackingSlips/`: `missing-order-identifier.pdf`, `zero-product-lines.pdf`, `invalid-quantity.pdf` (blank/zero/negative/non-numeric variants), `missing-product-name.pdf`, `missing-set.pdf`, `missing-collector-number.pdf`, `missing-condition.pdf`, `corrupted-file.pdf` (not a valid PDF), `partial-batch-one-bad-order.pdf` (multiple orders, one with a missing identifier)
- [ ] T029 [P] [US2] Failing unit tests in `backend/tests/LootSingles.UnitTests/Import/OrderValidationTests.cs` — each required-field-missing case maps to its FR-006 `FailureType`; rarity/variant absence does **not** fail validation (FR-005)
- [ ] T030 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/RejectionTests.cs` covering each malformed per-order fixture: no `Order`/`OrderLine` created, `ImportOrderResult.Outcome = Rejected` with the correct `FailureCode` and a specific (non-generic) message (FR-006, FR-007)
- [ ] T031 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/RejectionTests.cs` for `corrupted-file.pdf`: `ImportAttempt.AttemptFailureCode = UnreadablePdf`, zero `ImportOrderResult`s (FR-015)
- [ ] T032 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/PartialImportTests.cs` for `partial-batch-one-bad-order.pdf`: valid orders succeed and persist while only the bad order is rejected (FR-005)
- [ ] T033 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/DuplicateOrderTests.cs`: import `valid-multi-order-batch.pdf` twice; second import's matching orders report `Rejected`/`DuplicateOrder`, first import's `Order`/`OrderLine` rows unchanged (FR-008)
- [ ] T034 [US2] Failing concurrency integration test in `backend/tests/LootSingles.IntegrationTests/Import/DuplicateOrderTests.cs`: fire two concurrent `ImportAsync` calls for the same new order identifier; assert exactly one `Order` row exists afterward and the loser reports `DuplicateOrder` (not an unhandled exception) — proves the DB unique constraint, not just the pre-check, is what closes the race (FR-008, SC-007)
- [ ] T035 [P] [US2] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/RejectionTests.cs` using a manually-edited copy of the valid fixture with an altered summary/index page (an order id added or removed from the list): `ImportAttempt.AttemptFailureCode = SummaryMismatch` (FR-013)

### Implementation for User Story 2

- [ ] T036 [US2] Add required-field validation (product name, set, collector number, condition, quantity-is-positive-whole-number) to the extraction/validation path from US1, returning a `FailureType` instead of throwing, to pass T029/T030
- [ ] T037 [US2] Add whole-file-unreadable handling to `PdfPigPackingSlipParser`/`PackingSlipImportService`: catch unreadable/non-PDF input, set `ImportAttempt.AttemptFailureCode = UnreadablePdf`, short-circuit with zero `ImportOrderResult`s, to pass T031
- [ ] T038 [US2] Confirm/adjust `PackingSlipImportService`'s per-order loop already satisfies partial import (T032) — each order's failure must not affect sibling orders in the same batch
- [ ] T039 [US2] Add duplicate-order handling to `PackingSlipImportService`: pre-check by `TcgplayerOrderId` before insert, and catch the unique-constraint `DbUpdateException` on `SaveChangesAsync()` as the race-loss signal, both mapping to `DuplicateOrder` (research.md §6), to pass T033/T034
- [ ] T040 [US2] Add summary/index cross-check to `PdfPigPackingSlipParser`/`PackingSlipImportService`: compare the parsed order-identifier set against the detected summary page list, setting `AttemptFailureCode = SummaryMismatch` on discrepancy, to pass T035
- [ ] T041 [US2] Ensure `FailureMessage` text is specific per `FailureType` (identifies the affected order and/or product line, per FR-006) across all rejection paths

**Checkpoint**: The feature is now safe to point at real, messy packing slips — US1's happy path plus US2's defensive rejection together satisfy spec.md's "must exist alongside the happy path before real fulfillment" requirement for US2.

---

## Phase 5: User Story 3 - Keep Customer Shipping Information Out of Imported Records (Priority: P2)

**Goal**: No customer shipping PII, and no copy of the source PDF itself, survives an import — success or failure.

**Independent Test**: Import a fixture containing a customer's shipping name/address; inspect persisted data and any temp/durable storage afterward.

### Tests for User Story 3

- [ ] T042 [P] [US3] Failing unit test in `backend/tests/LootSingles.UnitTests/Import/PiiExclusionTests.cs` asserting no type in `LootSingles.Domain`/`LootSingles.Application` exposes a settable field for customer name/address/contact (a structural/reflection-based guard, so a future accidental addition fails loudly)
- [ ] T043 [P] [US3] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/PiiRetentionTests.cs`: import `valid-multi-order-batch.pdf` (contains real-shaped shipping names/addresses per order), then query all persisted `Order`/`OrderLine` rows and assert none contain any of the source file's customer names or address fragments (FR-009, SC-004)
- [ ] T044 [P] [US3] Failing integration test in `backend/tests/LootSingles.IntegrationTests/Import/PiiRetentionTests.cs`: after both a successful import and a rejected import (`corrupted-file.pdf`), scan the process's temp directory and any configured blob/file storage for a copy of the source PDF bytes — assert none exists (FR-019)

### Implementation for User Story 3

- [ ] T045 [US3] Audit `PdfPigPackingSlipParser` and `PackingSlipImportService` to confirm the "Ship To"/customer block is never mapped into any DTO or entity — parsing only ever reads the order-identifier and line-item table regions; adjust if T042/T043 reveal otherwise
- [ ] T046 [US3] Confirm the import pipeline holds the PDF only as an in-memory `Stream`/byte buffer passed directly to the parser, with no `File.Write`/blob-upload call anywhere in this feature's code, to pass T044 (research.md §7)

**Checkpoint**: All three user stories pass together — spec.md's full acceptance-scenario set for this feature is satisfied.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation against the spec as a whole, not tied to one story.

- [ ] T047 [P] Progress-reporting integration test in `backend/tests/LootSingles.IntegrationTests/Import/ProgressReportingTests.cs`: enumerate `ImportAsync` updates for `valid-multi-order-batch.pdf` and assert `OrdersProcessed`/`SucceededCount`/`FailedCount` increase monotonically and the final update has `IsComplete = true` (FR-014, SC-006)
- [ ] T048 [P] Run quickstart.md's full validation-scenario list end-to-end and record results
- [ ] T049 [P] Review all `FailureMessage` strings for clarity (a human unfamiliar with the code can tell what to fix from the message alone)
- [ ] T050 Confirm no NuGet package pulled in during T002 introduces a licensing conflict (research.md §1 — PdfPig is MIT; verify no transitive AGPL/commercial dependency was added)
- [ ] T051 Re-run the full test suite (`dotnet test`) and confirm 0 failures, output pristine (Superpowers verification-before-completion gate)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1. **Blocks all of Phase 3+.**
- **US1 (Phase 3)**: Depends on Phase 2 only.
- **US2 (Phase 4)**: Depends on Phase 2; in practice also builds on US1's extraction/persistence code (T022–T026), since US2 adds rejection branches to that same pipeline rather than separate files.
- **US3 (Phase 5)**: Depends on Phase 2; builds on US1's pipeline (audits/confirms it, per T045–T046) and is independent of US2's rejection-path additions.
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 all being complete.

### Parallel Opportunities

- T005–T009 (entity/enum creation) are all `[P]` — different files.
- T011–T012 (EF configurations) are `[P]` once T010 exists.
- Within US2, T028–T035 (fixtures + tests) are `[P]` — write them together before starting T036.
- Within US3, T042–T044 are `[P]`.
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
