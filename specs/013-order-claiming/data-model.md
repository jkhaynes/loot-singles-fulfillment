# Phase 1 Data Model: Exclusive Order Claiming

## Order (existing entity, `backend/src/LootSingles.Domain/Orders/Order.cs`)

Gains two new nullable fields — no existing field changes meaning.

| Field | Type | Notes |
|---|---|---|
| `ClaimedByEmployeeId` | `int?` | FK to `Employee.Id`. `null` = unclaimed. Set only by a successful claim (FR-001/FR-002); cleared only by release (FR-008) or force-release (FR-010). |
| `ClaimedAt` | `DateTimeOffset?` | UTC timestamp of the successful claim. `null` iff `ClaimedByEmployeeId` is `null`. Not exposed as a countdown/expiry — FR-008 explicitly forbids automatic expiry; this is informational only (e.g. for future "how long has this been claimed" display, out of this feature's scope to render). |

**Invariant**: `ClaimedByEmployeeId is null` **iff** `Status == OrderStatus.Ready`, and
`ClaimedByEmployeeId is not null` **iff** `Status == OrderStatus.InProgress`. This invariant is
maintained entirely by `OrderClaimService` — every write to these three fields goes through exactly
one of the four conditional operations in this feature (claim, pick-next, release, force-release);
no other code path mutates them.

## OrderStatus (existing enum, `backend/src/LootSingles.Domain/Orders/OrderStatus.cs`)

```csharp
public enum OrderStatus
{
    Ready = 0,
    InProgress = 1,
}
```

`InProgress` is new. No other values are added — `Picked`/`Completed` and any picking-issue states
belong to future features per FR-011 and are explicitly out of scope here.

## Employee (existing entity, unchanged)

No schema changes. `EmployeeRole.ManagerAdmin` gains the *behavioral* capability to force-release
(FR-010) — this is authorization logic in the API/Application layer, not a data model change.

## Order Claim (conceptual entity, Key Entities in spec.md — not a separate table)

Modeled entirely as the `(ClaimedByEmployeeId, ClaimedAt)` pair on `Order`, not as a standalone
table or history log — spec.md's Key Entities section describes it as "a conceptual association,"
and no requirement calls for retaining claim *history* (who claimed an order in the past), only
current claim state. This keeps the model proportional to what FR-001–FR-010 actually require
(constitution Principle XIII).

- **Cardinality**: at most one active claim per order (enforced by the conditional `WHERE
  ClaimedByEmployeeId IS NULL` update in research.md §1), and at most one active claim per employee
  (enforced by the filtered unique index in research.md §3).
- **Lifecycle**: created by a successful claim (FR-001/FR-002) → ended only by release (FR-004) or
  force-release (FR-010, Manager/Admin only) → never expires automatically (FR-008).

## Database Migration

One EF Core migration adds:
- `Orders.ClaimedByEmployeeId` (`int`, nullable, FK to `Employees.Id`)
- `Orders.ClaimedAt` (`datetimeoffset`, nullable)
- A filtered unique index: `CREATE UNIQUE INDEX IX_Orders_ClaimedByEmployeeId ON Orders(ClaimedByEmployeeId) WHERE ClaimedByEmployeeId IS NOT NULL`
  (research.md §3 — enforces "at most one active claim per employee" at the database layer)

`OrderStatus.InProgress = 1` requires no migration by itself (the `Status` column already exists and
stores the enum), but is only reachable once the above columns exist and `OrderClaimService` starts
writing it.
