# Feature Specification: TCGplayer API Order Import

**Feature Branch**: `020-tcgplayer-api-import`

**Created**: 2026-10-08

**Status**: Draft

**Input**: User description: "TCGplayer API order import. Replace packing-slip PDF upload as the normal way orders enter the app with a call to the TCGplayer Seller API that pulls the store's open orders." Product Owner decisions of 2026-10-08 and the signed TCGplayer Legacy Qualified API Addendum (2026-09-21) are recorded below and in `CLAUDE.md` ("TCGplayer API Agreement").

## Clarifications

### Session 2026-10-08

- Q: Which TCGplayer order statuses count as "open"? → A: Ready To Ship, provisionally. This will be confirmed with the Product Owner later and may change, so the status set is configuration rather than code (FR-004).
- Q: What if TCGplayer has no collector number for a line? → A: Import the order and show the collector number as unavailable on that line (FR-008).
- Q: What happens when an imported order is later changed or cancelled on TCGplayer? → A: Out of scope for this feature. The application only adds orders (Edge Cases).

## Product Owner Decisions *(2026-10-08)*

These decisions were made before specification and sit above the PRD in the source-of-truth hierarchy. They supersede PRD §28 ("V1 development should not depend on receiving new API access") and change §25's PDF-first import strategy. The PRD needs a matching amendment.

1. **The API becomes the normal import path.** PDF packing-slip upload stays available as a fallback for when the API is unavailable.
2. **An employee pulls orders on demand.** Import runs when an employee presses "Get new orders". There is no automatic or scheduled polling.
3. **No packing slip for API-imported orders.** TCGplayer's API provides no packing-slip document. The application stores no packing slip and no customer address for an API-imported order, and packers print that order's slip from the TCGplayer seller portal. PRD §27 is unchanged: no customer fields enter the data model. Orders imported through the PDF fallback still keep their stored slip.
4. **Card images for API-imported lines come from TCGplayer's own product data**, never from Scryfall, TCGdex or Lorcast, so no API data is sent to a third party. "No image is better than the wrong image" still applies.

## Agreement Constraints *(binding)*

Loot's API access is governed by the TCGplayer API Terms and Conditions and the signed **Legacy Qualified Addendum**, under which TCGplayer issued Loot's keys in 2026. A breach can end access immediately. This feature MUST:

- use only the keys TCGplayer issued to Loot and the store access token from authorizing them for Loot's own store, a one-time step a person performs; the application never creates credentials or authorizations itself;
- stay under 300 API calls per minute;
- identify every request with the business name and the application name and version;
- keep API data inside Loot's in-house application, never transferring it to any third party;
- never put API data into a third-party generative AI tool, including during development and testing;
- keep credentials out of the repository and out of any tool or person outside Loot's deployment configuration.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Employee Gets New Orders With One Press (Priority: P1)

An employee starting a picking session presses "Get new orders". The application asks TCGplayer for the store's open orders, imports every one that isn't already in the system, and shows which orders were added and which were rejected and why. Nobody downloads, prints or uploads a PDF.

**Why this priority**: This is the whole point of the feature. It removes the manual export-and-upload step that stands between a sale on TCGplayer and a picker starting work, and it replaces parsing a human-readable document with structured data, which removes a class of import errors.

**Independent Test**: With the TCGplayer service replaced by a synthetic stand-in holding a known set of open orders, press "Get new orders". Confirm every valid open order appears in the application as Ready with the correct lines, quantities, sets, conditions and variants, and that the employee sees a per-order result.

**Acceptance Scenarios**:

1. **Given** TCGplayer has three open orders not yet in the application, **When** an employee presses "Get new orders", **Then** all three are imported as Ready, and the employee sees each order reported as imported.
2. **Given** an open order containing a line with a quantity greater than one, **When** it is imported, **Then** that quantity is preserved exactly and receives the same strong emphasis on every picking surface as a PDF-imported line.
3. **Given** TCGplayer has no open orders that aren't already in the application, **When** the employee presses "Get new orders", **Then** they see a clear "no new orders" result, distinct from a failure.
4. **Given** one open order whose data cannot be translated into a complete, valid order, for example a line missing its quantity or product name, alongside valid orders, **When** the employee presses "Get new orders", **Then** the valid orders are imported, the invalid order is rejected with a specific human-readable reason, and no partial version of it is created.
5. **Given** an open order that is already in the application, whether it was imported earlier from the API or from a PDF, **When** the employee presses "Get new orders", **Then** that order is not imported again, and it is reported as already imported rather than as an error.
6. **Given** a large backlog, on the order of 200 open orders, **When** the employee presses "Get new orders", **Then** they see progress while the import runs and the full per-order results when it finishes.

---

### User Story 2 - Picker Sees Correct Card Images for API Orders (Priority: P2)

A picker working an API-imported order sees each card's image, taken from TCGplayer's own product data for exactly the product that was sold, so they can confirm they're holding the right printing.

**Why this priority**: Images are central to the visual picking experience, but orders can be picked without them, so this follows the ability to import at all. TCGplayer's own product data identifies the exact item sold, which makes the image match more reliable than a name-based lookup in an outside catalog.

**Independent Test**: Import a synthetic order whose lines carry TCGplayer product image data, plus one line without it. Open the order on a picking surface and confirm the first lines show TCGplayer's image, the last line shows no image, and no request about these lines reaches Scryfall, TCGdex or Lorcast.

**Acceptance Scenarios**:

1. **Given** an API-imported line for which TCGplayer provides a product image, **When** a picker views the order, **Then** that image is shown for the line.
2. **Given** an API-imported line for which TCGplayer provides no image, **When** a picker views the order, **Then** no image is shown, and no outside catalog is asked for one.
3. **Given** any API-imported order, **When** it is viewed anywhere in the application, **Then** no information about its lines is sent to any service other than TCGplayer.

---

### User Story 3 - Fall Back to PDF When the API Is Unavailable (Priority: P3)

When TCGplayer's API is down, rejects the store's credentials, or the store's API access has been suspended, the employee is told plainly that orders could not be fetched and that the packing-slip upload still works. They import that day's orders from a PDF as before.

**Why this priority**: This keeps fulfillment running when the API fails, but it only matters once the API path exists, and the PDF importer already works.

**Independent Test**: With the TCGplayer stand-in returning an outage, and separately an authorization failure, press "Get new orders". Confirm no orders are created, the employee sees a message distinguishing the two cases and pointing to PDF upload, and a PDF upload then imports orders normally.

**Acceptance Scenarios**:

1. **Given** the TCGplayer API is unreachable or returns a server error, **When** the employee presses "Get new orders", **Then** no order is created, and the employee sees that TCGplayer could not be reached, that they can retry later, and that PDF upload is available.
2. **Given** TCGplayer rejects the store's credentials, **When** the employee presses "Get new orders", **Then** no order is created, and the employee sees that the store's TCGplayer connection was refused, which needs a manager to act, distinct from a temporary outage.
3. **Given** the API failed partway through an import, **When** the employee looks at the results, **Then** orders completed before the failure remain imported, no partial order exists, and pressing "Get new orders" again imports the rest without duplicates.
4. **Given** the API is unavailable, **When** the employee uploads a packing-slip PDF, **Then** it imports exactly as it does today, including storing each order's packing slip.

---

### User Story 4 - Packer Handles an Order With No Stored Slip (Priority: P3)

A packer scans the label of an API-imported order at the packing desk. The desk records it as packed in the usual way and tells the packer that no slip is stored for this order, so they print it from TCGplayer using the order number.

**Why this priority**: Packing must keep working for API orders, and the packing desk already handles a missing slip, so this story mainly confirms that behavior covers every API-imported order.

**Independent Test**: Import an order through the API stand-in, take it through picking, and scan it at the packing desk. Confirm the desk shows the "no packing slip stored" guidance with the order number, offers no slip to print, and still records the order as packed.

**Acceptance Scenarios**:

1. **Given** an API-imported order that has been picked, **When** a packer scans it at the packing desk, **Then** the desk says no packing slip is stored and shows the TCGplayer order number to print it from, and the order can be recorded as packed.
2. **Given** a PDF-imported order, **When** a packer scans it, **Then** its stored packing slip is offered exactly as it is today.

---

### Edge Cases

- **Two employees press "Get new orders" at nearly the same time.** Each TCGplayer order is still created at most once. The employee whose request reaches an order second sees it reported as already imported, never a failure.
- **An order is imported by PDF while an API import is running, or the reverse.** The same TCGplayer order number never produces two orders, whichever path creates it first.
- **More open orders than TCGplayer returns in one response.** The import works through every page of results, so no open order is silently left behind.
- **An order has more lines than TCGplayer returns in one response.** Every line is fetched before the order is created. An order whose full set of lines cannot be retrieved is rejected, never created with some lines missing.
- **The application nears the 300-calls-per-minute limit on a very large import.** The import slows down to stay under the limit rather than exceeding it, and keeps showing progress.
- **TCGplayer reports an order's line quantities summing to a different total than the order's own product count.** The order is rejected with a specific reason rather than imported with an inconsistent picture.
- **A line is not a single card**, for example a sealed product or an accessory. TCGplayer's product data has no collector number for it, so it is handled like any line without one: the order is imported, and the line shows its product name, image and "No number" (FR-008). The line is never dropped, and the order is never partially imported. (Decided 2026-10-09.)
- **TCGplayer returns customer and shipping fields with order details.** They are discarded on receipt and never stored, logged or displayed.
- **The API's response doesn't match the documented shape**, with missing fields, unexpected types or an empty body. The affected order, or the whole import if no order can be read, fails safely with a specific reason, and no partial order is created.
- **An order changes or is cancelled on TCGplayer after it has been imported.** Out of scope. "Get new orders" only adds orders not already in the application, as the PDF path does today. It does not detect or flag later changes or cancellations, which people continue to handle as they do now. PRD open questions 20 and 21 stay open.
- **TCGplayer's product data has no collector number for a line.** The order is imported, and the line shows that no collector number is available (FR-008).

## Requirements *(mandatory)*

### Functional Requirements

**Importing from the API**

- **FR-001**: The system MUST provide a "Get new orders" action that imports the store's open TCGplayer orders through the TCGplayer API.
- **FR-002**: The action MUST run only when an employee triggers it. The system MUST NOT poll TCGplayer automatically or on a schedule.
- **FR-003**: Every authenticated employee, whatever their role, MUST be able to trigger "Get new orders", matching who may upload a packing slip today.
- **FR-004**: An order MUST count as "open" when its TCGplayer status is **Ready To Ship**. This is provisional, pending Product Owner confirmation, so the set of statuses MUST be changeable through configuration without a code change or redeployment of new code.
- **FR-005**: The import MUST retrieve every open order and every line of each order, following TCGplayer's paging until all results have been read. It MUST NOT create an order unless all of that order's lines were retrieved.
- **FR-006**: Each imported order MUST be created as Ready and identified by its TCGplayer order number, the same identifier PDF-imported orders use.
- **FR-007**: For each line, the system MUST record the data TCGplayer provides for picking: game, product name, set, condition, printing or variant (including foil), language, rarity where present, and quantity. TCGplayer's values are authoritative and MUST NOT be overwritten by any other source.
- **FR-008**: Each line MUST carry its collector number, taken from TCGplayer's own product data for the line's product. When TCGplayer's product data has no collector number for a line, the order MUST still be imported, and the line MUST visibly show that no collector number is available. It MUST never show a guessed or blank-looking value.
- **FR-009**: An order whose API data cannot be translated into a complete, valid order MUST be rejected with a specific, human-readable reason and MUST NOT be created, even partially. Business validation that applies to orders from any source MUST be the same validation used for PDF-imported orders, not a second copy of it.
- **FR-010**: An order whose TCGplayer order number already exists in the application, from either import path, MUST NOT be created again. It MUST be reported as already imported, distinct from other rejection reasons. This MUST hold when imports run concurrently, including an API import and a PDF upload at the same time.
- **FR-011**: After an import, the employee MUST see a per-order result for every open order considered: imported, already imported, or rejected with its reason. "No new orders" MUST be shown distinctly from a failure.
- **FR-012**: While an import runs, the employee MUST see progress, at minimum orders processed so far out of the total found.
- **FR-013**: If the import stops partway, through an API failure, a lost connection or cancellation, orders already completed MUST stay imported, no partial order may exist, and repeating "Get new orders" MUST import the remaining orders without duplicates.

**Failures and fallback**

- **FR-014**: When TCGplayer cannot be reached or returns a server error, the system MUST create no partial order, and MUST tell the employee the fetch failed, that they can retry, and that PDF upload is available.
- **FR-015**: When TCGplayer rejects the store's credentials or reports that access is suspended, the system MUST tell the employee this is a connection problem needing a manager, distinct from a temporary outage. The system MUST NOT respond by creating, requesting or re-authorizing any credential (see FR-020).
- **FR-016**: Packing-slip PDF upload MUST remain available and behave as it does today, including storing one packing slip per order under PRD §27's rules.

**Customer data and packing**

- **FR-017**: The system MUST NOT store, log or display any customer or shipping field that TCGplayer returns. API-imported orders carry no customer data, consistent with PRD §27.
- **FR-018**: The system MUST NOT store a packing slip for an API-imported order. At the packing desk, an API-imported order MUST show the existing "no packing slip stored" guidance with its TCGplayer order number, and MUST still be recordable as packed.

**Card images**

- **FR-019**: Card images for API-imported lines MUST come only from TCGplayer's product data for that line's product. When TCGplayer provides no image, the line MUST show no image. The system MUST NOT send any information about an API-imported line to Scryfall, TCGdex, Lorcast or any other third-party service. PDF-imported lines keep their current image sources.

**API agreement**

- **FR-020**: The system MUST authenticate to TCGplayer using only the configured keys and store access token, supplied through deployment configuration. The store access token comes from a one-time Store Authorization Workflow that a person performs outside the application. The application MUST NOT create, request or replace credentials or store authorizations itself. Obtaining short-lived session tokens with the configured credentials is permitted.
- **FR-021**: The system MUST NOT make more than 300 TCGplayer API calls in any one-minute window, across all employees and concurrent imports, and MUST slow down rather than exceed the limit.
- **FR-022**: Every TCGplayer API request MUST identify Loot Investments LLC and the application name and version.
- **FR-023**: TCGplayer credentials MUST NOT appear in the repository, logs, error messages shown to employees, or test fixtures.
- **FR-024**: Automated tests MUST use synthetic TCGplayer responses built from TCGplayer's published response formats, never responses captured from the live API.
- **FR-025**: The system MUST log each import's outcome, order counts and any failure category in one completion entry, as PDF imports do, with safe fields only: no customer data, credentials or raw API responses.

### Key Entities

- **Order / Order Line**: Already defined. An API-imported order uses the same entities as a PDF-imported one. It differs only in having no stored packing slip and in where its line images come from. No customer fields are added.
- **Import Attempt / Import Order Result**: Already defined for PDF imports. An API import is recorded the same way, so that its per-order outcomes and reasons are reported consistently, and it is marked as having come from the API rather than a PDF.
- **Line product identity**: TCGplayer's identifier for the exact product sold on a line. The application uses it to obtain that product's collector number and image from TCGplayer. Whether it is kept on the line is a planning decision.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An employee can bring all of the store's open orders into the application with one press and no file handling, and see per-order results within 30 seconds for a typical day's batch of up to 50 orders.
- **SC-002**: A backlog of 200 open orders imports in a single operation, with progress visible throughout, and with 0 minutes in which more than 300 TCGplayer API calls are made.
- **SC-003**: Across all automated import, retry, interruption and concurrent-import scenarios, every TCGplayer order number exists at most once, and no order exists with missing lines.
- **SC-004**: 0 customer or shipping fields from TCGplayer are present in stored data, logs or any screen, verified against synthetic responses that contain such fields.
- **SC-005**: 0 requests concerning API-imported lines reach Scryfall, TCGdex, Lorcast or any other non-TCGplayer service.
- **SC-006**: When the API is unavailable, an employee can still import that day's orders by PDF upload, with no change to that process.
- **SC-007**: 100% of failed fetches tell the employee whether the problem is a temporary outage or a refused connection, and that PDF upload is available.
- **SC-008**: 0 live TCGplayer API responses appear in the repository's test fixtures, and 0 credentials appear in the repository.

## Assumptions

- **Precondition:** Loot's keys, issued by TCGplayer in 2026, are authorized for Loot's own store through TCGplayer's Store Authorization Workflow, a one-time step a person performs. The resulting store access token is stored only as a secret. This step is done (2026-10-08), and TCGplayer enabled order and catalog access on 2026-10-09 (research.md §14).
- TCGplayer's published Seller API (v1.39) is the contract: searching the store's orders by status, reading order details, reading order lines with product details, and reading product data from TCGplayer's catalog for a line's product.
- Any authenticated employee may run "Get new orders", following the precedent of feature 004, which lets any authenticated employee upload a packing slip.
- The results and progress experience for API imports follows the existing import screen's patterns: per-order results, distinct failure states and safe retry.
- No PDF-imported order is migrated or re-imported. Existing orders stay as they are (PRD open question 26).
- Each API-imported line's TCGplayer image address is stored at import and shown from there, so viewing an order never calls TCGplayer or any other service. (Planning revised the earlier assumption of fetching at display time; plan.md, research.md §13.)
- The PRD will be amended (v0.7) to record decisions 1–4, retire §28's "do not depend on API access" stance and revise §25 and §40.6. That amendment is documentation work in this feature, not a new requirement.
- No accessibility scope beyond the existing design language, consistent with prior features.
