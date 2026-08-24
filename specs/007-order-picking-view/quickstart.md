# Quickstart: Validate Order Picking Detail View

This validates that a picker can open any listed order and see everything needed to pick it,
safely and responsively, with no card image guessed and no state-changing action exposed.

## Prerequisites

- .NET SDK 10.0.x, Node.js
- A running SQL Server-family database with migrations applied and at least one Ready order
  imported (see `specs/001-tcgplayer-order-import/quickstart.md` or import any fixture through the
  running app's Import Orders screen)
- Frontend dependencies installed (`npm --prefix frontend install`)

## Run the app

```powershell
dotnet run --project backend/src/LootSingles.Api
npm --prefix frontend run dev
```

Log in, then confirm at least one order exists in Ready status (import one via Import Orders if
the list is empty).

## Manual validation

1. **Reach the detail view from every listing** (US2, FR-005, SC-003): from the dashboard's
   Available Orders list, select an order — confirm it opens that exact order's detail view in a
   single interaction. Go back and repeat from Browse Orders.
2. **Full detail is shown** (US1, FR-001/FR-002, SC-001): on the detail view, confirm the order's
   TCGplayer identifier is visible, and every product line shows its card identity, set, variant
   (when present), condition, and quantity — without needing to open anything else.
3. **Quantity emphasis is not color-only** (FR-003, SC-002): find a line with quantity greater than
   one. Confirm it's visually distinct from quantity-one lines. Then simulate color-blindness (e.g.,
   your browser DevTools' rendering emulation, or a grayscale screenshot) and confirm the same line
   is still clearly distinguishable — the non-color cues (bold weight and the badge/pill shape around
   the quantity value) must still read correctly without relying on the badge's color tint.
4. **No card image is ever guessed** (US3, FR-004, SC-004): confirm every product line shows the
   same neutral placeholder, with no image ever displayed in its place.
5. **Read-only** (FR-006): confirm no control on the detail view claims the order, marks it picked,
   or otherwise changes its status.
6. **Not-found state** (FR-008): navigate directly to a detail URL for an order id that doesn't
   exist (e.g., a very large number) and confirm a clear "not found" message appears, not a broken
   page or unhandled error.
7. **Mobile usability** (US4, FR-007, SC-005): resize the browser (or use device emulation) to a
   common mobile-phone width and confirm every product line's information remains fully visible and
   legible without horizontal scrolling.

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

Expected: all existing tests continue to pass unchanged; new backend tests cover the
`GET /api/orders/{orderId}` endpoint (found and not-found cases); new frontend component tests
cover the detail page's rendering (full detail, quantity emphasis, placeholder, not-found); and a
new Playwright scenario exercises the full navigate-from-list-to-detail flow at both a desktop and
a mobile-phone viewport (research.md §7).
