# Quickstart: Employee Authentication

Validation guide for exercising this feature end-to-end once implemented. See [data-model.md](./data-model.md) for entity detail and [contracts/](./contracts/) for exact request/response shapes.

## Prerequisites

- Backend: .NET 10 SDK, a reachable SQL Server/LocalDB instance configured via the `LootSingles` connection string (same setup as feature 001).
- Frontend: Node.js (LTS), run from `frontend/`.
- A seeded initial `ManagerAdmin` employee exists in the target database (spec Assumptions: at least one Manager/Admin account must always be able to authenticate; seeding is an operational/migration concern, not this feature's runtime behavior).

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

Navigate to the login page shown by the dev server.

## Validation scenarios

Each scenario references the spec acceptance scenario(s) it proves.

### 1. Successful login (US1 AS1, AS3)

1. Enter the seeded Manager/Admin's username and correct PIN on the login page.
2. Expect: redirected into the app; `GET /api/auth/me` returns that employee's identity.

### 2. Wrong PIN / nonexistent username are indistinguishable (US1 AS2; FR-004; Edge Cases)

1. Attempt login with a real username and a wrong PIN. Expect: generic "Username or PIN is incorrect" rejection, no session.
2. Attempt login with a username that doesn't exist. Expect: the identical generic rejection.

### 3. Deactivated account rejected (US1 AS4; FR-005)

1. As the Manager/Admin, create an employee (`POST /api/employees`), then deactivate them (`POST /api/employees/{id}/deactivate`).
2. Attempt login as that employee with their correct PIN. Expect: the same generic rejection as scenario 2 (not the locked-account message).

### 4. Lockout after repeated failures (US2 AS1, AS2, AS3, AS5; FR-006, FR-021)

1. Submit 5 consecutive wrong-PIN attempts for one employee.
2. On the 5th failure, expect the account is now locked.
3. Submit the *correct* PIN. Expect: rejected with the distinct "This account is locked..." message (`423`), not the generic one.
4. Repeat the correct-PIN attempt again (simulating elapsed time). Expect: still locked — no auto-expiry.

### 5. Failed-attempt count resets on success (US2 AS4; FR-007)

1. Submit 2 wrong-PIN attempts for an employee (below the lockout threshold).
2. Submit the correct PIN. Expect: login succeeds.
3. Submit 4 more wrong-PIN attempts. Expect: account is *not* yet locked (count restarted from 0 after the earlier success, so 4 < 5 threshold).

### 6. Manager/Admin unlock (US3 AS6; FR-022)

1. Lock an employee's account (scenario 4).
2. As Manager/Admin, call `POST /api/employees/{id}/unlock`.
3. Attempt login with the employee's *existing* (unchanged) PIN. Expect: success.

### 7. Reactivation clears an existing lockout (US3 AS7; `/speckit-clarify` 2026-08-21)

1. Lock an employee's account (scenario 4), then deactivate it while still locked.
2. Reactivate it (`POST /api/employees/{id}/reactivate`) — do **not** call unlock separately.
3. Attempt login with the employee's existing PIN. Expect: success (lockout was cleared by reactivation alone).

### 8. Manager/Admin PIN reset (US3 AS5; FR-009)

1. As Manager/Admin, call `POST /api/employees/{id}/reset-pin` with a new 4-digit PIN.
2. Attempt login with the *old* PIN. Expect: generic rejection.
3. Attempt login with the *new* PIN. Expect: success.

### 9. Duplicate username rejected (US3 AS2; FR-014)

1. Create an employee with username `jsmith`.
2. Attempt to create another employee with username `JSmith`. Expect: `409 Conflict` (case-insensitive collision).

### 10. Picker cannot perform admin actions (US3 AS4; FR-016; SC-007)

1. Log in as a `Picker`-role employee.
2. Call any `POST /api/employees/...` management endpoint. Expect: `403 Forbidden`.

### 11. Session persists across reload; expires after inactivity (US4; FR-010, FR-011)

1. Log in, then reload the page. Expect: still authenticated, no re-prompt.
2. Wait past the 30-minute inactivity window (or advance server/test clock in an integration test), then make a request. Expect: `401`, requiring re-authentication.

### 12. Explicit logout (US4 AS3; FR-020)

1. Log in, then call `POST /api/auth/logout`.
2. Reload the page / call `GET /api/auth/me`. Expect: `401`, not authenticated.

### 13. Deactivation invalidates an active session immediately (Edge Cases; FR-012; SC-008)

1. Log in as employee X in one browser session.
2. In a separate Manager/Admin session, deactivate employee X.
3. Make any authenticated request as employee X (without them logging out). Expect: `401` on the very next request — not merely at the session's natural 30-minute expiry.

### 14. Auditability (FR-018; SC-005)

1. Perform a login, a logout, and one Manager/Admin action (e.g., unlock) from scenario 6.
2. Call `GET /api/employees/{id}/audit-events` as Manager/Admin. Expect: entries for each action, each attributing the specific performing employee, without querying the database directly.

## Automated coverage

- Backend: xUnit unit tests for lockout/credential/role rules (`LootSingles.UnitTests/Auth`), integration tests for full login/session/roster flows against a real database (`LootSingles.IntegrationTests/Auth`), per constitution Principle IV.
- Frontend: Vitest + React Testing Library for the login form and session context.
- Playwright: the login flow (scenario 1) end-to-end in a real browser, per the Definition of Done's critical-flow requirement.
