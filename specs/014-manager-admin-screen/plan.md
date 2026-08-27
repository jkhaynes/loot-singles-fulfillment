# Implementation Plan: Manager Admin Screen

**Branch**: `014-manager-admin-screen` | **Date**: 2026-08-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-manager-admin-screen/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add the first-ever UI for the employee-account-management capabilities feature 002 already built
server-side (create, deactivate/reactivate, reset PIN, unlock) — none of them have ever been
reachable through any screen — plus one genuinely new capability, changing an employee's role
after creation. The core technical challenge is a safety guard neither feature nor prior code has
needed until now: preventing any admin action (deactivation or the new role change) from leaving
the system with zero active Manager/Admin employees, race-free, reusing the exact
`sp_getapplock`-based serialization pattern this codebase already established for the analogous
"exactly one first-employee bootstrap" race in `EmployeeRepository.TryAddFirstEmployeeAsync`.
Role changes also immediately invalidate the affected employee's session by extending the existing
per-request `EmployeeSessionCookieEvents.ValidatePrincipal` check (currently only checks
`IsActive`) to also compare the cookie's role claim against the employee's current role.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (existing backend), TypeScript / React 18 (existing frontend,
`.tsx` components under `frontend/src/features/`) — unchanged from prior features.

**Primary Dependencies**: ASP.NET Core Web API, EF Core 8 (SQL Server provider), cookie-based
`ClaimsPrincipal` auth via `EmployeeSessionCookieEvents` — all existing, no new dependencies.

**Storage**: SQL Server via EF Core (`LootSinglesDbContext`), same `Employees` and
`EmployeeAuditEvents` tables feature 002 already created. This feature adds one new
`EmployeeAuditActionType` enum value (`RoleChanged`) — an int-backed enum column, so no migration
touches existing data — and no new tables or columns.

**Testing**: xUnit (backend unit/integration, including a real-SQL-Server concurrency test for the
last-Manager/Admin race, mirroring `SqlServerConcurrencyTests.cs`'s `sp_getapplock` bootstrap-race
test), Vitest + React Testing Library (frontend component tests, established in
`frontend/tests/`), Playwright (frontend E2E, established in `frontend/e2e/`).

**Target Platform**: Same as existing app — server-hosted ASP.NET Core API + browser-based React
SPA, used on shop-floor desktop/tablet browsers.

**Project Type**: Web application (existing `backend/` + `frontend/` structure).

**Performance Goals**: Standard web-app expectations for an internal tool with a small (dozens,
not thousands) employee roster; no specific throughput target.

**Constraints**: The system MUST NOT ever end up with zero active Manager/Admin employees as a
result of a deactivation or role change, including under concurrent admin actions (spec.md FR-007,
Edge Cases). A role change MUST invalidate the affected employee's active session immediately
(FR-010). Every action MUST be enforced server-side, not solely by the frontend (FR-013,
constitution Principle VI's server-enforcement ethos, matching FR-019 from feature 002).

**Scale/Scope**: Single-shop deployment; a handful to a few dozen employee accounts total. No
pagination, search, or bulk-action requirements implied by the spec.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Assessment |
|---|---|---|
| I. TCGplayer Data Authority | No | Not related to order data. |
| II. Catalog Enrichment is Supplemental | No | Not related to card-catalog enrichment. |
| III. Explicit Handling of Picking Problems | No | Not related to picking. |
| IV. Test-First Development (TDD, MUST) | Yes | Every new/changed behavior (role change, the last-Manager/Admin guard extended to deactivation, session invalidation on role change) MUST be built Red→Green→Refactor, including a genuine concurrent-request test for the last-Manager/Admin race, mirroring the existing bootstrap-race test's real-SQL-Server pattern. |
| V. No Image Is Better Than the Wrong Image | No | No card imagery in this feature. |
| VI. Server-Enforced Critical Business Rules (MUST) | Yes | The zero-Manager/Admin guard and role/access enforcement MUST be server-side; the frontend route guard is a UX convenience, never the sole enforcement point (FR-013). |
| VII. Employee PIN Security | Yes | Reuses the existing PIN-hashing mechanism unchanged (creating an account still uses `IPinHasher`); this feature introduces no new PIN handling. |
| VIII. PDF Import Fails Safely | No | Not related to import. |
| IX. Replaceable Card-Catalog Integrations | No | Not related to card-catalog providers. |
| X. Minimized, Purpose-Limited PII | Yes | Exposes only already-existing internal staff fields (username, display name, role, status) to Manager/Admin employees — no new PII, no customer data. |
| XI. Observability | Yes | FR-011 requires every state-changing action this screen enables — including the new role change — to be recorded via the existing `EmployeeAuditEvent` mechanism. |
| XII. Maintainable, Evolvable Design | Yes | Reuses the existing `EmployeeManagementService`/`EmployeeManagementResult` outcome-enum pattern, the existing `IEmployeeRepository` seam, and the existing `[Authorize(Roles=...)]`/`ActorEmployeeId()` conventions rather than inventing new ones. |
| XIII. Simplicity & Proportional Abstraction | Yes | No new tables, no new roles, no new persistence technology — one new enum value, one new service method, one new endpoint, one new repository guard reusing an already-proven technique. |

No violations requiring justification. Gate passes.

**Post-design re-check** (after Phase 1 data-model.md/contracts/quickstart.md): the design adds one
enum value each to `EmployeeAuditActionType` and `EmployeeManagementOutcome`, one new endpoint
following the controller's exact existing convention, and one repository guard reusing an
already-proven concurrency technique from this same codebase — no new principle exposure surfaced
by the concrete design. Gate still passes; no updates to the table above are needed.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── LootSingles.Domain/
│   │   └── Employees/
│   │       └── EmployeeAuditActionType.cs   # + RoleChanged
│   ├── LootSingles.Application/
│   │   └── Auth/
│   │       ├── EmployeeManagementResult.cs  # + WouldRemoveLastManagerAdmin outcome
│   │       ├── EmployeeManagementService.cs # + ChangeRoleAsync; DeactivateAsync gains the guard
│   │       └── IEmployeeRepository.cs       # + CountActiveManagerAdminsAsync (or equivalent)
│   ├── LootSingles.Infrastructure/
│   │   ├── Persistence/
│   │   │   └── EmployeeRepository.cs        # + the sp_getapplock-guarded count/guard, mirroring TryAddFirstEmployeeAsync
│   │   └── Auth/
│   │       └── EmployeeSessionCookieEvents.cs # + reject on role-claim mismatch, not just !IsActive
│   └── LootSingles.Api/
│       └── Controllers/
│           └── EmployeesController.cs       # + POST {id}/change-role
└── tests/
    ├── LootSingles.UnitTests/Auth/EmployeeManagementServiceTests.cs
    └── LootSingles.IntegrationTests/
        ├── Auth/EmployeesControllerTests.cs
        └── Persistence/EmployeeLastManagerAdminConcurrencyTests.cs  # new, real SQL Server

frontend/
├── src/
│   ├── App.tsx                       # + a Manager/Admin-only route guard, the first one needed
│   └── features/
│       ├── auth/authApi.ts           # AuthenticatedEmployee.role already present, reused
│       └── admin/                    # new feature folder
│           ├── AdminPage.tsx
│           ├── AdminPage.css
│           └── adminApi.ts
└── e2e/
    └── admin.spec.ts                 # new
```

**Structure Decision**: Existing web-application structure (`backend/` ASP.NET Core API +
`frontend/` React SPA) is reused as-is. A new `frontend/src/features/admin/` folder is added,
matching the existing per-feature folder convention (`features/orders/`, `features/dashboard/`,
etc.); no new projects or top-level directories.

## Complexity Tracking

*No Constitution Check violations — this section is not applicable.*
