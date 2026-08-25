# Feature Specification: Order Picking Detail — Additional Packing-Slip Fields

**Feature Branch**: `008-picking-detail-additional-fields`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Add product line (game), rarity, collector number, and order status to the order picking detail view. The detail view currently (feature 007) displays only product name, set, variant, condition, and quantity for each order line, plus the TCGplayer order identifier. All of these additional fields already exist on the imported Order/OrderLine data from feature 001 (TCGplayer order import) but are not surfaced anywhere in the picking detail view. This feature adds them to the same read-only detail view: per product line, add the product line/game (e.g. \"Pokemon\", \"Magic\") and rarity (e.g. \"Double Rare\"; optional field, may be absent) alongside the existing identity fields, add collector number (e.g. \"#067/086\") as a clearly associated part of the card's identity since it disambiguates cards that share a product name within a set, and at the order level add the order's status (currently always \"Ready\" but should display whatever the true status is) alongside the existing TCGplayer order identifier. No new data collection, no catalog/image matching, no state-changing behavior — purely additional read-only display of already-imported fields, matching feature 007's existing read-only scope and \"no image is better than the wrong image\" posture (this feature adds no image)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See Every Line's Full Identity, Including Collector Number and Rarity (Priority: P1)

As a picker, I need to see each product line's game/product line, collector number, and rarity — in addition to the product name, set, variant, condition, and quantity the detail view already shows — so I can identify the exact card and printing to pick, even when a set contains multiple like-named cards or multiple print runs of the same card.

**Why this priority**: Picking the wrong specific printing of a same-named card is a direct picking error. Collector number and rarity are exactly the disambiguating details a picker needs when a set contains near-duplicate cards (e.g., two different-rarity versions of "Genesect ex" in the same set) — this is the core value of this feature.

**Independent Test**: Open the detail view for an order whose lines include a collector number and a rarity in the imported data, and confirm both are visible on the correct line alongside the existing fields.

**Acceptance Scenarios**:

1. **Given** an order with at least one product line, **When** a picker opens that order's detail view, **Then** each line displays its product line/game and collector number, in addition to the fields feature 007 already displays.
2. **Given** a product line whose rarity is present in the imported data, **When** the detail view renders that line, **Then** the rarity is visible on that line.
3. **Given** a product line whose rarity is not present in the imported data, **When** the detail view renders that line, **Then** the line displays cleanly without a blank or broken rarity field, consistent with how an absent variant is already handled.

---

### User Story 2 - See the Order's Status on Its Detail View (Priority: P2)

As a picker, I need to see the order's status directly on its detail view, so I don't have to return to a list view to confirm the order's current state.

**Why this priority**: Useful context, but every order currently reachable through this view is already known to be Ready (per feature 007's existing assumption, since claiming/picking hasn't been built yet), so this is lower-value than the line-level identity fields in User Story 1 until other statuses exist in practice.

**Independent Test**: Open any order's detail view and confirm its status is visible alongside its TCGplayer order identifier.

**Acceptance Scenarios**:

1. **Given** an order's detail view, **When** it renders, **Then** the order's status is visible alongside its existing TCGplayer order identifier.

---

### Edge Cases

- What happens when a product line's rarity is absent from the imported data? The line MUST still render cleanly, simply omitting the field, the same way an absent variant is already handled (feature 007 FR-009).
- What happens with a long product line/game name, collector number, or rarity value? The layout MUST remain legible and MUST NOT clip or hide information at either desktop or mobile width, consistent with feature 007's existing responsive requirement.
- Product line/game and collector number are required fields on every imported order line (per the existing order-import feature), so no "missing value" state is needed for them — only rarity is ever absent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: For every product line in the order, the detail view MUST display the product line/game (e.g., "Pokemon", "Magic").
- **FR-002**: For every product line in the order, the detail view MUST display the collector number exactly as imported (e.g., "#067/086").
- **FR-003**: For every product line in the order, the detail view MUST display the rarity when present in the imported data (e.g., "Double Rare"); when rarity is absent, the detail view MUST render that line without a blank or broken rarity field, rather than omitting it.
- **FR-004**: The detail view MUST display the order's status alongside its existing TCGplayer order identifier.
- **FR-005**: Displaying these additional fields MUST NOT introduce any action that claims the order, marks it picked, or otherwise changes its status or data — the detail view remains fully read-only, continuing feature 007's FR-006.
- **FR-006**: The detail view MUST continue to render correctly and remain fully usable, with all added fields visible and legible, at both a common desktop viewport width and a common mobile-phone viewport width, continuing feature 007's FR-007.

### Key Entities

- **Order**: The existing imported TCGplayer order (feature 001, displayed read-only since feature 007). This feature adds display of its existing `Status` field, which the detail view does not currently show.
- **Order Line**: The existing single card/product entry within an order (feature 001, displayed read-only since feature 007). This feature adds display of its existing `ProductLine` (game) and `CollectorNumber` fields (both already required on every line) and its existing `Rarity` field (optional — may be absent).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A picker can identify the exact game, collector number, and rarity of every product line in a given order entirely from its detail view, without consulting any other screen or system.
- **SC-002**: A picker can determine an order's status entirely from its detail view, without navigating back to a list view.
- **SC-003**: Across every order and every product line reviewed, a line with no recorded rarity always renders cleanly, with zero instances of a blank or broken rarity field.
- **SC-004**: The detail view, including all fields added by this feature, remains fully usable — all information visible, legible, and unclipped — at both a common desktop width and a common mobile-phone width.

## Assumptions

- This feature extends feature 007's existing read-only order picking detail view. It adds display fields only — no new data collection, no catalog/image matching, and no claiming/picking or other state-changing action, consistent with feature 007's explicit deferral of claiming to a separate future feature.
- `ProductLine`, `CollectorNumber`, and `Rarity` are already captured by the order-import feature (001) on every `OrderLine`. `ProductLine` and `CollectorNumber` are required on every line (per the existing domain model), so no "missing value" handling is needed for them; `Rarity` is optional, so it follows the same absent-field handling feature 007 already established for `Variant`.
- `Order.Status` already exists on every `Order`. Every order currently reachable through this view is Ready (per feature 007's existing assumption, since the claiming/picking workflow that would move an order to another status has not been built yet); this feature displays whatever status value is present without adding new status-specific behavior.
