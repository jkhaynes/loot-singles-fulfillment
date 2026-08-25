# Research: Fix Multi-Page Packing-Slip Order Import

## 1. Root cause, traced against the real sample and live application logs

**Finding**: `PdfPigPackingSlipParser.ProcessPage` runs once per PDF page and appends a new
`RawOrderBlock` to `ParseState.OrderBlocks` whenever that page contains a "Quantity/Description/Price"
table header (`FindTableHeader`). The real 3-page sample repeats that column header on every page.
`ExtractProductLines` requires locating a short line containing the word "Total" *below* the header,
aligned with the description column, to serve as the lower Y-axis bound for scanning quantity/
description words (`totalLine`); when none is found, it returns `Array.Empty<RawProductLine>()` for
that page. TCGplayer prints the grand-total row (`"77 Total $379.05"`) only once, on the final page
of a multi-page order — continuation pages have real product rows but no "Total" row of their own.

Confirmed live against the running application: importing the real sample produced
`"Detected 3, succeeded 1, failed 2. NoProductLines: 2 [F8433182-69FC9F-7D725,F8433182-69FC9F-7D725]"`
— three page-level blocks, all sharing the same order identifier, only the last page's block (the
one with the "Total" row) succeeded, and pages 1–2's 30+ real product lines were silently absent
from the resulting order rather than surfaced as any kind of per-line error.

**Decision**: The defect is not in per-page extraction (`ExtractOrderIdentifier`,
`ExtractProductLines`, table-header detection, or `OrderLineExtractor`) — each of those already
produces the correct data for the page it's given. The defect is that `RawOrderBlock`/
`ParsedPackingSlip.OrderBlocks` are documented and treated as "one block per page," when a real
order can legitimately span pages. The fix adds a merge step, once per parse, that treats multiple
blocks sharing the same non-null order identifier as continuation pages of one logical order and
concatenates their product lines in page/reading order.

**Alternatives considered**: Changing `ExtractProductLines` to locate the *next* page's boundary
instead of a "Total" row — rejected as far more invasive (would require passing cross-page context
into a currently page-scoped method) for no behavioral benefit over merging after the fact, since
the per-page data extracted today is already correct wherever a table header and rows exist; only
the "Total" row is genuinely page-scoped and only the last page has one, by design of the source
document. Merging by order identifier is also simpler to reason about and test in isolation.

## 2. Where the merge belongs, and how it interacts with existing consumers

**Decision**: The merge lives in `PdfPigPackingSlipParser.ParseState.ToResult()` — the exact point
that already turns per-page scan state into the parser's output shape — so it runs once, after all
pages have been read. `PackingSlipImportService` needs no change: its `foreach (var block in
parsed.OrderBlocks)` loop, duplicate-order check, and validation already treat each block as one
logical order; it simply starts receiving correctly-assembled blocks instead of one per page.

**Grouping key**: Non-null, non-whitespace `OrderIdentifier`, compared with
`StringComparer.OrdinalIgnoreCase` — consistent with the existing case-insensitive identifier
comparison `PackingSlipImportService.HasSummaryMismatch` already uses for the same summary-page
cross-check. A block whose identifier is null or whitespace (the existing `MissingOrderIdentifier`
failure case) is never merged with anything, including other blocks with a missing identifier —
each remains its own distinct page/failure, exactly as today, since a missing identifier gives no
basis to say two such pages belong to the same order.

**Order preservation**: Merged blocks appear in the result at the position of their *first*
occurrence; a later page's product lines are appended (not prepended) to the existing block's list,
preserving the original page-by-page, top-to-bottom reading order (spec.md FR-002, User Story 1
Acceptance Scenario 3).

**Effect on live progress updates**: `ParseAsync` yields an intermediate `PackingSlipParseUpdate`
after each page using the raw (unmerged) `state.OrderBlocks.Count`, and only the final, `IsComplete`
update carries the fully-merged `ParsedPackingSlip`. Traced through `PackingSlipImportService`:
intermediate updates' `OrdersDetected` values are surfaced to the frontend progress display as pages
stream in, but the *final* detected/succeeded/failed counts reported to the caller and logged all
derive from `parsed.OrderBlocks.Count` post-merge (the parser's own final `PackingSlipParseUpdate.OrdersDetected`
field is computed but never read once `IsComplete` is true). Consequence: a multi-page order's live
progress indicator may transiently show a higher page-block count while later pages are still being
scanned, settling to the correct, merged count once parsing completes — a minor, pre-existing
property of the incremental-progress design, not a regression this fix introduces, and out of scope
per spec.md's stated boundary (this is a parsing/order-assembly correctness fix, not a progress-UI
change).

**Alternatives considered**: Merging incrementally as each page is processed (inside `ProcessPage`
itself) instead of once at the end — rejected as unnecessary complexity for no user-visible benefit
beyond the live progress-count nuance above, which spec.md does not require solving.

## 3. Fixture sanitization check

**Finding**: The provided PDF was inspected directly. It uses obviously placeholder data — "Jamie
Example", "123 Example Street", "Sample City, MI 00000" — and a synthetic-format order number
(`F8433182-69FC9F-7D725`), matching this project's existing fixture-sanitization conventions (the
same pattern already used by `010-lorcana-order-parsing`'s fixture). No real customer data is
present.

**Decision**: Safe to add to the repository as a project fixture
(`backend/tests/LootSingles.Fixtures/PackingSlips/multi-page-order-no-total-on-continuation-pages.pdf`,
already added), consistent with constitution Principle VII.

## 4. No new production logging

**Finding**: This is a pure parsing/assembly-correctness fix. `PackingSlipImportService`'s existing
per-attempt completion log (detected/succeeded/failed counts, per-failure-type breakdown) already
reports accurately — it will simply report correct numbers once it receives correctly-merged blocks.
No new failure mode is introduced; a page that fails for a genuine reason (missing identifier,
still-empty product lines after merging) still fails the same way it does today.

**Decision**: No new logging is added, consistent with the precedent established for routine,
no-new-failure-mode changes (features 007/008/010; contrast with feature 009's genuine new failure
mode, which did warrant new logging).
