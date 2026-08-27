# Implementation Plan: Exclusive Order Claiming

**Branch**: `013-order-claiming` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-order-claiming/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add a concurrency-safe order-claiming mechanism on top of the existing read-only order list/detail
views (feature 007/009). Two entry points — "Pick Next Order" (system-selected, FIFO-oldest
unclaimed) and "Choose Order" (picker-selected from the existing browse list) — both claim an order
exclusively for the acting employee. A claimed order shows who is picking it to every other employee.
Claims end only via an explicit "Release" action by the claiming employee, or a Manager/Admin
"Force-Release" action; each employee may hold at most one active claim at a time. The core technical
challenge (constitution Principle VI, an explicit MUST) is guaranteeing that two employees racing to
claim the same order can never both succeed — solved with a conditional, single-round-trip
`ExecuteUpdateAsync` compare-and-swap on `Order.ClaimedByEmployeeId`, backed by a database-level
filtered unique index as a second, independent safety net for the one-active-claim-per-employee rule.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (existing backend), TypeScript / React 18 (existing frontend,
`.tsx` components under `frontend/src/features/orders/`) — unchanged from features 007/009. (Note:
an earlier draft of this section incorrectly stated Vue 3 — corrected 2026-08-26 during Phase 5
implementation after checking the actual frontend source.)

**Primary Dependencies**: ASP.NET Core Web API, EF Core 8 (SQL Server provider), cookie-based
`ClaimsPrincipal` auth (existing `AuthController`/`[Authorize(Roles=...)]` pattern) — all existing,
no new dependencies introduced by this feature.

**Storage**: SQL Server via EF Core (`LootSinglesDbContext`), same database as `Order`/`Employee`.
This feature adds two nullable columns to the existing `Orders` table (`ClaimedByEmployeeId`,
`ClaimedAt`) plus a new `OrderStatus.InProgress` enum value; no new tables.

**Testing**: xUnit (backend unit/integration, including real relational concurrency tests against
SQL Server via Testcontainers — `SqlServerContainerFixture`/`SqlServerTestCollection`, per the
constitution's TDD principle), Playwright (frontend E2E) — both already established in this repo
(see `backend/tests/LootSingles.IntegrationTests/Persistence/SqlServerConcurrencyTests.cs`'s
real-concurrency test pattern, and `frontend/e2e/order-detail.spec.ts`).

**Target Platform**: Same as existing app — server-hosted ASP.NET Core API + browser-based Vue SPA,
used on shop-floor desktop/tablet browsers.

**Project Type**: Web application (existing `backend/` + `frontend/` structure).

**Performance Goals**: Claim/release actions must resolve in a single request round-trip
(no polling loop for the initial claim attempt); no specific throughput target beyond the existing
app's scale (a handful of concurrent pickers in one shop).

**Constraints**: Constitution Principle VI (MUST): two employees racing to claim the same order via
"Pick Next Order" or "Choose Order" MUST NOT both succeed — enforced server-side, not by client-side
locking or optimistic UI alone. Constitution EF Core Engineering Standards: prefer `ExecuteUpdateAsync`
set-based conditional updates over load-track-modify for the claim/release/force-release writes.

**Scale/Scope**: Single-shop deployment; low concurrent-picker count (order of single digits), so no
distributed-lock or queueing infrastructure is warranted — a single conditional SQL update against one
row is sufficient.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Assessment |
|---|---|---|
| I. TCGplayer Data Authority | No | This feature adds claim/status state; it does not touch imported order-line data. |
| II. Catalog Enrichment is Supplemental | No | Not related to card-catalog enrichment. |
| III. Explicit Handling of Picking Problems | No | Picking-issue reporting is out of scope for this feature (PRD §12-22, future feature). |
| IV. Test-First Development (TDD, MUST) | Yes | Claim/release/force-release logic — especially the concurrency-safe compare-and-swap — MUST be built Red→Green→Refactor, including a genuine concurrent-request test (mirroring feature 009's `ConcurrencyTrackingHandler` pattern) proving two simultaneous claims of the same order never both succeed. |
| V. No Image Is Better Than the Wrong Image | No | No card-image matching in this feature. |
| VI. Server-Enforced Critical Business Rules (MUST) | **Yes — central to this feature** | Exclusive claiming MUST be enforced server-side via an atomic conditional update, not client-side coordination. This is this feature's primary reason for existing. |
| VII. Employee PIN Security | No | No PIN handling changes; reuses existing `ClaimsPrincipal` auth as-is. |
| VIII. PDF Import Fails Safely | No | Not related to import. |
| IX. Replaceable Card-Catalog Integrations | No | Not related to card-catalog providers. |
| X. Minimized, Purpose-Limited PII | Yes | Claim state exposes only the claiming employee's display name to other employees (already-visible internal staff data), no new customer PII. |
| XI. Observability | Yes | Claim, release, force-release, and lost-claim-race events are worth structured `ILogger<T>` logging (who claimed what, when, and race outcomes) per the Definition of Done. |
| XII. Maintainable, Evolvable Design | Yes | Reuses the existing outcome-enum service pattern (`EmployeeManagementService`) and existing `[Authorize(Roles=...)]`/`ActorEmployeeId()` conventions rather than inventing a new authorization or result-shape convention. |
| XIII. Simplicity & Proportional Abstraction | Yes | No new tables, no distributed locking, no message queue — a single conditional `UPDATE` plus one filtered unique index, proportional to a single-shop, low-concurrency scale. |

No violations requiring justification. Gate passes.

**Post-design re-check** (after Phase 1 data-model.md/contracts/quickstart.md): the design adds two
nullable columns and one filtered unique index to the existing `Orders` table, and reuses the
existing outcome-enum service pattern and `[Authorize(Roles=...)]`/`ActorEmployeeId()` conventions
verbatim — no new principle exposure surfaced by the concrete design. Gate still passes; no updates
to the table above are needed.

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
│   │   └── Orders/
│   │       ├── Order.cs                  # + ClaimedByEmployeeId, ClaimedAt
│   │       └── OrderStatus.cs            # + InProgress = 1
│   ├── LootSingles.Application/
│   │   └── Orders/
│   │       ├── IOrderRepository.cs       # + claim/release/force-release methods
│   │       └── OrderClaimService.cs      # new: outcome-enum service (mirrors EmployeeManagementService)
│   ├── LootSingles.Infrastructure/
│   │   └── Persistence/
│   │       ├── OrderRepository.cs        # + conditional ExecuteUpdateAsync methods
│   │       └── Configurations/
│   │           └── OrderConfiguration.cs # + filtered unique index on ClaimedByEmployeeId
│   └── LootSingles.Api/
│       └── Controllers/
│           └── OrdersController.cs       # + POST {id}/claim, {id}/release, {id}/force-release, POST orders/pick-next
└── tests/
    ├── LootSingles.UnitTests/Orders/OrderClaimServiceTests.cs
    └── LootSingles.IntegrationTests/
        ├── Orders/OrdersControllerClaimingTests.cs
        └── Persistence/OrderClaimConcurrencyTests.cs  # genuine concurrent-claim race test, real SQL Server via Testcontainers

frontend/
├── src/
│   └── features/orders/
│       ├── OrdersPage.tsx       # + "Pick Next Order" entry point, in-progress badge/claimant name
│       ├── OrderDetailPage.tsx  # + Release / Force-Release actions, claimant display
│       └── ordersApi.ts         # + claim/release/force-release/pick-next calls
└── e2e/
    └── order-claiming.spec.ts     # new
```

**Structure Decision**: Existing web-application structure (`backend/` ASP.NET Core API +
`frontend/` Vue SPA) is reused as-is; this feature only adds files within it — no new
projects or top-level directories.

## Complexity Tracking

*No Constitution Check violations — this section is not applicable.*
