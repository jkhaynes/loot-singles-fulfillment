# Tasks: Order Picking Detail — Additional Packing-Slip Fields

**Input**: Design documents from `/specs/008-picking-detail-additional-fields/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/order-detail-api.md, quickstart.md

**Tests**: Required by constitution Principle IV (TDD is non-negotiable for new/modified application behavior). For every behavior-changing task below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file (or is a logically independent addition to a shared new file) and has no unmet dependency
- **[Story]**: Traceability to US1 or US2

---

## Phase 1: Foundational — Extend the Order Detail Backend Response

**Purpose**: `Status`, `ProductLine`, `CollectorNumber`, and `Rarity` all already exist on the persisted
`Order`/`OrderLine` entities; this phase extends the existing `OrderDetail`/`OrderLineDetail` records,
`OrderRepository.GetByIdAsync` projection, and `OrdersController`'s response shape (the same three files
feature 007 built) to expose all four together, per contracts/order-detail-api.md. Both user stories below
depend on this — there is no new endpoint or route, just a wider response on the existing one.

### Tests

- [X] T001 [P] Extend `GetByIdReturnsFullOrderDetail` in `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: seed the order's `Status`, give the existing "Genesect ex" line a `ProductLine`/`CollectorNumber`/`Rarity` (e.g. "Pokemon" / "#067/086" / "Double Rare"), give the existing "Pikachu" line (which already has `Variant = null`) a `Rarity = null` too, and assert the response's `status` and each line's `productLine`/`collectorNumber`/`rarity` — including that the null-rarity line's JSON `rarity` property is explicitly `null`, not omitted — per contracts/order-detail-api.md
- [X] T002 Run T001 and confirm it fails for the expected reason (the response does not yet contain `status`, `productLine`, `collectorNumber`, or `rarity`)

### Implementation

- [X] T003 [P] Extend `backend/src/LootSingles.Application/Orders/OrderDetail.cs` per data-model.md: add `OrderStatus Status` to `OrderDetail`, and add `string ProductLine`, `string CollectorNumber`, `string? Rarity` to `OrderLineDetail`
- [X] T004 Extend `OrderRepository.GetByIdAsync`'s projection in `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs` to populate the four new properties from the already-loaded `Order`/`OrderLine` entities (no new `Include`, no new query shape)
- [X] T005 Extend `OrderDetailResponse`/`OrderLineDetailResponse` and the `GetById` mapping in `backend/src/LootSingles.Api/Controllers/OrdersController.cs` to include `Status`, `ProductLine`, `CollectorNumber`, `Rarity`, matching contracts/order-detail-api.md, to make T001 pass
- [X] T006 Run T001, confirm it passes, and run the full backend test suite to confirm no regression

**Checkpoint**: `GET /api/orders/{orderId}` now returns `status` and every line's `productLine`/`collectorNumber`/`rarity` — both user stories below can now build their frontend rendering on this.

---

## Phase 2: User Story 1 - See Every Line's Full Identity, Including Collector Number and Rarity (Priority: P1) 🎯 MVP

**Goal**: Every product line on the detail view shows its game/product line and collector number (always present) and its rarity (when present), so a picker can identify the exact card and printing to pick.

**Independent Test**: Open an order's detail view and confirm every line shows its product line and collector number, that a line with a recorded rarity shows it, and that a line without one renders cleanly with no blank or broken rarity field.

### Tests for User Story 1

- [X] T007 [P] [US1] Extend the render test in `frontend/tests/orders/OrderDetailPage.test.tsx` (the one asserting the order identifier and every line field): assert `productLine` and `collectorNumber` render for every line, and `rarity` renders for the line where it's present
- [X] T008 [P] [US1] Add a failing test case to `frontend/tests/orders/OrderDetailPage.test.tsx`: a line with `rarity: null` renders without a blank or broken rarity field (no "Rarity" label rendered for that line), mirroring the existing variant-omission assertion, per FR-003
- [X] T009 [US1] Run T007–T008 and confirm they fail for the expected reason (`OrderDetailPage` does not render `productLine`, `collectorNumber`, or `rarity` yet)

### Implementation for User Story 1

- [X] T010 [P] [US1] Extend `OrderLineDetail` in `frontend/src/features/orders/ordersApi.ts`: add `productLine: string`, `collectorNumber: string`, `rarity: string | null`
- [X] T011 [US1] Update `frontend/src/features/orders/OrderDetailPage.tsx` to render `productLine` and `collectorNumber` unconditionally and `rarity` conditionally (omitted when `null`, mirroring the existing `variant` treatment) for every line, to make T007–T008 pass
- [X] T012 [US1] Adjust `frontend/src/features/orders/OrderDetailPage.css` as needed so the three added attribute rows remain legible and unclipped within the existing `.order-detail-line__attributes` grid
- [X] T013 [US1] Run T007–T008, confirm they pass, and run the full frontend test suite to confirm no regression

**Checkpoint**: Every product line shows its game, collector number, and rarity (when present) — User Story 1 is independently complete and testable.

---

## Phase 3: User Story 2 - See the Order's Status on Its Detail View (Priority: P2)

**Goal**: The order's status is visible on its detail view, next to the existing TCGplayer identifier.

**Independent Test**: Open any order's detail view and confirm its status is visible alongside its TCGplayer order identifier.

### Tests for User Story 2

- [X] T014 [P] [US2] Add a failing test case to `frontend/tests/orders/OrderDetailPage.test.tsx`: asserts the order's `status` renders in the page header alongside the TCGplayer identifier
- [X] T015 [US2] Run T014 and confirm it fails for the expected reason (`OrderDetailPage` does not render `status` yet)

### Implementation for User Story 2

- [X] T016 [P] [US2] Extend `OrderDetail` in `frontend/src/features/orders/ordersApi.ts`: add `status: string`
- [X] T017 [US2] Update `frontend/src/features/orders/OrderDetailPage.tsx` to render `order.status` in the header next to the TCGplayer identifier, to make T014 pass
- [X] T018 [US2] Run T014, confirm it passes, and run the full frontend test suite to confirm no regression

**Checkpoint**: Both user stories are complete; the detail view shows product line, collector number, rarity, and status.

---

## Phase 4: Polish & Cross-Cutting Validation

**Purpose**: Confirm full regression across both stacks and complete manual validation.

- [X] T019 [P] Extend `frontend/e2e/order-detail.spec.ts` to assert the product line, collector number, and order status are visible on the seeded E2E order's detail view
- [X] T020 [P] Re-run `frontend/e2e/responsive.spec.ts`'s existing order-detail mobile-viewport check with the added fields in place; adjust `OrderDetailPage.css` further only if it now fails
- [X] T021 [P] Run backend build, full unit suite, full SQL Server integration suite, and CSharpier; confirm all existing tests remain green and record results in `specs/008-picking-detail-additional-fields/validation-results.md`
- [X] T022 [P] Run frontend lint, unit test suite, production build, Prettier check, and the full Playwright suite; confirm all existing tests remain green and record results in `specs/008-picking-detail-additional-fields/validation-results.md`
- [X] T023 Perform the quickstart.md manual validation (run the app locally, exercise every scenario) and record observations in `specs/008-picking-detail-additional-fields/validation-results.md`
- [X] T024 Perform the constitution Architecture and Changeability Review against the implemented boundaries, capture any Must Fix findings as new tasks in `specs/008-picking-detail-additional-fields/tasks.md`, and do not proceed to convergence until resolved

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (Foundational) has no dependency and blocks both user stories (they both render fields this phase's response adds).
- US1 (Phase 2) depends only on Phase 1.
- US2 (Phase 3) depends only on Phase 1 — independent of US1's frontend changes (different fields, no shared new code path), though both edit `OrderDetailPage.tsx`/`ordersApi.ts` so should be sequenced to avoid merge conflicts if worked by different people.
- Phase 4 (Polish) depends on both user stories being complete.

### Red-Green ordering

- T001–T002 → T003–T005 → T006
- T007–T009 → T010–T012 → T013
- T014–T015 → T016–T017 → T018

### Parallel opportunities

- T001 and T003 are independent additions (test file vs. record file) and can be authored in parallel, then run together.
- T007 and T008 are independent test additions to the same file and can be authored in parallel, then run together.
- T010 has no dependency on T007/T008 passing first and can be authored alongside them.
- T014 and T016 are independent of US1's tasks and can be authored in parallel with Phase 2, once Phase 1 is complete.
- T019, T020, T021, and T022 are independent and can run in parallel.

## Implementation Strategy

### MVP first

1. Complete Phase 1 (backend response extension).
2. Complete Phase 2 (User Story 1) — every line already shows its game, collector number, and rarity.
3. Stop and validate: every field renders correctly, including the rarity-omission case.

### Incremental delivery

1. US1 makes every line's exact printing identifiable.
2. US2 adds order status visibility (independent of US1, can be delivered before or after it).
3. Polish proves zero regressions and completes manual validation.

## Notes

- No database schema or migration changes are needed anywhere in this feature (data-model.md) — every task reads existing `Order`/`OrderLine` columns.
- No new frontend or backend package, endpoint, route, or component is introduced anywhere in this feature.
- This feature adds no new production logging (research.md §5), consistent with the existing precedent.
- Assert against the JSON response shape and rendered DOM content precisely as specified in contracts/order-detail-api.md and data-model.md — not just that a value appears somewhere on the page.
