# Contract: Authentication API

Backend HTTP surface for login, logout, and current-session lookup. All endpoints are same-origin, cookie-based (research.md §2); no bearer token is ever returned to the client.

## `POST /api/auth/login`

**Auth required**: No.

**Request body**:
```json
{ "username": "string", "pin": "string (4 numeric digits)" }
```

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | Correct username + PIN, account active and not locked (FR-001) | `{ "employeeId": 42, "displayName": "Jamie", "role": "Picker" }`; sets the auth cookie |
| `401 Unauthorized` | Wrong PIN, nonexistent username, or deactivated account (FR-004, FR-005) | `{ "error": "invalid_credentials", "message": "Username or PIN is incorrect." }` — identical for all three cases (FR-004) |
| `423 Locked` | Account is locked (FR-021) | `{ "error": "account_locked", "message": "This account is locked. Ask a Manager/Admin to unlock it." }` |
| `400 Bad Request` | `pin` is not exactly 4 numeric digits, or fields missing | `{ "error": "invalid_request" }` |

A wrong-PIN `401` increments `Employee.FailedAttemptCount` server-side and may transition the account to locked (FR-006); the response for that specific request is still the generic `401`, not `423` — the `423` only appears once the account is *already* locked at the time of a subsequent attempt (FR-021 Acceptance Scenario, US2 Acceptance Scenario 5).

## `POST /api/auth/logout`

**Auth required**: Yes.

**Responses**: `204 No Content` — clears the auth cookie for this session only (FR-020); does not affect the employee's other active sessions on other devices (Assumptions).

## `GET /api/auth/me`

**Auth required**: Yes.

**Purpose**: Lets the frontend recover "who is logged in" after a page reload without re-prompting for credentials (FR-010, US4) — a technical support endpoint, not a new product capability.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | Valid, non-expired session for an active employee | `{ "employeeId": 42, "displayName": "Jamie", "role": "Picker" }` |
| `401 Unauthorized` | No session, expired session, or session for a now-deactivated employee (FR-011, FR-012) | `{ "error": "not_authenticated" }` |
