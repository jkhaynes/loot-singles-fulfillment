# Feature Specification: Manager Admin Screen

**Feature Branch**: `014-manager-admin-screen`

**Created**: 2026-08-27

**Status**: Draft

**Input**: User description: "We need to create the manager admin screen where the manager can go to do actions such as create a new 'user' aka picker, change that picker into another manager if they want, remove users from the application, and any other manager only actions relevant that are discussed in the prd."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Manager Views the Employee Roster (Priority: P1)

A Manager/Admin opens the admin screen and sees every employee — active and inactive — with
their role and account status, so they know who exists before acting on any of them.

**Why this priority**: Every other action on this screen (create, deactivate, role change, PIN
reset, unlock) depends on the Manager/Admin first being able to see the current roster. There is
no admin screen at all today — feature 002 built the account-management capabilities server-side,
but no UI ever exposed them.

**Independent Test**: As a Manager/Admin, open the admin screen and confirm every employee account
(both active and deactivated) is listed with its username, display name, role, active/inactive
status, and locked/unlocked status.

**Acceptance Scenarios**:

1. **Given** the employee roster contains both active and deactivated employees, **When** a
   Manager/Admin opens the admin screen, **Then** all of them are listed, each showing username,
   display name, role, active/inactive status, and locked/unlocked status.
2. **Given** a Picker-role employee is authenticated, **When** they attempt to open the admin
   screen, **Then** access is denied.

---

### User Story 2 - Manager Creates a New Employee Account (Priority: P1)

A Manager/Admin adds a new hire to the system by creating an account with a username, display
name, initial PIN, and role, so that employee can start logging in.

**Why this priority**: Onboarding a new picker or manager is the most basic administrative action
and was explicitly named as a requirement; it was already approved and built server-side in
feature 002 but has never been reachable through any screen.

**Independent Test**: As a Manager/Admin, create a new employee account with a unique username, an
initial PIN, and a role; confirm the account appears in the roster (User Story 1) and that
employee can subsequently log in with those credentials.

**Acceptance Scenarios**:

1. **Given** a Manager/Admin is on the admin screen, **When** they create a new employee account
   with a unique username, display name, initial PIN, and role, **Then** the account is created
   and that employee can log in with the assigned credentials.
2. **Given** a Manager/Admin attempts to create an account with a username already in use (by
   either an active or a deactivated employee), **When** they submit it, **Then** the system
   rejects the duplicate and clearly identifies the conflict.

---

### User Story 3 - Manager Removes or Restores an Employee (Priority: P1)

A Manager/Admin removes an employee who no longer works at Loot so they can no longer log in, and
can restore a previously removed employee if they return.

**Why this priority**: Explicitly requested ("remove users from the application"); keeping the
roster accurate as staff changes is core administrative upkeep, already approved and built
server-side in feature 002 as deactivation/reactivation, but never reachable through any screen.

**Independent Test**: As a Manager/Admin, remove an active employee and confirm they can no longer
log in; restore that employee and confirm they can log in again with their existing PIN.

**Acceptance Scenarios**:

1. **Given** an active employee, **When** a Manager/Admin removes them from the admin screen,
   **Then** that employee can no longer log in, and their historical records (e.g., past picks)
   remain attributed to them.
2. **Given** a previously removed employee, **When** a Manager/Admin restores them, **Then** that
   employee can log in again with their existing PIN.
3. **Given** exactly one active Manager/Admin remains, **When** that Manager/Admin attempts to
   remove their own account (or any action that would leave zero active Manager/Admin accounts),
   **Then** the action is rejected and the reason is clearly explained.

---

### User Story 4 - Manager Changes an Employee's Role (Priority: P2)

A Manager/Admin promotes a Picker to Manager/Admin, or demotes a Manager/Admin back to Picker, so
staffing changes are reflected in what that employee is allowed to do.

**Why this priority**: Explicitly requested ("change that picker into another manager if they
want"); this is new capability — no equivalent existed in feature 002, which only assigned a role
at account creation. Ranked after the P1 stories because it is a less frequent action than
onboarding or removing staff.

**Independent Test**: As a Manager/Admin, change an existing Picker's role to Manager/Admin;
confirm that employee's next login reflects Manager/Admin access. Change a Manager/Admin's role
back to Picker and confirm the reverse.

**Acceptance Scenarios**:

1. **Given** an active Picker-role employee, **When** a Manager/Admin changes their role to
   Manager/Admin, **Then** that employee's next login grants Manager/Admin access.
2. **Given** an active Manager/Admin-role employee, **When** another Manager/Admin changes their
   role to Picker, **Then** that employee's next login restricts them to Picker access.
3. **Given** that employee was already logged in when their role changed, **When** the role
   change is made, **Then** their existing session is invalidated and they must log in again
   before the new role takes effect.
4. **Given** exactly one active Manager/Admin remains, **When** a role change would leave zero
   active Manager/Admin accounts (including changing their own role), **Then** the action is
   rejected and the reason is clearly explained.
5. **Given** a Picker-role employee, **When** they attempt to change any employee's role, **Then**
   the action is rejected.

---

### User Story 5 - Manager Resets a PIN or Unlocks an Account (Priority: P3)

A Manager/Admin resets an employee's forgotten PIN or unlocks an account that locked itself out
after repeated incorrect PIN attempts, so that employee can get back into the application without
engineering involvement.

**Why this priority**: Already approved and built server-side in feature 002 (PIN reset, unlock)
but never reachable through any screen — a Manager/Admin currently has no way to perform either
action at all. Ranked below the other stories because it's a recovery action rather than routine
roster upkeep, and not explicitly named by the requester, though the underlying capability already
exists and closing this gap is low-risk.

**Independent Test**: As a Manager/Admin, reset an employee's PIN and confirm their old PIN no
longer works while the new one does. Unlock a locked employee's account and confirm they can log
in again with their existing PIN, without it having changed.

**Acceptance Scenarios**:

1. **Given** an employee's PIN, **When** a Manager/Admin resets it from the admin screen,
   **Then** that employee's previous PIN no longer authenticates them and the new PIN does.
2. **Given** a locked employee account, **When** a Manager/Admin unlocks it from the admin screen,
   **Then** that employee can authenticate again with their existing (unchanged) PIN.

---

### Edge Cases

- What happens when a Manager/Admin attempts to remove their own account, or change their own
  role away from Manager/Admin, while they are the only active Manager/Admin? The action is
  rejected — the same "at least one active Manager/Admin must remain" rule applies to a
  Manager/Admin acting on their own account as to any other employee's account (User Story 3
  Acceptance Scenario 3, User Story 4 Acceptance Scenario 4).
- What happens when two Manager/Admins simultaneously perform actions that would each be fine
  alone but together would leave zero active Manager/Admin accounts (e.g., each demotes a
  different one of the only two remaining managers at the same moment)? The system MUST NOT end
  up with zero active Manager/Admin accounts as a result; at most one of the two actions succeeds.
- What happens to an employee's active session when their role changes while they are logged in?
  It is invalidated immediately — consistent with the existing precedent that deactivating an
  employee invalidates their active session immediately (feature 002) — so a demoted employee
  cannot continue operating with their prior role's access until their session would otherwise
  expire.
- What happens when a Manager/Admin attempts to create an account with a username that matches a
  previously removed (deactivated) employee's username? The creation is rejected as a duplicate —
  usernames remain reserved by deactivated accounts, consistent with feature 002 retaining a
  removed employee's identity rather than deleting it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow a Manager/Admin to view a list of all employee accounts, both
  active and inactive, showing each employee's username, display name, role, active/inactive
  status, and locked/unlocked status.
- **FR-002**: System MUST allow a Manager/Admin to create a new employee account with a unique
  username, display name, initial PIN, and role (Picker or Manager/Admin).
- **FR-003**: System MUST reject creation of an employee account whose username duplicates an
  existing account's username (active or inactive), comparing case-insensitively, and clearly
  identify the conflict to the Manager/Admin.
- **FR-004**: System MUST allow a Manager/Admin to remove (deactivate) an active employee account,
  preventing that employee from logging in, without deleting the employee's underlying identity or
  historical records.
- **FR-005**: System MUST allow a Manager/Admin to restore (reactivate) a previously removed
  employee account, allowing that employee to log in again with their existing PIN.
- **FR-006**: System MUST allow a Manager/Admin to change an existing employee's role between
  Picker and Manager/Admin.
- **FR-007**: System MUST prevent any action from this screen — removing an employee or changing
  an employee's role — that would result in zero active Manager/Admin employees remaining,
  rejecting the action and clearly explaining why.
- **FR-008**: System MUST allow a Manager/Admin to reset another employee's PIN, issuing a new PIN
  that employee must use to authenticate going forward.
- **FR-009**: System MUST allow a Manager/Admin to unlock a locked employee account, restoring the
  ability to authenticate with the employee's existing (unchanged) PIN.
- **FR-010**: System MUST immediately invalidate an employee's active session(s) when their role is
  changed, requiring them to re-authenticate before the new role's access takes effect.
- **FR-011**: System MUST record every state-changing action this screen enables (create, remove,
  restore, role change, PIN reset, unlock) as an auditable event identifying which Manager/Admin
  performed it, extending the existing employee-account audit mechanism (feature 002) to cover
  role changes as well.
- **FR-012**: System MUST restrict this screen and every action it exposes to Manager/Admin-role
  employees only; a Picker-role employee MUST NOT be able to access the screen or perform any of
  its actions.
- **FR-013**: System MUST enforce every restriction and action in this feature server-side;
  frontend-only checks MUST NOT be the sole enforcement point.
- **FR-014**: System MUST apply the same rules to a Manager/Admin acting on their own account
  (changing their own role, or removing their own account) as to any other employee's account,
  with no special exemption, subject to FR-007's zero-Manager/Admin protection.
- **FR-015**: System MUST NOT permanently delete an employee account through this screen; removing
  an employee MUST be implemented as deactivation (FR-004), preserving the account for historical
  auditability, consistent with feature 002.

### Key Entities

- **Employee** (existing, feature 002): This feature adds no new attributes to Employee — it
  exposes existing attributes (username, display name, role, active status, locked status) through
  a UI for the first time, and adds the ability to change the existing `Role` attribute after
  creation (previously fixed at creation time).
- **Employee Audit Event** (existing, feature 002): This feature adds a new event type — role
  changed — to the existing audit mechanism that already records account creation, deactivation,
  reactivation, PIN reset, and unlock, so role changes are auditable the same way.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A Manager/Admin can view the complete current roster — every employee, active or
  inactive, with role and status — without needing direct database or API access.
- **SC-002**: A Manager/Admin can create a new employee account and that employee can successfully
  log in, completed end-to-end in under 2 minutes.
- **SC-003**: 100% of attempts by a Picker-role employee to access the admin screen or any action
  it exposes are rejected, verified across a representative sample.
- **SC-004**: An employee removed through the admin screen cannot log in immediately afterward,
  100% of the time.
- **SC-005**: 0 instances of the system ending up with zero active Manager/Admin accounts as a
  result of a removal or role change performed through this screen, including when two such
  actions are attempted at approximately the same time.
- **SC-006**: A Manager/Admin can change any employee's role, and that employee's next login
  reflects the new role's access, verified across both promotion (Picker to Manager/Admin) and
  demotion (Manager/Admin to Picker).
- **SC-007**: A Manager/Admin can resolve an employee's forgotten PIN or locked account (via reset
  or unlock) without engineering involvement, restoring that employee's ability to log in.
- **SC-008**: For every state-changing action this screen enables, an operator can determine which
  specific Manager/Admin performed it without inspecting application code or database internals
  directly.

## Assumptions

- "Remove users from the application" means deactivation, not permanent deletion — this preserves
  historical auditability (e.g., "picked by") consistent with feature 002's existing
  `FR-017` decision to never delete an employee's identity. Reversing that decision to support
  true hard deletion would require an explicit Product Owner decision and is out of scope here.
- Changing a role is a new capability this feature introduces; feature 002 only assigned a role at
  account creation. This feature reuses the existing Picker/Manager-Admin two-role model
  established in feature 002 — no additional roles are introduced.
- Preventing the system from ever reaching zero active Manager/Admin accounts (FR-007) extends a
  concern feature 002's own Assumptions already flagged ("technical planning should ensure that
  scenario cannot arise from ordinary use") but had not yet built a guard for, since deactivation
  was never reachable through any UI until this feature. This feature closes that gap for both the
  removal and the new role-change action.
- Invalidating an employee's session immediately on role change (FR-010) mirrors the existing,
  already-approved precedent that deactivation invalidates an active session immediately (feature
  002), applied here so a demoted employee cannot continue operating under their prior role's
  access for the remainder of their session.
- This feature scopes Manager/Admin capabilities to employee-account management (view, create,
  remove/restore, change role, reset PIN, unlock) surfaced via one dedicated screen. Other
  Manager/Admin capabilities named elsewhere in the PRD — for example, force-releasing a claimed
  order — are already implemented separately (feature 013, on the order detail screen) and are not
  duplicated here. The PRD itself states exact Manager/Admin capabilities "remain to be finalized"
  (PRD §9.4, open question 41); this feature resolves that only for the account-management actions
  explicitly requested plus the already-approved actions from feature 002 that had no UI, not for
  every conceivable Manager/Admin capability the PRD might eventually name.
- Viewing an employee's audit history (already available server-side via feature 002's
  audit-events endpoint) is not itself exposed as a requirement of this screen; it may be added by
  a future feature if needed.
