# Implementation Plan: Login Page and Dashboard UI Design

**Branch**: `003-login-dashboard-ui` | **Date**: 2026-08-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-login-dashboard-ui/spec.md`

## Summary

Give the already-functional login screen (from Employee Authentication) real visual design, and add a new post-login Dashboard implementing PRD §21's four order-status groupings. Per `/speckit-clarify`, only the Ready section is wired to real data (via one new read-only backend endpoint); the other three sections are static "not yet available" placeholders until the future order-claiming and picking-workflow features exist. Both screens apply the confirmed Product Owner visual design language (`docs/decisions/visual-design-language.md`): dark-mode-first, Loot green as the sole brand accent, soft rounded corners/restrained shadows, and the newly-introduced design-token system, implemented as plain CSS custom properties (no new styling framework). No formal accessibility standard applies (`/speckit-clarify` 2026-08-21).

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, unchanged); TypeScript / React 19 (frontend, unchanged) — this feature adds no new dependency to either project beyond what's already installed

**Primary Dependencies**: ASP.NET Core Web API + EF Core (backend, unchanged); React, Vite, `react-router-dom` (already an installed but unused frontend dependency — this feature still does not wire up client-side routing, see research.md §4), Vitest + React Testing Library, Playwright (frontend, unchanged)

**Storage**: Same SQL Server (prod) / SQLite (integration tests, superseded by 005-sql-server-migration — integration tests now use isolated SQL Server) via EF Core as features 001/002 — no schema change; the Dashboard's Ready section is a read-only projection over the existing `Orders`/`OrderLines` tables

**Testing**: xUnit (backend unit/integration), Vitest + React Testing Library (frontend unit/component), Playwright (critical flow: login → Dashboard)

**Target Platform**: ASP.NET Core Web API (backend, unchanged); React PWA-shaped SPA (frontend, unchanged)

**Project Type**: Web application (backend + frontend), extending the structure introduced in features 001/002

**Performance Goals**: Dashboard's Ready-section query is a single indexed-status-filtered read with server-side count/sum projection (SC-003: employee can state the Ready count within 3 seconds of load) — negligible load at this application's scale (tens of employees, a card shop's order volume)

**Constraints**: No change to existing login/session behavior (FR-001); Dashboard must not expose customer PII beyond what's already in order data (FR-007); no formal accessibility standard applies (`/speckit-clarify` 2026-08-21); Dashboard's In Progress/Needs Attention/Picked sections must never show a fabricated count (FR-005)

**Scale/Scope**: Single-business internal tool; small employee roster and order volume (consistent with features 001/002) — no pagination is introduced for the Ready list (matches the existing unpaginated `GET /api/employees` precedent)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Product Owner Authority | Plan implements the approved, clarified spec 003 and the Product Owner-confirmed `docs/decisions/visual-design-language.md`; no decision overridden | PASS |
| II. No Invented Requirements | Dashboard scope is strictly Ready-only per `/speckit-clarify`; no order-claiming/picking logic is invented to make the other three sections "real" | PASS |
| III. Small, Reviewable Changes | Work happens on feature branch `003-login-dashboard-ui`, not `main` | PASS |
| IV. Test-Driven Development | New behavior (Dashboard endpoint, Dashboard UI) requires Red → Green → Refactor; the login redesign changes presentation only, so its existing behavioral tests (role/text-based, not style-based) remain the safety net rather than being rewritten | PASS (design supports it; execution-time gate) |
| V. Safe Failure Over Silent Corruption | The Dashboard never fabricates a count for a status that doesn't exist yet (FR-005, SC-004) — explicit placeholder, not a guessed zero | PASS |
| VI. Server-Enforced Critical Business Rules | `GET /api/dashboard` requires server-side authentication like every other endpoint; no client-only gate | PASS |
| VII. Data Minimization and Credential Security | No PII beyond existing order data is exposed (FR-007); no change to PIN/session handling | PASS |
| VIII. One Responsive Product | FR-006 requires one responsive experience for both screens, not separate mobile/desktop designs | PASS |
| IX. Replaceable Integrations | N/A directly — no external integration; the new `IDashboardRepository` seam keeps `LootSingles.Application` free of a direct EF Core dependency, consistent with `IEmployeeRepository`/`IImportPersistence` | N/A / PASS |
| X. Single-Business Scope | No multi-tenant concept introduced | PASS |
| XI. Reliability During Fulfillment | The Ready query is a single server-side-aggregated read; no reliability/cost tradeoff introduced | PASS |
| XII. Maintainable and Extensible Design | `IDashboardRepository`/`DashboardService` is the extension point the other three sections will attach to when their features exist (research.md §2) — not built speculatively now, just given a seam | PASS |
| XIII. Simplicity and Proportional Abstraction | Deliberately not adopted: client-side routing (research.md §4), a light theme/toggle (research.md §5), a generic multi-status query API (research.md §2), or a formal accessibility framework (`/speckit-clarify` 2026-08-21) — none are called for by the approved spec | PASS |

No violations to justify — Complexity Tracking is empty.

**Post-Phase 1 re-check**: research.md, data-model.md, and contracts/dashboard-api.md confirm the read-only, non-persisted `OrderSummary` projection (XII/XIII), the single new `GET /api/dashboard` endpoint scoped to Ready only (II), and the CSS-custom-property token implementation with no new frontend dependency (XIII). No new violations were introduced by the detailed design; all gates remain PASS/N/A as above.

## Project Structure

### Documentation (this feature)

```text
specs/003-login-dashboard-ui/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── dashboard-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── LootSingles.Application/
│   │   └── Dashboard/
│   │       ├── OrderSummary.cs            # Read-model record (FR-004): not persisted, not a Domain entity
│   │       ├── IDashboardRepository.cs    # Persistence seam (Constitution IX/XII), Ready-only for now
│   │       └── DashboardService.cs        # Thin orchestration; the extension point for future statuses (XII)
│   ├── LootSingles.Infrastructure/
│   │   └── Persistence/
│   │       └── DashboardRepository.cs     # IDashboardRepository over LootSinglesDbContext, AsNoTracking + server-side projection
│   └── LootSingles.Api/
│       └── Controllers/
│           └── DashboardController.cs     # GET /api/dashboard (contracts/dashboard-api.md), [Authorize] (any role, FR-009)
├── tests/
│   ├── LootSingles.UnitTests/
│   │   └── Dashboard/                     # DashboardService/projection math (product count, total quantity)
│   └── LootSingles.IntegrationTests/
│       └── Dashboard/                     # GET /api/dashboard: auth required, empty state, populated state

frontend/
├── src/
│   ├── features/
│   │   ├── auth/
│   │   │   └── LoginPage.tsx              # Visual redesign only (existing file) — no behavior/prop change
│   │   └── dashboard/
│   │       ├── DashboardPage.tsx          # US2: renders Ready (live) + three static placeholder sections
│   │       └── dashboardApi.ts            # Calls contracts/dashboard-api.md's endpoint
│   ├── styles/
│   │   └── tokens.css                     # New: design-token CSS custom properties (docs/decisions/visual-design-language.md)
│   ├── assets/
│   │   └── lcs-logo.png                   # New: production brand-mark asset, light-stroke/transparent variant for dark surfaces (research.md §3)
│   ├── App.tsx                            # Updated: render DashboardPage instead of the current placeholder div
│   └── index.css                          # Updated: dark-mode-default base styles built on styles/tokens.css; scaffold-era rules removed
├── tests/
│   └── dashboard/
│       └── DashboardPage.test.tsx         # Vitest + RTL: Ready rendering, empty state, placeholder sections
└── e2e/
    └── login.spec.ts                      # Updated: assert the Dashboard (not the old placeholder) appears after login
```

**Structure Decision**: Backend follows the same layered structure as features 001/002 (Domain → Application → Infrastructure → Api), adding a `Dashboard` vertical that is read-only and has no Domain-layer entity of its own (`OrderSummary` is an Application-layer read-model over the existing `Order`/`OrderLine` Domain entities). Frontend follows the existing `features/<name>/` convention introduced for `auth`; a new `styles/tokens.css` centralizes the design-token system so component code references named tokens rather than ad hoc values, per the Product Owner's explicit request to establish this early.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
