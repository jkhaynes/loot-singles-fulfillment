# Research: Order Picking Detail View

## 1. Identifying an order in the detail route

**Decision**: Route by the order's existing internal identifier, `orderId` (int) — `GET /api/orders/{orderId}` and frontend route `/orders/:orderId`.

**Rationale**: Both existing list views (`GET /api/orders`, `GET /api/dashboard`) already return `orderId` on every row (`OrderListItem.OrderId`, `OrderSummaryResponse.OrderId`), so the frontend already has this value in hand for every row it renders — no extra lookup is needed to build a link. It's also already the primary key, so the repository query is a direct, indexed lookup.

**Alternatives considered**: Routing by `TcgplayerOrderId` (the business identifier) — rejected as unnecessary; it's a string, not indexed as the primary key, and offers no benefit over the id both list views already expose. Nothing about this feature requires the URL itself to be business-meaningful.

## 2. No new production logging

**Finding**: Feature 006 (production logging) established, and the constitution (Principle XI, as amended) now requires evaluating, whether a new or changed behavior warrants a log entry — and explicitly excluded order-listing/dashboard reads from that feature's scope because they are "simple queries with no business-meaningful failure mode of their own (an unexpected error already surfaces as an unhandled 500)."

**Decision**: This feature adds no new logging. A single-order detail read is the same class of operation as the existing list reads that precedent already covers — no new failure mode is introduced.

**Rationale**: Consistency with an already-decided, directly analogous precedent; adding logging here without a genuine new failure mode would contradict FR-009's proportionality standard for logging established in 006.

**Alternatives considered**: Logging "not found" lookups — rejected; a not-found result for a stale link or an edited URL is expected, low-stakes, user-facing behavior (FR-008), not a system failure an operator needs to diagnose from console output.

## 3. Quantity emphasis: color plus a non-color cue

**Finding**: The existing dashboard already has a quantity-emphasis convention — `data-emphasis="high"` on the quantity cell, styled with an orange/bold color treatment in CSS (`DashboardPage.tsx`/`DashboardPage.css`). Per this feature's clarification, color alone is not sufficient: a picker who cannot perceive the color difference must still be able to tell a line apart.

**Decision**: Reuse the existing `data-emphasis="high"` attribute convention on the quantity value for the same styling hook, and add one non-color cue alongside it — a `×N` prefix/marker rendered as text (e.g., "×4") together with a bold weight, so the emphasis reads correctly in grayscale or under color-blindness simulation without relying on an icon asset.

**Rationale**: Extends an existing, already-proven pattern rather than inventing a new one (Principle XIII); a text marker plus weight is the simplest change that satisfies FR-003 without adding an icon/asset dependency.

**Alternatives considered**: An icon/badge — rejected as unnecessary asset overhead when a text marker achieves the same distinguishability with less to maintain. A numeric-only badge without the "×" — rejected as more ambiguous out of context than a quantity multiplier is used to reading.

## 4. Card image placeholder

**Decision**: A static, neutral placeholder element (no image request, no external call) rendered in the position a card image would occupy, identical for every product line.

**Rationale**: FR-004 requires this never be a guess; the simplest way to guarantee that is to never attempt an image lookup at all in this feature — card enrichment is explicitly deferred (spec.md Assumptions). A static placeholder has no failure mode of its own and needs no test beyond "it always renders."

**Alternatives considered**: A generic icon per product line/game (e.g., a Magic vs. Pokémon icon) — rejected as speculative scope beyond what this feature's spec calls for; the spec only requires a neutral placeholder, not per-game visual differentiation, and that richer treatment belongs with the actual image-enrichment feature this one explicitly defers to.

## 5. Read model shape — no new persisted entity

**Decision**: Add one read-only Application-layer record, `OrderDetail` (and a nested line shape), projected directly from the existing `Order`/`OrderLine` entities via `AsNoTracking()`, mirroring the existing `OrderListItem` projection in `OrderRepository.GetAllAsync`.

**Rationale**: `Order`/`OrderLine` (feature 001) already contain every field this view needs (`TcgplayerOrderId`, and per line: `ProductName`, `Set`, `Variant`, `Condition`, `Quantity`). No schema change, migration, or new entity is needed — this is purely a new read shape over existing data, consistent with Constitution Principle XIII (no abstraction without a concrete purpose).

**Alternatives considered**: Returning the domain `Order`/`OrderLine` entities directly from the API — rejected; the existing codebase's established convention (already used by both `OrderListItem` and `OrderSummaryResponse`) is a dedicated read/response shape per boundary, keeping the API contract decoupled from the persistence model.

## 6. Not-found handling

**Decision**: `GetByIdAsync` returns `null` when no order matches; the controller translates `null` into an HTTP `404`; the frontend's existing loading/error/empty local-state convention (already used identically in `OrdersPage.tsx` and `DashboardPage.tsx`) gains one more explicit state for "not found," distinct from the generic network-error state, so the message can say "order not found" rather than "couldn't load."

**Rationale**: Matches FR-008's requirement for a clear, safe state rather than an unhandled failure, and reuses the exact state-handling shape already established in both existing list pages rather than inventing a new pattern.

## 7. Playwright validation

**Finding**: `docs/development/ai-assisted-development-workflow.md`'s "Picker UI" validation gate requires critical picking flows to receive Playwright validation at both desktop and mobile-phone viewport sizes. This feature is the first picking-specific screen built, and US4/FR-007 require verified responsive usability.

**Decision**: Add one Playwright scenario exercising the full flow — open an order from a list view, land on its detail view, confirm the key content (order id, at least one quantity>1 line with its emphasis, at least one placeholder) is present — run at both a desktop and a mobile-phone viewport, following the existing `LootSingles.E2EHost` pattern already used by prior features' Playwright suites.

**Rationale**: Directly required by the project's own documented validation gate for picker-facing UI; reuses the existing E2E host/test infrastructure rather than introducing a new one.
