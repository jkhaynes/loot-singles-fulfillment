# Feature Specification: Pick Completion and Hand-off

**Feature Branch**: `017-pick-completion-handoff`

**Created**: 2026-09-21

**Status**: Draft

**Input**: User description: "Pick completion and hand-off. Every pick ends on one of two screens: Pick Complete (every line confirmed, nothing unresolved) stating the physical card count, or Pick Ended — Needs a Manager (the picker pulled what they could and something is unresolved) stating cards pulled, cards set aside with the order, and which product is unresolved. Each screen prints a label to standard address stock (about 1⅛ × 3½ inches) from the picker's device through the browser — printing is client-side because the hosted API cannot reach a printer on the shop LAN. Printing is an explicit tap, never an automatic dialog; once printed, 'Next order' becomes the primary action and claims the next order as Pick Next does. The label carries a short order code (the application's own order number), the full TCGplayer order identifier, the physical card count, and who picked it and when — and no customer data. It carries a QR code encoding a link to the order, and a Code 128 barcode encoding the bare TCGplayer order identifier, whose printed value is the human-readable full identifier. A hold label is visually distinct in monochrome via an inverted band, not a colour, and states the held-aside count. The label includes a 'ships short' marker that nothing sets in this feature. Labels can be reprinted from the order and from the packing desk. The importer additionally records which page(s) of the batch packing slip each order occupies and stores that order's pages as its own PDF against the order; the batch document is not retained. A new desktop packing desk resolves a scanned QR link or a typed order number to an order, shows its card count, picker and time, prints that order's stored packing slip, and marks the order Packed; it also lists orders awaiting packing. Scanning a held order refuses and names the unresolved product. Any authenticated employee may open a slip and mark an order Packed; every slip retrieval is logged with the employee and the time, and no picking surface links to a slip. A new terminal Packed status is added — it is the first status not derived from line outcomes. The dashboard's Picked tile becomes Awaiting packing. Packed is terminal with no undo, and there is no history view for Packed orders. Orders imported before this feature have no stored slip and the packing desk says so plainly rather than failing. Implements PRD v0.5 §20.1 (Packed), §22, §22.1, and §27. Builds on features 015 and 016. Excludes the manager console, Awaiting Customer Decision, Cancelled, substitutes and write-offs, which belong to the issue-resolution feature."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A finished pick produces a labelled sleeve (Priority: P1)

A picker works through an order and reaches the end. Today the order's status changes and nothing
else happens: the picker sets a sleeve of cards aside, and the moment it leaves their hand nobody
can tell which order it belongs to.

Instead, finishing a pick ends on a screen that states how many physical cards should be in the
sleeve and offers to print a label. The picker taps print, sticks the label on the sleeve, and the
bundle is now identifiable by anyone who picks it up.

When something could not be picked, the pick ends on a different screen: it names what is
unresolved and what has been set aside with the order, prints a visibly different hold label, and
sends the bundle to the review area rather than to the ready-to-pack bin.

**Why this priority**: This is the gap the whole feature exists to close, and it delivers value on
its own. Even with nothing else built, a labelled sleeve can be matched to its order by a human
reading the label — which is strictly better than today.

**Independent Test**: Complete a pick end to end and confirm the ending screen appears with the
correct card count and a label that prints. Then complete a pick with an unresolved line and
confirm the hold ending appears with a visibly different label. Neither requires the packing desk
to exist.

**Acceptance Scenarios**:

1. **Given** a picker holds an order where every product line is confirmed picked and no line has
   an unresolved issue, **When** they finish the pick, **Then** the application presents a
   completion outcome stating the total number of physical cards that should be in the sleeve.
2. **Given** a picker holds an order where at least one product line has an unresolved issue,
   **When** they finish the pick, **Then** the application presents a needs-a-manager outcome
   stating the number of cards pulled, the number of cards set aside with the order, and which
   products are unresolved.
3. **Given** a picker is on either ending outcome, **When** they have not yet asked to print,
   **Then** no print dialog has been opened on their behalf.
4. **Given** a picker is on either ending outcome, **When** they choose to print, **Then** a label
   is produced for that order carrying the order's short code, the full TCGplayer order identifier,
   the physical card count, and who picked it and when.
5. **Given** a picker has printed a label, **When** they look at the ending outcome, **Then**
   continuing to the next order is presented as the primary action.
6. **Given** a picker chooses to continue to the next order, **When** an order is available,
   **Then** it is claimed for them and opened, the same way the existing Pick Next action behaves.
7. **Given** an order has an unresolved picking issue, **When** its label is produced, **Then** the
   label is distinguishable from a ready-to-pack label without relying on colour, and states how
   many cards were set aside with the order.
8. **Given** any order, **When** its label is produced, **Then** the label contains no customer
   name, address, or other customer information.

---

### User Story 2 - A packer finds the order and ships it (Priority: P1)

A labelled sleeve arrives at the packing bench. The packer scans its label, and the application
identifies the order, prints that order's packing slip, and lets them record that the order has
been packed.

Under thirty dollars the slip is the entire job — envelope, stamp, slip. Above it, the packer reads
the slip to fill in a third-party shipping-label printer and then records the resulting tracking
number against the order in TCGplayer, which the label's barcode gets them to without typing.

If the sleeve carries a hold label, the desk refuses to pack it and says which product is still
unresolved.

**Why this priority**: Without this, `Packed` is a state nothing can reach, and the label's code
resolves to a page with no packing action. The two stories together are the feature; either alone
is half a hand-off.

**Independent Test**: Take an order that has been picked, scan or type its code at the packing
desk, confirm its details and packing slip are produced, mark it packed, and confirm it no longer
appears as awaiting packing.

**Acceptance Scenarios**:

1. **Given** an order that is awaiting packing, **When** an employee enters or scans its code at
   the packing desk, **Then** the application identifies the order and shows its physical card
   count, who picked it, and when.
2. **Given** the packing desk has identified an order imported after this feature, **When** the
   employee asks for its packing slip, **Then** that order's packing slip is produced for printing
   and contains only that one order's information.
3. **Given** the packing desk has identified an order, **When** the employee records it as packed,
   **Then** the order reaches the terminal packed state and stops appearing as awaiting packing.
4. **Given** an order with an unresolved picking issue, **When** its code is entered at the packing
   desk, **Then** the application refuses to pack it and names the unresolved product or products.
5. **Given** an order that has already been packed, **When** its code is entered again, **Then** the
   application says it is already packed and does not record it a second time.
6. **Given** a code that matches no order, **When** it is entered at the packing desk, **Then** the
   application says so plainly and the desk remains ready for the next scan.
7. **Given** two employees attempt to record the same order as packed at the same time, **When**
   both submit, **Then** the order is recorded as packed exactly once and the second is told it is
   already packed.
8. **Given** any employee retrieves an order's packing slip, **When** the retrieval happens,
   **Then** the application records which employee retrieved it and when.

---

### User Story 3 - A lost or ruined label is replaced (Priority: P2)

Labels jam, misprint, fall off a sleeve, or get stuck on the wrong one. Someone needs to produce
another without re-picking the order.

**Why this priority**: The feature makes the physical workflow depend on a printed sticker. A
workflow that depends on a sticker needs a way to replace the sticker, but the first two stories
deliver value before this exists.

**Independent Test**: Open an already-picked order, produce its label again, and confirm it is
identical to the original — same code, same counts, same picker and time.

**Acceptance Scenarios**:

1. **Given** an order that has been picked, **When** an employee asks to print its label from the
   order, **Then** a label identical to the original is produced.
2. **Given** an order that has been picked, **When** an employee asks to print its label from the
   packing desk, **Then** a label identical to the original is produced.
3. **Given** an order whose label is reprinted, **When** the reprint is produced, **Then** it shows
   the original picker and the original pick time, not the person or moment of the reprint.

---

### User Story 4 - The queue shows what is still on the shelf (Priority: P3)

Today the dashboard counts orders that have been picked, and that number only ever grows. What
anyone actually wants to know is how many picked sleeves are still sitting on the shelf waiting to
be packed.

**Why this priority**: A correctness improvement to an existing number rather than new capability.
Valuable, and cheap once packed orders exist, but the feature works without it.

**Independent Test**: Pick an order and confirm the awaiting-packing count rises; pack it and
confirm the count falls.

**Acceptance Scenarios**:

1. **Given** orders that have been picked but not packed, **When** an employee views the dashboard,
   **Then** the count shown is of orders awaiting packing.
2. **Given** an order is recorded as packed, **When** the dashboard is next viewed, **Then** that
   order is no longer counted as awaiting packing.
3. **Given** an employee is at the packing desk, **When** they view it, **Then** they can see the
   orders currently awaiting packing without scanning anything.

---

### Edge Cases

- **An order imported before this feature has no stored packing slip.** The packing desk must say
  so plainly and remain usable for everything else — identifying the order, showing its counts, and
  recording it as packed. It must not fail or appear broken.
- **A packing slip cannot be extracted for one order during an import.** The order itself still
  imports, because the order data parsed successfully and is authoritative; the slip is simply
  absent and that order behaves like a pre-existing one. A slip problem must never cause an
  otherwise valid order to be rejected, and must never cause a whole batch to fail.
- **An order spans more than one page of the batch packing slip.** All of its pages belong to its
  stored slip, and none of another order's pages do.
- **A picker finishes an order they do not hold.** They are reviewing, not finishing. No ending
  outcome is recorded and no claim is released.
- **A picker finishes an order with nothing picked at all.** This is the needs-a-manager ending with
  a pulled count of zero; it is not a completion.
- **Printing is unavailable, cancelled, or fails silently.** The picker can still continue to the
  next order, and can print the label later from the order. The application cannot detect whether
  paper actually came out, so it must never treat "printed" as a recorded fact about the order.
- **A code is scanned into the packing desk as a full link rather than a bare number.** Both
  resolve to the same order.
- **A whitespace-padded or differently-cased code is entered.** It resolves to the same order.
- **An order that is claimed and in progress is scanned at the packing desk.** It is not awaiting
  packing; the desk says what state it is in rather than packing it.
- **A picking issue on an order is resolved after a hold label was printed.** The order becomes
  awaiting packing and can be packed, even though the sleeve carries a hold label. The desk reflects
  the order's current state, not the sticker's.

## Requirements *(mandatory)*

### Functional Requirements

#### Ending a pick

- **FR-001**: The application MUST present an ending outcome whenever a picker finishes an order
  they hold, and MUST distinguish a completion from a needs-a-manager ending.
- **FR-002**: A completion ending MUST state the total number of physical cards that should be in
  the sleeve.
- **FR-003**: A needs-a-manager ending MUST state the number of physical cards pulled, the number of
  cards set aside with the order, and which product or products are unresolved.
- **FR-004**: The ending outcome MUST NOT state a count of product lines. The physical card count is
  the only count presented, because it is the only one that can be checked against the sleeve.
- **FR-005**: The application MUST NOT initiate printing without an explicit request from the
  employee.
- **FR-006**: After a label has been requested for an order, the ending outcome MUST present
  continuing to the next order as its primary action.
- **FR-007**: Continuing to the next order MUST claim and open the next available order, behaving
  as the existing Pick Next action does, including when no order is available.
- **FR-008**: Finishing a pick MUST release the picker's claim on the order, as it does today.

#### The label

- **FR-009**: The application MUST produce a printable label for any order whose picking has ended
  — whether it completed or was held for a manager — sized for standard address label stock of
  approximately 1⅛ × 3½ inches. An order on which no pick outcome has yet been recorded has no
  label.
- **FR-010**: The label MUST carry, in human-readable form: a short order code, the full TCGplayer
  order identifier, the physical card count, and who picked the order and when (FR-041).
- **FR-011**: The label MUST carry a QR code that resolves to that order within the application.
- **FR-012**: The label MUST carry a Code 128 barcode encoding the bare TCGplayer order identifier,
  and the barcode's printed value MUST serve as the human-readable full identifier required by
  FR-010, so that what a person reads and what a scanner produces cannot differ.
- **FR-013**: A label for an order with an unresolved picking issue MUST be distinguishable from a
  ready-to-pack label in monochrome, by an inverted band rather than by colour, and MUST state the
  number of cards set aside with the order.
- **FR-014**: The label MUST be capable of marking an order as shipping short. Nothing in this
  feature sets that state; the marker exists so the label's design does not have to be reopened when
  short shipments become reachable.
- **FR-015**: The label MUST NOT carry customer name, address, or any other customer information.
- **FR-016**: Employees MUST be able to produce the label again for **any order whose picking has
  ended** — including one held for a manager, whose label is the most likely to be needed twice —
  both from the order and from the packing desk. A reproduced label MUST be identical to the
  original, including the original contributors and pick time.
- **FR-017**: The application MUST derive every count printed on a label from the order itself, so
  that a label cannot disagree with the order it identifies.

#### Storing the packing slip

- **FR-018**: When importing a batch packing slip, the application MUST record which page or pages
  of that document belong to each order.
- **FR-019**: The application MUST store, for each imported order, that order's own packing slip
  containing exactly that order's pages and no other order's.
- **FR-020**: The application MUST NOT retain the batch packing slip document.
- **FR-021**: Failure to store a packing slip for an order MUST NOT prevent that order from being
  imported, and MUST NOT cause any other order or the batch to be rejected. Such a failure MUST be
  recorded.
- **FR-022**: Orders imported before this feature existed have no stored packing slip. Every surface
  that offers a packing slip MUST state plainly that one is unavailable rather than failing.

#### The packing desk

- **FR-023**: The application MUST provide a packing surface designed for desktop use that resolves
  an entered or scanned code to an order.
- **FR-024**: The packing surface MUST accept both a link produced by scanning a label's QR code and
  a directly entered order code, resolving both to the same order.
- **FR-025**: On resolving an order, the packing surface MUST show its physical card count, every
  employee who picked it (FR-041), and when picking finished.
- **FR-026**: The packing surface MUST produce that order's stored packing slip for printing.
- **FR-027**: The packing surface MUST allow an order awaiting packing to be recorded as packed.
- **FR-028**: The packing surface MUST refuse to pack an order with an unresolved picking issue, and
  MUST name the unresolved product or products when refusing.
- **FR-029**: The packing surface MUST explain, rather than silently fail, when a code matches no
  order, when an order is already packed, or when an order is in a state that cannot be packed.
- **FR-030**: The packing surface MUST list the orders currently awaiting packing.

#### Order state

- **FR-031**: The application MUST support a packed state that is terminal. An order that is packed
  MUST NOT be returned to any earlier state by any action in this feature.
- **FR-032**: The application MUST record which employee packed an order and when.
- **FR-033**: An order MUST NOT be recorded as packed more than once, including when two employees
  attempt it simultaneously.
- **FR-034**: An order with an unresolved picking issue MUST NOT be able to reach the packed state.
- **FR-035**: Every other order status MUST continue to be derived from the order's current line
  outcomes and claim state exactly as it is today. The packed state is the single exception and MUST
  be an explicitly recorded event rather than a derived one.
- **FR-036**: The dashboard MUST count orders awaiting packing rather than orders that have been
  picked.

#### Access and traceability

- **FR-037**: Any authenticated employee MUST be able to retrieve an order's packing slip and record
  an order as packed. No role restriction applies to either.
- **FR-038**: The application MUST record every packing slip retrieval with the employee who
  retrieved it and the time.
- **FR-039**: No picking surface may link to, display, or otherwise expose a packing slip or its
  contents.
- **FR-040**: The application MUST NOT extract customer information out of a stored packing slip
  into any other part of its data.

#### Who picked it

- **FR-041**: "Who picked an order" MUST be **every** employee who recorded a pick outcome on one
  of its lines, not a single employee. An order released and re-claimed may be picked by more than
  one person, and naming only one of them would be inaccurate rather than merely incomplete.
- **FR-042**: The pick time MUST be when the most recent pick outcome was recorded — the moment
  picking finished.
- **FR-043**: Where the label's fixed size cannot hold every name, it MUST show the first two
  contributors in the order they first contributed, followed by a count of the remainder. The
  packing desk, which has no such constraint, MUST show all of them.
- **FR-044**: Contributors MUST be derived from recorded pick outcomes only, so that the list
  describes picking work and nothing else.

### Key Entities

- **Order**: Gains a packed outcome — the employee who packed it and when — and an associated
  packing slip. Continues to carry no customer fields of its own.
- **Stored packing slip**: The pages of the imported batch document belonging to exactly one order,
  kept so the packing workflow can print it. Contains that customer's shipping information and is
  the only place in the application that does.
- **Packing slip access record**: Which employee retrieved which order's slip, and when.
- **Label**: Not stored. Produced on demand from the order's own data, so that it always agrees with
  the order and a reprint always matches the original.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every sleeve leaving the picking area carries a label identifying its order. No picked
  order reaches the packing bench unidentifiable.
- **SC-002**: A packer goes from scanning a sleeve's label to a printed packing slip without typing
  an order identifier at any point.
- **SC-003**: A packer reaches the order in TCGplayer, for the tracking-number step, without typing
  an order identifier at any point.
- **SC-004**: No order with an unresolved picking issue is ever recorded as packed.
- **SC-005**: Every order imported after this feature has a retrievable packing slip containing
  exactly one customer's information, and no stored document contains more than one order's slip.
- **SC-006**: A picker can state, from the ending screen alone and without navigating, how many
  physical cards should be in the sleeve they are holding.
- **SC-007**: A lost or ruined label can be reproduced, matching the original exactly, without
  re-picking or re-importing the order.
- **SC-008**: No printed label contains any customer information.
- **SC-009**: The number of orders shown as awaiting packing equals the number of picked sleeves
  physically waiting to be packed, and falls when an order is packed.
- **SC-010**: Every packing slip retrieval is attributable to an employee and a time after the fact.

## Assumptions

These are reasonable defaults chosen where the feature description did not decide something. Each
is a decision that can be revisited without redesigning the feature.

- **The label's QR code resolves to the order's packing view.** A phone camera scanning a sleeve
  opens the place where that order gets packed, rather than a general order page the packer must
  navigate from.
- **Contributors describe picking, and the issue-resolution feature must keep it that way.**
  FR-044 derives the list from recorded pick outcomes, which today only pickers produce — a manager
  has no way to record one. **If the issue-resolution feature lets a manager record a pick outcome
  when capturing a substitute, that manager would appear on the label as a picker.** Preventing
  that is a constraint on the later feature, stated here so it is inherited rather than
  rediscovered.
- **The short order code is the application's own order number.** It is short enough to read aloud
  and to type, and is already how orders are referred to in practice. It is not meaningful outside
  the application.
- **Printing happens from the employee's own device through the browser.** The hosted application
  cannot reach a printer on the shop network, so printing is necessarily initiated client-side. The
  application cannot confirm that paper was produced and therefore never records "printed" as a fact
  about an order.
- **"Finishing" an order keeps its existing meaning from feature 016**, including that an order may
  be finished while lines remain unresolved. This feature adds what happens next; it does not change
  what finishing means or when it is allowed.
- **An order's status continues to be recomputed from its lines on every write**, as established by
  feature 015. Packed is layered on top of that derivation rather than replacing it.
- **A stored packing slip is retained indefinitely.** Deleting a slip when its order is packed was
  considered and deliberately deferred, because it makes a mistimed action unrecoverable. PRD §41
  open question 59 records that a retention rule is owed.
- **No history view of packed orders is provided.** A packed order remains reachable by its order
  code and is not otherwise listed.
- **Packed orders cannot be un-packed.** PRD §20.1 defines packed as terminal, and a mis-pack is
  corrected in TCGplayer rather than here.
- **Employees at the packing bench sign in with the same accounts they use elsewhere.** No new role
  is introduced; the existing role split does not describe packers.

## Dependencies

- **Features 015 and 016 as built.** This feature extends the pick-completion lifecycle from 015 and
  the finishing flow from 016; it does not modify how lines are confirmed or how issues are
  reported.
- **A label printer on the shop network, reachable from the employee's device.** Out of the
  application's control.
- **A scanner at the packing bench capable of reading at least Code 128.** A 1D laser scanner is
  sufficient for the TCGplayer identifier path. Reading the QR code requires a 2D imager, which PRD
  §22.1 now treats as an upgrade rather than a prerequisite.

## Out of Scope

Deferred to the issue-resolution feature, and explicitly not built here:

- The manager console, and any customer-contact workflow
- The Awaiting Customer Decision and Cancelled states
- Substitutes, write-offs, and shipping an order short — including setting the label's short marker
- Resolving a picking issue by any means other than those that already exist

Also out of scope:

- Packing **verification** — whether a sleeve's contents are correct. PRD §22.1 draws this boundary
  explicitly: this feature covers association, not verification, and verification remains a V2 goal.
- Any retention or deletion rule for stored packing slips.

## Risks

- **Browser printing has never been validated against the shop's actual label printer**, and the
  Product Owner decided on 2026-09-21 to proceed without waiting for it. Whether a browser-produced
  label comes out at the correct physical size on 1⅛ × 3½ inch stock depends on print scaling,
  margins and driver behaviour that cannot be confirmed from here.

  The risk is carried, not removed. It is made affordable by keeping every physical dimension as a
  named token in one stylesheet, so wrong numbers are corrected in one place. It is **not** removed
  for the case where a browser cannot produce a correctly sized label at all, which would change
  the label's form rather than its measurements. The feature is not complete until a physical label
  has been printed and measured.
- **This feature introduces the first customer personal information the application has ever
  stored.** PRD §27 was amended to permit it, bounded by one-order-per-file, no picking surface
  reaching a slip, recorded access, and a deferred retention rule. Those bounds are the whole of the
  mitigation; weakening any of them re-opens the amendment rather than adjusting an implementation
  detail.
- **None of this has been validated with the people who pull and pack Loot orders.** The workflow
  reflects what the owners described. PRD §42 Priority 4 still asks for observation of real picking
  and packing, and it has not happened.
