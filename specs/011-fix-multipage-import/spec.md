# Feature Specification: Fix Multi-Page Packing-Slip Order Import

**Feature Branch**: `011-fix-multipage-import`

**Created**: 2026-08-25

**Status**: Draft

**Input**: User description: "Fix the TCGplayer packing-slip parser (feature 001, PdfPigPackingSlipParser) so it correctly imports multi-page orders instead of silently dropping product lines from every page except the last. A real, sanitized 3-page TCGplayer packing slip (backend/tests/LootSingles.Fixtures/PackingSlips/multi-page-order-no-total-on-continuation-pages.pdf, order F8433182-69FC9F-7D725, 44 product lines across 3 pages) demonstrates the defect: ProcessPage treats every PDF page containing a \"Quantity/Description/Price\" table header as the start of a brand-new order block, and ExtractProductLines requires finding a \"Total\" summary row below that header to know where the product rows end (used as the lower Y-axis bound when scanning for description/quantity words) - but TCGplayer's real packing slips only print that grand-total row once, on the final page of a multi-page order. Continuation pages (2 of 3, 3 of 3, etc.) repeat the same column header but have no \"Total\" row of their own, so ExtractProductLines returns zero product lines for every page except the last, and PackingSlipImportService rejects each of those pages as a separate malformed order (FailureType.NoProductLines) rather than recognizing them as continuation pages of the single order whose identifier (from \"Order Number: <id>\") is identical across all of them. Confirmed with this real sample: 3 order-blocks were detected (one per page, all sharing the same Order Number), only the last page's block succeeded, and the other two pages' 30+ real product lines (mostly Pokemon \"Mega Evolution\" and \"Scarlet & Violet\" era cards, e.g. \"Pokemon - ME05: Pitch Black: Fomantis - 085/084 - #085/084 - Illustration Rare - Near Mint Holofoil\") were silently never imported at all - not rejected with a visible error the importing employee would see as a per-line issue, just absent from the resulting order. This is a defect in already-shipped feature 001 import behavior (multi-page orders were evidently never validated against a real multi-page sample before), not new product scope - single-page orders and the existing test fixtures must continue to import exactly as they do today. The fix must correctly recognize that multiple detected page-level blocks sharing the same Order Number represent one logical order and merge their product lines (in page/reading order) into a single imported order, using this real sample as validation, without changing how a genuinely distinct order (a different Order Number) is detected or how the existing single-page fixtures behave. This is strictly a parsing/order-assembly correctness fix within feature 001's existing import pipeline - it does not add new fields, new games, new UI, or any new functional scope beyond correctly importing multi-page order data that should already import successfully today."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-Page Orders Import Completely (Priority: P1)

As an employee importing a TCGplayer packing slip that spans multiple pages, I need every product line from every page to end up in the resulting order, so the order I see in the picking queue actually contains everything the customer bought instead of only whatever happened to be on the last page.

**Why this priority**: This is the entire purpose of the fix. A multi-page order currently loses most of its product lines silently — no error the importing employee would see as a per-line problem, the lines are just absent — which is a data-integrity defect, not a missing enhancement.

**Independent Test**: Import the real, sanitized 3-page sample packing slip and confirm the resulting order contains all 50 product lines from all 3 pages, in the same top-to-bottom, page-by-page order they appear in the source document, with no rejection.

**Acceptance Scenarios**:

1. **Given** a packing slip whose product table spans 3 pages, with a "Total" summary row present only on the last page, and the same "Order Number: F8433182-69FC9F-7D725" identifier printed on every page, **When** the order is imported, **Then** a single order is created containing all product lines from all 3 pages, not just the last page's lines.
2. **Given** the same packing slip, **When** the order is imported, **Then** the import succeeds with no per-page rejection or "no product lines" failure for pages 1 or 2.
3. **Given** the same packing slip, **When** the resulting order's lines are inspected, **Then** they appear in the same order the corresponding rows appear in the source PDF (page 1 top-to-bottom, then page 2, then page 3).

---

### User Story 2 - Existing Import Behavior Remains Unaffected (Priority: P1)

As the business owner, I need this fix to leave every already-working import case exactly as it works today, so correcting the multi-page defect doesn't introduce a regression in orders that already import correctly.

**Why this priority**: An already-shipped, already-tested capability regressing as a side effect of an unrelated fix would be a worse outcome than the original defect. This must be verified with the same rigor as the fix itself.

**Independent Test**: Re-run the existing packing-slip import test suite (including the single-page Pokémon and Lorcana fixtures) after the fix and confirm every previously-passing case still produces byte-for-byte identical results, and that two genuinely separate orders (different Order Numbers) in the same upload still import as two separate orders.

**Acceptance Scenarios**:

1. **Given** the existing single-page packing-slip fixtures used by feature 001's test suite, **When** they are imported after this fix, **Then** every resulting order's identifier, status, and line data are identical to before the fix.
2. **Given** an upload containing two genuinely distinct single-page orders (two different Order Numbers), **When** it is imported, **Then** two separate orders are still created, each with only its own product lines — the fix does not cause unrelated orders to merge.

---

### Edge Cases

- What happens when a continuation page's own product-line extraction genuinely fails for a reason unrelated to the missing "Total" row (e.g., a page whose table header can't be located at all)? The order MUST still fail safely with a clear, typed reason for the affected line(s) — this fix corrects the false-positive "no product lines on a continuation page" case, it does not weaken detection of genuinely malformed pages.
- What happens when an order's pages are not contiguous in the source PDF (e.g., interleaved with a different order's pages)? Out of scope for this fix: TCGplayer packing slips are assumed to print one order's pages contiguously, consistent with every sample seen to date; this fix does not need to reorder or re-associate non-contiguous pages.
- What happens when a single-page order has its own "Total" row, as today? Behavior MUST be unchanged — a single page-block with no sibling sharing its Order Number continues to be treated as one complete order exactly as it already is.
- What happens when the same Order Number legitimately appears more than once across unrelated uploads (e.g., a re-upload of the same packing slip)? Unchanged by this fix — existing duplicate-order handling (feature 001) continues to apply exactly as it does today; this fix only changes how blocks are assembled within a single parse of one PDF.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST recognize multiple detected page-level order blocks that share the same order identifier as continuation pages of a single logical order, rather than as separate orders.
- **FR-002**: When continuation pages are recognized, the system MUST merge their product lines into one imported order, preserving the original page-by-page, top-to-bottom reading order of the lines.
- **FR-003**: The system MUST continue to treat page-level blocks with different order identifiers as separate, independent orders, exactly as it does today.
- **FR-004**: A page whose product lines genuinely cannot be extracted for a reason other than a missing continuation-page "Total" row MUST continue to be rejected with a clear, typed failure reason, consistent with existing safe-failure behavior for malformed order data.
- **FR-005**: The fix MUST be validated against the real, representative 3-page packing-slip sample provided for this feature, added as a project fixture consistent with feature 001's existing fixture-based validation approach.
- **FR-006**: Existing single-page import behavior, including every existing test fixture's expected result, MUST remain identical after this fix.

### Key Entities

- **Order**: The existing entity produced by packing-slip import (feature 001). This feature changes no field and adds no field — it corrects how product lines discovered across multiple PDF pages are assembled into the one order they actually belong to.
- **Order Line**: The existing per-product entity within an order. This feature does not change how an individual line's fields (product name, set, collector number, etc.) are extracted — only which order a line ends up attached to, and in what sequence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The provided real, sanitized 3-page packing-slip sample imports as a single order containing all 50 product lines from the source document, in source reading order.
- **SC-002**: 100% of existing packing-slip import test cases continue to pass with identical results after the fix.
- **SC-003**: Two genuinely distinct orders present in the same upload continue to import as two separate orders — zero instances of unrelated orders being incorrectly merged as a side effect of this fix.

## Assumptions

- The provided sanitized 3-page packing-slip PDF sample is representative of real multi-page TCGplayer packing-slip formatting (a repeated column header on every page, a grand-total row only on the final page, and the same Order Number printed on every page) and will be added as a project fixture for parser test validation (constitution: "Parser changes must be validated against representative fixtures").
- This is a defect correction to already-shipped, already-merged behavior (feature 001), not new functional scope: multi-page orders were always expected to import as one complete order; this fix restores that intended behavior rather than adding a new capability.
- A page's product-line rows are assumed to always belong to whichever order identifier appears on that same page — this fix does not need to handle a page that legitimately contains rows from more than one order.
- No new fields, entities, API changes, or UI changes are introduced. Card image enrichment (feature 009, in progress) is unaffected in scope by this fix, though real multi-page Pokémon orders become fully importable to exercise it against, once this fix lands.
