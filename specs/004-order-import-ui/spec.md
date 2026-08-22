# Feature Specification: Order Import UI

**Feature Branch**: `004-order-import-ui`

**Created**: 2026-08-21

**Status**: Draft

**Input**: User description: "Order Import UI: let any authenticated employee upload a TCGplayer packing slip PDF, trigger the existing import service, and see per-order success/failure results with reasons; also provide a browse view listing all imported orders with their status."

## Clarifications

### Session 2026-08-21

- Q: When a packing slip's summary/index page lists a different set of order IDs than what was actually parsed from the document (a "summary mismatch"), should the employee see this as a distinct, batch-level warning — separate from the per-order success/failure list? → A: Yes — show a distinct, batch-level warning message (separate from the per-order list) whenever a summary mismatch is detected, since it's a genuine data-integrity signal the underlying Order Import feature already computes (its FR-013) and dropping it silently would hide a real problem rather than surface it.
- Q: When an import becomes Interrupted, should the UI keep the last received progress and per-order results visible while showing that the overall outcome is uncertain? → A: Keep the last snapshot with a prominent Interrupted warning and safe-retry action; label the snapshot incomplete and potentially stale.
- Q: What maximum PDF upload size should the system accept before rejecting the request? → A: 25 MB maximum.
- Q: When the server sends a terminal Failed update after some orders were processed, should the UI retain those partial results? → A: Keep partial results with a Failed label, explain that completed orders remain imported, and offer a safe retry.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Employee Uploads a Packing Slip and Sees What Happened (Priority: P1)

An authenticated employee has a TCGplayer packing slip PDF (often bundling several orders) and needs to get its orders into the system so picking can begin. They upload the file through the application, the system processes it, and the employee sees exactly which orders were imported successfully and which were rejected — and why — without needing to check with anyone else or look at logs.

**Why this priority**: This is the missing front door to the whole fulfillment workflow. The packing-slip parsing and persistence logic already exists (Order Import feature), but nothing in the product lets an employee actually trigger it today. Without this, orders can only enter the system through a manual/technical process — no employee can self-serve an import.

**Independent Test**: Upload a representative packing slip PDF containing a mix of valid and structurally-broken orders. Confirm the employee sees, for that single upload, which orders succeeded, which were rejected, and a specific human-readable reason for each rejection — with no need to inspect the database or backend logs.

**Acceptance Scenarios**:

1. **Given** an authenticated employee on the import screen, **When** they select and submit a valid packing slip PDF, **Then** the system processes it and the employee sees a per-order result: which orders were imported successfully.
2. **Given** a packing slip PDF containing one order that is structurally invalid (e.g., missing a required field) alongside other valid orders, **When** it is submitted, **Then** the employee sees the valid orders reported as successful and the invalid order reported as rejected with a specific, human-readable reason — not a generic failure.
3. **Given** a packing slip PDF for an order that was already successfully imported previously, **When** it is submitted again, **Then** the employee sees that order reported as rejected as an already-imported duplicate, distinct from other rejection reasons.
4. **Given** a file that is not a readable packing slip (corrupted, wrong file type, or unreadable), **When** it is submitted, **Then** the employee sees a clear message that the file could not be processed, with no orders reported as imported.
5. **Given** a large packing slip batch (on the order of 200 orders) is submitted, **When** processing is underway, **Then** the employee sees progress (e.g., orders processed so far) rather than an unresponsive-looking screen, and the final per-order results once processing completes.
6. **Given** the import request fails for a reason unrelated to the packing slip's content (e.g., a network or server error interrupts the request), **When** this happens, **Then** the employee sees a clear message that the import could not be completed and should be retried — distinct from a per-order rejection — rather than an unresponsive screen or a misleading "no orders found" result.
7. **Given** a packing slip PDF whose summary/index page lists a different set of order identifiers than what was actually parsed from the document, **When** it is submitted, **Then** the employee sees a distinct, batch-level warning about the mismatch, separate from and in addition to the per-order success/failure results.

---

### User Story 2 - Employee Browses Previously Imported Orders (Priority: P2)

An authenticated employee wants to confirm whether a particular order has already been imported, or get a sense of everything currently in the system, without re-uploading anything. They open a browse view listing every imported order and its current status.

**Why this priority**: This gives visibility beyond the Dashboard's Ready-only view (Login/Dashboard UI feature) and beyond a single upload's immediate results — useful for confirming an import "stuck," spotting an order that never made it in, or simply seeing the full picture. It's valuable on its own but secondary to actually being able to import in the first place (User Story 1).

**Independent Test**: With one or more orders already present in the system (however they got there), open the browse view and confirm every one of those orders appears with its TCGplayer order identifier, status, and import timestamp — independently of whether this session performed an upload.

**Acceptance Scenarios**:

1. **Given** one or more orders have been imported, **When** an employee opens the browse view, **Then** every imported order is listed with its TCGplayer order identifier, current status, and import timestamp.
2. **Given** zero orders have been imported yet, **When** an employee opens the browse view, **Then** they see a clear empty-state message rather than a blank area.
3. **Given** the browse view is displayed, **When** an employee views it on a small mobile screen, **Then** the list remains legible and usable without horizontal scrolling.
4. **Given** an order just imported via User Story 1, **When** the employee opens the browse view afterward, **Then** that order appears in the list without any additional manual step.

---

### Edge Cases

- What happens when an employee submits a file that isn't a PDF at all (e.g., an image or spreadsheet)? The system rejects it with a clear message before or during processing, and no orders are reported as imported (Acceptance Scenario US1-4).
- What happens when an employee submits a PDF larger than 25 MB? The system rejects it before import processing with a clear size-limit message, and no orders are reported as imported.
- What happens when the employee navigates away from the import screen before an in-progress upload finishes? This feature does not need to preserve or resume that in-progress result — the employee can re-check via the browse view (User Story 2) or re-submit if unsure.
- What happens when the HTTP connection ends before the employee receives a terminal Completed or Failed update? The UI enters an Interrupted state, says the connection was lost and the overall outcome is uncertain, preserves the last received counters and per-order results as explicitly incomplete and potentially stale context, and offers a safe retry. Already-committed per-order transactions remain; retrying cannot create duplicate orders and imports any valid orders that were not committed before cancellation.
- What happens when the server reports a terminal Failed update after processing some orders? The UI keeps the partial counters and per-order results visible, labels the batch Failed, explains that completed orders remain imported, and offers a safe retry; it does not present the partial batch as Completed.
- What happens when two employees upload the same packing slip at nearly the same time? The existing Order Import feature's race-safe duplicate guarantee applies unchanged; whichever request's order is persisted first succeeds, and the other reports that order as an already-imported duplicate (per Acceptance Scenario US1-3), never a hard failure.
- What happens when an order's status is something other than "Ready" (e.g., a status introduced by a future order-claiming/picking feature)? The browse view reflects whatever status the order actually has — it never fabricates or defaults a status.
- What happens when the browse view has a very large number of orders over time? The layout must remain usable rather than becoming unresponsive or unreadable (see Assumptions for scale expectations).
- What happens when a packing slip's summary/index page and its actually-parsed orders disagree (a "summary mismatch")? The employee sees a distinct, batch-level warning about the mismatch, in addition to whatever per-order results the parse produced — this signal is never silently dropped (Acceptance Scenario US1-7).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let any authenticated employee, regardless of role, select and submit a single TCGplayer packing slip PDF file for import.
- **FR-002**: Submitting a file MUST trigger the existing packing-slip import processing for that file; the employee MUST NOT need a separate confirmation step to make an already-submitted file's import happen.
- **FR-003**: While an import is processing, the system MUST show the employee progress information (at minimum, orders processed so far out of the total detected) for batches large enough that processing is not near-instantaneous.
- **FR-004**: After an import finishes, the system MUST show the employee, for every order the packing slip contained, whether it was imported successfully or rejected.
- **FR-005**: For every rejected order, the system MUST show a specific, human-readable reason distinguishing it from other rejection reasons (e.g., missing required data vs. already-imported duplicate vs. unreadable file) — never a generic "import failed" message alone.
- **FR-006**: When the submitted file itself cannot be read as a packing slip at all, the system MUST show a clear, specific message describing the problem, and MUST NOT report any order from it as imported.
- **FR-007**: When the import request fails for a reason unrelated to the packing slip's own content (e.g., a network or server error), the system MUST show the employee a distinct message indicating the import did not complete and may be retried — this MUST NOT be presented the same way as a legitimate "zero orders imported" or per-order rejection result.
- **FR-008**: The system MUST provide a browse view, reachable by any authenticated employee, listing every imported order with at least its TCGplayer order identifier, current status, and import timestamp.
- **FR-009**: The browse view MUST reflect each order's actual current status; it MUST NOT display a fabricated, guessed, or default status for any order.
- **FR-010**: An order successfully imported via User Story 1 MUST appear in the browse view without requiring any additional manual step.
- **FR-011**: Both the import screen and the browse view MUST be fully usable on a mobile phone-sized viewport and a desktop-sized viewport, as a single responsive experience.
- **FR-012**: Neither the import screen nor the browse view MUST display any customer shipping information (name, mailing address, or contact details) — consistent with the Order Import feature's existing data-minimization guarantee that this data is never persisted in the first place.
- **FR-013**: When the Order Import feature's summary/index cross-check detects a mismatch between the packing slip's listed order identifiers and what was actually parsed, the system MUST show the employee a distinct, batch-level warning about the mismatch, separate from the per-order success/failure results — this signal MUST NOT be silently dropped or folded into another message (`/speckit-clarify` 2026-08-21).
- **FR-014**: Import progress updates exposed to the employee MUST use an explicit InProgress, Completed, or Failed status. InProgress updates MUST be emitted only when another order finishes processing, not for parser-internal page, line, or field activity.
- **FR-015**: The system MUST cancel remaining import processing when its HTTP request is aborted while preserving every already-completed per-order transaction; an active order MUST NOT leave partially persisted order data.
- **FR-016**: Re-uploading the same packing slip after interruption MUST be safe and MUST NOT create duplicate orders; already-committed orders MUST remain unique and valid orders not previously committed MAY import on retry.
- **FR-017**: If the client connection ends before a terminal Completed or Failed update arrives, the UI MUST enter an Interrupted state, explain that the connection was lost and the outcome may be partial, retain the last received snapshot only as visibly incomplete and potentially stale context, and allow the employee to retry safely.
- **FR-018**: The system MUST reject an uploaded PDF larger than 25 MB before import processing begins and MUST show a clear message identifying the 25 MB limit.
- **FR-019**: When a terminal Failed update follows one or more processed orders, the UI MUST retain the partial counters and per-order results, visibly label the batch Failed, explain that completed orders remain imported, and offer a safe retry without presenting the batch as Completed.

### Key Entities *(include if feature involves data)*

- **Order / Order Line**: Already defined and persisted by the Order Import feature. This feature adds no new fields and changes no stored data — it only reads and displays them (browse view) and triggers their creation (import screen).
- **Import Result** (UI-facing concept, not a new stored entity): The per-order success/failure outcome and reason an employee sees immediately after one upload, derived from the Order Import feature's existing Import Attempt / Import Order Result records for that operation. Also carries the attempt-wide summary-mismatch warning (FR-013), when present, distinct from per-order outcomes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An employee can upload a packing slip and determine, without any help or access to logs/database, exactly which of its orders succeeded and which failed and why, within 5 seconds of the operation completing.
- **SC-002**: 100% of rejected orders shown to an employee include a specific reason distinguishable from every other rejection reason type, not a generic failure message.
- **SC-003**: An import of the largest expected real-world batch (approximately 200 orders) completes within a single operation, with progress visible throughout rather than an unresponsive-looking screen at any point.
- **SC-004**: An employee can determine whether any specific previously-imported order exists in the system, and its status, within 10 seconds of opening the browse view, without needing to re-upload anything.
- **SC-005**: 100% of import screen and browse view renders on both a representative mobile viewport and a representative desktop viewport show no horizontal scrolling, overlapping content, or illegible text.
- **SC-006**: 0 instances of customer shipping information appearing anywhere in the import screen or browse view.
- **SC-007**: 100% of imports where the packing slip's summary/index page disagrees with its actually-parsed orders show the employee a distinct mismatch warning — 0 instances of this signal being silently absorbed into another message or omitted.
- **SC-008**: In automated interruption-and-retry scenarios, every source order identifier is persisted at most once, no Order has a partial Order Line graph, and every valid order not committed before interruption can be imported by retrying the same PDF.
- **SC-009**: 100% of uploads larger than 25 MB are rejected before import processing and produce no newly imported orders.

## Assumptions

- This feature adds only a front-end/API surface over the Order Import feature's existing parsing, validation, and persistence logic — it introduces no new import rules, failure types, or persisted fields beyond what that feature already defines.
- Any authenticated employee may import and browse orders, regardless of role (Picker or Manager/Admin) — consistent with the Login/Dashboard UI feature's precedent of not introducing role-gating unless a requirement calls for it.
- One packing slip PDF file is submitted per import operation; the screen does not need to accept multiple files in a single submission. A single file may still bundle many orders internally, per the Order Import feature's existing behavior.
- The per-order import results shown after an upload (User Story 1) are for that specific upload only and are not required to be saved as a browsable history — an employee who wants to confirm an order's presence afterward uses the browse view (User Story 2), which reflects current state rather than a log of past operations.
- The browse view is a flat, unfiltered, unpaginated list for V1, consistent with this project's existing precedent (e.g., the Dashboard's unpaginated Ready section) — sorting, filtering, and search are not required now and may be revisited if order volume at Loot's actual scale makes an unfiltered list impractical.
- The browse view's status column currently only ever shows "Ready," since that is the only order status that exists today (per the Login/Dashboard UI feature's data model) — this is expected and correct, not a defect; other statuses will appear automatically once future order-claiming/picking features introduce them, with no change needed to this feature.
- No formal accessibility standard applies to this feature, consistent with the Login/Dashboard UI feature's precedent — visual polish follows the existing design language on an ad hoc basis.
- The maximum accepted upload size is 25 MB; the limit applies to the server regardless of any client-side validation.
