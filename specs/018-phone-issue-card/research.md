# Research: Reported Issues on the Card and the List

Decisions for [plan.md](plan.md). Every decision was taken by reading the code as it stands.
Sections 1–6 date from the original phone-only plan (2026-09-21); sections 5 and 7 were revised,
and 8–10 added, after the Product Owner's clarifications of 2026-09-22 (spec.md, Clarifications).

## 1. No backend change

**Decision**: The feature is frontend only. No endpoint, DTO, migration or repository changes.

**Rationale**: Everything shown is already in the order detail both views load.
`OrderLineDetail.currentIssue` carries `issueType`, `requiredQuantity`, `foundQuantity`, `note`,
`reportedByEmployeeName` and `reportedAt`. Both corrections are existing calls:

- **Resolved** is `recordPicked`. A later outcome supersedes a report (015 FR-011), and the server
  already does that.
- **Edit report** is `reportIssue` again. A new report supersedes the old one, the same way.

This holds for the desktop list too, which calls the same two functions through
`OrderDetailPage`'s `handlePicked` and `handleReportIssue`.

**Alternatives considered**: A dedicated "resolve issue" endpoint was rejected. It would be a second
way of doing what recording an outcome already does, and the history of superseded reports is
already kept (015).

## 2. The phone sheet is shown only while the product has a report

**Decision**: The sheet's visibility is local state (`isShowingIssue`) in `FocusedPickView`, and it
renders only while the current line's `pickOutcome` is `hasIssue`. **Resolved** keeps the sheet open
with its buttons disabled while the pick records. When the line comes back picked, the sheet
disappears because its condition no longer holds; nothing has to close it explicitly.

**Rationale**: This satisfies three requirements with no extra code.

- **FR-019, a correction that fails**: the line is unchanged, so the sheet stays and the existing
  page-level error explains why.
- **FR-020, taps while saving**: `recordingLineId === line.id` is the busy flag. It already disables
  the Picked button today.
- **A correction that succeeds**: the sheet goes away without a close call that could race the
  response.

**Alternatives considered**: Closing the sheet as soon as the button is tapped was rejected. The
picker would then see a card with no indication that anything is happening, or that it failed.

## 3. The phone sheet is a plain overlay, not `<dialog>`

**Decision**: An element with `role="dialog"` and an accessible name, drawn over the card and
anchored to the bottom of the screen, as in direction C. Not the native `<dialog>` opened with
`showModal()`.

**Rationale**: jsdom, which the RTL suite runs in, does not implement `showModal()`. The application
has no accessibility scope (an in-house tool), so what the native modal adds (a focus trap,
top-layer stacking) is not required.

## 4. Editing reuses the issue form, pre-filled

**Decision**: `ReportIssueForm` has an optional `initial` prop, a `PickingIssueDetail`. Its fields
start from that report instead of blank. The phone's **Edit report** closes the sheet and opens the
form with the current report. The desktop's **Edit report** opens the same form in the row, the way
**Report Issue** already does, with `initial` set.

**Rationale**: It's one form, which is why 016 extracted it. It was built for the phone in the first
round and needs no change for the desktop. The desktop's **Report Issue** on an unreported row still
passes no `initial`, so a first report starts blank as today.

**Alternatives considered**: A second, edit-only form was rejected. Two forms for one record drift
apart, which is the problem 016 solved.

## 5. Counts, in the form's own words (revised 2026-09-22)

**Decision**: Counts are shown as "Required R · Found F", and only when **both** `requiredQuantity`
and `foundQuantity` were recorded. Nothing computes or shows a difference, and no text calls
anything "pulled" or "short".

**Rationale**: The Product Owner rejected wording that assumes a shortage (spec.md, Clarifications).
Counts are optional for every issue type, and the server only checks that they aren't negative.
Their meaning depends on the type: for Damaged, "found" might mean found in good condition;
for Wrong Variant, found in the right variant. The form's own labels are the only wording that's
true for all of them. A count missing either half can't be written as "R · F" without inventing the
other half.

**Alternatives considered**:
- "F of R pulled" and "R − F short", the original design, was superseded.
- Filling a missing required count from the line's quantity was rejected. The line quantity is what
  was ordered, and the picker may have recorded something different on purpose.

## 6. The phone dock's primary button needs a distinct accessible name

**Decision**: The issue dock's primary button reads **Next card ›**. The ‹ › arrows keep their
names ("Previous card", "Next card").

**Consequence for tests**: A locator matching `/next card/i` finds both buttons on a reported
product. Test locators use the exact name `'Next card'` for the arrow. This was done in the first
round (T002).

## 7. The correction's label (revised 2026-09-22)

**Decision**: The correction reads **Resolved**, a fixed label, on both views, for every issue type
and quantity. The `foundActionLabel` helper built in the first round, which produced "I found it" and
"I found all N", is deleted along with its test.

**Rationale**: The Product Owner chose "Resolved" (spec.md, Clarifications). A fixed label has no
rule left to test, so a helper for it would be abstraction without a job.

**Alternatives considered**:
- "I found all N" and "Mark all N picked" were both rejected by the Product Owner in favour of
  "Resolved".

## 8. One component renders a report's details, for both views (added 2026-09-22)

**Decision**: Extract the details the phone sheet shows (issue type, counts, note, reporter and time)
into a small `ReportedIssueDetails` component beside `FocusedPickView`. It is used by the phone sheet
and by the desktop issue panel. The two views differ only in their frame (a bottom sheet versus a
panel in the row) and in where the actions sit.

**Rationale**: FR-022 requires the desktop panel to follow "the same rules as the sheet
(FR-011–FR-013)". Those rules are four omission cases (both counts, either count missing, no note,
no reporter name). Stating them once means one set of tests guards both views, and they cannot drift
into two different answers for the same report.

**Alternatives considered**: Duplicating the markup in each view was rejected. The rules are exactly
the part most likely to be fixed in one place and forgotten in the other.

## 9. The desktop row decides its own actions (added 2026-09-22)

**Decision**: In `OrderDetailPage`'s row rendering:
- when the line is `hasIssue`, the one-line `order-detail-line__issue` summary and the Picked and
  Report Issue buttons are replaced by the issue panel;
- the panel's **Resolved** calls `handlePicked(line.id)`, and **Edit report** sets
  `issueFormLineId` to the line, exactly as Report Issue does, with `initial={line.currentIssue}`;
- while the form is open for that row, the panel's actions are hidden (the form has its own Cancel).

**Rationale**: It's the same state and handlers the row already uses, and the busy flag
(`recordingLineId === line.id`) already disables the row's buttons while a save is in flight
(FR-026). A failed save leaves the line `hasIssue`, so the panel stays, and the page-level error
explains why, as it does today.

**Alternatives considered**: Moving the desktop row into its own component was rejected here. It
would be a refactor of 016's list for a change that touches one branch of it.

## 10. Existing desktop tests keep passing (added 2026-09-22)

**Decision**: No existing test is rewritten for the desktop change. The tests in
`frontend/tests/orders/OrderDetailPage.test.tsx` that report an issue check that the issue type and
the note appear (for example /Insufficient Quantity/ and /Only one left/), and that the order reads
Needs Attention. The panel keeps showing both. None asserts the old one-line summary ("found N of
M", "reported by …"), or Picked and Report Issue on a reported row.

**Correction, found while running the gates**: that scan covered the unit tests only. One E2E,
`frontend/e2e/pick-completion.spec.ts`, resolved a reported line by clicking **Picked** on it, which
FR-024 removes. It now clicks **Resolved**. The behaviour it guards (a reported line reaching Picked
after release and re-claim) is unchanged.

**Consequence**: The desktop tasks run this file after the row changes and confirm it stays green.
The new behaviour (the panel's contents, Resolved, Edit report, and no Picked or Report Issue on a
reported row) gets new tests, written first.
