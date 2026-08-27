# Phase 0 Research: Manager Admin Screen

No `NEEDS CLARIFICATION` markers remain in the Technical Context — `/speckit-specify` and the
explicit `/speckit-clarify` pass already resolved every open product question. This research
covers the technical decisions needed to implement the spec's two genuinely new requirements
(role change, and the zero-Manager/Admin guard) consistently with this codebase's existing
patterns.

## 1. Preventing zero active Manager/Admin employees (FR-007), race-free

**Decision**: Reuse the exact `sp_getapplock`-based cross-process serialization technique already
established in `EmployeeRepository.TryAddFirstEmployeeAsync` for the analogous "exactly one
first-employee bootstrap" invariant. A new repository method (e.g.
`Task<bool> TryGuardLastManagerAdminAsync(int excludingEmployeeId, CancellationToken)`, exact
shape decided during implementation) begins a transaction, acquires a named application lock
(e.g. `"LootSingles.Employees.LastManagerAdminGuard"`), counts active Manager/Admin employees
excluding the one being acted on, and returns whether at least one other active Manager/Admin
would remain. `EmployeeManagementService.DeactivateAsync` and the new `ChangeRoleAsync` both call
this guard before applying their change, inside the same transaction, so the count and the
subsequent write are atomic with respect to any other concurrent admin action.

**Rationale**: This is the same class of problem this codebase has already solved once — "ensure
at most/at least N of something exists, race-free, across processes" — and `sp_getapplock` is
already proven here (`TryAddFirstEmployeeAsync`, with a real-concurrency test in
`BootstrapAdminCommandTests.cs`'s `ExecuteAsync_ConcurrentBootstrapAttemptsOnSameEmptyDatabase_ExactlyOneSucceeds`).
Reusing it keeps this feature consistent with existing idiom instead of introducing a second
concurrency-control technique for a structurally identical problem (constitution Principle XII).

**Alternatives considered**:
- *Plain check-then-act (count managers, then act if >1)*: this is exactly the TOCTOU race
  feature 013's `/code-design-review` already caught and fixed once this session (a read and a
  later write as two separate, unguarded round trips) — repeating that mistake here was rejected
  outright.
- *A compare-and-swap `ExecuteUpdateAsync` with a subquery predicate (feature 013's Order-claiming
  technique)*: would work, but `EmployeeManagementService` doesn't use the load-track-`SaveChangesAsync`-avoiding
  `ExecuteUpdateAsync` style anywhere today — introducing it here for one guard, while every other
  employee-management write stays on the existing tracked-entity pattern, would be an inconsistent,
  narrower fix for the same underlying "count + act atomically" need `sp_getapplock` already
  solves cleanly for this codebase's employee-management code specifically.

## 2. Invalidating a session immediately on role change (FR-010)

**Decision**: Extend `EmployeeSessionCookieEvents.ValidatePrincipal` — which already runs on every
authenticated request and already rejects the principal when `!employee.IsActive` — to also
compare the cookie's `ClaimTypes.Role` claim against the employee's *current* `Role` from the
database, and reject the principal (forcing re-authentication) on a mismatch, exactly as it
already does for deactivation.

**Rationale**: This is the exact mechanism FR-012 (feature 002, "immediately invalidate any active
session belonging to an employee whose account is deactivated") already relies on for immediate
invalidation without server-side session storage — ASP.NET Core cookie auth is otherwise stateless,
so "immediate" effect requires a per-request DB check, which this class already performs. Extending
the same check to cover role is the smallest possible change and keeps exactly one place
responsible for "is this cookie still valid," rather than adding a second, parallel invalidation
mechanism.

**Alternatives considered**:
- *Re-issue the cookie with refreshed claims (`ReplacePrincipal`) instead of rejecting it*: would
  let the employee keep working without re-authenticating, silently picking up the new role's
  access. Rejected — it contradicts spec.md's explicit requirement (User Story 4, Acceptance
  Scenario 3: "their existing session is invalidated and they must log in again") and the
  project's general preference for safe rejection over silently continued access.

## 3. API shape for the new role-change endpoint

**Decision**: `POST /api/employees/{id}/change-role` with a JSON body `{ "role": "Picker" |
"ManagerAdmin" }`, parsed the same way `CreateEmployeeRequest.Role` already is
(`Enum.TryParse<EmployeeRole>`), returning the same success/error shape as the existing
`deactivate`/`reactivate`/`unlock` actions.

**Rationale**: Matches the existing `EmployeesController` convention exactly — every other
account-management action is a role-restricted `POST {id}/{action-name}` route
(`/deactivate`, `/reactivate`, `/reset-pin`, `/unlock`). A new, differently-shaped endpoint (e.g. a
generic `PUT /api/employees/{id}`) would be a second convention for the same controller with no
benefit.

**Alternatives considered**: A generic partial-update `PATCH`/`PUT` endpoint accepting any
employee field — rejected as unnecessary generality (constitution Principle XIII); nothing in the
spec calls for updating any field other than role, and the existing controller already treats each
account-management action as its own explicit, narrowly-scoped endpoint.

## 4. Frontend route protection

**Decision**: Add a small `<RequireManagerAdmin>` wrapper component in `App.tsx` that checks
`employee.role === 'ManagerAdmin'` (the same string comparison already used for the Force-Release
button in `OrderDetailPage.tsx`) and renders the admin page only when true, otherwise redirecting
to the dashboard. This is the first *route-level* guard in the frontend — prior manager-only
behavior (Force-Release) was an inline conditional within an otherwise-shared page, not a
dedicated screen.

**Rationale**: This is a UX convenience only — FR-013 and constitution Principle VI already
require server-side enforcement regardless (the `[Authorize(Roles=...)]` attribute on the new and
existing employee-management endpoints), so a Picker who bypasses the frontend guard still gets
403s from every action. The guard exists purely so a Picker who stumbles onto the route sees a
clear "not for you" outcome instead of a page full of failed API calls.

**Alternatives considered**: None — this is a direct, minimal application of the existing
role-string-comparison pattern to a new context (route-level instead of button-level), not a new
design decision.
