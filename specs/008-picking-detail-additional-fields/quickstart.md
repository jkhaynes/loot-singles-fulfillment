# Quickstart: Validate Order Picking Detail — Additional Packing-Slip Fields

This validates that the order picking detail view (feature 007) now also shows the product
line/game, collector number, and rarity for every product line, and the order's status — all
already-imported data that was previously omitted — while remaining read-only and usable at both
desktop and mobile-phone widths.

## Prerequisites

- Same as feature 007's quickstart: .NET SDK 10.0.x, Node.js, a running SQL Server-family
  database with migrations applied and at least one Ready order imported (see
  `specs/001-tcgplayer-order-import/quickstart.md`, or import `valid-multi-order-batch.pdf` via
  Import Orders — it includes a line for "Genesect ex — SV: Black Bolt — #067/086")
- Frontend dependencies installed (`npm --prefix frontend install`)

## Run the app

```powershell
dotnet run --project backend/src/LootSingles.Api
npm --prefix frontend run dev
```

Log in, then open an order's detail view (from the dashboard's Available Orders list or Browse
Orders).

## Manual validation

1. **Product line and collector number are always shown** (US1, FR-001/FR-002, SC-001): for every
   product line, confirm the game (e.g., "Pokemon") and collector number (e.g., "#067/086") are
   visible alongside the existing product name, set, variant, condition, and quantity.
2. **Rarity shown when present, omitted cleanly when absent** (US1, FR-003, SC-003): find a line
   with a recorded rarity (e.g., "Double Rare") and confirm it's visible; find or construct a line
   with no recorded rarity and confirm the line renders cleanly with no blank or broken rarity
   field, the same way an absent variant already renders.
3. **Order status is visible** (US2, FR-004, SC-002): confirm the order's status appears next to
   its TCGplayer identifier, without navigating back to a list view.
4. **Still read-only** (FR-005): confirm no control on the detail view claims the order, marks it
   picked, or otherwise changes its status or data.
5. **Still usable at mobile width** (FR-006, SC-004): resize the browser (or use device emulation)
   to a common mobile-phone width and confirm every product line's information — including the
   three new fields — remains fully visible and legible without horizontal scrolling.

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
npm --prefix frontend run lint
npm --prefix frontend run test
npm --prefix frontend run build
npm --prefix frontend run format:check
npm --prefix frontend run test:e2e
```

Expected: all existing tests continue to pass; the existing `GET /api/orders/{orderId}` backend
tests are extended to assert `status`, `productLine`, `collectorNumber`, and `rarity`; the existing
`OrderDetailPage` component tests are extended to assert the same fields render (and that an
absent rarity omits cleanly); the existing Playwright order-detail scenario is extended to assert
the new fields are visible, and the existing mobile-viewport no-horizontal-scroll check continues
to pass with the larger field set.
