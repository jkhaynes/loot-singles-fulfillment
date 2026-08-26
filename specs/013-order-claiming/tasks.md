---

description: "Task list for Exclusive Order Claiming"
---

# Tasks: Exclusive Order Claiming

**Input**: Design documents from `/specs/013-order-claiming/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/order-claiming-api.md, quickstart.md

**Tests**: Required. Constitution Principle IV mandates strict Red → Green → Refactor for all new
or modified application behavior; every test task below MUST be written and confirmed failing
before its paired implementation task.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P2/P3) so each story is
independently implementable, testable, and demonstrable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story this task belongs to (US1–US5)

## Path Conventions (existing web app structure — see plan.md Project Structure)

- Backend: `backend/src/LootSingles.{Domain,Application,Infrastructure,Api}/`
- Backend tests: `backend/tests/LootSingles.{UnitTests,IntegrationTests}/`
- Frontend: `frontend/src/`, `frontend/e2e/`

---

## Phase 1: Setup

- [X] T001 Verify EF Core tooling is available for generating a new migration (`dotnet ef --version` from `backend/src/LootSingles.Api`) — no new NuGet/npm dependencies are needed for this feature (plan.md Technical Context)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared domain/schema/outcome-type changes every user story depends on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete.

- [X] T002 [P] Add `ClaimedByEmployeeId` (`int?`), `ClaimedAt` (`DateTimeOffset?`), and a `ClaimedByEmployee` (`Employee?`) navigation property to `Order` in `backend/src/LootSingles.Domain/Orders/Order.cs` (data-model.md)
- [X] T003 [P] Add `InProgress = 1` to `OrderStatus` in `backend/src/LootSingles.Domain/Orders/OrderStatus.cs` (data-model.md)
- [X] T004 [P] Define `OrderClaimOutcome` enum (`Success, OrderNotFound, AlreadyClaimed, NoOrdersAvailable, EmployeeHasActiveClaim, NotYourClaim, OrderNotClaimed`) and an `OrderClaimResult` record (`Outcome`, `Order? Order = null`, `int? ConflictingOrderId = null`) with static factories, in new `backend/src/LootSingles.Application/Orders/OrderClaimResult.cs` — mirror the shape of `backend/src/LootSingles.Application/Auth/EmployeeManagementResult.cs`
- [X] T005 Add a filtered unique index on `ClaimedByEmployeeId` (`.HasIndex(o => o.ClaimedByEmployeeId).IsUnique().HasFilter("[ClaimedByEmployeeId] IS NOT NULL")`) in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/OrderConfiguration.cs` (research.md §3) — depends on T002
- [X] T006 Generate the EF Core migration `AddOrderClaiming` (`dotnet ef migrations add AddOrderClaiming --project backend/src/LootSingles.Infrastructure --startup-project backend/src/LootSingles.Api`) adding the two new `Orders` columns and the filtered unique index — depends on T002, T003, T005
- [X] T007 [P] Update the expected migration list in `Clean_database_has_all_migrations_in_assembly_order_and_matches_current_model` in `backend/tests/LootSingles.IntegrationTests/Persistence/MigrationTests.cs` to include the new `AddOrderClaiming` migration — depends on T006
- [X] T008 [P] Update `Schema_contains_behaviorally_relevant_unique_and_lookup_indexes` in `backend/tests/LootSingles.IntegrationTests/Persistence/SqlServerSchemaTests.cs` to assert the new `IX_Orders_ClaimedByEmployeeId` unique index exists — depends on T006
- [X] T009 [P] Add `Model_ForOrder_HasUniqueIndexOnClaimedByEmployeeId` to `backend/tests/LootSingles.IntegrationTests/Persistence/OrderConfigurationTests.cs`, mirroring the existing `Model_ForOrder_HasUniqueIndexOnTcgplayerOrderId` test — depends on T005

**Checkpoint**: Domain model, migration, and outcome types exist — user story phases can begin.

---

## Phase 3: User Story 1 - Pick Next Order Assigns Exclusively (Priority: P1) 🎯 MVP

**Goal**: A picker presses "Pick Next Order" and is exclusively assigned the oldest available
order; two pickers pressing it at the same time never receive the same order; no-orders-available
and already-has-a-claim are both clear, safe outcomes (spec.md US1, FR-001/FR-003/FR-006/FR-009).

**Independent Test**: With ≥2 available orders, have two employees press "Pick Next Order" at
approximately the same time and confirm they are assigned two different orders.

### Tests for User Story 1 ⚠️ Write first, confirm failing before implementing

- [X] T010 [P] [US1] Add failing unit tests for `OrderClaimService.PickNextAsync` (assigns the oldest Ready+unclaimed order; returns `NoOrdersAvailable` when none exist; returns `EmployeeHasActiveClaim` when the actor already holds a claim) in `backend/tests/LootSingles.UnitTests/Orders/OrderClaimServiceTests.cs`, using a fake `IOrderRepository`
- [X] T011 [P] [US1] Add failing integration tests for `POST /api/orders/pick-next` (200 success, 409 `no_orders_available`, 409 `employee_has_active_claim`, 401) per `contracts/order-claiming-api.md` in `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerClaimingTests.cs`
- [X] T012 [US1] Add a failing real-database concurrency test — two simultaneous `PickNextAsync` calls (`Task.WhenAll`) with two available orders always assign two different orders and never both succeed on the same order — in `backend/tests/LootSingles.IntegrationTests/Persistence/OrderClaimConcurrencyTests.cs`, following the `[Collection(SqlServerTestCollection.Name)]` / `SqlServerContainerFixture` pattern in `SqlServerConcurrencyTests.cs`

### Implementation for User Story 1

- [X] T013 [US1] Add `ClaimNextAvailableAsync(int actorEmployeeId, CancellationToken)` to `IOrderRepository` (`backend/src/LootSingles.Application/Orders/IOrderRepository.cs`) and `OrderRepository` (`backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs`): select the oldest `Ready`+unclaimed order id, attempt the conditional `ExecuteUpdateAsync` (`WHERE Id == candidateId && ClaimedByEmployeeId == null`, setting `ClaimedByEmployeeId`, `ClaimedAt`, `Status = InProgress`), retry with the next-oldest candidate up to 5 attempts on 0-rows-affected, return the claimed `Order` or `null` if none available (research.md §1–2)
- [X] T014 [US1] Add `GetActiveClaimedOrderIdAsync(int employeeId, CancellationToken)` to `IOrderRepository`/`OrderRepository` — `AsNoTracking` lookup returning the id of the order the employee currently claims, or `null` (returns the id directly rather than a bare bool, since the API contract needs `claimedOrderId` in the `employee_has_active_claim` response body — same underlying check as originally planned, one round trip)
- [X] T015 [US1] Implement `OrderClaimService.PickNextAsync(int actorEmployeeId, CancellationToken)` in new `backend/src/LootSingles.Application/Orders/OrderClaimService.cs`: check `GetActiveClaimedOrderIdAsync` first → `EmployeeHasActiveClaim`; else call `ClaimNextAvailableAsync` → `null` maps to `NoOrdersAvailable`, non-null maps to `Success`; catch the unique-index violation (research.md §3) and map to `EmployeeHasActiveClaim` as the race backstop — depends on T004, T013, T014
- [X] T016 [US1] Add `POST /api/orders/pick-next` to `backend/src/LootSingles.Api/Controllers/OrdersController.cs`, mapping `OrderClaimOutcome` to the HTTP responses in `contracts/order-claiming-api.md`, using the existing `ActorEmployeeId()` helper pattern — depends on T015
- [X] T017 [US1] Add structured `ILogger<T>` logging in `OrderClaimService.cs` for pick-next success, no-orders-available, and lost-race outcomes (constitution Principle XI; no PII beyond internal employee/order ids)

**Checkpoint**: User Story 1 is fully functional and independently testable/demoable (MVP).

---

## Phase 4: User Story 2 - Choose a Specific Order (Priority: P2)

**Goal**: A picker freely claims a specific available order; claiming an already-claimed order is
safely rejected without disturbing the existing claim, including races against "Pick Next Order"
(spec.md US2, FR-002/FR-003/FR-004/FR-009).

**Independent Test**: With one order already claimed by picker A, have picker B attempt to choose
that same order (rejected) and a different, unclaimed order (succeeds).

### Tests for User Story 2 ⚠️ Write first, confirm failing before implementing

- [X] T018 [P] [US2] Add failing unit tests for `OrderClaimService.ClaimAsync(orderId, actorEmployeeId)` (`Success`, `OrderNotFound`, `AlreadyClaimed`, `EmployeeHasActiveClaim`) in `OrderClaimServiceTests.cs`
- [X] T019 [P] [US2] Add failing integration tests for `POST /api/orders/{orderId}/claim` (200, 404, 409 `order_already_claimed` with claimant fields, 409 `employee_has_active_claim`) in `OrdersControllerClaimingTests.cs`
- [X] T020 [US2] Add failing real-database concurrency tests in `OrderClaimConcurrencyTests.cs`: (a) two simultaneous `ClaimAsync` calls against the same order id yield exactly one `Success` and one `AlreadyClaimed`; (b) one `PickNextAsync` and one `ClaimAsync` targeting the same order at the same time still yield exactly one winner (spec.md Edge Cases §2)

### Implementation for User Story 2

- [X] T021 [US2] Add `ClaimSpecificAsync(int orderId, int actorEmployeeId, CancellationToken)` to `IOrderRepository`/`OrderRepository`: the same conditional `ExecuteUpdateAsync` predicate as T013 but targeting a caller-supplied `orderId`; on 0-rows-affected, load the order (with `ClaimedByEmployee`) to distinguish "doesn't exist" from "already claimed by X" — returns a new `ClaimAttemptResult(bool Succeeded, Order? Order)` so the service can distinguish all three outcomes from one repository call
- [X] T022 [US2] Implement `OrderClaimService.ClaimAsync(int orderId, int actorEmployeeId, CancellationToken)`, reusing `GetActiveClaimedOrderIdAsync` and `ClaimSpecificAsync`, mapping outcomes per `contracts/order-claiming-api.md` — depends on T014, T021
- [X] T023 [US2] Add `POST /api/orders/{orderId}/claim` to `OrdersController.cs` — depends on T022
- [X] T024 [US2] Add logging for claim success/already-claimed/lost-race outcomes in `OrderClaimService.cs`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - See Who Is Already Working an Order (Priority: P2)

**Goal**: Every employee can see, for any order, whether it's claimed and by whom (spec.md US3,
FR-005, SC-003).

**Independent Test**: With one order claimed, a second employee views the order list and detail
view and sees that order marked in-progress with the claimant's name.

### Tests for User Story 3 ⚠️ Write first, confirm failing before implementing

- [X] T025 [P] [US3] Add failing unit tests to `backend/tests/LootSingles.UnitTests/Orders/OrdersServiceTests.cs` asserting `OrderListItem`/`OrderDetail` carry claim state for both a claimed and an unclaimed order
- [X] T026 [P] [US3] Add failing integration tests to `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs` asserting `GET /api/orders` and `GET /api/orders/{id}` include `claimedByEmployeeId`/`claimedByEmployeeName` (`null` when unclaimed) per `contracts/order-claiming-api.md`

### Implementation for User Story 3

- [X] T027 [US3] Extend `OrderListItem`/`OrderDetail` (`backend/src/LootSingles.Application/Orders/`) and `OrderRepository.GetAllAsync`/`GetByIdAsync` (`backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs`) to project `ClaimedByEmployeeId` and the claimant's `DisplayName` via the `ClaimedByEmployee` navigation from T002
- [X] T028 [US3] Extend `OrderResponse`/`OrderDetailResponse` in `OrdersController.cs` with `ClaimedByEmployeeId`/`ClaimedByEmployeeName` — depends on T027
- [X] T029 [P] [US3] Frontend: show an "In Progress · Picking by <name>" badge on the order list in `frontend/src/features/orders/OrdersPage.tsx` and the header of `frontend/src/features/orders/OrderDetailPage.tsx`, matching the PRD's example text (the frontend is React/`.tsx`, not Vue — plan.md corrected to match; see note there)
- [X] T030 [P] [US3] Frontend: extend the order response types in `frontend/src/features/orders/ordersApi.ts` with the new claim fields

**Checkpoint**: User Stories 1–3 all work independently.

---

## Phase 6: User Story 4 - Release a Claimed Order (Priority: P1)

**Goal**: A picker releases their own claim, freeing the order for anyone (including themselves,
for a different order); only the claimant may release it (spec.md US4, FR-008).

**Independent Test**: A picker releases an order they hold; it becomes available and claimable by
a different picker.

### Tests for User Story 4 ⚠️ Write first, confirm failing before implementing

- [ ] T031 [P] [US4] Add failing unit tests for `OrderClaimService.ReleaseAsync` (`Success`, `OrderNotFound`, `NotYourClaim` for both "unclaimed" and "claimed by someone else") in `OrderClaimServiceTests.cs`
- [ ] T032 [P] [US4] Add failing integration tests for `POST /api/orders/{orderId}/release` (200, 404, 409 `not_your_claim`) in `OrdersControllerClaimingTests.cs`
- [ ] T033 [US4] Add a failing real-database concurrency test in `OrderClaimConcurrencyTests.cs`: simultaneous `ReleaseAsync` (by the claimant) and `ForceReleaseAsync` (by a manager) against the same claimed order — exactly one succeeds, the order ends released exactly once, the other gets a "no longer claimed" outcome (spec.md Edge Cases §4)

### Implementation for User Story 4

- [ ] T034 [US4] Add `ReleaseAsync(int orderId, int actorEmployeeId, CancellationToken)` to `IOrderRepository`/`OrderRepository`: conditional `ExecuteUpdateAsync` (`WHERE Id == orderId && ClaimedByEmployeeId == actorEmployeeId`, clearing `ClaimedByEmployeeId`/`ClaimedAt`, setting `Status = Ready`); on 0-rows-affected, load the order to distinguish "doesn't exist" from "not your claim"
- [ ] T035 [US4] Implement `OrderClaimService.ReleaseAsync(int orderId, int actorEmployeeId, CancellationToken)` mapping outcomes per `contracts/order-claiming-api.md` — depends on T034
- [ ] T036 [US4] Add `POST /api/orders/{orderId}/release` to `OrdersController.cs` — depends on T035
- [ ] T037 [US4] Add logging for release success/rejected outcomes in `OrderClaimService.cs`
- [ ] T038 [P] [US4] Frontend: add a "Release" action to `frontend/src/features/orders/OrderDetailPage.tsx`, visible only when `claimedByEmployeeId` matches the signed-in employee, calling a new `releaseOrder` function in `frontend/src/features/orders/ordersApi.ts`

**Checkpoint**: User Stories 1–4 all work independently; the claim lifecycle is fully usable by a
picker end-to-end.

---

## Phase 7: User Story 5 - Manager Force-Releases a Stuck Claim (Priority: P3)

**Goal**: A Manager/Admin can force-release any employee's claim; non-managers cannot (spec.md
US5, FR-010).

**Independent Test**: A manager force-releases an order claimed by a picker; it becomes available
and the original claimant no longer holds it. A non-manager's attempt is rejected.

### Tests for User Story 5 ⚠️ Write first, confirm failing before implementing

- [ ] T039 [P] [US5] Add failing unit tests for `OrderClaimService.ForceReleaseAsync` (`Success`, `OrderNotFound`, `OrderNotClaimed`) in `OrderClaimServiceTests.cs`
- [ ] T040 [P] [US5] Add failing integration tests for `POST /api/orders/{orderId}/force-release` (200, 404, 409 `order_not_claimed`, 403 for a non-Manager/Admin caller) in `OrdersControllerClaimingTests.cs`

### Implementation for User Story 5

- [ ] T041 [US5] Add `ForceReleaseAsync(int orderId, CancellationToken)` to `IOrderRepository`/`OrderRepository`: conditional `ExecuteUpdateAsync` (`WHERE Id == orderId && ClaimedByEmployeeId != null`, clearing claim fields, setting `Status = Ready`); on 0-rows-affected, load the order to distinguish "doesn't exist" from "not currently claimed"
- [ ] T042 [US5] Implement `OrderClaimService.ForceReleaseAsync(int orderId, CancellationToken)` mapping outcomes per `contracts/order-claiming-api.md` — depends on T041
- [ ] T043 [US5] Add `POST /api/orders/{orderId}/force-release` to `OrdersController.cs` with `[Authorize(Roles = nameof(EmployeeRole.ManagerAdmin))]` on the action, mirroring `EmployeesController` — depends on T042
- [ ] T044 [US5] Add logging for force-release outcomes in `OrderClaimService.cs`, including which manager force-released which employee's claim
- [ ] T045 [P] [US5] Frontend: add a Manager/Admin-only "Force-Release" action to `frontend/src/features/orders/OrderDetailPage.tsx`, visible only when the signed-in employee's role is `ManagerAdmin` and the order is claimed by someone else, calling a new `forceReleaseOrder` function in `frontend/src/features/orders/ordersApi.ts`

**Checkpoint**: All 5 user stories are independently functional. Feature is complete per spec.md.

---

## Phase 8: Polish & Cross-Cutting Validation

- [ ] T046 Extend `backend/tests/LootSingles.E2EHost/Program.cs`'s `SeedAsync` with a second employee (a `Picker`, e.g. `e2epicker`/"E2E Picker") and one or two additional `Ready` orders dedicated to claiming tests (e.g. `E2E-ORDER-00002`/`00003`) — needed so `order-claiming.spec.ts` can exercise claim/release as a non-manager and force-release by the manager against a *different* employee's claim, without touching `E2E-ORDER-00001`, which `frontend/e2e/order-detail.spec.ts` already depends on staying `Ready`/unclaimed
- [ ] T047 [P] Add `frontend/e2e/order-claiming.spec.ts` covering: claim via each entry point → visible as in-progress in a second session → release; and the Manager force-release path — using two authenticated browser contexts, per `quickstart.md` — depends on T046
- [ ] T048 Run the full manual validation in `quickstart.md` (all 9 scenarios, including the two-session concurrent-race scenario)
- [ ] T049 Run the full automated validation suite from `quickstart.md` (`dotnet build`, both `dotnet test` projects, `dotnet csharpier check`, `npm run lint/test/build/format:check/test:e2e`) and confirm everything passes with zero regressions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story.
- **User Stories (Phase 3–7)**: All depend on Foundational completion. US1 and US2 share the same
  underlying conditional-update primitive and are best implemented in priority order (US1 → US2)
  since US2 reuses `HasActiveClaimAsync` from US1; US3 only needs Foundational; US4 and US5 both
  read/write claim state established by US1/US2 and are naturally sequenced after them, though
  their own tests can be drafted in parallel.
- **Polish (Phase 8)**: Depends on all five user stories being complete.

### Within Each User Story

- Tests are written and confirmed failing before implementation (constitution Principle IV).
- Repository methods before service methods; service methods before controller endpoints.
- Backend endpoint before its corresponding frontend UI task.

### Parallel Opportunities

- T002, T003, T004 (Foundational) touch different files and can run in parallel.
- T007, T008, T009 (Foundational) touch different test files and can run in parallel once T005/T006 are done.
- Within each user story, the `[P]`-marked test tasks (different files) can run in parallel; the `[P]`-marked frontend tasks can run in parallel with each other once their backend endpoint exists.
- US3's tasks have no dependency on US1/US2/US4/US5 beyond Foundational and can be staffed in parallel with them.

---

## Parallel Example: User Story 1

```bash
# Tests (different files):
Task: "Unit tests for OrderClaimService.PickNextAsync in backend/tests/LootSingles.UnitTests/Orders/OrderClaimServiceTests.cs"
Task: "Integration tests for POST /api/orders/pick-next in backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerClaimingTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (US1 — Pick Next Order).
3. **STOP and VALIDATE**: run T012's concurrency test and quickstart.md scenarios 1–2, 4–5 manually.
4. This alone proves the constitution's Principle VI MUST for the primary entry point.

### Incremental Delivery

1. Setup + Foundational → schema and outcome types ready.
2. US1 (P1) → Pick Next Order is exclusive and safe → demoable MVP.
3. US2 (P2) → Choose Order reuses the same safety guarantee.
4. US3 (P2) → claim visibility makes US1/US2 useful in a multi-picker setting.
5. US4 (P1) → release makes the claim lifecycle actually usable day-to-day.
6. US5 (P3) → manager safety net for stuck claims.
7. Polish → E2E coverage and full quickstart validation.

## Notes

- No task in this list touches guided picking, issue reporting, or pick completion — those remain
  future features per FR-011.
- Every write path (pick-next, claim, release, force-release) is a single conditional
  `ExecuteUpdateAsync`, never a load-track-`SaveChangesAsync` — per the constitution's EF Core
  Engineering Standards and research.md §1.
