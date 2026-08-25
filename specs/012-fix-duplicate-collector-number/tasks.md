# Tasks: Fix Duplicate Collector-Number Echo in Product Names

**Input**: Design documents from `specs/012-fix-duplicate-collector-number/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md

**Tests**: Included — the project constitution (Principle IV) mandates strict TDD (Red → Green → Refactor) for all new or modified application behavior.

**Organization**: This feature has a single user-story-pair (US1: correct extraction, US2: no regression) both P1 and inseparable in implementation — one code change satisfies both, verified by two complementary sets of test assertions. Tasks are grouped accordingly rather than split into fully independent phases.

## Phase 1: Setup

No new project, package, or scaffolding is required — this feature edits one existing file and one existing test file.

- [ ] T001 Confirm the backend builds cleanly on the `012-fix-duplicate-collector-number` branch before making changes: `dotnet build backend/LootSingles.sln`

## Phase 2: Foundational

No foundational/blocking work — the fix is entirely self-contained within `OrderLineExtractor.Extract`, which already computes every value the fix needs (`segments`, `collectorIndex`, `setAndProductSegments`).

## Phase 3: User Story 1 — Product Name No Longer Includes a Redundant Collector-Number Echo (Priority: P1)

**Goal**: A line whose raw description contains a redundant, bare collector-number echo immediately before its true `#`-prefixed collector number produces a `ProductName` with the echo removed and `Set` derived correctly from the remaining text.

**Independent Test**: Run `OrderLineExtractor.Extract` against the real Fomantis line (and the other representative real lines from the same order) and confirm `ProductName`/`Set` are correct with no trailing duplicated number.

### Tests for User Story 1 (write first — must fail before the fix)

- [X] T002 [P] [US1] In `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`, add `Extract_DuplicateCollectorNumberEcho_ExcludesEchoFromProductName` using the real description `"Pokemon - ME05: Pitch Black: Fomantis - 085/084 - #085/084 - Illustration Rare - Near Mint Holofoil"`, asserting `Set == "ME05: Pitch Black"` and `ProductName == "Fomantis"` (spec.md Acceptance Scenario US1/AC1)
- [X] T003 [P] [US1] In the same file, add a `[Theory]` covering at least two more real duplicate-echo lines from the same order (e.g. Manectric, Clefairy) asserting the echo is excluded from `ProductName` in each case (spec.md Acceptance Scenario US1/AC2)
- [X] T004 Run `dotnet test backend/tests/LootSingles.UnitTests --filter FullyQualifiedName~OrderLineExtractionTests` and confirm T002/T003 FAIL for the expected reason (echo still present in `ProductName`) before implementing the fix — Red step of TDD

### Implementation for User Story 1

- [X] T005 [US1] In `backend/src/LootSingles.Application/Import/OrderLineExtractor.cs`, immediately after the existing `var setAndProductSegments = segments.Skip(1).Take(collectorIndex - 1).ToArray();` line and before `var setAndProduct = string.Join(" - ", setAndProductSegments);`, add the exact-match exclusion check from research.md §3/data-model.md: if `setAndProductSegments` is non-empty and its last element equals `segments[collectorIndex].TrimStart('#')` (ordinal), drop that last element
- [X] T006 Run `dotnet test backend/tests/LootSingles.UnitTests --filter FullyQualifiedName~OrderLineExtractionTests` and confirm T002/T003 now PASS — Green step of TDD

**Checkpoint**: User Story 1 is independently functional and testable — real duplicate-echo lines now extract the correct `ProductName`.

---

## Phase 4: User Story 2 — Existing Import Behavior Remains Unaffected (Priority: P1)

**Goal**: Every line without the duplicate-echo shape — across all existing fixtures and the real order's non-duplicated lines — produces byte-for-byte identical results after the fix.

**Independent Test**: Re-run the full existing `OrderLineExtractionTests` suite plus a new false-positive boundary case, and the full backend test suite, confirming no change and no regression.

### Tests for User Story 2

- [X] T007 [P] [US2] In `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`, add `Extract_NonDuplicateSegmentBeforeCollectorNumber_IsNeverDroppedAsFalsePositive` using a line where the segment before the collector number is number-like but NOT an exact duplicate (e.g. `"Pokemon - Some Set: Card - 099 - #100/150 - Common - Near Mint"`), asserting that segment is still present and correctly incorporated into `ProductName`, per spec.md Edge Case (FR-003)
- [X] T008 [P] [US2] In the same file, add a regression case using a real non-duplicate line from the same order (e.g. `"Pokemon - ... Meowth - #106/094 - ..."` or `"Pokemon - ... Blastoise ex - #030/142 - ..."`) asserting `ProductName` is exactly the card name with no change from today's behavior (spec.md Acceptance Scenario US2/AC2)
- [X] T009 [US2] Run `dotnet test backend/tests/LootSingles.UnitTests --filter FullyQualifiedName~OrderLineExtractionTests` and confirm ALL existing tests in the file (T002/T003/T007/T008 plus every pre-existing test) pass together

### Validation for User Story 2

- [X] T010 [US2] Run the full backend suite — `dotnet test backend/LootSingles.sln` — and confirm 100% pass, including feature 011's multi-page fixture tests and every Magic/Lorcana case, per spec.md SC-002

**Checkpoint**: Both user stories pass together; the fix introduces zero regressions.

---

## Phase 5: Code Design Review Remediation

- [X] T013 [US2] In `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`, add `Extract_EchoOnlySpan_IsStillRejectedAsMissingProductName` using a description where the redundant echo is the ONLY content between the game name and the collector number (e.g. `"Pokemon - 067/086 - #067/086 - Rare - Near Mint"`), asserting `result.IsValid == false` and `result.FailureType == FailureType.MissingProductName`, per spec.md Edge Case / FR-004 (code-design-review finding)

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T011 Re-read the final diff of `OrderLineExtractor.cs` against research.md §3's decision to confirm the change is exactly the one conditional described (no incidental refactoring bundled in), per Constitution Principle III (Reviewability)
- [X] T012 (verified — no deviation from research.md occurred, so no spec.md edit was needed) Update `specs/012-fix-duplicate-collector-number/spec.md` Assumptions/Notes only if implementation revealed any deviation from the planned approach (expected: none, since the fix matches research.md exactly)

## Dependencies & Execution Order

- Phase 1 (Setup) has no dependencies.
- Phase 3 (US1) depends on Phase 1 only. T002/T003 (tests) before T004 (confirm Red) before T005 (implementation) before T006 (confirm Green) — strict TDD order.
- Phase 4 (US2) depends on Phase 3's implementation (T005) existing, since its tests validate against the already-fixed code plus confirm no regression. T007/T008 can be written in parallel with T002/T003 (all are test-file additions to different test methods) but are listed after Phase 3 here because their assertions are about the *post-fix* state.
- Phase 5 depends on Phases 3 and 4 both being complete.

## Parallel Example

T002, T003, T007, and T008 all add independent test methods to the same file (`OrderLineExtractionTests.cs`) and can be drafted in parallel by content, but since they land in one file, apply them as sequential edits to avoid merge conflicts within the same file — parallelism here is about independent authorship/review, not concurrent file writes.

## Implementation Strategy

**MVP = the entire feature**: This is a single, small, atomic fix — there is no meaningful smaller increment. Implement Phase 3 (US1) first to prove the Red→Green cycle on the primary defect, then Phase 4 (US2) to prove no regression, then Phase 5 to confirm reviewability.
