# Feature Specification: Exclusive Order Claiming

**Feature Branch**: `013-order-claiming`

**Created**: 2026-08-26

**Status**: Draft

**Input**: User description: "Add exclusive, concurrency-safe order claiming to the fulfillment app, per PRD Section 10 (Order Queue) and Section 11 (Concurrent Picking and Order Claiming), and constitution Principle VI (Server-Enforced Critical Business Rules), which names this as a MUST: 'two employees pressing Pick Next Order or attempting to claim the same order at approximately the same time MUST NOT both succeed in claiming the same order.' This is the deferred follow-up feature 007's own spec explicitly named — 007/008/009 built import, auth, and a read-only order detail view (now with card images), but OrderStatus currently has only a single Ready value; there is no picking workflow yet at all. Per PRD Section 10, V1 supports two entry points: 'Pick Next Order' (system selects) and 'Choose Order' (picker freely selects). Per PRD Section 11: once claimed, exclusive; other pickers see it's in progress, e.g. 'Order #12345 — In Progress · Picking by Sam'. Scope is the claim mechanism and the two queue entry points only — not guided picking, issue reporting, or completion (PRD Sections 12-22, separate follow-on features)."

## Clarifications

### Session 2026-08-26

- Q: How does an employee's claim on an order end? → A: Explicit release only — a picker can release their own claim; no automatic timeout.
- Q: Can one employee hold more than one claimed order at the same time? → A: No — one active claim per employee at a time.
- Q: Does the Manager/Admin role have any claiming capability beyond an ordinary picker? → A: Managers can force-release any employee's claimed order.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pick Next Order Assigns Exclusively (Priority: P1)

As a picker, I want to ask the application to give me the next available order to work, so I can start picking without browsing the full list — and I need to trust that if another picker asks at the same moment, we will never both be handed the same order.

**Why this priority**: This is the feature's entire safety-critical purpose and its first proof point. Without a working, concurrency-safe assignment path, nothing else in the picking workflow can be built on top of this feature.

**Independent Test**: With multiple available (unclaimed) orders, have two employees press "Pick Next Order" at approximately the same time and confirm they are assigned two different orders, each exclusively claimed to the employee who received it.

**Acceptance Scenarios**:

1. **Given** at least one order is available (Ready and unclaimed), **When** a picker with no other active claim presses "Pick Next Order," **Then** the system assigns them one available order and exclusively claims it in their name.
2. **Given** two pickers press "Pick Next Order" at approximately the same time and at least two orders are available, **When** both requests are processed, **Then** each picker is assigned a different order, and neither order ends up claimed by both.
3. **Given** no orders are currently available, **When** a picker presses "Pick Next Order," **Then** the picker is clearly told no orders are available, rather than the request failing unexpectedly or hanging.
4. **Given** a picker already holds an active claim on another order, **When** they press "Pick Next Order," **Then** the request is rejected and the picker is told they must release their current order first — they are not assigned a second order.

---

### User Story 2 - Choose a Specific Order (Priority: P2)

As a picker, I want to freely select a specific available order to work — rather than whatever "Pick Next Order" would assign me — so I can handle situations where I need to work a particular order out of system order, while still being protected from claiming an order someone else has already started.

**Why this priority**: Extends the same safety guarantee proven in User Story 1 to a second, picker-directed entry point named explicitly in the PRD. Lower priority than User Story 1 because "Pick Next Order" is the primary flow; this is an accommodation for exceptions, not the default path.

**Independent Test**: With an order already claimed by one picker, have a second picker attempt to choose that same order and confirm the claim attempt is rejected safely, while choosing a different, unclaimed order succeeds.

**Acceptance Scenarios**:

1. **Given** a specific order is available (Ready and unclaimed) and the picker has no other active claim, **When** a picker chooses that order, **Then** the system exclusively claims it in their name.
2. **Given** a specific order is already claimed by another employee, **When** a picker attempts to choose that same order, **Then** the claim attempt is rejected, the picker is clearly told the order is already being worked (and by whom), and the existing claim is unaffected.
3. **Given** a picker already holds an active claim on another order, **When** they attempt to choose any order, **Then** the request is rejected and the picker is told they must release their current order first.

---

### User Story 3 - See Who Is Already Working an Order (Priority: P2)

As a picker, I want to see when an order is already being worked and by whom, so I don't waste time attempting to start an order someone else has already claimed.

**Why this priority**: Makes the claim mechanism from User Stories 1–2 genuinely useful in a multi-picker environment, rather than a claim that's only enforced silently on the backend. Independently testable and demonstrable on its own once any claim exists.

**Independent Test**: With an order claimed by one employee, have a second employee view the order list and confirm that order is visibly shown as in progress, identifying the employee who claimed it.

**Acceptance Scenarios**:

1. **Given** an order is currently claimed by an employee, **When** any employee views the list of orders, **Then** that order is shown as in progress and identifies which employee is picking it.
2. **Given** an order is not currently claimed by anyone, **When** any employee views the list of orders, **Then** that order is shown as available, with no picker attributed to it.

---

### User Story 4 - Release a Claimed Order (Priority: P1)

As a picker, I want to release an order I've claimed so I can free it up for another picker (or myself, for a different order) — since a claim only ever ends when I explicitly release it, this is the only way an order I started stops being exclusively mine.

**Why this priority**: Without this, an order claimed via User Story 1 or 2 stays claimed indefinitely — including if the picker simply claimed the wrong order or needs to switch tasks. This is essential, not optional, given claims never expire automatically; the claiming feature is not usably complete without it.

**Independent Test**: With an order claimed by a picker, have that picker release it and confirm the order becomes available again (visible as unclaimed, and claimable by any picker, including a different one).

**Acceptance Scenarios**:

1. **Given** a picker holds an active claim on an order, **When** they release it, **Then** the order becomes available (Ready and unclaimed) again, and that picker no longer holds any active claim.
2. **Given** a picker does not hold the claim on a particular order, **When** they attempt to release that order, **Then** the request is rejected — only the claiming employee (or a manager, per User Story 5) can end that claim.

---

### User Story 5 - Manager Force-Releases a Stuck Claim (Priority: P3)

As a manager, I want to force-release an order claimed by another employee, so an order isn't stuck indefinitely if the picker who claimed it can't or forgets to release it themselves (e.g., they end their shift, or made an error).

**Why this priority**: A safety net for the "explicit release only, no timeout" decision (Clarifications) — real, but lower priority than the core claim/release flows, since it's an exception-handling capability rather than part of the everyday picking loop.

**Independent Test**: With an order claimed by one employee, have a manager force-release it and confirm the order becomes available again and the original claimant no longer holds it.

**Acceptance Scenarios**:

1. **Given** an order is claimed by an employee, **When** a manager force-releases it, **Then** the order becomes available (Ready and unclaimed) again, and the original claimant no longer holds any claim on it.
2. **Given** an order is claimed by an employee, **When** a non-manager employee (other than the claimant) attempts to force-release it, **Then** the request is rejected.

---

### Edge Cases

- What happens when two pickers press "Pick Next Order" at approximately the same time and only one order is available? Exactly one of them is assigned that order; the other is told no orders are currently available (not shown an error, and not given a duplicate claim).
- What happens when a picker attempts to choose an order that a second picker claims (via either entry point) in the same instant? The claim attempt that loses the race is rejected safely, exactly as in User Story 2's acceptance scenario 2 — this must hold regardless of which entry point either picker used.
- What happens to an order's claim when the claiming employee's session ends (logs out, or their session expires) without explicitly releasing it? The claim remains in effect — since claims only end via explicit release (Clarifications), the order stays claimed until the same employee logs back in and releases it, or a manager force-releases it (User Story 5).
- What happens if a picker's release and a manager's force-release of the same order are attempted at approximately the same time? Exactly one succeeds; the order ends up released exactly once, not in an inconsistent state, and the other request is told the claim no longer exists to release.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow a picker to request the next available order ("Pick Next Order") and be exclusively assigned one available order in response.
- **FR-002**: The system MUST allow a picker to freely select and claim a specific available order ("Choose Order") from the list of orders.
- **FR-003**: Order claiming MUST be concurrency-safe: when two employees attempt to claim the same order at approximately the same time — whether via "Pick Next Order," "Choose Order," or one of each — at most one of them MUST succeed.
- **FR-004**: An order MUST NOT be claimable by more than one employee at the same time. While an order is claimed, every other employee's attempt to claim it (via either entry point) MUST be rejected without disturbing the existing claim.
- **FR-005**: Every employee MUST be able to see, for any order, whether it is currently claimed and, if so, by which employee.
- **FR-006**: When no order is available to assign, "Pick Next Order" MUST clearly indicate that to the picker rather than failing unexpectedly.
- **FR-007**: Claiming MUST be enforced by the server; the system MUST NOT rely on the frontend as the sole mechanism preventing two employees from claiming the same order (constitution Principle VI).
- **FR-008**: An employee's claim on an order MUST end only through an explicit release action — by the claiming employee themselves (User Story 4) or a manager force-releasing it (User Story 5). The system MUST NOT automatically expire or time out a claim due to inactivity.
- **FR-009**: An employee MUST NOT hold more than one active claim at a time. "Pick Next Order" and "Choose Order" MUST be rejected for an employee who already holds an active claim on another order, with a clear indication that their existing claim must be released first.
- **FR-010**: An employee with the Manager/Admin role MUST be able to force-release any order currently claimed by any employee, ending that claim and returning the order to available. An employee without the Manager/Admin role MUST NOT be able to release a claim other than their own.
- **FR-011**: This feature MUST NOT introduce the guided picking flow, quantity-acknowledgment gestures, picking-issue reporting, or pick completion — those remain separate, future features building on top of claiming.

### Key Entities

- **Order** (existing, feature 001): Gains claim-related state — whether it is currently claimed, and if so which employee holds the claim. No change to any imported/textual order data.
- **Employee** (existing, feature 002): The actor who claims, releases, and is attributed to a claimed order. The existing Manager/Admin role gains the additional force-release capability (FR-010); no new employee data is collected by this feature.
- **Order Claim**: A conceptual association between exactly one order and the one employee currently working it, in effect from the moment a claim succeeds until it is explicitly released (by the claiming employee or a manager). Never more than one active claim per order, and never more than one active claim per employee.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When two employees press "Pick Next Order" at approximately the same time with multiple orders available, they are always assigned two different orders — verified across repeated concurrent attempts, with zero instances of the same order being assigned to both.
- **SC-002**: An employee attempting to claim (via either entry point) an order another employee already holds is always clearly informed the order is unavailable, and never becomes a second claimant of that order.
- **SC-003**: Any employee can determine, for any order, whether it is available or already being worked — and by whom — without needing to attempt a claim to find out.
- **SC-004**: A picker requesting "Pick Next Order" when nothing is available always receives a clear "nothing available" outcome rather than an error or an indefinite wait.
- **SC-005**: A picker can release an order they hold, and it becomes available to any other picker (or themselves, for a different order) immediately afterward.
- **SC-006**: A manager can free up any claimed order without needing the original claimant present, and this never leaves the order in a state claimed by no one yet still unavailable, or claimed by two employees at once.
- **SC-007**: An employee who already holds a claim is always prevented from acquiring a second one, whether attempted via "Pick Next Order" or "Choose Order."

## Assumptions

- "Pick Next Order" assigns orders in a defined, consistent order when the PRD's own prioritization rule is unspecified — oldest-imported-and-still-available order first (first-in, first-out). The PRD explicitly leaves the exact "next" rule open (including whether shipping deadlines should affect it); this is a reasonable V1 default, not a permanent decision, and no shipping-deadline data currently exists on an order to prioritize by even if desired.
- "Choose Order" is the picker selecting from the same set of currently available (Ready, unclaimed) orders already visible via the existing Browse Orders view (feature 007), extended to reflect claim status per User Story 3 — this feature does not introduce a new, separate order-browsing surface.
- This feature adds claim-related state to the Order domain model but does not add, persist, or display any picking-progress data (which cards have been picked, quantities confirmed, etc.) — that remains entirely out of scope, deferred to the guided-picking feature that will build on top of claiming.
- Manager force-release (User Story 5, FR-010) reuses the existing Manager/Admin role already established by feature 002's authentication/employee model — this feature does not introduce a new role or permission concept, only an additional capability for the role that already exists.
- This feature reuses the existing employee authentication and session model (feature 002); no new login, role, or permission concept is introduced.
