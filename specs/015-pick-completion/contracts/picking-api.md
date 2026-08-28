# Picking API Contract

New endpoints under the existing `OrdersController` (mirroring `POST
/api/orders/{id}/claim`/`release`/`force-release` from feature 013), plus extensions to two
existing response shapes and one existing controller.

## POST /api/orders/{orderId}/lines/{lineId}/pick

Records the line as successfully picked.

**Auth**: authenticated employee (existing convention — `ActorEmployeeId()` helper).

**Response** (`switch` expression over a new `PickingOutcome`, mirroring `OrderClaimOutcome`'s
existing controller pattern):

| Outcome | HTTP | Body |
|---|---|---|
| `Success` | 200 | `OrderDetailResponse` (updated order, all lines, recomputed `Status`) |
| `OrderNotFound` | 404 | problem details |
| `LineNotFound` | 404 | problem details |
| `NotYourClaim` | 409 | problem details — actor does not currently hold this order's claim (FR-010) |

## POST /api/orders/{orderId}/lines/{lineId}/report-issue

Records the line as having an unresolved picking issue.

**Auth**: same as above.

**Request body**:

```json
{
  "issueType": "CardNotFound",
  "requiredQuantity": 2,
  "foundQuantity": 1,
  "note": "Only one copy in the bin"
}
```

`issueType` required (one of the `PickingIssueType` values). `requiredQuantity`, `foundQuantity`,
`note` all optional (FR-002 — note is explicitly optional).

**Response**: same outcome table as `pick`, plus:

| Outcome | HTTP | Body |
|---|---|---|
| `InvalidIssueType` | 400 | problem details — unrecognized `issueType` value |

## GET /api/orders/{orderId} (existing endpoint — response extended)

`OrderLineDetailResponse` gains:

```json
{
  "id": 42,
  "pickOutcome": "HasIssue",
  "currentIssue": {
    "issueType": "CardNotFound",
    "requiredQuantity": 2,
    "foundQuantity": 1,
    "note": "Only one copy in the bin",
    "reportedByEmployeeName": "J. Smith",
    "reportedAt": "2026-08-27T14:32:00Z"
  }
}
```

`id` is new and required (closes the gap identified in research.md/plan.md — the frontend
currently has no stable line identifier to target). `pickOutcome` is `null` until first recorded.
`currentIssue` is `null` when `pickOutcome` is `Picked` or not yet recorded.

`OrderResponse` (list view) is unchanged beyond already exposing `Status`, which now may also be
`Picked` or `NeedsAttention`.

## GET /api/dashboard (existing endpoint — response extended)

`DashboardResponse` gains three sections alongside the existing `ready`, mirroring
`ReadySectionResponse`'s shape:

```json
{
  "ready": { "count": 3, "orders": [ ... ] },
  "inProgress": { "count": 2, "orders": [ ... ] },
  "needsAttention": {
    "count": 1,
    "orders": [
      { "orderId": 101, "tcgplayerOrderId": "TCG-1001", "flaggedProductNames": ["Lightning Bolt (Foil)"] }
    ]
  },
  "picked": { "count": 5, "orders": [ ... ] }
}
```

`needsAttention` orders carry `flaggedProductNames` (FR-014); `inProgress` and `picked` reuse the
plain order-summary shape already used by `ready`.
