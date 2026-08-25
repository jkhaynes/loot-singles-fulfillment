# Research: Order Picking Detail — Additional Packing-Slip Fields

## 1. Extending the existing read projection rather than adding a new one

**Decision**: Add `Status` to the existing `OrderDetail` record and `ProductLine`, `CollectorNumber`,
`Rarity` to the existing `OrderLineDetail` record (`backend/src/LootSingles.Application/Orders/OrderDetail.cs`),
rather than introducing a second projection, a versioned response, or a new endpoint.

**Rationale**: All four fields are already columns on the persisted `Order`/`OrderLine` entities
(feature 001) and are already loaded by `OrderRepository.GetByIdAsync`'s existing query — this is
purely a wider `Select` projection over data already in scope, consistent with Constitution
Principle XIII (no abstraction without a concrete purpose). `Status` already has an established
serialization precedent: it's returned today by `GET /api/orders` (`OrderResponse.Status`) using
the app's global camelCase `JsonStringEnumConverter`, so the detail endpoint will serialize it
identically (e.g. `"ready"`) with no new conversion logic.

**Alternatives considered**: A separate "extended detail" endpoint or response version — rejected;
nothing about this feature is optional or versioned, it simply completes the field set the existing
endpoint should have returned, and two endpoints returning overlapping order-detail data would
duplicate the same query for no benefit.

## 2. Optional field handling for `Rarity`

**Decision**: Render `Rarity` using the exact same "omit the field when absent" treatment
`OrderDetailPage.tsx` already applies to `Variant` (feature 007, FR-009's precedent) — no dash,
no "N/A" placeholder, no empty label.

**Rationale**: `Rarity` is nullable on `OrderLine` for the same reason `Variant` is (not every
card has one); reusing the identical, already-tested rendering pattern avoids introducing a second
convention for the same kind of optionality.

**Alternatives considered**: A distinct "Rarity: —" placeholder — rejected as an unnecessary second
pattern for a case feature 007 already solved once.

## 3. `ProductLine` and `CollectorNumber` require no optionality handling

**Finding**: Both are `required string` on the `OrderLine` domain entity (feature 001) — every
persisted order line already has both. No absent-value state is possible for either field.

**Decision**: Render both unconditionally, the same way `ProductName`/`Set`/`Condition` are
already rendered unconditionally today.

**Rationale**: Matches the existing required-field rendering pattern; adding conditional-absence
handling for a field that can never be absent would be speculative defensive code the domain model
already rules out (Constitution Principle XIII).

## 4. Order status placement and no new status-specific behavior

**Decision**: Display `Status` next to the existing TCGplayer order identifier in the detail
view's header, as plain text (its existing serialized enum value, e.g. "ready") — no color coding,
no icon, no status-specific branching logic.

**Rationale**: FR-004 only requires the status be visible; feature 007's existing Assumptions
already establish that every order reachable through this view is currently `Ready`, so no
behavior needs to vary by status value yet. Introducing status-specific visual treatment now would
be speculative — a future claiming/picking feature (already anticipated by feature 007's
Assumptions) is the right place to decide that, once other statuses are actually reachable through
this view.

**Alternatives considered**: A colored status badge (as already exists on `DashboardPage`'s
quantity-emphasis convention, unrelated to status) — rejected as speculative for a value that is
always "Ready" today; nothing in this feature's spec calls for a visual treatment beyond visibility.

## 5. No new production logging

**Finding**: Consistent with feature 006/007's established precedent, this remains the same class
of routine, no-side-effect single-order read with no new failure mode — returning four more
already-loaded scalar fields does not introduce a business-meaningful attempt/outcome event.

**Decision**: No new logging is added.

**Rationale**: Directly consistent with feature 007's research.md §2 and the constitution's
proportionality standard for logging (Principle XI, as amended by feature 006).

## 6. Layout: extend the existing attribute grid, don't restructure it

**Decision**: Add the three new per-line fields (`ProductLine`, `CollectorNumber`, `Rarity`) as
additional `<dt>`/`<dd>` pairs inside the existing `.order-detail-line__attributes` grid in
`OrderDetailPage.tsx`/`.css`, and add `Status` as an additional element in the existing
`.order-detail-header` next to the order identifier — rather than redesigning the page's layout.

**Rationale**: Feature 007 already validated (via Playwright, `frontend/e2e/responsive.spec.ts`)
that this grid has no horizontal overflow at a common mobile-phone width with five attributes per
line; adding three more items to an existing `grid-template-columns: repeat(2, minmax(0, 1fr))`
auto-flowing grid is a bounded, low-risk change that the same responsive test can re-verify, rather
than a new layout mechanism.

**Alternatives considered**: A collapsible "more details" section — rejected as unnecessary
complexity; FR-006 only requires the added fields remain visible and legible, not that they be
hidden by default, and the existing grid already has room to grow.

## 7. Playwright validation

**Decision**: Extend the existing `frontend/e2e/order-detail.spec.ts` scenario (feature 007) to
also assert the product line, collector number, and status are visible, and extend
`frontend/e2e/responsive.spec.ts`'s existing order-detail mobile-viewport check implicitly (no new
test needed — it already asserts no horizontal scroll on the same page, now with more content).

**Rationale**: Reuses the existing critical-flow scenario and existing mobile-viewport check rather
than introducing new Playwright specs for what is fundamentally the same user flow with a larger
field set, consistent with feature 007's established E2E footprint.
