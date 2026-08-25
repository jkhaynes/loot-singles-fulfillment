# Feature Specification: Card Image Enrichment for Order Picking Detail

**Feature Branch**: `009-card-image-enrichment`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Add card image enrichment to the order picking detail view, for Pokémon, Magic: The Gathering, and Disney Lorcana in this phase (One Piece is explicitly deferred — its catalog provider is still an open discovery item per the PRD, not ready for implementation). For each product line on the detail view (feature 007/008), attempt to resolve a confident match against the appropriate game-specific external card catalog provider — Pokémon TCG API for Pokémon, Scryfall for Magic, Lorcast for Lorcana — using the game identified from the line's existing ProductLine field, and if a sufficiently confident match is found, display that card's real image in place of today's neutral placeholder. Matching must follow the PRD's conservative algorithm: normalize the imported set information, prefer an exact match on set plus collector number, verify the returned card name, and validate variant/printing information where possible; accept the match only when sufficiently confident, never on name-only fuzzy matching. This is a hard product safety rule, not a preference: no image is better than the wrong image — when no match is found, multiple plausible matches exist, or confidence is insufficient, the view must fall back to today's existing neutral \"no image\" placeholder exactly as it already works today (feature 007 already built and tested this fallback; it must not be changed by this feature), and the underlying textual identifying information (product name, set, collector number, rarity, variant, condition) must always remain visible regardless of whether an image was found. The design must be built as a provider abstraction (an adapter per game, mirroring how packing-slip import already isolates TCGplayer-specific parsing from the rest of the domain) so that adding a future game/provider (e.g. One Piece once a provider is selected) requires adding one new adapter and does not require redesigning the picking domain, the API contract, or the frontend. Catalog data is strictly supplemental and must never overwrite or be treated as more authoritative than the imported TCGplayer order data — if a provider's data disagrees with the imported order, the imported order still wins and is still what's displayed as text. Image resolution happens as a live, on-demand lookup each time the order detail view is requested (extending the existing GET /api/orders/{orderId} response with the resolved image URL per line) — no local caching or persistence of catalog data in this phase; that is an explicit, deliberate deferral, not an oversight. This feature's scope is the order picking detail view only — Browse Orders and the Dashboard's Available Orders list are explicitly out of scope and must remain text-only exactly as they are today. If a provider is temporarily unavailable or a request to it fails, the feature must fail safe to the same \"no image\" placeholder rather than erroring the whole detail view."

## Clarifications

### Session 2026-08-24

- Q: Should the order detail view display an attribution credit for whichever external provider supplied a card's image, or is no attribution needed since this is an internal, employee-only tool? → A: No attribution needed — this is an internal, non-public tool.

### Session 2026-08-25

- Q: For V1, how should a game's catalog provider handle a packing-slip `Set` value that doesn't exactly, literally match the provider's set name (e.g. TCGplayer's Pokémon `Set` text `"SV: Black Bolt"` vs the Pokémon TCG API's set name `"Black Bolt"`)? This resolves PRD §41 Q29. → A: Before querying, strip a recognized series-abbreviation prefix (e.g. `"SV: "`, `"SV10: "`, `"ME05: "`) from the start of the `Set` text when present, using a small, Product Owner-supplied table of series abbreviations (e.g. `"SV"` → `"Scarlet & Violet"`) that only needs updating when Pokémon starts an entirely new naming era (infrequent — not per new set release, unlike a per-set/per-group table); query using the remaining text after stripping. When no known abbreviation prefix is recognized, query the raw `Set` text unchanged exactly as today. This is internal to the catalog provider only — used solely to build a better provider query — and does not add, persist, or display any new field on the order line. Capturing Series/Set Number as their own persisted, displayed order-line fields (e.g. for a product like `"ME05: Pitch Black"` → series "Mega Evolution", set number "5") is real, useful information but is explicitly out of scope for this feature and deferred to a separate future feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See a Pokémon Card's Real Image When Confidently Matched (Priority: P1)

As a picker, I need the order detail view to show a Pokémon product line's real card image when the system can confidently identify the exact card and printing, so I can visually confirm I'm picking the right physical card instead of relying on text alone.

**Why this priority**: This is the entire value of the feature and its first proof point. Without a working confident-match path for at least one game, the feature delivers nothing beyond what feature 007 already built.

**Independent Test**: Open the detail view for an order containing a Pokémon product line whose set and collector number exactly identify a known card, and confirm that card's real image is displayed in place of the neutral placeholder, with all of its textual fields still visible.

**Acceptance Scenarios**:

1. **Given** a Pokémon product line whose set and collector number exactly identify one card, **When** a picker opens that order's detail view, **Then** that card's real image is displayed for that line, and its product name, set, collector number, rarity, variant, and condition remain fully visible.
2. **Given** the same order, **When** the picker reopens the detail view later, **Then** the same correct image is displayed again for that line.

---

### User Story 2 - Never See a Wrong or Guessed Card Image (Priority: P1)

As the business owner, I need the picking detail view to never display an image it isn't confident about — whether no match was found, multiple cards plausibly matched, or a catalog source could not be reached — so a picker is never misled into picking the wrong card because the screen showed an unverified or incorrect picture.

**Why this priority**: An incorrect card image can directly cause a picking error. This is a hard safety rule for the product, and it governs how the entire matching mechanism this feature introduces is allowed to fail — it must be verified as rigorously as the confident-match path in User Story 1.

**Independent Test**: Open the detail view for an order containing product lines that cannot be confidently matched (no match, an ambiguous/multiple-candidate match, or a simulated catalog-source failure) and confirm every one of those lines shows the existing neutral placeholder — never a guessed image — while its textual information remains fully visible.

**Acceptance Scenarios**:

1. **Given** a product line for which no catalog match is found, **When** the detail view renders that line, **Then** the neutral placeholder is displayed, not an image.
2. **Given** a product line for which more than one plausible catalog match exists, **When** the detail view renders that line, **Then** the neutral placeholder is displayed, not a best-guess image.
3. **Given** a product line whose game's catalog source is temporarily unreachable, **When** the picker opens the order's detail view, **Then** the view still loads successfully, that line shows the neutral placeholder, and no error is shown to the picker.
4. **Given** a product line for a game not yet supported by this feature (e.g., One Piece), **When** the detail view renders that line, **Then** the neutral placeholder is displayed exactly as it is today.

---

### User Story 3 - See a Magic: The Gathering Card's Real Image When Confidently Matched (Priority: P2)

As a picker, I need the same confident-match image behavior proven for Pokémon in User Story 1 to also work for Magic: The Gathering product lines, so Magic orders get the same picking-accuracy benefit.

**Why this priority**: Extends proven value to a second game. Lower priority than User Story 1 only because it depends on the matching mechanism User Story 1 establishes; it does not introduce new product behavior beyond applying that mechanism to a second game's catalog source.

**Independent Test**: Open the detail view for an order containing a Magic product line whose set and collector number exactly identify a known card, and confirm that card's real image is displayed, with all of its textual fields still visible.

**Acceptance Scenarios**:

1. **Given** a Magic product line whose set and collector number exactly identify one card, **When** a picker opens that order's detail view, **Then** that card's real image is displayed for that line, and its textual fields remain fully visible.

---

### User Story 4 - See a Disney Lorcana Card's Real Image When Confidently Matched (Priority: P3)

As a picker, I need the same confident-match image behavior to also work for Disney Lorcana product lines, so Lorcana orders get the same picking-accuracy benefit.

**Why this priority**: Extends proven value to a third game. Sequenced last since it depends on the same mechanism already proven twice; no new product behavior is introduced.

**Independent Test**: Open the detail view for an order containing a Lorcana product line whose set and collector number exactly identify a known card, and confirm that card's real image is displayed, with all of its textual fields still visible.

**Acceptance Scenarios**:

1. **Given** a Lorcana product line whose set and collector number exactly identify one card, **When** a picker opens that order's detail view, **Then** that card's real image is displayed for that line, and its textual fields remain fully visible.

---

### Edge Cases

- What happens when a set contains multiple printings/variants of a card with the same name? The system MUST NOT display an image for one printing when the expected printing (per the imported variant/condition information) cannot be confidently distinguished from another.
- What happens when a catalog source's data disagrees with the imported TCGplayer order data (e.g., a different card name for that set/collector-number combination)? The imported order's textual data remains what is displayed; the discrepancy must not cause an image to be shown for the wrong card, and must not alter any displayed textual field.
- What happens when a product line belongs to a game not covered by this phase (e.g., One Piece)? It displays the existing neutral placeholder, exactly as every product line does today, without attempting a catalog lookup.
- What happens when the same order's detail view is opened more than once? Each view independently resolves the same result for the same underlying data — the picker never sees a different outcome (image vs. no image) for the same line across repeated views without the underlying order data having changed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: For every product line in the order detail view belonging to Pokémon, Magic: The Gathering, or Disney Lorcana, the system MUST attempt to resolve that line's card against that game's designated external card catalog.
- **FR-002**: The system MUST display an image for a product line only when it can associate that image with the expected card and printing with sufficient confidence, based on the line's set and collector number, verified against the returned card's name and, where obtainable, its variant/printing information.
- **FR-003**: The system MUST NOT display an image selected merely because it is the closest or most likely match, MUST NOT substitute an image from a same-named card in a different set, and MUST NOT select among multiple plausible matches.
- **FR-004**: When no match is found, when multiple plausible matches exist, when confidence is insufficient, or when a game's catalog source cannot be reached, the product line MUST display the same neutral placeholder already used today for every line (feature 007) — this is a hard safety rule, not merely a fallback behavior.
- **FR-005**: A product line's imported textual identifying information (product name, set, collector number, rarity, variant, condition) MUST remain fully visible regardless of whether an image was displayed for that line.
- **FR-006**: Catalog-sourced information MUST NOT overwrite, replace, or be treated as more authoritative than the imported TCGplayer order data; the imported order's textual fields remain what is displayed even when a catalog source's data for a matched card differs.
- **FR-007**: The system MUST be able to support an additional trading card game in the future by adding that game's catalog source, without changing the picking behavior already verified for Pokémon, Magic: The Gathering, or Disney Lorcana.
- **FR-008**: This feature applies only to the order picking detail view; the dashboard's Available Orders list and the Browse Orders view MUST continue to display order information exactly as they do today, without card images.
- **FR-009**: A temporarily unavailable or failing catalog source MUST NOT prevent the order detail view from loading; the affected product line(s) MUST fall back to the neutral placeholder rather than the view failing or showing an error.
- **FR-010**: A product line belonging to a game not yet supported by this feature (e.g., One Piece Card Game) MUST continue to display the neutral placeholder exactly as it does today, without a catalog lookup being attempted for it.
- **FR-011**: Before querying a catalog provider by set, the system MUST normalize the imported `Set` text by stripping a recognized series-abbreviation prefix (e.g. `"SV: "`, `"SV10: "`) from its start when one is present, using a maintainable table of known series abbreviations; when no known prefix is recognized, the raw `Set` text MUST be used unchanged, exactly as today. This normalization is used only to build the provider query and MUST NOT add, persist, or display any new field on the order line (Clarifications, 2026-08-25).

### Key Entities

- **Order Line**: The existing single card/product entry within an order (feature 001, displayed read-only since feature 007/008). This feature adds, for lines in a supported game, an associated card image when a confident match exists — the image is a display-time association, not new order data.
- **Card Catalog Match**: A conceptual, non-persisted result of checking a product line against its game's external catalog source: either a confidently identified card (with its image) or no confident result. Never itself treated as authoritative order data.
- **Card Catalog Source**: A conceptual, game-specific external source of card/printing information and imagery. Each source serves exactly one game in this phase: Pokémon TCG API (Pokémon), Scryfall (Magic: The Gathering), Lorcast (Disney Lorcana). One Piece Card Game has no designated source in this phase.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a product line whose card can be confidently identified, the correct card image for that specific printing is displayed, demonstrated across representative examples from all three phase-1 games (Pokémon, Magic: The Gathering, Disney Lorcana).
- **SC-002**: Across every order and every product line reviewed, zero images are ever displayed for an unconfident, ambiguous, unreachable, or absent catalog match — the existing neutral placeholder appears in their place every time.
- **SC-003**: A product line's textual identifying information is fully visible and unaffected by whether an image was displayed, verified across both matched and unmatched lines.
- **SC-004**: A picker can open and fully use an order's detail view even when a game's catalog source is unreachable, with no visible error and only that game's lines affected.
- **SC-005**: Supporting an additional trading card game does not require changing the picking behavior already verified for Pokémon, Magic: The Gathering, or Disney Lorcana.

## Assumptions

- Card images are resolved live from each game's external catalog source each time the order detail view is requested. Catalog match results are not cached or persisted in this phase — a deliberate, explicit deferral (not an oversight), to be revisited later only if real usage or provider reliability needs justify it.
- This feature's scope is the order picking detail view only. The dashboard's Available Orders list and the Browse Orders view are unchanged by this feature and remain text-only, consistent with how they already work today.
- The three phase-1 games' catalog sources are Pokémon TCG API (Pokémon), Scryfall (Magic: The Gathering), and Lorcast (Disney Lorcana), per the PRD's provider research (PRD §31). One Piece Card Game is explicitly deferred: a suitable catalog source has not yet been selected for it (PRD §31, §42 Priority 1), and product lines for it continue to show the existing neutral placeholder.
- A product line's game is determined from data the order-import feature (001) already captures on every line; this feature does not change import behavior, collect new data, or modify any existing order/order-line field.
- This feature reuses the order detail view's existing authentication and authorization; no new access policy is introduced.
- "Confident" matching follows the PRD's conservative approach (PRD §32): preferring an exact match on set and collector number, verified against the card's name and, where possible, its variant/printing information — never accepting a match based on name similarity alone.
- No attribution or credit is displayed for whichever external catalog source supplied a card's image (Clarifications, 2026-08-24). This tool is internal and employee-only — images are never shown to customers or the public — so no attribution UI is required, resolving PRD §41 Q32's provider-terms question for this specific concern.
- Set-name normalization (Clarifications, 2026-08-25) is a catalog-provider-internal prefix-stripping step only, against a small table of known Pokémon series abbreviations supplied by the Product Owner (e.g. `"SV"` → `"Scarlet & Violet"`) — deliberately chosen over a per-set/per-group table so it only needs updating when an entirely new series/era launches, not with every new set release. It resolves PRD §41 Q29 for the purpose of building a correct provider query. Capturing Series and Set Number as their own persisted, displayed order-line fields is a separate, future feature — out of scope here; this feature does not change feature 001's import parsing, add any order-line column, or change what the picking detail view (007/008) displays.
