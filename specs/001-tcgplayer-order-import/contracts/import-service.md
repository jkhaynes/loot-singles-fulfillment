# Contract: Packing Slip Import Service

This feature's spec explicitly defers "how an operator supplies the PDF" to a future feature (import UI/entry point). There is therefore no HTTP endpoint contract here — the seam this feature exposes to any future caller is an **application-service interface**, consumed in-process by whatever future feature owns the actual upload/trigger mechanism.

## `IPackingSlipImportService`

```text
IAsyncEnumerable<ImportProgressUpdate> ImportAsync(Stream packingSlipPdf, CancellationToken ct)
```

- **Input**: `packingSlipPdf` — a readable stream of the packing slip PDF's bytes. The service does not persist this stream or any copy of it anywhere durable (FR-019); the caller owns the stream's lifecycle before and after this call.
- **Output**: An async stream of `ImportProgressUpdate` values, yielded as processing advances through the file's orders. The caller awaits the full enumeration to know the import is complete (FR-014 — this is synchronous from the caller's perspective: the call is not "done" until enumeration ends). The final `ImportProgressUpdate` reflects the completed `ImportAttempt`.
- **Does not throw** for ordinary rejection cases (an unreadable file, a malformed order, a duplicate) — those are represented as data in the yielded updates and the final `ImportAttempt`/`ImportOrderResult` records (FR-006), not as exceptions. **This also covers a genuine persistence failure while saving an individual order** (e.g., a transient database error during that order's `SaveChangesAsync`): FR-016 requires that such a failure must not prevent sibling orders in the same batch from still being persisted, so it too is represented as a per-order `ImportOrderResult` rejection, never a propagated exception — regardless of whether the failure was a duplicate-key violation or some other persistence problem. Exceptions are reserved for infrastructure failures that occur **outside** the per-order persistence boundary — before any order has been evaluated, or after the per-order loop has finished processing every order — where no order-level "keep processing the rest of the batch" guarantee is at stake. Those are the caller's problem to handle, not a defined outcome of this contract.

### `ImportProgressUpdate` (yielded value shape)

| Field | Meaning |
|---|---|
| OrdersDetected | Total orders found in the file so far (grows as pages are read; stable once the whole file has been scanned) — FR-014 |
| OrdersProcessed | Count of orders evaluated (succeeded or rejected) so far — FR-014 |
| SucceededCount | Running count of successfully imported orders — FR-014 |
| FailedCount | Running count of rejected orders — FR-014 |
| IsComplete | True only on the final yielded update |
| ImportAttempt | The (in-progress or, on the final update, completed) `ImportAttempt` record, including its `ImportOrderResult`s so far |

### Consumer expectations (for whoever builds the future caller)

- Treat every `ImportProgressUpdate` before the last as a snapshot, not a delta — always read the whole set of counters/results, don't try to diff them.
- The final `ImportAttempt` (where `IsComplete = true`) is the durable record (FR-012); everything before that is presentation-layer convenience only and does not need to be persisted by the caller.
- A `SummaryMismatch` or `UnreadablePdf` outcome on the final `ImportAttempt` means zero or partial `ImportOrderResult`s may be present — the caller must not assume every order in the file was evaluated in that case (FR-015).

## Non-goals of this contract

- No authentication/authorization is defined here — that's the future caller's concern, layered on top of this service, not inside it (spec Assumptions).
- No HTTP status codes, routes, or request/response DTOs — this is an in-process C# interface, not a network API, until/unless a future feature decides to expose it over HTTP.
