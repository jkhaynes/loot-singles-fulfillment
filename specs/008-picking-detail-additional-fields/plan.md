# Implementation Plan: Order Picking Detail — Additional Packing-Slip Fields

**Branch**: `008-picking-detail-additional-fields` | **Date**: 2026-08-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-picking-detail-additional-fields/spec.md`

## Summary

Extend feature 007's existing read-only order picking detail view to display four
already-imported fields it currently omits: per product line, the product line/game (e.g.
"Pokemon") and collector number (e.g. "#067/086") — both required on every `OrderLine` — and
rarity (e.g. "Double Rare") when present; at the order level, the order's `Status`. All four
fields already exist on the persisted `Order`/`OrderLine` entities from feature 001 and are
already returned in the existing `OrderListItem` projection (`Status`) or sit unused on
`OrderLine` (`ProductLine`, `CollectorNumber`, `Rarity`). This is a pure additive extension of
the existing `GET /api/orders/{orderId}` response and the existing `OrderDetailPage` — no new
endpoint, no new route, no new component, no schema change, no catalog/image work, no
state-changing behavior.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend); TypeScript / React 19 (frontend) — both unchanged

**Primary Dependencies**: ASP.NET Core + EF Core (already used by `OrdersController`/`OrderRepository`); no new package on either side

**Storage**: SQL Server via the existing `Order`/`OrderLine` entities (feature 001) — read-only; every field this feature displays is an existing column; no new table, column, or migration

**Testing**: xUnit (backend unit + SQL Server Testcontainers integration), Vitest + React Testing Library (frontend component tests), Playwright (extends the existing critical-flow scenario from feature 007 per `docs/development/ai-assisted-development-workflow.md`'s Picker UI gate)

**Target Platform**: ASP.NET Core Web API + React SPA (unchanged)

**Project Type**: Web application (backend + frontend) — this feature touches both

**Performance Goals**: No new targets; still a single-order read, now returning four more scalar fields already loaded as part of the same `Order`/`OrderLine` query

**Constraints**: Read-only (FR-005) — no endpoint or UI control that changes order state; no new data collection or catalog/image matching (Assumptions); must remain usable at desktop and mobile-phone widths with the added fields (FR-006)

**Scale/Scope**: Additive changes only to the existing `GET /api/orders/{orderId}` response shape (`OrderDetail`/`OrderLineDetail` records, `OrderDetailResponse`/`OrderLineDetailResponse` records) and the existing `OrderDetailPage.tsx`/`.css` — no new backend endpoint, no new frontend route or page

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design — see below.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Every requirement traces to spec.md, which traces directly to the Developer's explicit request (product line, rarity, collector number, status) with a worked example. No invented functionality — all four fields already exist on the persisted entities. | PASS |
| III Reviewability | Single feature, no new endpoint/route/component — four fields added to two existing records, two existing response DTOs, one existing page. Small, reviewable diff. | PASS |
| IV TDD | Extended backend projection/response and frontend rendering get Red→Green tests per constitution Principle IV (see tasks.md, once generated). | PASS |
| V Safe failure | N/A — no external/malformed input handling introduced; all four fields are already-validated persisted data (`ProductLine`/`CollectorNumber` required, `Rarity` optional, `Status` a closed enum). | PASS (N/A) |
| VI Concurrency-safe claiming | Not applicable — this feature is read-only and adds no claim/state-transition logic. | PASS (N/A) |
| VII PII minimization | `ProductLine`, `CollectorNumber`, `Rarity`, and `Status` are all product/order metadata, not customer PII; `Order`/`OrderLine` already contain no PII fields. | PASS |
| VIII One responsive PWA | FR-006 requires the enlarged detail view to remain fully usable at both desktop and mobile-phone widths — addressed in the Architecture and Changeability Review below. | PASS |
| IX Replaceable integrations | No catalog-provider integration introduced — these fields already come from the TCGplayer import adapter (feature 001), not a new source. | PASS (N/A) |
| X Single-business scope | No multi-tenant concept introduced. | PASS |
| XI Observability | Evaluated: this remains the same class of routine, no-side-effect read as feature 007's endpoint — no new business-meaningful failure mode is introduced by returning four more already-loaded fields. No new production logging is added. | PASS |
| XII Maintainable design | Extends the existing `OrderDetail`/`OrderLineDetail` records, the existing `OrderRepository.GetByIdAsync` projection, and the existing `OrderDetailResponse`/`OrderLineDetailResponse` records — no new class, layer, or abstraction. | PASS |
| XIII Simplicity | No new abstraction, endpoint, route, or component — four additional properties on records/DTOs/types already created by feature 007, and four additional rendered fields on the page it already built. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `OrderDetail` / `OrderLineDetail` (Application) | `OrderDetail` gains `Status` (`OrderStatus`); `OrderLineDetail` gains `ProductLine` (string), `CollectorNumber` (string), `Rarity` (string?). Same read-only record shapes feature 007 introduced. | No new failure mode — these are additional scalar projections of already-loaded entity properties, not new queries or new optionality handling beyond the existing `Variant`/`Rarity` nullable pattern. |
| `OrderRepository.GetByIdAsync` (Infrastructure) | Projection extended to select the four additional properties already present on the tracked `Order`/`OrderLine` entities. No new `Include`, no new query shape, no new round trip. | `AsNoTracking()` read is unchanged; still returns `null` for a missing order (FR-005/feature 007 FR-008 unaffected). |
| `OrdersController` (Api) | `OrderDetailResponse`/`OrderLineDetailResponse` gain the same four properties, mapped 1:1 from the extended `OrderDetail`/`OrderLineDetail`. No new action, no new route, no new authorization concept. | Testable via the same `WebApplicationFactory` pattern already used by feature 007's `OrdersControllerTests`. |
| `orders` frontend feature (React) | `ordersApi.ts`'s `OrderDetail`/`OrderLineDetail` types gain the four fields; `OrderDetailPage.tsx` renders them in the same identity/attributes area feature 007 already built, applying the same "omit when absent" treatment already established for `Variant` to the new optional `rarity` field. `OrderDetailPage.css` is adjusted so the added attribute rows and the order-level status remain legible and unclipped at mobile width (FR-006), extending the same responsive grid feature 007 already validated with Playwright rather than introducing a new layout approach. | No new state-management pattern, no new loading/error state — the existing fetch/render flow is unchanged; only the rendered field set grows. |

**Post-design gate**: No new abstraction, endpoint, route, or component was introduced; every change is an additive extension of a boundary feature 007 already established. No deviation required.

## Project Structure

### Documentation (this feature)

```text
specs/008-picking-detail-additional-fields/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── contracts/
│   └── order-detail-api.md   # Phase 1 output (documents the additive response change)
├── quickstart.md        # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Application/Orders/OrderDetail.cs             # add Status/ProductLine/CollectorNumber/Rarity
├── src/LootSingles.Infrastructure/Persistence/OrderRepository.cs # extend GetByIdAsync projection
├── src/LootSingles.Api/Controllers/OrdersController.cs           # extend response records/mapping
└── tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs  # extend GetByIdReturnsFullOrderDetail

frontend/
├── src/features/orders/
│   ├── ordersApi.ts               # extend OrderDetail/OrderLineDetail types
│   ├── OrderDetailPage.tsx         # render the four added fields
│   └── OrderDetailPage.css         # layout for added attribute rows, mobile-safe
└── tests/orders/OrderDetailPage.test.tsx  # extend existing render/rarity-omission tests
frontend/e2e/order-detail.spec.ts  # extend to assert the added fields render
```

**Structure Decision**: No new project, layer, endpoint, route, or component. Every change is an
additive extension of the exact backend slice and frontend page feature 007 already built.

## Complexity Tracking

No constitution violations require justification.
