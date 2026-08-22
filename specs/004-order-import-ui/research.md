# Phase 0 Research: Order Import UI

## Progress transport

**Decision**: One authenticated multipart `POST /api/imports` returns `application/x-ndjson`. The HTTP adapter suppresses parsing-only updates and flushes `InProgress` only when `OrdersProcessed` increases (one completed order), then emits exactly one terminal `Completed` or `Failed` update when the connection remains available.

**Rationale**: Employees need meaningful progress—orders completed—not parser implementation details. Coalescing preserves the richer internal stream without coupling the UI to it. Explicit status separates successful completion from a confirmed server failure. A required terminal status makes truncation observable.

**Alternatives considered**: Forward every service/parser update (too noisy and couples UI to parsing internals); time-based throttling (can hide completed-order milestones and makes tests timing-sensitive); job/polling (contradicts synchronous scope); WebSockets/SignalR (unneeded bidirectionality); SSE (GET-oriented and awkward for upload); one final JSON response (no live progress).

## Failure mapping

**Decision**: Ordinary file/order outcomes remain typed records. Missing/non-PDF or oversized input gets Problem Details before invocation. A caught non-cancellation infrastructure exception after streaming begins produces terminal `Failed` if the response is writable; the UI retains any completed-order snapshot, labels it Failed, explains committed orders remain, and offers retry. Non-success HTTP, stream/JSON failure, or EOF before terminal `Completed`/`Failed` derives client-only `Interrupted` and retains the last snapshot only as incomplete and potentially stale.

**Rationale**: Unreadable PDFs and order rejections are completed data outcomes, while infrastructure can fail the operation. HTTP status cannot change after streaming begins, so typed terminal records and client-side interruption detection are essential. UI behavior keys on status/codes, never message text.

**Alternatives considered**: Make all failures HTTP errors (loses partial outcomes); inspect exception/message text (constitution violation); treat truncation as empty success (misleading).

## Cancellation and retry safety

**Decision**: Pass `HttpContext.RequestAborted` to `ImportAsync` and all downstream async work. On cancellation, stop parsing/processing remaining orders and do not attempt to write a terminal update to the disconnected client. Keep the existing per-order transaction boundary: committed orders remain, an in-flight uncommitted order rolls back, and later orders are untouched. Re-upload uses the existing unique `Order.TcgplayerOrderId` constraint and duplicate translation, so it imports only missing orders and cannot create duplicates.

**Rationale**: Continuing work after the employee disconnects wastes resources, while rolling back completed orders would require a batch transaction that contradicts partial-import behavior. Database uniqueness is the authoritative concurrency-safe idempotency guarantee; application pre-checks provide friendly duplicate results but are not relied upon for correctness.

**Alternatives considered**: Continue as background work (requires durable jobs/results); roll back the entire batch (contradicts per-order atomicity); client-generated idempotency key (new persisted mechanism unnecessary because source order IDs already provide the required invariant); pre-check alone (race unsafe).

## Upload and retention

**Decision**: Accept one `file` multipart field with a server-enforced 25 MB maximum, defined for configuration/testing as 25 MiB (`26,214,400` file bytes); reject an oversized file before invoking the import service. Configure the enclosing multipart request limit slightly higher so form framing does not reject a file exactly at the approved boundary. Use filename/content type only for early PDF feedback, then let the parser authoritatively decide readability. Pass `OpenReadStream()` directly to the service and never create application-owned disk/blob copies.

**Rationale**: The clarified 25 MB boundary limits memory/request exposure and is independently enforceable and testable. Metadata is untrusted but useful; parser validation stays authoritative and direct streaming preserves feature 001's zero-retention decision.

**Alternatives considered**: Hosting defaults alone (not a stable product boundary); extension-only trust; duplicate parser logic in controller; durable temporary storage.

## Order browse query

**Decision**: Add `OrdersService` for cohesive application operations on the `Order` aggregate and an aggregate-aligned `IOrderRepository` with a narrow projected list method, implemented by EF `OrderRepository`. EF uses `AsNoTracking()`, projects only id/identifier/status/import time, and sorts by newest import then identifier.

**Rationale**: Orders have foreseeable lifecycle operations, including status changes, so browse-only repository and service types would create temporary artificial boundaries. One service and repository stay aligned with the `Order` aggregate; they can hold cohesive order operations and be split only if concrete complexity later warrants it. Projection avoids line loading and structurally excludes customer data. V1 explicitly permits an unpaginated list.

**Alternatives considered**: `IOrderBrowseRepository`/`OrderBrowseService` (too narrowly named for known upcoming order lifecycle work); one service class per order operation (unnecessary at current complexity); Dashboard endpoint (Ready-only/missing timestamp); controller DbContext access; generic filtering/pagination (unapproved).

## Responsive presentation

**Decision**: Use stacked semantic cards on narrow screens and a denser grid/table-like layout on wide screens, using existing tokens. Long values wrap.

**Rationale**: One card-first DOM avoids horizontal scroll and duplicated product logic.

**Alternatives considered**: Horizontally scrolling table; separate mobile implementation; new component library.

## Test strategy

**Decision**: xUnit integration tests cover auth, multipart and 25 MB boundary validation, no processing for oversized input, suppression of parsing-only updates, one progress advance per processed order, all three wire statuses, cancellation propagation, per-order commit preservation, and retry without duplicates/corruption; Vitest/RTL covers decoding, retained partial results for Failed, derived `Interrupted` with incomplete/stale labeling, connection-lost copy, and retry actions; Playwright covers authenticated import/browse and responsive overflow.

**Rationale**: Streaming crosses service, serialization, framing, and presentation boundaries; focused tests cover those without duplicating parser tests owned by feature 001.

**Alternatives considered**: Full parser retesting through UI; snapshot-only tests; manual-only responsive checks.
