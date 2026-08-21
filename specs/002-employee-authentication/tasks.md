---

description: "Task list template for feature implementation"
---

# Tasks: Employee Authentication

**Input**: Design documents from `/specs/002-employee-authentication/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included and REQUIRED — constitution Principle IV (Test-Driven Development, NON-NEGOTIABLE) mandates Red → Green → Refactor for all new or modified application behavior in this project; every test task below MUST be written and confirmed failing before its corresponding implementation task.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- File paths are exact and match `plan.md`'s Project Structure

## Path Conventions

Web application per plan.md: `backend/src/`, `backend/tests/`, `frontend/src/`, `frontend/tests/`, `frontend/e2e/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the one new backend package this feature needs, and scaffold the frontend project (first use in this repo).

- [X] T001 Add a `Microsoft.Extensions.Identity.Core` package reference (the minimal package containing `PasswordHasher<T>` without pulling in EF Core-backed Identity stores, research.md §1) to `backend/src/LootSingles.Infrastructure/LootSingles.Infrastructure.csproj` — superseded during Phase 2 by the plan.md 2026-08-21 .NET 10 correction: this package is now redundant (pruned) once the `Microsoft.AspNetCore.App` FrameworkReference is present, since `PasswordHasher<T>` ships in the shared framework on .NET 10; the explicit PackageReference was removed
- [X] T002 [P] Scaffold the `frontend/` React + TypeScript + Vite project: `frontend/package.json`, `frontend/tsconfig.json`, `frontend/vite.config.ts`, `frontend/index.html`, `frontend/src/main.tsx`, `frontend/src/App.tsx`
- [X] T003 [P] Configure frontend unit testing (Vitest + React Testing Library) in `frontend/package.json` and `frontend/vitest.config.ts`
- [X] T004 [P] Configure Playwright for the frontend in `frontend/playwright.config.ts` and `frontend/e2e/` directory

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `Employee`/`EmployeeAuditEvent` domain model, persistence, PIN hashing, and cookie-authentication pipeline that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 [P] Create `EmployeeRole` enum (`Picker`, `ManagerAdmin`) in `backend/src/LootSingles.Domain/Employees/EmployeeRole.cs` (research.md §6)
- [X] T006 [P] Create `EmployeeAuditActionType` enum (`Login`, `Logout`, `AccountCreated`, `Deactivated`, `Reactivated`, `PinReset`, `Unlocked`) in `backend/src/LootSingles.Domain/Employees/EmployeeAuditActionType.cs`
- [X] T007 [P] Create the `Employee` domain entity in `backend/src/LootSingles.Domain/Employees/Employee.cs` (`Id`, `Username`, `NormalizedUsername`, `DisplayName`, `PinHash`, `Role`, `IsActive`, `FailedAttemptCount`, `IsLocked`, `CreatedAt` — per data-model.md)
- [X] T008 [P] Create the `EmployeeAuditEvent` domain entity in `backend/src/LootSingles.Domain/Employees/EmployeeAuditEvent.cs` (`Id`, `ActorEmployeeId`, `TargetEmployeeId`, `ActionType`, `OccurredAt`)
- [X] T009 [P] Create the `IPinHasher` interface (`Hash`, `Verify`) in `backend/src/LootSingles.Application/Auth/IPinHasher.cs`
- [X] T010 [P] Create the `IEmployeeRepository` interface (`GetByNormalizedUsernameAsync`, `GetByIdAsync`, `AddAsync`, `ListAsync`, `AddAuditEventAsync`, `GetAuditEventsAsync`) in `backend/src/LootSingles.Application/Auth/IEmployeeRepository.cs`
- [X] T011 [P] Unit test: `Pbkdf2PinHasher` hashes a PIN, verifies the correct PIN, and rejects an incorrect PIN, in `backend/tests/LootSingles.UnitTests/Auth/Pbkdf2PinHasherTests.cs` — must fail (type doesn't exist yet)
- [X] T012 Implement `Pbkdf2PinHasher` (`IPinHasher` via `PasswordHasher<Employee>`, research.md §1) in `backend/src/LootSingles.Infrastructure/Auth/Pbkdf2PinHasher.cs` (depends on: T007, T009, T011 failing)
- [X] T013 [P] Create `EmployeeConfiguration` (EF Core Fluent API, unique index on `NormalizedUsername`, research.md §7) in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/EmployeeConfiguration.cs`
- [X] T014 [P] Create `EmployeeAuditEventConfiguration` in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/EmployeeAuditEventConfiguration.cs`
- [X] T015 Add `DbSet<Employee>` and `DbSet<EmployeeAuditEvent>` to `backend/src/LootSingles.Infrastructure/Persistence/LootSinglesDbContext.cs` (depends on: T007, T008, T013, T014)
- [X] T016 Generate and apply the EF Core migration for the `Employee`/`EmployeeAuditEvent` tables (depends on: T015)
- [X] T017 Implement `EmployeeRepository` (`IEmployeeRepository` over `LootSinglesDbContext`) in `backend/src/LootSingles.Infrastructure/Persistence/EmployeeRepository.cs` (depends on: T010, T016)
- [X] T018 [P] Integration test: the unique index on `NormalizedUsername` rejects a case-insensitive duplicate username (FR-014) in `backend/tests/LootSingles.IntegrationTests/Auth/EmployeeConfigurationTests.cs` (depends on: T016)
- [X] T019 Wire ASP.NET Core Cookie Authentication (HttpOnly, Secure, `SameSite=Strict`) and `AddAuthorization()` in `backend/src/LootSingles.Api/Program.cs` (depends on: T007; research.md §2)
- [X] T020 Create `EmployeeSessionCookieEvents` (`ValidatePrincipal` override, rejects the principal when the live `Employee.IsActive` is `false`, research.md §3) in `backend/src/LootSingles.Infrastructure/Auth/EmployeeSessionCookieEvents.cs` and wire it into T019's cookie options via `EventsType` (depends on: T017, T019)
- [X] T021 [P] Integration test: a cookie for an employee whose `IsActive` is set to `false` directly in the database is rejected on the very next request (FR-012 mechanism) in `backend/tests/LootSingles.IntegrationTests/Auth/SessionInvalidationTests.cs` (depends on: T020)

**Checkpoint**: Foundation ready — `Employee` persists, PINs hash/verify, cookies authenticate and self-invalidate on deactivation. User story implementation can now begin.

---

## Phase 3: User Story 1 - Employee Logs In With Username and PIN (Priority: P1) 🎯 MVP

**Goal**: An employee can authenticate with their own username + PIN and get a session that identifies them; wrong credentials and deactivated accounts are rejected identically (FR-001, FR-004, FR-005, FR-018).

**Independent Test**: Supply correct credentials for an active employee → authenticated with an identifying session. Supply a wrong PIN, a nonexistent username, or a deactivated account's credentials → identical generic rejection, no session.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T022 [P] [US1] Unit test `AuthenticationService.LoginAsync`: correct credentials → `Success`; wrong PIN → `InvalidCredentials`; nonexistent username → `InvalidCredentials` (identical to wrong PIN); deactivated account → `InvalidCredentials` regardless of PIN correctness, in `backend/tests/LootSingles.UnitTests/Auth/AuthenticationServiceTests.cs`
- [X] T023 [P] [US1] Integration test: `POST /api/auth/login` returns `200` + sets the auth cookie on success, identical `401` body for wrong-PIN/nonexistent-username/deactivated-account; `GET /api/auth/me` returns the authenticated employee's identity, in `backend/tests/LootSingles.IntegrationTests/Auth/AuthControllerTests.cs`
- [X] T024 [P] [US1] Frontend unit test: `LoginPage` renders username + PIN fields and shows the generic error message on a failed login, in `frontend/tests/auth/LoginPage.test.tsx`

### Implementation for User Story 1

- [X] T025 [US1] Create `AuthenticationResult` (`Success`, `InvalidCredentials`) in `backend/src/LootSingles.Application/Auth/AuthenticationResult.cs`
- [X] T026 [US1] Implement `AuthenticationService.LoginAsync` (normalize + look up username, verify PIN via `IPinHasher`, reject inactive accounts identically to wrong-PIN, record an `EmployeeAuditEvent(Login)` on success) in `backend/src/LootSingles.Application/Auth/AuthenticationService.cs` (depends on: T009, T010, T025; T022 must fail first)
- [X] T027 [US1] Implement `AuthController`: `POST /api/auth/login` (issues the cookie with employee-id + role claims on success) and `GET /api/auth/me`, in `backend/src/LootSingles.Api/Controllers/AuthController.cs` (depends on: T026; T023 must fail first)
- [X] T028 [P] [US1] Implement `authApi.ts` (`login`, `me`) in `frontend/src/features/auth/authApi.ts`
- [X] T029 [US1] Implement `LoginPage.tsx` (username + PIN form, calls `authApi.login`, shows the generic error) in `frontend/src/features/auth/LoginPage.tsx` (depends on: T028; T024 must fail first)
- [X] T030 [US1] Implement `AuthContext.tsx` (session state, whoami-on-load via `authApi.me`) in `frontend/src/features/auth/AuthContext.tsx` (depends on: T028)
- [X] T031 [US1] Wire `LoginPage`/`AuthContext` into `frontend/src/App.tsx` and `frontend/src/main.tsx` (depends on: T029, T030)
- [X] T032 [US1] Playwright e2e test: successful login flow in `frontend/e2e/login.spec.ts` (depends on: T027, T031)

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Failed Login Attempts Are Protected Against Repeated Guessing (Priority: P1)

**Goal**: Repeated wrong-PIN attempts lock the account after a threshold; a locked account rejects even the correct PIN with a distinct message, and stays locked until a Manager/Admin acts (FR-006, FR-007, FR-021).

**Independent Test**: Submit repeated wrong PINs past the threshold → account locked, further attempts (including the correct PIN) rejected with the distinct locked message, indefinitely.

### Tests for User Story 2

- [X] T033 [P] [US2] Unit test `AuthenticationService.LoginAsync`: increments `FailedAttemptCount` on wrong PIN, locks the account at the configured threshold, returns `AccountLocked` for a locked account regardless of PIN correctness, and resets `FailedAttemptCount` on a successful login, in `backend/tests/LootSingles.UnitTests/Auth/AuthenticationServiceTests.cs`
- [X] T034 [P] [US2] Integration test: `POST /api/auth/login` returns `423` with the distinct locked-account message once the threshold is reached, remains `423` on a subsequent correct-PIN attempt, and a login succeeding below the threshold resets the count for future attempts, in `backend/tests/LootSingles.IntegrationTests/Auth/AuthControllerTests.cs`

### Implementation for User Story 2

- [X] T035 [US2] Add the lockout-threshold configuration (default 5, research.md §5) as an options class bound from `backend/src/LootSingles.Api/appsettings.json`
- [X] T036 [US2] Extend `AuthenticationResult` with `AccountLocked` in `backend/src/LootSingles.Application/Auth/AuthenticationResult.cs` (depends on: T025)
- [X] T037 [US2] Extend `AuthenticationService.LoginAsync` with lockout enforcement (check `IsLocked` first; increment/reset `FailedAttemptCount`; set `IsLocked` at threshold) in `backend/src/LootSingles.Application/Auth/AuthenticationService.cs` (depends on: T026, T035, T036; T033 must fail first)
- [X] T038 [US2] Update `AuthController`'s login endpoint to return `423` with the locked-account message for `AccountLocked` in `backend/src/LootSingles.Api/Controllers/AuthController.cs` (depends on: T027, T037; T034 must fail first)
- [X] T039 [US2] Update `LoginPage.tsx` to render the distinct locked-account message separately from the generic invalid-credentials message in `frontend/src/features/auth/LoginPage.tsx` (depends on: T029, T038)

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Manager Maintains the Employee Roster (Priority: P2)

**Goal**: A Manager/Admin can create, deactivate, reactivate, PIN-reset, and unlock employee accounts; a Picker cannot (FR-009, FR-013–FR-017, FR-022).

**Independent Test**: As Manager/Admin, create an employee (can then log in per US1); deactivate them (can no longer log in); as Picker, attempt any of these actions (rejected).

### Tests for User Story 3

- [ ] T040 [P] [US3] Unit test `EmployeeManagementService`: create rejects a case-insensitive duplicate username; deactivate/reactivate toggle `IsActive`; reactivating a previously locked account clears the lockout and resets `FailedAttemptCount`; reset-pin replaces the hash without touching lock state; unlock clears the lock without touching the PIN, in `backend/tests/LootSingles.UnitTests/Auth/EmployeeManagementServiceTests.cs`
- [ ] T041 [P] [US3] Integration tests for all `/api/employees...` endpoints (create 201/409/400, deactivate/reactivate incl. reactivate-clears-lockout, reset-pin, unlock, list, audit-events listing, and `403` for every endpoint when called by a Picker-role session), in `backend/tests/LootSingles.IntegrationTests/Auth/EmployeesControllerTests.cs`

### Implementation for User Story 3

- [ ] T042 [US3] Implement `EmployeeManagementService` (`CreateAsync`, `DeactivateAsync`, `ReactivateAsync` with lockout-clear, `ResetPinAsync`, `UnlockAsync`), recording the matching `EmployeeAuditEvent` for each, in `backend/src/LootSingles.Application/Auth/EmployeeManagementService.cs` (depends on: T009, T010, T012, T017; T040 must fail first)
- [ ] T043 [US3] Implement `EmployeesController` (`POST /`, `GET /`, `POST /{id}/deactivate`, `POST /{id}/reactivate`, `POST /{id}/reset-pin`, `POST /{id}/unlock`, `GET /{id}/audit-events`), each requiring `[Authorize(Roles = "ManagerAdmin")]`, in `backend/src/LootSingles.Api/Controllers/EmployeesController.cs` (depends on: T042; T041 must fail first)

**Checkpoint**: User Stories 1, 2, and 3 all work independently.

---

## Phase 6: User Story 4 - Authenticated Session Persists Across Normal Interruptions (Priority: P2)

**Goal**: A logged-in employee stays authenticated across reloads without re-entering credentials, until idle timeout or explicit logout (FR-010, FR-011, FR-020).

**Independent Test**: Authenticate, reload, remain authenticated. Go idle past the session lifetime, next interaction requires re-authentication. Explicitly log out, next access requires re-authentication.

### Tests for User Story 4

- [ ] T044 [P] [US4] Integration tests: the same cookie authenticates across two separate requests with no re-authentication (reload simulation); a request made after the 30-minute inactivity window (advanced/fake clock) returns `401`; `POST /api/auth/logout` ends the session immediately (a following `GET /api/auth/me` returns `401`), in `backend/tests/LootSingles.IntegrationTests/Auth/AuthControllerTests.cs`
- [ ] T045 [P] [US4] Frontend unit test: `AuthContext` restores the session on load and clears it after logout, in `frontend/tests/auth/AuthContext.test.tsx`

### Implementation for User Story 4

- [ ] T046 [US4] Configure the cookie's sliding expiration (`ExpireTimeSpan = 30 minutes`, `SlidingExpiration = true`, FR-010) in `backend/src/LootSingles.Api/Program.cs` (depends on: T019; T044 must fail first)
- [ ] T047 [US4] Implement `AuthController`'s `POST /api/auth/logout` (`SignOutAsync`) in `backend/src/LootSingles.Api/Controllers/AuthController.cs` (depends on: T027; T044 must fail first)
- [ ] T048 [US4] Add a logout action and affordance to `AuthContext.tsx` / the app shell in `frontend/src/features/auth/AuthContext.tsx` (depends on: T030, T047; T045 must fail first)
- [ ] T049 [US4] Playwright e2e test: session survives a reload, explicit logout requires re-login, in `frontend/e2e/login.spec.ts` (depends on: T046, T048)

**Checkpoint**: All four user stories work independently and together.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final verification against the spec's own validation guide and Definition of Done.

- [ ] T050 [P] Walk through quickstart.md scenarios 1–14 end-to-end against a running instance and record the outcome of each
- [ ] T051 [P] Audit backend logging configuration and code paths touching `Employee.PinHash`/raw PIN values to confirm no plaintext PIN is ever logged (constitution VII, FR-003)
- [ ] T052 Run the full backend (`dotnet test`) and frontend (`npm test`, Playwright e2e) suites and confirm everything is green

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T001 for `Pbkdf2PinHasher`'s package). BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational only.
- **User Story 2 (Phase 4)**: Depends on Foundational **and** extends User Story 1's `AuthenticationService`/`AuthController`/`LoginPage` (T026, T027, T029) — not independent of US1's code, but independently *testable* once built (its own lockout scenarios).
- **User Story 3 (Phase 5)**: Depends on Foundational only (uses `IEmployeeRepository`/`IPinHasher` directly, not US1/US2's `AuthenticationService`) — fully independent of US1/US2 and could be built in parallel with them.
- **User Story 4 (Phase 6)**: Depends on Foundational and extends US1's `AuthController`/`AuthContext` (T027, T030) — same relationship as US2.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- Tests MUST be written and confirmed failing before their corresponding implementation task (constitution Principle IV).
- Backend service logic before backend controller endpoints.
- Backend endpoints before the frontend code that calls them.

### Parallel Opportunities

- Setup: T002, T003, T004 in parallel (T001 is backend-only, independent of these too).
- Foundational: T005–T010 in parallel; T013/T014 in parallel; T018 and T019 can proceed in parallel once their own dependencies are met.
- Once Foundational is complete: **User Story 3 can be built fully in parallel with User Stories 1/2/4**, since it doesn't touch `AuthenticationService`/`AuthController`/`LoginPage`/`AuthContext` at all. User Stories 2 and 4 both extend US1's files, so within a single working session they're naturally sequential after US1, though a second developer could still write US2/US4's test files in parallel with US1 implementation.
- Test tasks within a phase that target different files are marked `[P]`.

---

## Parallel Example: Foundational Phase

```bash
# Domain types together:
Task: "Create EmployeeRole enum in backend/src/LootSingles.Domain/Employees/EmployeeRole.cs"
Task: "Create EmployeeAuditActionType enum in backend/src/LootSingles.Domain/Employees/EmployeeAuditActionType.cs"
Task: "Create Employee entity in backend/src/LootSingles.Domain/Employees/Employee.cs"
Task: "Create EmployeeAuditEvent entity in backend/src/LootSingles.Domain/Employees/EmployeeAuditEvent.cs"

# Application seams together:
Task: "Create IPinHasher interface in backend/src/LootSingles.Application/Auth/IPinHasher.cs"
Task: "Create IEmployeeRepository interface in backend/src/LootSingles.Application/Auth/IEmployeeRepository.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run quickstart.md scenarios 1–3 independently
5. Demo: an employee can log in; wrong/nonexistent/deactivated credentials are rejected identically

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. User Story 1 → validate (quickstart 1–3) → MVP
3. User Story 2 → validate (quickstart 4–5) — completes the safety property the constitution requires before US1's login is "safe to ship" per the spec's own priority rationale
4. User Story 3 → validate (quickstart 6–10) — independent of US2/US4, could be delivered in either order
5. User Story 4 → validate (quickstart 11–13) → all stories complete
6. Polish → full quickstart + Definition of Done pass

### Notes

- `[P]` tasks = different files, no dependencies within that phase.
- `[Story]` label maps each task to its user story for traceability.
- Constitution Principle IV requires every test task to fail for the expected reason before its implementation task begins.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
