# Feature Specification: Local Mock Orders and Order Deletion

**Feature Branch**: `021-local-mock-orders`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Local development mock data for TCGplayer API import. When the app runs locally (Development environment) with no TCGplayer credentials, pressing "Get new orders" on the Import page should import synthetic mock orders (built from TCGplayer's published response schema, invented values only, like the existing E2E fixtures served by TcgplayerStubHandler) instead of showing "Getting orders from TCGplayer isn't set up here". This lets the developer exercise the API import, picking and packing flows locally without real API keys or real orders. It must never be possible for stage or production to use mock data, and real API calls must never happen from a local machine." Added the same day: "the ability to delete orders through the app but as an admin only action and the main purpose of it would be for testing not really used in prod."

## Clarifications

### Session 2026-10-10

- Q: What happens when **Get new orders** is pressed again after mock orders were imported? → A: The mock set is fixed. A second press reports the orders as already imported, or "No new orders", exactly as with the real store. Starting over means clearing the local orders (FR-007).
- Q: Should the Import page label the orders as mock data? → A: No. The invented order numbers already make that plain (FR-008).
- Q: Should the mock import guard against a local app pointed at a shared database? → A: No guard. The developer's local app points at its own dedicated database, never stage or production (FR-009, Assumptions).
- Q: Where is deleting an order available? → A: In every environment, production included, but only to Manager/Admin employees. Its main purpose is testing (Product Owner decision, FR-010). *(Narrowed for production below.)*
- Q: What can be deleted in one action? → A: Both a single order and all orders at once (FR-011, FR-012).
- Q: Can an order that someone has claimed be deleted? → A: Yes, in any status (Product Owner decision). *(Narrowed for production below.)*
- Q: Are deletions recorded? → A: Yes. Each deletion records which Manager/Admin deleted which order, and when (FR-015).
- Q: The order itself holds the PRD §34 history of who picked it: the claim, each line's pick outcome, picking issues and their reporters, and who packed it. How is that protected? → A: On production, only an order nobody has touched yet can be deleted: one that has never been claimed. Local and stage can delete an order in any status (Product Owner decision, FR-013).
- Q: Hard or soft delete? → A: Soft. A deleted order is hidden from every screen but stays stored with all its history (Product Owner decision, FR-016).
- Q: What happens when a soft-deleted order is imported again? → A: A fresh, unpicked copy is imported as a new order. The deleted one stays hidden with its history unchanged (Product Owner decision, FR-017).
- Q: How should the delete controls look, given current delete-UI practice (Carbon, NN/g, Primer)? → A: Single delete sits in a per-order menu, not a red button on every row. Its confirmation names the order with "Delete order" / "Keep order", and delete isn't the default. Delete all also requires typing `DELETE`. No checkboxes for picking several orders and no undo toast for now (Product Owner decision, FR-021–FR-023).
- Q: What happens to a soft-deleted order's stored packing slip? → A: It is removed at deletion, because a hidden order is never packed and the customer's address is no longer needed. The log of who retrieved the slip stays (Product Owner decision, FR-016).

## Context

Feature 020 made the TCGplayer API the normal way orders enter the app. On a machine without TCGplayer keys, "Get new orders" shows "Getting orders from TCGplayer isn't set up here" (020 quickstart §C). The only place API-shaped orders can be seen today is the automated end-to-end test host.

The TCGplayer API agreement (`CLAUDE.md`, "TCGplayer API Agreement") forbids AI tools from making live TCGplayer calls or reading real responses, and the TCGplayer keys have now been removed from the developer's machine. Local development therefore needs a source of API-shaped orders that contains no TCGplayer data at all.

The second part of this feature lets a Manager/Admin delete orders from inside the app. Today, clearing test orders means running SQL against the database by hand, as was done on stage on 2026-10-10. Deletion is mainly for testing, so imported orders can be removed and imported again. Deletion is soft: a deleted order is hidden, never erased, so the record of who picked and packed it is kept. On production it is limited to orders nobody has touched yet, so pick history there is never affected.

The mock orders are a developer-facing change: pickers and packers on stage and production see no change from them. Order deletion is visible only to Manager/Admin employees.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import mock orders locally (Priority: P1)

A developer runs the app on their own machine, signs in, opens **Import** and presses **Get new orders**. Instead of the "not set up here" message, the app imports a set of invented orders that look and behave like real API-imported orders. The developer can then pick and pack those orders the same way staff handle real ones.

**Why this priority**: This is the whole feature. Without it, the API import path and everything after it can only be exercised on stage with real orders or through the automated test suite.

**Independent Test**: Run the app locally with no TCGplayer keys, press **Get new orders**, and confirm that mock orders appear in the queue and can be claimed, picked and packed.

**Acceptance Scenarios**:

1. **Given** the app is running locally, **When** the developer presses **Get new orders**, **Then** the mock orders are imported and the Import page reports them the same way it reports a real API import.
2. **Given** mock orders were imported, **When** the developer opens one, **Then** its lines show the same high-risk emphasis as real orders (quantity greater than one, variant, set, card identity).
3. **Given** a mock order has been picked, **When** the developer scans it at the packing desk, **Then** the app shows the existing "no packing slip is stored" handling for API orders and lets it be recorded as packed.
4. **Given** mock orders were already imported, **When** the developer presses **Get new orders** again, **Then** the Import page reports them as already imported, or "No new orders", exactly as with the real store, and no order is duplicated.

---

### User Story 2 - Stage and production can never use mock data (Priority: P1)

Stage and production always talk to the real TCGplayer API, or show the "not set up here" message when their keys are missing. They never import mock orders, whatever their configuration says.

**Why this priority**: Mock orders reaching stage or production would put invented orders in front of pickers. It shares P1 because the feature can't ship without this guarantee.

**Independent Test**: Start the app as stage or production with no TCGplayer keys, and with any setting that might turn mock data on, then confirm that "Get new orders" shows the "not set up here" message and imports nothing.

**Acceptance Scenarios**:

1. **Given** the app runs as stage or production without TCGplayer keys, **When** someone presses **Get new orders**, **Then** the "not set up here" message is shown and no orders are imported.
2. **Given** the app runs as stage or production with any configuration value that tries to turn mock data on, **When** it starts or someone presses **Get new orders**, **Then** no mock orders are ever imported.

---

### User Story 3 - A local machine never calls the real TCGplayer API (Priority: P1)

Even if TCGplayer keys are present on a developer's machine, the app running locally does not call the real TCGplayer API. It uses mock orders instead.

**Why this priority**: The API agreement forbids live calls whose responses an AI tool could read. A local machine is where AI coding assistants run, so local runs must never reach the real API.

**Independent Test**: Run the app locally with TCGplayer key values present, press **Get new orders**, and confirm that only mock orders are imported and no request leaves the machine for TCGplayer.

**Acceptance Scenarios**:

1. **Given** the app runs locally with TCGplayer keys configured, **When** the developer presses **Get new orders**, **Then** mock orders are imported and no request is made to TCGplayer.

---

### User Story 4 - A Manager/Admin deletes orders (Priority: P2)

A Manager/Admin deletes an order, or all orders at once. The app asks them to confirm first and says what will be deleted. A deleted order disappears from every screen but stays stored with its history, and importing it again brings in a fresh copy. On production, only orders nobody has touched yet can be deleted.

**Why this priority**: It replaces the hand-run SQL clean-up and makes the mock orders reusable, so a test can import, pick, delete and import again. The mock import (P1) is usable without it, because a fixed set can still be cleared by hand.

**Independent Test**: Locally or on stage, as a Manager/Admin, delete one picked order, confirm it's gone from every screen, and confirm **Get new orders** imports a fresh copy. Delete all orders and confirm the queue is empty. On production, confirm a claimed order can't be deleted. As a picker, confirm no delete action is offered or allowed.

**Acceptance Scenarios**:

1. **Given** a Manager/Admin is viewing an order they're allowed to delete, **When** they choose to delete it and confirm, **Then** it no longer appears on any screen, and its pick, issue and packing history is still stored.
2. **Given** a Manager/Admin chooses to delete all orders, **When** the confirmation appears, **Then** it states how many orders will be deleted (and, on production, how many will be kept because they've been touched), and nothing is deleted until they confirm.
3. **Given** an order was deleted, **When** it is imported again (by **Get new orders** or packing-slip upload), **Then** a fresh, unpicked copy is imported as a new order, and the deleted one stays hidden with its history unchanged.
4. **Given** an employee who is not a Manager/Admin, **When** they look at an order or the order list, **Then** no delete action is shown, and a delete request sent directly to the server is refused.
5. **Given** the app is running as production and an order has been claimed at least once, **When** a Manager/Admin tries to delete it, **Then** no delete action is offered for it, and a delete request sent directly to the server is refused.
6. **Given** the app is running locally or on stage and a picker has claimed an order, **When** a Manager/Admin deletes it, **Then** the deletion succeeds, and the picker's next action on that order tells them it no longer exists instead of failing silently or showing stale data.
7. **Given** the app is running locally or on stage and an order has an unresolved picking issue, **When** a Manager/Admin starts to delete it, **Then** the confirmation says so.
8. **Given** a Manager/Admin deleted an order, **When** the deletion is reviewed later, **Then** the record shows who deleted which order number and when.

---

### Edge Cases

- **Card images**: mock orders carry invented product identifiers. Any image that can't be matched to the invented card shows the existing "no image" state, never a real card's image. No image is better than the wrong image.
- **Order numbers**: mock order numbers are visibly invented, so they can't be mistaken for real TCGplayer order numbers.
- **Non-open orders**: the mock data includes orders that aren't open, as the real store does. They're excluded the same way real ones are, so local runs exercise the open-order rules too.
- **Packing slip**: mock orders, like real API orders, have no stored packing slip. Packing uses the existing API-order handling.
- **Starting over**: once every mock order has been imported, further presses import nothing. The developer deletes the orders (User Story 4) to import the set again.
- **What "touched" means on production**: an order counts as touched once anyone has claimed it, even if the claim was later released. A claim is where pick history starts, so an order that was claimed and released can't be deleted on production either.
- **An order is claimed while a delete is being confirmed on production**: the delete is refused when it reaches the server, because the order is now touched. The Manager/Admin is told why.
- **Delete all on production**: only the untouched orders are deleted; touched ones stay. The confirmation and the result both say how many of each.
- **Deleting a claimed order (local and stage)**: the picker loses it mid-pick. Their screen must say the order no longer exists, not fail silently or let them keep recording outcomes against it.
- **Deleting an order with unresolved picking issues (local and stage)**: the issues stay stored with the hidden order, so nothing is dropped, and the confirmation names them (FR-014).
- **Deleting an order with a stored packing slip**: the slip is removed, so the customer's address is no longer kept. The log of who retrieved it stays (FR-016).
- **Deleting all orders while an import is running**: orders the import adds after the confirmation stay. "All orders" means the deletable orders that existed when the Manager/Admin confirmed.
- **Two Manager/Admins delete the same order**: the second is told it is already gone; nothing fails.
- **Re-importing a deleted order more than once**: each delete and re-import leaves one more hidden copy. Only the visible copy counts for duplicate detection.
- **Import history**: earlier import results that named a deleted order stay readable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the app runs in the local Development environment, **Get new orders** MUST import orders from the mock source instead of the TCGplayer API.
- **FR-002**: The mock source MUST contain only invented values, shaped like TCGplayer's published response schema. It MUST NOT contain or be derived from any real TCGplayer response.
- **FR-003**: Mock orders MUST pass through the same import rules as real API orders: open-order selection, line translation, duplicate detection, validation and rejection. Any difference in outcome between a mock order and an identically shaped real one is a defect.
- **FR-004**: Outside the local Development environment, the app MUST never use the mock source, whatever its configuration contains.
- **FR-005**: In the local Development environment, the app MUST NOT send any request to TCGplayer, even when TCGplayer keys are configured.
- **FR-006**: Mock orders MUST be importable, pickable and packable through the existing flows, with no special handling beyond what applies to any API-imported order.
- **FR-007**: The mock order set MUST be fixed: the same orders, with the same order numbers, on every press. A repeated press MUST report already-imported mock orders through the existing duplicate handling and MUST NOT create new ones.
- **FR-008**: The Import page MUST show mock import results exactly as it shows real API import results, with no added mock label.
- **FR-009**: The mock import MUST NOT add any check on which database it writes to. Keeping the local app on its own database is the developer's responsibility (Assumptions).

- **FR-010**: Deleting orders MUST be available only to Manager/Admin employees, in every environment, and MUST be enforced by the server. Requests from any other role MUST be refused.
- **FR-011**: A Manager/Admin MUST be able to delete a single order.
- **FR-012**: A Manager/Admin MUST be able to delete all deletable orders in one action.
- **FR-013**: On production, only an order that has never been claimed MUST be deletable, and the server MUST refuse any other. Locally and on stage, an order in any status MUST be deletable, including one that is claimed, picked, packed or has unresolved picking issues.
- **FR-014**: Every deletion MUST require an explicit confirmation first. The confirmation MUST state the order number, or for all orders how many will be deleted (and on production how many will be kept as touched), and whether any of them is claimed or has unresolved picking issues.
- **FR-015**: Every deletion MUST record the Manager/Admin who did it and the time. The record MUST NOT contain customer details.
- **FR-016**: Deletion MUST be soft. A deleted order MUST be hidden from every screen and count, and MUST keep its lines, claims, pick outcomes, picking issues, packing details and their employees and times, unchanged. Its stored packing slip MUST be removed at deletion; the log of who retrieved that slip MUST be kept.
- **FR-017**: A deleted order MUST NOT count as already imported. Importing it again by either path MUST create a fresh, unpicked copy as a new order, and MUST leave the deleted order and its history unchanged.
- **FR-018**: An employee acting on an order that was deleted MUST be told it no longer exists. No action on a deleted order may succeed.
- **FR-019**: Deleting orders MUST NOT change employees or their records, and MUST NOT change import history.
- **FR-020**: On production, deletion MUST never change or hide the pick history of an order that anyone has claimed.
- **FR-021**: Deleting a single order MUST be offered from a per-order menu on **Browse Orders**, not as a button shown on every row. Where an order can't be deleted (production, already claimed), its menu MUST say why instead of offering the action.
- **FR-022**: The single-order confirmation MUST name the order, and its buttons MUST say what they do ("Delete order" and "Keep order"). The delete button MUST be styled as destructive and MUST NOT be the default choice. The warnings about a current claim or an unresolved picking issue MUST appear only when they apply.
- **FR-023**: Deleting all orders MUST require the Manager/Admin to type `DELETE` before the delete button works, in addition to the counts in FR-014.

### Key Entities

- **Mock order set**: a fixed collection of invented TCGplayer-shaped orders, with lines covering the high-risk cases: quantity greater than one, foil and other variants, condition and printing wording, a line with no collector number, and orders that aren't open.
- **Deleted order**: an order marked as deleted, with who deleted it (a Manager/Admin) and when. It is hidden from every screen but keeps all of its history. The same TCGplayer order number can then exist once visibly, in the fresh copy, and any number of times as deleted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer on a fresh local setup with no TCGplayer keys can go from pressing **Get new orders** to a packed mock order without any extra setup step.
- **SC-002**: In 100% of stage and production runs, no mock order is ever imported, including with deliberately wrong configuration.
- **SC-003**: Local runs make zero requests to TCGplayer, with or without keys configured.
- **SC-004**: Every high-risk line type that the API import supports appears in at least one mock order, so each one can be checked by eye locally.
- **SC-005**: A Manager/Admin can clear all orders from inside the app, with no database access, in under a minute.
- **SC-006**: 100% of delete attempts by employees who are not Manager/Admin are refused, whether made through the screen or sent directly.
- **SC-007**: Every deleted order can be traced to who deleted it and when.
- **SC-008**: On production, no order that anyone has claimed ever loses or hides its pick history through deletion.
- **SC-009**: Every deleted order still answers "who picked this order?" (PRD §34) from its stored history.

## Assumptions

- "Local" means the app running in the Development environment on a developer's machine. Stage and production never run as Development.
- The mock order set reuses or matches the synthetic fixtures already used by the automated tests (020), so there is one set of invented TCGplayer data to maintain.
- The developer's local app points at its own dedicated database, never the stage or production database, so mock orders stay local.
- PDF packing-slip upload is unchanged and keeps working locally.
- The existing "not set up here" message stays exactly as it is for stage and production without keys.
- The mock-order part adds no change for pickers, packers or managers on stage or production. Order deletion is the only part production staff can see, and only Manager/Admins see it.
- "Manager/Admin" is the existing single role (PRD §9.4). No new role is added.
- There is no screen for viewing or restoring deleted orders. Their history stays stored for when it's needed, and an order deleted by mistake is brought back by importing it again.
- Selecting several orders with checkboxes, and an undo toast after deleting, are out of scope (Product Owner decision, 2026-10-10). Deletion is one order or all orders.
- "Production" means the production deployment. Local and stage count as non-production for the deletion rules.
- Deleting an order changes nothing on TCGplayer. It only removes the order from this app.
