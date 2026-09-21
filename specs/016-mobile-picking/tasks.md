# Tasks: Mobile Picking Experience

**Input**: Design documents from `/specs/016-mobile-picking/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: **MANDATORY, not optional.** Constitution Principle IV makes Test-Driven Development
non-negotiable for all new or modified application behaviour. Every test task below precedes
the implementation task it covers, and each must be written and seen to **fail** first.

**Delivery**: one pull request for the whole feature (Product Owner decision, 2026-09-20). The
three stories remain independently testable, and the checkpoints below are the points where the
branch is known-good.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel — different files, no dependency on an incomplete task
- **[Story]**: the user story the task serves

## Path Conventions

Web application, per [plan.md](plan.md): `backend/src/`, `backend/tests/`, `frontend/src/`,
`frontend/tests/`, `frontend/e2e/`.

---

## Phase 1: Setup (Shared Test Fixtures)

**Purpose**: the fixtures every story's tests depend on. An order spanning one set exercises
almost none of this feature, so building the right fixture first is real work, not ceremony.

- [X] T001 [P] Add an `OrderDetail` / `OrderLineDetail` test builder in `frontend/tests/support/orderBuilders.ts`, supporting multiple games, multiple sets per game, quantity greater than one, missing/blank set values, and each `PickOutcome` state
- [X] T002 Extend the E2E seed data in `backend/tests/LootSingles.E2EHost/Program.cs` with an order spanning at least two games and two sets per game, including one line with quantity greater than one
- [X] T003 [P] Add a `matchMedia` test helper in `frontend/tests/support/matchMedia.ts` so component tests can drive phone-sized and desktop-sized viewports

**Checkpoint**: fixtures exist; story work can begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**None.** This feature adds no shared infrastructure — no schema, no migration, no new
framework, no auth change. The order detail payload and the claim endpoint it needs already
exist (see [research.md](research.md) R7).

Listing nothing here is deliberate rather than an omission: inventing foundational work for a
feature that needs none would violate Principle III.

---

## Phase 3: User Story 1 — Walk to each storage box once (Priority: P1) 🎯 MVP

**Goal**: an order's products are grouped into the boxes a picker walks, ordered by game then
set, so each storage box is visited once.

**Independent Test**: open an order spanning two games and several sets; confirm grouping and
ordering, and that no line has gone missing.

### Tests for User Story 1 ⚠️ WRITE FIRST, MUST FAIL

- [X] T004 [P] [US1] Unit tests for `groupOrderLines` totality in `frontend/tests/orders/orderGrouping.test.ts` — every input line appears in exactly one output group, for empty, single-line, and multi-group orders. This is the invariant that a dropped line would break
- [X] T005 [P] [US1] Unit tests for game and set ordering in `frontend/tests/orders/orderGrouping.test.ts` — games alphabetical, sets alphabetical within a game, lines keeping their relative order within a set
- [X] T006 [P] [US1] Unit tests proving sets with the same name in different games do **not** merge, in `frontend/tests/orders/orderGrouping.test.ts`
- [X] T007 [P] [US1] Unit tests for missing, blank and whitespace-only set values in `frontend/tests/orders/orderGrouping.test.ts` — the line stays present, in a group marked not-recorded, sorted last within its game
- [X] T008 [P] [US1] Unit tests for `computeProgress` in `frontend/tests/orders/orderGrouping.test.ts` — products resolved out of total, physical cards summing `Quantity` so a line of 3 contributes 3, and a `HasIssue` line counting as accounted for
- [X] T009 [P] [US1] Component test in `frontend/tests/orders/OrderDetailPage.test.tsx` asserting the list view renders set headers with per-set product and card counts

### Implementation for User Story 1

- [X] T010 [US1] Implement `groupOrderLines` and the `SetGroup` shape in `frontend/src/features/orders/orderGrouping.ts` per [contracts/order-grouping.md](contracts/order-grouping.md)
- [X] T011 [US1] Implement set ordering as its own named comparator function in `frontend/src/features/orders/orderGrouping.ts`, kept separable so release-date ordering (PRD §13.1) is a one-place change later — a named function, **not** an injected strategy interface
- [X] T012 [US1] Implement `computeProgress` in `frontend/src/features/orders/orderGrouping.ts`
- [X] T013 [US1] Render the grouped order with set headers and counts in `frontend/src/features/orders/OrderDetailPage.tsx`
- [X] T014 [P] [US1] Style set headers and group separation in `frontend/src/features/orders/OrderDetailPage.css`
- [X] T015 [US1] E2E coverage in `frontend/e2e/order-detail.spec.ts` — open the multi-game seed order, assert game and set grouping and ordering, and assert the rendered line count equals the order's line count

**Checkpoint**: US1 is independently shippable. The existing list view is already more useful.

---

## Phase 4: User Story 2 — Pick one card at a time on a phone (Priority: P2)

**Goal**: a focused view that shows one product at a time, never records on navigation, names
box boundaries, and refuses to present an unfinished box as finished.

**Independent Test**: on a phone viewport, work an order through the focused view — advance,
go back, record, finish a set, and try to leave a set with work outstanding.

### Tests for User Story 2 ⚠️ WRITE FIRST, MUST FAIL

- [X] T016 [P] [US2] Unit tests for the set-transition and guard logic in `frontend/tests/orders/orderGrouping.test.ts` — complete set yields a transition naming the next set with its counts; incomplete set yields the guard carrying its unresolved lines; the last set yields neither
- [X] T017 [P] [US2] Unit test proving a set stops reporting as incomplete once its last outstanding line is resolved, in `frontend/tests/orders/orderGrouping.test.ts`
- [X] T018 [P] [US2] Component tests for `useViewPreference` in `frontend/tests/orders/useViewPreference.test.ts` — focused is default at phone width, list at desktop width, a stored choice overrides both
- [X] T019 [P] [US2] Component test in `frontend/tests/orders/useViewPreference.test.ts` proving that when `localStorage` throws or is unavailable, the view still renders using the size-based default
- [X] T020 [US2] Component test in `frontend/tests/orders/FocusedPickView.test.tsx` asserting **positively** that navigating forward and back issues no pick or report-issue request and leaves every line's outcome unchanged (FR-011). Assert on calls not made and state unchanged — never merely that no error appeared
- [X] T021 [P] [US2] Component test in `frontend/tests/orders/FocusedPickView.test.tsx` asserting every product is reachable using on-screen controls alone, without a swipe gesture
- [X] T022 [P] [US2] Component test in `frontend/tests/orders/FocusedPickView.test.tsx` asserting an explicit confirm records a pick for that product only
- [X] T023 [P] [US2] Component tests in `frontend/tests/orders/SetTransition.test.tsx` for both the finished-box panel and the unfinished-box guard, including that the guard offers exactly three choices
- [X] T024 [P] [US2] Component test in `frontend/tests/orders/FocusedPickView.test.tsx` asserting quantity greater than one receives strong visual emphasis (PRD §5.3, §15) — the focused view is where a missed "PULL 3 COPIES" costs most
- [X] T025 [P] [US2] Component test in `frontend/tests/orders/FocusedPickView.test.tsx` asserting picking actions become unavailable, with an explanation, when the claim is lost mid-pick

### Implementation for User Story 2

- [X] T026 [US2] Implement `findNextUnresolved` and the set-guard result shape in `frontend/src/features/orders/orderGrouping.ts`
- [X] T027 [P] [US2] Implement `useViewPreference` in `frontend/src/features/orders/useViewPreference.ts` — `matchMedia` for the size default, guarded `localStorage` for the per-device choice, every access wrapped so failure degrades to the default
- [X] T028 [US2] Implement `FocusedPickView` in `frontend/src/features/orders/FocusedPickView.tsx` — one product at a time, on-screen navigation, quantity emphasis, reusing the existing pick and report-issue calls from `ordersApi.ts`
- [X] T029 [US2] Add swipe navigation to `frontend/src/features/orders/FocusedPickView.tsx` as an **additional** affordance over the on-screen controls, never as the only way to move, and never bound to a recording action
- [X] T030 [US2] Implement `SetTransition` in `frontend/src/features/orders/SetTransition.tsx` covering the finished-box panel and the unfinished-box guard
- [X] T031 [US2] Wire the view switch into `frontend/src/features/orders/OrderDetailPage.tsx`, including the end-of-order hand-off as a callback so feature 017's completion screen can plug into it
- [X] T032 [US2] Render progress — products, physical cards, and position within the current set — in `frontend/src/features/orders/OrderDetailPage.tsx`
- [X] T033 [P] [US2] Style the focused view and transitions in `frontend/src/features/orders/OrderDetailPage.css`
- [X] T034 [US2] E2E coverage in `frontend/e2e/mobile-picking.spec.ts` at a phone viewport — focused view is default, navigation records nothing (assert the line's outcome is unchanged after navigating away and back), an explicit confirm records, the finished-box transition appears, and the unfinished-box guard blocks a silent exit
- [X] T035 [P] [US2] E2E coverage in `frontend/e2e/responsive.spec.ts` asserting the list view is the default at desktop width and no horizontal scroll appears in the focused view at phone width

**Checkpoint**: US2 shippable. The phone experience is the one the Product Owner asked for.

---

## Phase 5: User Story 3 — Start an order from the order itself (Priority: P3)

**Goal**: viewing an order is always safe, claiming is explicit on the order, and a picker who
already holds an order is offered it back rather than offered an action that fails.

**Independent Test**: open an unclaimed order without claiming it; claim it; then return to the
dashboard holding it and confirm it offers to resume.

### Tests for User Story 3 ⚠️ WRITE FIRST, MUST FAIL

- [X] T036 [P] [US3] Integration test in `backend/tests/LootSingles.IntegrationTests/Dashboard/ActiveClaimTests.cs` asserting `activeClaim` returns the order the authenticated employee holds, and `null` when they hold none
- [X] T037 [US3] Integration test in `backend/tests/LootSingles.IntegrationTests/Dashboard/ActiveClaimTests.cs` asserting `activeClaim` **still returns the order when it sits in Needs Attention** rather than In Progress — the case an In-Progress scan would miss, since reporting an issue retains the claim (feature 015)
- [X] T038 [P] [US3] Integration test in `backend/tests/LootSingles.IntegrationTests/Dashboard/ActiveClaimTests.cs` asserting one employee's `activeClaim` never exposes another employee's order or identifier
- [X] T039 [P] [US3] Component test in `frontend/tests/dashboard/DashboardPage.test.tsx` asserting the dashboard offers to resume when `activeClaim` is present, and offers to start work when it is null
- [X] T040 [P] [US3] Component test in `frontend/tests/orders/OrderDetailPage.test.tsx` asserting an unclaimed order renders a claim action, and that merely rendering the page issues no claim request
- [X] T041 [P] [US3] Component test in `frontend/tests/orders/OrderDetailPage.test.tsx` asserting that when the viewer already holds another order, the page explains this instead of offering a claim action
- [X] T042 [P] [US3] Component test in `frontend/tests/orders/OrderDetailPage.test.tsx` asserting a claim rejected because another picker holds it names that holder

### Implementation for User Story 3

- [X] T043 [US3] Add the `ActiveClaim` field to the dashboard response shape in `backend/src/LootSingles.Application/Dashboard/OrderSummary.cs`, reusing the existing `OrderSummary` record rather than adding a parallel type
- [X] T044 [US3] Add the active-claim read to `backend/src/LootSingles.Application/Dashboard/IDashboardRepository.cs` and `DashboardService.cs`
- [X] T045 [US3] Implement the projection in `backend/src/LootSingles.Infrastructure/Persistence/DashboardRepository.cs` — computed from the employee's claim directly, **not** by filtering a status list, so new lifecycle states in features 017 and 018 need no change here. `AsNoTracking`, projected to the DTO
- [X] T046 [US3] Return the field from `backend/src/LootSingles.Api/Controllers/DashboardController.cs` for the authenticated employee only
- [X] T047 [P] [US3] Add the `activeClaim` type to `frontend/src/features/dashboard/dashboardApi.ts`
- [X] T048 [US3] Render the resume affordance in `frontend/src/features/dashboard/DashboardPage.tsx`
- [X] T049 [US3] Add the claim action and the already-hold explanation to `frontend/src/features/orders/OrderDetailPage.tsx`, calling the existing `POST /api/orders/{orderId}/claim`
- [X] T050 [P] [US3] Add the claim call to `frontend/src/features/orders/ordersApi.ts`
- [X] T051 [US3] E2E coverage in `frontend/e2e/order-claiming.spec.ts` — viewing does not claim, claiming from the order works, the dashboard offers to resume while holding, and reporting an issue then returning to the dashboard **still** offers to resume

**Checkpoint**: all three stories complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T052 Assess whether any new behaviour warrants production logging per Constitution XI, and add it via `ILogger<T>` with non-PII structured fields only where warranted. Expectation: little or none — grouping is client-side and the dashboard change is a read. Record the assessment rather than adding logging reflexively
  - **Assessment (2026-09-20): no logging added.** The branch's only production backend change is `GetActiveClaimAsync`, a read on the dashboard returning the signed-in employee's own claim. It is neither an important event nor a failure path, and it has no error branch of its own. Everything else in the feature — grouping, the focused view, the final review — is client-side. Claiming and releasing, the events that *are* worth logging, already log where they are handled and were not changed here. Adding a log line for a read would be the reflexive logging Constitution XI warns against.
- [X] T053 [P] Run `dotnet csharpier format .` and the frontend formatter; confirm both are clean
- [X] T054 [P] Confirm the backend and frontend builds produce zero new warnings
  - Backend: 0 warnings, 0 errors. Frontend: `tsc -b` initially failed on two imports in `OrderDetailPage.tsx` left dead when the issue form moved to `ReportIssueForm` — removed; build is clean. `oxlint` reports one pre-existing warning in `AuthContext.tsx`, untouched by this branch.
- [X] T055 Run the full suites — backend unit, backend integration (Docker required), frontend, and Playwright — and confirm all pass
  - 228 backend unit, 181 backend integration, 183 frontend unit, 24 Playwright — all passing.
- [X] T056 Walk [quickstart.md](quickstart.md) manually, including the order line with no recorded set
  - Story 2's steps were rewritten on 2026-09-20 to match the approved spec. They had described the set-transition panel and the three-choice guard (both removed by FR-019a), the view toggle and the `localStorage`-blocked fallback (both removed by FR-009). Walked against the corrected steps and passed, 2026-09-21.
- [X] T057 Run `/branch-review` and resolve every Required finding before `/speckit-converge`
  - First round, 2026-09-21: **PASS WITH SUGGESTIONS** — zero Required findings, three Optional (BR-001 to BR-003). The Product Owner accepted all three; they are planned as Phase 7 below.
  - Second round, 2026-09-21, after Phase 7: **PASS** — zero Required, zero Optional. All three round-1 findings verified resolved in the code rather than from the checkboxes. No third round: a round producing no Required findings is the stopping point.

---

## Phase 7: Review Remediation (branch-review round 1)

All three findings are **Optional** and were accepted by the Product Owner on 2026-09-21.
None blocks `/speckit-converge`.

### BR-002 — `computeProgress` reports a position it does not have

Behavioural: the function returns a wrong value. Test-first — the regression test must fail
against the current implementation before the fix.

- [X] T058 [US1] Add a failing regression test in `frontend/tests/orders/orderGrouping.test.ts` proving `computeProgress(groups, null)` currently reports `currentSetPosition: 1` and the first group's `currentSetName`, when there is no current line to have a position in. Cover the same for a `currentLineId` that matches no line
- [X] T059 [US1] Make `computeProgress` in `frontend/src/features/orders/orderGrouping.ts` return `currentSetPosition: 0` and an empty `currentSetName` when `currentLineId` is null or unmatched, keeping the 1-based floor for a line that *is* found, so T058 passes. Confirm `OrderDetailPage.tsx:204` (the desktop header, which passes `null`) still reads the same card counts

### BR-003 — no component test for completing an order the employee does not hold

The fix is already in (`OrderDetailPage.tsx:339`, commit `64314de`), so this is the regression
test that fix never got at the component level. It is currently covered only end to end.

- [X] T060 [US3] Add a regression test to `frontend/tests/orders/OrderDetailPage.test.tsx`, in the existing `OrderDetailPage on a phone — letting go of an order` block, proving that reaching the final review on an order this employee does **not** hold and pressing the primary action calls **no** `releaseOrder` and still reaches `/orders`. Verify it genuinely guards the defect by collapsing `onCompleted={canRelease ? handleRelease : …}` back to an unconditional `handleRelease` and confirming the test fails, then restore it

### BR-001 — stale comment above `onCompleted`

Non-behavioural: a comment only. No automated regression test is meaningful here — no test can
observe a comment, and inventing one to satisfy TDD would be the reflexive coverage the
constitution warns against. Verified by reading the diff.

- [X] T061 [P] Delete the first of the two stacked comment blocks above `onCompleted` in `frontend/src/features/orders/OrderDetailPage.tsx:333-334` ("Feature 017's pick completion screen plugs in here. Until it exists, finishing shows the whole order rather than a dead end") — finishing no longer shows the whole order, and the block below it already explains the release-vs-close split and names the 017 seam

**Checkpoint**: re-run `/branch-review` (T057) once Phase 7 is implemented.

---

## Dependencies

```text
Phase 1 Setup (T001–T003)
      ↓
Phase 3 US1 — grouping (T004–T015)
      ↓                      ↘
Phase 4 US2 — focused view    Phase 5 US3 — claiming (T036–T051)
   (T016–T035)                  independent of US1 and US2
      ↓                      ↙
Phase 6 Polish (T052–T057)
      ↓
Phase 7 Review remediation (T058–T061) → re-run T057
```

- **US2 depends on US1.** The focused view cannot announce "box finished" until products are
  grouped into boxes. This is a real dependency, not a sequencing preference.
- **US3 depends on neither.** It could be built first. It is last only because it is the
  smallest and least urgent.
- Within US1, T010 must precede T011 and T012; T013 depends on all three.
- Within US2, T026 depends on T010; T028 and T030 depend on T026; T031 depends on T027, T028
  and T030.
- Within US3, T043 → T044 → T045 → T046 in order; T047–T050 depend on T046 for the contract.
- Within Phase 7, T058 must precede T059 — the regression test has to be seen failing before
  the fix that makes it pass. T060 and T061 depend on nothing and on each other not at all.

## Parallel Opportunities

- **T004–T009** — all US1 tests target different behaviours and can be written together.
- **T016–T025** — the US2 test set, except T020, which shares a file with others and is called
  out separately because it is the feature's central safety assertion.
- **T036, T038** — independent integration tests. T037 is sequential because it shares setup.
- **T039–T042** — component tests across two files.
- **T047, T050** — separate frontend API modules.
- **T053, T054** — formatting and warning checks.

## Implementation Strategy

**MVP**: Phase 1 + User Story 1. That alone reorders every order into the boxes a picker walks,
in the list view that already exists, and is worth shipping on its own.

**Then** User Story 2, which delivers the experience the Product Owner asked for and is the
largest single piece of work in the feature.

**Then** User Story 3, closing the dashboard dead end. Small — one backend field and a button
against an endpoint that already exists.

All three land in **one pull request** (Product Owner decision). The checkpoints above are
where the branch is known-good, so a mid-feature review has stable points to look at.

**TDD is not optional here.** Every test task precedes its implementation task and must be seen
to fail first (Constitution Principle IV). T020 deserves particular care: a test asserting that
nothing happened is worthless if written carelessly. It must assert that no request was issued
and that recorded outcomes are unchanged — not merely that no error was shown. Vacuous
assertions masked a real failure during feature 015.
