# Quickstart: Validating Pick Completion and Hand-off

**Feature**: `017-pick-completion-handoff` | **Date**: 2026-09-21

How to prove this feature works end to end. Scenario 0 comes first and is not optional — it gates
whether the rest of the label design is worth building.

## Prerequisites

- The application running locally against a database that has had this feature's migration applied.
- At least one order imported **after** this feature exists, so it has a stored packing slip.
- For scenario 0 only: the shop's small label printer, loaded with standard address stock
  (~1⅛ × 3½ inches), reachable from the device running the browser.

```powershell
dotnet build backend/LootSingles.sln
dotnet test  backend/LootSingles.sln
npm --prefix frontend run build
npm --prefix frontend test
npm --prefix frontend run test:e2e
```

---

## Scenario 0 — The label prints at the right physical size *(do this first)*

**This is the feature's largest risk and it is unresolved** (research.md §9). Everything physical
rests on a browser being able to produce a correctly sized label, and that depends on print
scaling, driver defaults and margin handling that cannot be determined from a specification.

1. Open a picked order's label view and print it to the real label printer.
2. Measure the printed label against the stock.

**Expected**: the printed label occupies the stock at approximately 1⅛ × 3½ inches, with nothing
clipped and no unrequested scaling. The QR and the Code 128 are both fully within the printable
area.

3. Scan the Code 128 into any text field. **Expected**: the bare TCGplayer order identifier appears,
   character for character identical to the text printed beneath the bars.
4. Scan the QR with a phone camera. **Expected**: it resolves to that order in the application.

**If the size is wrong, stop and report it.** The label's design changes and the work resting on it
moves. Discovering this here is cheap; discovering it after the ending screens and packing desk are
built is not.

---

## Scenario 1 — A completed pick ends on a screen and produces a label

Covers User Story 1, FR-001 through FR-008.

1. Claim an order and confirm every product line as picked.
2. Finish the pick.

**Expected**: a completion outcome stating the total physical card count. **No product-line count
appears anywhere on it** — its absence is the requirement (FR-004). No print dialog has opened.

3. Ask to print the label.

**Expected**: a label carrying the short order code, the full TCGplayer identifier, the physical
card count, and the picker and time. **No customer name or address appears on it** (FR-015).
Continuing to the next order is now the primary action (FR-006).

4. Continue to the next order.

**Expected**: the next available order is claimed and opened, exactly as Pick Next behaves.

---

## Scenario 2 — A pick that needs a manager ends differently

Covers User Story 1, FR-003 and FR-013.

1. Claim an order, confirm some lines, and report an unresolved issue on at least one.
2. Finish the pick.

**Expected**: a needs-a-manager outcome stating cards pulled, cards set aside with the order, and
**which products** are unresolved.

3. Print the label.

**Expected**: a hold label distinguishable from a ready-to-pack one **in monochrome** — an inverted
band, not a colour — stating the set-aside count. Print it in greyscale to confirm it survives
(FR-013).

---

## Scenario 3 — A packer finds the order and ships it

Covers User Story 2, FR-023 through FR-030.

1. At the packing desk, scan the label from scenario 1 — or type the bare order number.

**Expected**: both resolve to the same order (FR-024), showing its card count, picker and time.

2. Print the packing slip.

**Expected**: the slip prints and contains **exactly one order's** information — check it names one
customer, not several (FR-019).

3. Mark the order packed.

**Expected**: it reaches the packed state and disappears from awaiting-packing.

4. Scan it again.

**Expected**: told plainly it is already packed; no second pack is recorded (FR-029).

---

## Scenario 4 — A held order is refused

Covers FR-028.

1. At the packing desk, enter the code for the held order from scenario 2.

**Expected**: packing is refused, and the refusal **names the unresolved product**. A bare "this
order has a problem" is a failure of this scenario — the packer must be able to tell it is not
theirs to fix.

---

## Scenario 5 — An order with no slip stays usable

Covers FR-022. This is the state of every order imported before this feature.

1. At the packing desk, resolve an order imported before this feature existed.

**Expected**: the order resolves normally, showing its counts, picker and time. Asking for its slip
says plainly that none is stored. **The desk does not error and does not appear broken**, and the
order can still be marked packed.

---

## Scenario 6 — A reprint matches the original

Covers User Story 3, FR-016.

1. Print an order's label, then print it again from the order, then again from the packing desk.

**Expected**: all three are identical, including the **original** picker and pick time — not the
person or moment of the reprint.

---

## Scenario 7 — The queue counts what is on the shelf

Covers User Story 4, FR-036.

1. Note the dashboard's awaiting-packing count. Pick an order.

**Expected**: the count rises.

2. Pack it.

**Expected**: the count falls. (The old behaviour — a picked count that only ever grew — is the
thing being corrected.)

---

## Scenario 8 — Two packers, one order

Covers FR-033. Exercised by automated test rather than by hand.

Two simultaneous attempts to pack the same order: exactly one succeeds, the other is told it is
already packed, and one packed record exists.

---

## Scenario 9 — A slip that will not extract does not sink its order

Covers FR-021, and Constitution V.

Import a batch in which one order's slip cannot be extracted.

**Expected**: every order in the batch imports, including that one. The affected order simply has
no slip and behaves as in scenario 5. The failure is recorded. **A slip problem must never reject
an otherwise valid order or fail a batch** — the order data parsed successfully and is
authoritative.

---

## Privacy checks

Run these before considering the feature done. They verify PRD §27's four bounds, which are the
whole of the mitigation for storing customer data.

1. **One order per file** — open several stored slips; each names one customer.
2. **The batch is not retained** — after an import, no stored artifact holds the whole batch
   document (FR-020).
3. **Picking surfaces cannot reach a slip** — no picking screen links to one, and no payload a
   picking screen consumes carries slip content or customer fields (FR-039).
4. **Access is attributable** — retrieve a slip, then confirm a durable access record names the
   employee and the time (FR-038, SC-010).
5. **Logs are clean** — confirm no log line contains customer name, address, or slip content, per
   the constitution's logging rule.
