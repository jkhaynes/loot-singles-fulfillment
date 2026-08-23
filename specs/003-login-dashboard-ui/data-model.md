# Data Model: Login Page and Dashboard UI Design

This feature introduces no new persisted entities and no database migration. It reads existing data from features 001 (`Order`, `OrderLine`) through one new Application-layer read-model.

## OrderSummary (Application-layer read-model, not persisted)

`LootSingles.Application.Dashboard.OrderSummary` — derived, in-memory only; never stored, never a `LootSingles.Domain` type (research.md §2).

| Field | Type | Derivation |
|---|---|---|
| `OrderId` | `int` | `Order.Id` |
| `TcgplayerOrderId` | `string` | `Order.TcgplayerOrderId` |
| `ProductCount` | `int` | Count of `Order.OrderLines` (distinct product/card line items) |
| `TotalQuantity` | `int` | Sum of `OrderLine.Quantity` across `Order.OrderLines` |

Both `ProductCount` and `TotalQuantity` are computed server-side within the EF Core query projection (`Select`), not materialized-then-computed in memory, per the constitution's EF Core Engineering Standards (server-side execution, no over-fetching).

## Source data (unchanged, from feature 001)

- **`Order`** (`LootSingles.Domain.Orders.Order`): `Id`, `TcgplayerOrderId`, `Status` (`OrderStatus`, currently only `Ready = 0` exists), `ImportedAt`, `OrderLines`. No fields added or changed by this feature.
- **`OrderLine`** (`LootSingles.Domain.Orders.OrderLine`): `Id`, `OrderId`, `Quantity`, plus product/card descriptive fields not needed for the Dashboard's summary row. No fields added or changed by this feature.

## Query shape

`IDashboardRepository.GetReadyOrderSummariesAsync(CancellationToken)` →

```text
Orders
  .AsNoTracking()
  .Where(o => o.Status == OrderStatus.Ready)
  .Select(o => new { o.ImportedAt, Summary = new OrderSummary(o.Id, o.TcgplayerOrderId, o.OrderLines.Count, o.OrderLines.Sum(l => l.Quantity)) })
  .ToListAsync()
  -- then ordered by ImportedAt and mapped to OrderSummary after materialization
```

No pagination (matches the existing unpaginated `GET /api/employees` precedent at this project's scale, plan.md Scale/Scope). No ordering requirement is specified by the spec; a stable, deterministic order (oldest-ready-first, by `ImportedAt`) is a reasonable implementation default, not a functional requirement. At the time this feature was implemented, the ordering was applied after materialization rather than via a SQL `ORDER BY`, because SQLite (used in integration tests) could not translate ordering on `DateTimeOffset` — the same constraint documented for `EmployeeRepository.GetAuditEventsAsync` in feature 002. 005-sql-server-migration removed SQLite from integration tests and restored a SQL-translated `OrderBy`/`ThenBy` in `DashboardRepository.GetReadyOrderSummariesAsync` before materialization, since SQL Server has no such translation limitation.

## In Progress / Needs Attention / Picked

No data model exists for these yet — there is no `OrderStatus` value for "claimed," "has an unresolved picking problem," or "picked" (only `Ready` exists today). Per `/speckit-clarify` 2026-08-21 (Option A), the Dashboard's frontend renders these three sections as static "not yet available" placeholders with no corresponding backend query or API field. The future order-claiming and picking-workflow features own introducing the actual `OrderStatus` values and any further data model changes those states require.
