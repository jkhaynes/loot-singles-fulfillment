# Contract: Order Claiming API

Extends the existing `backend/src/LootSingles.Api/Controllers/OrdersController.cs` (`api/orders`,
`[Authorize]` at the controller level — any authenticated employee). All routes below are new or
extend an existing response shape; nothing existing is removed or renamed.

## Extended: `GET /api/orders` and `GET /api/orders/{orderId}`

Same routes, auth, and existing error responses as before (see
`specs/009-card-image-enrichment/contracts/order-detail-api.md`). Each order in both responses gains
two new nullable fields (US3, FR-005, SC-003):

```json
{
  "orderId": 42,
  "tcgplayerOrderId": "F8433182-7B9B3B-BC75E",
  "status": "ready",
  "importedAt": "2026-08-20T12:00:00Z",
  "claimedByEmployeeId": null,
  "claimedByEmployeeName": null
}
```

When claimed: `"status": "inProgress"`, `"claimedByEmployeeId": 7`,
`"claimedByEmployeeName": "Sam"` — matching the PRD's own example UI text
("Order #12345 — In Progress · Picking by Sam"). `claimedByEmployeeName` is the employee's existing
`DisplayName` (already-visible internal staff data, not customer PII — constitution Principle X).

## New: `POST /api/orders/pick-next` (FR-001, US1)

**Auth required**: Yes — any authenticated employee (Picker or Manager/Admin).

**Request body**: none.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | An order was available and successfully claimed for the caller | `{ "orderId": 42, "tcgplayerOrderId": "...", "status": "inProgress", "claimedByEmployeeId": 7, "claimedByEmployeeName": "Sam" }` |
| `409 Conflict` | No orders are currently available (Ready and unclaimed) | `{ "error": "no_orders_available" }` (FR-006, SC-004 — a clear outcome, not an error page or hang) |
| `409 Conflict` | Caller already holds an active claim on another order | `{ "error": "employee_has_active_claim", "claimedOrderId": 17 }` (FR-009, SC-007) |
| `401 Unauthorized` | No/expired session | `{ "error": "not_authenticated" }` |

## New: `POST /api/orders/{orderId}/claim` (FR-002, US2)

**Auth required**: Yes — any authenticated employee.

**Path parameter**: `orderId`.

**Request body**: none.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | The order was available and successfully claimed for the caller | Same shape as `pick-next`'s `200 OK` |
| `404 Not Found` | No order with this id exists | `{ "error": "order_not_found" }` |
| `409 Conflict` | The order is already claimed by someone (including a race lost to another request) | `{ "error": "order_already_claimed", "claimedByEmployeeId": 3, "claimedByEmployeeName": "Alex" }` (FR-003, FR-004, US2 AC2, SC-002 — existing claim is unaffected) |
| `409 Conflict` | Caller already holds an active claim on another order | `{ "error": "employee_has_active_claim", "claimedOrderId": 17 }` (FR-009) |
| `401 Unauthorized` | No/expired session | `{ "error": "not_authenticated" }` |

## New: `POST /api/orders/{orderId}/release` (FR-008, US4)

**Auth required**: Yes — must be the employee currently holding the claim.

**Path parameter**: `orderId`.

**Request body**: none.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | Caller held the claim; it is released, order returns to `ready` | `{ "orderId": 42, "tcgplayerOrderId": "...", "status": "ready", "claimedByEmployeeId": null, "claimedByEmployeeName": null }` |
| `404 Not Found` | No order with this id exists | `{ "error": "order_not_found" }` |
| `409 Conflict` | Order is not currently claimed by the caller (unclaimed, claimed by someone else, or already released/force-released in a race — Edge Cases §4) | `{ "error": "not_your_claim" }` |
| `401 Unauthorized` | No/expired session | `{ "error": "not_authenticated" }` |

## New: `POST /api/orders/{orderId}/force-release` (FR-010, US5)

**Auth required**: Yes — `EmployeeRole.ManagerAdmin` only (mirrors `[Authorize(Roles =
nameof(EmployeeRole.ManagerAdmin))]` already used on `EmployeesController`).

**Path parameter**: `orderId`.

**Request body**: none.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | Order was claimed by anyone; claim is force-released, order returns to `ready` | Same shape as `release`'s `200 OK` |
| `404 Not Found` | No order with this id exists | `{ "error": "order_not_found" }` |
| `409 Conflict` | Order is not currently claimed by anyone (e.g. lost the race to a concurrent release — Edge Cases §4) | `{ "error": "order_not_claimed" }` |
| `401 Unauthorized` | No/expired session | `{ "error": "not_authenticated" }` |
| `403 Forbidden` | Caller is authenticated but not a Manager/Admin | Standard ASP.NET Core `[Authorize(Roles=...)]` 403 (US5 AC2) |

## Notes

- All four write endpoints (`pick-next`, `claim`, `release`, `force-release`) are implemented as a
  single conditional `ExecuteUpdateAsync` each (research.md §1) — there is no intermediate "pending"
  state and no polling; the HTTP response itself is the final outcome (FR-003, FR-007).
- `employee_has_active_claim` on `pick-next`/`claim` is the application-level fast path for FR-009;
  the filtered unique index (research.md §3, data-model.md) is the race-proof backstop should two
  requests from the same employee's two tabs race each other.
