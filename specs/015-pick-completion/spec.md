# Feature Specification: Pick Completion

**Feature Branch**: `015-pick-completion`

**Created**: 2026-08-27

**Status**: Draft

**Input**: User description: "Add the core picking-completion workflow: at each product line in an order, a picker can confirm it was successfully picked, or report a picking issue instead of falsely confirming a pick (issue capturing type, relevant quantity information, an optional note, the reporting employee, and a timestamp, per PRD Section 19.3 — the exact issue-type taxonomy and any downstream issue-resolution process remain open PRD discovery questions and are out of scope here). An order automatically transitions through its lifecycle based on line-level outcomes: Ready -> In Progress (already exists, feature 013) -> Picked once every required product line is successfully confirmed with no unresolved issues, or -> Needs Attention if any line has an unresolved reported issue. An order with an unresolved issue must never be represented as successfully picked. This closes the loop the Dashboard already has placeholder tiles for (In Progress, Needs Attention, Picked all currently show \"Not yet available\")."

## Clarifications

### Session 2026-08-27

- Q: When a picker re-claims a previously released Needs Attention order, does it immediately
  display as In Progress again, or does it keep showing Needs Attention until the actual problem
  line is fixed? → A: Needs Attention persists through re-claim. More generally: an order's status
  is never an independently-tracked flag — it is always a live aggregate recomputed from its
  product lines' *current* recorded outcomes. A 10-line order with 2 lines that can't be found is
  Needs Attention because those 2 specific lines have an unresolved issue, not because "the order"
  was separately marked that way. Marking one of those 2 as picked doesn't "resolve the order" as
  its own action — it simply changes that one line, and the order's status is re-derived from
  scratch afterward: still Needs Attention if the other line remains unresolved, or Picked once
  neither does (and every other line is already confirmed).

### Session 2026-09-19 (during `/speckit-implement`)

- Q: When the last line is confirmed and an order becomes Picked, what happens to the picker's
  claim? → A: The claim is kept. The picker may still revise lines while holding it, and releases
  it through the normal feature-013 release action. Because status is always derived from the
  lines (FR-006), releasing a Picked order leaves it Picked — it never reverts to Ready. The one
  derivation, applied at every write: any line has an unresolved issue → Needs Attention; else
  every line confirmed picked → Picked; else claimed → In Progress; else Ready.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Picker Confirms a Product Line as Successfully Picked (Priority: P1)

A picker working a claimed order goes through its product lines one at a time, confirming each
one they successfully pulled. Once every line is confirmed, the order is done.

**Why this priority**: This is the fundamental action the entire product exists to support.
Nothing else in the application — authentication, claiming, viewing order details — produces a
completed pick without this.

**Independent Test**: As a picker with a claimed order, confirm each product line as picked; once
every line is confirmed with no reported issues, the order's status becomes Picked automatically.

**Acceptance Scenarios**:

1. **Given** a claimed order with unconfirmed product lines, **When** the picker confirms a line
   as picked, **Then** that line is recorded as picked, attributed to that picker with a
   timestamp.
2. **Given** every line in a claimed order has been confirmed as picked with no unresolved
   reported issues, **When** the picker confirms the final line, **Then** the order's status
   becomes Picked automatically, with no separate manual "complete order" step.
3. **Given** an order that is not yet fully confirmed, **When** the picker views the order,
   **Then** they can see their current position (for example, "3 of 5 lines confirmed").
4. **Given** a line the picker already confirmed as picked, **When** they need to correct a
   mistake before finishing the order, **Then** they can revise that line's recorded outcome.
5. **Given** a previously Needs Attention order the picker released earlier, **When** they later
   re-claim that same order (for example, once they've located a card they couldn't find before),
   **Then** they can revise the previously problematic line's outcome, and if no unresolved issues
   remain and every line is confirmed, the order becomes Picked.

---

### User Story 2 - Picker Reports a Picking Issue Instead of a False Confirmation (Priority: P1)

At any product line, a picker who cannot actually fulfill it reports a problem rather than
confirming a pick that didn't happen.

**Why this priority**: Equally foundational as confirming a successful pick — a core product and
constitution principle is that the system must never force a false happy path, and an order with
an unresolved issue must never be represented as successfully picked.

**Independent Test**: As a picker who cannot fulfill a line, report an issue with a type and
optional note; confirm the order is never shown as Picked while that issue is unresolved.

**Acceptance Scenarios**:

1. **Given** a line the picker cannot successfully pick, **When** they report an issue, selecting
   a type and, where relevant, quantity information, **Then** the issue is recorded with the
   reporting employee, timestamp, and details, and that line is not treated as picked.
2. **Given** any line in a claimed order has an unresolved reported issue, **When** the order's
   status is evaluated, **Then** it is Needs Attention, not Picked, regardless of the state of the
   order's other lines.
3. **Given** an order with an unresolved issue, **When** anyone views that order's status anywhere
   in the application, **Then** it is never shown as Picked.
4. **Given** a 10-line order where 2 lines can't be found and the other 8 are confirmed picked,
   **When** the picker later finds one of the 2 missing cards and confirms that line as picked,
   **Then** the order is still Needs Attention, because 1 line's issue remains unresolved — and
   **When** the picker also confirms the last remaining line, **Then** the order becomes Picked.

---

### User Story 3 - Everyone Can See Real Picking Progress Across All Orders (Priority: P2)

The Dashboard's In Progress, Needs Attention, and Picked tiles — currently placeholders — show
accurate live counts, and a Needs Attention order's problem is visible without opening it.

**Why this priority**: Depends on User Story 1 and User Story 2 producing real data; without them
this would just be wiring stub tiles to nothing. Valuable, but secondary to the core picking loop
itself existing.

**Independent Test**: As any employee, view the Dashboard; confirm accurate counts for each order
state, and that a Needs Attention order surfaces which line has an unresolved issue.

**Acceptance Scenarios**:

1. **Given** orders in various states, **When** any employee views the Dashboard, **Then** In
   Progress, Needs Attention, and Picked tiles show accurate live counts.
2. **Given** an order in Needs Attention, **When** an employee views the Dashboard or order list,
   **Then** they can identify which product line(s) have an unresolved issue without opening the
   full order detail screen.

---

### Edge Cases

- What happens if the order has only one product line? Confirming it or reporting an issue on it
  immediately determines the order's final status (Picked or Needs Attention).
- What happens if a picker reports an issue on a line, then later reports a different issue on the
  same line? The most recent report is what determines that line's current unresolved state;
  earlier reports for that line remain part of its history, not discarded (traceability).
- What happens to a Manager/Admin performing any action reserved for the assigned picker? It is
  rejected — recording a line outcome is restricted to whoever currently holds the order's
  exclusive claim (feature 013), with no exemption.
- What happens if a picker releases a Needs Attention order and a *different* employee claims it
  next? Standard exclusive-claim rules (feature 013) apply unchanged — whoever holds the claim,
  original reporter or not, can revise its line outcomes, same as any other claimed order.
- What happens to a Needs Attention order nobody ever re-claims? It remains Needs Attention and
  unclaimed indefinitely — this feature does not add automatic expiry, escalation, or reminders.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: At each product line of a claimed order, the assigned picker MUST be able to record
  that line as successfully picked, attributed to that picker with a timestamp.
- **FR-002**: At each product line of a claimed order, the assigned picker MUST be able to report
  a picking issue instead of confirming a pick, capturing at minimum: issue type, relevant
  quantity information where applicable, an optional note, the reporting employee, and a timestamp
  (PRD §19.1/19.3).
- **FR-003**: The system MUST NOT require a picker to falsely confirm a line as picked when they
  cannot actually fulfill it.
- **FR-004**: The system MUST allow whichever employee currently holds an order's exclusive claim
  to revise a previously recorded line outcome (a confirmed pick or a reported issue) at any point
  while they hold that claim — including a Needs Attention order they have just re-claimed after
  releasing it earlier, whether they are the employee who originally reported the issue or not.
- **FR-005**: The system MUST make a Needs Attention order reachable and claimable through the
  same order-browsing flow (Choose Order) as any other unclaimed order, so its issue can later be
  addressed by whoever claims it next. Automatic "Pick Next Order" assignment MUST continue to
  prioritize Ready orders for new work rather than resuming a previously-flagged one.
- **FR-006**: An order's status MUST always be derived fresh from its product lines' current
  recorded outcomes — never stored or updated as an independent flag that could drift out of sync
  with what's actually true of its lines. Every time any line's outcome is recorded or revised, the
  order's status MUST be re-evaluated immediately from scratch against FR-007/FR-008.
- **FR-007**: An order's status MUST be Picked the moment every one of its product lines has been
  confirmed as successfully picked and none currently has an unresolved reported issue.
- **FR-008**: An order's status MUST be Needs Attention whenever at least one of its product lines
  currently has an unresolved reported issue, regardless of how many other lines are confirmed —
  for example, a 10-line order with 2 lines that can't be found is Needs Attention because of those
  2 specific lines, not as a separate order-level designation; fixing one of them (revising it to a
  successful pick) doesn't "resolve the order" as its own step, it simply changes that line, after
  which FR-006 re-evaluates the order's status from the remaining line outcomes.
- **FR-009**: The system MUST NEVER represent an order as Picked while any of its product lines
  has an unresolved reported issue — restated explicitly, as this is the one outcome the system
  must never get wrong, even though FR-007/FR-008 already entail it.
- **FR-010**: The system MUST restrict recording a line outcome (confirming a pick or reporting an
  issue) to the employee who currently holds the order's exclusive claim; no other employee,
  including a Manager/Admin, may record a line outcome for an order they have not claimed.
- **FR-011**: The system MUST retain, for every reported issue, the order, the product line, the
  issue type, relevant quantity information, the optional note, the reporting employee, and the
  timestamp (PRD §19.3), even after that line's outcome is later revised.
- **FR-012**: The system MUST show a picker their current position within an order's product
  lines (for example, how many of the total lines have been confirmed).
- **FR-013**: The system MUST display accurate, live counts of orders In Progress, Needs
  Attention, and Picked, replacing the current placeholder values.
- **FR-014**: The system MUST allow any employee to identify, without opening full order detail,
  which product line(s) in a Needs Attention order have an unresolved issue.
- **FR-015**: The system MUST enforce every restriction and status transition in this feature
  server-side; frontend-only checks MUST NOT be the sole enforcement point.

### Key Entities

- **Order Line Pick Outcome**: The current recorded result of one product line — successfully
  picked, or has an unresolved reported issue — including who recorded it and when. A new outcome
  for a line supersedes its prior one but does not erase the historical record (FR-011).
- **Picking Issue**: The structured record created each time a picker reports an issue at a
  product line — type, relevant quantity information, an optional note, the reporting employee,
  and a timestamp.
- **Order** (existing, feature 001): Gains two new status values, Picked and Needs Attention.
  Status is always a live aggregate derived from the order's product lines' current outcomes
  (FR-006) — never an independently-tracked flag — so it is always consistent with what's actually
  true of its lines; no other changes to the entity's existing purpose.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A picker can record every product line in a typical order as picked without leaving
  the order-detail screen or needing a separate "complete order" step.
- **SC-002**: 100% of orders with at least one unresolved reported issue are shown as Needs
  Attention, never as Picked, verified across a representative sample.
- **SC-003**: 100% of orders where every line is confirmed picked with zero unresolved issues
  automatically become Picked with no manual completion action.
- **SC-004**: For every reported picking issue, an operator can determine which employee reported
  it, when, and what the issue was, without inspecting application code or database internals
  directly.
- **SC-005**: The Dashboard's In Progress, Needs Attention, and Picked counts always match the
  actual number of orders in each state, replacing today's permanently placeholder values.
- **SC-006**: An employee who does not currently hold an order's claim is prevented from recording
  a line outcome on it 100% of the time.
- **SC-007**: A Needs Attention order, released and later re-claimed once its problem is resolved,
  reaches Picked without any special-case action beyond the normal claim/release flow already used
  for every other order.
- **SC-008**: A picker can record a line outcome — confirm it picked, or report an issue — in a
  small, consistent number of interactions (aiming for no more than a couple of taps or clicks per
  line) on both a mobile phone and a desktop browser, using the same controls and navigation
  pattern on each, without device-specific workarounds or extra scrolling beyond what's already
  needed to view the order.

## Assumptions

- The exact issue-type taxonomy is a working v1 set drawn directly from the categories the PRD
  already lists as potential (§19: card not found, insufficient quantity, wrong card/variant/
  condition in storage, damaged, inventory discrepancy, information appears incorrect, other) —
  the PRD itself states "the final issue taxonomy remains to be validated," so this set is this
  feature's starting point, not a final Product Owner decision.
- This feature's only resolution mechanism is the existing claim/release cycle (feature 013)
  applied to Needs Attention orders exactly the way it already applies to Ready ones: whoever
  claims it can revise its line outcomes, including fixing a previously reported issue. Anything
  beyond that — restocking, contacting the customer, a dedicated issue queue or dashboard, manager-
  specific reopening tools — is explicitly out of scope. The PRD (§41-42) lists "issue resolution
  workflow" as its own unresolved discovery priority, and §20 lists "Resolved issues" as an
  additional state/transition that "remains to be designed"; this feature deliberately limits
  itself to reusing an already-approved mechanism rather than designing a new one.
- Reporting an issue on a line is a binary line-level outcome (the line currently has an
  unresolved issue, or it doesn't) rather than a partial-success/partial-issue split within a
  single line. Quantity information captured on an issue (for example, "required 4, found 2") is
  structured detail about that issue, not a separate partial-pick state.
- A Needs Attention order is reachable only through the existing Choose Order (browse-and-select)
  flow, not through Pick Next Order's automatic FIFO assignment — that flow exists to hand a
  picker fresh, unstarted work, not to redirect them into someone else's flagged problem.
- This feature adds its "confirm picked" / "report issue" controls onto the existing responsive
  order-detail screen (features 007/008), not a new or separate UI — it inherits that screen's
  established mobile/desktop layout and the app's existing shared design system (constitution
  Principle VIII: one responsive product, not separate codebases per device) rather than
  introducing new device-specific patterns.
