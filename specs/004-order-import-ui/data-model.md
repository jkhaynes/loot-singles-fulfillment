# Data Model: Order Import UI

No database tables or columns are added. Existing entities are exposed through narrow stream/read models.

## Existing persisted entities

### Order

| Field | Type | Rule |
|---|---|---|
| `Id` | integer | Stable internal identifier |
| `TcgplayerOrderId` | string | Required, unique, displayed |
| `Status` | `OrderStatus` | Display actual stored value; never infer/default |
| `ImportedAt` | timestamp with offset | Existing import timestamp |

`OrderLines` are neither loaded nor exposed. No customer/shipping field is included.

### ImportAttempt

Uses `StartedAt`, `CompletedAt`, `AttemptFailureCode`, `AttemptFailureMessage`, and its `ImportOrderResults`. `UnreadablePdf` is presented as file failure; `SummaryMismatch` as a distinct warning.

### ImportOrderResult

Uses nullable `SourceOrderIdentifier`, required `Outcome`, typed `FailureCode`/specific `FailureMessage` for rejection, and `ResultingOrderId` for success.

## Import progress snapshot (wire/UI state)

Each immutable NDJSON record contains `status` (`InProgress`, `Completed`, or `Failed`), detected/processed/succeeded/failed counters, attempt code/message, nullable employee-safe `operationFailureMessage`, and all evaluated results so far. A line replaces the prior snapshot rather than acting as a delta. Public records are emitted only after `ordersProcessed` increases and at terminal completion; parsing pages, lines, or newly detected-but-unprocessed orders do not produce UI events.

Invariants:

- Counters are non-negative; processed equals succeeded plus failed and does not exceed detected.
- Success has a resulting order id and no failure code.
- Rejection has a typed failure code and no resulting order id.
- `InProgress` is non-terminal; each such public snapshot has `ordersProcessed` greater than the previous emitted snapshot.
- `Completed` is terminal and means the service finished evaluating the file, including ordinary unreadable-file, mismatch, and per-order rejection outcomes.
- `Failed` is terminal and means the server confirmed an infrastructure/operation failure while it could still communicate. The UI retains completed-order counters/results, labels the batch Failed, explains that completed orders remain imported, and offers retry.
- `operationFailureMessage` is non-null only for `Failed`; it contains no exception, stack trace, or customer data and is not parsed for control flow.
- `Interrupted` is client-only, never sent by the server, and is derived when the connection ends before `Completed` or `Failed`. The last snapshot remains visible only as explicitly incomplete and potentially stale context, with connection-loss explanation and retry.
- `SummaryMismatch` is warning severity alongside results; `UnreadablePdf` is file-failure severity.

```text
Idle -> InProgress -> Completed
                  |-> Failed
                  `-> Interrupted (client-derived missing terminal update)
```

Completed can contain all successes, mixed outcomes, unreadable input with no results, or a mismatch warning with partial/full results. Failed and Interrupted may leave previously completed per-order transactions committed and therefore retain qualified partial snapshots. A retry converges on one Order per source identifier: committed orders become typed duplicates and missing orders are imported.

## Order list item (read projection)

| Field | Type | Rule |
|---|---|---|
| `orderId` | integer | Existing `Order.Id` |
| `tcgplayerOrderId` | string | Existing identifier |
| `status` | enum-name string | Exact current stored status |
| `importedAt` | ISO-8601 timestamp | Existing import time |

Items sort by import time descending, then identifier. An empty array is valid. `OrderListItem` is returned by the narrow list query on `IOrderRepository`; it is not a Domain entity and does not imply that the repository loads the full aggregate for reads. The immediate import result is request/UI state, not a new stored entity or history. A successful result may reference one persisted Order.
