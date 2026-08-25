# Feature Specification: Fix Duplicate Collector-Number Echo in Product Names

**Feature Branch**: `012-fix-duplicate-collector-number`

**Created**: 2026-08-25

**Status**: Draft

**Input**: User description: "Fix OrderLineExtractor (feature 001) so a redundant, bare (unprefixed) collector-number echo that TCGplayer sometimes prints immediately before the real, '#'-prefixed collector number in a product description is not absorbed into ProductName. Confirmed with a real, sanitized packing-slip sample (the same 3-page multi-page order used by feature 011): many Pokémon lines' raw descriptions genuinely contain the card's number twice, e.g. \"Pokemon - ME05: Pitch Black: Fomantis - 085/084 - #085/084 - Illustration Rare - Near Mint Holofoil\" — confirmed via direct inspection of the source PDF's word bounding-box positions (not a parsing artifact of our own code: '085/084' and '#085/084' are two distinct, separately-positioned text tokens genuinely present in the rendered PDF). Today, OrderLineExtractor takes every segment between the game name and the matched ('#'-prefixed) collector-number segment as the combined set+product-name span, so the bare echo becomes part of ProductName (\"Fomantis - 085/084\" instead of \"Fomantis\"). This doesn't cause an import rejection — the line still imports successfully — but the resulting ProductName is wrong, which was discovered because it silently breaks a downstream best-effort process (feature 009's card-image name verification, which correctly refused to display an image rather than guess). This is a defect in already-shipped feature 001 import behavior (the product's real name has never been \"Fomantis - 085/084\"), not new product scope. The fix must recognize when the segment immediately before the true, '#'-prefixed collector-number segment is an exact duplicate of it (same digits/slashes, without the '#'), and exclude that redundant segment entirely from both Set and ProductName, using this real order as validation across every affected line, without changing behavior for any line that doesn't have this duplicate (including the existing Lorcana and Pokémon fixtures, whose lines have no such echo)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Product Name No Longer Includes a Redundant Collector-Number Echo (Priority: P1)

As an employee viewing an order's picking detail, I need each product line's displayed name to be the card's actual name, so I can visually confirm I'm picking the right card instead of seeing a name with a stray, duplicated number tacked onto it.

**Why this priority**: This is the entire purpose of the fix. A wrong `ProductName` isn't just cosmetic — it's exactly the kind of "high-risk information" (card identity) this application's picking view is built to get right, and it silently defeats any process (today, feature 009's card-image matching; potentially others later) that depends on `ProductName` accurately naming the card.

**Independent Test**: Import the real 3-page sample (or its fixture, already present from feature 011) and confirm every line whose raw description contains a redundant bare collector-number echo produces a `ProductName` equal to the card's real name, with no trailing number.

**Acceptance Scenarios**:

1. **Given** a product line whose raw description reads `"Pokemon - ME05: Pitch Black: Fomantis - 085/084 - #085/084 - Illustration Rare - Near Mint Holofoil"`, **When** the order is imported, **Then** the resulting line has `Set = "ME05: Pitch Black"` and `ProductName = "Fomantis"` (not `"Fomantis - 085/084"`).
2. **Given** the same packing slip, **When** every other line with this same double-number shape is inspected (e.g. Manectric, Clefairy, Mega Clefable ex, Hitmontop, and the rest of the ~23 affected lines in this real order), **Then** each one's `ProductName` also has the redundant echo removed, with `Set` and every other field unaffected.

---

### User Story 2 - Existing Import Behavior Remains Unaffected (Priority: P1)

As the business owner, I need this fix to leave every already-working import case exactly as it works today, so correcting the duplicate-echo defect doesn't introduce a regression in lines that were already parsing correctly.

**Why this priority**: An already-shipped, already-tested capability regressing as a side effect of an unrelated fix would be a worse outcome than the original defect. This must be verified with the same rigor as the fix itself.

**Independent Test**: Re-run the existing packing-slip import test suite (Pokémon, Magic, and Lorcana fixtures, including feature 011's multi-page fixture's non-duplicated lines) after the fix and confirm every previously-passing case still produces byte-for-byte identical results.

**Acceptance Scenarios**:

1. **Given** the existing test fixtures used by features 001/010/011 (none of which have this duplicate-echo shape), **When** they are imported after this fix, **Then** every resulting line's `Set`, `ProductName`, and every other field are identical to before the fix.
2. **Given** a product line in the same real 3-page order whose raw description does *not* contain a duplicate echo (e.g. `"Meowth - #106/094"`, `"Blastoise ex - #030/142"`), **When** the order is imported, **Then** that line's `ProductName` is unaffected by this fix.

---

### Edge Cases

- What happens when the segment immediately before the true collector number looks number-like but is *not* an exact duplicate of it (e.g. a genuinely different number, or a card name that happens to contain digits)? It MUST NOT be treated as a redundant echo and MUST NOT be dropped — only an exact match (same digits/slashes, ignoring the leading `#`) is recognized as the echo, so nothing that isn't confirmed identical is ever silently discarded.
- What happens when dropping the redundant echo would leave nothing else between the game name and the collector number (i.e., the echo was the *only* content there)? The line MUST still be rejected as malformed (`FailureType.MissingProductName`), exactly as a line with no set/product content at all is already rejected today — this fix corrects a false *acceptance* of wrong data, it does not weaken rejection of genuinely malformed data.
- What happens when a line has no duplicate echo at all (the common case)? Behavior MUST be completely unchanged — the fix only ever activates when the specific duplicate shape is detected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST recognize when the segment immediately preceding a line's matched, `#`-prefixed collector-number segment is an exact duplicate of it (identical digits/slashes, without the `#`), and MUST exclude that segment entirely from the combined set-and-product-name text used to derive `Set` and `ProductName`.
- **FR-002**: When the redundant echo is excluded, the resulting `ProductName` MUST be the card's real name with no trailing duplicated number, and `Set` MUST be derived from the remaining text exactly as it already is today (unchanged colon-splitting logic).
- **FR-003**: A segment that merely resembles a number but is not an exact duplicate of the true collector number MUST NOT be treated as a redundant echo and MUST NOT be dropped.
- **FR-004**: If excluding the redundant echo leaves no remaining content between the game name and the collector number, the line MUST still be rejected with `FailureType.MissingProductName`, consistent with existing safe-failure behavior for malformed order data.
- **FR-005**: The fix MUST be validated against the real, representative 3-page packing-slip sample already present as a fixture (feature 011), across every line in it that exhibits this duplicate shape.
- **FR-006**: Existing import behavior for every line without this duplicate shape, across all existing test fixtures (Pokémon, Magic, Lorcana, and feature 011's non-duplicated lines), MUST remain identical after this fix.

### Key Entities

- **Order Line**: The existing entity produced by packing-slip import (feature 001). This feature changes no field and adds no field — it corrects how the existing `ProductName` (and, indirectly, `Set`) fields are derived when a redundant collector-number echo is present in the raw description.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every line in the real 3-page sample that has a redundant bare collector-number echo produces a `ProductName` matching the card's real name, with the echo removed, and a `Set` unaffected beyond that.
- **SC-002**: 100% of existing packing-slip import test cases (features 001, 010, 011) continue to pass with identical results after the fix.
- **SC-003**: Zero instances of a non-duplicate segment being incorrectly dropped as a false-positive echo — verified by an explicit boundary-condition test.

## Assumptions

- The provided real, sanitized 3-page packing-slip sample (already a project fixture from feature 011, `multi-page-order-no-total-on-continuation-pages.pdf`) is representative of this duplicate-echo defect and is reused as this feature's validation evidence — no new fixture is needed.
- This is a defect correction to already-shipped, already-merged behavior (feature 001), not new functional scope: a product's real name was never meant to include a duplicated collector number: this fix restores the import behavior that was always intended, rather than adding a new capability.
- The redundant echo is recognized only by exact-match comparison against the confirmed, `#`-prefixed collector number already found by the existing, unchanged collector-number detection logic — no new independent number-detection heuristic is introduced.
- No new fields, entities, API changes, or UI changes are introduced. This fix directly benefits feature 009 (card image enrichment, in progress) by making the majority of this real order's Pokémon lines' names match their catalog provider's canonical card name, but feature 009's own matching logic and Pokémon provider are unaffected in scope by this fix.
