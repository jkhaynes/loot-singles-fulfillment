---

description: "Task list for 018-phone-issue-card"
---

# Tasks: Reported Issues on the Card and the List

**Input**: Design documents from `/specs/018-phone-issue-card/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/card-ui.md](contracts/card-ui.md),
[quickstart.md](quickstart.md)

**Tests**: **Required, not optional.** Constitution Principle IV is NON-NEGOTIABLE: every
behavioural change follows Red → Green → Refactor, and a behaviour's test task appears before its
implementation task. Each test task names what it must prove, and must be seen to fail for the
expected reason before its implementation task starts.

**Organization**: Grouped by user story. The stories build on one another: US2's sheet opens from
US1's chip, and US3's corrections live in US2's sheet. See Dependencies.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task serves (US1–US3)

## Path Conventions

Frontend only (plan.md), plus the E2E host's seed: `frontend/src/features/orders/`,
`frontend/tests/orders/`, `frontend/e2e/`, `backend/tests/LootSingles.E2EHost/Program.cs`.

---

## Phase 1: Setup

**Purpose**: The E2E data this feature's end-to-end tests need. The suite runs fully parallel, so
the new spec gets an order and a picker of its own.

- [X] T001 Seed an order `E2E-ORDER-00016` with one product of quantity 4 ("Issue Card Four") and one of quantity 1 ("Issue Card One"), both in one set, and a picker `e2epickerfourteen`, in `backend/tests/LootSingles.E2EHost/Program.cs`, following the 017 T101/T108 seed entries

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Make the tests' "next card" locators exact before a second control with that text
exists (research.md §6). Otherwise a later red phase could fail on an ambiguous locator rather than
on the behaviour under test.

- [X] T002 Change every `/next card/i` button locator to the exact name `'Next card'` (`{ name: 'Next card', exact: true }`) in `frontend/tests/orders/FocusedPickView.test.tsx`, `frontend/tests/orders/OrderDetailPage.test.tsx` and `frontend/e2e/pick-handoff.spec.ts`, and any other file where a test reports an issue and then moves on. Run the unit and E2E suites and confirm they are unchanged and green. This changes no behaviour

**Checkpoint**: The suites pass with exact locators. User story work can begin.

---

## Phase 3: User Story 1 — A reported product stops offering to be picked (Priority: P1) 🎯 MVP

**Goal**: A product with a reported issue shows a chip naming the issue, and its dock offers only
moving on. Nothing on the screen states a quantity the picker did not pull.

**Independent test**: On a phone, report "3 found of 4" on a quantity-4 product. The card shows a
**Card Not Found** chip, and the dock shows **Next card ›** and the arrows only, with no "Pulled all
4" and no **Report an issue**.

### Tests for User Story 1 (write first, watch fail)

- [X] T003 [US1] Write failing RTL tests in `frontend/tests/orders/FocusedPickView.test.tsx`, in a new `describe('FocusedPickView — a reported product')`, using a line with `pickOutcome: 'hasIssue'` and a `currentIssue` of Card Not Found, required 4, found 3, on a quantity-4 line:
  - the card shows a `button` named **Card Not Found**, and the chip names the type even though counts exist (FR-001, FR-002);
  - with `canRecordOutcome` true, the dock has a `button` named **Next card ›** and the ‹ › arrows, and no button whose name starts with "Picked" or "Pulled all", and no **Report an issue** (FR-005, FR-006);
  - the product's identity, image and quantity emphasis are still shown (FR-004).
- [X] T004 [US1] Write failing RTL tests in the same file:
  - tapping **Next card ›** shows the next product and calls neither `onPicked` nor `onReportIssue`;
  - from the last product it opens the final review, exactly as the › arrow does (FR-007);
  - moving away and back to the reported product still shows the chip and the issue dock.
- [X] T005 [US1] Write RTL tests in the same file pinning what must not change:
  - a line with no outcome keeps **Pulled all 4** and **Report an issue**;
  - a picked line keeps **Picked ✓** and has no chip (FR-008);
  - a reported line with `canRecordOutcome` false shows the chip, and its dock shows what it shows today (the Claim action or the blocked reason), not **Next card ›**.

  These pass before and after the change. Their job is to fail if T006 overreaches.

### Implementation for User Story 1

- [X] T006 [US1] In `frontend/src/features/orders/FocusedPickView.tsx`, replace the small issue text with a chip button naming `pickingIssueTypeLabel(line.currentIssue.issueType)` (it does nothing yet; US2 opens the sheet). When the line is `hasIssue` and `canRecordOutcome` is true, render the issue dock: **Next card ›** calling `goNext`, plus the existing navigation, in place of the Picked and Report buttons. This makes T003 and T004 pass with T005 still green
- [X] T007 [P] [US1] Style the chip (amber, from the existing warning tokens, reading as tappable) and the issue dock's primary button in `frontend/src/features/orders/OrderDetailPage.css`, matching direction C

**Checkpoint**: US1 is complete and demonstrable. The defect is gone: a reported product can no
longer offer "Pulled all N".

---

## Phase 4: User Story 2 — The picker can see what was reported (Priority: P1)

**Goal**: Tapping the chip opens a sheet showing the issue type, the counts (when both exist), the
note, and the reporter and time, with **Close**.

**Independent test**: Report with counts and a note, tap the chip, and see "Card Not Found", "3 of
4 pulled", "1 short", the note, and the reporter and time. Close it and the card is unchanged.

### Tests for User Story 2 (write first, watch fail)

- [X] T008 [US2] Write failing RTL tests in `frontend/tests/orders/FocusedPickView.test.tsx`:
  - no `dialog` is present on arriving at a reported product;
  - tapping the chip opens a `dialog` named **Reported issue** (FR-009) showing the issue type, "3 of 4 pulled" and "1 short", the note, and the reporter's name with the time (FR-010–FR-013);
  - omission cases, each asserted separately:
    - a report with only one of the two counts shows no quantity line (research.md §5);
    - a report with neither count shows no quantity line;
    - a report with no note shows no note;
    - a report with a null reporter name shows the time alone;
  - **Close** removes the dialog and calls neither `onPicked` nor `onReportIssue` (FR-014);
  - moving to another product closes the sheet.

### Implementation for User Story 2

- [X] T009 [US2] In `frontend/src/features/orders/FocusedPickView.tsx`, add `isShowingIssue` local state:
  - the chip sets it;
  - `goTo` clears it;
  - the sheet renders only while it is set **and** the line is `hasIssue` (research.md §2);
  - the sheet is an element with `role="dialog"` and `aria-label="Reported issue"` (research.md §3), showing the fields per data-model.md, with **Close**.

  This makes T008 pass.
- [X] T010 [P] [US2] Style the sheet in `frontend/src/features/orders/OrderDetailPage.css`: a bottom sheet over a dimmed card, from the existing tokens, matching direction C

**Checkpoint**: US1 and US2 are complete. A picker can see everything that was reported, without
leaving the product.

---

## Phase 5: User Story 3 — The picker can correct a report from the sheet (Priority: P2)

**Goal**: From the sheet, **I found all N** (or **I found it**) records the product as picked, and
**Change report** opens the issue form pre-filled. Neither is offered to someone who cannot record.

**Independent test**: From the sheet, tap **I found all 4** and see the card return to picked.
Separately, tap **Change report**, change the type, submit, and see the chip show the new type.

### Tests for User Story 3 (write first, watch fail)

- [X] T011 [P] [US3] Write failing unit tests for `foundActionLabel` in `frontend/tests/orders/foundActionLabel.test.ts`: `1` → "I found it"; `2` and `4` → "I found all 2" and "I found all 4" (FR-015; research.md §7)
- [X] T012 [P] [US3] Write failing RTL tests in a new `frontend/tests/orders/ReportIssueForm.test.tsx`:
  - given `initial` (Wrong Variant, required 4, found 3, a note), every field starts from it, and submitting unchanged sends that same request;
  - without `initial` the form starts blank, as today, which is the desktop path (FR-016; research.md §4).
- [X] T013 [US3] Write failing RTL tests in `frontend/tests/orders/FocusedPickView.test.tsx`, on the open sheet:
  - with `canRecordOutcome` true, it offers **I found all 4**, **Change report** and **Close**; on a quantity-1 line the found action reads **I found it** (FR-015);
  - **I found all 4** calls `onPicked` with the line's id;
  - while `recordingLineId` is that line, the correction buttons are disabled (FR-020);
  - when the line stays `hasIssue` after recording (a failed save), the sheet is still open and the chip is unchanged (FR-019);
  - **Change report** closes the sheet and opens the issue form pre-filled from `currentIssue`; submitting calls `onReportIssue` with the edited request; **Cancel** calls nothing (FR-016, FR-017);
  - with `canRecordOutcome` false, the sheet offers only **Close** (FR-018).

### Implementation for User Story 3

- [X] T014 [P] [US3] Create `frontend/src/features/orders/foundActionLabel.ts`, making T011 pass
- [X] T015 [P] [US3] Add an optional `initial?: PickingIssueDetail` prop to `frontend/src/features/orders/ReportIssueForm.tsx` that seeds each field's initial state, making T012 pass. The desktop call site in `OrderDetailPage.tsx` passes nothing and is unchanged
- [X] T016 [US3] In `frontend/src/features/orders/FocusedPickView.tsx`, add the sheet's correction actions when `canRecordOutcome` is true:
  - the found action, labelled by `foundActionLabel(line.quantity)`, calls `onPicked`, disabled while recording;
  - **Change report** clears `isShowingIssue` and opens `ReportIssueForm` with `initial={line.currentIssue}`.

  This makes T013 pass.

**Checkpoint**: All three stories are complete. Corrections live in the sheet, one deliberate tap
beyond the chip.

---

## Phase 6: Polish and Cross-Cutting Concerns

### End to end

- [X] T017 Write `frontend/e2e/mobile-issue-card.spec.ts` at phone size, as `e2epickerfourteen` on `E2E-ORDER-00016`, covering quickstart.md scenarios 1–4:
  - Report 3 of 4 on the quantity-4 product. Assert the **Card Not Found** chip, the **Next card ›** dock, and that no button reads "Pulled all 4" or "Report an issue".
  - Open the sheet and assert its contents.
  - **Change report** to Wrong Variant and assert the chip changes.
  - **I found all 4** and assert **Picked ✓**.
  - On the quantity-1 product, report, then assert the sheet's **I found it**.
- [X] T018 In the same file, cover quickstart.md scenario 5 with a second, read-only context. As an employee who does not hold the claim, open the order while it has a reported product. Assert the chip, a sheet with **Close** only, and no **Next card ›** dock. Use an employee that is not the claim holder; `e2emanager` is already seeded

### Ordinary gates

- [X] T019 [P] Review whether anything here warrants production logging, per the constitution's Observability standard. Expected answer: none new, because the pick and report calls are already logged server-side. Record the decision in `plan.md` if it differs
- [X] T020 [P] Run `npm --prefix frontend run build`, `npm --prefix frontend run lint` and `npm --prefix frontend run format:check`; `tsc --noEmit` checks nothing in this project
- [X] T021 [P] Run `npm --prefix frontend test` and the full Playwright suite, and confirm both are green
- [ ] T022 Walk quickstart.md on a real phone against the dev stack, and correct any step that does not match what was built
- [ ] T023 Run `/branch-review` and resolve every Required finding before `/speckit-converge`, per CLAUDE.md's Branch Review Gate

> **T022 and T023 now run after Phase 7.** The amendment below changes what the quickstart describes
> (its desktop scenarios 6–8 are new) and adds code to review. Walking and reviewing the phone-only
> version first would be done twice.

---

## Phase 7: Amendment 2026-09-22 — every issue type, "Resolved", and the desktop list

**Purpose**: Carry out the Product Owner's decisions recorded in spec.md's Clarifications (Session
2026-09-22), on top of the phone work committed in `dc5e843`:
- the phone sheet is reworded;
- a report's details move into one shared component;
- the desktop list gains the issue panel (US4).

See plan.md, "What the amendment changes", and research.md §5 and §7–§10.

**Test-first on changed behaviour**: the first round's tests assert the old wording ("3 of 4
pulled", "1 short", "I found all 4", "Change report"). They are reworded **before** the code
changes, so each fails because the old behaviour is still there, not because a test was edited to
match new code.

### Setup

- [ ] T024 Seed an order `E2E-ORDER-00017` and a picker `e2epickerfifteen` in `backend/tests/LootSingles.E2EHost/Program.cs`, following T001's entry, for the desktop E2E (T036). It needs three products in one set: "Panel Card Four" (quantity 4), "Panel Card Damaged" (quantity 1) and "Panel Card Clean" (quantity 1)

### A report's details, shared by both views (research.md §8)

- [ ] T025 [P] Write failing RTL tests in a new `frontend/tests/orders/ReportedIssueDetails.test.tsx` for a `ReportedIssueDetails` component given a `PickingIssueDetail`:
  - it shows the issue type label;
  - with both counts it shows "Required 4 · Found 3"; with only one count, or neither, there is no counts line (FR-011; research.md §5);
  - the note is shown when present and absent otherwise (FR-012);
  - the reporter and time are shown, or the time alone when the name is null (FR-013);
  - for **every** entry in `pickingIssueTypes`, with counts and a note, no text matches `/pulled|short/i` (FR-011, SC-001).

  It must fail because the component does not exist yet.
- [ ] T026 Create `frontend/src/features/orders/ReportedIssueDetails.tsx` from the details the phone sheet renders today (`ReportedIssueSheet` in `FocusedPickView.tsx`), with the counts in the form's own words, making T025 pass

### Phone: the sheet reworded (US2, US3)

- [ ] T027 [US2] [US3] Reword the first round's sheet tests in `frontend/tests/orders/FocusedPickView.test.tsx` **before any code changes**:
  - "3 of 4 pulled" and "1 short" become "Required 4 · Found 3";
  - "I found all 4" and "I found it" become **Resolved**; the single-copy test becomes "reads Resolved whatever the quantity";
  - "Change report" becomes **Edit report**.

  Add a case: a Card Damaged report with a note and no counts shows no counts line, and nothing in the sheet matches `/pulled|short/i`. Run the file and confirm the reworded tests fail on the old wording.
- [ ] T028 [US2] [US3] Reword `frontend/e2e/mobile-issue-card.spec.ts` the same way ("Required 4 · Found 3", **Resolved**, **Edit report**, and no "pulled" or "short" in the sheet). Run it against the current code and confirm it fails on the old wording
- [ ] T029 [US2] [US3] In `frontend/src/features/orders/FocusedPickView.tsx`, make the sheet render `ReportedIssueDetails`, label the correction **Resolved** and the edit action **Edit report**. Delete `frontend/src/features/orders/foundActionLabel.ts` and `frontend/tests/orders/foundActionLabel.test.ts` (research.md §7). This makes T027 and T028 pass

### Desktop: the issue panel (US4)

**Goal**: A reported row on the desktop list shows what was reported, and offers **Resolved** and
**Edit report** in place of **Picked** and **Report Issue**.

**Independent test**: On a desktop, report 3 of 4 with a note. The row shows the panel ("Required 4
· Found 3", the note, and the reporter and time) with **Resolved** and **Edit report**, and no
**Picked** or **Report Issue**. **Edit report** opens the form filled in.

- [ ] T030 [US4] Write failing RTL tests in `frontend/tests/orders/OrderDetailPage.test.tsx`, in a new `describe('OrderDetailPage — a reported row (018 US4)')` at desktop width (`installMatchMedia(false)`), with an order claimed by the viewer:
  - a row reported as Card Not Found, required 4, found 3, with a note, shows the issue type, "Required 4 · Found 3", the note, and the reporter and time; it offers **Resolved** and **Edit report**, and no **Picked** or **Report Issue** (FR-022–FR-024);
  - a row reported as Card Damaged with a note and no counts shows no counts line;
  - **Resolved** calls `recordPicked` for that line, and after it resolves the row shows **Picked** pressed;
  - while the pick records, **Resolved** and **Edit report** are disabled; when `recordPicked` rejects, the panel is still there and the page shows its error (FR-026);
  - **Edit report** opens the issue form in the row, pre-filled from the current report, with the panel's actions hidden; submitting a changed note calls `reportIssue` with the edited request; **Cancel** calls nothing (FR-023);
  - on an order someone else holds, the panel shows the report with neither **Resolved** nor **Edit report** (FR-025).
- [ ] T031 [US4] Write RTL tests in the same describe pinning what must not change: a row with no outcome and a picked row both keep **Picked** and **Report Issue**, and show no issue panel (FR-027). These pass before and after the change. Their job is to fail if T032 overreaches
- [ ] T032 [US4] In `frontend/src/features/orders/OrderDetailPage.tsx`, render the issue panel for a `hasIssue` row in place of the one-line `order-detail-line__issue` summary and the Picked and Report Issue buttons (research.md §9):
  - the panel shows `ReportedIssueDetails`;
  - when `canRecordOutcome`, it offers **Resolved** (`handlePicked(line.id)`) and **Edit report** (`setIssueFormLineId(line.id)`), both disabled while `recordingLineId === line.id`;
  - while the form is open for that row, the panel's actions are hidden and the form gets `initial={line.currentIssue}`.

  This makes T030 pass with T031 still green.
- [ ] T033 [P] [US4] Style the issue panel in `frontend/src/features/orders/OrderDetailPage.css`: amber border and tint from the existing warning tokens, the details in a row, and the actions beneath, matching the final mockups
- [ ] T034 [US4] Run the whole of `frontend/tests/orders/OrderDetailPage.test.tsx` and confirm the tests that existed before T030 pass unchanged (research.md §10). If one fails, it was asserting the old summary or buttons on a reported row. Update it to the panel's behaviour and record why in this task

### End to end and gates

- [ ] T035 [US4] Write `frontend/e2e/desktop-issue-panel.spec.ts` at desktop size, as `e2epickerfifteen` on `E2E-ORDER-00017`:
  - report 3 of 4 with a note on "Panel Card Four" through **Report Issue**; assert the panel, and that the row has no **Picked** or **Report Issue**;
  - report "Panel Card Damaged" as Card Damaged with a note and no counts; assert no counts line;
  - **Edit report** on "Panel Card Four": assert the form is filled in, change the note, submit, and assert the new note;
  - **Resolved**: assert **Picked** pressed;
  - as `e2emanager` in a second context, assert the Card Damaged panel shows no **Resolved** or **Edit report**;
  - "Panel Card Clean" keeps **Picked** and **Report Issue** throughout.
- [ ] T036 [P] Run `npm --prefix frontend run build`, `npm --prefix frontend run lint`, `npm --prefix frontend run format:check`, and `dotnet csharpier check backend`
- [ ] T037 [P] Run `npm --prefix frontend test` and the full Playwright suite, and confirm both are green

**Checkpoint**: The amendment is complete. Then T022 (the quickstart, now nine scenarios, on a phone
and a desktop) and T023 (`/branch-review`).

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 7 (the amendment)**: after Phases 1–6.
  - T024 is needed only by T035.
  - T025 → T026 come first, because T029 and T032 both use the shared component.
  - The phone (T027 → T028 → T029) and the desktop (T030 + T031 → T032 → T033, T034) are independent of each other after T026.
  - T035–T037 come last, then T022 and T023.

- **Setup (Phase 1)**: none. T001 is needed only by the E2E tasks (T017, T018).
- **Foundational (Phase 2)**: none. It blocks every user story's red phase.
- **US1 (Phase 3)**: after Phase 2.
- **US2 (Phase 4)**: after US1. The sheet opens from US1's chip.
- **US3 (Phase 5)**: after US2. The corrections live in the sheet. T011/T014 and T012/T015 are independent of the sheet and can start any time after Phase 2.
- **Polish (Phase 6)**: after all stories.

### Within each story

The test task is seen to fail for the expected reason → its implementation task → the story's
tests green → the checkpoint.

## Parallel Opportunities

- T001 alongside T002.
- **US1**: T007 (CSS) alongside T006 once T003–T005 are red.
- **US2**: T010 (CSS) alongside T009.
- **US3**: T011 and T012 in parallel, then T014 and T015 in parallel; T013 → T016 after them.
- **Polish**: T019, T020 and T021 in parallel.

```text
# US3, after Phase 2:
T011 foundActionLabel tests      T012 ReportIssueForm pre-fill tests
T014 foundActionLabel            T015 ReportIssueForm `initial`
                 └──── T013 sheet corrections tests ──── T016 sheet corrections
```

## Implementation Strategy

### MVP

**US1 alone fixes the defect.** Once the dock stops offering "Pulled all N" on a reported product,
a picker can no longer state a quantity they didn't pull, or replace a report with one mis-tap.
Stop after Phase 3 if the rest must wait.

### Incremental delivery

1. Phases 1–2: seed and exact locators.
2. US1: the defect is fixed.
3. US2: what was reported is visible on the phone.
4. US3: corrections without leaving the card.
5. Polish: E2E, gates, quickstart on a phone, branch review.

### Notes

- **No backend changes** apart from the E2E seed (research.md §1). A task that finds itself
  editing an API or a repository has left the plan.
- **The sheet follows the line's state** (research.md §2). Don't add code that closes it after a
  successful correction; the line leaving `hasIssue` does that.
- Commit after each completed task or coherent group; the Product Owner confirms each commit.
