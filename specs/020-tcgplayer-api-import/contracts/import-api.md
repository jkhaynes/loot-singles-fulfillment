# Contract: Get New Orders from TCGplayer

**Feature**: `020-tcgplayer-api-import`. This extends `specs/004-order-import-ui/contracts/import-api.md`, which still governs `POST /api/imports` (PDF upload) unchanged.

## `POST /api/imports/tcgplayer`

Pulls the store's open orders from TCGplayer and imports each one that isn't already in the application.

- **Auth**: `[Authorize]`, so any signed-in employee (FR-003). Unauthenticated requests get `401`, as on every API route.
- **Request body**: none. The route takes no parameters, so nothing a client sends can change which statuses, store or credentials are used.
- **Response**: `200 OK`, `Content-Type: application/x-ndjson`. The response is a stream of `ImportSnapshot` lines in **exactly the shape `POST /api/imports` already emits**: the same fields, camelCase, and enums as camelCase strings. The frontend reuses one stream reader for both routes.
- **Cancellation**: aborting the request cancels the remaining work. Orders already committed stay, the order in flight is not committed, and pressing again resumes (FR-013). This is the same rule as PDF import.

### Snapshot stream

```text
{ "status": "inProgress", "ordersDetected": 12, "ordersProcessed": 1, "succeededCount": 1, "failedCount": 0, "results": [ … ] }
…
{ "status": "completed",  "ordersDetected": 12, "ordersProcessed": 12, "succeededCount": 9, "failedCount": 3, "results": [ … ] }
```

- A snapshot is written each time another order finishes, plus a final one. For the API source, `ordersDetected` is the number of open orders TCGplayer returned.
- `results[]`: `{ sourceOrderIdentifier, outcome: "succeeded" | "rejected", failureCode?, failureMessage?, resultingOrderId? }`.
  - An already-imported order is `rejected` with `failureCode: "duplicateOrder"` and the message "Already imported". Its items are **not** fetched again.
  - A per-order `incompleteOrder` or `tcgplayerResponseInvalid` result carries a specific message, for example "TCGplayer returned 3 of this order's 4 lines", or "Line quantities total 5 but TCGplayer reports 6 items".
  - The wire format is unchanged: the stream keeps one `duplicateOrder` result per already-imported order, and `failedCount` still counts them. Only the presentation differs. On an API press the UI collapses them into one summary line, "N already imported", and lists new and rejected orders individually (FR-011). Pressing again while orders are still open is therefore a normal outcome, not a failure. PDF results are unchanged.
- **No new orders**: a final `completed` snapshot with `ordersDetected: 0`, or with every result a `duplicateOrder`. The UI shows "No new orders", distinct from any failure (FR-011).

### Attempt-wide failures

These end the stream with `status: "failed"` and `attemptFailureCode` set. Results already streamed stay valid.

| `attemptFailureCode` | When | `attemptFailureMessage` (shown verbatim) |
|---|---|---|
| `tcgplayerNotConfigured` | No credentials in this environment. No call is made. | "Getting orders from TCGplayer isn't set up here. Use packing-slip upload instead." |
| `tcgplayerUnavailable` | TCGplayer is unreachable, times out, or returns 5xx or 429 | "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip." |
| `tcgplayerAccessRefused` | Credentials rejected (401 after one refresh, or 403) | "TCGplayer refused the store's connection. A manager needs to check the TCGplayer API setup. You can upload a packing slip meanwhile." |
| `tcgplayerResponseInvalid` | The manifest or search response didn't have the expected shape, a configured open status doesn't exist, or paging stalled | A specific message, for example "TCGplayer has no order status named 'Ready To Ship'." |

None of these messages contains credentials, raw response content or customer data (FR-023, FR-017).

### Server errors

As for `POST /api/imports`: an unexpected exception before streaming starts returns `500 application/problem+json`. After streaming has started, it ends the stream with a `failed` snapshot carrying `operationFailureMessage`.

## `GET /api/orders/{orderId}`: changed field semantics

The response shape is unchanged. For lines:

- `collectorNumber` may now be `null`, for API orders only. Clients show "No number".
- `language` is **new**, a nullable string. Clients show it when present and not `English`.
- `imageUrl`:
  - for `TcgplayerApi` orders, the stored TCGplayer image, or `null`;
  - for `PackingSlipPdf` orders, resolved as before.
  An API order never triggers a third-party catalog lookup.

The other order read models that carry `collectorNumber` (focused pick view, order finish) follow the same null rule.
