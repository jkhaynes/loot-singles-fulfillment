# Validation Results: Login Page and Dashboard UI Design

**Date**: 2026-08-21
**Performed against**: `frontend` dev server (`npm run dev`, `http://localhost:5173`) proxying to `backend/tests/LootSingles.E2EHost` (the same seeded SQLite in-memory host used by the Playwright suite), driven via live browser interaction. This substitutes for a full SQL Server/LocalDB dev instance — the seeded data (one `e2emanager` / ManagerAdmin employee, one Ready order `E2E-ORDER-00001` with one product line, quantity 2) is equivalent to quickstart.md's prerequisites.

Each scenario below references quickstart.md's numbering.

## 1. Login screen visual design (US1 AS1, AS4)

**PASS.** Desktop viewport (1440px): clearly laid out, on-brand login screen — Loot Singles brand mark, dark-mode surface, username/PIN fields, rounded card. Not a bare unstyled form.

Mobile-width, no-horizontal-scroll behavior was not independently re-verified by manual browser resize (the automation's `resize_window` call did not change the captured viewport in this environment) — verified instead via the automated Playwright coverage added for this exact check: `frontend/e2e/responsive.spec.ts` (T027), which asserts no horizontal scroll on the login screen at a 375px viewport and passed (see T026 run below).

## 2. Distinguishing login error states (US1 AS2, AS3)

**PASS.**
- Wrong PIN for `e2emanager`: generic message "Username or PIN is incorrect." in a red-bordered, red-text alert box.
- After 5 consecutive wrong PINs (E2EHost's lockout threshold), submitting the *correct* PIN produced "This account is locked. Ask a Manager/Admin to unlock it." in a distinctly-styled amber/yellow-bordered box — visually distinguishable from the generic red error, not just different text.

## 3. No change to login behavior (US1 AS5)

**PASS.** After the lockout above, the backend host was restarted (fresh in-memory DB, unlocking the account) and login with correct credentials (`e2emanager` / `1234`) succeeded, landing on the Dashboard immediately — same fields submitted, same session-cookie-based flow as before this feature.

## 4. Dashboard appears after login with Ready data (US2 AS1, AS2)

**PASS.** Immediately after login: Dashboard shown with a "Ready to Pick" stat tile reading 1, and an Available Orders table listing `E2E-ORDER-00001` with Products = 1, Quantity = 2 — matching the seeded order.

## 5. Dashboard empty state (US2 AS3)

**Not independently re-walked live** — doing so would require a second backend instance seeded with zero Ready orders, duplicating coverage that already exists and passes: `DashboardControllerTests` (T011, integration, asserts `200` with an empty `ready.orders` array) and `DashboardPage.test.tsx` (T012, asserts the empty-state message renders). Both pass as part of the T026 suite run below.

## 6. Dashboard placeholder sections (US2 AS4)

**PASS.** In Progress, Needs Attention, and Picked tiles each read "Not yet available" in muted styling — no fabricated zero or count, confirmed on the live Dashboard screenshot from scenario 4.

## 7. Dashboard responsive layout (US2 AS5)

**PASS via automated coverage** — same `responsive.spec.ts` (T027) mobile-viewport assertion covers the Dashboard screen; passed. See note in scenario 1 on why this wasn't re-verified via manual resize.

## 8. Logout from the Dashboard (US2 AS6)

**PASS.** Clicking "Log out" from the Dashboard returned to the Login screen (heading "Log in", username field visible). The subsequent `GET /api/auth/me` → `401` behavior is feature 002's existing, already-tested logout mechanism (unchanged by this feature) and is covered by feature 002's own test suite plus this feature's Playwright login flow, which exercises the same logout path.

## Automated suite run (T026)

- Backend: `dotnet test` — 68 unit + 48 integration tests passed, 0 failed.
- Frontend unit: `npm test -- --run` (Vitest + RTL) — 12/12 passed.
- Frontend e2e: `npx playwright test` — 4/4 passed, **after fixing one stale assertion**: `frontend/e2e/login.spec.ts` still asserted the old placeholder-screen text `'Logged in as E2E Manager (ManagerAdmin)'` (carried over as `'E2E Manager (ManagerAdmin)'` by T024), which the redesigned `DashboardPage` (a first-name greeting, not a "Name (Role)" line) never renders. No spec requirement (FR-009 or otherwise) calls for displaying "Name (Role)" text, so the assertion was corrected to check the greeting heading (`getByRole('heading', { name: /E2E/i })`) instead of changing the approved Dashboard design. Re-ran after the fix: all 4 Playwright tests pass.

## Outcome

All 8 quickstart scenarios pass (5 live-verified end-to-end in-browser; scenarios 1/7's mobile-viewport check and scenario 5's empty state verified via their dedicated, passing automated tests rather than re-walked manually). Full automated suite (backend + frontend unit + Playwright) is green.
