# Feature Specification: Login Page and Dashboard UI Design

**Feature Branch**: `003-login-dashboard-ui`

**Created**: 2026-08-21

**Status**: Draft

**Input**: User description: "I want to design the UI for the login page and dashboard"

## Clarifications

### Session 2026-08-21

- Q: How should this feature handle the Dashboard's In Progress, Needs Attention, and Picked sections, whose underlying order states (an order being actively picked, an order with an unresolved picking problem, a completed pick) don't exist yet since order claiming and the picking workflow are separate, not-yet-built features? → A: Show live data for the Ready section only, since that order state already exists; the other three sections show an explicit "not yet available" placeholder until the features that create those order states are built. No backend changes beyond reading existing order data are introduced by this feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Employee Sees a Polished, On-Brand Login Screen (Priority: P1)

An employee opens the application and is greeted by a clear, intentionally designed login screen — not the current bare, unstyled form — so that logging in feels like using a real internal tool rather than a placeholder page.

**Why this priority**: The login screen is the first thing every employee sees on every session. It already works functionally (per the Employee Authentication feature), but currently has no real visual design. This is the entry point to the entire application and the most-seen screen in the product.

**Independent Test**: Load the login screen on both a phone-sized and a desktop-sized viewport and confirm it presents a clear, legible, on-brand layout with usable username/PIN fields and clearly distinguished error states, with no change to the underlying login behavior.

**Acceptance Scenarios**:

1. **Given** an employee opens the application, **When** the login screen loads, **Then** it presents the username field, PIN field, and submit action in a clear, legible, intentionally designed layout.
2. **Given** an employee submits an incorrect username or PIN, **When** the generic "invalid credentials" rejection is shown, **Then** it is visually presented as an error in a way that is immediately noticeable without reading fine print.
3. **Given** an employee submits credentials for a locked account, **When** the distinct "account locked" message is shown, **Then** it is visually distinguishable from the generic invalid-credentials error so the employee understands these are different situations.
4. **Given** an employee is using a small mobile screen, **When** they view the login screen, **Then** every field and control remains fully usable without horizontal scrolling, overlapping content, or elements that are too small to tap accurately.
5. **Given** an employee successfully logs in, **When** authentication completes, **Then** no part of the existing login/session behavior (fields submitted, validation, how a session is established) changes as a result of this feature.

---

### User Story 2 - Employee Sees an At-a-Glance Order-Picking Dashboard After Login (Priority: P2)

An authenticated employee lands on a Dashboard that summarizes the current state of order picking, so they can see at a glance what's ready to pick, what's in progress, and what needs attention — the V1 dashboard concept described in the product requirements.

**Why this priority**: This gives every login a purposeful destination and delivers real at-a-glance value once an employee is authenticated, but the application already functions (login → a simple authenticated placeholder) without it, so it is valuable but not as foundational as Story 1.

**Independent Test**: Log in as an employee and confirm the Dashboard displays sectioned order-status information (Ready, In Progress, Needs Attention, Picked) that is legible and usable on both mobile and desktop viewports.

**Acceptance Scenarios**:

1. **Given** an authenticated employee, **When** they land on the application after login, **Then** they see a Dashboard organized into the four order-status groupings: Ready, In Progress, Needs Attention, and Picked.
2. **Given** there are orders in the Ready status, **When** the Dashboard displays the Ready section, **Then** each order is shown with enough summary detail (order identifier, product count, card/quantity count) to be recognizable at a glance.
3. **Given** there are zero orders in the Ready status, **When** the Dashboard displays the Ready section, **Then** it shows a clear empty-state message rather than an ambiguous blank area.
4. **Given** the Dashboard is displayed, **When** an employee views the In Progress, Needs Attention, and Picked sections, **Then** each section makes clear that its data is not yet available, rather than showing a fabricated or misleading zero/placeholder count (see Assumptions).
5. **Given** an employee is using a small mobile screen, **When** they view the Dashboard, **Then** all four sections remain legible and usable without horizontal scrolling.
6. **Given** an authenticated employee on the Dashboard, **When** they log out, **Then** they return to the login screen, consistent with existing session-ending behavior.

---

### Edge Cases

- What happens when the Ready section has a very large number of orders? The layout must remain usable (e.g., scrollable within its section) rather than breaking or pushing other sections off-screen.
- What happens when an order's product/card quantity is unusually high? Per this product's existing safety emphasis on quantity greater than one, any quantity shown in an order summary must remain clearly legible, not truncated or visually de-emphasized.
- What happens when a Manager/Admin (rather than a Picker) logs in? They see the same Dashboard layout in this feature's scope (see Assumptions).
- What happens on the very first login after this feature ships, before any orders have been imported? The Dashboard shows the empty state for Ready and the not-yet-available treatment for the other three sections, not an error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The login screen MUST present a clear, intentionally designed layout for the username field, PIN field, and submit action, without altering the underlying authentication behavior, fields, or session mechanics already defined by the Employee Authentication feature.
- **FR-002**: The login screen MUST visually distinguish the generic invalid-credentials rejection from the distinct locked-account rejection, so an employee can tell these two situations apart without reading the message text carefully.
- **FR-003**: The system MUST present a Dashboard screen immediately after a successful login, organized into four groupings: Ready, In Progress, Needs Attention, and Picked.
- **FR-004**: The Dashboard's Ready section MUST reflect the actual current count and per-order summary (order identifier, product count, card/quantity count) of orders in the Ready status, sourced from real order data.
- **FR-005**: The Dashboard's In Progress, Needs Attention, and Picked sections MUST clearly communicate that their underlying data is not yet available, rather than displaying a fabricated, guessed, or silently-zero count, since the order states behind these sections do not exist until the future order-claiming and picking-workflow features introduce them (`/speckit-clarify` 2026-08-21).
- **FR-006**: Both the login screen and the Dashboard MUST be fully usable on a mobile phone-sized viewport and a desktop-sized viewport, as a single responsive experience rather than separate designs or codebases.
- **FR-007**: The Dashboard MUST NOT display any customer information beyond what is already present in the order data it summarizes.
- **FR-008**: An authenticated employee MUST be able to log out from the Dashboard, ending their session per existing logout behavior.
- **FR-009**: The Dashboard MUST be shown to an authenticated employee regardless of role (Picker or Manager/Admin), using the same layout for both in this feature's scope.

### Key Entities *(include if feature involves data)*

- **Order Summary Row**: A Dashboard-only presentation concept, not a new stored entity — represents one order for display purposes (order identifier, product count, total card/quantity count, status grouping). Derived from existing order data; this feature does not change how orders are stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An employee can tell whether a login attempt succeeded, failed generically, or failed due to lockout within 2 seconds of seeing the response, without needing to read the message text closely.
- **SC-002**: 100% of login-screen and Dashboard views render without horizontal scrolling, overlapping content, or illegible text on both a representative mobile viewport and a representative desktop viewport.
- **SC-003**: An employee can state the number of Ready-to-pick orders within 3 seconds of the Dashboard finishing loading, without manually counting rows.
- **SC-004**: 0 instances of the Dashboard displaying a fabricated or misleading count for an order status that is not yet tracked by the system.
- **SC-005**: 100% of successful logins land the employee on the Dashboard with no intermediate blank or broken-looking screen under normal conditions.

## Design Direction

A Product Owner-confirmed visual design language applies to this feature: see [`docs/decisions/visual-design-language.md`](../../docs/decisions/visual-design-language.md). Key points most relevant to this feature's two screens:

- Dark mode by default, using charcoal/slate surfaces rather than pure black.
- A single accent color (Loot green) reserved for primary actions, active states, success, and key highlights; the rest of the palette stays neutral.
- No bright/neon "gaming" styling — the target feel is a modern card shop.
- Soft rounded corners, subtle borders, restrained shadows, layered surfaces.
- Clean, highly readable typography; operational data (order counts, statuses) uses a straightforward sans serif.
- Cards/panels may subtly reference trading cards (proportions, borders, hover, stacked treatments) without becoming literal card replicas.
- Fast scanning takes priority over decorative density.
- Mobile and desktop are designed together as one responsive experience (Constitution VIII), with comfortably tappable controls.
- Card images are used as identifiers where available, but the UI remains fully usable when no image exists.
- Animation stays subtle and functional (hover, selection, state transition) — no decorative motion.

The design language document also records status-color and interaction-target guidance intended for the future order-claiming and picking-workflow features; only the points above are in scope for this feature's two screens.

A Loot Card Shop brand mark (a d20-and-helmet line-art logo) now exists and is recorded in the same document, along with its practical implications for a dark-mode-first UI (it needs a light-stroke variant for dark surfaces, and vectorizes cleanly to SVG). The login screen is this feature's natural first place to apply it; exact placement and treatment is a design/plan-level decision, not a functional requirement.

## Assumptions

- This feature is a visual and interaction design pass over the login screen and a new Dashboard screen; it does not change the authentication behavior, functional requirements, or session mechanics already established by the Employee Authentication feature.
- No interaction or navigation is added from Dashboard rows in this feature (e.g., selecting a Ready order does not open a picking flow yet) — that behavior belongs to the future order-claiming and picking-workflow features referenced in the product requirements.
- The Dashboard is the same for both Picker and Manager/Admin roles in this feature's scope; the product requirements do not describe a role-differentiated dashboard, and none is introduced here.
- High-level visual identity direction (dark-mode-first, accent color usage, corner/shadow/typography treatment) is now a confirmed Product Owner decision — see Design Direction above. Specific implementation values (exact hex codes, spacing scale, component-level styling) are not yet mandated and will be recorded in the technical plan rather than this specification.
- The product requirements describe the dashboard's four groupings (Ready, In Progress, Needs Attention, Picked) as a "potential V1 dashboard" whose "exact design remains subject to UX validation" — this feature is that validation for the Ready section, which already has real underlying data; the other three groupings depend on order states that do not exist until future features introduce them.
