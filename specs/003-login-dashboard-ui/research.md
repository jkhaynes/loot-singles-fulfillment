# Research: Login Page and Dashboard UI Design

## 1. Dashboard Backend Surface

**Decision**: One new endpoint, `GET /api/dashboard`, requiring authentication (any role) but no new role restriction. It returns only the Ready section's live data (count + per-order summary). No general-purpose Orders API (e.g., `GET /api/orders?status=X`) is introduced.

**Rationale**: FR-003/FR-004 require the Dashboard to show a real Ready count and per-order summary; FR-005 requires the other three sections to explicitly not fabricate data. Since In Progress/Needs Attention/Picked have no backing order state yet (`/speckit-clarify` 2026-08-21, Option A), there is nothing for the backend to return for them — the frontend renders their placeholder treatment statically, with no corresponding API shape to design or maintain. A dedicated `/api/dashboard` endpoint (rather than a generic, filterable Orders API) matches the actual, current caller (one screen, one need) without inventing a general query surface no requirement calls for (Constitution XIII).

**Alternatives considered**:
- `GET /api/orders?status=Ready` — rejected for now; a generic, filterable Orders API is exactly the kind of speculative generality Constitution XIII warns against when only one filter value (`Ready`) is actually usable today. `IDashboardRepository`'s Ready-only method (§2) can be extended or a general Orders API introduced later, once the order-claiming/picking features give a second real caller and a second real status.

## 2. Read-Model Seam for Future Growth

**Decision**: `LootSingles.Application.Dashboard.IDashboardRepository` (`GetReadyOrderSummariesAsync`), returning `OrderSummary` records (`OrderId`, `TcgplayerOrderId`, `ProductCount`, `TotalQuantity`) — a read-model, not a persisted entity. A thin `DashboardService` sits between the controller and repository.

**Rationale**: `OrderSummary` is derived data (line count, quantity sum), not a new stored concept — it belongs in `LootSingles.Application`, not `LootSingles.Domain`, mirroring how `EmployeeManagementResult` and similar typed results already work in this codebase. The repository interface keeps `LootSingles.Application` free of a direct EF Core dependency (Constitution IX/XII), matching the established `IEmployeeRepository`/`IImportPersistence` pattern. `DashboardService` is thin today (it does nothing but forward to the repository), but per Constitution XII's guidance to provide an extension boundary at a genuine, already-foreseeable variation point: the Dashboard's spec itself, the PRD (§21), and the design-language decision doc's "Notes for future features" all describe three more sections (In Progress, Needs Attention, Picked) that will attach to this exact concept once order-claiming/picking exist. Introducing the seam now — without building those sections' logic — is the appropriate boundary, not speculative machinery, because the next foreseeable case is not hypothetical; it is already named in three separate approved artifacts.

**Alternatives considered**:
- Controller calling `IDashboardRepository` directly, skipping `DashboardService` — rejected for consistency with every other controller in this codebase (`AuthController`/`EmployeesController` both depend on an Application-layer service, never a raw repository), and because the service is exactly where the other three sections' future logic will live without requiring a new architectural layer to be introduced later.
- A generic `IOrderRepository` covering all order-read needs — rejected as premature; nothing outside the Dashboard reads orders yet, and a Dashboard-scoped interface can be broadened later without breaking callers, per Constitution XIII's preference for duplication over premature abstraction until a second concrete need exists.

## 3. Brand Mark Asset Preparation

**Decision**: Produce a production SVG of the Loot Card Shop logo (`frontend/src/assets/lcs-logo.svg`) with light strokes suitable for the dark-mode-first surfaces, derived from the source raster (`docs/decisions/assets/lcs-logo-source.png`). Used on the login screen; a favicon-sized export is also produced for the app's favicon.

**Rationale**: `docs/decisions/visual-design-language.md`'s Brand Mark section already identifies that the source asset (black line art on a light background) is unusable as-is on a charcoal/slate surface. The login screen (US1) is this feature's natural first consumer of the mark. Producing the asset once, as a small task within this feature, avoids every future feature that touches branded chrome (app header, etc.) re-deriving it independently.

**Alternatives considered**:
- Use the raw source PNG directly with a CSS filter (e.g., `invert()`) to fake a light variant — rejected; a filter-based inversion is fragile (breaks if the source asset changes) and produces a lower-quality result than a purpose-made light-stroke SVG export for a geometric line-art mark that vectorizes cleanly.
- Defer logo use entirely to a later feature — rejected; the spec explicitly names the login screen as the natural first place to apply the mark, and the source asset already exists.

## 4. No Client-Side Routing Introduced

**Decision**: Continue the existing conditional-render pattern in `App.tsx` (unauthenticated → `LoginPage`; authenticated → now `DashboardPage` instead of the current placeholder). `react-router-dom` remains an installed-but-unwired dependency, as it already was after feature 002.

**Rationale**: Neither the spec nor the PRD describes a second, independently addressable route for this feature — there are exactly two states (logged out, logged in), which the existing pattern already models correctly. Wiring a router now, with nothing yet to route between, would be infrastructure introduced because a library is already installed rather than because an approved requirement calls for it (Constitution XIII). The next feature that needs a real second URL (e.g., an order detail page during picking) is the appropriate place to introduce routing.

**Alternatives considered**:
- Wire up `react-router-dom` now with a `/dashboard` route — rejected per the rationale above; revisit when a second real destination exists.

## 5. Dark Mode Only (No Light Theme)

**Decision**: Implement one dark theme as the only theme; do not build a light-mode variant or a theme toggle.

**Rationale**: The design-language decision doc states "dark mode is the default, not an afterthought" and describes one intentional dark aesthetic throughout — it does not ask for two themes or a switch between them, and neither does the spec. Building an unused light theme would be exactly the kind of unrequested product surface Constitution II warns against. The existing scaffold's `@media (prefers-color-scheme: dark)` block (which currently flips a light-default palette to dark) is inverted: the token system in `styles/tokens.css` defines the dark values as the unconditional default, with no light alternative defined at all.

**Alternatives considered**:
- Keep the scaffold's light-default/dark-media-query structure and simply swap which side is "default" — rejected; still carries unused light-theme values and a media query with nothing to toggle, adding maintenance surface with no requirement behind it.

## 6. No Formal Accessibility Standard

**Decision**: No WCAG or other formal accessibility target applies to this feature (`/speckit-clarify` 2026-08-21, Option A). Color, sizing, and keyboard/focus behavior follow the design-language doc's general visual-polish guidance only; no dedicated contrast audit or screen-reader-labeling pass is performed as part of this feature.

**Rationale**: This was an explicit Product Owner decision during clarification, made with the tradeoff stated plainly: faster delivery now, revisit formally if a real need arises. The proposed color tokens in `docs/decisions/visual-design-language.md` are not re-validated against a contrast ratio as part of this plan.

**Alternatives considered**: WCAG 2.1 AA (rejected by the Product Owner during clarification) and a full audit (also rejected, more so).

## 7. Design Tokens as Plain CSS Custom Properties

**Decision**: Implement `docs/decisions/visual-design-language.md`'s token taxonomy (color, radius, spacing, typography) as CSS custom properties in a new `frontend/src/styles/tokens.css`, imported once. No CSS-in-JS or utility-CSS framework (e.g., Tailwind) is introduced.

**Rationale**: The frontend has no styling library today (plain `.css` files per component, per feature 002's scaffold) — introducing one now would be a dependency added for this feature's convenience rather than because a requirement calls for it (Constitution XIII). CSS custom properties already work in this codebase (the current scaffold's `--accent`, `--text`, etc. in `index.css`) and directly satisfy "a small, named token system... rather than arbitrary colors spread everywhere," which is the Product Owner's own stated goal.

**Alternatives considered**:
- A CSS-in-JS solution (e.g., styled-components, vanilla-extract) — rejected; new dependency with no functional requirement demanding component-scoped styling beyond what plain CSS + tokens already provides at this project's current size (two screens).
- Tailwind CSS — rejected for the same reason; would also fight against the token-first approach the Product Owner explicitly asked for (utility classes encode values, not semantic token names).

## 8. Login Screen Redesign Preserves Existing Tests

**Decision**: `LoginPage.tsx`'s visual redesign changes markup/CSS classes only; the component's props (`onLoginSuccess`), form field `id`/`label` associations, and rendered text (error messages, button label) are unchanged.

**Rationale**: FR-001 explicitly forbids changing login behavior. The existing Vitest/RTL tests (`LoginPage.test.tsx`) query by label text and role, not CSS classes, so they remain valid as the regression safety net for "the redesign didn't change behavior" without needing to be rewritten — only new tests for the added visual states (e.g., distinct locked-vs-generic error styling, if that becomes testable via a role/attribute rather than a raw class name) are added.

**Alternatives considered**: Rewriting `LoginPage.test.tsx` from scratch — rejected; unnecessary churn on already-correct, still-valid tests (Constitution III's spirit against unrelated rework).

## 9. Testing Strategy for the Dashboard's Live Section

**Decision**: Backend — xUnit unit tests for `DashboardService`'s projection math (product count, total quantity) against a fake repository, plus xUnit integration tests for `GET /api/dashboard` (auth required, empty Ready list, populated Ready list) using the existing `AuthWebApplicationFactory` pattern from feature 002. Frontend — Vitest/RTL tests for `DashboardPage` (Ready rendering, empty state, the three static placeholders). The existing Playwright `login.spec.ts` is updated to assert the Dashboard (not the old placeholder text) appears after login; `LootSingles.E2EHost`'s seed data gains one sample Ready order so the Playwright flow has something real to assert on.

**Rationale**: Matches this project's established test pyramid (xUnit unit/integration, Vitest/RTL component, Playwright for the one critical flow) and constitution Principle IV. Extending the E2E host's seed is the smallest change that lets Playwright verify SC-003's "employee can see the Ready count" through a real browser rather than only through lower-level tests.

**Alternatives considered**: A dedicated new Playwright spec for the Dashboard — rejected as unnecessary; the Dashboard's appearance is already the natural continuation of the existing critical login flow (SC-005), so extending `login.spec.ts` covers it without a second browser-test file for what is still one end-to-end journey.
