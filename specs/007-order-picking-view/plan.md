# Implementation Plan: Order Picking Detail View

**Branch**: `007-order-picking-view` | **Date**: 2026-08-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/007-order-picking-view/spec.md`

## Summary

Add a read-only order detail view reachable from every place an order is currently listed
(Browse Orders, the dashboard's Available Orders list). It shows the order's TCGplayer identifier
and, for each product line, the card identity, set, variant, condition, and quantity — with
quantity greater than one emphasized via color plus a non-color cue. A neutral placeholder stands
in for the card image on every line (image enrichment is out of scope). No claiming, picking, or
other state-changing action is exposed. Implemented as one new backend read endpoint extending the
existing `Orders` slice, and one new frontend route/page extending the existing `orders` feature
module — no new architectural layer, abstraction, or persisted data.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend); TypeScript / React 19 (frontend) — both unchanged

**Primary Dependencies**: ASP.NET Core + EF Core (already used by `OrdersController`/`OrderRepository`); React Router (`react-router-dom`, already used for `/orders`) — no new package on either side

**Storage**: SQL Server via the existing `Order`/`OrderLine` entities (feature 001) — read-only; no new table, column, or migration

**Testing**: xUnit (backend unit + SQL Server Testcontainers integration), Vitest + React Testing Library (frontend component tests), Playwright (critical picking-flow validation at desktop and mobile-phone viewports, per `docs/development/ai-assisted-development-workflow.md`'s Picker UI gate)

**Target Platform**: ASP.NET Core Web API + React SPA (unchanged)

**Project Type**: Web application (backend + frontend) — this feature touches both

**Performance Goals**: No new targets beyond the existing app's standard responsive-UI expectations; this is a single-order read, not a bulk/high-volume path

**Constraints**: Read-only (FR-006) — no endpoint or UI control that changes order state; no card-image fetching/enrichment (FR-004); quantity emphasis must combine color with a non-color cue (FR-003, per clarification); must remain usable at desktop and mobile-phone widths (FR-007)

**Scale/Scope**: One new backend endpoint (`GET /api/orders/{orderId}`) extending the existing `OrdersController`/`OrdersService`/`OrderRepository` slice; one new frontend route/page extending the existing `orders` feature module; navigation wiring in the two existing list views (`OrdersPage.tsx`, `DashboardPage.tsx`)

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design — see below.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Every requirement traces to spec.md, which traces to the Developer's explicit scope decisions and the PRD's existing picking-screen and "no image is better than the wrong image" concepts. No invented functionality. | PASS |
| III Reviewability | Single feature, one new endpoint, one new page, two small navigation edits. Small, reviewable diff. | PASS |
| IV TDD | New backend endpoint and frontend component behavior get Red→Green tests per constitution Principle IV (see tasks.md, once generated). | PASS |
| V Safe failure | FR-008 requires a clear "not found" state instead of an unhandled failure for an invalid order id; FR-004/US3 require a neutral placeholder rather than any guessed image — directly implements "no image is better than the wrong image." | PASS |
| VI Concurrency-safe claiming | Not applicable — this feature is read-only and adds no claim/state-transition logic (explicitly deferred to a follow-up feature per spec.md Assumptions). | PASS (N/A) |
| VII PII minimization | `Order` already contains no customer shipping/PII fields (see `Order.cs`); this feature adds no new data, so nothing new is exposed to pickers. | PASS |
| VIII One responsive PWA | FR-007/US4 require the same detail view to work at both desktop and mobile-phone widths — no separate mobile codebase. | PASS |
| IX Replaceable integrations | The card-image placeholder is static UI, not a catalog-provider integration — no adapter is introduced or bypassed. | PASS (N/A) |
| X Single-business scope | No multi-tenant concept introduced. | PASS |
| XI Observability | Evaluated: this is a routine, no-side-effect read with no business-meaningful failure mode of its own (an unexpected error already surfaces as an unhandled 500), matching the exact precedent already established for order-listing/dashboard reads in feature 006's spec. No new production logging is added. | PASS |
| XII Maintainable design | Extends the existing `OrdersController` → `OrdersService` → `IOrderRepository` slice with one more method, mirroring the existing `GetAllAsync` pattern exactly. No Domain/UI leakage. | PASS |
| XIII Simplicity | No new abstraction, service, or repository is introduced — one additional method on an existing interface/service/controller, and one additional page in the existing `orders` frontend feature module. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `OrdersController` (Api) | Gains one `GET /api/orders/{orderId}` action, mirroring the existing `GET /api/orders` action's shape and `[Authorize]` policy. Depends only on `OrdersService`. | Returns `404` for a non-existent order (FR-008) instead of throwing; no new authorization concept. Testable via the existing `WebApplicationFactory` pattern used elsewhere in this project. |
| `OrdersService` (Application) | Gains one `GetByIdAsync(int orderId)` method delegating to `IOrderRepository`, mirroring `GetAllAsync`. | No business logic beyond delegation — this is a pure read. |
| `IOrderRepository` / `OrderRepository` (Application/Infrastructure) | Gains one `GetByIdAsync` method querying `Order` with its `OrderLines` included, projected into a new read-only `OrderDetail` record (mirrors the existing `OrderListItem` projection pattern). | `AsNoTracking()` read, no tracked-entity risk; returns `null` for a missing id, translated to `404` at the controller. |
| `orders` frontend feature (React) | Gains one route (`/orders/:orderId`), one page component, and one typed API client function (`getOrderDetail`), mirroring the existing `getOrders`/`OrdersPage` pattern exactly. `OrdersPage` and `DashboardPage` rows become links into it. | No new state-management pattern; reuses the existing loading/error/empty local-state convention already used in both list views. |

**Post-design gate**: Boundaries remain inward-facing (Application layer owns the read, Infrastructure implements it, Api exposes it); no new abstraction was introduced without a concrete purpose; the frontend reuses its one existing feature-module pattern rather than introducing a new one. No deviation required.

## Project Structure

### Documentation (this feature)

```text
specs/007-order-picking-view/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/
│   └── order-detail-api.md   # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Api/Controllers/OrdersController.cs        # add GET /api/orders/{orderId}
├── src/LootSingles.Application/Orders/
│   ├── OrdersService.cs                                        # add GetByIdAsync
│   ├── IOrderRepository.cs                                     # add GetByIdAsync
│   └── OrderDetail.cs                                           # new — read-only detail + line shapes
├── src/LootSingles.Infrastructure/Persistence/OrderRepository.cs  # implement GetByIdAsync
└── tests/LootSingles.IntegrationTests/Orders/                  # new order-detail endpoint tests

frontend/
├── src/features/orders/
│   ├── ordersApi.ts               # add getOrderDetail + OrderDetail types
│   ├── OrdersPage.tsx              # rows become links to /orders/:orderId
│   ├── OrderDetailPage.tsx         # new
│   └── OrderDetailPage.css         # new
├── src/features/dashboard/DashboardPage.tsx   # rows become links to /orders/:orderId
├── src/App.tsx                      # add /orders/:orderId route
└── tests/ (component tests alongside the new page; Playwright scenario for the picking flow)
```

**Structure Decision**: No new project, layer, or abstraction. Backend changes stay inside the
existing `Orders` vertical slice (Api → Application → Infrastructure). Frontend changes stay inside
the existing `orders` feature module, plus the two small navigation edits in `orders`/`dashboard`
where orders are already listed.

## Complexity Tracking

No constitution violations require justification.
