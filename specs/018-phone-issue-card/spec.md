# Feature Specification: Reported Issues on the Card and the List

**Feature Branch**: `018-phone-issue-card`

**Created**: 2026-09-21

**Status**: Draft (amended 2026-09-22: every issue type, "Resolved", the desktop list)

**Input**: User description: "Reported issues on the phone's card. On the single-card picking view, a product with a reported issue shows the issue instead of the untouched card's actions. The card stays as it is, plus one amber chip naming the issue type (for example "Card Not Found ›"); the chip always shows the type, even when needed/found counts exist. Tapping the chip opens a sheet showing the problem, pulled X of Y (when counts were recorded), the note, and who reported it and when. The sheet's actions are "I found all N", which records the product as picked and replaces the report (feature 015), "Change report" (a new report replaces the old one), and "Close". Once an issue is reported, the dock shows only a green "Next card ›" and the ‹ › arrows: the Picked button and "Report an issue" go away, so the dock can never state a quantity the picker didn't pull. This fixes "Pulled all 4" showing on a product reported as 3 of 4. Moving stays unblocked (016 FR-019a). Scope: the phone card view only; the desktop list, the review screen and the endings are unchanged. Builds on features 015, 016 and 017, and implements PRD §19 on the card. Mockups (direction C): https://claude.ai/artifact/8UzoNHfCH2QZgokeMtE7WD"

## Background

A picker on a phone works one product at a time (feature 016). When they report an issue on a
product, for example "need 4, found 3", the card today keeps offering the actions of a product
nobody has touched: its main button still reads **Pulled all 4**, and **Report an issue** is still
offered as if nothing had been reported. The only sign of the report is a small line of text
naming the issue type. The found and needed counts, the note and the reporter are recorded
(PRD §19.1–§19.3) but never shown on the phone.

The main button is the most dangerous part. Quantity greater than one is the application's
highest-risk information, and a button stating a quantity the picker did not pull contradicts the
report they have just made. Tapping it also silently replaces that report, because recording a
product as picked supersedes an earlier issue (feature 015).

The Product Owner reviewed three designs on 2026-09-21 and chose **direction C**: the card stays as
it is, a chip on it names the issue, and the detail lives in a sheet the chip opens.

The desktop list has the same fault, and one of its own. A reported row keeps its **Picked** button,
which replaces the report with no hint that it will, and **Report Issue** opens a blank form, so
changing a report means entering all of it again. On 2026-09-22 the Product Owner brought the
desktop list into this feature, and required the whole design to hold for every issue type rather
than assume the issue is a missing card.

## Clarifications

### Session 2026-09-22

Product Owner design decisions, made against the mockups at
https://claude.ai/artifact/XdfCTKJHRt4XPJMGDcM55p (final row), after the phone work had been built:

- Q: Does the design assume the issue is a shortage? → A: No. It must hold for every issue type
  (Card Damaged, Wrong Variant, Other and the rest), not only Card Not Found. Counts are shown in
  the issue form's own words, "Required N · Found M", only when both were recorded; nothing is
  described as "pulled" or "short". This supersedes "3 of 4 pulled" and "1 short".
- Q: What does the correction that records the product as picked say? → A: "Resolved", on the phone
  and the desktop, for every issue type and quantity. It still records the product as picked. This
  supersedes "I found all N" and "I found it".
- Q: Is the desktop list in scope? → A: Yes. A reported row shows an amber issue panel (the issue
  type, the reporter and time, and the counts and note when recorded) with "Resolved" and "Edit
  report", which opens the issue form pre-filled in the row. While a row is reported it offers no
  Picked or Report Issue button. This supersedes the earlier scope boundary that excluded the
  desktop view.
- Q: Should the phone sheet's button that opens the pre-filled form be renamed from "Change report" to "Edit report"? → A: Yes. It reads "Edit report" on the phone and the desktop.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A reported product stops offering to be picked (Priority: P1)

A picker reports an issue on a product, for example 3 found of 4 needed. The card now shows that
the product has a reported issue, and the dock offers only moving on. Nothing on the screen states
a quantity the picker did not pull.

**Why this priority**: This is the defect that started the feature. A button reading "Pulled all 4"
on a product reported as 3 of 4 misstates the highest-risk number in the application, and one tap
on it replaces the report.

**Independent Test**: On a phone, report an issue on a product with quantity 4, then confirm the
card shows an issue chip naming the issue type, the dock shows **Next card ›** and the arrows only,
and no control on the screen reads "Pulled all 4" or offers to report an issue.

**Acceptance Scenarios**:

1. **Given** a claimed order and a product with quantity 4 and no outcome, **When** the picker
   reports it as Card Not Found, 3 found of 4 needed, **Then** the card shows a chip reading the
   issue type ("Card Not Found"), and the dock shows **Next card ›** and the ‹ › arrows and nothing
   else.
2. **Given** a product with a reported issue, **When** the picker looks at the dock, **Then** there
   is no Picked or "Pulled all N" button and no **Report an issue** button.
3. **Given** a product with a reported issue, **When** the picker taps **Next card ›**, **Then** the
   next product opens and nothing is recorded, exactly as the › arrow does.
4. **Given** a product with a reported issue that is the last product of the order, **When** the
   picker taps **Next card ›**, **Then** the final review opens, as the › arrow does from the last
   product.
5. **Given** a product with a reported issue, **When** the picker moves away and comes back to it,
   **Then** it still shows the chip and the issue dock.

---

### User Story 2 - The picker can see what was reported (Priority: P1)

A picker comes back to a product with a reported issue, perhaps one reported by someone else
before the order was released and re-claimed. They tap the chip and see what was reported.

**Why this priority**: The report already holds the counts, the note and the reporter (PRD §19.1–
§19.3), and the phone never shows them. Without them, a picker coming back to the product cannot
tell what is actually in the sleeve.

**Independent Test**: Report an issue with counts and a note, tap the chip, and confirm the sheet
shows the issue type, "Required 4 · Found 3", the note, and the reporter and time.

**Acceptance Scenarios**:

1. **Given** a product reported as Card Not Found, 3 found of 4 needed, with the note "Only 3 in
   the binder slot", **When** the picker taps the chip, **Then** a sheet opens showing the problem
   ("Card Not Found"), the counts ("Required 4 · Found 3"), the note, and who reported it and when.
2. **Given** a product reported as Card Damaged with a note and no counts, **When** the picker taps
   the chip, **Then** the sheet shows the problem, the note, and who reported it and when, and no
   counts line.
3. **Given** a report with only one of the two counts recorded, **When** the sheet opens, **Then** it
   omits the counts line rather than showing an empty or zero value.
4. **Given** a report with no note, **When** the sheet opens, **Then** it omits the note line.
5. **Given** the sheet is open, **When** the picker taps **Close**, **Then** the sheet closes and
   the card is unchanged.

---

### User Story 3 - The picker can correct a report from the sheet (Priority: P2)

The problem goes away (a missing copy turns up, a clean copy replaces a damaged one, the right
variant is found), or the picker realises they reported the wrong problem. From the sheet they
either mark the report resolved, which records the product as picked, or change the report.

**Why this priority**: Both corrections already exist (feature 015: a later outcome replaces the
earlier one). Removing them from the dock must not remove them altogether; the sheet is where
they now live, one deliberate tap away from the everyday action of moving on.

**Independent Test**: From the sheet, tap **Resolved** and confirm the product is recorded as
picked and the card returns to its picked state. Separately, tap **Edit report**, submit a
different report, and confirm the chip and sheet show the new one.

**Acceptance Scenarios**:

1. **Given** a product with quantity 4 and a reported issue, **When** the picker opens the sheet
   and taps **Resolved**, **Then** the product is recorded as picked, the report is replaced, the
   sheet closes, and the card shows its picked state.
2. **Given** a reported product of any issue type and any quantity, **When** the picker opens the
   sheet, **Then** the correction reads **Resolved**.
3. **Given** a product with a reported issue, **When** the picker opens the sheet and taps **Change
   report**, **Then** the issue form opens for that product, filled in with the current report's
   type, counts and note.
4. **Given** the issue form opened from **Edit report**, **When** the picker submits it, **Then**
   the new report replaces the old one, and the chip and sheet show the new report.
5. **Given** the issue form opened from **Edit report**, **When** the picker cancels it, **Then**
   the existing report is unchanged.

---

### User Story 4 - The desktop list shows a report and lets the picker correct it (Priority: P2)

A picker working from a desktop sees a reported row. The row shows what was reported, and offers
**Resolved** and **Edit report** in place of **Picked** and **Report Issue**. **Edit report** opens
the form already filled in from the current report.

**Why this priority**: The desktop list has the phone's fault (a **Picked** button that silently
replaces the report) and one of its own: a report can only be changed by entering it again from a
blank form. It is P2 because picking happens mostly on phones (feature 016).

**Independent Test**: On a desktop, report an issue with counts and a note. Confirm the row shows
the issue panel with the type, "Required 4 · Found 3", the note, and the reporter and time, and offers
**Resolved** and **Edit report** but not **Picked** or **Report Issue**. Tap **Edit report** and
confirm the form opens filled in.

**Acceptance Scenarios**:

1. **Given** a desktop order with a row reported as Card Not Found, required 4, found 3, with a
   note, **When** the picker views it, **Then** the row shows an issue panel with the issue type, who
   reported it and when, "Required 4 · Found 3", and the note.
2. **Given** a row reported as Card Damaged with a note and no counts, **When** the picker views it,
   **Then** the panel shows the type, the reporter and time, and the note, and no counts line.
3. **Given** a reported row, **When** the picker holds the claim, **Then** the row offers
   **Resolved** and **Edit report**, and no **Picked** or **Report Issue** button.
4. **Given** a reported row, **When** the picker taps **Resolved**, **Then** the product is recorded
   as picked and the row returns to its picked state.
5. **Given** a reported row, **When** the picker taps **Edit report**, **Then** the issue form opens
   in the row, filled in from the current report. Submitting replaces the report; cancelling leaves
   it unchanged.
6. **Given** a reported row on an order the viewer does not hold, **When** they view it, **Then** the
   panel shows the report and offers neither **Resolved** nor **Edit report**.
7. **Given** a row with no outcome or a picked outcome, **When** the picker views it, **Then** it is
   unchanged by this feature.

---

### Edge Cases

- **Viewing without the claim.** A picker viewing a product they cannot record on (the order is
  not claimed, or someone else holds it) still sees the chip and can open the sheet to read the
  report. The sheet offers only **Close**, because neither correction can be recorded. The dock
  keeps showing what it shows today for an order they cannot record on (the Claim action, or the
  reason recording is unavailable).
- **A correction fails.** If **Resolved** or a changed report cannot be saved, the picker sees
  the same kind of error they see today when recording fails, and the existing report stays as it
  was. Nothing on the card claims the correction happened.
- **A correction is still being saved.** While a correction from the sheet is being saved, its
  controls cannot be tapped again, as the Picked button cannot today while it records.
- **Reported by someone else.** The sheet names whoever made the current report, which may not be
  the picker viewing it.
- **No reporter name.** If the reporter's name is unavailable, the sheet shows the time alone
  rather than an empty name.
- **A picked product.** A product recorded as picked is unaffected by this feature. It keeps
  today's picked state and its **Picked ✓** button.
- **A product with no outcome.** A product nobody has looked at is unaffected. It keeps today's
  Picked (or **Pulled all N**) button and **Report an issue**.
- **Moving.** Nothing in this feature blocks or interrupts moving between products (016 FR-019a).
  Opening the sheet is a tap on the chip, never something that happens on arrival.

## Requirements *(mandatory)*

### Functional Requirements

**The card**

- **FR-001**: On the phone's single-card view, a product with a reported issue MUST show a chip on
  the card naming the issue type.
- **FR-002**: The chip MUST name the issue type in every case, including when found and needed
  counts were recorded. The counts appear in the sheet, not on the chip.
- **FR-003**: The chip MUST be visually distinct as an issue, and MUST read as something that can
  be tapped.
- **FR-004**: The rest of the card MUST be unchanged by a reported issue. The product's identity,
  image and quantity emphasis stay exactly as they are for any other product.

**The dock**

- **FR-005**: When the current product has a reported issue and the picker can record outcomes on
  the order, the dock MUST offer **Next card ›** as its primary action, together with the existing
  ‹ › arrows, and nothing else.
- **FR-006**: When the current product has a reported issue, the dock MUST NOT offer the Picked
  button (including its "Pulled all N" form) or **Report an issue**. No control on the card screen
  may state a quantity pulled that contradicts the report.
- **FR-007**: **Next card ›** MUST behave exactly as the › arrow does: it moves to the next product,
  or to the final review from the last product, and records nothing (016 FR-011).
- **FR-008**: The dock for a product without a reported issue MUST be unchanged by this feature.

**The sheet**

- **FR-009**: Tapping the chip MUST open a sheet about the current report. The sheet MUST NOT open
  by any other means, in particular not on arriving at the product.
- **FR-010**: The sheet MUST show the issue type.
- **FR-011**: When both counts were recorded, the sheet MUST show them in the issue form's own
  words, "Required N · Found M". When either count is missing it MUST omit the counts rather than
  show a zero or blank value. Nothing about a report may be described as "pulled" or "short": the
  design MUST hold for every issue type, not only a missing card.
- **FR-012**: When a note was recorded, the sheet MUST show it; otherwise it MUST omit it.
- **FR-013**: The sheet MUST show who made the current report and when. If the reporter's name is
  unavailable it MUST show the time alone.
- **FR-014**: The sheet MUST offer **Close**, which dismisses it and changes nothing.

**Correcting a report**

- **FR-015**: When the picker can record outcomes on the order, the sheet MUST offer **Resolved**,
  which records the product as picked, replacing the report as any later outcome does (feature
  015). It reads **Resolved** for every issue type and quantity.
- **FR-016**: When the picker can record outcomes on the order, the sheet MUST offer **Change
  report**, which opens the issue form for that product, filled in with the current report's type,
  counts and note.
- **FR-017**: Submitting the form opened by **Edit report** MUST record a new report that
  replaces the current one. Cancelling it MUST leave the current report unchanged.
- **FR-018**: When the picker cannot record outcomes on the order, the sheet MUST offer only
  **Close**.
- **FR-019**: If a correction cannot be saved, the application MUST tell the picker and MUST leave
  the existing report and the card as they were.
- **FR-020**: While a correction is being saved, the sheet's correction controls MUST NOT accept
  another tap.

**Scope**

- **FR-021**: This feature MUST NOT change the final review screen, the ending screens or labels
  (feature 017), or what any report records.

**The desktop list**

- **FR-022**: On the desktop order view, a reported row MUST show an issue panel in place of the
  one-line issue summary. The panel shows the issue type, who reported it and when, and the counts
  and note under the same rules as the sheet (FR-011–FR-013).
- **FR-023**: When the viewer can record outcomes on the order, the panel MUST offer **Resolved**
  (as FR-015) and **Edit report**, which opens the issue form in the row, pre-filled from the
  current report (as FR-016, FR-017).
- **FR-024**: While a row is reported, it MUST NOT offer the Picked or Report Issue buttons.
- **FR-025**: When the viewer cannot record outcomes on the order, the panel MUST show the report
  and offer neither correction.
- **FR-026**: A desktop correction that fails, or is still being saved, MUST behave as on the phone
  (FR-019, FR-020).
- **FR-027**: Rows without a reported issue MUST be unchanged by this feature.

### Key Entities

- **Reported issue (existing)**: the current picking issue on a product line, as features 015 and
  016 already record it: issue type, optional needed and found counts, optional note, the employee
  who reported it and when. This feature displays it and offers corrections; it adds nothing to it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a product with a reported issue, no control on the phone's card screen or the
  desktop row states a quantity pulled, and no text describes the report as "pulled" or "short".
  Verified for every issue type, and for quantities of one and more than one.
- **SC-002**: A picker can see the recorded counts, note, reporter and time of a report from the
  card screen in one tap, without leaving the product.
- **SC-003**: A picker can correct a report, either by recording that every copy was found or by
  changing the report, in no more than two taps from the card, plus the form itself for a changed
  report.
- **SC-004**: Reaching the next product from a product with a reported issue takes one tap, the
  same as from any other product.
- **SC-005**: None of the final review, the endings or the labels behave differently for any order
  after this feature.
- **SC-006**: On the desktop, a picker can see everything a report holds, and resolve or edit it,
  without leaving the row and without re-entering a report from a blank form.

## Assumptions

- **"Edit report" opens the form pre-filled.** The form is the one the picker already uses to
  report, filled in with the current report so a correction is an edit rather than a re-entry
  (PRD §19.2: common reporting should not require unnecessary typing).
- **The picker stays on the card after reporting.** Reporting an issue does not move the picker to
  the next product. The card changes to its reported state, and moving on is the picker's choice,
  now the dock's primary action.
- **"Next card ›" keeps its label on the last product.** From the last product it opens the final
  review, as the › arrow already does. The label is not changed to name the review.
- **The chip uses the existing issue type names.** The chip and sheet use the same names the issue
  form and the desktop view already use (PRD §19).
- **Picked is the only resolved outcome.** "Resolved" records the product exactly as the Picked
  button does today. There is no partial found outcome. Recording fewer than all copies is a
  report, which **Edit report** covers.
- **The final mockups are the reference** for the desktop issue panel and the any-issue wording
  (https://claude.ai/artifact/XdfCTKJHRt4XPJMGDcM55p, final row). For the phone's layout,
  **direction C's layout is the reference.** The mockup shows the look: the chip below the quantity
  emphasis, and the sheet rising from the bottom of the screen. The exact styling follows the
  application's existing theme.

## Dependencies

- **Feature 015 (pick completion)**: a later outcome replaces an earlier one, which is what both
  corrections rely on. The record of past reports is kept as it is today.
- **Feature 016 (mobile picking)**: the phone's single-card view, its dock, and the rules that
  moving never records (FR-011) and is never blocked (FR-019a).
- **Feature 017 (pick completion and hand-off)**: a product with a reported issue, or with no
  outcome, is unresolved, and the ending and label already reflect that. This feature changes
  neither.
- **PRD §19.1–§19.3**: what a report holds (structured counts, an optional note, the reporter).
