# Tasks: Fix Lorcana Order Line Parsing

**Input**: Design documents from `/specs/010-lorcana-order-parsing/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Required by constitution Principle IV (TDD is non-negotiable for new/modified application behavior). For every behavior-changing task below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file (or is a logically independent addition to a shared new file) and has no unmet dependency
- **[Story]**: Traceability to US1 or US2

---

## Phase 1: User Story 1 - Lorcana Orders Import Successfully (Priority: P1) 🎯 MVP

**Goal**: A real Lorcana packing-slip line (whose card name contains its own " - ") imports successfully with the correct set, card name, collector number, rarity, condition, and variant, instead of being rejected.

**Independent Test**: Import the real sample (or its fixture) and confirm the resulting order line's fields exactly match the sample, with no rejection.

### Tests for User Story 1

- [ ] T001 [P] [US1] Add the provided sanitized packing-slip sample as a new fixture file, `backend/tests/LootSingles.Fixtures/PackingSlips/lorcana-dashed-card-name.pdf` (copy from `C:\Users\jkhay\Downloads\TCGplayer_PackingSlip_Sanitized_Sample.pdf`), per data-model.md and constitution's "validated against representative fixtures" requirement
- [ ] T002 [US1] In `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`, **replace** the existing synthetic, colon-based Lorcana `[InlineData]` case (`"Lorcana TCG - The First Chapter: John Silver - Alien Pirate - #82/204 - Legendary - Near Mint"`) with one built from the real sample: `"Lorcana TCG - Disney Lorcana Promo Cards - Scrooge McDuck - S.H.U.S.H. Agent - #36 - Promo - Near Mint Holofoil"`, expecting `Set = "Disney Lorcana Promo Cards"`, `ProductName = "Scrooge McDuck - S.H.U.S.H. Agent"`, `CollectorNumber = "#36"`, `Rarity = "Promo"`, `Condition = "Near Mint"`, `Variant = "Holofoil"` (research.md §2)
- [ ] T003 [US1] Run the updated `OrderLineExtractionTests.cs` and confirm the new case fails for the expected reason (the current algorithm returns `Invalid`/`MissingProductName` for this no-colon input)

### Implementation for User Story 1

- [ ] T004 [US1] In `backend/src/LootSingles.Application/Import/OrderLineExtractor.cs`, add the no-colon fallback to the existing `Set`/`ProductName` extraction per research.md §1/data-model.md: when the combined set+product span contains no colon and spans two or more `" - "`-delimited segments, treat the first segment as `Set` and rejoin every remaining segment (with `" - "`) as `ProductName`; when it's a single segment with no colon, continue returning `Invalid(FailureType.MissingProductName, ...)` exactly as today — to make T002 pass
- [ ] T005 [US1] Run T002, confirm it passes, and run the full `OrderLineExtractionTests.cs` suite (all existing Pokémon and Magic cases) to confirm no regression

**Checkpoint**: A real Lorcana line imports successfully with every field correct — User Story 1 (MVP) is independently complete and testable.

---

## Phase 2: User Story 2 - Existing Game Parsing Remains Unaffected (Priority: P1)

**Goal**: Pokémon (and any other already-working) parsing is byte-for-byte unchanged by this fix.

**Independent Test**: Re-run the full existing import test suite and confirm every previously-passing case still produces identical results.

### Tests for User Story 2

- [ ] T006 [P] [US2] Confirm every existing Pokémon test (`Extract_RealFixtureLine_ExtractsEveryStructuredFieldAndQuantity`, `Extract_RealFixtureLine_PreservesRawDescriptionExactly`) and both existing Magic `[InlineData]` cases (`"Magic - Commander: FINAL FANTASY..."`, `"Magic - The Hobbit: My Precious..."`) in `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs` still pass unchanged after T004's fix — this is expected to pass immediately, since the fallback only activates on the previously-failing no-colon path; this task exists to give FR-002/FR-005's regression-safety requirement its own explicit, accurately-targeted verification, mirroring feature 007 US3's and feature 009 US2's precedent, not to drive additional production code
- [ ] T007 [P] [US2] Run the full `backend/tests/LootSingles.IntegrationTests` suite (which exercises the broader import pipeline, including the existing Pokémon fixture PDFs) and confirm no regression
- [ ] T008 [US2] Run T006–T007 and confirm they pass immediately; if either reveals a regression, fix `OrderLineExtractor.cs` to resolve it before proceeding
- [ ] T009 [P] [US2] Add a test case to `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs` (e.g. a new `[InlineData]` row or `[Fact]`) for a product description whose set+product span is a single segment with no colon (e.g. `"SomeGame - AmbiguousSegment - #12 - Common - Near Mint"`), asserting `Extract` still returns `Invalid` with `FailureType.MissingProductName` — expected to pass both before and after T004, since this exact shape was already rejected before this feature; this test exists to explicitly verify the new branch's boundary condition (data-model.md row 3) rather than relying on inference, closing the FR-003/SC-003 coverage gap identified by `/speckit-analyze`

**Checkpoint**: Zero behavior change confirmed for anything that worked before this fix — User Story 2 is complete.

---

## Phase 3: Polish & Cross-Cutting Validation

**Purpose**: Confirm full regression and complete manual validation.

- [ ] T010 [P] Run backend build, full unit suite, full SQL Server integration suite, and CSharpier; confirm all existing tests remain green and record results in `specs/010-lorcana-order-parsing/validation-results.md`
- [ ] T011 Perform the quickstart.md manual validation (import the real sample through the running app, confirm correct display in Browse Orders / the order detail view, confirm Pokémon import unaffected, confirm genuinely malformed data is still rejected) and record observations in `specs/010-lorcana-order-parsing/validation-results.md`
- [ ] T012 Perform the constitution Architecture and Changeability Review against the implemented fix, capture any Must Fix findings as new tasks in `specs/010-lorcana-order-parsing/tasks.md`, and do not proceed to convergence until resolved

---

## Dependencies & Execution Order

### Phase dependencies

- US1 (Phase 1) has no dependency and can start immediately.
- US2 (Phase 2) depends on US1's implementation (T004) existing, since it verifies that change didn't regress anything — in practice runs right after US1.
- Phase 3 (Polish) depends on both user stories being complete.

### Red-Green ordering

- T001–T003 → T004 → T005
- T006–T007 → T008
- T009 → (expected Green immediately; verifies T004's new branch boundary)

### Parallel opportunities

- T001 (fixture file) and T002 (test case) touch different files and can be authored in parallel, though T002 references the sample T001 adds.
- T006 and T007 are independent verification runs and can run in parallel.
- T009 is independent of T006–T008 and can run in parallel with them.
- T010 is independent of T011/T012 and can run in parallel with preparing for them.

## Implementation Strategy

### MVP first

1. Complete Phase 1 (User Story 1) — Lorcana imports correctly.
2. Stop and validate: the real sample's fields are exactly correct, and existing tests still compile/pass.

### Incremental delivery

1. US1 fixes the actual defect.
2. US2 proves the fix introduced zero regression, independent of US1's own correctness.
3. Polish confirms full-stack regression and completes manual validation.

## Notes

- No database schema or migration changes are needed anywhere in this feature (data-model.md) — this is a pure parsing-logic correction.
- No new package is introduced on either stack.
- This feature adds no new production logging (research.md §4) — the existing rejection path for genuinely ambiguous input is unchanged, no new failure mode is introduced.
- This fix does not touch Magic: The Gathering's parsing behavior at all (FR-005) — its two existing `[InlineData]` cases in `OrderLineExtractionTests.cs` remain as-is, though research.md §2 notes they are, by the same reasoning that disproved the old Lorcana case, also unconfirmed against real data — out of scope for this feature, worth a future feature once a real Magic sample exists.
