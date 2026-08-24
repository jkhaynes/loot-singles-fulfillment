# Tasks: Order Picking Detail View

**Input**: Design documents from `/specs/007-order-picking-view/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/order-detail-api.md, quickstart.md

**Tests**: Required by constitution Principle IV (TDD is non-negotiable for new/modified application behavior). For every behavior-changing task below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file (or is a logically independent addition to a shared new file) and has no unmet dependency
- **[Story]**: Traceability to US1, US2, US3, or US4

---

## Phase 1: Foundational — Order Detail Backend Endpoint

**Purpose**: Every user story depends on `GET /api/orders/{orderId}` existing and returning full order detail. This phase delivers that endpoint per contracts/order-detail-api.md.

### Tests

- [X] T001 [P] Add failing tests to `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: `GetById_ReturnsFullOrderDetail` (seed an order with two lines, one with a variant and one with `Variant = null`; assert `200 OK`, the response's `orderId`/`tcgplayerOrderId`, and each line's `productName`/`set`/`condition`/`quantity`, asserting the null-variant line's JSON `variant` property is explicitly `null`, not omitted) and `GetById_ForNonExistentOrder_Returns404WithOrderNotFoundError` (assert `404 Not Found` with body `{"error":"order_not_found"}`), following the existing `LoginAsync`/`NewOrder` helper pattern already in that file
- [X] T002 Add a failing test to the same file: `GetByIdWithoutSessionReturns401`, mirroring the existing `GetWithoutSessionReturns401` test
- [X] T003 Run T001–T002 and confirm they fail for the expected reason (no `GET /api/orders/{orderId}` route exists yet)

### Implementation

- [X] T004 [P] Add `OrderDetail` and a nested order-line-detail record to `backend/src/LootSingles.Application/Orders/OrderDetail.cs` (new file), per data-model.md: `OrderDetail(int OrderId, string TcgplayerOrderId, IReadOnlyList<OrderLineDetail> Lines)` and `OrderLineDetail(string ProductName, string Set, string? Variant, string Condition, int Quantity)`
- [X] T005 Add `Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken)` to `backend/src/LootSingles.Application/Orders/IOrderRepository.cs`
- [X] T006 Implement `GetByIdAsync` in `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs`: `AsNoTracking()` query on `Order` including `OrderLines`, projecting to `OrderDetail`/`OrderLineDetail` (mirroring the existing `GetAllAsync` projection style), returning `null` when no order matches
- [X] T007 Add `Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken)` to `backend/src/LootSingles.Application/Orders/OrdersService.cs`, delegating to `IOrderRepository.GetByIdAsync`
- [X] T008 Add a `GET("{orderId:int}")` action to `backend/src/LootSingles.Api/Controllers/OrdersController.cs` calling `OrdersService.GetByIdAsync`, returning `200 OK` with an `OrderDetailResponse`/`OrderLineDetailResponse` shape matching contracts/order-detail-api.md when found, or `404 Not Found` with `{ error = "order_not_found" }` when `null`, to make T001–T002 pass
- [X] T009 Run T001–T002, confirm they pass, and run the full backend test suite to confirm no regression

**Checkpoint**: `GET /api/orders/{orderId}` exists, is authenticated, and returns full order detail or a typed 404 — every user story below can now build on it.

---

## Phase 2: User Story 1 - View an Order's Full Picking Detail (Priority: P1) 🎯 MVP

**Goal**: A picker who opens an order's detail view sees its identifier and every line's card identity, set, variant, condition, and quantity — with quantity greater than one emphasized via color plus a non-color cue — without consulting any other screen.

**Independent Test**: Navigate directly to an order's detail URL and confirm every required field renders correctly, including the quantity emphasis and the not-found state for an invalid id.

### Tests for User Story 1

- [ ] T010 [P] [US1] Add `getOrderDetail(orderId)` and `OrderDetail`/`OrderLineDetail` types to `frontend/src/features/orders/ordersApi.ts`, mirroring `getOrders`'s shape: `GET /api/orders/{orderId}`, throwing a distinct `OrderNotFoundError` on a `404` response and a generic `Error` on any other non-ok response
- [ ] T011 [P] [US1] Add a failing test file `frontend/tests/orders/OrderDetailPage.test.tsx`: renders the order's `tcgplayerOrderId` and, for every line, `productName`/`set`/`condition`/`quantity`; a line with `variant: null` renders without a variant field (no blank/broken placeholder for it), following the `MemoryRouter` + `vi.mock('.../ordersApi')` pattern already used in `frontend/tests/orders/OrdersPage.test.tsx`
- [ ] T012 [P] [US1] Add a failing test case to `frontend/tests/orders/OrderDetailPage.test.tsx`: a line with `quantity` greater than 1 renders with both the existing `data-emphasis="high"` styling hook and a non-color "×N" text marker (e.g., "×4"), while a `quantity: 1` line has neither, per FR-003
- [ ] T013 [P] [US1] Add a failing test case to `frontend/tests/orders/OrderDetailPage.test.tsx`: when `getOrderDetail` rejects with `OrderNotFoundError`, the page shows a distinct "order not found" message (not the generic load-error message), per FR-008
- [ ] T014 [US1] Run T011–T013 and confirm they fail for the expected reason (`OrderDetailPage` does not exist yet)

### Implementation for User Story 1

- [ ] T015 [US1] Create `frontend/src/features/orders/OrderDetailPage.tsx`: read `orderId` via `useParams`, fetch with `getOrderDetail` on mount (same loading/error local-state convention as `OrdersPage.tsx`), render the order identifier and each line's fields, apply `data-emphasis="high"` plus a "×N" marker when `quantity > 1`, omit the variant field when `null`, and show a distinct not-found state when the fetch rejects with `OrderNotFoundError` versus a generic error state otherwise, to make T011–T013 pass
- [ ] T016 [US1] Create `frontend/src/features/orders/OrderDetailPage.css` for the page's layout (desktop-first pass; mobile responsiveness is finished in Phase 5/US4)
- [ ] T017 [US1] Add the `/orders/:orderId` route rendering `OrderDetailPage` to `frontend/src/App.tsx`
- [ ] T018 [US1] Run T011–T013, confirm they pass, and run the full frontend test suite to confirm no regression

**Checkpoint**: Navigating directly to `/orders/:orderId` for a real order shows its full picking detail with correct quantity emphasis; navigating to an invalid id shows a clear not-found state.

---

## Phase 3: User Story 2 - Reach Order Detail from Anywhere Orders Are Listed (Priority: P1)

**Goal**: Every order in the dashboard's Available Orders list and in Browse Orders is directly selectable into its detail view.

**Independent Test**: From each of the two existing list views, select an order and confirm it opens that exact order's detail view in one interaction.

### Tests for User Story 2

- [ ] T019 [P] [US2] Add a failing test case to `frontend/tests/orders/OrdersPage.test.tsx`: each rendered order row links to `/orders/{orderId}` for that specific order
- [ ] T020 [P] [US2] Add a failing test case to `frontend/tests/dashboard/DashboardPage.test.tsx`: each rendered order row in the Available Orders table links to `/orders/{orderId}` for that specific order
- [ ] T021 [US2] Run T019–T020 and confirm they fail for the expected reason (rows are not currently links)

### Implementation for User Story 2

- [ ] T022 [P] [US2] In `frontend/src/features/orders/OrdersPage.tsx`, wrap each order row (or its identifying content) in a `Link` to `/orders/{order.orderId}`, to make T019 pass
- [ ] T023 [P] [US2] In `frontend/src/features/dashboard/DashboardPage.tsx`, wrap each Available Orders table row (or its identifying cell) in a `Link` to `/orders/{order.orderId}`, to make T020 pass
- [ ] T024 [US2] Run T019–T020, confirm they pass, and run the full frontend test suite to confirm no regression

**Checkpoint**: Both existing order-listing views open directly into the correct order's detail view.

---

## Phase 4: User Story 3 - Never See a Guessed Card Image (Priority: P1)

**Goal**: Every product line shows the same neutral placeholder in place of a card image — never a guessed or low-confidence image.

**Independent Test**: Open any order's detail view and confirm every line shows the identical static placeholder, with no `<img>` sourced from any dynamic or external reference.

### Tests for User Story 3

- [ ] T025 [US3] Add a test case to `frontend/tests/orders/OrderDetailPage.test.tsx` asserting every rendered product line contains the same neutral placeholder element (a stable, queryable marker such as a fixed `aria-label`), and that no line ever renders an `<img>` element with a `src` attribute. This is expected to pass immediately once T015 lands, since the placeholder is part of that same line-rendering implementation — it exists to give User Story 3 its own independent acceptance verification, not to drive additional production code.

**Checkpoint**: The placeholder-never-a-guess safety property is independently verified.

---

## Phase 5: User Story 4 - Use the Detail View on a Phone (Priority: P2)

**Goal**: The detail view is fully usable at a common mobile-phone width, with all information visible and legible without horizontal scrolling.

**Independent Test**: View an order's detail at a common mobile-phone viewport width and confirm every line's information remains fully visible without horizontal scrolling.

### Tests for User Story 4

- [ ] T026 [US4] Add a failing Playwright test to a new `frontend/e2e/order-detail.spec.ts`: log in, open an order from the dashboard's Available Orders list, confirm the detail view shows the order identifier and at least one line's fields, at the default (desktop) viewport
- [ ] T027 [US4] Add a failing Playwright test to `frontend/e2e/responsive.spec.ts` (reusing that file's existing `test.use({ viewport: ..., isMobile: true, hasTouch: true })` mobile-viewport pattern): open an order's detail view and confirm `document.documentElement.scrollWidth` does not exceed `clientWidth` (no horizontal scroll), per FR-007/SC-005
- [ ] T028 [US4] Run T026–T027 and confirm T026 passes (given Phase 2–3 are already implemented) while T027 fails for the expected reason (layout not yet verified at mobile width)

### Implementation for User Story 4

- [ ] T029 [US4] Adjust `frontend/src/features/orders/OrderDetailPage.css` as needed so the layout has no horizontal overflow at a common mobile-phone width, to make T027 pass
- [ ] T030 [US4] Run T026–T027, confirm both pass, and run the full Playwright suite to confirm no regression

**Checkpoint**: All four user stories are implemented and independently verified; the detail view is usable at both desktop and mobile-phone widths.

---

## Phase 6: Polish & Cross-Cutting Validation

**Purpose**: Confirm full regression across both stacks and complete manual validation.

- [ ] T031 [P] Run backend build, full unit suite, full SQL Server integration suite, and CSharpier; confirm all existing tests remain green and record results in `specs/007-order-picking-view/validation-results.md`
- [ ] T032 [P] Run frontend lint, unit test suite, production build, Prettier check, and the full Playwright suite; confirm all existing tests remain green and record results in `specs/007-order-picking-view/validation-results.md`
- [ ] T033 Perform the quickstart.md manual validation (run the app locally, exercise every scenario including the colorblind-simulation check for quantity emphasis) and record observations in `specs/007-order-picking-view/validation-results.md`
- [ ] T034 Perform the constitution Architecture and Changeability Review against the implemented boundaries, capture any Must Fix findings as new tasks in `specs/007-order-picking-view/tasks.md`, and do not proceed to convergence until resolved

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (Foundational) has no dependency and blocks every user story (they all need the endpoint).
- US1 (Phase 2) depends only on Phase 1.
- US2 (Phase 3) depends on Phase 1 and, in practice, on US1's `OrderDetailPage`/route existing (Phase 2), since it links into it.
- US3 (Phase 4) depends on the line-rendering implementation already built in Phase 2 (US1); it only adds independent verification of a property that implementation already has.
- US4 (Phase 5) depends on Phase 2 (US1) for the page it makes responsive, and benefits from Phase 3 (US2) for a realistic navigation-driven Playwright scenario.
- Phase 6 (Polish) depends on all four user stories being complete.

### Red-Green ordering

- T001–T003 → T004–T008 → T009
- T010–T014 → T015–T017 → T018
- T019–T021 → T022–T023 → T024
- T025 → (expected Green immediately; see task note)
- T026–T028 → T029 → T030

### Parallel opportunities

- T001 and T002 are separate test additions to the same file and can be authored in parallel, then run together.
- T004 has no dependency on T001/T002 passing first and can be authored alongside them.
- T010, T011, T012, and T013 are independent additions and can be authored in parallel, then run together.
- T019 and T020 touch different files and can run in parallel; likewise T022 and T023.
- T031 and T032 are independent (different stacks) and can run in parallel.

## Implementation Strategy

### MVP first

1. Complete Phase 1 (backend endpoint).
2. Complete Phase 2 (User Story 1) — a picker can already see full order detail by navigating directly to it.
3. Stop and validate: every field renders correctly, including quantity emphasis and the not-found state.

### Incremental delivery

1. US1 makes an order's full picking detail viewable.
2. US2 makes that view reachable from where pickers actually browse orders (mostly free, from US1's route already existing).
3. US3 verifies the image-safety property US1's implementation already has.
4. US4 proves and finishes mobile usability.
5. Polish proves zero regressions and completes manual validation.

## Notes

- No database schema or migration changes are needed anywhere in this feature (data-model.md) — every task reads existing `Order`/`OrderLine` data.
- No new frontend or backend package is introduced anywhere in this feature.
- This feature adds no new production logging (research.md §2), consistent with the existing order-listing/dashboard read precedent.
- Assert against the JSON response shape and rendered DOM content precisely as specified in contracts/order-detail-api.md and data-model.md — not just that a value appears somewhere on the page.
