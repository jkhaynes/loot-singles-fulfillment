# Research: Employee Authentication

## 1. PIN Hashing Mechanism

**Decision**: Use `Microsoft.AspNetCore.Identity`'s `PasswordHasher<TUser>` (PBKDF2-HMACSHA256, ASP.NET Core's default) standalone, generic over the Domain's own `Employee` type, wrapped behind an Application-layer `IPinHasher` interface. On .NET 10 (plan.md's Post-Phase 2 correction), this type ships as part of the `Microsoft.AspNetCore.App` shared framework `LootSingles.Infrastructure` already references — no separate `Microsoft.Extensions.Identity.Core` NuGet package is needed (or accepted; .NET 10's package pruning rejects it as redundant).

**Rationale**: PRD §40.5 directs "established ASP.NET Core Core authentication and credential-security primitives." `PasswordHasher<TUser>` is exactly that primitive — it does not require adopting the rest of ASP.NET Core Identity (see §9 below) and works against any plain class, so `Employee` stays a Domain POCO rather than an `IdentityUser` subclass (Constitution XII). Satisfies FR-003 (never plaintext, industry-standard hashing).

**Alternatives considered**:
- `BCrypt.Net-Next` — a well-regarded third-party hasher, but not an "ASP.NET Core primitive"; adds a dependency PRD §40.5 doesn't call for when a built-in one already satisfies FR-003.
- Full ASP.NET Core Identity (`UserManager`/`SignInManager`) — rejected, see §9.

## 2. Session Mechanism

**Decision**: ASP.NET Core Cookie Authentication middleware (`Microsoft.AspNetCore.Authentication.Cookies`), HttpOnly + Secure + `SameSite=Strict`, sliding expiration with a 30-minute inactivity window.

**Rationale**: PRD §40.5: "For the internal browser application, secure cookie-based authentication is the preferred V1 direction unless technical planning identifies a concrete requirement for a different mechanism" — no such requirement exists here. Sliding expiration directly implements FR-010 (persists across reload, 30-minute inactivity timeout) and FR-011 (re-authenticate after expiry) without custom timer logic.

**Alternatives considered**:
- JWT bearer tokens in local/session storage — rejected; adds client-side token storage and refresh-flow complexity for a same-origin browser PWA where PRD explicitly prefers cookies, and would need separate work to prevent access from JS (XSS surface) that HttpOnly cookies get for free.

## 3. Immediate Session Invalidation on Deactivation

**Decision**: A `CookieAuthenticationEvents.ValidatePrincipal` override checking the current `Employee.IsActive` (by the id claim) on every authenticated request; calls `context.RejectPrincipal()` when `false` or the employee no longer exists. No explicit `SignOutAsync` call is needed — the raw cookie handler (not wrapped in ASP.NET Core Identity) raises `ValidatePrincipal` on every request by default, so a rejected principal simply keeps failing on each subsequent request; the browser-held cookie becomes permanently inert rather than being proactively cleared.

**Rationale**: A cookie ticket is otherwise self-contained and would keep authenticating a deactivated employee until the ticket's own expiry, violating FR-012/SC-008 ("immediately... including for an employee who held an active session at the moment of deactivation"). Validating on every request is a single indexed primary-key lookup — negligible at this application's scale (Technical Context: tens of employees) — and keeps the cookie itself dead simple (Constitution XIII).

**Alternatives considered**:
- A persisted, revocable `Session`/token table checked on each request — rejected as unneeded machinery; the same guarantee already falls out of checking the `Employee` row that must exist anyway, with no second table to keep consistent (Constitution XIII).
- Periodic (non-zero) `ValidationInterval` — rejected; any positive interval reopens a window where a deactivated employee's existing session keeps working, contradicting "immediately."

## 4. Lockout State Modeling

**Decision**: Explicit Domain fields on `Employee` — `FailedAttemptCount` (int) and `IsLocked` (bool) — rather than ASP.NET Core Identity's built-in time-based lockout (`LockoutEnd`, `AccessFailedCount`).

**Rationale**: Identity's built-in lockout is designed to *auto-expire* at `LockoutEnd`; FR-006/SC-004 require the opposite — lockout must never clear from elapsed time alone, only from an explicit Manager/Admin unlock (FR-022) or a reactivation that was locked at deactivation time (FR-015, per `/speckit-clarify` 2026-08-21). Setting `LockoutEnd = DateTimeOffset.MaxValue` as a "permanent lock" is a known workaround, but it encodes a permanent business rule as an indirect consequence of a time-comparison field — exactly the kind of clever-but-indirect design Constitution XIII discourages. A plain `IsLocked` boolean says what it means.

**Alternatives considered**:
- Identity's `LockoutEnd = DateTimeOffset.MaxValue` technique — rejected for the indirectness above.
- A separate `EmployeeLockout` table/entity — rejected; lockout is 1:1 with `Employee` and has no independent lifecycle worth its own table (Constitution XIII).

## 5. Failed-Attempt Lockout Threshold

**Decision**: 5 consecutive failed PIN attempts, defined as a named constant/configuration value (not hardcoded inline), configurable via `appsettings` without a code change.

**Rationale**: Spec Assumptions explicitly defer the exact number to planning, with guidance: "low enough to stop guessing quickly, high enough that ordinary mistyping doesn't routinely require a Manager/Admin to intervene." Against a 4-digit / 10,000-combination PIN space (FR-008), 5 attempts caps blind-guess odds at 5/10,000 (0.05%) before lockout while tolerating a couple of realistic mistypes on a shared device.

**Alternatives considered**:
- 3 attempts — too easily triggered by ordinary mistyping (e.g., cold hands, a shared device, a rushed picker), which the Assumptions text specifically warns against given lockout requires Manager/Admin intervention to clear.
- 10 attempts — meaningfully raises blind-guess odds (0.1%) with no real usability benefit over 5.

## 6. Role Representation

**Decision**: `enum EmployeeRole { Picker, ManagerAdmin }`, persisted via an EF Core string value conversion (not the raw int).

**Rationale**: FR-002 requires "at least two distinct employee roles: Picker and Manager/Admin"; `/` is not a valid C# identifier, so `ManagerAdmin` is the code-facing name for that one V1 role. Storing as a string avoids silent data corruption if enum member order/values ever change.

**Alternatives considered**:
- Separate `Manager` and `Admin` roles — rejected; FR-002 and PRD §9.4 both treat "Manager/Admin" as a single V1 role, and PRD §9.4 itself says "exact Manager/Admin capabilities remain to be finalized" for *future* refinement, not this feature. Splitting it now would be an invented requirement (Constitution II).
- Persisting the raw `int` — rejected per the corruption risk above.

## 7. Username Case-Insensitive Uniqueness

**Decision**: Store a `NormalizedUsername` (invariant-uppercase) column alongside `Username`, with a unique index on `NormalizedUsername`. Login lookup and duplicate-check both normalize the submitted username the same way before comparing.

**Rationale**: FR-014 requires case-insensitive uniqueness enforced server-side (Constitution VI). A normalized column with a DB unique index is explicit in the schema and independent of SQL Server collation configuration.

**Alternatives considered**:
- A case-insensitive DB collation on `Username` itself — rejected; correctness would depend on a collation setting configured outside the schema/migration, which is easy to lose track of across environments.

## 8. Auditability (FR-018)

**Decision**: A new `EmployeeAuditEvent` table (`ActorEmployeeId`, `ActionType`, `TargetEmployeeId` nullable, `OccurredAt`), written for every state-changing action this feature enables: successful login, logout, account creation, deactivation, reactivation, PIN reset, and unlock. A Manager/Admin-only read endpoint (`GET /api/employees/{id}/audit-events`, see contracts/employee-management-api.md) exposes it.

**Rationale**: FR-018 requires attributing "every action this feature enables that changes state" to the performing employee; its parenthetical list (login, logout, creation, deactivation, reactivation) is illustrative, not exhaustive of the feature's own state-changing actions, so PIN reset and unlock are included too under the general clause. SC-005 requires an operator be able to determine this "without inspecting application code or database internals directly" — a read endpoint satisfies that; direct DB inspection would not.

**Alternatives considered**:
- A generic, cross-feature audit-log abstraction — rejected as premature; Constitution XIII prefers duplication over a shared abstraction until a second concrete feature actually needs the same one (order claiming/picking auditability, when built, can either reuse this table or introduce its own — that decision belongs to that feature's own plan).
- No dedicated table; rely on database transaction/change logs — rejected; not queryable by "the specific employee" without inspecting database internals, failing SC-005 directly.

## 9. Full ASP.NET Core Identity vs. Standalone Primitives

**Decision**: Do not adopt the full ASP.NET Core Identity framework (`UserManager<TUser>`, `SignInManager<TUser>`, `RoleManager`, `IdentityDbContext`). Use only its `PasswordHasher<T>` primitive (§1) plus the framework-level Cookie Authentication middleware (§2), both invoked directly from a plain `AuthenticationService` in `LootSingles.Application`.

**Rationale**: Full Identity brings substantial unused surface for this feature — email confirmation, phone numbers, two-factor authentication, external logins, `IdentityRole` management UI conventions — none of which trace to any spec FR or PRD passage. Constitution XIII requires the simplest design satisfying the requirements and warns against machinery introduced "because a design pattern could apply." PRD §40.5's own phrase is "credential-security *primitives*," which the two adopted pieces literally are, without requiring the framework built around them. It also keeps `Employee` a plain Domain POCO instead of forcing it to extend `IdentityUser<TKey>`, preserving Constitution XII's domain/persistence-framework independence.

**Alternatives considered**:
- Full ASP.NET Core Identity with `Employee : IdentityUser<Guid>` — rejected for the reasons above; its built-in lockout semantics also conflict with §4's requirement (auto-expiring vs. never-auto-clearing).

## 10. Frontend Scope for This Feature

**Decision**: This feature scaffolds `frontend/` (React + TypeScript + Vite, first use per PRD §40.1/§40.2) with only a login screen and an authenticated-session context (whoami-on-load, logout). Manager/Admin roster-management actions (US3) are exposed as authenticated REST endpoints only; no dedicated admin UI screen is built in this feature. PWA manifest/service-worker/offline configuration is also out of scope here.

**Rationale**: Spec Assumptions explicitly frame "how a login screen presents itself" as a decision "for technical planning" on *this* feature — unlike feature 001, which explicitly deferred its entire UI to "whichever future feature first needs it" because no UI was needed to exercise 001's acceptance criteria at all. Here, US1 ("an employee opens the application and authenticates") cannot be demonstrated as a real browser flow without some login UI, and the Definition of Done requires Playwright validation of critical user flows — login qualifies, roster management does not have an equivalent UI-flow requirement anywhere in spec or PRD. Building a roster-management screen now would be scope beyond FR-013–FR-017/FR-022's literal "System MUST allow a Manager/Admin... to X," which a secured API endpoint already satisfies (Constitution II). PWA-specific tooling (offline support, installability) is not requested by spec 002 or PRD §9.

**Alternatives considered**:
- Also build a roster-management UI now — rejected as unrequested scope (Constitution II); can be proposed as its own future feature.
- No frontend at all (validate login via API calls / integration tests only) — rejected; would leave US1 unvalidatable as an actual employee-facing flow and would not satisfy the Definition of Done's Playwright requirement.
- Full PWA setup now (manifest, service worker) — rejected; not called for by spec 002, and would duplicate/preempt whatever future feature is responsible for offline behavior.
