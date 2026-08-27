# Contract: Employee Management API (extended)

This extends feature 002's
[`specs/002-employee-authentication/contracts/employee-management-api.md`](../../002-employee-authentication/contracts/employee-management-api.md) —
same base routes, same `[Authorize(Roles = ManagerAdmin)]` enforcement, same response shapes for
every unchanged endpoint. Only two things change: `POST /api/employees/{id}/deactivate` gains a
new failure case, and one new endpoint is added. No dedicated frontend screen consumed any of
feature 002's employee-management endpoints before this feature (its own contract says so
explicitly) — this feature is that first screen.

## Extended: `POST /api/employees/{id}/deactivate`

Same route, auth, and existing responses as feature 002. Gains one new response:

| Status | Condition | Body |
|---|---|---|
| `409 Conflict` | Deactivating this employee would leave zero active Manager/Admin employees (FR-007) | `{ "error": "would_remove_last_manager_admin" }` |

The existing account is left unchanged in this case — no partial deactivation occurs.

## New: `POST /api/employees/{id}/change-role`

Change an existing employee's role between `Picker` and `ManagerAdmin` (FR-006).

**Request body**:

```json
{ "role": "Picker | ManagerAdmin" }
```

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | Role changed | `{ "employeeId": 42, "role": "ManagerAdmin" }` |
| `404 Not Found` | No employee with this id exists | `{ "error": "not_found" }` |
| `400 Bad Request` | `role` is missing or not one of the two valid values | `{ "error": "invalid_request" }` |
| `409 Conflict` | Changing this employee's role away from Manager/Admin would leave zero active Manager/Admin employees (FR-007) | `{ "error": "would_remove_last_manager_admin" }` |
| `403 Forbidden` | Caller is not `ManagerAdmin` (FR-012) | — |

Setting an employee's role to the role they already hold is accepted and treated as a no-op
success (idempotent), consistent with `deactivate`/`reactivate`'s existing idempotent-ish
behavior — it still records an audit event.

Immediately invalidates any of the target employee's active sessions on their next request
(FR-010, research.md §2) — the same mechanism `deactivate` already uses for immediate effect.
Records an `EmployeeAuditEvent` (`RoleChanged`, actor = the acting Manager/Admin, target = the
employee whose role changed).

## Unchanged endpoints

`POST /api/employees` (create), `GET /api/employees` (list), `POST /api/employees/{id}/reactivate`,
`POST /api/employees/{id}/reset-pin`, `POST /api/employees/{id}/unlock`, and
`GET /api/employees/{id}/audit-events` are all unchanged from feature 002's contract — this
feature's screen simply calls them for the first time from a UI. `GET /api/employees`'s existing
response already includes `role`, so the admin screen's roster view needs no new fields from it.
