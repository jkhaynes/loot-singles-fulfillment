# PRD v0.4 — Proposed Amendments (for Product Owner review)

**Base**: `Loot_Singles_Fulfillment_PRD_v0.3.md`
**Drafted**: 2026-09-20
**Source**: [Picking Experience Prototype discovery](../discovery/2026-09-20-picking-experience-prototype.md)
**Status**: **Applied.** Approved by the Product Owner on 2026-09-20 (PR #30) and folded into the
PRD, which was v0.4 at the time and is now
[`Loot_Singles_Fulfillment_PRD_v0.5.md`](Loot_Singles_Fulfillment_PRD_v0.5.md).

This document is kept as the record of *why* each change was made and what it replaced. It is not
a live proposal and must not be cited as a requirement — cite the current PRD.

Every amendment below traces to a decision recorded in that discovery session. Two of them
**change approved V1 scope** and are marked accordingly — those are the ones needing the closest
reading.

---

## A1 — §8 Supported Devices: name the two postures

**Today** §8 requires a responsive experience for desktop and mobile phone, and says "the
interaction may adapt to available screen size while maintaining the same underlying workflow."

**Add**: On a phone, picking defaults to a focused view showing one product at a time. On a
desktop, it defaults to the full list. Either view MUST remain reachable from the other on any
device, and a deliberate choice MUST persist for that employee.

**Add**: Manager work — reviewing problem orders and recording what a customer decided — is a
seated task at a computer, and its surfaces are designed for the desktop. Picking is the
mobile-first workflow; manager review is not.

**Why**: The two devices are used in genuinely different postures — one-handed at a storage box
versus seated at a bench. §8 already permits this; the amendment makes the default explicit so it
is not decided per-screen during implementation.

---

## A2 — §10 Order Queue: claiming happens on the order

**Today** §10 defines two ways to start work: Pick Next Order, and Choose Order.

**Add**: Choosing an order opens it. Claiming is an explicit action on the order itself, so that
viewing an order is always safe and starting one is always the same act. Pick Next Order remains a
single action that claims and opens the next order.

**Add**: When an employee already holds a claim, the dashboard MUST offer to resume that order
rather than offering to start another. Attempting to start another is a state the interface should
reflect, not an error it should report after the fact.

**Why**: As built, an order opened from the dashboard has no claim action, so the most obvious path
dead-ends and the employee must back out to a different screen. Pressing Pick Next while holding an
order currently produces an error message.

---

## A3 — §12 and §18: focused picking, and what advancing means

**Today** §18 offers "Product 3 of 5" and "6 of 9 physical cards accounted for" as potential
progress information, and requires that the picker "be able to navigate backward or review the
order rather than being permanently committed by an accidental swipe/action." §23 requires that the
application "must not falsely record a card as picked merely because the interface advanced."

**Add**: In the focused view, moving between products — by swipe or by control — MUST NOT record any
outcome. Only an explicit action records a pick or an issue. Navigation controls MUST be operable
without swiping.

**Add**: Progress MUST be expressed in physical cards as well as products, and MUST include the
picker's position within the current set.

**Why**: Swipe-to-advance and swipe-to-confirm are one gesture apart, and the second would violate
§23. Stating it in the PRD keeps the distinction from eroding under implementation pressure.

---

## A4 — §13 Set-Aware Picking: transitions and the incomplete-box guard

**Today** §13 requires cards to be grouped/sorted by set and suggests explicit transitions such as
"Surging Sparks complete / Next set: Destined Rivals".

**Add**: When every product in a set is resolved, the application MUST present the transition
explicitly, naming the next set and its size.

**Add**: When a picker reaches the end of a set that still has unresolved products, the application
MUST say so, list what remains, and require a deliberate choice — return to those products, report
what is missing, or leave the set. It MUST NOT present a set as finished while products in it are
unresolved.

**Add — set ordering**: sets MUST be grouped by game, because Loot stores each game in its own
section and crossing between sections is the expensive walk. Within a game, sets are ordered
alphabetically.

Loot's shelves run newest-to-oldest within a section, so release-date ordering would match the
aisle more closely. It is deliberately **not** required in V1: the order data carries no release
date, and adding one means widening the catalog fetches per game and defining where an undated set
sorts. Ordering within one game's aisle only decides which direction the picker walks it, so the
gain does not yet justify the work. Release ordering may be added later without disturbing
anything built on this rule.

**Why**: The whole point of set grouping is walking to a box once. A picker who skips a card and
walks away has to walk back, which is exactly the cost §13 exists to remove.

---

## A5 — §19.1 Structured Issue Information: record what was found instead

**Today** §19.1 asks issues to capture structured information, giving required-versus-found
quantities as the example.

**Add**: Where a picker finds a different card that appears to explain the discrepancy — a
different variant, condition or set of the same product — the issue MUST be able to record that
card's identity alongside the ordered one, and note that it has been set aside with the order.

**Why**: The most common real case is an inventory mismatch: the ordered non-holo is missing, an
unlisted holo is present, and the holo was almost certainly entered as the non-holo. The picker
already sets that card aside with the order. Without somewhere to record it, that evidence survives
only as prose in an optional note, and the manager reviewing it is reading a problem report rather
than a proposal.

---

## A6 — §20 Order Status: finish the lifecycle ⚠️ **changes approved scope**

**Today** §20 defines Ready → In Progress → Picked, with Needs Attention as the problem branch, and
states that additional states "may be needed for: Released orders, Abandoned orders, Resumed
orders, Resolved issues, Cancelled orders. These remain to be designed."

**Add — Awaiting Customer Decision**: an order whose issue has been put to the customer. It is not
actionable by a picker and MUST be visually distinct from issues that still need attention. It
MUST show how long it has been waiting, so a manager can see which conversations have gone quiet.
Nothing expires or cancels automatically — a refund is a decision about a customer's money, and it
stays with a person.

**Add — Packed**: an order whose sleeve has been packed and dispatched. This is the terminal state.
Without it, Picked is terminal and completed work accumulates indefinitely with no record of what
is still physically on the shelf.

**Add — Cancelled**: an order the customer has had refunded in full. It stops appearing in working
views.

**Add — line-level outcomes**: a product line may also end as **written off** (the customer accepted
a refund for it and the rest ships) or **substituted** (a different card ships with the customer's
approval, recorded against the line). An order containing a written-off line is complete but
**short**, and MUST be represented as short wherever it is packed.

**Why**: Today an order in Needs Attention can only leave by someone finding the card. When the card
genuinely is not there, the order is stuck forever. These states are the ones §20 already
anticipated; this amendment designs them.

---

## A7 — §21 Dashboard: counts, not contents

**Today** §21 shows Needs Attention with inline detail, for example an order id, the flagged product
name, and required-versus-found quantities.

**Change**: The dashboard MUST show counts per state. Detail belongs on the page dedicated to that
state, which the count links to.

**Why**: Inline detail grows without bound as the queue grows — precisely when the dashboard most
needs to stay readable. Feature 015 shipped flagged product names on the tile; this supersedes that
surface, and the requirement it served (identifying which lines are flagged without opening the
order) is satisfied by the dedicated page.

---

## A8 — §22 Pick Completion: two endings, both deliberate

**Today** §22 says an order cannot become Picked until every line is acknowledged with no unresolved
blocking issues, and that a completion screen "may summarize" products, physical cards and "all
items picked", as an opportunity to catch quantity mistakes.

**Change "may" to "MUST"**, and add the second ending:

- **Pick complete** — every line confirmed, nothing unresolved. Summarises products, physical cards
  and issues, prints a ready-to-pack label, and offers the next order.
- **Needs a manager** — the picker has pulled what they can and something is unresolved. Summarises
  what was pulled, what is unresolved, and what has been set aside; prints a hold label; directs the
  bundle to the review area.

**Why**: A pick currently ends with a status label changing and nothing else, which is the single
clearest cause of the workflow feeling unfinished. The second ending does not exist at all, so a
bundle with an unresolved issue has no defined resting place.

---

## A9 — New section: Order Hand-off and Labelling ⚠️ **changes approved scope**

**Today** nothing in V1 covers what happens between "the cards are picked" and "someone packs
them". §35 lists "Generate physical order barcodes for packing" as a V1 non-goal, and §36 places the
whole scanned hand-off in V2.

**Add a new section** covering:

- Every completed pick MUST produce a printed label identifying the order, applied to the sleeve.
- The label MUST carry, in human-readable form: a short order code, the full TCGplayer order id,
  the product and physical card counts, and who picked it and when.
- The label MUST carry a QR code encoding a link to that order, so a phone camera and a desk
  scanner both resolve it. QR is fixed by the phone-camera requirement; if the current desk scanner
  is a 1D laser it cannot read QR, and the Product Owner has accepted replacing the scanner rather
  than changing the symbology.
- The label MUST NOT carry customer name, address or any other customer data. A dropped label must
  leak nothing.
- A label for an order with an unresolved issue MUST be visually distinct in monochrome — an
  inverted band, not a colour — and MUST state the held-aside count.
- An order that ships short MUST be labelled as short.
- Scanning or entering a code at the packing desk MUST identify the order, its counts and its
  picker, and allow it to be marked Packed. Scanning a held order MUST refuse and explain.
- Printing happens from the picker's device through the browser. The hosted API cannot reach a
  printer on the shop network, so printing is necessarily client-side.
- The label is sized for standard address stock (about 1⅛ × 3½ inches) on the shop's small label
  printer. The larger printer's shipping stock is bigger than the sleeve the label is applied to.

**What stays out**: packing verification — checking that the sleeve's contents match the order —
remains a V2 goal per §36. This amendment covers **association** (which order is this?), not
**verification** (is this correct?).

**Why**: Sleeved cards currently leave the picker's hands with nothing tying them to an order. The
owners confirmed label printers are already on site and asked for a barcode in V1. This is a
Product Owner decision that overrides the §35 non-goal, recorded here rather than absorbed
silently.

---

## A10 — §34 Auditability: packed by, and substitutions

**Today** §34 asks V1 to retain enough history to answer "who picked this order?", listing order,
picker, pick started and completed, issues, reporter and timestamps.

**Add**: who packed the order and when, and — where a line was substituted — what was ordered, what
shipped in its place, and that the customer approved it.

**Why**: §36 names "Picked by / Packed by" as a V2 aspiration, but the Packed state added in A6
makes it available in V1 at no extra cost. A substitution changes what physically ships; if it is
not recorded, nobody can answer a customer question about it later.

---

## A11 — §35 V1 Non-Goals: remove the barcode exclusion ⚠️ **changes approved scope**

**Today** §35 lists, among V1 non-goals:

> - Perform packing verification
> - Generate physical order barcodes for packing

**Remove** the second line. **Keep** the first, explicitly.

**Why**: Product Owner decision, 2026-09-20. Labels carrying a QR code are now V1 (A9). Packing
verification remains out of scope, and keeping that line makes the boundary legible: V1 identifies
a bundle, it does not check its contents.

---

## A12 — §36 Future V2: Packing — narrow it to what remains

**Today** §36 describes a V2 workflow beginning "Generate Order QR / Barcode → Attach Identifier to
Bag" and ending in packing verification, and states "V2 is explicitly outside V1 scope."

**Change**: Note that the identifier and attachment steps have moved into V1 (A9). What remains V2
is scan-based **verification** of a bag's contents against its order, and the packing workflow built
on it.

---

## A13 — §42 Remaining Discovery Priorities: mark progress

**Priority 4 (Picker Prototype)** — addressed by the 2026-09-20 session. Card information hierarchy,
quantity treatment, set transitions, mobile versus desktop, swipe versus buttons, order overview and
picking issue interaction were all prototyped. **Not yet validated with people who actually pull
Loot orders** — that validation is still outstanding and should happen before or alongside
implementation.

**Priority 2 (Issue Resolution Workflow)** — substantially answered: the customer decides between a
full refund, a partial refund with the rest shipped, or an approved substitute; managers own that
conversation; pickers set candidate cards aside at pick time.

---

## Amendment summary

| Id | Section | Nature |
|---|---|---|
| A1 | §8 Supported Devices | Clarifies |
| A2 | §10 Order Queue | Clarifies |
| A3 | §12, §18 Guided picking and progress | Clarifies, adds a MUST NOT |
| A4 | §13 Set-Aware Picking | Adds requirements |
| A5 | §19.1 Structured Issue Information | Adds a requirement |
| A6 | §20 Order Status | ⚠️ Adds lifecycle states |
| A7 | §21 Dashboard | Changes an existing requirement |
| A8 | §22 Pick Completion | Strengthens "may" to MUST; adds an ending |
| A9 | New — Order Hand-off and Labelling | ⚠️ New V1 scope |
| A10 | §34 Auditability | Adds requirements |
| A11 | §35 V1 Non-Goals | ⚠️ Removes an exclusion |
| A12 | §36 Future V2: Packing | Narrows |
| A13 | §42 Discovery Priorities | Records progress |

## Risks this draft carries into planning

Every open question from the first draft was settled on 2026-09-20: set ordering (A4), scanner
symbology (A9), manager console device (A1), the fate of an unanswered customer (A6), and label
stock (A9). Two risks remain, and neither blocks approving these amendments:

1. **Browser printing has not been tried on the real printer.** Print scaling, margins and driver
   behaviour decide whether a browser-generated label comes out the right physical size. Prove it
   on the actual hardware early in the hand-off feature rather than late.
2. **Nothing here has been validated with the people who pull Loot orders.** §42 Priority 4 asks
   for that, and it remains worth doing before or alongside implementation.
