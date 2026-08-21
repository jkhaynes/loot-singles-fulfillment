# Data Model: Employee Authentication

## Employee

Domain aggregate root (`LootSingles.Domain.Employees.Employee`) — a plain POCO, not an EF Core/Identity type (research.md §9).

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` (identity) | Primary key, consistent with `Order.Id` convention (feature 001) |
| `Username` | `string` (required) | As entered/displayed; uniqueness enforced via `NormalizedUsername` |
| `NormalizedUsername` | `string` (required) | Invariant-uppercase of `Username`; unique index (FR-014, research.md §7) |
| `DisplayName` | `string` (required) | PRD §9.3 employee record attribute |
| `PinHash` | `string` (required) | PBKDF2 hash from `IPinHasher` (FR-003); never the raw PIN |
| `Role` | `EmployeeRole` (required) | `Picker` \| `ManagerAdmin` (FR-002, research.md §6) |
| `IsActive` | `bool` | Default `true` on creation; `false` after deactivation (FR-005, FR-015) |
| `FailedAttemptCount` | `int` | Default `0`; increments on wrong-PIN rejection, resets on success or unlock/reactivation (FR-007) |
| `IsLocked` | `bool` | Default `false`; set `true` at the failed-attempt threshold (FR-006, research.md §4–§5) |
| `CreatedAt` | `DateTimeOffset` | Set at creation, for operational/audit context |

### Validation rules (enforced in `LootSingles.Application`, not just at the DB)

- `Username`: required, non-empty; uniqueness is case-insensitive via `NormalizedUsername` (FR-014).
- `PinHash`: never set from a raw value shorter/longer than 4 numeric digits — the *input* PIN is validated as exactly 4 numeric digits (FR-008) before hashing; any 4-digit numeric value is accepted, including repeating/sequential digits (FR-008, `/speckit-clarify` 2026-08-21) — no complexity blocklist.
- `Role`: required, one of the two `EmployeeRole` values (FR-002).
- An employee is deleted from the database — deactivation only flips `IsActive` (FR-017, retains identity for historical "picked by" attribution in future features).

### State transitions

`IsActive` and `IsLocked` are independent booleans; the valid combinations and their login effect:

| IsActive | IsLocked | Login attempt result |
|---|---|---|
| `true` | `false` | Normal authentication (correct PIN → success; wrong PIN → generic rejection + increments `FailedAttemptCount`, possibly setting `IsLocked`) |
| `true` | `true` | Always rejected with the distinct locked-account message (FR-021), regardless of PIN correctness (FR-006 Acceptance Scenario 2) |
| `false` | `false` | Always rejected with the generic invalid-credentials message (FR-005) — deactivation does not reveal lockout state |
| `false` | `true` | Always rejected with the generic invalid-credentials message — FR-005 (inactive) takes precedence over the distinct locked message, since a deactivated account should not be distinguishable from "doesn't exist" by the locked-account message |

Transitions:

- `Active+Unlocked` → **wrong PIN, count reaches threshold** → `Active+Locked` (FR-006)
- `Active+Locked` → **Manager/Admin unlock** → `Active+Unlocked`, `FailedAttemptCount = 0` (FR-022)
- `Active+*` → **Manager/Admin deactivate** → `Inactive`, `IsLocked` unchanged (FR-015; a locked account can be deactivated while still locked)
- `Inactive+Locked` → **Manager/Admin reactivate** → `Active+Unlocked`, `FailedAttemptCount = 0` (FR-015, `/speckit-clarify` 2026-08-21: reactivation clears an existing lockout)
- `Inactive+Unlocked` → **Manager/Admin reactivate** → `Active+Unlocked` (no lockout to clear)
- `Active+Unlocked` → **successful login** → `FailedAttemptCount = 0` (FR-007; no-op if already 0)
- **Manager/Admin PIN reset** → `PinHash` replaced; `IsActive`/`IsLocked`/`FailedAttemptCount` unchanged (FR-009, FR-022 — reset and unlock are independent)

## EmployeeAuditEvent

Domain entity (`LootSingles.Domain.Employees.EmployeeAuditEvent`) recording FR-018's required attribution.

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` (identity) | Primary key |
| `ActorEmployeeId` | `int` (FK → `Employee.Id`) | The employee who performed the action (for `Login`, the employee who logged in) |
| `TargetEmployeeId` | `int?` (FK → `Employee.Id`) | The employee acted upon; `null` for `Login`/`Logout` (actor and target are the same); set for admin actions on another employee |
| `ActionType` | `EmployeeAuditActionType` (required) | `Login`, `Logout`, `AccountCreated`, `Deactivated`, `Reactivated`, `PinReset`, `Unlocked` |
| `OccurredAt` | `DateTimeOffset` (required) | Server timestamp of the action |

Written only for actions that actually succeed (a rejected login attempt is reflected in `Employee.FailedAttemptCount`, not in this table — FR-018 attributes actions to "the specific employee who performed it," which presumes the action succeeded and the actor is known).

## Session (not persisted)

The spec's "Session" entity (an authenticated login for an Employee: associated employee, creation timestamp, expiration/last-activity timestamp, ended/invalidated state) is represented entirely by the ASP.NET Core Cookie Authentication ticket — no `Session` database table exists (research.md §2–§3).

| Spec attribute | Represented by |
|---|---|
| Associated employee | Claim (`ClaimTypes.NameIdentifier` = `Employee.Id`, plus a role claim) inside the encrypted cookie |
| Creation timestamp | Ticket `IssuedUtc` (framework-managed) |
| Expiration / last-activity | Ticket `ExpiresUtc` with `SlidingExpiration = true`, 30-minute window (FR-010) |
| Ended (explicit logout) | `HttpContext.SignOutAsync(...)` clears the cookie (FR-020) |
| Invalidated (deactivation) | `OnValidatePrincipal` rejects the ticket when the live `Employee.IsActive` is `false` (FR-012, research.md §3) — the ticket itself is not proactively revoked, but every subsequent request fails validation immediately |

Multiple concurrent sessions per employee (Assumptions: multi-device use permitted) fall out naturally — each browser/device holds its own independent cookie; there is no shared session store to reconcile.
