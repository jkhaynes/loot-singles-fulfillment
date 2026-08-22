# Quickstart: Login Page and Dashboard UI Design

Validation guide for exercising this feature end-to-end once implemented. See [data-model.md](./data-model.md) for the read-model shape and [contracts/dashboard-api.md](./contracts/dashboard-api.md) for the new endpoint.

## Prerequisites

- Backend: .NET 10 SDK, a reachable SQL Server/LocalDB instance (same setup as features 001/002), with at least one employee account and, for the Ready-section scenarios below, at least one imported order (feature 001's packing-slip import).
- Frontend: Node.js (LTS), run from `frontend/`.

## Run the backend

```powershell
cd backend
dotnet run --project src/LootSingles.Api
```

## Run the frontend

```powershell
cd frontend
npm install
npm run dev
```

## Validation scenarios

Each scenario references the spec acceptance scenario(s) it proves.

### 1. Login screen visual design (US1 AS1, AS4)

1. Open the application on a desktop-sized browser window. Expect: a clearly laid out, intentionally designed login screen (not a bare unstyled form) with username and PIN fields.
2. Resize the browser to a phone-sized width (or use browser dev tools' device emulation). Expect: no horizontal scrolling, no overlapping content, all controls remain easily tappable.

### 2. Distinguishing login error states (US1 AS2, AS3)

1. Submit a wrong PIN or nonexistent username. Expect: the generic invalid-credentials message, visually presented as an error.
2. Lock an account (5 consecutive wrong PINs, per feature 002), then submit its correct PIN. Expect: the distinct locked-account message, visually distinguishable from the generic error in scenario 1 — not just different text.

### 3. No change to login behavior (US1 AS5)

1. Log in with correct credentials. Expect: authentication succeeds exactly as it did before this feature (same fields submitted, same session established) — this feature changed only appearance.

### 4. Dashboard appears after login with Ready data (US2 AS1, AS2)

1. Ensure at least one order exists in the Ready status (import a packing slip per feature 001's quickstart if needed).
2. Log in. Expect: immediately land on the Dashboard, organized into four sections (Ready, In Progress, Needs Attention, Picked). The Ready section shows the correct count and, for each Ready order, its identifier, product count, and total card/quantity count.

### 5. Dashboard empty state (US2 AS3)

1. Using a database/environment with zero Ready orders, log in. Expect: the Ready section shows a clear empty-state message, not a blank area.

### 6. Dashboard placeholder sections (US2 AS4)

1. On the Dashboard (any state), view the In Progress, Needs Attention, and Picked sections. Expect: each clearly communicates its data is not yet available — not a fabricated zero or count.

### 7. Dashboard responsive layout (US2 AS5)

1. View the Dashboard on a phone-sized viewport. Expect: all four sections remain legible and usable without horizontal scrolling.

### 8. Logout from the Dashboard (US2 AS6)

1. From the Dashboard, log out. Expect: return to the login screen; a subsequent `GET /api/auth/me` returns `401` (per feature 002's existing logout behavior).

## Automated coverage

- Backend: xUnit unit tests for `DashboardService`'s summary projection math, integration tests for `GET /api/dashboard` (auth required, empty Ready list, populated Ready list), per constitution Principle IV.
- Frontend: Vitest + React Testing Library for `DashboardPage` (Ready rendering, empty state, placeholder sections) and the existing `LoginPage` tests (unchanged behavior).
- Playwright: the existing login flow (`login.spec.ts`), updated to assert the Dashboard appears after login instead of the prior placeholder text, with the E2E host seeding one sample Ready order.
