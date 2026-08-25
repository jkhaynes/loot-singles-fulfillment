# Tasks: Fix Multi-Page Packing-Slip Order Import

**Input**: Design documents from `/specs/011-fix-multipage-import/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Required by constitution Principle IV (TDD is non-negotiable for new/modified application behavior). For every behavior-changing task below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file (or is a logically independent addition to a shared new file) and has no unmet dependency
- **[Story]**: Traceability to US1 or US2

---

## Phase 1: User Story 1 - Multi-Page Orders Import Completely (Priority: P1) 🎯 MVP

**Goal**: A real multi-page packing slip (grand-total row only on the last page) imports as one order containing every product line from every page, in source reading order.

**Independent Test**: Import the real 3-page sample (or its fixture) and confirm the resulting order contains all 50 product lines, with no per-page rejection.

### Tests for User Story 1

- [X] T001 [P] [US1] Add the provided sanitized packing-slip sample as a new fixture file, `backend/tests/LootSingles.Fixtures/PackingSlips/multi-page-order-no-total-on-continuation-pages.pdf` (copy from `C:\Users\jkhay\Downloads\TCGplayer_PackingSlips_20260825_PII_REMOVED.pdf`), per data-model.md and constitution's "validated against representative fixtures" requirement
- [X] T002 [US1] Add a failing test to `backend/tests/LootSingles.UnitTests/Import/PdfPigPackingSlipParserTests.cs` (`Parse_MultiPageOrderFixture_ReturnsOneOrderWithAllLinesInReadingOrder`) that parses the new fixture and asserts: exactly one `RawOrderBlock` is returned, its `OrderIdentifier` is `"F8433182-69FC9F-7D725"`, it has exactly 50 `ProductLines` (the real fixture's true count, confirmed by direct enumeration — corrected from this feature's earlier "44" estimate in spec.md/plan.md/research.md, which underdid the page-1 and page-2 row counts), and the lines appear in source reading order (asserted via the first line containing "Fomantis" with quantity "6", and the last line containing "Uta" with quantity "1")
- [X] T003 [US1] Run T002 and confirm it fails for the expected reason (today, 3 separate `RawOrderBlock`s are produced for the same order identifier, and only the last page's block has any product lines — nowhere near 50 total)

### Implementation for User Story 1

- [X] T004 [US1] In `backend/src/LootSingles.Infrastructure/Import/PdfPigPackingSlipParser.cs`: (1) add a merge step to `ParseState.ToResult()` per research.md §2/data-model.md — group `OrderBlocks` by non-null, non-whitespace `OrderIdentifier` (`StringComparer.OrdinalIgnoreCase`), keeping each merged block at the position of its first occurrence and concatenating `ProductLines` in original page order for every later block sharing that identifier; a block with a null/whitespace `OrderIdentifier` is never merged with anything (including other such blocks) and is passed through unchanged; (2) **discovered during T002/T003's Red step and required in addition to the merge**: `ExtractProductLines` itself returned zero lines for every page lacking its own "Total" row, so merging alone wasn't sufficient — extracted the existing total-row search into `FindGrandTotalLine` and added a `FindOrderNumberFooterLine` fallback (the "Order Number:" footer line already used by `ExtractOrderIdentifier`, reused via the existing `ContainsSequence` helper) as the lower bound when no total row exists on a page, so a continuation page's real rows are extracted instead of none. Updated the `RawOrderBlock` and `ParsedPackingSlip.OrderBlocks` XML doc comments to describe the corrected "one block per order" invariant (data-model.md) instead of the old "one block per page" wording — to make T002 pass
- [X] T005 [US1] Run T002, confirm it passes, and run the full `PdfPigPackingSlipParserTests.cs` suite to confirm no regression

**Checkpoint**: The real 3-page sample imports as one complete order — User Story 1 (MVP) is independently complete and testable.

---

## Phase 2: User Story 2 - Existing Import Behavior Remains Unaffected (Priority: P1)

**Goal**: Every already-working import case — single-page orders, multi-order batches, malformed-page rejection — is byte-for-byte unchanged by this fix.

**Independent Test**: Re-run the full existing import test suite and confirm every previously-passing case still produces identical results, and that two genuinely distinct single-page orders in one upload still import as two separate orders.

### Tests for User Story 2

- [X] T006 [P] [US2] Confirm the existing `Parse_SanitizedMultiOrderBatch_ReturnsThirteenOrdersAndSummaryIndex` test (`valid-multi-order-batch.pdf`, 13 distinct single-page orders each with a unique identifier) in `PdfPigPackingSlipParserTests.cs` still passes unchanged after T004 — this is expected to pass immediately, since T004's merge only combines blocks that share an identifier, and every block in this fixture has a different one; this task exists to give FR-003/FR-006's regression-safety requirement its own explicit, accurately-targeted verification, mirroring feature 010 US2's precedent, not to drive additional production code
- [X] T007 [P] [US2] Confirm the existing `Parse_DuplicateProductLineFixture_PreservesBothRowsSeparately` test (`duplicate-product-line-same-order.pdf`, one single-page order) in `PdfPigPackingSlipParserTests.cs` still passes unchanged after T004 — expected to pass immediately, since a single page with no sibling sharing its identifier is unaffected by the merge
- [X] T008 [US2] Run the full `backend/tests/LootSingles.IntegrationTests` suite (which exercises the broader import pipeline through `PackingSlipImportService`, including `missing-order-identifier.pdf` and `zero-product-lines.pdf`'s genuinely-malformed rejection paths) and confirm no regression
- [X] T009 [US2] Run T006–T008 and confirm they pass immediately; if any reveals a regression, fix `PdfPigPackingSlipParser.cs` to resolve it before proceeding

**Checkpoint**: Zero behavior change confirmed for anything that worked before this fix — User Story 2 is complete.

---

## Phase 3: Polish & Cross-Cutting Validation

**Purpose**: Confirm full regression and complete manual validation.

- [X] T010 [P] Run backend build, full unit suite, full SQL Server integration suite, and CSharpier; confirm all existing tests remain green and record results in `specs/011-fix-multipage-import/validation-results.md`
- [X] T011 Perform the quickstart.md manual validation (import the real sample through the running app, confirm the order's detail view shows all 50 lines, confirm the 13-order batch fixture still imports as 13 separate orders, confirm genuinely malformed pages are still rejected) and record observations in `specs/011-fix-multipage-import/validation-results.md` — performed by the Developer directly
- [X] T012 Perform the constitution Architecture and Changeability Review against the implemented fix, capture any Must Fix findings as new tasks in `specs/011-fix-multipage-import/tasks.md`, and do not proceed to convergence until resolved — zero Must Fix findings; three Advisory findings addressed directly (T010-T012 checkbox tracking, spec.md Edge Case wording precision, strengthened T002 test assertion) rather than deferred, since none required new implementation work

---

## Dependencies & Execution Order

### Phase dependencies

- US1 (Phase 1) has no dependency and can start immediately.
- US2 (Phase 2) depends on US1's implementation (T004) existing, since it verifies that change didn't regress anything — in practice runs right after US1.
- Phase 3 (Polish) depends on both user stories being complete.

### Red-Green ordering

- T001–T003 → T004 → T005
- T006–T008 → T009

### Parallel opportunities

- T001 (fixture file) and T002 (test case) touch different files and can be authored in parallel, though T002 references the sample T001 adds.
- T006 and T007 are independent verification runs and can run in parallel.
- T010 is independent of T011/T012 and can run in parallel with preparing for them.

## Implementation Strategy

### MVP first

1. Complete Phase 1 (User Story 1) — multi-page orders import completely.
2. Stop and validate: the real 3-page sample produces exactly one order with all 50 lines in the correct order.

### Incremental delivery

1. US1 fixes the actual defect.
2. US2 proves the fix introduced zero regression, independent of US1's own correctness.
3. Polish confirms full-stack regression and completes manual validation.

## Notes

- No database schema or migration changes are needed anywhere in this feature (data-model.md) — this is a pure parser-internal assembly correction.
- No new package is introduced.
- This feature adds no new production logging (research.md §4) — `PackingSlipImportService`'s existing completion log already reports accurately once it receives correctly-merged blocks.
- The merge's "never merge two blocks that both have a missing identifier" guard (research.md §2, data-model.md) is exercised by code review and the Architecture and Changeability Review (T012) rather than a dedicated fixture, since there is no real evidence a packing slip ever produces two same-page-position pages that both lack a readable order identifier — constructing a synthetic fixture for a scenario with no confirmed real precedent would repeat exactly the invented-not-validated test data pattern feature 010's research.md (§2) already identified and corrected against.
