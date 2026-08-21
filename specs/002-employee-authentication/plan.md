# Implementation Plan: Employee Authentication

**Branch**: `002-employee-authentication` | **Date**: 2026-08-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-employee-authentication/spec.md`

## Summary

Individual employee username + 4-digit-PIN authentication for the picker application, backed by server-enforced credential security (PBKDF2 PIN hashing via an ASP.NET Core primitive, never plaintext), a never-auto-clearing failed-attempt lockout that only a Manager/Admin can lift (by explicit unlock or by reactivating a previously locked-and-deactivated account), secure cookie-based sessions (30-minute sliding inactivity timeout, immediate invalidation on deactivation), and Manager/Admin-only employee roster management (create, deactivate, reactivate, PIN reset, unlock). This is the first feature to add an HTTP surface to `LootSingles.Api` and the first to introduce the `frontend/` project (a minimal login screen only — roster management is API-only in V1, since no spec or PRD passage calls for a dedicated admin UI screen yet).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (LTS) (backend — corrected 2026-08-21, see note below); TypeScript / React (frontend, per PRD §40.1/§40.2) — first feature to scaffold `frontend/`

**Primary Dependencies**: ASP.NET Core Cookie Authentication middleware (`Microsoft.AspNetCore.Authentication.Cookies` — PRD §40.5's preferred "secure cookie-based authentication"), `Microsoft.AspNetCore.Identity`'s `PasswordHasher<T>` used standalone for PBKDF2 PIN hashing (an "established ASP.NET Core credential-security primitive" per PRD §40.5, without adopting the full Identity framework — see research.md §1), Entity Framework Core (persistence, consistent with feature 001); frontend: React, Vite, `react-router` for the login route, Vitest + React Testing Library, Playwright for the critical login E2E flow

**Storage**: Azure SQL Database in production, SQL Server (LocalDB/containerized) for local dev and integration tests, via EF Core — consistent with feature 001

**Testing**: xUnit (backend unit/integration, per constitution Principle IV), Vitest + React Testing Library (frontend unit), Playwright (critical end-to-end flow: login, per Definition of Done)

**Target Platform**: ASP.NET Core Web API on Azure Container Apps (backend, unchanged from 001); React PWA on Azure Static Web Apps (frontend, PRD §40.1/§40.2) — this feature scaffolds the frontend project but does not add PWA manifest/offline/service-worker configuration (out of scope, see research.md §10)

**Project Type**: Web application (backend + frontend) — first feature with both an HTTP surface and a frontend

**Performance Goals**: Login completes in under 10 seconds under normal conditions (SC-001); per-request session validation (live `IsActive` check, see research.md §3) adds a single indexed-PK lookup, negligible at this application's scale

**Constraints**: PIN never stored or logged in plaintext (FR-003); authentication and role checks enforced server-side, never solely in the frontend (FR-019); lockout never clears on elapsed time alone, only via explicit Manager/Admin unlock or reactivation (FR-006, FR-015, FR-022); session invalidated immediately (not on next natural expiry) when the owning account is deactivated (FR-012)

**Scale/Scope**: Single-business internal tool; small employee roster (Loot's own staff, expected to be on the order of tens of accounts, not thousands); multiple concurrent sessions per employee across devices are expected and permitted

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Product Owner Authority | Plan implements the approved, PO-clarified spec only (including both `/speckit-clarify` sessions); no PO decision overridden | PASS |
| II. No Invented Requirements | Every design element traces to a spec FR/SC or an explicit PRD §9/§40.5 direction; roster-management UI is deliberately *not* built because nothing requires it (research.md §9) | PASS |
| III. Small, Reviewable Changes | Work happens on feature branch `002-employee-authentication`, not `main` | PASS |
| IV. Test-Driven Development | Login (US1), lockout (US2), roster management (US3), and session persistence (US4) are all new application behavior requiring Red → Green → Refactor coverage; enforced during `/speckit-implement`, not by this plan alone | PASS (design supports it; execution-time gate) |
| V. Safe Failure Over Silent Corruption | Invalid credentials and locked/deactivated accounts are explicitly rejected, never silently accepted (FR-004, FR-005, FR-021); no "best guess" authentication path exists | PASS |
| VI. Server-Enforced Critical Business Rules | Credential checks, role checks (FR-019), and lockout state all live in `LootSingles.Application`/`LootSingles.Domain` and are enforced by the backend on every request; the frontend never independently decides who is authenticated or authorized | PASS |
| VII. Data Minimization and Credential Security | PINs hashed via PBKDF2 (`PasswordHasher<T>`), never plaintext, never logged (FR-003); no customer PII is part of this feature at all | PASS |
| VIII. One Responsive Product | Login screen is part of the single responsive React PWA shell, not a separate codebase; exact responsive layout is a UI detail within quickstart/tasks | PASS |
| IX. Replaceable Integrations | N/A directly — this feature has no external TCGplayer/catalog integration; internal auth primitives (`IPinHasher`, `IEmployeeRepository`) are still defined as Application-layer interfaces so the Infrastructure implementation remains swappable | N/A / PASS |
| X. Single-Business Scope | Single employee roster, no tenant concept introduced | PASS |
| XI. Reliability During Fulfillment | Session validation adds one indexed lookup per request; no reliability/cost tradeoff that could destabilize active picking is introduced | PASS |
| XII. Maintainable and Extensible Design | `Employee` is a plain Domain POCO (not an `IdentityUser` subclass) so Domain does not depend on the persistence/auth framework directly; lockout state is explicit domain state, not framework-internal (research.md §4) | PASS |
| XIII. Simplicity and Proportional Abstraction | Full ASP.NET Core Identity (UserManager/SignInManager/roles/2FA/email) is deliberately *not* adopted — only the two primitives PRD §40.5 actually calls for (`PasswordHasher<T>`, Cookie Authentication) are used; no persisted `Session` table is introduced where a live `IsActive` check on the existing `Employee` row already satisfies every requirement (research.md §2–§4) | PASS |

No violations to justify — Complexity Tracking is empty.

**Post-Phase 1 re-check**: research.md, data-model.md, and contracts/ confirm the plain-POCO `Employee` domain model (XII), the explicit non-time-based lockout fields (XIII), and the deliberately API-only roster-management scope (II) as concrete designs, not just intentions. No new violations were introduced by the detailed design; all gates remain PASS/N/A as above.

**Post-Phase 2 correction (2026-08-21) — target framework**: this plan originally stated ".NET 8" as the backend Language/Version, citing "PRD §40.3." On re-reading, neither PRD §40.3 nor the README actually pin a .NET version — both say only "ASP.NET Core Web API" / "C#"; ".NET 8" was an unstated assumption carried over from feature 001's plan (written when .NET 8 was current). Raised during Phase 2 implementation (this machine has no .NET 8 SDK at all, only .NET 10), the Product Owner confirmed the backend should target .NET 10 (the current LTS; .NET 8 support ends ~November 2026) rather than continue building new work against a framework nearing end of life. Since all six backend projects share one solution, this retargets feature 001's already-merged code (`net8.0` → `net10.0` across `LootSingles.Domain/Application/Infrastructure/Api` and both test projects) as well as this feature's new code — a deliberate, Product-Owner-confirmed exception to Principle III's "no unrelated refactoring bundled into a feature," justified because splitting the six projects across two different target frameworks within one solution is not a viable intermediate state. `Microsoft.Extensions.Identity.Core`'s explicit `PackageReference` (research.md §1) was removed as part of this change: .NET 10's NuGet package-pruning treats it as redundant once `FrameworkReference Include="Microsoft.AspNetCore.App"` is present, since the identity primitives moved into the shared framework's own package set. No other Constitution Check gate is affected — this is a build/runtime target change, not a design change.

## Project Structure

### Documentation (this feature)

```text
specs/002-employee-authentication/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── auth-api.md
│   └── employee-management-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── LootSingles.Domain/
│   │   └── Employees/
│   │       ├── Employee.cs                   # Employee aggregate (FR-001–FR-003, FR-005–FR-008, FR-014, FR-017)
│   │       ├── EmployeeRole.cs                # Picker | ManagerAdmin (FR-002)
│   │       ├── EmployeeAuditEvent.cs          # Auditability record (FR-018)
│   │       └── EmployeeAuditActionType.cs
│   ├── LootSingles.Application/
│   │   └── Auth/
│   │       ├── IEmployeeRepository.cs         # Persistence seam (Constitution IX/XII)
│   │       ├── IPinHasher.cs                  # Hashing seam over PasswordHasher<T> (Constitution IX)
│   │       ├── AuthenticationService.cs       # Login: verify credentials, lockout, session claims (FR-001, FR-004–FR-007, FR-021)
│   │       ├── AuthenticationResult.cs        # Typed result: Success | InvalidCredentials | AccountLocked (Constitution XII)
│   │       └── EmployeeManagementService.cs   # Create/deactivate/reactivate/reset-pin/unlock (FR-009, FR-013–FR-017, FR-022)
│   ├── LootSingles.Infrastructure/
│   │   ├── Auth/
│   │   │   ├── Pbkdf2PinHasher.cs             # IPinHasher via PasswordHasher<Employee> (research.md §1)
│   │   │   └── EmployeeSessionCookieEvents.cs # OnValidatePrincipal: live IsActive check (FR-012, research.md §3)
│   │   └── Persistence/
│   │       ├── EmployeeRepository.cs
│   │       └── Configurations/
│   │           ├── EmployeeConfiguration.cs           # Unique index on NormalizedUsername (FR-014)
│   │           └── EmployeeAuditEventConfiguration.cs
│   └── LootSingles.Api/
│       ├── Program.cs                         # + AddAuthentication(Cookie), AddAuthorization (FR-019)
│       └── Controllers/
│           ├── AuthController.cs              # POST /api/auth/login, /logout, GET /api/auth/me
│           └── EmployeesController.cs         # Manager/Admin roster management + audit-events read
├── tests/
│   ├── LootSingles.UnitTests/
│   │   └── Auth/                              # Lockout rules, credential checks, role checks (FR-004–FR-009, FR-016, FR-021, FR-022)
│   └── LootSingles.IntegrationTests/
│       └── Auth/                              # Full login/session/roster flows against a real DB (US1–US4, concurrency of claim-adjacent lockout writes)

frontend/                                       # New in this feature (research.md §9)
├── src/
│   ├── features/
│   │   └── auth/
│   │       ├── LoginPage.tsx                  # US1: username + PIN entry
│   │       ├── AuthContext.tsx                # Session state: whoami-on-load, logout (US4)
│   │       └── authApi.ts                     # Calls auth-api.md endpoints
│   ├── App.tsx
│   └── main.tsx
├── tests/
│   └── auth/                                  # Vitest + React Testing Library
├── e2e/
│   └── login.spec.ts                          # Playwright: critical login flow (Definition of Done)
├── package.json
├── tsconfig.json
└── vite.config.ts
```

**Structure Decision**: Backend follows the same layered structure introduced in feature 001 (Domain → Application → Infrastructure → Api), adding an `Employees`/`Auth` vertical alongside the existing `Orders`/`Import` one — `IEmployeeRepository` and `IPinHasher` are the Application-layer seams that keep `LootSingles.Domain` and `LootSingles.Application` free of any direct dependency on EF Core or ASP.NET Core Identity types (Constitution XII). This is the first feature to add controllers to `LootSingles.Api` and the first to scaffold `frontend/`; per research.md §9, the frontend is limited to the login screen and session context that US1/US4 require to be exercisable in a browser at all — Manager/Admin roster management (US3) is implemented as authenticated REST endpoints only, with no dedicated admin screen, since no spec requirement or PRD passage calls for one in V1.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
