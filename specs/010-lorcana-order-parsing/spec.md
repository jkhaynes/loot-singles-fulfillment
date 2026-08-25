# Feature Specification: Fix Lorcana Order Line Parsing

**Feature Branch**: `010-lorcana-order-parsing`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Fix the TCGplayer packing-slip order line parser (feature 001, OrderLineExtractor) so it correctly imports Disney Lorcana order lines instead of rejecting them. A real, sanitized TCGplayer packing-slip sample shows Lorcana product descriptions are formatted as \"Lorcana TCG - {Set} - {Card Name} - #{CollectorNumber} - {Rarity} - {Condition}{Variant}\", for example: \"Lorcana TCG - Disney Lorcana Promo Cards - Scrooge McDuck - S.H.U.S.H. Agent - #36 - Promo - Near Mint Holofoil\". Lorcana card names routinely contain their own \" - \" separator as part of the canonical card name itself (e.g. \"Scrooge McDuck - S.H.U.S.H. Agent\" is one card's full name, not a set/product pair) — a completely different shape from Pokemon's colon-delimited \"{Set}: {Card Name}\" format the parser currently assumes. Today, importing this real, valid order line fails with a \"missing product name\" rejection (OrderLineExtractor requires a colon to split set from product name, and this Lorcana line has none), even though the order data itself is entirely valid. This is a defect in already-shipped import behavior, not new product scope: the PRD and feature 001 already list Disney Lorcana as a supported V1 game, so packing slips for it are expected to import successfully today, not be rejected as malformed. The fix must correctly extract Set, ProductName (preserving any internal \" - \" it may contain), CollectorNumber, Rarity, Condition, and Variant for Lorcana lines using this real sample as validation, without breaking the existing, already-tested Pokemon parsing behavior or introducing incorrect behavior for Magic: The Gathering lines (which also have no confirmed real sample yet, so the fix should be evaluated for whether it's safe for Magic's format too, without assuming Magic definitely has the same quirk as Lorcana). This is strictly a parsing-correctness fix within feature 001's existing import pipeline — it does not add new fields, new games, new UI, or any new functional scope beyond correctly importing data that should already import successfully."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lorcana Orders Import Successfully (Priority: P1)

As an employee importing a TCGplayer packing slip containing Disney Lorcana cards, I need the order to import successfully with the correct card details, so the order appears correctly in the picking queue instead of being incorrectly rejected as malformed.

**Why this priority**: This is the entire purpose of the fix. Disney Lorcana is an already-committed V1 game per the PRD; an order containing Lorcana cards currently cannot be imported at all, which is a functional defect blocking real business use, not a missing enhancement.

**Independent Test**: Import the real, sanitized sample packing slip (or an equivalent fixture built from it) containing a Lorcana line whose card name contains its own " - " separator, and confirm the resulting order line has the correct set, card name, collector number, rarity, condition, and variant — with no rejection.

**Acceptance Scenarios**:

1. **Given** a packing slip containing the line "Lorcana TCG - Disney Lorcana Promo Cards - Scrooge McDuck - S.H.U.S.H. Agent - #36 - Promo - Near Mint Holofoil", **When** the order is imported, **Then** the resulting order line has set "Disney Lorcana Promo Cards", card name "Scrooge McDuck - S.H.U.S.H. Agent" (with its internal " - " preserved as part of the name, not treated as a field separator), collector number "#36", rarity "Promo", condition "Near Mint", and variant "Holofoil".
2. **Given** the same packing slip, **When** the order is imported, **Then** the import succeeds with no rejection or failure code for that line.

---

### User Story 2 - Existing Game Parsing Remains Unaffected (Priority: P1)

As the business owner, I need this fix to leave Pokémon order parsing exactly as it already works today, so correcting the Lorcana defect doesn't introduce a new regression in a game that already imports correctly.

**Why this priority**: An already-shipped, already-tested capability regressing as a side effect of an unrelated fix would be a worse outcome than the original defect. This must be verified with the same rigor as the fix itself.

**Independent Test**: Re-run the existing Pokémon import test suite after the fix and confirm every previously-passing case still produces byte-for-byte identical results.

**Acceptance Scenarios**:

1. **Given** the existing Pokémon packing-slip fixtures used by feature 001's test suite, **When** they are imported after this fix, **Then** every resulting order line's set, card name, collector number, rarity, condition, and variant are identical to before the fix.

---

### Edge Cases

- What happens when a Lorcana card name contains more than one " - " occurrence (e.g., a hypothetical three-part name)? All parts MUST be preserved together as the single card name, not split or truncated.
- What happens when a Lorcana line has no variant, only a condition (e.g., "Near Mint" with no trailing variant text)? Condition/variant extraction MUST continue to work exactly as it already does today for every other game — this fix changes only how set and card name are separated, not condition/variant parsing.
- What happens when a Lorcana line is genuinely malformed (e.g., missing a collector number entirely)? It MUST still be rejected with a clear, typed failure reason, exactly as any other malformed line is today — this fix corrects a false-positive rejection of valid data, not the underlying defensive validation itself.
- What happens with a Magic: The Gathering line? Its behavior MUST NOT change as a result of this fix. No real Magic packing-slip sample has been confirmed yet, so this feature does not assume Magic shares Lorcana's card-name quirk and does not attempt to fix a Magic-specific case that hasn't been observed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST successfully import a Lorcana order line whose product description follows the pattern "{Game} - {Set} - {Card Name} - #{Collector Number} - {Rarity} - {Condition}{Variant}", correctly extracting each field — including when the card name itself contains its own " - " separator, as demonstrated by the representative sample ("Scrooge McDuck - S.H.U.S.H. Agent").
- **FR-002**: The system MUST continue to correctly import Pokémon order lines using the existing colon-delimited set/card-name format, with results identical to before this fix.
- **FR-003**: A malformed Lorcana line (e.g., one missing a collector number) MUST continue to be rejected with a clear, typed failure reason, consistent with the existing safe-failure behavior for malformed order data — this fix corrects a false-positive rejection of valid data, it does not weaken defensive validation of genuinely invalid data.
- **FR-004**: The fix MUST be validated against the real, representative Lorcana packing-slip sample provided for this feature, added as a project fixture consistent with feature 001's existing fixture-based validation approach.
- **FR-005**: The fix MUST NOT change Magic: The Gathering's existing parsing behavior. Since no real Magic packing-slip sample has been confirmed, this feature does not assume Magic shares Lorcana's card-name quirk and leaves Magic's behavior exactly as it is today.

### Key Entities

- **Order Line**: The existing entity produced by packing-slip import (feature 001). This feature changes no field, adds no field, and adds no new entity — it corrects how the existing `Set`, `ProductName`, `CollectorNumber`, `Rarity`, `Condition`, and `Variant` fields are populated for one game's already-supported format.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The provided real, sanitized Lorcana packing-slip sample imports successfully, producing an order line whose set, card name, collector number, rarity, condition, and variant exactly match the values evident in the sample.
- **SC-002**: 100% of existing Pokémon import test cases continue to pass with identical results after the fix.
- **SC-003**: A malformed Lorcana line (e.g., missing collector number) is still rejected with the correct existing failure classification — zero instances of malformed data being silently accepted as a side effect of this fix.

## Assumptions

- The provided sanitized packing-slip PDF sample is representative of real Disney Lorcana packing-slip formatting and will be added as a project fixture for parser test validation (constitution: "Parser changes must be validated against representative fixtures").
- This fix targets Disney Lorcana specifically. Magic: The Gathering's real packing-slip format remains unconfirmed; a similar fix for Magic, if one turns out to be needed, is deferred to a future feature once a real Magic sample is available — this is a deliberate scope boundary, not an oversight.
- This is a defect correction to already-shipped, already-merged behavior (feature 001), not new functional scope: Disney Lorcana was already a committed V1 game per the PRD (§30) before this fix. This feature restores the import behavior that was always intended for it rather than adding a new capability.
- No new fields, entities, API changes, or UI changes are introduced. Card image enrichment (feature 009, in progress) is unaffected in scope by this fix, though it benefits once this fix lands, since Lorcana orders will actually be importable to exercise that feature's Lorcana user story against real data.
