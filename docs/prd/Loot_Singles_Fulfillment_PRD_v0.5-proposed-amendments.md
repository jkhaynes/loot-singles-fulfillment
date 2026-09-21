# PRD v0.5 — Proposed Amendments (for Product Owner review)

**Base**: `Loot_Singles_Fulfillment_PRD_v0.4.md` (now
[`Loot_Singles_Fulfillment_PRD_v0.5.md`](Loot_Singles_Fulfillment_PRD_v0.5.md))
**Drafted**: 2026-09-21
**Source**: [Pick Completion and Hand-off discovery](../discovery/2026-09-21-pick-completion-handoff.md)
**Status**: **Applied.** Approved by the Product Owner on 2026-09-21 and folded into
[`Loot_Singles_Fulfillment_PRD_v0.5.md`](Loot_Singles_Fulfillment_PRD_v0.5.md), which is now the
authoritative PRD.

This document is kept as the record of *why* each change was made and what it replaced. It is not
a live proposal and must not be cited as a requirement — cite the current PRD.

Three amendments, arising from designing the pick completion and hand-off feature before
specifying it. **A14 changes what kind of data the application stores** and is the one needing the
closest reading. A15 and A16 are small.

---

## A14 — §27 Customer Privacy: V1 stores one packing slip per order

**Today** §27 says packing slips contain customer information the picker does not require, that V1
should follow data minimization principles, and — the operative sentence — that "where technically
practical, V1 should extract and retain only information required for picking and order
identification rather than persisting customer shipping PII." It closes by noting that "the packing
workflow may have different requirements in V2."

**Change**: V1 stores one TCGplayer packing slip per order, extracted from the imported batch
document at import time. The batch document itself is never retained.

**Replace §27 with:**

> # 27. Customer Privacy
>
> TCGplayer packing slips contain customer information that the picker does not require, including
> shipping information.
>
> V1 follows data minimization principles. The picker workflow must not expose customer
> information, and no order or order line carries customer fields.
>
> **The packing workflow is the exception, because packing is not picking.** The packer needs the
> customer's address to produce a shipment, and in V1 that address reaches them on the TCGplayer
> packing slip itself. V1 therefore stores one packing slip per order, extracted from the imported
> batch document at import time; the batch document is not retained.
>
> This reverses 0.4's instruction to avoid persisting customer shipping PII. It is bounded by four
> rules, and the reversal is only acceptable with all four:
>
> - **One order per file.** A stored slip contains exactly one customer's data. No stored artifact
>   contains more than one order's slip.
> - **Not reachable from picking.** No picking surface links to a slip or exposes its contents. A
>   slip is reachable only from the packing workflow (§22.1).
> - **Access is recorded.** Every retrieval of a slip is logged with the employee and the time.
> - **Retention is deferred, not absent.** V1 keeps slips indefinitely, so that a mistimed action
>   cannot destroy a slip a packer still needs. A retention and deletion rule is owed and is
>   recorded as an open question (§41).
>
> Nothing further is extracted from a slip into the application's own data model.

**Also add to §41 Remaining Open Questions:**

> **How long is a stored packing slip kept?**
>
> V1 stores one packing slip per order (§27) and deletes none. Deleting a slip when its order is
> marked `Packed` was considered and deliberately deferred, because it makes a mistimed action
> unrecoverable and the reprint window matters more today than the retention does. The store
> therefore grows without bound and accumulates customer shipping data. A retention and deletion
> rule is owed.

**Why**: The packer's job is to print the packing slip. The alternative — the packer finds the
order in TCGplayer and prints it from there — was put to the shop owners as a copy-and-paste step
per order and rejected as too much work at the bench. Holding the slip is what removes that step.

Feasibility was verified before this was proposed: a 14-page, 35KB batch fixture split into a 3KB
single-order PDF that reopens cleanly with its text intact, using `PdfMerger` from the PdfPig
already installed. No new dependency and no second parser.

**Read this closely.** It is the first customer PII the application will ever hold, and the four
bounding rules are the whole of the mitigation. If any of them is not worth its cost, the
amendment should be rejected rather than weakened — the fallback is the copy-and-paste flow the
owners have already turned down, which is a worse product but a smaller liability.

---

## A15 — §22 Pick Completion: one count, not two

**Today** §22 specifies the completion screen as:

> **Pick Complete**
>
> 5 products\
> 8 physical cards\
> All items picked

and the manager ending as "7 cards pulled / 1 product unresolved / 1 card set aside with the
order", and says each screen "summarizes products, physical cards and issues".

**Change**: The screen states the physical card count and not the product count.

**Replace the two example screens and their surrounding sentences with:**

> **Pick complete** --- every line confirmed, nothing unresolved:
>
> > **Pick Complete**
> >
> > **8**\
> > cards in the sleeve
>
> It states the physical card count, prints a ready-to-pack label (§22.1), and offers the next
> order.
>
> **Needs a manager** --- the picker has pulled what they can and something is unresolved:
>
> > **Pick Ended --- Needs a Manager**
> >
> > **7**\
> > cards pulled\
> > 1 card set aside with the order
>
> It states what was pulled, what has been set aside and which product is unresolved, prints a hold
> label (§22.1), and directs the bundle to the review area.
>
> The screen states one count deliberately. A product count --- distinct card entries, as distinct
> from the cards themselves --- cannot be checked against a sleeve of loose cards. On a screen
> whose only job is catching a miscount before the sleeve is sealed, a number nobody can verify
> competes for attention with the one they can.

**Why**: Directly from the Product Owner, reviewing the mockup: the product count "is irrelevant at
this stage. Just total number of cards is good." The reasoning holds — the completion screen exists
to be counted against a physical sleeve, and only one of the two numbers can be.

---

## A16 — §22.1 Order Hand-off and Labelling: card count only, a second code, and the slip

**Today** §22.1 requires the label to carry "the product and physical card counts", requires a QR
code encoding a link to the order, and says scanning at the packing desk "must identify the order,
its counts and its picker, and allow it to be marked `Packed`."

**Change 1 — counts.** In the human-readable list, replace:

> -   The product and physical card counts

with:

> -   The physical card count

for the same reason as A15.

**Change 2 — a second code.** After the existing QR paragraph, add:

> The label must also carry a **Code 128 barcode** encoding the bare TCGplayer order identifier.
>
> The two codes serve two destinations. The QR brings a scanner or a phone camera into this
> application. The Code 128 types the order identifier into TCGplayer's own search, which is where
> a tracking number is recorded for an order shipped with a carrier label. A scanner types exactly
> what is encoded, so one code cannot do both.
>
> The Code 128 also reads on a 1D laser scanner, which cannot read QR at all. A 2D imager is
> therefore an upgrade rather than a prerequisite, revising 0.4's acceptance that one must be
> bought.
>
> The Code 128's printed value is the human-readable full TCGplayer order identifier required
> above, so what a person reads and what a scanner types cannot diverge.

**Change 3 — the packing desk prints the slip.** After the existing packing-desk sentence, add:

> The packing desk must also print that order's stored packing slip (§27), which is what the packer
> physically needs. Under thirty dollars the slip is the whole job; above it, the slip's information
> is keyed into a third-party shipping-label printer and the resulting tracking number is entered
> against the order in TCGplayer.
>
> Any authenticated employee may open a slip and mark an order `Packed`. The role split (§9.4) does
> not describe packers, and a role check here would obstruct packing rather than protect anything.

**Why**: Printing the slip from this application does not remove the trip to TCGplayer — it moves
it from the start of packing to the end, where the tracking number is recorded. The Code 128 is
what keeps that step from requiring an order identifier to be typed by hand.

---

## What these amendments do not change

- Packing **verification** remains a V2 goal (§36) and a V1 non-goal (§35). §22.1's boundary
  between association and verification stands untouched.
- `Packed` remains terminal (§20.1). No undo is proposed.
- `Awaiting Customer Decision` and `Cancelled` (§20.1) remain defined and unreachable until the
  issue-resolution feature.
- No customer data beyond the stored slip enters the application. Orders and order lines are
  unchanged in this respect.
