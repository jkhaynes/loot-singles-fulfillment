# Feature Specification: Mobile Picking Experience

**Feature Branch**: `016-mobile-picking`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "Mobile picking experience. Pickers working from a phone get a focused view showing one product at a time; desktop defaults to the full list. Either view is reachable from the other on any device, and a deliberate choice persists for that employee. Moving between products never records an outcome. An order's lines are grouped by set; sets are grouped by game and ordered alphabetically within a game. Explicit set transitions, a guard against leaving a set with unresolved products, progress in physical cards including position within the set, claiming as an explicit action on the order, and a dashboard that offers to resume an order the employee already holds. Implements PRD v0.4 §8, §10, §12.1, §13, §13.1, §13.2, §18."

## Clarifications

### Session 2026-09-20

- Q: In what order do the games themselves appear within an order? → A: Alphabetically by
  game name. Consistent with the alphabetical rule already chosen for sets within a game,
  needs no configuration, and stays stable as games are added. (FR-004)
- Q: Does a picker's chosen view follow them across devices, or is it remembered per
  device? → A: Per device. (FR-010)

**Note on FR-010 and PRD §8.** §8 says a deliberate view choice "MUST persist for that
employee", which reads as following the employee across devices. The Product Owner decided
per-device instead, because an employee-wide preference defeats §8's own size-based default
— a picker who once chose the list at a desktop would then be given the list on their phone,
which is the behaviour this feature exists to remove.

This is a confirmed Product Owner decision and therefore sits above the PRD in the
source-of-truth hierarchy, so it governs. §8's wording should be corrected to say the choice
persists per device when the PRD is next amended; it is recorded here so the two are not
left quietly disagreeing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Walk to each storage box once (Priority: P1)

A picker opens an order containing cards from several games and several sets. The
application presents the products grouped by set, with the sets from one game kept
together, so the picker can clear one storage box before moving to the next instead of
walking back and forth between sections of the shop.

**Why this priority**: This is the largest reduction in physical work in the feature and
it applies to every picking view, including the list view that exists today. Order lines
currently render in the arbitrary order they arrived from TCGplayer, so every picker walks
further than necessary on every order. It is also a prerequisite for User Story 2 — a
focused view cannot announce "box finished" until the products are grouped into boxes.

**Independent Test**: Import an order whose lines span at least two games and two sets per
game, open it, and confirm the products are presented grouped and ordered as specified.
Delivers the reduced-walking benefit on its own, in the existing list view, with no other
part of this feature present.

**Acceptance Scenarios**:

1. **Given** an order with products from two different games, **When** the picker opens
   the order, **Then** all products from one game appear together before any product from
   the other game.
2. **Given** an order with three sets within one game, **When** the picker opens the order,
   **Then** the sets appear in alphabetical order by set name and every product of a set
   appears contiguously within it.
3. **Given** an order whose products all belong to one set, **When** the picker opens the
   order, **Then** the products are presented as a single group with no empty or redundant
   grouping shown.
4. **Given** an order line whose set cannot be determined from the imported data, **When**
   the picker opens the order, **Then** the line is still presented and reachable, and is
   not silently dropped from the order.

---

### User Story 2 - Pick one card at a time on a phone (Priority: P2)

A picker holding a phone in one hand and cards in the other works through an order one
product at a time. Each product fills the screen with its identity, quantity and image.
Moving to the next product never records anything; only a deliberate action records a pick
or an issue. When the last product in a box is resolved, the application says so and names
the next box. If the picker reaches the end of a box while products in it are still
unresolved, the application says that too, rather than letting them walk away believing
the box is done.

**Why this priority**: This is the change the Product Owner most wants, and it addresses
the "flow doesn't feel good" complaint directly. It is second only because it depends on
the grouping delivered by User Story 1 to define what a box is.

**Independent Test**: With grouping in place, open an order on a phone-sized viewport and
work through it in the focused view — advancing, going back, recording a pick, recording
an issue, finishing a set, and attempting to leave a set with work outstanding.

**Acceptance Scenarios**:

1. **Given** the picker is on a phone-sized screen, **When** they open a claimed order,
   **Then** the focused view showing one product at a time is presented by default.
2. **Given** the picker is on a desktop-sized screen, **When** they open a claimed order,
   **Then** the full list view is presented by default.
3. **Given** the picker is in either view, **When** they choose the other view, **Then**
   the other view is presented and that choice is remembered for their next order.
4. **Given** the picker is viewing a product, **When** they move to the next or previous
   product by any means, **Then** no pick and no issue is recorded for the product they
   left.
5. **Given** the picker is viewing a product, **When** they explicitly confirm the pick,
   **Then** the pick is recorded for that product only.
6. **Given** a device with no touch screen or a picker who does not swipe, **When** they
   use the on-screen navigation controls, **Then** they can reach every product in the
   order without needing to swipe.
7. **Given** every product in the current set is resolved, **When** the picker advances
   past the last one, **Then** the application states that the set is finished and names
   the next set together with its product and physical card counts.
8. **Given** at least one product in the current set is unresolved, **When** the picker
   advances past the last product in that set, **Then** the application states that the
   set is not finished, lists the unresolved products, and requires the picker to choose
   between returning to them, reporting what is missing, or leaving the set.
9. **Given** the picker chooses to leave a set with unresolved products, **When** they
   confirm, **Then** those products remain unresolved and the order is not represented as
   fully picked.
10. **Given** the picker is anywhere in the order, **When** they look at the progress
    display, **Then** it shows products resolved out of total, physical cards accounted
    for out of total, and their position within the current set.

---

### User Story 3 - Start an order from the order itself (Priority: P3)

A picker browsing orders opens one to look at it, decides to work on it, and claims it
from that screen. A picker who already holds an order is offered that order to resume
rather than being offered a new one and then told they cannot have it.

**Why this priority**: It closes a dead end — an order opened from the dashboard currently
has no way to start work, so the picker must back out and use a different screen — but the
picker can reach their work today by another route, so it is less urgent than the two
stories above. It is fully independent of them and could be delivered in any order.

**Independent Test**: Open an unclaimed order without claiming it, confirm nothing changes;
claim it from that screen; then return to the dashboard while holding it and confirm the
dashboard offers to resume rather than to start another.

**Acceptance Scenarios**:

1. **Given** an unclaimed order, **When** the picker opens it, **Then** the order is shown
   without being claimed and no other picker is prevented from claiming it.
2. **Given** the picker is viewing an unclaimed order they hold no claim on, **When** they
   choose to claim it, **Then** the claim is granted to them and picking actions become
   available.
3. **Given** two pickers viewing the same unclaimed order, **When** both attempt to claim
   it, **Then** exactly one succeeds and the other is told who holds it.
4. **Given** the picker already holds an active claim, **When** they view the dashboard,
   **Then** the dashboard offers to resume the order they hold instead of offering to start
   another.
5. **Given** the picker already holds an active claim, **When** they open a different
   unclaimed order, **Then** the application explains that they already hold an order
   rather than presenting a claim action that will fail.

---

### Edge Cases

- A product's set is missing or empty in the imported order data. The product must remain
  visible and pickable; grouping must not hide it.
- Two different games contain sets with the same name. Grouping by game must keep them
  apart rather than merging them into one box.
- An order contains exactly one product. No set transition can occur, and the focused view
  must still present it and allow it to be resolved.
- A picker reaches the last product of the last set. There is no next box to name, and the
  application must not present an empty transition.
- The picker's claim is released by a manager while they are working in the focused view.
  Picking actions must stop being available and the picker must be told.
- A picker changes view preference on one device and then picks on another. The second
  device keeps its own preference, or its size-based default if it has none.
- The picker resolves the final unresolved product in an earlier set by navigating back to
  it. The set it belongs to must stop being reported as unfinished.

## Requirements *(mandatory)*

### Functional Requirements

**Grouping and ordering**

- **FR-001**: The application MUST group an order's products by set, so that every product
  belonging to one set is presented contiguously.
- **FR-002**: The application MUST group sets by game, so that all sets belonging to one
  game are presented before any set of another game.
- **FR-003**: The application MUST order the sets within a game alphabetically by set name.
- **FR-004**: The application MUST order games alphabetically by game name.
- **FR-005**: The application MUST present a product whose set is missing or unrecognised
  without dropping it from the order.
- **FR-006**: Grouping and ordering MUST derive from the game and set recorded on the order
  line, which is authoritative imported data, and MUST NOT depend on catalog enrichment.

**Focused picking**

- **FR-007**: The application MUST provide a focused view that presents one product at a
  time, and a list view that presents the whole order.
- **FR-008**: The application MUST default to the focused view on a phone-sized screen and
  to the list view on a desktop-sized screen.
- **FR-009**: Both views MUST be reachable from the other on any device.
- **FR-010**: When an employee deliberately chooses a view, the application MUST remember
  that choice **on the device where it was made** and apply it there in place of the
  size-based default. A choice made on one device MUST NOT change the view presented on
  another.
- **FR-011**: Moving between products MUST NOT record a pick, an issue, or any other
  outcome for any product.
- **FR-012**: Only an explicit, deliberate action MUST record a pick or an issue.
- **FR-013**: Every product in an order MUST be reachable using on-screen controls, without
  requiring a swipe gesture.
- **FR-014**: The picker MUST be able to return to any product they have already passed and
  change nothing by doing so.

**Set transitions and the unresolved guard**

- **FR-015**: When every product in a set is resolved and a further set remains, the
  application MUST present an explicit transition naming the next set and stating its
  product count and physical card count.
- **FR-016**: When the picker reaches the end of a set that still contains unresolved
  products, the application MUST state that the set is unfinished and list the unresolved
  products.
- **FR-017**: In that situation the application MUST require the picker to choose between
  returning to an unresolved product, reporting what is missing, and leaving the set.
- **FR-018**: The application MUST NOT present a set as finished while any product in it is
  unresolved.
- **FR-019**: Leaving a set with unresolved products MUST leave those products unresolved,
  and MUST NOT cause the order to be represented as fully picked.

**Progress**

- **FR-020**: The application MUST show the picker how many products are resolved out of
  the order's total.
- **FR-021**: The application MUST show how many physical cards are accounted for out of
  the order's total, counting quantity rather than product lines.
- **FR-022**: The application MUST show the picker's position within the current set.

**Claiming**

- **FR-023**: Viewing an order MUST NOT claim it.
- **FR-024**: The application MUST offer an explicit claim action on the order itself when
  the viewing employee holds no active claim and the order is available.
- **FR-025**: Claiming MUST remain exclusive and enforced server-side, such that exactly one
  of two simultaneous claim attempts on the same order succeeds.
- **FR-026**: When an employee already holds an active claim, the dashboard MUST offer to
  resume that order rather than offering to start another.
- **FR-027**: When an employee holding a claim views a different available order, the
  application MUST explain that they already hold an order rather than presenting a claim
  action that is known to fail.
- **FR-028**: Pick Next Order MUST continue to claim and open the next available order in a
  single action.

### Key Entities

- **Order**: unchanged by this feature. Gains no new stored state beyond what claiming
  already records.
- **Order line**: unchanged. Its existing game, set, quantity and resolved-outcome
  attributes are what grouping, progress and the unresolved guard read.
- **Set group**: a presentation grouping of an order's lines sharing one game and set,
  carrying the set's name, its product count and its physical card count. Derived for
  display; not necessarily stored.
- **View preference**: a deliberate choice between the focused and list views, held per
  device and applied in place of that device's size-based default.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For any order, the number of times a picker must move between game sections
  of the shop equals the number of distinct games in that order — never more.
- **SC-002**: A picker can resolve every product in an order using only on-screen controls,
  without performing a swipe gesture.
- **SC-003**: No sequence of navigation actions, without an explicit confirm or report
  action, changes any product's recorded outcome.
- **SC-004**: A picker who reaches the end of a set with unresolved products is always told
  so before they can move on to another set.
- **SC-005**: At every point in an order, the picker can state how many physical cards
  remain and how many remain in the box they are standing at, from the screen alone.
- **SC-006**: Two pickers attempting to claim the same order at the same moment result in
  exactly one claim, with the other picker told who holds it.
- **SC-007**: A picker who holds an order is never offered an action to start a different
  one that then fails.

## Assumptions

- **One claim per employee** is already enforced server-side by feature 013 and is treated
  as established behaviour here, not re-specified.
- **A product is "resolved"** when it has a recorded pick outcome — either picked, or
  carrying a reported issue. A product with no recorded outcome is unresolved. This matches
  the behaviour delivered by feature 015.
- **Physical card counts** come from the quantity already recorded on each order line, which
  is authoritative imported data.
- **Set release dates are not available** and set ordering within a game is therefore
  alphabetical, per PRD §13.1. Loot's shelves are ordered newest-to-oldest, so alphabetical
  ordering will not match the aisle direction; this was accepted deliberately because the
  expensive walk is between game sections, not along one.
- **The set transition presentation** is specific to the focused view. The list view shows
  the same grouping without interstitial screens.
- **Existing picking actions are reused.** Recording a pick and reporting an issue behave as
  feature 015 delivered them; this feature changes where and how they are presented, not
  what they do.
- **Screen size determines the default view**, not device type detection.

## Out of Scope

Deferred to later features, and explicitly not part of this one:

- The order lifecycle states added in PRD §20.1 and §20.2 — Awaiting Customer Decision,
  Packed, Cancelled, and the written-off and substituted line outcomes.
- The counts-only dashboard redesign in PRD §21, and the dedicated per-state pages it links
  to. This feature changes the dashboard only to offer resuming a held order (FR-026).
- The pick completion screens in PRD §22.
- Order hand-off and labelling in PRD §22.1, including the sleeve label and the packing
  desk.
- The ability for a picking issue to record a card found instead, in PRD §19.1.
- Release-date ordering of sets, deliberately deferred by PRD §13.1.
