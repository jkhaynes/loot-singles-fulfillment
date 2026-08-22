---

description: "Task list template for feature implementation"
---

# Tasks: Login Page and Dashboard UI Design

**Input**: Design documents from `/specs/003-login-dashboard-ui/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included and REQUIRED — constitution Principle IV (Test-Driven Development, NON-NEGOTIABLE) mandates Red → Green → Refactor for all new or modified application behavior in this project; every test task below MUST be written and confirmed failing before its corresponding implementation task.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- File paths are exact and match `plan.md`'s Project Structure

## Path Conventions

Web application per plan.md: `backend/src/`, `backend/tests/`, `frontend/src/`, `frontend/tests/`, `frontend/e2e/`.

---

## Phase 1: Setup (Shared Design Infrastructure)

**Purpose**: The design-token system and brand-mark asset both User Story 1 and User Story 2 build on. No other blocking foundational work exists for this feature (research.md) — Setup doubles as the foundation here.

- [X] T001 [P] Produce the production brand-mark asset (`frontend/src/assets/lcs-logo.png`), a light-stroke, transparent-background variant of `docs/decisions/assets/lcs-logo-source.png` suitable for dark surfaces (research.md §3) — implemented as a colorized/transparency-mapped PNG via PowerShell's `System.Drawing`, not a hand-vectorized SVG, since no vectorization tool (ImageMagick, Python/Pillow) is available in this environment; true SVG vectorization remains a candidate follow-up if crisper arbitrary-size scaling is later needed
- [X] T002 Wire the brand mark as the app favicon: copied `docs/decisions/assets/lcs-logo-source.png` (the original dark-line-art-on-light version, not the light-stroke variant, since favicons must stay legible against arbitrary — often light — browser-chrome backgrounds) to `frontend/public/favicon.png`, updated `frontend/index.html`'s `<link rel="icon">`, and removed the superseded `frontend/public/favicon.svg` (depends on: T001)
- [X] T003 [P] Create `frontend/src/styles/tokens.css` implementing the color/radius/spacing/typography CSS custom properties from `docs/decisions/visual-design-language.md`'s Design Tokens section
- [X] T004 Update `frontend/src/index.css` to import `styles/tokens.css`, remove the scaffold's light-default/`prefers-color-scheme: dark` toggle in favor of one dark-mode-only palette (research.md §5), and rebase base element styles (`body`, `h1`, `h2`, `p`, `code`) onto the new tokens (depends on: T003)
- [X] T005 Remove the unused Vite-scaffold styling from `frontend/src/App.css` and delete the now-orphaned scaffold image assets (`frontend/src/assets/hero.png`, `react.svg`, `vite.svg`) — on inspection, *all* of `App.css`'s rules (`.counter`, `.hero`, `#next-steps`, `#docs`, `#spacer`, `.ticks`, not just the `.hero`/`.counter` subset originally called out) were unreferenced by any component, so the file was emptied entirely rather than partially trimmed. No dependency on T002 after all — `vite.svg` was never the favicon (`favicon.svg`/now `favicon.png` was, and is unrelated to the scaffold assets this task removes)

**Checkpoint**: Design tokens and brand asset exist — both user stories can now proceed independently, in parallel if desired.

---

## Phase 2: User Story 1 - Employee Sees a Polished, On-Brand Login Screen (Priority: P1) 🎯 MVP

**Goal**: Give the already-functional login screen real visual design (dark-mode-first, token-driven, brand mark, distinct error-state treatment) without changing its authentication behavior (FR-001, FR-002).

**Independent Test**: Load the login screen on phone-sized and desktop-sized viewports; confirm a clear, on-brand layout, distinctly-styled generic-vs-locked error states, and unchanged login behavior (existing tests still pass).

### Tests for User Story 1

> **NOTE: Write this test FIRST, ensure it FAILS before implementation**

- [X] T006 [P] [US1] Frontend test: `LoginPage`'s rendered error element carries a distinguishing attribute (e.g., `data-variant="locked"` vs `data-variant="generic"`) for the locked-account message vs. the generic invalid-credentials message (FR-002), in `frontend/tests/auth/LoginPage.test.tsx` — must fail (attribute doesn't exist yet)

### Implementation for User Story 1

- [X] T007 [US1] Redesign `frontend/src/features/auth/LoginPage.tsx`'s markup — token-driven layout, brand mark placement, and the distinguishing error-state attribute from T006 — without changing its props, field `id`/`label` associations, or submitted behavior (FR-001) (depends on: T001, T004; T006 must fail first)
- [X] T008 [US1] Create `frontend/src/features/auth/LoginPage.css`, styled entirely from the tokens in `styles/tokens.css` (soft rounded corners, restrained shadows, layered surface, comfortably tappable controls) (depends on: T004, T007)
- [X] T009 [US1] Verify `frontend/tests/auth/LoginPage.test.tsx`'s existing behavioral assertions and `frontend/e2e/login.spec.ts`'s existing login/error assertions still pass unchanged against the redesigned `LoginPage` (regression check, FR-001) (depends on: T007, T008) — verified via Vitest (8/8 passing) and Playwright (2/2 passing)

**Checkpoint**: User Story 1 is fully functional and independently testable — the login screen is redesigned with no behavior change.

---

## Phase 3: User Story 2 - Employee Sees an At-a-Glance Order-Picking Dashboard After Login (Priority: P2)

**Goal**: A new Dashboard screen shown immediately after login, with a live Ready section (count + per-order summary) and three static "not yet available" placeholder sections (FR-003–FR-005, FR-007–FR-009).

**Independent Test**: Log in and confirm the Dashboard shows a correct Ready count/summary (or empty state), the three placeholder sections, works on mobile and desktop, and that logout returns to the login screen.

### Tests for User Story 2

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T010 [P] [US2] Unit test: the Dashboard's summary projection computes `ProductCount` (distinct order lines) and `TotalQuantity` (sum of line quantities) correctly from `Order`/`OrderLine` fixtures, in `backend/tests/LootSingles.UnitTests/Dashboard/DashboardServiceTests.cs` — must fail (types don't exist yet)
- [X] T011 [P] [US2] Integration test: `GET /api/dashboard` returns `401` when not authenticated, `200` with an empty `ready.orders` array when no Ready orders exist, and `200` with correct per-order summaries when Ready orders exist, in `backend/tests/LootSingles.IntegrationTests/Dashboard/DashboardControllerTests.cs` — must fail
- [X] T012 [P] [US2] Frontend test: `DashboardPage` renders the Ready section from live data (including the empty-state message when there are none), renders the three "not yet available" placeholder sections, and invokes logout when its logout control is activated (FR-008), in `frontend/tests/dashboard/DashboardPage.test.tsx` — must fail (component doesn't exist yet)

### Implementation for User Story 2

- [X] T013 [P] [US2] Create the `OrderSummary` read-model record (`OrderId`, `TcgplayerOrderId`, `ProductCount`, `TotalQuantity`) in `backend/src/LootSingles.Application/Dashboard/OrderSummary.cs` (data-model.md)
- [X] T014 [P] [US2] Create the `IDashboardRepository` interface (`GetReadyOrderSummariesAsync`) in `backend/src/LootSingles.Application/Dashboard/IDashboardRepository.cs` (Constitution IX/XII persistence seam)
- [X] T015 [US2] Implement `DashboardService` in `backend/src/LootSingles.Application/Dashboard/DashboardService.cs` (depends on: T013, T014; T010 must fail first)
- [X] T016 [US2] Implement `DashboardRepository` (`IDashboardRepository` over `LootSinglesDbContext`, `AsNoTracking` + server-side `Count`/`Sum` projection per data-model.md) in `backend/src/LootSingles.Infrastructure/Persistence/DashboardRepository.cs` (depends on: T014) — ordering applied after materialization, not via SQL `ORDER BY` (SQLite cannot translate `DateTimeOffset` ordering; see data-model.md)
- [X] T017 [US2] Register `IDashboardRepository` and `DashboardService` in `backend/src/LootSingles.Api/Program.cs` (depends on: T015, T016)
- [X] T018 [US2] Implement `DashboardController` (`GET /api/dashboard`, `[Authorize]` with no role restriction, contracts/dashboard-api.md) in `backend/src/LootSingles.Api/Controllers/DashboardController.cs` (depends on: T017; T011 must fail first)
- [X] T019 [P] [US2] Implement `dashboardApi.ts` (`getDashboard`) in `frontend/src/features/dashboard/dashboardApi.ts` (calls contracts/dashboard-api.md's endpoint)
- [X] T020 [US2] Implement `DashboardPage.tsx` (Ready section from live data, the three static placeholder sections, logout action) in `frontend/src/features/dashboard/DashboardPage.tsx` (depends on: T019; T012 must fail first)
- [X] T021 [US2] Create `frontend/src/features/dashboard/DashboardPage.css`, styled from `styles/tokens.css` (distinct section treatments, comfortably tappable logout control, responsive layout per FR-006) (depends on: T004, T020)
- [X] T022 [US2] Wire `DashboardPage` into `frontend/src/App.tsx`, replacing the current authenticated placeholder (depends on: T020)
- [X] T023 [P] [US2] Seed one sample Ready order (with product lines) in `backend/tests/LootSingles.E2EHost/Program.cs` for Playwright validation — also registered `IDashboardRepository`/`DashboardService` in the E2E host's DI container, required for `DashboardController` to resolve there
- [X] T024 [US2] Update `frontend/e2e/login.spec.ts`'s existing tests: replace the prior placeholder-text assertion with an assertion that the Dashboard's Ready section (showing the seeded order) appears after login, and update — not remove — the same spec's existing reload and logout assertions so they check the new Dashboard content instead of the old placeholder text (FR-008, US2 AC6) (depends on: T018, T022, T023)

**Checkpoint**: User Stories 1 and 2 both work independently and together.

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: Final verification against the spec's own validation guide and Definition of Done.

- [X] T025 [P] Walk through quickstart.md scenarios 1–8 end-to-end against a running instance and record the outcome of each in `specs/003-login-dashboard-ui/validation-results.md` — all 8 pass (see validation-results.md for which were live-browser-verified vs. covered by their dedicated automated tests)
- [X] T026 Run the full backend (`dotnet test`) and frontend (`npm test`, Playwright e2e) suites and confirm everything is green — 68 backend unit + 48 backend integration + 12 frontend unit + 4 Playwright all passing; fixed one stale Playwright assertion in `frontend/e2e/login.spec.ts` left over from T024 (expected old placeholder-screen text `'E2E Manager (ManagerAdmin)'` that the redesigned `DashboardPage` never renders; no spec requirement calls for that text, so the assertion was corrected to match the approved greeting-based design)
- [X] T027 [P] Playwright test: at a mobile-width viewport (e.g., 375px), both the login screen and the Dashboard render with no horizontal scroll, in `frontend/e2e/responsive.spec.ts` (FR-006, SC-002)

---

## Phase 5: Code and Design Review Follow-ups

**Purpose**: Resolve the findings from the `/code-design-review` pass run after Phase 4. T028/T029 are Must Fix (block `/speckit-converge`); T030–T032 are Advisory (do not block, but are being addressed now per Product Owner/Developer preference).

### Must Fix

> **NOTE: Write T028 FIRST, ensure it FAILS before implementing T029** (constitution Principle IV)

- [X] T028 [P] Test: `DashboardPage` renders a distinct load-failure message (not the same copy used for a genuine zero-Ready-orders empty state) when `getDashboard()` rejects, in `frontend/tests/dashboard/DashboardPage.test.tsx` — must fail (no error handling exists yet) — confirmed failing (unhandled rejection) before T029
- [X] T029 Catch `getDashboard()` rejection in `frontend/src/features/dashboard/DashboardPage.tsx`'s load effect, track a load-error state distinct from `data`/`isLoading`, and render the distinct failure message instead of falling through to `data?.ready.count ?? 0` (depends on: T028; T028 must fail first) — code-design-review finding: an API failure (401, 500, network error) was previously indistinguishable from a legitimate empty Ready section, violating constitution Principle V/XI and undermining FR-004/SC-003's accuracy guarantee. Stat tile shows `—` and the panel shows a distinct red `role="alert"` message instead of the empty-state copy.

### Advisory

- [X] T030 [P] Remove `backend/tests/LootSingles.UnitTests/Dashboard/DashboardServiceTests.cs`'s `GetReadyOrderSummariesAsync_ComputesProductCountAndTotalQuantityFromOrderLines` test — `DashboardService` has no computation logic of its own (pure delegation to `IDashboardRepository`), so this test only exercises `FakeDashboardRepository`'s hand-copied projection formula, never production code; the real computation is already verified by `DashboardControllerTests.GetDashboard_ReadyOrdersExist_ReturnsCorrectSummaries` (integration test against real EF Core/SQLite), so it serves no real purpose and is misleading about what safety net exists
- [X] T031 [P] Test: the Dashboard's Available Orders table applies a visually distinguishing treatment to an order row's Quantity cell when `totalQuantity > 1`, in `frontend/tests/dashboard/DashboardPage.test.tsx` — must fail (no such treatment exists yet) — confirmed failing before T032
- [X] T032 Add the quantity-greater-than-one visual treatment (e.g., bold weight) to the Quantity column in `frontend/src/features/dashboard/DashboardPage.tsx`/`DashboardPage.css` (depends on: T031; T031 must fail first) — code-design-review finding: CLAUDE.md's product-specific safety rule ("quantity greater than one... must receive strong visual emphasis wherever it is displayed") is not reflected in the Dashboard's order-total Quantity column, which is styled identically regardless of value; the current styling still satisfies spec.md's own narrower Edge Case bar (legible, not truncated, not de-emphasized), so this is a consistency improvement, not a blocking defect. Implemented as bold weight + `--color-warning` (amber, matching the locked-account error's cautionary tone) on the Quantity cell via a `data-emphasis="high"` attribute.
- [X] T033 [P] Remove `DashboardServiceTests.GetReadyOrderSummariesAsync_ExcludesNonReadyOrders` and `DashboardControllerTests.GetDashboard_NonReadyOrderExists_IsExcludedFromReadySection` (and `DashboardServiceTests`'s now-unused `NewLine` helper) — code-design-review finding (second pass): both tests' fixtures only ever construct a `Status = OrderStatus.Ready` order, since `OrderStatus` currently has only that one value (data-model.md) — a genuine non-Ready fixture cannot be constructed yet, so both tests were misleadingly-named duplicates of "one Ready order → one summary" that provided zero actual coverage of the `.Where(order => order.Status == OrderStatus.Ready)` filter. Per Product Owner/Developer decision, removed rather than renamed — to be re-added with a real negative-path fixture once a second `OrderStatus` value exists (future order-claiming/picking-workflow feature)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. Doubles as the foundation for both user stories (research.md).
- **User Story 1 (Phase 2)**: Depends on Setup only (T001, T004). Fully independent of User Story 2 — no shared files, no shared backend surface.
- **User Story 2 (Phase 3)**: Depends on Setup only (T004). Fully independent of User Story 1 — touches only new backend files and a new frontend feature folder, plus `App.tsx`'s authenticated branch (which User Story 1 never touches).
- **Polish (Phase 4)**: Depends on both user stories being complete.
- **Code and Design Review Follow-ups (Phase 5)**: Depends on Phase 4 (the `/code-design-review` pass that produced these findings ran after Phase 4 completed). T028/T029 (Must Fix) and T030–T032 (Advisory) each touch different files/tests and are independent of each other.

### Within Each User Story

- Tests MUST be written and confirmed failing before their corresponding implementation task (constitution Principle IV).
- Backend service logic before backend controller endpoints (User Story 2).
- Backend endpoints before the frontend code that calls them (User Story 2).
- Component implementation before its dedicated stylesheet, since the stylesheet targets the component's markup.

### Parallel Opportunities

- Setup: T001, T003, and T005 in parallel (T002 depends on T001; T004 depends on T003). T005 turned out to have no real dependency on T002 — `vite.svg` was never the favicon (see T005's note).
- Once Setup is complete: **User Story 1 and User Story 2 can be built fully in parallel** by two developers — they share no files beyond the read-only `styles/tokens.css` from Setup.
- Within User Story 2: T010, T011, T012 (all test files) in parallel; T013, T014 (different new files) in parallel; T019 and T023 (different files, no shared dependency) in parallel.
- Test tasks within a phase that target different files are marked `[P]`. T027 (Polish) is `[P]` relative to T025/T026 (different files, no shared dependency).

---

## Parallel Example: User Story 2 Tests

```bash
# Launch all three test-writing tasks for User Story 2 together (different files):
Task: "Unit test: Dashboard summary projection in backend/tests/LootSingles.UnitTests/Dashboard/DashboardServiceTests.cs"
Task: "Integration test: GET /api/dashboard in backend/tests/LootSingles.IntegrationTests/Dashboard/DashboardControllerTests.cs"
Task: "Frontend test: DashboardPage rendering in frontend/tests/dashboard/DashboardPage.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: User Story 1
3. **STOP and VALIDATE**: run quickstart.md scenarios 1–3 independently
4. Demo: a redesigned, on-brand login screen with distinctly-styled error states and unchanged behavior

### Incremental Delivery

1. Setup → foundation ready
2. User Story 1 → validate (quickstart 1–3) → MVP
3. User Story 2 → validate (quickstart 4–8) → both stories complete
4. Polish → full quickstart + Definition of Done pass

### Notes

- `[P]` tasks = different files, no dependencies within that phase.
- `[Story]` label maps each task to its user story for traceability.
- Constitution Principle IV requires every test task to fail for the expected reason before its implementation task begins.
- Commit after each task or logical group; stop at either checkpoint to validate a story independently.
