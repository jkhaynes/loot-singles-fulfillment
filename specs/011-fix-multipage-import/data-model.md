# Data Model: Fix Multi-Page Packing-Slip Order Import

No entity, field, table, or migration is added or changed by this feature. `Order` and `OrderLine`
(feature 001) are unchanged. `RawOrderBlock` and `ParsedPackingSlip` (feature 001, internal parser
output shapes, never persisted) keep the exact same fields — only what a "block" is assembled from
changes.

## `RawOrderBlock` / `ParsedPackingSlip.OrderBlocks` — corrected invariant

Both types' existing XML doc comments state "one [block] per order **page**" — this was the latent
assumption that caused the defect. After this fix, the invariant becomes "one block per logical
**order**," which may be assembled from one or more pages:

| Before this fix | After this fix |
|---|---|
| `ParsedPackingSlip.OrderBlocks` = one entry per PDF page that has a product table, regardless of whether multiple pages share the same order identifier | `ParsedPackingSlip.OrderBlocks` = one entry per distinct, non-null order identifier found across all pages; a block's `ProductLines` is the concatenation, in page/reading order, of every page sharing that identifier |
| A continuation page with no "Total" row produces its own block with zero product lines, later rejected as `FailureType.NoProductLines` | A continuation page's product lines are appended to the block for the order identifier it shares with an earlier page — no separate, near-empty block is produced for it |
| A page with a missing/unreadable order identifier produces its own block (`OrderIdentifier = null`) | Unchanged: still produces its own block, never merged with anything, still eligible for `FailureType.MissingOrderIdentifier` downstream |

The doc comments on `RawOrderBlock.OrderIdentifier`/`ProductLines` and
`ParsedPackingSlip.OrderBlocks` are updated to describe this corrected invariant as part of this
fix, since the previous wording ("one page per order," "one raw block per order page") is no longer
accurate and would mislead a future reader.

## Test fixture (new)

A new fixture file, `multi-page-order-no-total-on-continuation-pages.pdf`, is added to
`backend/tests/LootSingles.Fixtures/PackingSlips/`, alongside feature 001's existing fixtures. It
is purely additive — no existing fixture file is changed or removed.
