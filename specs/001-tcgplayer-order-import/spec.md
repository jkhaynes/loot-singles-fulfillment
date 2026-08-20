# Feature Specification: TCGplayer Packing Slip Order Import

**Feature Branch**: `001-tcgplayer-order-import`

**Created**: 2026-08-19

**Status**: Draft

**Input**: User description: "TCGplayer Packing Slip Order Import: parse the TCGplayer Packing Slip PDF for an order into validated Order and Order Line records (order identifier, product line/game, product name, set, card/collector number, rarity, condition, quantity, and variant information where present in the description), so that imported orders become available with the Order → Product Lines → Quantity relationship preserved exactly as the PDF describes. Parsing must be defensive: if the importer cannot confidently reconstruct the expected order and line-item data (missing order identifier, missing product lines, invalid/unparseable quantities, or other structural problems), it must reject the import and surface the problem rather than silently create an incomplete or incorrect order. Imported TCGplayer data is authoritative and must be preserved exactly, including quantity greater than one. This feature covers PDF import and persistence of Order + Order Line records only -- it does not include picker-facing UI, order claiming/picking workflow, or card catalog/image enrichment, which are separate future features. Customer shipping PII present in the packing slip should not be persisted beyond what is needed for picking and order identification, per data minimization."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import a Valid Packing Slip (Priority: P1)

An authorized Loot operator provides a TCGplayer Packing Slip PDF to the system. The system parses it and makes the Order(s) it contains, with all of their Order Lines and exact quantities, available for the rest of the fulfillment workflow to use.

**Why this priority**: Without this, no order data exists anywhere in the system — every other V1 capability (picker login, order queue, guided picking) has nothing to operate on. This is the minimum slice that delivers standalone value: proof that Loot's real packing slips can be turned into trustworthy structured data.

**Independent Test**: Supply a representative, valid packing slip PDF (one containing one or more orders with single- and multi-quantity product lines). Confirm that for each order, the resulting Order record has the correct TCGplayer order identifier and that every Order Line matches the source document's product, set, card/collector number, condition, variant text, and quantity exactly — with no lines added, dropped, or merged.

**Acceptance Scenarios**:

1. **Given** a packing slip PDF containing one order with several product lines including a quantity of 1, **When** it is imported, **Then** an Order is created with one Order Line per product, each with the correct quantity of 1.
2. **Given** a packing slip PDF containing an order with a product line requesting more than one copy of the same card, **When** it is imported, **Then** the resulting Order Line's quantity exactly matches the requested quantity (e.g., 4), with no rounding or substitution.
3. **Given** a packing slip PDF containing multiple distinct orders, each with their own product lines, **When** it is imported, **Then** each Order Line remains associated with its original order only — no product line from one order appears under a different order.
4. **Given** a packing slip PDF containing multiple orders where one has a structural problem (e.g., a missing order identifier) and the others are valid, **When** it is imported, **Then** the valid orders are imported successfully while only the invalid order is rejected.

---

### User Story 2 - Reject an Unparseable Packing Slip (Priority: P1)

An operator provides a packing slip PDF that the system cannot confidently reconstruct into complete order and line-item data (for example, the document is missing an order identifier, contains no product lines, or has a quantity that cannot be read as a valid number). The system rejects the affected order rather than creating an incomplete or incorrect record, and tells the operator specifically what could not be parsed.

**Why this priority**: This is the safety behavior that makes the feature trustworthy at all. Silently creating a wrong or incomplete order is worse than not importing it, because a picker would later act on bad data without knowing it. This must exist alongside the happy path before the feature can be used for real fulfillment.

**Independent Test**: Supply a packing slip PDF engineered to be missing an order identifier, to contain an order with zero product lines, and to contain a product line with an unreadable quantity. Confirm that no Order or Order Line is created for any of these cases, and that each rejection is reported with a specific, human-readable reason rather than a generic failure.

**Acceptance Scenarios**:

1. **Given** a packing slip PDF with no discoverable order identifier, **When** it is imported, **Then** no Order is created and the operator is shown a specific reason describing what could not be found.
2. **Given** a packing slip PDF whose order contains zero product lines, **When** it is imported, **Then** no Order is created for that order, and the operator is shown a specific reason.
3. **Given** a packing slip PDF whose order contains a product line with a blank, zero, negative, or non-numeric quantity, **When** it is imported, **Then** no Order Line is created from that malformed data, and the operator is shown a specific reason identifying the affected product line.
4. **Given** a rejected import, **When** the operator reviews the result, **Then** no partially-populated Order (e.g., an Order with missing or zero Order Lines) exists anywhere in the system as a result of the attempt.
5. **Given** a packing slip PDF for a TCGplayer order identifier that has already been successfully imported, **When** it is imported again, **Then** the existing Order and its Order Lines are left unchanged, no duplicate Order is created, and the operator is shown that it was rejected as an already-imported duplicate.

---

### User Story 3 - Keep Customer Shipping Information Out of Imported Records (Priority: P2)

An operator imports a packing slip that includes the customer's shipping name, address, and contact details alongside the order and product information. The system retains only the order identifier and product/line-item data needed for picking and order identification — not the customer's shipping information.

**Why this priority**: This directly protects customer privacy and satisfies the project's data-minimization requirement. It is independent of the parsing logic itself (it constrains what gets persisted, not how parsing works), so it can be verified on its own once basic import succeeds.

**Independent Test**: Supply a packing slip PDF containing a customer's shipping name and address. After import, inspect the persisted Order and Order Line data and confirm no shipping name, address, or contact detail is present anywhere in it.

**Acceptance Scenarios**:

1. **Given** a packing slip PDF whose order contains a customer's shipping name and mailing address, **When** it is imported, **Then** the resulting Order and Order Line records contain the order identifier and product/line-item data but no customer shipping name, address, or contact detail.

---

### Edge Cases

- When a packing slip PDF contains multiple distinct orders and only some of them fail to parse, the orders that parsed successfully are still imported; only the failing orders are rejected (FR-005).
- When a packing slip is imported for a TCGplayer order identifier that has already been successfully imported previously, the import is rejected as a duplicate and the existing Order is left unchanged (FR-008).
- What happens when the supplied file cannot be opened or read at all (corrupted file, wrong document type, or not actually a packing slip)?
- What happens when the same product/card appears as more than one product line within the same order (a legitimate repeated purchase line vs. a parsing artifact that duplicated a line)?
- What happens when required card-identification fields (product name, set) are present in the document but truncated or garbled by the PDF's layout, such that they cannot be read with confidence?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST parse a TCGplayer Packing Slip PDF into one or more Orders, each identified by its TCGplayer order identifier, each containing one or more Order Lines.
- **FR-002**: System MUST extract, for each Order Line, the product line/game, product name, set, card/collector number, rarity, condition, quantity, and variant/printing information when present in the source description text.
- **FR-003**: System MUST preserve the exact requested quantity for every Order Line, including quantities greater than one, with no rounding, truncation, capping, or default substitution.
- **FR-004**: System MUST preserve the Order → Order Line relationship exactly as described in the source document — no Order Line may be merged with another, split, or associated with an order other than the one it appeared under in the packing slip.
- **FR-005**: System MUST reject any Order that cannot be confidently reconstructed — specifically when its order identifier is missing or unreadable, it contains zero product lines, any of its product lines has a quantity that cannot be parsed as a positive whole number, or required card-identification fields (product name, set) cannot be read with confidence. When a single packing slip contains multiple orders, each order MUST be evaluated and imported or rejected independently: a failure in one order does not prevent the other, valid orders in the same packing slip from being imported (partial import).
- **FR-006**: When any Order or Order Line is rejected, the system MUST surface a specific, human-readable description of what could not be parsed (identifying the affected order and/or product line), not a generic failure message.
- **FR-007**: System MUST NOT create a partially-populated Order (e.g., an Order record with zero or missing Order Lines, or an Order Line with an unparseable quantity) as the result of a failed or partial parse.
- **FR-008**: System MUST detect when an imported packing slip's TCGplayer order identifier matches an Order already present in the system. When a duplicate order identifier is detected, the system MUST reject that order's import, leave the existing Order and its Order Lines unchanged, and report it as already-imported. This does not prevent other, new orders in the same packing slip from being imported per FR-005.
- **FR-009**: System MUST NOT persist customer shipping information (name, mailing address, or contact details) present in the packing slip; only the TCGplayer order identifier and the Order Line data required for picking and order identification are retained.
- **FR-010**: System MUST make a successfully imported Order and its Order Lines immediately available for use by subsequent workflow steps, with no additional manual publishing step required.
- **FR-011**: System MUST NOT apply, infer, or attach card catalog/image enrichment during import — an imported Order Line contains only what the packing slip itself describes.
- **FR-012**: System MUST retain a record of every import attempt sufficient to answer, for any given packing slip, whether it succeeded or was rejected and why, distinguishing successfully imported Orders from rejected ones.

### Key Entities *(include if feature involves data)*

- **Order**: Represents one TCGplayer order recovered from a packing slip. Key attributes: TCGplayer order identifier, import outcome/timestamp. Contains one or more Order Lines. Does not contain customer shipping information.
- **Order Line**: Represents one product/card line item within an Order as described by the packing slip. Key attributes: product line/game, product name, set, card/collector number, rarity, condition, quantity, variant/printing text (when present). Belongs to exactly one Order.
- **Import Attempt**: Represents one execution of the import process against a supplied packing slip. Key attributes: timestamp, outcome (succeeded/rejected, potentially per-order within a multi-order packing slip), and the specific reason(s) for any rejection. Used to answer "was this packing slip imported, and what happened?"

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Order Lines in a successfully imported packing slip have a quantity that exactly matches the source document, verified across a representative sample of real packing slips including multi-quantity lines.
- **SC-002**: 100% of individual orders that cannot be confidently parsed are rejected rather than producing an incomplete or incorrect Order — zero instances of a partially-populated Order existing after any rejected import — while other, valid orders in the same packing slip are still successfully imported.
- **SC-003**: For any import attempt, an operator can determine whether it succeeded or failed and the specific reason for any failure without inspecting application code, database contents, or logs directly.
- **SC-004**: 0 instances of customer shipping name, address, or contact information found in persisted Order or Order Line data across a representative sample of imports whose source packing slips contained such information.
- **SC-005**: A successfully imported Order and its Order Lines are available for use by subsequent workflow steps immediately after the import completes, with no separate manual step required to "publish" or activate them.

## Assumptions

- The specifics of how an operator supplies the packing slip PDF to the system (e.g., a future admin/import screen, an interim manual process, or an API) are out of scope for this feature; this feature covers parsing, validation, and persistence of the resulting Order and Order Line data once a PDF is supplied. Employee authentication and authorization for who may perform an import are handled by a separate feature.
- Variant/printing information is captured as it appears within the packing slip's product description text (per prior discovery of Loot's real packing slips), rather than normalized against a canonical list of variant types; normalization is left to future catalog enrichment.
- A successfully imported Order begins in a "Ready" state consistent with the picking workflow described in the product requirements, though the order queue, claiming, and picking workflow themselves are separate features not built by this one.
- Retention or deletion of the original source PDF file after successful extraction is not required by this feature; the durable record this feature is responsible for is the structured Order and Order Line data. Whether the raw file is additionally retained is left to technical planning.
- "Rarity" and "condition" are recorded as they appear in the packing slip; no validation against an external list of valid values is performed by this feature.
