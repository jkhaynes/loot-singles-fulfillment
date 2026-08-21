# Contract: Employee Management API

Backend HTTP surface for Manager/Admin roster maintenance (US3). Every endpoint in this file requires an authenticated session with `role = ManagerAdmin` (FR-016, FR-019); a `Picker` session receives `403 Forbidden` from all of them. No dedicated frontend screen consumes these in this feature (research.md §10) — they exist for a future roster-management UI and for direct administrative use.

## `POST /api/employees`

Create a new employee account (FR-013).

**Request body**:
```json
{ "username": "string", "displayName": "string", "initialPin": "string (4 numeric digits)", "role": "Picker | ManagerAdmin" }
```

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `201 Created` | Username unique (case-insensitive), PIN valid | `{ "employeeId": 42 }` |
| `409 Conflict` | Username already in use, case-insensitively (FR-014) | `{ "error": "username_taken" }` |
| `400 Bad Request` | `initialPin` not exactly 4 numeric digits, or missing fields | `{ "error": "invalid_request" }` |
| `403 Forbidden` | Caller is not `ManagerAdmin` (FR-016) | — |

Records an `EmployeeAuditEvent` (`AccountCreated`, actor = the creating Manager/Admin, target = the new employee) on success.

## `GET /api/employees`

List employees, to support selecting one for the actions below (implied by US3's "maintain the roster" — not a distinct FR, no filtering/paging requirement exists yet).

**Responses**: `200 OK` → `[{ "employeeId": 42, "username": "...", "displayName": "...", "role": "...", "isActive": true, "isLocked": false }, ...]`

## `POST /api/employees/{id}/deactivate`

Deactivate an active employee account (FR-015).

**Responses**: `204 No Content` on success (idempotent-ish — deactivating an already-inactive account is also `204`); `404 Not Found` if `id` doesn't exist; `403 Forbidden` if caller is not `ManagerAdmin`.

Immediately invalidates any of that employee's active sessions on their next request (FR-012, data-model.md "Session"). Records an `EmployeeAuditEvent` (`Deactivated`).

## `POST /api/employees/{id}/reactivate`

Reactivate a previously deactivated account (FR-015).

**Responses**: `204 No Content`; `404 Not Found`; `403 Forbidden` as above.

If the account was locked at the time of reactivation, this also clears the lockout and resets `FailedAttemptCount` to `0` (`/speckit-clarify` 2026-08-21, data-model.md state transitions) — no separate unlock call is needed afterward. Records an `EmployeeAuditEvent` (`Reactivated`).

## `POST /api/employees/{id}/reset-pin`

Issue a new PIN for an employee (FR-009). Self-service reset is not supported — this endpoint is Manager/Admin-only regardless of whose PIN is being reset.

**Request body**:
```json
{ "newPin": "string (4 numeric digits)" }
```

**Responses**: `200 OK` (empty body); `400 Bad Request` if `newPin` isn't 4 numeric digits; `404 Not Found`; `403 Forbidden` as above.

Does not change `IsActive` or `IsLocked` (FR-022: reset and unlock are independent). Records an `EmployeeAuditEvent` (`PinReset`).

## `POST /api/employees/{id}/unlock`

Unlock a locked employee account without changing their PIN (FR-022).

**Responses**: `204 No Content`; `404 Not Found`; `403 Forbidden` as above. `IsLocked → false`, `FailedAttemptCount → 0`.

Records an `EmployeeAuditEvent` (`Unlocked`).

## `GET /api/employees/{id}/audit-events`

Read the audit trail for state-changing actions performed by or on an employee (FR-018, SC-005; research.md §8).

**Responses**: `200 OK` → `[{ "actionType": "Login", "actorEmployeeId": 42, "targetEmployeeId": null, "occurredAt": "2026-08-21T14:03:00Z" }, ...]`, most recent first; `404 Not Found`; `403 Forbidden` as above.
