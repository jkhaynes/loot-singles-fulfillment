# Research: Reported Issues on the Phone's Card

Phase 0 decisions for [plan.md](plan.md). Every decision was taken by reading the code as it stands
after feature 017 merged (`main` at `5501183`).

## 1. No backend change

**Decision**: The feature is frontend only. No endpoint, DTO, migration or repository changes.

**Rationale**: Everything the sheet shows is already in the order detail the phone loads.
`OrderLineDetail.currentIssue` carries `issueType`, `requiredQuantity`, `foundQuantity`, `note`,
`reportedByEmployeeName` and `reportedAt`. Both corrections are existing calls:

- **I found all N** is `recordPicked`. A later outcome supersedes a report (015 FR-011), and the
  server already does that.
- **Change report** is `reportIssue` again. A new report supersedes the old one, the same way.

**Alternatives considered**: A dedicated "resolve issue" endpoint was rejected. It would be a
second way of doing what recording an outcome already does, and the history of superseded reports
is already kept (015).

## 2. The sheet is shown only while the product has a report

**Decision**: The sheet's visibility is local state (`isShowingIssue`) in `FocusedPickView`, and it
renders only while the current line's `pickOutcome` is `hasIssue`. **I found all N** keeps the sheet
open with its buttons disabled while the pick records. When the line comes back picked, the sheet
disappears because its condition no longer holds; nothing has to close it explicitly.

**Rationale**: This satisfies three requirements with no extra code.

- **FR-019, a correction that fails**: the line is unchanged, so the sheet stays and the existing
  page-level error explains why.
- **FR-020, taps while saving**: the existing `recordingLineId === line.id` flag is the busy flag.
  It already disables the Picked button today.
- **A correction that succeeds**: the sheet goes away without a close call that could race the
  response.

The same state resets on moving to another product, which `goTo` already does for the issue form.

**Alternatives considered**: Closing the sheet as soon as the button is tapped was rejected. The
picker would then see a card with no indication that anything is happening, or that it failed.

## 3. The sheet is a plain overlay, not `<dialog>`

**Decision**: The sheet is an element with `role="dialog"` and an accessible name, drawn over the
card and anchored to the bottom of the screen, as in the direction C mockup. It is not the native
`<dialog>` element opened with `showModal()`.

**Rationale**: jsdom, which the RTL suite runs in, does not implement `showModal()`. Using it would
mean a polyfill or a mock just to test the sheet. The application has no accessibility scope (an
in-house tool), so what the native modal adds (a focus trap, top-layer stacking) is not required,
and a plain element is what 016 already uses for the issue form.

**Alternatives considered**: `<dialog open>`, the non-modal attribute form, renders in jsdom but
gives nothing over a plain element and reads as a modal that isn't one.

## 4. "Change report" reuses the issue form, pre-filled

**Decision**: `ReportIssueForm` gains an optional `initial` prop, a `PickingIssueDetail`. Its
fields start from that report instead of blank. **Change report** closes the sheet and opens the
form with the current report. Submitting goes through the existing `onReportIssue`.

**Rationale**: It's one form, which is why 016 extracted it. The desktop list view passes no
`initial`, so its behaviour cannot change (FR-021). Pre-filling follows PRD §19.2: common reporting
should not require unnecessary typing.

**Alternatives considered**: A second, sheet-specific edit form was rejected. Two forms for one
record drift apart, which is the problem 016 solved.

## 5. The quantity line needs both counts

**Decision**: The sheet shows "pulled F of R" and "R − F short" only when both `foundQuantity` and
`requiredQuantity` were recorded. If either is missing it omits the quantity line entirely.

**Rationale**: FR-011 says to omit rather than show a zero or blank value. A found count without a
required count, or the reverse, cannot be expressed as "X of Y" without inventing the missing half.
Both counts are optional in the form today (PRD §19.1 "where useful"), so partial reports exist.

**Alternatives considered**: Filling a missing required count from the line's quantity was
rejected. The line quantity is what was ordered. The picker may have recorded something different
on purpose, and silently substituting it would misstate the report.

## 6. The dock's new primary button needs a distinct accessible name

**Decision**: The issue dock's primary button reads **Next card ›**. The ‹ › arrows keep their
existing accessible names ("Previous card", "Next card").

**Consequence for tests**: A locator matching `/next card/i` will find **two** buttons on a
reported product, the new primary and the › arrow. Playwright's strict mode then fails. Existing
tests that report an issue and then move on must name the control they mean, with an exact name:

- `frontend/e2e/pick-handoff.spec.ts`: the held-order test reports and then moves on.
- `frontend/tests/orders/OrderDetailPage.test.tsx`: the BR-002 in-flight test reports and then
  moves on.

The tasks must update these before the dock changes, so they fail for the right reason rather than
on an ambiguous locator.

**Alternatives considered**: Removing the › arrow from the issue dock was rejected. FR-005 keeps
both, and the arrows are the navigation a picker already uses on every card.

## 7. Where the found action's wording lives

**Decision**: A small pure helper, `foundActionLabel(quantity)`, returns "I found it" for 1 and
"I found all N" otherwise. It is unit-tested beside the view.

**Rationale**: It's the one piece of wording with a rule in it (FR-015), and the dock's existing
`Pulled all N` label is the defect this feature exists to remove. A named rule with its own test is
cheaper than rediscovering it in a component test.

**Alternatives considered**: An inline conditional in the JSX was rejected. It would be the third
quantity-dependent label in the file, and the only one without a test.
