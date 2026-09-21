# Discovery: Pick Completion and Hand-off

**Date**: 2026-09-21
**Participants**: Developer (jkhaynes), with card shop owners consulted on the packing bench workflow
**Addresses**: The second of the three features proposed in
[the 2026-09-20 picking experience prototype](2026-09-20-picking-experience-prototype.md)
**Mockups**: <https://claude.ai/artifact/8eL3MUHsB7Q8e5JHZUitKf> (private; share from the page's Share menu)

## Why this session happened

The 2026-09-20 session decomposed the remaining V1 work into three features and recommended
building them in order. The first — mobile picking — shipped as feature 016. This session designed
the second before specifying it, because the previous feature reached implementation with its flow
still moving and paid for it in mid-spec changes.

Mockups came first deliberately. Every decision below was made against a drawn screen rather than
a paragraph.

## What we learned about the packing bench

These are operational facts from the shop, not inferences.

**The packer's real need is the TCGplayer packing slip**, printed. Everything else the packing desk
could show is secondary to putting that document in their hand.

**Shipping splits at thirty dollars.** Under $30, an order is an envelope, a stamp and the packing
slip — the slip is the entire job. Over $30, information from the slip is keyed into a third-party
shipping-label printer, the label is printed, and **the resulting tracking number is entered back
into TCGplayer against that order**.

**The application does not know an order's value**, and nothing here changes that. The $30 split
lives in the packer's judgement, read off the slip.

**Copy-and-paste is not acceptable at the bench.** An earlier proposal — show the order number
with a copy button, let the packer paste it into TCGplayer — was rejected by the owners as too much
work per order. That rejection is what drove the two decisions that follow.

## The packing slip: reversed, deliberately

The first proposal was to store nothing: the importer would keep discarding the packing slip PDF,
and the packer would find the order in TCGplayer themselves. The owners rejected the manual lookup,
which left two options — encode the order number so a scanner could type it into TCGplayer's
search, or hold the slip ourselves and print it.

**Decision: the application stores one packing slip per order.** The importer already opens the
batch PDF; it will now also record which page(s) each order occupies and keep that order's pages as
its own file.

This was verified before it was designed around, against a real fixture
(`valid-multi-order-batch.pdf`):

| Measurement | Result |
|---|---|
| Batch | 14 pages, 35KB, one order per page |
| Extracted single order | **3KB**, reopens cleanly, text intact |
| Library | `PdfMerger` from the already-installed PdfPig 1.7.0 |

No new dependency, no second parser, and a per-order file small enough for a `varbinary(max)`
column rather than a blob store.

**This stores customer PII the application has never held.** The extracted slip carries `Ship To:`,
`Shipping Address:` and `Buyer Name:`. PRD §27 currently instructs the opposite — "extract and
retain only information required for picking and order identification rather than persisting
customer shipping PII" — and `Order.cs` documents itself as carrying no customer shipping
information. The reversal is intentional and requires a PRD amendment (A14 below); it is bounded by
one-order-per-file, no picking surface linking to a slip, and logged access.

**Retention is deferred, not decided.** Deleting a slip when its order is marked `Packed` was
proposed and rejected: it makes a mistimed tap unrecoverable, and the reprint window matters more
today than the retention does. Slips are kept indefinitely for now, and a retention rule is owed.

## The label carries two codes

A scanner is a keyboard — it types exactly what is encoded — so one code cannot serve two
destinations, and the packing workflow has two.

| Code | Encodes | Gets the packer to |
|---|---|---|
| QR | A link to the order in this application | The packing desk, in one scan; also what a phone camera needs |
| Code 128 | The bare TCGplayer order identifier | TCGplayer's own search, for the over-$30 tracking-number step |

Printing the slip from our app does not remove the TCGplayer trip — it moves it from the start of
packing to the end. The Code 128 is the original "scan into their search bar" idea, kept for the
step that still needs it.

It also removes a hardware gamble. The 2026-09-20 session accepted buying a 2D imager because a 1D
laser cannot read QR. A 1D laser reads Code 128 fine, so the desk works with whatever scanner is
already on it; a 2D imager is now an upgrade rather than a prerequisite.

The Code 128's printed value is the human-readable full order identifier, so what a person reads
and what a scanner types cannot diverge.

## Decisions taken

| Decision | Relationship to the PRD |
|---|---|
| Every pick ends on a screen: complete, or needs a manager | Implements §22 |
| The ending screen states **one** count — physical cards | **Narrows §22**, which specifies products and cards |
| The label carries the physical card count only | **Narrows §22.1**, which requires both counts |
| Product counts removed from the label, packing desk and queue alike | Follows from the above |
| Printing the label is an explicit tap; the print dialog never opens by itself | New |
| Once printed, "Next order" becomes the primary action | New |
| "Next order" claims the next order, as Pick Next does | Clarifies §10 |
| Finishing a pick releases the claim, as it does today; the ending screen is a receipt | Confirms 015 |
| Reprinting a label is available from the order and from the packing desk | New |
| The label carries a Code 128 alongside the required QR | **Extends §22.1** |
| The short order code is the application's own order number | Implements §22.1 |
| The application stores one packing slip per order, extracted at import | **Contradicts §27** |
| Slips are kept indefinitely; a retention rule is deferred | New open question |
| Any authenticated employee may open a slip and mark an order `Packed` | Narrows §9.4 by declining to use it |
| A slip is reachable only from the packing workflow; access is logged | New |
| Scanning a held order refuses and names the unresolved product | Implements §22.1 |
| `Packed` is terminal — no undo | Confirms §20.1 |
| No history view for `Packed` orders in V1 | New |
| The dashboard's "Picked" tile becomes "Awaiting packing" | Revises 015's surface |
| The label's "ships short" marker is built now, unused until feature 3 | Implements §22.1 ahead of need |

## `Packed` is the first status that is not derived

Feature 015 established one rule, enforced in `OrderStatusComputation.cs` and applied on every
write that can change status: *any line has an unresolved issue → Needs Attention; else every line
picked → Picked; else claimed → In Progress; else Ready.* Status is never an independent flag,
which is what stops it drifting out of step with the lines.

`Packed` cannot be derived that way. Nothing about an order's lines changes when its sleeve goes in
the mail — it is an event, recorded with who and when.

The decision is to keep the derivation exactly as it stands and let a recorded `PackedAt`
short-circuit it: if the order is packed it is `Packed`, otherwise derive as before. One explicit,
documented exception is preferable to loosening the rule.

## What this requires of the PRD

Three amendments, drafted for Product Owner approval in
[`Loot_Singles_Fulfillment_PRD_v0.5-proposed-amendments.md`](../prd/Loot_Singles_Fulfillment_PRD_v0.5-proposed-amendments.md):

- **A14 — §27 Customer Privacy.** Reverses 0.4's instruction not to persist customer shipping PII.
  The substantive one.
- **A15 — §22 Pick Completion.** Card count only on the ending screen.
- **A16 — §22.1 Order Hand-off and Labelling.** Card count only on the label; Code 128 alongside
  the QR; the packing desk prints the slip.

A15 and A16 are small. A14 changes what kind of data the application holds and should be read
closely.

## Still open

- **Browser printing has still not been tried against the real printer.** Carried forward unchanged
  from 2026-09-20. The owners confirm shipping labels have been printed to it from a computer
  before, which is encouraging but is not the same as a browser hitting a 3½ × 1⅛ inch label at the
  right physical size. This remains the largest risk in the feature and should be proven early in
  implementation rather than discovered late.
- **How long a stored packing slip is kept.** Deferred above; owed before the slip store grows to a
  size anyone has to think about.
- **None of this has been validated with the people who pull and pack Loot orders.** The owners
  described the bench workflow; no one has watched a packer work through a real order against these
  screens. PRD §42 Priority 4 still asks for this.

## Next

`/speckit-specify` for the feature, once A14–A16 are approved. Nothing here is a specification —
these are the decisions a specification will be written from.
