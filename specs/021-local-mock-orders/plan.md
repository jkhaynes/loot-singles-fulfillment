# Implementation Plan: Local Mock Orders and Order Deletion

**Branch**: `021-local-mock-orders` | **Date**: 2026-10-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/021-local-mock-orders/spec.md`

## Summary

The plan covers two independent parts.

**Mock orders (US1–US3).** The synthetic TCGplayer stub moves from the test projects into Infrastructure, together with its invented JSON as embedded resources. When the app runs as Development, `Program.cs` does two things after every other configuration source:
- forces fake keys and the stub's host;
- installs the stub as the TCGplayer client's primary handler.

The real token, rate-limit, timeout and User-Agent handlers and the whole import pipeline still run, but nothing can leave the machine. Outside Development nothing is registered and no setting exists to turn it on.

**Order deletion (US4).** Deletion is soft, using `Orders.DeletedAt` and `DeletedByEmployeeId`:
- a global query filter hides deleted orders from every screen;
- the filtered unique index on the order number lets a re-import create a fresh copy;
- the stored packing slip is removed in the same transaction.

A new `Orders.FirstClaimedAt`, set by the first claim and never cleared, defines "touched". Production refuses to delete touched orders. Development (set by `Program.cs`, lowest precedence) and stage (portal env var) opt in with `OrderDeletion:AllowTouchedOrders=true`, so a missing setting gives the strict rule.

The UI adds a per-row ⋯ menu on Browse Orders and a **Delete orders…** action. Both confirmations use a `ConfirmationDialog` extracted from the Import page. A picker whose order was deleted sees "This order no longer exists".

## Technical Context

**Language/Version**: C# on .NET 10 (backend). TypeScript with React and Vite (frontend).

**Primary Dependencies**: the existing ASP.NET Core, EF Core (SQL Server) and `IHttpClientFactory`. No new NuGet or npm packages.

**Storage**: Azure SQL / SQL Server. One additive migration, `AddOrderSoftDelete` (data-model.md).

**Testing**:
- xUnit unit tests.
- Integration tests on Testcontainers SQL through `WebApplicationFactory`. The factories move to a `Testing` environment (research R2).
- Vitest and React Testing Library.
- Playwright through the E2E host.

**Target Platform**: a Linux container on Azure Container Apps, one replica per environment. Browser clients on phone and desktop.

**Project Type**: a web application (backend API and SPA served from one origin).

**Performance Goals**: a local press finishes in a few seconds, since the rate limiter isn't a factor for about 20 calls. Delete-all is one set-based update.

**Constraints**:
- No TCGplayer call from a local run, with or without keys (FR-005).
- No synthetic source outside Development (FR-004).
- No customer data in logs.
- Production pick history is never changed or hidden (FR-020).

**Scale/Scope**: one store, two to a handful of employees, tens to hundreds of orders.

There are no open unknowns. Every decision is in [research.md](research.md).

## Constitution Check

*Gate before Phase 0 and again after Phase 1. Both pass.*

| Principle | How the design meets it |
|---|---|
| I. Product Owner authority | Every rule traces to the spec's Clarifications (2026-10-10). The one environment fact the spec couldn't know, that stage and production both run as Production, is resolved in the plan without changing behavior (research R5). |
| II. No invented requirements | No restore screen, undo, checkboxes or environment banner. The typed `DELETE` is UI-only, as specified (research R10). |
| III. Small changes | One branch and two stories. Moving the stub and extracting the dialog are both required by XIII (below) and become tasks. |
| IV. TDD | Every behavior in quickstart §A has a test, written first. Moving the stub is a refactor that keeps the existing 020 tests green. |
| V. Safe failure | Strict deletion is the default. A deleted order answers like a missing one, never with stale data. Synthetic images never point at a real CDN (R4). |
| VI. Server-enforced rules | The role check, the production rule and the claim-versus-delete race are all decided by a conditional SQL update on the server (R7). |
| VII. Data minimization | The packing slip is deleted when its order is. The slip access log is kept. The synthetic data is invented only. |
| IX. Replaceable integrations | The stub is a primary `HttpMessageHandler`, so the TCGplayer adapter and the import pipeline are unchanged. |
| XI. Observability | Startup logs one Information line when the synthetic source is active. Each delete logs one Information line, and each delete-all logs a summary line. Refusals are API responses and are not logged. |
| XII / XIII. Design and simplicity | No new interfaces. A global query filter instead of editing about 20 queries. One shared dialog at its second use. The ⋯ menu stays local to `features/orders` because it has one user. |
| TCGplayer agreement | No new keys or calls. The User-Agent and limiter still run. No API data enters tests or tools. |
| EF standards | Set-based `ExecuteUpdateAsync`/`ExecuteDeleteAsync` in one transaction. `IgnoreQueryFilters()` only in the deletion code. |

### Architecture and changeability review

| Component | Responsibility | Depends on | Variation and extension cost | Failure modeling | Abstraction rationale |
|---|---|---|---|---|---|
| `SyntheticTcgplayerHandler` (Infrastructure) | Serve invented TCGplayer responses | BCL and embedded JSON only | New mock cases are new JSON files and a list entry | An unknown route fails the import attempt, as a real API fault would | Already exists. It moves so one copy serves dev, tests and E2E. |
| Development wiring (`Program.cs`) | Choose the synthetic handler in Development only | `IHostEnvironment` | None foreseen | It can't be turned on by configuration (SC-002) | None. One `if` at composition. |
| `OrderDeletionService` (Application) | Apply the deletion rules and report outcomes | `IOrderDeletionRepository`, options, `TimeProvider`, logger | A future rule change is one condition, in one place | A typed `OrderDeletionResult` (Deleted, NotFound, AlreadyDeleted, ClaimedBefore) | A repository port, as for claims, so the rules can be tested without SQL |
| `OrderDeletionRepository` (Infrastructure) | Conditional soft delete, slip removal, summary counts | `LootSinglesDbContext` | n/a | Refusals are classified from an `IgnoreQueryFilters` re-read | Follows the `OrderRepository` pattern |
| Global query filter on `Order` | Hide deleted orders from every reader | EF Core | New readers are covered automatically | An `ExecuteUpdate` on a deleted order affects no rows, so the caller reports not found | Saves editing every query, and saves future queries forgetting to filter |
| `ConfirmationDialog` (frontend) | Destructive and other confirmations | React | Used by Import (two) and Orders (two) | n/a | Consolidates at the second use (XIII) |

## Project Structure

### Documentation (this feature)

```text
specs/021-local-mock-orders/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/api.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
backend/src/LootSingles.Domain/Orders/Order.cs                         # FirstClaimedAt, DeletedAt, DeletedByEmployeeId
backend/src/LootSingles.Application/Orders/
├── OrderDeletionService.cs                                            # new: rules, results, logging
├── IOrderDeletionRepository.cs                                        # new port
├── OrderDeletionOptions.cs                                            # new: AllowTouchedOrders
└── OrderListItem.cs                                                   # + CanDelete, DeleteBlockedReason
backend/src/LootSingles.Infrastructure/
├── Tcgplayer/Synthetic/SyntheticTcgplayerHandler.cs                   # moved from tests (was TcgplayerStubHandler)
├── Tcgplayer/Synthetic/Data/*.json                                    # moved from LootSingles.Fixtures/Tcgplayer, embedded
├── Tcgplayer/Synthetic/SyntheticTcgplayerServiceCollectionExtensions.cs  # AddSyntheticTcgplayer: config override + primary handler
├── Persistence/OrderDeletionRepository.cs                             # new
├── Persistence/OrderRepository.cs                                     # claims set FirstClaimedAt
├── Persistence/Configurations/OrderConfiguration.cs                   # query filter, filtered indexes, FK
└── Persistence/Migrations/<ts>_AddOrderSoftDelete.cs                  # + backfill
backend/src/LootSingles.Api/
├── Program.cs                                                         # Development-only AddSyntheticTcgplayer; options binding; supplies OrderDeletion:AllowTouchedOrders=true when IsDevelopment() (appsettings.Development.json is git-ignored)
├── Controllers/OrdersController.cs                                    # DELETE, delete-all, deletion-summary; list fields
backend/tests/
├── LootSingles.UnitTests/Orders/OrderDeletionServiceTests.cs
├── LootSingles.UnitTests/Tcgplayer/SyntheticDataTests.cs              # no TCGplayer hosts in data
├── LootSingles.IntegrationTests/Orders/OrderDeletionTests.cs
├── LootSingles.IntegrationTests/Import/SyntheticTcgplayerSourceTests.cs
├── LootSingles.IntegrationTests/**/…Factory.cs                        # UseEnvironment("Testing")
└── LootSingles.E2EHost/                                               # use moved handler; seed delete-test orders
frontend/src/
├── components/ConfirmationDialog.tsx (+ .css)                         # extracted from ImportPage
├── features/import/ImportPage.tsx                                     # uses ConfirmationDialog
├── features/orders/OrdersPage.tsx (+ .css)                            # ⋯ menu, Delete orders…
├── features/orders/OrderRowMenu.tsx                                   # new, local
├── features/orders/ordersApi.ts                                       # summary, delete, delete-all, errors
└── features/orders/OrderDetailPage.tsx                                # OrderNotFoundError → "no longer exists"
frontend/tests/ … and frontend/e2e/order-deletion.spec.ts
```

**Structure Decision**: The existing web-application layout. The fixtures folder `backend/tests/LootSingles.Fixtures/Tcgplayer/` is replaced by the embedded data. Its README moves with the data. The unit, integration and E2E projects stop linking the JSON.

## Delivery order

1. **US1–US3**, mock orders: move the stub and data (a green refactor), switch the test factories to Testing, then add the Development wiring test-first.
2. **US4 backend**: migration, query filter, `FirstClaimedAt`, the deletion service and repository, then the endpoints.
3. **US4 frontend**: `ConfirmationDialog` extraction (a green refactor of ImportPage), the menu and confirmations, then the deleted-order handling on the detail page.
4. **E2E and quickstart**, then the stage setting (quickstart §D, a manual step).

## Complexity Tracking

No constitution violations to justify.
