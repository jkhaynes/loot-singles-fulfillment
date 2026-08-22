# Contract: Packing Slip Import HTTP API

## `POST /api/imports`

- Authenticated cookie session required; either employee role.
- Request: `multipart/form-data` with exactly one `file` field and a maximum file size of 25 MB (`26,214,400` bytes).
- PDF metadata required; parser determines actual readability.
- File stream is not retained.
- Success: `200 OK`, `application/x-ndjson`.

Each line is a complete snapshot emitted after one order finishes processing:

```json
{"status":"InProgress","ordersDetected":2,"ordersProcessed":1,"succeededCount":1,"failedCount":0,"attemptFailureCode":null,"attemptFailureMessage":null,"results":[{"sourceOrderIdentifier":"A-100","outcome":"succeeded","failureCode":null,"failureMessage":null,"resultingOrderId":42}]}
```

Every snapshot also has nullable `operationFailureMessage`. It is populated only for terminal `Failed`, contains a safe employee-facing explanation without exception/stack/PII details, and is null for `InProgress` and `Completed`. Control flow depends on `status`, not this message.

The API MUST NOT emit parser-internal milestones such as a page or product line being scanned, nor updates that only increase `ordersDetected`. It emits `InProgress` when `ordersProcessed` increases, so employees see order 1 processed, order 2 processed, and so on. It then emits exactly one terminal `Completed` or `Failed` update when the connection is writable. Consumers replace current state per line.

### Operation status

| Status | Terminal | Meaning |
|---|---|---|
| `InProgress` | No | Another order finished; counters/results are the current snapshot |
| `Completed` | Yes | The service finished the file; ordinary typed file/order outcomes may be present |
| `Failed` | Yes | The server confirmed an infrastructure/operation failure and communicated it; completed-order results remain valid partial context |

`Interrupted` and `Cancelled` are not server statuses. The client derives `Interrupted` when an unexpected HTTP/body failure, JSON framing failure, or premature EOF occurs before a terminal `Completed` or `Failed` update. The UI preserves the last snapshot as explicitly incomplete and potentially stale, states that the connection was lost and some orders may already have imported, and offers a safe retry. The client derives `Cancelled` when the employee intentionally aborts the active request; it retains the last snapshot as partial context, explains that completed orders remain imported and remaining work stopped, and offers retry without a connection-loss message. On terminal `Failed`, the UI likewise retains partial results, but labels the batch Failed and explains that completed orders remain imported.

### Cancellation and idempotent retry

The controller MUST pass `HttpContext.RequestAborted` to the import service and downstream async operations. When aborted, processing stops at the next cancellation boundary. Already committed per-order transactions remain committed; the active uncommitted order must not leave partial Order/OrderLine data.

The frontend import decoder accepts an `AbortSignal` and passes it to `fetch`. An employee-initiated abort remains distinguishable as `AbortError` to the page rather than being translated into `Interrupted`; the page derives `Cancelled` while retaining its last received snapshot. Each submission or retry uses a fresh abort controller.

Re-uploading the same PDF is duplicate-safe. `Order.TcgplayerOrderId` database uniqueness is authoritative, including concurrent retry races. Previously committed orders return typed `duplicateOrder` results and are not inserted again; orders not committed before interruption may import normally. A retry therefore cannot create duplicate or partially corrupted orders.

Stable strings: outcomes are `succeeded`/`rejected`; attempt codes are `unreadablePdf`, `summaryMismatch`, or null; order failure codes are lower-camel existing `FailureType` values (including missing fields, invalid quantity, duplicate order, and persistence failure where applicable). UI behavior uses codes and displays paired detail; it never parses messages.

| Status | Meaning |
|---|---|
| `400` | Missing/multiple file fields or malformed multipart |
| `415` | Non-PDF metadata |
| `413` | File exceeds the server-enforced 25 MB maximum; import processing was not invoked |
| `401` | Invalid/missing session |
| `500` | Infrastructure failure before streaming begins |

Errors use Problem Details without customer data. After headers begin, a non-cancellation server failure yields `Failed` when writable; an unexpected disconnect prevents that update and is derived as `Interrupted` by the client; an employee-initiated abort is derived as `Cancelled`. None of these paths may emit a false `Completed`.

Presentation semantics:

- `summaryMismatch` is a distinct batch warning alongside results.
- `unreadablePdf` is a file-level failure with no successful orders.
- Each rejection shows its specific typed reason.
- Confirmed server failure shows `Failed`; unexpected connection loss/missing terminal state shows `Interrupted`; employee-initiated abort shows `Cancelled`. Each retains qualified partial context and safe-retry guidance rather than reporting zero results.
