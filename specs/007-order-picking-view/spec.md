# Feature Specification: Order Picking Detail View

**Feature Branch**: `007-order-picking-view`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Add a read-only order detail screen for the picking workflow. Given an order in Ready status, show its full detail: TCGplayer order identifier, and for each product line the card identity, set, variant, and quantity — with quantity greater than one given strong, unmistakable visual emphasis, since picking the wrong quantity is a high-risk error. Since card image enrichment is out of scope for this feature (deferred to a later feature), display a neutral placeholder in place of the card image for every line — never a guessed or low-confidence image. This detail view must be reachable from every place an order is listed in the UI (currently Browse Orders and the dashboard's Available Orders list). This feature is read-only: no claiming, no marking an order as picked, and no other state changes — those become a separate follow-up feature. Must work responsively on both desktop and mobile phone per the single responsive PWA requirement."

## Clarifications

### Session 2026-08-24

- Q: Should the "quantity greater than one" emphasis rely on color alone, or must it also be distinguishable without color? → A: Must also be distinguishable without color — emphasis MUST combine color with a non-color cue (e.g., bold weight, an icon/badge, or a "×N" style marker), since relying on color alone risks a colorblind picker missing a high-risk quantity and defeats the purpose of this requirement.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View an Order's Full Picking Detail (Priority: P1)

As a picker, I need to open a single order and see everything I need to identify and pick every card in it — its order identifier, and for each line the card's identity, set, variant, condition, and quantity — so I can pick that order correctly without consulting any other system.

**Why this priority**: This is the entire value of the feature. Without it, pickers still have to work from the printed TCGplayer invoice or the database directly, which is the exact problem this product replaces.

**Independent Test**: Open the detail view for an order with multiple product lines and confirm every line's card identity, set, variant, condition, and quantity is visible and correctly matches the imported order data, without navigating anywhere else.

**Acceptance Scenarios**:

1. **Given** an order in Ready status with multiple product lines, **When** a picker opens that order's detail view, **Then** the view shows the order's TCGplayer identifier and, for every line, the card identity, set, variant (when present), condition, and quantity.
2. **Given** a product line whose variant is not present in the imported data, **When** the detail view renders that line, **Then** the line displays cleanly without a blank or broken variant field.

---

### User Story 2 - Reach Order Detail from Anywhere Orders Are Listed (Priority: P1)

As a picker browsing orders on the dashboard or in Browse Orders, I need to open any listed order's detail directly from that list, so I don't have to look up or type an order identifier myself.

**Why this priority**: A detail view that exists but isn't reachable from the screens pickers actually use provides no real value. This makes User Story 1 usable in practice.

**Independent Test**: From the dashboard's Available Orders list and separately from Browse Orders, select any listed order and confirm it opens that exact order's detail view.

**Acceptance Scenarios**:

1. **Given** the dashboard's Available Orders list, **When** a picker selects an order in it, **Then** that order's detail view opens.
2. **Given** the Browse Orders view, **When** a picker selects an order in it, **Then** that order's detail view opens.

---

### User Story 3 - Never See a Guessed Card Image (Priority: P1)

As the business owner, I need the picking detail view to never show a guessed or low-confidence card image, so a picker is never misled into picking the wrong card because the screen showed a picture that wasn't actually verified.

**Why this priority**: An incorrect card image can directly cause a picking error. This is a hard safety rule for the product, not a cosmetic choice, and it governs this feature's only visual placeholder for card art.

**Independent Test**: Open the detail view for any order and confirm every product line shows the same neutral placeholder in place of a card image, with no image ever sourced from a guess or partial match.

**Acceptance Scenarios**:

1. **Given** any order's detail view, **When** it renders each product line, **Then** a neutral placeholder appears where the card image would go, for every line, with no exceptions.

---

### User Story 4 - Use the Detail View on a Phone (Priority: P2)

As a picker working from a mobile phone on the floor, I need the order detail view to be fully usable at phone width, so I'm not forced to use a desktop to pick.

**Why this priority**: Important for real floor usage, but the desktop experience alone already delivers the feature's core value, so this can follow once the layout itself is proven.

**Independent Test**: Open an order's detail view at a common mobile-phone viewport width and confirm all information is visible and legible without horizontal scrolling or overlapping content.

**Acceptance Scenarios**:

1. **Given** an order's detail view, **When** it is viewed at a common mobile-phone width, **Then** every product line's information remains fully visible and legible without horizontal scrolling.

---

### Edge Cases

- What happens when a picker navigates to a detail view for an order identifier that does not exist (e.g., a stale link or a manually edited URL)? The view MUST show a clear, safe "not found" state rather than an unhandled error.
- What happens when a product line has no variant recorded? The line MUST still render cleanly, simply omitting the field rather than showing an empty or broken placeholder for it.
- What happens with an order that has many product lines, or lines with unusually long card names or set names? The layout MUST remain legible and MUST NOT clip or hide information at either desktop or mobile width.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a detail view for a single order that displays that order's TCGplayer order identifier.
- **FR-002**: For every product line in the order, the detail view MUST display the card identity (product name), set, variant (when present), condition, and quantity.
- **FR-003**: The detail view MUST visually emphasize any product line whose quantity is greater than one, so it is unmistakable at a glance and clearly distinguished from quantity-one lines. This emphasis MUST combine color with at least one non-color cue (e.g., bold weight, an icon/badge, or a distinct quantity marker) so the emphasis remains distinguishable for a picker who cannot perceive the color difference.
- **FR-004**: The detail view MUST display a neutral placeholder in place of a card image for every product line, and MUST NOT display a guessed, ambiguous, or low-confidence card image under any circumstance.
- **FR-005**: Every order shown in the dashboard's Available Orders list and in the Browse Orders view MUST be selectable to navigate directly to that order's detail view.
- **FR-006**: The detail view MUST be read-only: it MUST NOT expose any action that claims an order, marks it picked, or otherwise changes its status or data.
- **FR-007**: The detail view MUST render correctly and remain fully usable at both a common desktop viewport width and a common mobile-phone viewport width.
- **FR-008**: When a detail view is requested for an order identifier that does not exist, the system MUST present a clear, safe "not found" state rather than failing unhandled.
- **FR-009**: When a product line's optional field (variant) is absent from the imported data, the detail view MUST render that line without that field rather than showing an empty or broken placeholder for it.

### Key Entities

- **Order**: An imported TCGplayer order being viewed read-only. Identified by its TCGplayer order identifier; contains one or more product lines. Already exists from the order-import feature — this feature only displays it.
- **Order Line**: A single card/product entry within an order. Carries the card identity, set, variant, condition, and quantity this view renders. Already exists from the order-import feature — this feature only displays it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A picker can identify every product line's card identity, set, variant, and quantity for a given order entirely from its detail view, without consulting any other screen or system.
- **SC-002**: In review, testers correctly distinguish every product line with quantity greater than one from quantity-one lines at a glance, with zero misidentifications — including when the review is done with color perception simulated away (e.g., a grayscale or color-blindness-simulated view).
- **SC-003**: Every order visible in the dashboard's Available Orders list or in Browse Orders can be opened into its detail view in a single interaction.
- **SC-004**: Across every order and every product line reviewed, zero guessed or low-confidence card images ever appear; a neutral placeholder appears in their place every time.
- **SC-005**: The detail view is fully usable — all information visible, legible, and unclipped — at both a common desktop width and a common mobile-phone width.

## Assumptions

- Card image enrichment and catalog matching are out of scope for this feature and are deferred to a later feature; a neutral placeholder is used for every product line in the meantime. This is consistent with the product's existing "no image is better than the wrong image" rule for unconfident matches.
- Claiming an order, marking it picked, and any other action that changes order state are out of scope for this feature and are deferred to a follow-up feature. This feature is read-only.
- The only two places an order is currently listed in the product are the dashboard's Available Orders list and the Browse Orders view; this feature makes the detail view reachable from both. No other order-listing surface exists in the product today.
- The order and product-line data this view displays (card identity, set, variant, condition, quantity) already exists from the order-import feature; this feature does not collect or modify any new data.
- Every order currently in the system is in Ready status, since the claiming/picking workflow that would move an order to another status has not been built yet; behavior for non-Ready statuses is not a concern for this feature and is left to the follow-up claiming feature.
