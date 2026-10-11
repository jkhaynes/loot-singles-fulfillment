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
  - A per-order `incompleteOrder` or `tcgplayerResponseInvalid` result carries a specific message, for example "TCGplayer returned 3 of this order's 4 lines", "Line quantities total 5 but TCGplayer reports 6 items", or "SYN-0001-A1: TCGplayer returned no details for this order".
  - The wire format is unchanged: the stream keeps one `duplicateOrder` result per already-imported order, and `failedCount` still counts them. Only the presentation differs. On an API press the UI collapses them into one summary line, "N already imported", and lists new and rejected orders individually (FR-011). Pressing again while orders are still open is therefore a normal outcome, not a failure. PDF results are unchanged.
- **No new orders**: a final `completed` snapshot with `ordersDetected: 0`, or with every result a `duplicateOrder`. The UI shows "No new orders", distinct from any failure (FR-011).

### Attempt-wide failures

These end the stream with `status: "failed"` and `attemptFailureCode` set. Results already streamed stay valid.

| `attemptFailureCode` | When | `attemptFailureMessage` (shown verbatim; see the display notes below for `tcgplayerResponseInvalid`) |
|---|---|---|
| `tcgplayerNotConfigured` | No credentials in this environment. No call is made. | "Getting orders from TCGplayer isn't set up here. Use packing-slip upload instead." |
| `tcgplayerUnavailable` | TCGplayer is unreachable, times out, or returns 5xx or 429 | "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip." |
| `tcgplayerAccessRefused` | Credentials rejected (401 after one refresh, or 403) | "TCGplayer refused the store's connection. A manager needs to check the TCGplayer API setup. You can upload a packing slip meanwhile." |
| `tcgplayerResponseInvalid` | The manifest, search or details response didn't have the expected shape; the manifest lacks a configured open order status, open pickup status or order type name; or paging stalled | A specific message naming every missing name, for example "The order manifest has no pickup status named 'Awaiting Collection' (check Tcgplayer:OpenPickupStatuses)." |

None of these messages contains credentials, raw response content or customer data (FR-023, FR-017).

How the import screen shows them: the failure banner sits directly under "Get new orders", and the packing-slip section follows it.

- `tcgplayerNotConfigured`, `tcgplayerUnavailable` and `tcgplayerAccessRefused`: the banner shows `attemptFailureMessage` verbatim.
- `tcgplayerResponseInvalid`: the banner leads with a fixed plain-language line, "TCGplayer sent a reply the app didn't understand. A manager should check the TCGplayer setup.", and shows `attemptFailureMessage` underneath in small muted text as the technical detail.
- `tcgplayerResponseInvalid` ends with "You can still upload a packing slip below." The other three messages already point to packing-slip upload, so the banner shows them without that line and the advice appears once.
- When the attempt failed before any order was detected, the "0 of 0 orders processed" count is not shown.
- There is no separate Retry button for an API press: "Get new orders" stays enabled after a failure or cancel, and pressing it again is the retry. PDF imports keep their Retry button.
- A cancelled API press reads "Import cancelled. Completed orders remain imported and remaining processing stopped. Press Get new orders to try again." A cancelled PDF import keeps "… You can safely retry." with its Retry button.
- While an import runs, "Cancel Import" sits with the control that started it: under "Get new orders" for an API press, and in the packing-slip section above the PDF progress for a PDF import.

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
