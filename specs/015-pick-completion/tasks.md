---

description: "Task list for Pick Completion (015)"
---

# Tasks: Pick Completion

**Input**: Design documents from `/specs/015-pick-completion/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/picking-api.md, quickstart.md

**Tests**: Included and REQUIRED — constitution Principle IV (TDD, NON-NEGOTIABLE) mandates
Red → Green → Refactor for all new/modified application behavior in this project; this overrides
the tasks-template's default "tests are optional" note. Every implementation task below has its
failing test written and confirmed red immediately before it.

**Organization**: Tasks are grouped by user story (US1, US2, US3) per spec.md's priorities, after
a Foundational phase both P1 stories structurally depend on (shared entities, migration, and the
required correction to feature 013's `OrderRepository`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

Existing web-app layout (unchanged): `backend/src/`, `backend/tests/`, `frontend/src/`,
`frontend/tests/`, `frontend/e2e/`.

---

## Phase 1: Setup

- [X] T001 Confirm `dotnet build` (backend/LootSingles.sln) and `npm run build` (frontend/) both
      succeed on a clean checkout of `015-pick-completion` before any change, and record the
      current full test-suite pass count as a baseline (backend: `dotnet test`; frontend:
      `npm test`) — establishes the "existing tests pass" starting point required by the
      Definition of Done.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared schema, domain types, and the required correction to feature 013's status
handling — both US1 and US2 depend on these; neither can be completed independently without them.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Extend `OrderStatus` enum with `Picked = 2` and `NeedsAttention = 3` in
      `backend/src/LootSingles.Domain/Orders/OrderStatus.cs` (data-model.md).
- [X] T003 [P] Add new `PickOutcome` enum (`Picked = 0`, `HasIssue = 1`) in
      `backend/src/LootSingles.Domain/Orders/PickOutcome.cs` (research.md §4, data-model.md).
- [X] T004 [P] Add new `PickingIssueType` enum (`CardNotFound`, `InsufficientQuantity`,
      `WrongCardInLocation`, `WrongVariant`, `WrongCondition`, `Damaged`,
      `InventoryDiscrepancy`, `InformationIncorrect`, `Other`) in
      `backend/src/LootSingles.Domain/Orders/PickingIssueType.cs` (data-model.md).
- [X] T005 Add new `PickingIssue` entity (`Id`, `OrderLineId`, `IssueType`, `RequiredQuantity?`,
      `FoundQuantity?`, `Note?`, `ReportedByEmployeeId`, `ReportedAt`) in
      `backend/src/LootSingles.Domain/Orders/PickingIssue.cs` (data-model.md) — depends on T003
      not existing at all; independent of T002-T004 otherwise but keep after T004 for review
      clarity.
- [X] T006 Extend `OrderLine` entity with `PickOutcome?`, `PickOutcomeRecordedByEmployeeId?`,
      `PickOutcomeRecordedAt?`, `CurrentPickingIssueId?` in
      `backend/src/LootSingles.Domain/Orders/OrderLine.cs` (data-model.md; depends on T003, T005).
- [X] T007 [P] Add `PickingIssueConfiguration.cs` (EF configuration: PK, FK to `OrderLine`,
      `.HasConversion<string>()` on `IssueType`) in
      `backend/src/LootSingles.Infrastructure/Persistence/Configurations/PickingIssueConfiguration.cs`
      (research.md §4; depends on T005).
- [X] T008 Extend `OrderLineConfiguration.cs` with the four new `OrderLine` column mappings and the
      `CurrentPickingIssueId` FK to `PickingIssue` in
      `backend/src/LootSingles.Infrastructure/Persistence/Configurations/OrderLineConfiguration.cs`
      (depends on T006, T007).
- [X] T009 Add shared `OrderStatusComputation.FromCurrentLines` static helper building the reusable
      `Expression<Func<Order, OrderStatus>>` (order has an unresolved-issue line → NeedsAttention;
      else Ready/InProgress per caller context) in
      `backend/src/LootSingles.Infrastructure/Persistence/OrderStatusComputation.cs` (research.md
      §1, §5; Constitution Principle XIII — extracted because it is about to have its second
      concrete use case; depends on T002, T006).
- [X] T010 Add EF Core migration `AddPickCompletion` (new `PickingIssues` table, four new
      `OrderLines` columns, `CurrentPickingIssueId` FK) via `dotnet ef migrations add
      AddPickCompletion` in `backend/src/LootSingles.Infrastructure/Persistence/Migrations/`
      (depends on T007, T008; naming convention per plan.md).
- [X] T011 [P] Write failing integration test asserting `OrderRepository.ReleaseAsync` and
      `ForceReleaseAsync` leave `Order.Status` as `NeedsAttention` (not reset to `Ready`) when the
      order has a line with `PickOutcome.HasIssue` at release time, in
      `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerClaimingTests.cs`
      (research.md §5; run and confirm it fails for the expected reason before T013).
- [X] T012 [P] Write failing integration test asserting `OrderRepository.ClaimSpecificAsync`
      (re-claiming via Choose Order) sets `Order.Status` to `NeedsAttention` — not `InProgress` —
      when the order still has a line with `PickOutcome.HasIssue`, in the same test file as T011
      (research.md §5, data-model.md state-transition table; run and confirm it fails before T013);
      additionally seed one Ready and one NeedsAttention order and assert Pick Next Order
      (`ClaimNextAvailableAsync`) returns only the Ready one, never the NeedsAttention one (FR-005 —
      closes a gap flagged by `/speckit-analyze`: this exclusion is structurally guaranteed by the
      existing Ready-only filter per research.md §5 but was previously unverified by any test).
- [X] T013 Update `OrderRepository.ClaimSpecificAsync`, `ReleaseAsync`, and `ForceReleaseAsync` in
      `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs` to compute `Status`
      via `OrderStatusComputation.FromCurrentLines` instead of hardcoding `InProgress`/`Ready`
      (research.md §5; makes T011, T012 pass; depends on T009, T011, T012).

**Checkpoint**: Schema, domain types, and the corrected claim/release status logic are in place and
tested. User story implementation can now begin.

---

## Phase 3: User Story 1 - Picker Confirms a Product Line as Successfully Picked (Priority: P1) 🎯 MVP

**Goal**: A picker can confirm a claimed order's product lines as picked, one at a time, with the
order automatically becoming Picked once every line is confirmed and none has an unresolved issue.

**Independent Test**: As a picker with a claimed order, confirm each product line as picked; once
every line is confirmed with no reported issues, the order's status becomes Picked automatically
(spec.md US1 Independent Test).

### Tests for User Story 1

- [X] T014 [P] [US1] Write failing unit test for `PickingService.RecordPickedAsync` — success case
      (returns updated order/line state) and `NotYourClaim` case (actor doesn't hold the claim) —
      in `backend/tests/LootSingles.UnitTests/Picking/PickingServiceTests.cs` (FR-001, FR-010).
- [X] T015 [P] [US1] Write failing integration test for `POST
      /api/orders/{orderId}/lines/{lineId}/pick`: success (200, order status recomputed per
      FR-007), line-not-found (404), not-your-claim (409), and — critically — the order becomes
      `Picked` only once every line is confirmed and stays `InProgress` until then (US1 AC2), in
      `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`
      (contracts/picking-api.md); include a case using a 1-line order, asserting the single confirm
      immediately sets status to Picked (spec.md Edge Cases — gap flagged by `/speckit-analyze`).
- [X] T016 [P] [US1] Write failing integration test for a concurrent "two employees attempt to
      record an outcome on the same line's order without holding its claim" race, mirroring
      feature 013's exclusive-claim concurrency test shape, in
      `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs` (FR-010,
      Constitution Principle VI — concurrency-safe server enforcement).
- [X] T017 [P] [US1] Write failing component test for a per-line "Picked" button and a position
      indicator ("3 of 5 lines confirmed") on `OrderDetailPage` in
      `frontend/tests/orders/OrderDetailPage.test.tsx` (US1 AC1, AC3; FR-012).
- [X] T018 [P] [US1] Write failing component test confirming a picker can revise an already-picked
      line's outcome before finishing the order, in the same file as T017 (US1 AC4, FR-004).

### Implementation for User Story 1

- [X] T019 [US1] Add `IPickingRepository` (`RecordOutcomeAsync(int orderLineId, int
      actorEmployeeId, PickOutcomeChange change, CancellationToken)`) and `PickOutcomeChange`
      (discriminated input: `Picked` | `IssueReport(...)` — Picked case only wired through in this
      story) in `backend/src/LootSingles.Application/Picking/IPickingRepository.cs` and
      `backend/src/LootSingles.Application/Picking/PickOutcomeChange.cs` (depends on Phase 2).
- [X] T020 [US1] Add `PickingOutcome` enum and `PickingResult` record (static factories:
      `Success(order)`, `OrderNotFound`, `LineNotFound`, `NotYourClaim`) mirroring
      `OrderClaimResult`'s pattern, in
      `backend/src/LootSingles.Application/Picking/PickingResult.cs`.
- [X] T021 [US1] Implement `PickingRepository.RecordOutcomeAsync` for the `Picked` case: one
      explicit transaction — conditional `ExecuteUpdateAsync` on `OrderLine` gated on
      `Order.ClaimedByEmployeeId == actorEmployeeId` (rows-affected check for `NotYourClaim`),
      then `ExecuteUpdateAsync` the parent `Order.Status` via `OrderStatusComputation`, re-read,
      commit — in `backend/src/LootSingles.Infrastructure/Persistence/PickingRepository.cs`
      (research.md §2; makes T014-T016's Picked-path assertions pass; depends on T009, T019, T020).
- [X] T022 [US1] Implement `PickingService.RecordPickedAsync(int orderLineId, int
      actorEmployeeId, CancellationToken)` — constructor-injects `IPickingRepository` and
      `ILogger<PickingService>`, logs the outcome at Information level (Constitution Principle XI)
      — in `backend/src/LootSingles.Application/Picking/PickingService.cs` (mirrors
      `OrderClaimService`'s shape; depends on T021).
- [X] T023 [US1] Extend `OrderLineDetail` record with the new `Id` field (closes the
      no-stable-line-identifier gap) in `backend/src/LootSingles.Application/Orders/OrderDetail.cs`,
      and update `OrderRepository.GetByIdAsync`'s projection to populate it, in
      `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs`
      (contracts/picking-api.md).
- [X] T024 [US1] Add `POST /api/orders/{orderId}/lines/{lineId}/pick` action to
      `OrdersController`, mapping `PickingOutcome` to HTTP results per contracts/picking-api.md's
      table, and extend `OrderLineDetailResponse` with `id` and `pickOutcome` fields, in
      `backend/src/LootSingles.Api/Controllers/OrdersController.cs` (makes T015, T016 pass;
      depends on T022, T023).
- [X] T025 [US1] Add `recordPicked(orderId, lineId)` to `frontend/src/features/orders/ordersApi.ts`
      and extend its `OrderLineDetail` type with `id`/`pickOutcome` (contracts/picking-api.md).
- [X] T026 [US1] Add a per-line "Picked" button and a "N of M lines confirmed" position indicator
      to `frontend/src/features/orders/OrderDetailPage.tsx`, wired to `recordPicked`, allowing
      re-recording an already-picked line (US1 AC4) — no confirmation dialog, one tap/click per
      FR-001/SC-008 — with supporting styles in
      `frontend/src/features/orders/OrderDetailPage.css` (makes T017, T018 pass; depends on T025).

**Checkpoint**: User Story 1 is fully functional and independently testable — a picker can confirm
every line of a claimed order and watch it become Picked.

---

## Phase 4: User Story 2 - Picker Reports a Picking Issue Instead of a False Confirmation (Priority: P1)

**Goal**: A picker who cannot fulfill a line reports a structured issue instead of falsely
confirming a pick; the order is never shown as Picked while any line has an unresolved issue.

**Independent Test**: As a picker who cannot fulfill a line, report an issue with a type and
optional note; confirm the order is never shown as Picked while that issue is unresolved (spec.md
US2 Independent Test).

### Tests for User Story 2

- [X] T027 [P] [US2] Write failing unit test for `PickingService.ReportIssueAsync` — success case
      (creates a `PickingIssue`, line's `CurrentPickingIssueId` points to it), `InvalidIssueType`,
      and `NotYourClaim` — in `backend/tests/LootSingles.UnitTests/Picking/PickingServiceTests.cs`
      (FR-002, FR-003, FR-010).
- [X] T028 [P] [US2] Write failing integration test for `POST
      /api/orders/{orderId}/lines/{lineId}/report-issue`: success (200, order status becomes
      `NeedsAttention` per FR-008 even with every other line confirmed — spec.md US2 AC2/AC3), and
      the exact 10-line/2-issue scenario from FR-008/spec.md US2 AC4 (order stays NeedsAttention
      after resolving one of two flagged lines, becomes Picked after resolving both), in
      `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`; include a case
      using a 1-line order, asserting the single issue report immediately sets status to
      NeedsAttention (spec.md Edge Cases — gap flagged by `/speckit-analyze`).
- [X] T029 [P] [US2] Write failing integration test asserting a superseded `PickingIssue` remains
      queryable after its line's outcome is later revised (FR-011 — history is retained, not
      erased), in the same file as T028.
- [X] T030 [P] [US2] Write failing component test for a "Report Issue" inline reveal-form (issue
      type select, optional required/found quantity, optional note) per line on `OrderDetailPage`,
      in `frontend/tests/orders/OrderDetailPage.test.tsx` (US2 AC1; mirrors the PIN-reset
      inline-form pattern from feature 014 per research.md §6).

### Implementation for User Story 2

- [X] T031 [US2] Extend `PickOutcomeChange`'s `IssueReport` case (issue type, optional required/
      found quantity, optional note) to be fully consumed, and extend
      `PickingRepository.RecordOutcomeAsync` to, inside the same transaction as T021's Picked path,
      insert a new `PickingIssue` row first, then point the line's `CurrentPickingIssueId` at it
      via the same conditional `ExecuteUpdateAsync`, in
      `backend/src/LootSingles.Application/Picking/PickOutcomeChange.cs` and
      `backend/src/LootSingles.Infrastructure/Persistence/PickingRepository.cs` (research.md §2;
      makes T027-T029 pass; depends on T021).
- [X] T032 [US2] Add `PickingService.ReportIssueAsync(int orderLineId, int actorEmployeeId,
      PickingIssueType issueType, int? requiredQuantity, int? foundQuantity, string? note,
      CancellationToken)`, validating `issueType` and returning `InvalidIssueType` for an
      unrecognized value, logging the outcome at Information level, in
      `backend/src/LootSingles.Application/Picking/PickingService.cs` (depends on T031).
- [X] T033 [US2] Add `POST /api/orders/{orderId}/lines/{lineId}/report-issue` action to
      `OrdersController` (request/response per contracts/picking-api.md), and extend
      `OrderLineDetailResponse` with the nested `currentIssue` object, in
      `backend/src/LootSingles.Api/Controllers/OrdersController.cs` (makes T028 pass; depends on
      T032).
- [X] T034 [US2] Add `reportIssue(orderId, lineId, request)` to
      `frontend/src/features/orders/ordersApi.ts` and extend `OrderLineDetail`'s type with
      `currentIssue` (contracts/picking-api.md).
- [X] T035 [US2] Add the "Report Issue" inline reveal-form to
      `frontend/src/features/orders/OrderDetailPage.tsx` (issue type select, optional
      required/found quantity fields, optional note, submit), wired to `reportIssue`, with
      supporting styles in `frontend/src/features/orders/OrderDetailPage.css` (makes T030 pass;
      depends on T034).

### Cross-Story Verification for User Story 1 + User Story 2

- [X] T036 [US1] Write and confirm an integration test for the full release→re-claim→revise
      cross-story flow (also exercises User Story 2's report-issue endpoint as part of the setup):
      claim a Ready order → report an issue on one line → confirm the remaining lines → release
      (status stays NeedsAttention, per T011) → re-claim via Choose Order (status stays
      NeedsAttention, per T012) → resolve the flagged line via `/pick` → assert order status
      becomes Picked with no separate "complete" action, in
      `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs` (spec.md US1
      AC5, FR-004's re-claim clause, SC-007 — gap flagged by `/speckit-analyze`: previously this
      flow was only manually validated via quickstart, not by an automated test, which falls short
      of Constitution Principle IV's required-automated-test standard; depends on T013, T024, T033).

**Checkpoint**: User Stories 1 AND 2 both work independently — the full core picking loop (confirm
or report, order auto-transitions to Picked or Needs Attention, FR-009 never violated) is
functional end to end, including across a release/re-claim cycle.

---

## Phase 5: User Story 3 - Everyone Can See Real Picking Progress Across All Orders (Priority: P2)

**Goal**: The Dashboard's In Progress, Needs Attention, and Picked tiles show accurate live counts,
and a Needs Attention order's flagged line(s) are visible without opening it.

**Independent Test**: As any employee, view the Dashboard; confirm accurate counts for each order
state, and that a Needs Attention order surfaces which line has an unresolved issue (spec.md US3
Independent Test).

### Tests for User Story 3

- [X] T037 [P] [US3] Write failing unit test for new `DashboardService` methods
      (`GetInProgressOrderSummariesAsync`, `GetNeedsAttentionOrderSummariesAsync` including
      `flaggedProductNames`, `GetPickedOrderSummariesAsync`/count) in
      `backend/tests/LootSingles.UnitTests/Dashboard/DashboardServiceTests.cs` (FR-013, FR-014).
- [X] T038 [P] [US3] Write failing integration test for `GET /api/dashboard` asserting the three
      new sections (`inProgress`, `needsAttention` with `flaggedProductNames`, `picked`) reflect
      real order state created via US1/US2 flows, in
      `backend/tests/LootSingles.IntegrationTests/Dashboard/DashboardControllerTests.cs`
      (contracts/picking-api.md, US3 AC1, AC2).
- [X] T039 [P] [US3] Write failing component test asserting `DashboardPage` renders live counts
      (not "Not yet available" placeholders) for the three tiles, and shows a Needs Attention
      order's flagged product name(s), in `frontend/tests/dashboard/DashboardPage.test.tsx`.

### Implementation for User Story 3

- [X] T040 [US3] Add `GetInProgressOrderSummariesAsync`, `GetNeedsAttentionOrderSummariesAsync`
      (returning a new `NeedsAttentionOrderSummary` extending `OrderSummary` with
      `FlaggedProductNames`), and `GetPickedOrderSummariesAsync`/count to `IDashboardRepository`
      and `DashboardRepository`, mirroring the existing `GetReadyOrderSummariesAsync` shape, in
      `backend/src/LootSingles.Application/Dashboard/IDashboardRepository.cs` and
      `backend/src/LootSingles.Infrastructure/Persistence/DashboardRepository.cs` (data-model.md;
      makes T037 pass).
- [X] T041 [US3] Add matching pass-through methods to `DashboardService` in
      `backend/src/LootSingles.Application/Dashboard/DashboardService.cs` (depends on T040).
- [X] T042 [US3] Extend `DashboardController`'s response with `inProgress`, `needsAttention`
      (including `flaggedProductNames`), and `picked` sections, replacing the frontend-only
      placeholder values, in `backend/src/LootSingles.Api/Controllers/DashboardController.cs`
      (contracts/picking-api.md; makes T038 pass; depends on T041).
- [X] T043 [US3] Extend `frontend/src/features/dashboard/dashboardApi.ts`'s `DashboardData` type
      with the three new sections, and wire `DashboardPage.tsx`'s three stub tiles
      (`frontend/src/features/dashboard/DashboardPage.tsx`) to the real data, showing flagged
      product names on the Needs Attention tile (makes T039 pass; depends on T042).
- [X] T044 [US3] Write failing component test asserting a claimed order with status
      `needsAttention` renders distinctly as "Needs Attention" — not
      `"In Progress · Picking by {name}"` — and that Ready/InProgress/Picked statuses render
      human-readable labels rather than raw camelCase enum strings, in the existing
      `frontend/tests/orders/OrdersPage.test.tsx` (FR-006, FR-008, FR-013 — gap flagged by
      `/speckit-analyze`: this file already exists but was previously unreferenced by any task; run
      and confirm it fails for the expected reason before T045).
- [X] T045 [US3] Fix `frontend/src/features/orders/OrdersPage.tsx`'s status column: check
      `order.status === 'needsAttention'` first (rendering it distinctly, e.g. "Needs Attention")
      before falling back to `"In Progress · Picking by {name}"` when claimed and otherwise picked;
      add a shared status→label map (Ready, In Progress, Needs Attention, Picked) in
      `frontend/src/features/orders/ordersApi.ts` or `OrdersPage.tsx` instead of rendering the raw
      camelCase enum string (FR-006, FR-008, FR-013 — today this file unconditionally shows a
      claimed order as "In Progress," which would silently mask a re-claimed Needs Attention
      order's unresolved problem on the main order list; makes T044 pass; depends on T013, T044).

**Checkpoint**: All three user stories are independently functional; the Dashboard's placeholder
tiles and the order list's status display are both fully accurate and closed out.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T046 [P] Write and confirm a Playwright E2E test covering the full happy path (claim → confirm
      every line → Picked) and the needs-attention-then-resolve path (claim → report issue on one
      line → confirm the rest → release → re-claim → resolve → Picked), at both a desktop and a
      simulated mobile (375px) viewport, in `frontend/e2e/pick-completion.spec.ts` (SC-001, SC-007,
      SC-008; mirrors the multi-context Playwright pattern from feature 013's claiming E2E tests).
- [ ] T047 Run every scenario in `specs/015-pick-completion/quickstart.md` manually against
      `scripts/start-dev.ps1`-started servers and record results (Definition of Done: Playwright
      validation performed for critical user flows; manual quickstart validation).
- [X] T048 Run the full backend (`dotnet test`) and frontend (`npm test`) suites and confirm 100%
      pass, with no regression against the T001 baseline count.
- [X] T049 Review every new/changed operation in `PickingService` and `DashboardService` against
      Constitution Principle XI's logging standard (ILogger<T>, Information-level, proportional,
      no PII) and confirm each warranted event is actually logged — this was designed into T022/
      T032 but must be re-verified against the finished code, not assumed from the design.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS both P1 user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational. Independently testable and shippable as the
  MVP on its own (a picker can confirm lines; issue reporting is not yet available, but nothing in
  US1 depends on US2).
- **User Story 2 (Phase 4)**: Depends on Foundational. Structurally extends the same
  `PickingRepository.RecordOutcomeAsync` method US1 built (T021 → T031), so in practice implement
  after US1, but its acceptance scenarios are independently verifiable once T031-T035 land. Phase 4
  also ends with a cross-story verification task (T036) that requires User Story 1's pick endpoint
  (T024) as well as User Story 2's report-issue endpoint (T033).
- **User Story 3 (Phase 5)**: Depends on Foundational, and functionally on US1 + US2 having
  produced real In Progress/Needs Attention/Picked data to display (spec.md US3 "Why this
  priority") — implement last among the three stories. T044-T045 (the order-list status-display
  fix and its test) depend only on T013, not on the rest of Phase 5.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests are written first and confirmed to fail for the expected reason before the corresponding
  implementation task (Constitution Principle IV).
- Domain/contract types before repository before service before controller before frontend API
  client before frontend UI — matches the dependency chain noted on each task above.

### Parallel Opportunities

- T002, T003, T004 (independent new enum files) can run in parallel.
- T011, T012 (independent test additions to the same file) should be written together but are
  logically independent of each other.
- Within Phase 3: T014-T018 (all test-writing tasks, different files) can run in parallel; T019-T020
  (new small files) can run in parallel with each other before T021 depends on both.
- Within Phase 4: T027-T030 can run in parallel.
- Within Phase 5: T037-T039 can run in parallel.
- T046 (E2E) is independent of T047-T049 and can run in parallel with them.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational) — required even for US1 alone, since `Order.Status` derivation
   and the corrected claim/release behavior are shared groundwork.
3. Complete Phase 3 (User Story 1).
4. **STOP and VALIDATE**: confirm a picker can claim an order and confirm every line, watching it
   become Picked, with existing tests green.

### Incremental Delivery

1. Setup + Foundational → shared foundation ready.
2. User Story 1 → validate independently → the core "happy path" picking loop exists.
3. User Story 2 → validate independently → the system now also correctly refuses to hide a
   picking problem behind a false Picked status (FR-009's central safety guarantee).
4. User Story 3 → validate independently → Dashboard placeholders are closed out.
5. Polish → E2E, full quickstart pass, full suite green, logging review.

## Notes

- [P] tasks touch different files with no dependency between them.
- Every implementation task's corresponding test task is listed immediately before it within its
  phase; run the test, confirm it fails for the expected reason, then implement — per Constitution
  Principle IV's Red → Green → Refactor cycle. Do not implement first and backfill tests.
- T021 and T031 both modify `PickingRepository.RecordOutcomeAsync` — US2's task extends rather than
  duplicates US1's method (the `IssueReport` branch of the same transactional flow), consistent
  with research.md §2's single-method design; do not create a second, parallel recording method.
- T036, T044, T045, and the added assertions on T012/T015/T028 were appended/amended via two rounds
  of `/speckit-analyze` remediation (2026-08-27): T036 automates the release→re-claim→revise→Picked
  flow (previously validated only manually via quickstart T047); T044/T045 fix a pre-existing
  display bug in `OrdersPage.tsx` that would otherwise mask a re-claimed Needs Attention order as
  merely "In Progress" — T044 is the required failing-test-first step (Constitution Principle IV),
  added in the second remediation round after the first round's T044 was found to lack one; the
  T012/T015/T028 additions close single-line-order and Pick-Next-exclusion coverage gaps.
- Commit after each task or logical group, per the standing project convention of asking for
  confirmation before every `git commit` (including local, reversible commits).
