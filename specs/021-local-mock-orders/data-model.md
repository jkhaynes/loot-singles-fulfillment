# Data Model: Local Mock Orders and Order Deletion

One additive migration, `AddOrderSoftDelete`. Every change applies while the previous image is still running: the new columns are nullable, and the rebuilt indexes accept every row that exists today, because no order is deleted yet.

## Order (changed)

| Field | Type | Rule |
|---|---|---|
| `FirstClaimedAt` | `datetimeoffset?` | Set by the first claim, by either Pick Next or claiming a specific order, and never cleared. Not null means the order is **touched** (research R6). |
| `DeletedAt` | `datetimeoffset?` | Set when a Manager/Admin deletes the order. Not null means the order is **deleted**. |
| `DeletedByEmployeeId` | `int?` (FK Employees, Restrict) | The Manager/Admin who deleted it. Set together with `DeletedAt`: both or neither. |

Unchanged and kept on a deleted order: lines, pick outcomes and who recorded them, picking issues and their reporters, claim fields, `PackedAt`/`PackedByEmployeeId`, and import results (FR-016, FR-019).

**Removed at deletion**: the `OrderPackingSlips` row, if any. `PackingSlipAccesses` rows stay, because they point at the order, which still exists.

### Indexes

| Index | Before | After |
|---|---|---|
| `IX_Orders_TcgplayerOrderId` | unique | unique, filter `[DeletedAt] IS NULL` (FR-017) |
| `IX_Orders_ClaimedByEmployeeId` | unique, filter `[ClaimedByEmployeeId] IS NOT NULL` | unique, filter `[ClaimedByEmployeeId] IS NOT NULL AND [DeletedAt] IS NULL` |

### Query filter

`HasQueryFilter(o => o.DeletedAt == null)` on `Order`. Only the deletion code and the deletion summary use `IgnoreQueryFilters()`. Nothing else may bypass the filter.

### Backfill (in the migration)

`FirstClaimedAt` is set on existing orders to the earliest of the following, for orders that have any of them:
- `ClaimedAt` (currently claimed);
- the minimum `OrderLines.PickOutcomeRecordedAt`;
- the minimum `PickingIssues.ReportedAt` across its lines;
- `PackedAt`.

## Deletability

| Setting `OrderDeletion:AllowTouchedOrders` | Deletable when |
|---|---|
| `false` or absent (production) | `DeletedAt` is null **and** `FirstClaimedAt` is null **and** `ClaimedByEmployeeId` is null |
| `true` (Development, stage) | `DeletedAt` is null |

The second and third conditions in the production row overlap, since a claim always sets `FirstClaimedAt`. `ClaimedByEmployeeId` is kept in the condition so the rule doesn't rely on the backfill alone.

**Refusal reasons**:
- `order_not_found`: no such order;
- `order_already_deleted`: someone else deleted it first;
- `order_claimed_before`: production, and the order is touched.

## State

```
visible ──delete (Manager/Admin)──▶ deleted   (terminal; hidden; history kept)
```

There's no restore. Re-importing the same TCGplayer order number creates a new, visible `Order` row. The deleted row is not changed.

## Configuration

| Key | Default | Development | Stage | Production |
|---|---|---|---|---|
| `OrderDeletion:AllowTouchedOrders` | `false` | `true` (supplied by `Program.cs` when `IsDevelopment()`, lowest precedence) | `true` (portal env var, quickstart §D) | not set |

The synthetic TCGplayer source has **no** configuration key. It depends only on the environment being Development (research R2).
