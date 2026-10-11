# API Contract: Order Deletion

All endpoints below are under the existing cookie authentication. Errors use the existing `{ "error": "<code>" }` shape (`OrdersController`).

## `GET /api/orders` (changed)

Each list item gains two fields:

```json
{
  "orderId": 42,
  "tcgplayerOrderId": "SYN-0002-A1",
  "status": "InProgress",
  "importedAt": "2026-10-10T14:00:00Z",
  "claimedByEmployeeId": 7,
  "claimedByEmployeeName": "Sam",
  "canDelete": true,
  "deleteBlockedReason": null
}
```

- `canDelete` is `false` for any employee who isn't a Manager/Admin. The UI shows no delete option to them (FR-010).
- When the caller is a Manager/Admin and the order can't be deleted, `deleteBlockedReason` is `"claimed_before"`, and the row menu says why (FR-021). Otherwise it's `null`.
- Deleted orders are never listed.

## `GET /api/orders/deletion-summary` (new, Manager/Admin)

The deletion summary drives the delete-all confirmation (FR-014).

```json
{
  "asOf": "2026-10-10T15:02:11.123Z",
  "deletableCount": 2,
  "keptCount": 4,
  "claimedCount": 0,
  "withUnresolvedIssueCount": 0
}
```

- `keptCount` is the number of visible orders that can't be deleted. It's always 0 when touched orders are allowed.
- `claimedCount` and `withUnresolvedIssueCount` count only deletable orders. They drive the confirmation's warning line.
- Any other role gets **403**.

## `DELETE /api/orders/{orderId}` (new, Manager/Admin)

| Result | Status | Body |
|---|---|---|
| Deleted | 204 | none |
| No such order | 404 | `{ "error": "order_not_found" }` |
| Already deleted | 404 | `{ "error": "order_already_deleted" }` |
| Production rule refuses it | 409 | `{ "error": "order_claimed_before" }` |
| Not a Manager/Admin | 403 | none |

Deleting an order:
- sets `DeletedAt` and `DeletedByEmployeeId`;
- removes the stored packing slip;
- logs one Information line: `Manager {ManagerId} deleted order {OrderId} ({TcgplayerOrderId}).`

## `POST /api/orders/delete-all` (new, Manager/Admin)

Request:

```json
{ "asOf": "2026-10-10T15:02:11.123Z" }
```

`asOf` is the value from the deletion summary. Only orders imported at or before it are considered.

| Result | Status | Body |
|---|---|---|
| Done (including 0 deleted) | 200 | `{ "deletedCount": 2, "keptCount": 4 }` |
| `asOf` missing or in the future | 400 | `{ "error": "invalid_as_of" }` |
| Not a Manager/Admin | 403 | none |

Logs one Information line: `Manager {ManagerId} deleted {DeletedCount} orders; {KeptCount} kept.` Each deleted order also carries its own `DeletedAt` and `DeletedByEmployeeId`.

The typed `DELETE` word is checked only in the UI (research R10).

## Existing endpoints on a deleted order

Every order endpoint that takes an `orderId`, or a packing scan, answers a deleted order exactly as it answers a missing one: **404** `order_not_found`, or `PackingOrderNotFound` at the packing desk (FR-018). This covers claim, release, force-release, pick, issue, label, detail and packing slip.

## Frontend contract

`ordersApi.ts` gains:
- `getDeletionSummary()`;
- `deleteOrder(orderId)`, which throws `OrderNotFoundError`, `OrderAlreadyDeletedError` or `OrderClaimedBeforeError`;
- `deleteAllOrders(asOf)`.

`OrderListItem` gains `canDelete` and `deleteBlockedReason`.
