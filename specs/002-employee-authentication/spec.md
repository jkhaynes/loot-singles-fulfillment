# Feature Specification: Employee Authentication

**Feature Branch**: `002-employee-authentication`

**Created**: 2026-08-20

**Status**: Draft

**Input**: User description: "Employee username and PIN authentication for the Loot Singles Fulfillment picker application, including individual employee accounts, secure PIN storage, failed-attempt protection, session management, and Picker/Manager roles"

## Clarifications

### Session 2026-08-21

- Q: Should a rejected login during an active account lockout show the same generic message as any other failed login, or a distinct message? → A: Two distinct messages: a generic "PIN is incorrect" message covers both a wrong PIN and a nonexistent username (so those two cases remain indistinguishable from each other), while a separate "Account is locked" message is shown specifically when the account is locked, directing the employee to a Manager/Admin to have it unlocked. Usability for a legitimately locked-out employee is prioritized over withholding lockout state from a potential attacker.
- Q: Does clearing a lockout require a Manager/Admin to act, or does it clear on its own after a cooldown period? → A: Admin-driven only — a Manager/Admin must actively unlock the account. There is no automatic cooldown expiration; the lockout persists until a Manager/Admin unlocks it.
- Q: If an employee account is deactivated while locked, and a Manager/Admin later reactivates it, does reactivation also clear the lockout, or does it stay locked pending a separate unlock? → A: Reactivation clears the lockout — reactivating a locked account resets the failed-attempt count and restores the ability to authenticate with the employee's existing PIN, without requiring a separate unlock action.
- Q: Beyond being a 4-digit numeric code (FR-008), must the system reject obviously weak PINs (e.g., 0000, 1234, repeating digits) when a Manager/Admin sets an initial or reset PIN? → A: No — any 4-digit numeric value is acceptable. The lockout mechanism (FR-006) already protects against brute-force guessing regardless of PIN value; a weak-PIN blocklist is not a requirement.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Employee Logs In With Username and PIN (Priority: P1)

An employee opens the application and authenticates using their individual username and PIN so they can access the picking workflow as themselves.

**Why this priority**: Nothing else in the product — order claiming, guided picking, issue reporting, or auditability ("who picked this order?") — can function without a known, authenticated employee identity. This is the foundational capability every other V1 feature depends on.

**Independent Test**: Given a valid employee account with a known username and PIN, supply the correct credentials and confirm the employee is authenticated and an active session is established that identifies them.

**Acceptance Scenarios**:

1. **Given** an active employee account, **When** the employee enters their correct username and PIN, **Then** they are authenticated and a session begins that identifies them by their employee identity.
2. **Given** an active employee account, **When** the employee enters an incorrect PIN, **Then** authentication is rejected and no session is established.
3. **Given** an authenticated session, **When** the employee performs an action elsewhere in the system that records "who did this," **Then** that record reflects the authenticated employee's identity.
4. **Given** a disabled/inactive employee account, **When** that employee attempts to log in with their username and PIN, **Then** authentication is rejected regardless of PIN correctness.

---

### User Story 2 - Failed Login Attempts Are Protected Against Repeated Guessing (Priority: P1)

An employee — or anyone else — enters an incorrect PIN, and repeated incorrect attempts trigger a protective lockout so a short PIN cannot be brute-forced. Clearing the lockout requires a Manager/Admin to act; it does not clear on its own.

**Why this priority**: This is a required safety property of the credential model itself. A short, quickly-enterable PIN is a deliberate, approved usability tradeoff, but only alongside protection against repeated guessing. Without it, User Story 1's login capability would be unsafe to ship.

**Independent Test**: Given an employee account, submit repeated incorrect PIN attempts and confirm that after the defined threshold, further attempts — even with the correct PIN — are rejected and remain rejected indefinitely until a Manager/Admin unlocks the account.

**Acceptance Scenarios**:

1. **Given** an employee account with no recent failed attempts, **When** several consecutive incorrect PINs are submitted, **Then** the account becomes locked once the defined threshold is reached.
2. **Given** a locked account, **When** the correct PIN is submitted while still locked, **Then** authentication is still rejected.
3. **Given** a locked account, **When** no Manager/Admin has unlocked it, **Then** authentication remains rejected indefinitely, regardless of how much time has passed.
4. **Given** an employee successfully authenticates, **When** their prior failed attempts had not yet reached the lockout threshold, **Then** the failed-attempt count resets.
5. **Given** a locked account, **When** a login attempt is made, **Then** the rejection explicitly states the account is locked and that a Manager/Admin must unlock it, distinct from the generic message used for a wrong PIN or nonexistent username.

---

### User Story 3 - Manager Maintains the Employee Roster (Priority: P2)

A Manager/Admin creates new employee accounts, deactivates employees who no longer work at Loot, and assigns each employee's role, so the roster of who can log in stays accurate as staff changes.

**Why this priority**: Accounts must exist before User Story 1 can be exercised for a given employee, and Loot's staff will change over time — but this is administrative upkeep rather than the core picking-enablement capability, so it follows login itself.

**Independent Test**: As a Manager/Admin, create a new employee account with a username, initial PIN, and role; confirm the new employee can log in (User Story 1). Deactivate an existing employee; confirm that employee can no longer log in (User Story 1, Acceptance Scenario 4).

**Acceptance Scenarios**:

1. **Given** a Manager/Admin is authenticated, **When** they create a new employee account with a unique username and role, **Then** the account exists and that employee can subsequently log in with the assigned credential.
2. **Given** a Manager/Admin attempts to create an employee account with a username already in use, **When** they submit it, **Then** the system rejects the duplicate and identifies the conflict.
3. **Given** a Manager/Admin is authenticated, **When** they deactivate an active employee account, **Then** that employee can no longer authenticate, without deleting their historical identity needed for past auditability records (e.g., "picked by").
4. **Given** a Picker-role employee (not Manager/Admin), **When** they attempt to create, deactivate, reactivate, or modify another employee's account, **Then** the action is rejected.
5. **Given** a Manager/Admin is authenticated, **When** they reset an employee's PIN, **Then** that employee's previous PIN no longer authenticates them and the newly issued PIN does.
6. **Given** a Manager/Admin is authenticated, **When** they unlock a locked employee account, **Then** that employee can authenticate again with their existing PIN, without it having been reset.
7. **Given** an employee account that was locked at the time it was deactivated, **When** a Manager/Admin reactivates it, **Then** the lockout is cleared and that employee can authenticate again with their existing PIN, without a separate unlock action.

---

### User Story 4 - Authenticated Session Persists Across Normal Interruptions (Priority: P2)

An employee who is already logged in reloads the page or briefly navigates away, and remains authenticated without needing to log in again.

**Why this priority**: Re-entering a PIN on every page load or brief interruption would make the application slower and more frustrating than the paper process it's replacing, undermining V1's core usability goal — but it is not required for the login capability to exist at all.

**Independent Test**: Authenticate, reload the page (or simulate returning after a short interval within the session lifetime), and confirm the employee is still recognized as authenticated without re-entering credentials.

**Acceptance Scenarios**:

1. **Given** an authenticated employee, **When** the page is reloaded, **Then** the employee remains authenticated without re-entering credentials.
2. **Given** an authenticated employee's session has been idle longer than the session's defined lifetime, **When** they next interact with the application, **Then** they are required to re-authenticate.
3. **Given** an employee explicitly logs out, **When** they next access the application, **Then** they are required to re-authenticate.

---

### Edge Cases

- When an employee's account is deactivated while they have an active session, that session is invalidated immediately rather than allowed to continue until it would otherwise expire (see Assumptions).
- When a username does not correspond to any account, the system's rejection is indistinguishable from a wrong-PIN rejection for an existing username, so a login attempt cannot be used to discover which specific usernames exist. This does not extend to lockout: a locked account is reported distinctly (see Clarifications), which does reveal that account's existence — a deliberate usability tradeoff for this internal tool.
- When two devices or browser tabs authenticate as the same employee, both may remain active; this feature does not restrict an employee to a single concurrent session.
- When a Manager/Admin deactivates their own account, the same deactivation rules apply as for any other employee (no special exemption), including immediate session invalidation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an employee to authenticate using their individual username and PIN.
- **FR-002**: System MUST support at least two distinct employee roles: Picker and Manager/Admin.
- **FR-003**: System MUST NOT store an employee PIN in plaintext under any circumstance; PINs MUST be stored using a secure, industry-standard hashing mechanism.
- **FR-004**: System MUST reject an authentication attempt with an incorrect username or PIN using a single generic rejection message that does not reveal whether the submitted username corresponds to an existing account — a nonexistent username and a wrong PIN for a real account MUST produce the same response. This generic message applies only to invalid-credential rejections; a locked account is reported distinctly per FR-021.
- **FR-005**: System MUST reject authentication for an employee account that has been deactivated, regardless of whether the correct PIN is supplied.
- **FR-006**: System MUST lock an employee account from further authentication attempts after a defined number of consecutive failed PIN attempts, to prevent PIN-guessing. The lockout does not clear on its own; it persists until a Manager/Admin unlocks the account (FR-022).
- **FR-007**: System MUST reset an employee's failed-attempt count upon their next successful authentication.
- **FR-008**: System MUST require an employee PIN to be a 4-digit numeric code. Any 4-digit numeric value MUST be accepted; the system MUST NOT reject a PIN for being a weak or predictable value (e.g., repeating or sequential digits) — brute-force protection is provided by the lockout mechanism (FR-006), not by PIN complexity rules.
- **FR-009**: System MUST allow only a Manager/Admin-role employee to reset another employee's PIN, issuing a new PIN the employee must use to authenticate going forward; self-service PIN reset is not supported.
- **FR-010**: System MUST maintain an authenticated employee's session across page reloads and brief interruptions without requiring re-authentication, expiring the session after 30 minutes of inactivity.
- **FR-011**: System MUST require re-authentication once a session has expired or the employee has explicitly logged out.
- **FR-012**: System MUST immediately invalidate any active session belonging to an employee whose account is deactivated.
- **FR-013**: System MUST allow a Manager/Admin-role employee to create new employee accounts, each with a unique username, an initial PIN, and an assigned role.
- **FR-014**: System MUST reject creation of an employee account whose username duplicates an existing account's username, comparing case-insensitively.
- **FR-015**: System MUST allow a Manager/Admin-role employee to deactivate an active employee account and to reactivate a previously deactivated one. Reactivating an account that was locked (FR-006) MUST also clear the lockout and reset the failed-attempt count, restoring the ability to authenticate with the employee's existing PIN without a separate unlock action.
- **FR-016**: System MUST NOT permit a Picker-role employee to create, deactivate, reactivate, or modify any employee account, including their own role.
- **FR-017**: System MUST retain a deactivated employee's identity rather than deleting it, so that historical records referencing that employee (e.g., a completed pick attributed to them) remain intact and attributable.
- **FR-018**: System MUST associate every action this feature enables that changes state (login, logout, account creation, deactivation, reactivation) with the specific employee who performed it, for later auditability.
- **FR-019**: System MUST enforce authentication and role checks server-side; frontend checks alone MUST NOT be the sole enforcement point for who can authenticate or perform account-management actions.
- **FR-020**: System MUST allow an authenticated employee to explicitly log out, ending their session immediately.
- **FR-021**: System MUST reject a login attempt against a locked account (FR-006) with a message distinct from the generic invalid-credentials message (FR-004), explicitly stating the account is locked and that a Manager/Admin must unlock it.
- **FR-022**: System MUST allow a Manager/Admin-role employee to unlock a locked employee account, restoring the ability to authenticate with the employee's existing PIN (unchanged) and resetting the failed-attempt count. Unlocking is independent of resetting a PIN (FR-009) — a Manager/Admin may unlock without changing the PIN, or reset the PIN without a prior lockout.

### Key Entities *(include if feature involves data)*

- **Employee**: Represents an individual Loot staff member who can authenticate into the application. Key attributes: employee identifier, display name, username (unique, case-insensitive), PIN (hashed, never plaintext), role (Picker or Manager/Admin), active/inactive status, failed-attempt count, and current lockout state (cleared only by a Manager/Admin unlock or by reactivating the account, never by elapsed time). Referenced by other, future features (order claiming, picking, issue reporting) via employee identity for auditability.
- **Session**: Represents one authenticated login for an Employee. Key attributes: the associated employee, creation timestamp, expiration/last-activity timestamp, and whether it has ended (explicit logout) or been invalidated (e.g., due to account deactivation).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An employee with valid credentials can complete login in under 10 seconds under normal conditions.
- **SC-002**: 100% of authentication attempts with an incorrect username/PIN combination are rejected, verified across a representative sample including nonexistent usernames, wrong PINs for existing usernames, and deactivated accounts.
- **SC-003**: 0 instances of a PIN being discoverable in plaintext anywhere in persistent storage, verified by inspecting stored credential data directly.
- **SC-004**: 100% of accounts subjected to repeated incorrect PIN attempts beyond the defined threshold are locked out, preventing further authentication attempts (including with the correct PIN) until a Manager/Admin unlocks the account — verified including that the lockout does not clear on its own regardless of elapsed time.
- **SC-005**: For any action this feature enables that changes state, an operator can determine which specific employee performed it without inspecting application code or database internals directly.
- **SC-006**: An authenticated employee's session survives a page reload with no re-authentication required, 100% of the time within the session's defined lifetime.
- **SC-007**: 0 instances of a Picker-role employee successfully performing an account-management action (create/deactivate/reactivate/modify) reserved for Manager/Admin, verified across a representative sample of attempts.
- **SC-008**: A deactivated employee cannot authenticate, verified 100% of the time immediately after deactivation — including for an employee who held an active session at the moment of deactivation.
- **SC-009**: 100% of login rejections correctly distinguish between the generic invalid-credentials message (wrong PIN or nonexistent username) and the distinct locked-account message, verified across a representative sample of each case.

## Assumptions

- The specifics of how a login screen presents itself on a shared device (e.g., a quick-select employee list versus manual username entry) are a UX/frontend decision for technical planning; this feature's requirement is only that authentication via username + PIN is possible, per the PRD's own "could allow" framing (PRD §9.1).
- Deactivating an employee account immediately invalidates any of that employee's currently active sessions, since deactivation implies their access should stop immediately, consistent with this project's general preference for safe rejection over silently continued access.
- Manager/Admin capabilities in this feature are scoped strictly to employee-account management (create, deactivate, reactivate, assign role). Any other Manager/Admin capability implied elsewhere in the PRD (e.g., force-releasing a claimed order) is out of scope for this feature and belongs to whichever future feature introduces that specific capability — the PRD itself states exact Manager/Admin capabilities "remain to be finalized" (PRD §9.4).
- The exact failed-attempt lockout threshold (FR-006) is a technical-planning detail; this feature only requires that some such protection exists, with a specific default chosen during planning informed by standard practice for short-PIN credential schemes. Because the PIN is a 4-digit numeric code (FR-008, 10,000 possible combinations) and lockouts do not clear on their own (FR-006, FR-022), the threshold should be chosen conservatively — low enough to stop guessing quickly, high enough that ordinary mistyping doesn't routinely require a Manager/Admin to intervene.
- This feature does not implement order claiming, the picking workflow, or issue reporting. It establishes the employee identity and session mechanism those future features will depend on.
- Concurrent sessions for the same employee across multiple devices or browser tabs are permitted; this feature does not restrict an employee to a single active session.
- Username uniqueness is enforced case-insensitively, since usernames are expected to be short and entered quickly, often on shared devices.
- Because lockouts require a Manager/Admin to clear (FR-006, FR-022) with no automatic expiration, at least one Manager/Admin account must always exist and be able to authenticate; this feature does not need to solve "every Manager/Admin is locked out simultaneously" as a designed recovery path, but technical planning should ensure that scenario cannot arise from ordinary use (e.g., seed/protect an initial Manager/Admin account).
