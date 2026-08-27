---

description: "Task list for Manager Admin Screen"
---

# Tasks: Manager Admin Screen

**Input**: Design documents from `/specs/014-manager-admin-screen/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/employee-management-api.md, quickstart.md

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
- Frontend: `frontend/src/features/admin/`, `frontend/tests/admin/`, `frontend/e2e/`

---

## Phase 1: Setup

- [X] T001 Confirm no new dependencies or database migration are needed: `EmployeeManagementOutcome.WouldRemoveLastManagerAdmin` has no schema impact at all; `EmployeeAuditActionType.RoleChanged` is stored via the existing `.HasConversion<string>()` into an unbounded `nvarchar(max)` `ActionType` column (verified in the migration/designer files — corrected in data-model.md, which had wrongly assumed a raw `int` column), so a new value needs no migration; `sp_getapplock` (research.md §1) is already used in this codebase via `EmployeeRepository.TryAddFirstEmployeeAsync` — no new package references required. Also confirmed no test hardcodes the exhaustive set of `ActionType` values (checked `SqlServerQueryTranslationTests.cs` and `MigrationTests.cs`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared outcome type and concurrency guard both US3 (deactivation) and US4 (role
change) depend on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete.

- [X] T002 [P] Add `WouldRemoveLastManagerAdmin` to `EmployeeManagementOutcome` and a matching `public static readonly EmployeeManagementResult WouldRemoveLastManagerAdmin` factory in `backend/src/LootSingles.Application/Auth/EmployeeManagementResult.cs` (data-model.md), mirroring the existing `NotFound`/`UsernameTaken`/`InvalidRequest` pattern
- [X] T003 Add `Task<bool> SaveChangesGuardingLastManagerAdminAsync(int excludingEmployeeId, CancellationToken)` to `IEmployeeRepository` (`backend/src/LootSingles.Application/Auth/IEmployeeRepository.cs`) and implement it in `EmployeeRepository` (`backend/src/LootSingles.Infrastructure/Persistence/EmployeeRepository.cs`): begin a transaction, acquire the named `sp_getapplock` resource `"LootSingles.Employees.LastManagerAdminGuard"`, count active Manager/Admin employees excluding `excludingEmployeeId`, and — only if at least one would remain — save pending changes and commit, returning whether it saved. **Renamed and reshaped from the originally-planned standalone bool-returning guard check**: a separate "check" call followed by a separate `SaveChangesAsync()` call would reopen the exact TOCTOU race feature 013's `/code-design-review` (M1) already found and fixed once this session — the count and the save must happen inside the same held lock/transaction to be genuinely race-free, so the guard method now performs both. Also added a fake implementation to the three existing `FakeEmployeeRepository` test doubles (`EmployeeManagementServiceTests.cs` gets a real in-memory guard simulation since it will exercise this in US3/US4; `BootstrapAdminServiceTests.cs`/`AuthenticationServiceTests.cs` get a `NotSupportedException` stub since they never call it).

**Checkpoint**: Outcome type and guard primitive exist — user story phases can begin. (The guard
has no dedicated *behavioral* test yet — it is exercised and tested via its first real caller in
US3; full backend suite re-run clean at 341/341 after this phase.)

---

## Phase 3: User Story 1 - Manager Views the Employee Roster (Priority: P1) 🎯 MVP

**Goal**: A Manager/Admin can open a dedicated admin screen and see every employee — active and
inactive — with role and status; a Picker cannot reach it. No backend work is needed — `GET
/api/employees` already returns everything required (contracts/employee-management-api.md); this
story is the first-ever UI for it.

**Independent Test**: As a Manager/Admin, open the admin screen and confirm the full roster is
listed with username, display name, role, and active/locked status. As a Picker, confirm the
screen is not reachable.

### Tests for User Story 1 ⚠️ Write first, confirm failing before implementing

- [X] T004 [P] [US1] Add a failing frontend component test for a `RequireManagerAdmin` route-guard component in `frontend/tests/admin/RequireManagerAdmin.test.tsx`: renders its children when `employee.role === 'ManagerAdmin'`, redirects (or shows an access-denied state) otherwise
- [X] T005 [P] [US1] Add a failing frontend component test for `AdminPage.tsx` in `frontend/tests/admin/AdminPage.test.tsx`: renders every employee returned by a mocked `listEmployees()` call, showing username, display name, role, and active/locked status for both active and inactive employees

### Implementation for User Story 1

- [X] T006 [US1] Create `frontend/src/features/admin/adminApi.ts` with an `EmployeeListItem` type and a `listEmployees()` function calling `GET /api/employees` (contracts/employee-management-api.md), following the existing `ordersApi.ts`/`dashboardApi.ts` conventions
- [X] T007 [US1] Create `frontend/src/features/admin/AdminPage.tsx` and `AdminPage.css` rendering the roster (username, display name, role, active/locked status) fetched via `listEmployees()`, following the existing `OrdersPage.tsx` list-rendering pattern
- [X] T008 [US1] Add a `RequireManagerAdmin` route-guard component and register the `/admin` route behind it in `frontend/src/App.tsx` (research.md §4). Also made the guard wait for `useAuth()`'s `isLoading` to resolve before evaluating the role, rather than assuming its caller already gates on that (defensive — correct regardless of where the guard is mounted, confirmed via a direct-render test that isn't wrapped in App's own loading gate).
- [X] T009 [P] [US1] Add a "Manage Employees" link to `frontend/src/features/dashboard/DashboardPage.tsx`'s header actions, visible only when `employee.role === 'ManagerAdmin'`, targeting `/admin` — following the same conditional-visibility pattern already used for Force-Release in `OrderDetailPage.tsx`

**Checkpoint**: User Story 1 is fully functional and independently testable/demoable (MVP — the
screen exists and is reachable only by a Manager/Admin, even though it has no actions yet).

---

## Phase 4: User Story 2 - Manager Creates a New Employee Account (Priority: P1)

**Goal**: A Manager/Admin can create a new employee account from the admin screen. No backend work
is needed — `POST /api/employees` already exists and is fully tested (feature 002); this story is
the first-ever UI for it.

**Independent Test**: As a Manager/Admin, create a new employee with a unique username, initial
PIN, and role; confirm it appears in the roster and that employee can log in. Attempt a duplicate
username and confirm a clear rejection.

### Tests for User Story 2 ⚠️ Write first, confirm failing before implementing

- [X] T010 [US2] Add failing frontend component tests to `AdminPage.test.tsx`: submitting the create-employee form with a mocked successful `createEmployee()` call refreshes the roster to include the new employee; a mocked duplicate-username rejection shows a clear conflict message and does not add a row

### Implementation for User Story 2

- [X] T011 [US2] Add a `CreateEmployeeRequest` type and `createEmployee(username, displayName, initialPin, role)` function to `adminApi.ts`, calling `POST /api/employees` and handling the `409 username_taken`/`400 invalid_request` responses with typed errors (mirroring `ordersApi.ts`'s typed-error-class pattern). Note: implemented as typed `UsernameTakenError`/`InvalidEmployeeRequestError` classes per the mirrored pattern; a standalone `CreateEmployeeRequest` type was not needed since `createEmployee` takes plain positional parameters (matching the task's own function signature) rather than an object.
- [X] T012 [US2] Add a "Create Employee" form to `AdminPage.tsx` (username, display name, initial PIN, role fields), calling `createEmployee`, refreshing the roster on success, and showing the duplicate-username error inline

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Manager Removes or Restores an Employee (Priority: P1)

**Goal**: A Manager/Admin can remove (deactivate) an active employee and restore (reactivate) a
previously removed one from the admin screen, and the system refuses to remove the last active
Manager/Admin. `POST /api/employees/{id}/reactivate` already exists unchanged (feature 002); this
story adds the FR-007 guard to `deactivate` and is the first-ever UI for both actions.

**Independent Test**: As a Manager/Admin, remove an active employee and confirm they can no longer
log in; restore them and confirm they can log in again. With only one active Manager/Admin,
confirm removing it is rejected.

### Tests for User Story 3 ⚠️ Write first, confirm failing before implementing

- [ ] T013 [P] [US3] Add failing unit tests to `backend/tests/LootSingles.UnitTests/Auth/EmployeeManagementServiceTests.cs`: `DeactivateAsync` returns `WouldRemoveLastManagerAdmin` (and leaves the account active) when the target is the only active Manager/Admin; still succeeds when another active Manager/Admin remains; still succeeds deactivating a Picker regardless of manager count
- [ ] T014 [P] [US3] Add failing integration tests to `backend/tests/LootSingles.IntegrationTests/Auth/EmployeesControllerTests.cs`: `POST /api/employees/{id}/deactivate` returns `409 { "error": "would_remove_last_manager_admin" }` per contracts/employee-management-api.md when it's the last active Manager/Admin
- [ ] T015 [US3] Add a failing real-database concurrency test in a new `backend/tests/LootSingles.IntegrationTests/Persistence/EmployeeLastManagerAdminConcurrencyTests.cs`: with exactly two active Manager/Admin employees, two simultaneous `DeactivateAsync` calls — one per employee — never both succeed (exactly one succeeds, the other returns `WouldRemoveLastManagerAdmin`), mirroring the real-concurrency pattern in `BootstrapAdminCommandTests.cs`'s `ExecuteAsync_ConcurrentBootstrapAttemptsOnSameEmptyDatabase_ExactlyOneSucceeds`
- [ ] T016 [P] [US3] Add failing frontend component tests to `AdminPage.test.tsx`: a "Remove" action per active-employee row calls `deactivateEmployee` and the row shows inactive afterward; a "Restore" action per inactive-employee row calls `reactivateEmployee` and the row shows active afterward; a mocked `would_remove_last_manager_admin` rejection shows a clear message and leaves the row unchanged

### Implementation for User Story 3

- [ ] T017 [US3] In `EmployeeManagementService.DeactivateAsync` (`backend/src/LootSingles.Application/Auth/EmployeeManagementService.cs`), call `TryGuardLastManagerAdminAsync` (T003) before applying the deactivation; return `EmployeeManagementResult.WouldRemoveLastManagerAdmin` if it would leave zero active Manager/Admin employees, otherwise proceed as today — depends on T002, T003
- [ ] T018 [US3] In `EmployeesController.Deactivate` (`backend/src/LootSingles.Api/Controllers/EmployeesController.cs`), map `EmployeeManagementOutcome.WouldRemoveLastManagerAdmin` to `409 Conflict` with `{ "error": "would_remove_last_manager_admin" }` — depends on T017
- [ ] T019 [P] [US3] Add `deactivateEmployee(id)`/`reactivateEmployee(id)` functions to `adminApi.ts`, calling the existing `POST /api/employees/{id}/deactivate`/`/reactivate` endpoints and handling the new `409` typed error
- [ ] T020 [US3] Add "Remove"/"Restore" actions per roster row in `AdminPage.tsx` (Remove shown for active employees, Restore for inactive), refreshing the roster on success and showing the blocked-removal error inline — depends on T019

**Checkpoint**: User Stories 1–3 all work independently; the core roster-management loop (view,
create, remove, restore) is fully usable end-to-end.

---

## Phase 6: User Story 4 - Manager Changes an Employee's Role (Priority: P2)

**Goal**: A Manager/Admin can change an existing employee's role between Picker and Manager/Admin;
this immediately invalidates that employee's active session; the system refuses a change that
would leave zero active Manager/Admin employees. This is genuinely new capability — no equivalent
existed before this feature.

**Independent Test**: As a Manager/Admin, promote a Picker to Manager/Admin and confirm their next
login reflects it, and that any of their already-active sessions are invalidated immediately.
Demote a Manager/Admin to Picker and confirm the reverse. With only one active Manager/Admin,
confirm changing their own role away from Manager/Admin is rejected.

### Tests for User Story 4 ⚠️ Write first, confirm failing before implementing

- [ ] T021 [P] [US4] Add failing unit tests to `EmployeeManagementServiceTests.cs`: `ChangeRoleAsync` returns `Success` and updates `Role` for a valid promotion/demotion; returns `NotFound` for a nonexistent employee; returns `WouldRemoveLastManagerAdmin` (leaving the role unchanged) when demoting the last active Manager/Admin; is a no-op `Success` (still recording an audit event) when the requested role matches the employee's current role
- [ ] T022 [P] [US4] Add failing integration tests to `EmployeesControllerTests.cs`: `POST /api/employees/{id}/change-role` returns `200` with the updated role, `404` for a nonexistent employee, `400` for a missing/invalid `role` value, `409 would_remove_last_manager_admin` when applicable, and `403` for a non-Manager/Admin caller, per contracts/employee-management-api.md
- [ ] T023 [US4] Add failing tests to `backend/tests/LootSingles.IntegrationTests/Auth/SessionInvalidationTests.cs`: extend the synthetic-principal `ValidatePrincipal` test helper to accept a `ClaimTypes.Role` claim, then add `ValidatePrincipal_EmployeeRoleChangedSinceIssuance_RejectsThePrincipal` (cookie's role claim no longer matches the employee's current role) and `ValidatePrincipal_EmployeeRoleUnchanged_AcceptsThePrincipal`; also add a full-endpoint test mirroring `Deactivation_ViaRealEndpoint_InvalidatesAlreadyActiveSessionOnNextRequest`, but changing the target's role via `POST /api/employees/{id}/change-role` instead of deactivating
- [ ] T024 [P] [US4] Add failing frontend component tests to `AdminPage.test.tsx`: a role-change control per row calls `changeEmployeeRole` and the row shows the new role afterward; a mocked `would_remove_last_manager_admin` rejection shows a clear message and leaves the role unchanged

### Implementation for User Story 4

- [ ] T025 [US4] Add `RoleChanged = 7` to `EmployeeAuditActionType` (`backend/src/LootSingles.Domain/Employees/EmployeeAuditActionType.cs`, data-model.md)
- [ ] T026 [US4] Add `ChangeRoleAsync(int actorEmployeeId, int targetEmployeeId, EmployeeRole newRole, CancellationToken)` to `EmployeeManagementService.cs`: look up the target, return `NotFound` if missing; if `newRole` differs from the current role and the current role is `ManagerAdmin`, call `TryGuardLastManagerAdminAsync` and return `WouldRemoveLastManagerAdmin` if it would leave zero active Manager/Admin employees; otherwise set `Role = newRole`, record a `RoleChanged` audit event (recorded even when `newRole` equals the current role), and save — depends on T002, T003, T025
- [ ] T027 [US4] Add `POST /api/employees/{id}/change-role` to `EmployeesController.cs` with a `ChangeRoleRequest(string? Role)` body, parsing via `Enum.TryParse<EmployeeRole>` (mirroring `CreateEmployeeRequest.Role`'s existing parsing), mapping outcomes per contracts/employee-management-api.md — depends on T026
- [ ] T028 [US4] Extend `EmployeeSessionCookieEvents.ValidatePrincipal` (`backend/src/LootSingles.Infrastructure/Auth/EmployeeSessionCookieEvents.cs`) to also reject the principal when the cookie's `ClaimTypes.Role` claim doesn't match the employee's current `Role`, alongside the existing `!IsActive` check (research.md §2) — depends on T025
- [ ] T029 [P] [US4] Add `changeEmployeeRole(id, role)` to `adminApi.ts`, calling `POST /api/employees/{id}/change-role` and handling the new `409`/`400` typed errors
- [ ] T030 [US4] Add a role-change control per roster row in `AdminPage.tsx`, refreshing the roster on success and showing the blocked-change error inline — depends on T029

**Checkpoint**: All 4 stories through role change are independently functional.

---

## Phase 7: User Story 5 - Manager Resets a PIN or Unlocks an Account (Priority: P3)

**Goal**: A Manager/Admin can reset an employee's PIN or unlock a locked account from the admin
screen. No backend work is needed — both endpoints already exist and are fully tested (feature
002); this story is the first-ever UI for either.

**Independent Test**: As a Manager/Admin, reset an employee's PIN and confirm the old PIN stops
working while the new one works. Unlock a locked account and confirm the employee can log in again
with their existing PIN.

### Tests for User Story 5 ⚠️ Write first, confirm failing before implementing

- [ ] T031 [US5] Add failing frontend component tests to `AdminPage.test.tsx`: a "Reset PIN" action per row prompts for a new PIN and calls `resetPin`, showing success/error feedback; an "Unlock" action is shown only for locked employees and calls `unlockEmployee`, showing success/error feedback

### Implementation for User Story 5

- [ ] T032 [US5] Add `resetPin(id, newPin)`/`unlockEmployee(id)` functions to `adminApi.ts`, calling the existing `POST /api/employees/{id}/reset-pin`/`/unlock` endpoints and handling their existing `400`/`404` responses
- [ ] T033 [US5] Add "Reset PIN" (all rows) and "Unlock" (locked rows only) actions to `AdminPage.tsx`, calling T032's functions and showing success/error feedback — depends on T032

**Checkpoint**: All 5 user stories are independently functional. Feature is complete per spec.md.

---

## Phase 8: Polish & Cross-Cutting Validation

- [ ] T034 [P] Add `frontend/e2e/admin.spec.ts` covering: create a new employee → see it in the roster → remove it → restore it; and a role change that invalidates a second, already-logged-in session for the promoted/demoted employee — using two authenticated browser contexts, per quickstart.md. The test creates its own throwaway employee(s) via the UI rather than needing new `E2EHost` seed data.
- [ ] T035 Run the full manual validation in `quickstart.md` (all 9 scenarios, including the concurrent last-Manager/Admin scenario)
- [ ] T036 Run the full automated validation suite from `quickstart.md` (`dotnet build`, both `dotnet test` projects, `dotnet csharpier check`, `npm run lint/test/build/format:check/test:e2e`) and confirm everything passes with zero regressions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story.
- **User Stories (Phase 3–7)**: All depend on Foundational completion. US1 has no backend work and
  no dependency on US2–US5 beyond Foundational. US2 depends only on US1's `AdminPage.tsx` existing
  as a place to add the create form. US3 introduces the guard's first real caller and is naturally
  sequenced after US1/US2 (same file, `AdminPage.tsx`, growing incrementally) though its backend
  work is independent. US4 reuses the guard US3 already exercised. US5 has no backend work and no
  dependency on US3/US4 beyond sharing `AdminPage.tsx`.
- **Polish (Phase 8)**: Depends on all five user stories being complete.

### Within Each User Story

- Tests are written and confirmed failing before implementation (constitution Principle IV).
- Repository/service methods before controller endpoints before frontend API functions before
  frontend UI controls.

### Parallel Opportunities

- T002 and T003 (Foundational) touch different files and can run in parallel.
- Within each user story, the `[P]`-marked test tasks (different files) can run in parallel; the
  `[P]`-marked frontend-API and backend-controller tasks can run in parallel with each other once
  their respective service-layer dependency is done.
- US1's `AdminPage.tsx`/`RequireManagerAdmin`/`adminApi.ts` creation and US1's dashboard-link
  addition (T009) touch different files and can run in parallel.

---

## Parallel Example: User Story 3

```bash
# Tests (different files):
Task: "Unit tests for DeactivateAsync's last-manager guard in backend/tests/LootSingles.UnitTests/Auth/EmployeeManagementServiceTests.cs"
Task: "Integration tests for POST /api/employees/{id}/deactivate's 409 case in backend/tests/LootSingles.IntegrationTests/Auth/EmployeesControllerTests.cs"
Task: "Frontend component tests for Remove/Restore actions in frontend/tests/admin/AdminPage.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (US1 — view the roster, Manager/Admin-only).
3. **STOP and VALIDATE**: confirm the screen is reachable only by a Manager/Admin and shows the
   full roster accurately.
4. This alone proves the first piece of previously-unreachable feature 002 capability is now
   visible, and establishes the route guard every later story's actions sit behind.

### Incremental Delivery

1. Setup + Foundational → outcome type and guard primitive ready.
2. US1 (P1) → the screen exists and is properly gated → demoable MVP.
3. US2 (P1) → onboarding new staff is possible from the screen.
4. US3 (P1) → removing/restoring staff is possible, and the last-Manager/Admin guard exists and is
   proven under real concurrency.
5. US4 (P2) → role changes are possible, reusing the same guard, with immediate session effect.
6. US5 (P3) → PIN reset/unlock round out the screen with feature 002's remaining capabilities.
7. Polish → E2E coverage and full quickstart validation.

## Notes

- No task in this list changes the shape of `Employee`, adds a new role, or introduces hard
  deletion — see spec.md's Assumptions for why each was deliberately out of scope.
- The last-Manager/Admin guard (T003) is written once in Foundational and reused, unmodified, by
  both US3 (deactivation) and US4 (role change) — see research.md §1 for why `sp_getapplock` was
  chosen over a second, inconsistent concurrency-control technique.
