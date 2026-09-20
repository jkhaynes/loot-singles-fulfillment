# Implementation Plan: Pick Completion

**Branch**: `015-pick-completion` | **Date**: 2026-08-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/015-pick-completion/spec.md`

## Summary

Pickers currently have no way to record the outcome of picking an individual product line, and an
order's status stops updating once claiming happens (feature 013): there is no path from
`InProgress` to `Picked` or to a distinct `NeedsAttention` state. This feature adds a per-line
"confirm picked" / "report issue" action to the existing order-detail screen, makes `Order.Status`
a value that is always freshly recomputed from its lines' current pick outcomes (never an
independently-tracked flag — per the clarified FR-006), and closes the Dashboard's three
placeholder tiles (In Progress, Needs Attention, Picked) with real, live data. Issue resolution
reuses the existing exclusive claim/release mechanism from feature 013 exactly as-is (release,
later re-claim, revise) rather than introducing any new workflow — the PRD's open "issue
resolution" discovery question stays open; this feature does not answer it beyond what the
Product Owner already confirmed during `/speckit-clarify`.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend), TypeScript 5 / React 18 (frontend) — unchanged
from features 001–014.

**Primary Dependencies**: ASP.NET Core Web API, EF Core 8, SQL Server (Azure SQL in
production, LocalDB for local dev per project convention), xUnit + a real SQL Server test
container (integration tests), Vitest + React Testing Library (frontend unit), Playwright
(critical E2E flows) — all existing, unchanged.

**Storage**: SQL Server via EF Core. New tables: `PickingIssues`. New columns on existing
`OrderLines`: `PickOutcome`, `PickOutcomeRecordedByEmployeeId`, `PickOutcomeRecordedAt`,
`CurrentPickingIssueId`. No new columns on `Orders` — `Status` is an existing column that gains
two new enum values.

**Testing**: xUnit unit + integration tests for `PickingService`/`PickingRepository` and the
modified `OrderRepository` claim/release methods (including a concurrency test mirroring feature
013's exclusive-claim race test, applied to concurrent line-outcome recording); Vitest/RTL
component tests for the new per-line controls on `OrderDetailPage`; Playwright E2E covering the
full happy-path and needs-attention-then-resolve flows across a simulated mobile viewport and
desktop viewport (SC-008).

**Target Platform**: Responsive PWA (existing), single codebase for desktop and mobile browsers —
no new platform.

**Project Type**: Web application — existing `backend/` (ASP.NET Core) + `frontend/` (React/Vite)
structure, extended, not restructured.

**Performance Goals**: No new performance targets beyond the project's existing implicit
expectation of responsive interactive use by a small number of concurrent pickers (single-business
scale, per Principle X) — not formally load-tested.

**Constraints**: Every write that changes `Order.Status` MUST go through the transactional,
claim-gated pattern established in research.md §2 — no code path may set `Status` directly to
`Picked` or `NeedsAttention` without deriving it from current line outcomes (FR-006). Recording a
line outcome is restricted to the current claim holder, server-side (FR-010, Constitution
Principle VI).

**Scale/Scope**: Single-business scope (Constitution Principle X) — no multi-tenant concerns.
Touches 2 existing backend projects (`Application`, `Infrastructure`, `Api`), 1 new migration, and
1 existing frontend feature folder (`orders`) plus the `dashboard` feature folder.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design below.*

| Principle | Check | Status |
|---|---|---|
| I. Product Owner Authority | Spec was built directly from confirmed PO answers during `/speckit-clarify` (order-status derivation) and `/speckit-specify` (resolution-via-existing-claim-mechanism); no artifact-level conflict exists. | PASS |
| II. No Invented Requirements | Issue taxonomy sourced from PRD §19.3, not invented; PRD §20's "resolved issues" design question explicitly left unanswered — this feature answers only what the PO explicitly confirmed (reuse claim/release), not the full discovery question. | PASS |
| III. Small, Reviewable Changes | Scoped to one branch/feature; the required correction to feature 013's `OrderRepository` claim/release methods (research.md §5) is necessary to implement this feature's approved FR-005/006 safely, not unrelated cleanup — captured explicitly as its own task, per Principle III's own carve-out. | PASS |
| IV. TDD (NON-NEGOTIABLE) | tasks.md (Phase 2 of Spec Kit) will sequence a failing test before each implementation task, per the constitution's task-generation requirement. Addressed in Task Generation section below, enforced at `/speckit-tasks` and `/speckit-implement`. | PASS (procedural gate — enforced downstream) |
| V. Safe Failure Over Silent Corruption | FR-009 (order must never be Picked while any line has an unresolved issue) is enforced structurally by deriving `Status` from actual line state every time, not by a separate flag that could drift and silently under-report a problem. | PASS |
| VI. Server-Enforced Critical Business Rules | FR-010 (only the claim holder may record outcomes) is enforced in `PickingRepository.RecordOutcomeAsync`'s conditional `ExecuteUpdateAsync` (research.md §2), the same server-side mechanism, not a frontend-only check. | PASS |
| VII. Data Minimization / Credential Security | No new PII surfaced to pickers; `PickingIssue` captures employee id (already-authorized internal identifier, not customer PII), quantity, type, optional note — no customer data added. | PASS |
| VIII. One Responsive Product | research.md §6 — new controls extend the existing responsive `OrderDetailPage`, not a new screen; SC-008 requires identical controls mobile/desktop. | PASS |
| IX. Replaceable Integrations | No changes to TCGplayer import or catalog-enrichment adapters; this feature is entirely downstream of already-imported, already-enriched order data. | N/A |
| X. Single-Business Scope | No multi-tenant concerns introduced. | PASS |
| XI. Reliability During Fulfillment | `PickingService` follows the established `ILogger<T>` convention (mirroring `OrderClaimService`), logging outcome-level events (line picked, issue reported, order transitioned to Picked/NeedsAttention) at Information level — proportional, no PII, console/stdout only, no new logging abstraction. Evaluated explicitly in tasks.md polish phase. | PASS |
| XII. Maintainable and Extensible Design | `PickingRepository`/`PickingService` kept separate from `OrderRepository`/`OrdersService`/`OrderClaimService`, mirroring the existing 013 split — each component keeps one focused responsibility (claim lifecycle vs. line-outcome recording), consistent with the existing service boundary. | PASS |
| XIII. Simplicity and Proportional Abstraction | The shared `OrderStatusComputation.FromCurrentLines` helper (research.md §5) is introduced only because a second concrete use case (the new `PickingRepository`) now needs the exact same expression `OrderRepository` needs — this is exactly the "second concrete use case" trigger the principle names, not speculative abstraction. `OrderLine` gains fields directly rather than a new 1:1 entity (research.md §3), avoiding unneeded abstraction; `PickingIssue` is a genuinely separate entity only because it has a real one-to-many history requirement no simpler shape satisfies (research.md §4). | PASS |
| EF Core Standards | `RecordOutcomeAsync` uses `ExecuteUpdateAsync` for both the conditional line update and the status recompute (no full entity load/mutate/save round trip); reads use `AsNoTracking()`; `CancellationToken` propagated throughout, matching existing `OrderRepository` conventions. | PASS |

No violations requiring justification — Complexity Tracking table below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/015-pick-completion/
├── plan.md              # This file
├── research.md           # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── picking-api.md    # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not yet created)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── LootSingles.Domain/                          # (or wherever OrderStatus/PickOutcome enums live today)
│   │   └── (extend OrderStatus; add PickOutcome, PickingIssueType)
│   ├── LootSingles.Application/
│   │   ├── Orders/
│   │   │   ├── OrderDetail.cs                       # add Id, PickOutcome, CurrentIssue to OrderLineDetail
│   │   │   ├── OrdersService.cs                     # no material change (reads flow through updated projection)
│   │   │   └── OrderClaimService.cs                 # unchanged (claim/release orchestration itself untouched)
│   │   ├── Picking/                                  # NEW
│   │   │   ├── IPickingRepository.cs
│   │   │   ├── PickingService.cs
│   │   │   ├── PickingResult.cs                      # PickingOutcome enum + PickingResult record
│   │   │   └── PickOutcomeChange.cs                   # input DTO: Picked | IssueReport(type, qty, qty, note)
│   │   └── Dashboard/
│   │       ├── IDashboardRepository.cs                # add InProgress/NeedsAttention/Picked methods
│   │       └── DashboardService.cs                    # add matching pass-through methods
│   ├── LootSingles.Infrastructure/
│   │   └── Persistence/
│   │       ├── OrderRepository.cs                     # ClaimSpecificAsync/ReleaseAsync/ForceReleaseAsync: use shared status computation
│   │       ├── PickingRepository.cs                   # NEW — implements IPickingRepository, research.md §2 transaction
│   │       ├── OrderStatusComputation.cs               # NEW — shared Expression<Func<Order, OrderStatus>> helper
│   │       ├── DashboardRepository.cs                  # add InProgress/NeedsAttention/Picked query methods
│   │       ├── Configurations/
│   │       │   ├── OrderLineConfiguration.cs           # add new column mappings, CurrentPickingIssueId FK
│   │       │   └── PickingIssueConfiguration.cs        # NEW
│   │       └── Migrations/
│   │           └── {timestamp}_AddPickCompletion.cs    # NEW
│   └── LootSingles.Api/
│       └── Controllers/
│           ├── OrdersController.cs                     # add pick/report-issue actions + response DTO fields
│           └── DashboardController.cs                  # add three new response sections
└── tests/
    ├── LootSingles.UnitTests/
    │   └── Application/Picking/PickingServiceTests.cs  # NEW
    └── LootSingles.IntegrationTests/
        ├── Orders/OrdersControllerTests.cs               # extend: pick/report-issue endpoints, status transitions
        ├── Orders/OrderRepositoryTests.cs                # extend: release/re-claim preserves NeedsAttention
        └── Dashboard/DashboardControllerTests.cs         # extend: three new sections

frontend/
├── src/
│   ├── features/
│   │   ├── orders/
│   │   │   ├── OrderDetailPage.tsx                      # add per-line Picked / Report Issue controls
│   │   │   ├── OrderDetailPage.css                       # add line-action/issue-form styles
│   │   │   └── ordersApi.ts                               # add recordPicked/reportIssue functions + response types
│   │   └── dashboard/
│   │       ├── DashboardPage.tsx                          # wire the three stub tiles to real data
│   │       └── dashboardApi.ts                             # extend response types
└── tests/
    └── orders/
        └── OrderDetailPage.test.tsx                        # extend: pick/report-issue interactions
frontend/e2e/
└── pick-completion.spec.ts                                  # NEW — Playwright, desktop + mobile viewport
```

**Structure Decision**: Existing Option 2 (web application: `backend/` + `frontend/`) structure,
unchanged. No new top-level projects. New backend work lives in a `Picking` subfolder within the
existing `Application`/`Infrastructure` projects, mirroring how feature 013's claim/release
subsystem lives in the existing `Orders` subfolder rather than a separate project — this feature's
`Picking` types are closely related to but conceptually distinct from claim/release management, so
they get their own subfolder (not a new project, and not merged into `Orders`) while staying in the
same assemblies.

## Complexity Tracking

*No Constitution Check violations — table intentionally empty.*
