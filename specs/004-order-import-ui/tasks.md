---

description: "Task list template for feature implementation"
---

# Tasks: Order Import UI

**Input**: Design documents from `/specs/004-order-import-ui/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/import-api.md](contracts/import-api.md), [contracts/orders-api.md](contracts/orders-api.md), [quickstart.md](quickstart.md)

**Tests**: Required, not optional — constitution Principle IV mandates strict Red → Green → Refactor TDD for all new/modified application behavior (CLAUDE.md). Every test task below MUST be written and confirmed failing before its paired implementation task begins.

**Organization**: Tasks are grouped by user story (US1 = P1 import screen, US2 = P2 browse view) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Exact file paths are included in every description

## Path Conventions

Existing layered backend (`backend/src/LootSingles.{Domain,Application,Infrastructure,Api}`, `backend/tests/LootSingles.{UnitTests,IntegrationTests,E2EHost}`) and feature-folder frontend (`frontend/src/features/<feature>/`, `frontend/tests/<feature>/`, `frontend/e2e/`), per plan.md's Project Structure. No new projects or dependencies are introduced; `react-router-dom` is already an installed but unused frontend dependency.

---

## Phase 1: Setup

No project-initialization tasks are required. This feature extends the existing solution: no new projects, packages, or fixture files are needed (`react-router-dom` is already listed in `frontend/package.json`, and all packing-slip fixtures referenced by quickstart.md already exist under `backend/tests/LootSingles.Fixtures/PackingSlips/`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared plumbing that both user stories' implementation tasks depend on.

**⚠️ CRITICAL**: No user story implementation task may begin until this phase is complete.

- [X] T001 [P] Configure camelCase JSON serialization in `backend/src/LootSingles.Api/Program.cs` (`PropertyNamingPolicy.CamelCase` plus a `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`), matching the contracts' lower-camel field names, `status` values, and `FailureType` codes (e.g. `unreadablePdf`, `missingOrderIdentifier`). Both `OrdersController`'s `Ok(...)` result and `ImportsController`'s manually-written NDJSON lines must reuse these same options.
- [X] T002 [P] Wrap the app in `react-router-dom`'s `BrowserRouter` in `frontend/src/main.tsx` and introduce a `<Routes>` shell in `frontend/src/App.tsx` with the existing `DashboardPage` mounted at `/`, preserving the current login-gated (`employee ? ... : <LoginPage>`) behavior unchanged. Later story tasks add the `/import` and `/orders` routes once those pages exist.
- [X] T003 [P] Register the Import services (`IPackingSlipParser`/`PdfPigPackingSlipParser`, `IPackingSlipImportService`/`PackingSlipImportService`, `IImportPersistence`) and the same camelCase `AddJsonOptions` configuration as T001 in `backend/tests/LootSingles.E2EHost/Program.cs`, mirroring `backend/src/LootSingles.Api/Program.cs`, so Playwright can exercise `POST /api/imports` against the E2E host. `IOrderRepository`/`OrderRepository`/`OrdersService` do not exist yet at this point in the plan (they're created by T023–T026) — their E2EHost registration is added later, by T028.
- [X] T004 [P] Consolidate `IImportPersistence` (feature 001) into a dedicated `ImportRepository` class in `backend/src/LootSingles.Infrastructure/Persistence/ImportRepository.cs` — constructor-injecting `LootSinglesDbContext`, moving `AddImportAttempt`/`AddOrder`/`DiscardOrder`/`OrderExistsAsync`/`SaveChangesAsync` (including its `DbUpdateException` → `UniqueConstraintViolationException`/`OrderPersistenceException` translation) off `LootSinglesDbContext` itself and onto this new class, mirroring the established `EmployeeRepository`/`DashboardRepository` shape (and the `OrderRepository` this feature is introducing) instead of the interface being implemented directly by the DbContext. Update the DI registration in `backend/src/LootSingles.Api/Program.cs` and `backend/tests/LootSingles.E2EHost/Program.cs` to `AddScoped<IImportPersistence, ImportRepository>()`, and update every feature-001 test call site that currently passes a raw `LootSinglesDbContext` where `IImportPersistence` is expected (`ImportTestSupport.CreateService`, `ValidImportTests`, `ProgressReportingTests`, `DuplicateOrderTests`'s `CoordinatedPersistence` decorator, `AtomicPersistenceTests`) to go through `ImportRepository` instead. Behavior-preserving refactor (constitution Principle IV's Refactor step; Principle XIII's consolidation MUST once a second concrete use case — this feature's own repository pattern — showed the codebase already has an established shape for this) — the full existing feature-001 Import test suite MUST remain green throughout, with no new test required for this task itself.

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Employee Uploads a Packing Slip and Sees What Happened (Priority: P1) 🎯 MVP

**Goal**: An authenticated employee can upload a TCGplayer packing-slip PDF, watch live per-order progress, and see exactly which orders succeeded, which were rejected and why, a distinct summary-mismatch warning when present, and safe-retry guidance for `Failed`/`Interrupted` outcomes.

**Independent Test**: Upload a representative packing-slip PDF with a mix of valid and structurally-broken orders; confirm the employee sees, for that single upload, which orders succeeded, which were rejected, and a specific human-readable reason for each rejection, with no need to inspect the database or logs.

### Tests for User Story 1 ⚠️

> Write these tests FIRST; confirm they FAIL before implementation begins.

- [X] T005 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerAuthTests.cs`: `POST /api/imports` without a session returns `401` and invokes no import processing.
- [X] T006 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerValidationTests.cs`: missing/multiple `file` fields or malformed multipart return `400`; non-PDF content type returns `415`; a file just over 25 MB (`26,214,400` bytes) returns `413` before the import service runs and creates no orders; a file exactly at the 25 MB boundary is accepted and evaluated normally.
- [X] T007 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerStreamingTests.cs`: uploading `partial-batch-one-bad-order.pdf` streams `application/x-ndjson` where `InProgress` snapshots are emitted only as each order finishes (never for parser-internal page/line/field activity), followed by exactly one terminal `Completed` snapshot whose results show the valid orders succeeded and the invalid order rejected with its specific typed reason (FR-003, FR-004, FR-005, FR-014).
- [X] T008 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerScaleTests.cs`: using a synthetic ~200-order parser test double (mirroring feature 001's `ProgressReportingTests.SyntheticProgressiveParser`) registered for this test only, `POST /api/imports` streams progressive `InProgress` snapshots as orders complete and exactly one terminal `Completed` snapshot reporting all 200 orders succeeded — verifying the NDJSON transport/coalescing layer itself (not just the underlying `PackingSlipImportService`, which feature 001 already covers at this scale) behaves correctly for the largest expected real-world batch (FR-003, SC-003).
- [X] T009 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerDuplicateAndWarningTests.cs`: uploading `valid-multi-order-batch.pdf` twice reports the second run's orders as typed `duplicateOrder` rejections; uploading `corrupted-file.pdf` yields a terminal `Completed` snapshot with an `unreadablePdf` attempt code and zero successful results; uploading `summary-mismatch.pdf` yields a terminal `Completed` snapshot carrying a distinct `summaryMismatch` batch-level warning alongside whatever per-order results were produced (FR-005, FR-006, FR-013).
- [X] T010 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerFailureTests.cs`: with a test double that throws a non-cancellation exception after at least one order has committed, the response ends with a terminal `Failed` snapshot that retains the completed-order counters/results, and whose `operationFailureMessage` is populated, non-null only here, and contains no exception/stack-trace/customer detail (FR-007, FR-019).
- [X] T011 [P] [US1] Integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerCancellationTests.cs`: aborting the HTTP request mid-stream after at least one order has committed propagates cancellation into `ImportAsync`, leaves no partial `Order`/`OrderLine` graph, and a subsequent re-upload of the same PDF reports the already-committed order as a duplicate while importing every remaining valid order exactly once (FR-015, FR-016, SC-008).
- [X] T012 [P] [US1] Unit tests in `frontend/tests/import/importApi.test.ts` for the NDJSON decoder: state is replaced (not merged) per line; a terminal `Completed` or `Failed` line ends decoding; a stream that ends without a terminal line yields a client-only `Interrupted` snapshot retaining the last counters/results as explicitly incomplete/stale; non-2xx responses (`400`/`413`/`415`/`401`/`500`) are mapped to specific, distinguishable messages instead of being parsed as NDJSON.
- [X] T013 [P] [US1] Component tests in `frontend/tests/import/ImportPage.test.tsx` (mocking `importApi`, following `DashboardPage.test.tsx`'s pattern) covering: live progress display; per-order success/reject results with distinct reasons; the summary-mismatch warning banner; the unreadable-file message; the terminal-`Failed` banner with retained partial results and a retry action; the `Interrupted` banner with incomplete/stale labeling and a retry action; and the file-select/submit flow.

### Implementation for User Story 1

- [X] T014 [US1] Implement `backend/src/LootSingles.Api/Controllers/ImportsController.cs`: `[Authorize]` `POST /api/imports` accepting one `multipart/form-data` `file` field, PDF content-type pre-check, server-enforced 25 MB (`26,214,400`-byte) limit with an explicit request-size configuration (raise Kestrel/`FormOptions` limits above the boundary), and Problem Details responses for `400`/`415`/`413` before invoking the import service. Makes T005 and T006 pass.
- [X] T015 [US1] Implement the NDJSON streaming/coalescing transport adapter in `ImportsController.cs`: set `Content-Type: application/x-ndjson`, pass `HttpContext.RequestAborted` into `IPackingSlipImportService.ImportAsync`, suppress updates where `OrdersProcessed` did not increase, emit `InProgress` only when it did, carry the attempt's `attemptFailureCode`/`attemptFailureMessage` (`unreadablePdf`, `summaryMismatch`) through to the snapshot, emit exactly one terminal `Completed` snapshot when the service enumerator completes normally, and flush the response after every line using T001's camelCase JSON options. Makes T007, T008, and T009 pass.
- [X] T016 [US1] Implement terminal `Failed` handling in `ImportsController.cs`: catch a non-cancellation exception raised while iterating the service's async enumerable after streaming has started, write one terminal `Failed` snapshot retaining the last known counters/results with a safe, generic `operationFailureMessage` (no exception message, stack trace, or customer data), and leave a disconnect (cancellation) unhandled so the client derives `Interrupted` instead. Makes T010 pass.
- [X] T017 [P] [US1] Implement `frontend/src/features/import/importApi.ts`: a multipart `POST /api/imports` call whose response body is decoded as NDJSON into a typed `ImportSnapshot` async sequence (status `InProgress`/`Completed`/`Failed`), non-stream error-response mapping for `400`/`413`/`415`/`401`/`500`, and a client-derived `Interrupted` snapshot when the stream/connection ends before a terminal line. Makes T012 pass.
- [X] T018 [US1] Implement `frontend/src/features/import/{ImportPage.tsx,ImportPage.css}`: PDF file picker and submit control, live "N of M orders processed" progress, a per-order outcome list with each rejection's specific reason, the summary-mismatch warning banner, the unreadable-file message, the `Failed` banner (retained partial results + retry), the `Interrupted` banner (incomplete/stale labeling + retry), no customer/shipping data anywhere on the screen (FR-012), a responsive layout with no mobile horizontal scroll (FR-011), and navigation links to the Dashboard and the Orders browse view. Mount `<Route path="/import" element={<ImportPage />} />` in `frontend/src/App.tsx` (built on T002's router shell). Makes T013 pass.
- [X] T019 [US1] Add the Playwright import scenario to `frontend/e2e/order-import.spec.ts` (built on T003's E2EHost wiring): sign in, navigate to the Import screen, upload a fixture PDF (`backend/tests/LootSingles.Fixtures/PackingSlips/partial-batch-one-bad-order.pdf`), observe live progress followed by terminal per-order results with no customer/shipping data, at both a desktop and a mobile viewport with no horizontal overflow (FR-011, SC-005).
- [X] T033 [P] [US1] Add failing component tests before implementation: extend `frontend/tests/dashboard/DashboardPage.test.tsx` to require a prominent application-style "Import Orders" action targeting `/import`, and extend `frontend/tests/import/ImportPage.test.tsx` to require a button-style "Back to Dashboard" control targeting `/` while asserting that no "Browse orders" navigation is rendered. These tests encode the confirmed near-term navigation direction: use focused action controls for the two available screens and defer a shared top navigation or sidebar until additional destinations justify it (supersedes only the navigation-link portion of completed T018; depends on T002 and T018).
- [X] T034 [US1] Implement the tested two-screen navigation: add a prominent responsive "Import Orders" action in `frontend/src/features/dashboard/{DashboardPage.tsx,DashboardPage.css}`; replace `ImportPage.tsx`'s plain Dashboard hyperlink with a clearly styled button-like "Back to Dashboard" control in `frontend/src/features/import/{ImportPage.tsx,ImportPage.css}`; and remove the premature "Browse orders" link until the Orders screen exists. Use React Router navigation semantics underneath, preserve keyboard/focus behavior, and do not introduce a sidebar or shared navigation shell yet (depends on T033).
- [X] T035 [US1] Update `frontend/e2e/order-import.spec.ts` to navigate from Dashboard to Import through the visible "Import Orders" action, verify the button-style return control navigates back to Dashboard, verify no Orders navigation is shown on Import, and retain the existing desktop/mobile import and horizontal-overflow assertions (depends on T034 and T019).

**Checkpoint**: User Story 1 is fully functional and independently testable — an employee can upload a packing slip and see exactly what happened.

---

## Phase 4: User Story 2 - Employee Browses Previously Imported Orders (Priority: P2)

**Goal**: An authenticated employee can open a browse view listing every imported order with its TCGplayer identifier, actual current status, and import timestamp — without re-uploading anything.

**Independent Test**: With one or more orders already present in the system, open the browse view and confirm every one of those orders appears with its TCGplayer order identifier, status, and import timestamp.

### Tests for User Story 2 ⚠️

> Write these tests FIRST; confirm they FAIL before implementation begins.

- [X] T020 [P] [US2] Unit tests in `backend/tests/LootSingles.UnitTests/Orders/OrdersServiceTests.cs` (following `DashboardServiceTests.cs`'s fake-repository pattern): the service returns an empty list when the repository has no orders, and otherwise returns the repository's projected list items unmodified.
- [X] T021 [P] [US2] Integration tests in `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: `GET /api/orders` without a session returns `401`; with no orders returns `200` and `[]`; with orders present returns every one sorted by `importedAt` descending then `tcgplayerOrderId` ascending, each item containing exactly `orderId`/`tcgplayerOrderId`/`status`/`importedAt` and never order lines, customer name/address/contact, or import-attempt details (FR-008, FR-009, FR-012).
- [X] T022 [P] [US2] Component tests in `frontend/tests/orders/OrdersPage.test.tsx` (mocking `ordersApi`, following `DashboardPage.test.tsx`'s pattern): renders every returned order with its identifier/status/import time, shows a clear empty-state message when the list is empty, and shows a distinct error state when the load fails.

### Implementation for User Story 2

- [X] T023 [P] [US2] Create `backend/src/LootSingles.Application/Orders/OrderListItem.cs`: `public sealed record OrderListItem(int OrderId, string TcgplayerOrderId, OrderStatus Status, DateTimeOffset ImportedAt)`, per data-model.md's read projection.
- [X] T024 [US2] Create `backend/src/LootSingles.Application/Orders/IOrderRepository.cs` with `Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken)` (depends on T023).
- [X] T025 [P] [US2] Create `backend/src/LootSingles.Application/Orders/OrdersService.cs` delegating to `IOrderRepository.GetAllAsync` (depends on T024). Makes T020 pass.
- [X] T026 [P] [US2] Implement `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs`: `AsNoTracking()` query projecting only id/identifier/status/import time (no `OrderLines` load), materializing before sorting by `ImportedAt` descending then `TcgplayerOrderId` ascending (same SQLite-`DateTimeOffset` pattern as `DashboardRepository.GetReadyOrderSummariesAsync`) (depends on T024).
- [X] T027 [US2] Implement `backend/src/LootSingles.Api/Controllers/OrdersController.cs`: `[Authorize]` `GET /api/orders` mapping `OrdersService`'s list items to response records matching contracts/orders-api.md (depends on T025). Makes T021 pass.
- [X] T028 [US2] Register `IOrderRepository`/`OrderRepository` and `OrdersService` in `backend/src/LootSingles.Api/Program.cs` **and** in `backend/tests/LootSingles.E2EHost/Program.cs` (completing T003's deferred registration now that these types exist), so both the real API and the Playwright E2E host expose `GET /api/orders` (depends on T026, T027).
- [X] T029 [P] [US2] Implement `frontend/src/features/orders/ordersApi.ts`: `getOrders(): Promise<OrderListItem[]>` fetching `GET /api/orders` with credentials, matching `dashboardApi.ts`'s pattern.
- [X] T030 [US2] Implement `frontend/src/features/orders/{OrdersPage.tsx,OrdersPage.css}`: stacked-card layout on narrow viewports and a denser grid/table layout on wide viewports (no horizontal scroll, FR-011), each order's actual stored status shown verbatim (never fabricated/defaulted, FR-009), a clear empty-state message, loading/error states, no customer/shipping data (FR-012), and navigation links to the Dashboard and the Import screen. Mount `<Route path="/orders" element={<OrdersPage />} />` in `frontend/src/App.tsx` (depends on T002, T022, T029). Makes T022 pass.
- [X] T031 [US2] Extend `frontend/e2e/order-import.spec.ts`: after importing an order, open the Browse Orders view and confirm it appears (FR-010) alongside any pre-seeded order with identifier/status/import time and no customer/shipping data, at both a desktop and a mobile viewport with no horizontal overflow (depends on T030, T019, T028).

**Checkpoint**: User Stories 1 AND 2 both work independently — an employee can import orders and separately confirm what has been imported.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T032 Run full quickstart.md validation: `dotnet build backend/LootSingles.sln`, `dotnet test backend/LootSingles.sln`, `npm --prefix frontend test`, `npm --prefix frontend run build`, `npm --prefix frontend run test:e2e`; confirm all nine quickstart.md end-to-end scenarios pass.

---

## Phase 6: Code Review Follow-ups

**Purpose**: Resolve the Must Fix behavior defects and verification gaps found during the full-feature code review. For behavior corrections, preserve strict Red → Green → Refactor ordering.

- [X] T036 [P] [US1] Add a failing integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerDuplicateAndWarningTests.cs` proving that a packing slip with a `summaryMismatch` still evaluates and persists its valid orders, returns their per-order results and counters, and carries the distinct batch warning alongside those results (FR-005, FR-013). The test MUST fail against the current early-exit behavior before T037 begins.
- [X] T037 [US1] Correct summary-mismatch handling in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`: retain `AttemptFailureCode = SummaryMismatch` and its warning message, continue processing every parsed order block normally, persist valid orders independently, and emit the warning alongside the final per-order results rather than completing with zero processed orders. Make T036 pass without weakening existing duplicate, unreadable-file, or partial-import behavior (depends on T036).
- [X] T038 [P] [US1] Add a failing integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerFailureTests.cs` with an import-service double that throws before yielding any snapshot; assert `POST /api/imports` returns `500` Problem Details with a safe generic message and no NDJSON terminal record or exception/customer detail. Retain the existing after-progress test proving that a failure after response streaming begins produces terminal `Failed` (FR-007, FR-019). The new test MUST fail against the current all-exceptions-to-`Failed` behavior before T039 begins.
- [X] T039 [US1] Split pre-stream and mid-stream failure handling in `backend/src/LootSingles.Api/Controllers/ImportsController.cs`: when an infrastructure exception occurs before the response has started, return `500` Problem Details; once NDJSON streaming has started, retain the terminal `Failed` snapshot behavior and partial results. Never emit false `Completed`, exception text, stack traces, or customer data. Make T038 pass (depends on T038).
- [X] T040 [P] [US1] Replace the reflection-only test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerCancellationTests.cs` with the planned HTTP cancellation integration scenario: abort after at least one order commits, prove `HttpContext.RequestAborted` reaches `ImportAsync`, verify the active order leaves no partial `Order`/`OrderLine` graph, then re-upload and assert the committed order is reported as a typed duplicate while every remaining valid order imports exactly once (FR-015, FR-016, SC-008).
- [X] T041 [US1] Make the cancellation/retry integration scenario from T040 pass, changing `backend/src/LootSingles.Api/Controllers/ImportsController.cs`, the import application service, or persistence adapter only if the new behavioral test exposes a defect; preserve committed per-order transactions and database-authoritative duplicate safety (depends on T040).
- [X] T042 [P] [US1] Replace the three-order line-count assertion in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerScaleTests.cs` with the specified synthetic approximately 200-order `IPackingSlipImportService` test double; assert strictly increasing `ordersProcessed` snapshots, suppression of parser-only/non-increasing updates, all 200 successes, and exactly one terminal `Completed` snapshot (FR-003, SC-003).
- [X] T043 [P] [US1] Expand `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerValidationTests.cs` to cover multiple `file` fields, an incorrectly named file field, malformed multipart input, and files exactly at and one byte above `26,214,400` bytes. Use an import-service spy and database assertions to prove invalid/oversized requests never invoke processing or create orders, while the exact-boundary request reaches normal parser evaluation (FR-018, SC-009).
- [X] T044 [P] [US1] Strengthen `frontend/e2e/order-import.spec.ts` so both desktop and mobile runs observe at least one visible intermediate progress state before terminal completion, then retain the existing final per-order outcomes, browse continuity, PII-exclusion, and horizontal-overflow assertions (FR-003, SC-005).
- [X] T045 Run the complete quickstart validation after T037, T039, and T041–T044: backend build/tests, frontend tests/build, and the full Playwright suite. Confirm all nine scenarios now have direct automated evidence, formatter/lint checks remain clean apart from documented pre-existing warnings, and mark the review follow-up phase complete.

---

## Phase 7: Full-Feature Review Follow-ups

**Purpose**: Close the remaining cancellation, backend verification, and information-disclosure issues found during the final full-feature review.

- [X] T046 [US1] Supersede the production-path verification claim of T040/T041 by replacing the first-request `IPackingSlipImportService` test double in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerCancellationTests.cs` with the production `PackingSlipImportService`. Keep `ImportRepository` authoritative by wrapping/decorating `IImportPersistence` only in the test host: delegate every real operation to `ImportRepository` and pause immediately before the second order's `SaveChangesAsync`; use a deterministic parser as the other below-service test control. Abort the real HTTP request after receiving the first progress snapshot, then assert that the first order and its complete `OrderLine` graph remain committed, the paused order leaves no partial graph, and a retry through the production service reports the committed order as `duplicateOrder` while importing every remaining valid order exactly once (FR-015, FR-016, SC-008). T040/T041 remain historical evidence that HTTP cancellation reaches a service double, but no longer serve as production-service transaction evidence.
- [X] T047 [P] [US1] Add failing application/integration tests for parser exceptions containing a distinctive secret value. Assert the terminal `unreadablePdf` result uses exactly `The supplied PDF could not be read as a TCGplayer packing slip.` and that the secret appears in neither the HTTP response, persisted `ImportAttempt.AttemptFailureMessage`, nor captured application logs. Assert structured logging retains only safe diagnostics needed to classify the failure, including the exception type and a stable event/message, without recording the exception object, exception message, uploaded content, stack trace, or customer data. The tests MUST fail against the current raw `exception.Message` behavior before T048 begins (FR-006, FR-012).
- [X] T048 [US1] Replace raw parser-exception disclosure in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs` with the exact employee-safe message `The supplied PDF could not be read as a TCGplayer packing slip.` and sanitized structured logging. Inject `ILogger<PackingSlipImportService>` and log only safe classification data such as the exception type and a stable event/message; do not pass the exception object or its message to the logger or place it in persisted/transport-facing fields. Update all production/test construction sites and make T047 pass without changing typed `unreadablePdf` behavior (depends on T047).
- [X] T049 [P] [US1] Add failing frontend API and component tests in `frontend/tests/import/importApi.test.ts` and `frontend/tests/import/ImportPage.test.tsx` for deliberate cancellation. Prove `importPackingSlip` forwards an `AbortSignal` to `fetch` and preserves an intentional `AbortError`; while an import is running, the visible Cancel Import action plus application and browser-history navigation ask for confirmation; refresh/tab-close registers a native `beforeunload` warning; declining leaves the request running; confirming aborts it; navigation occurs only after abort; the on-page Cancel action retains the last snapshot as `cancelled` with safe-retry guidance; and deliberate cancellation never shows "Connection lost." The tests MUST fail before T050 and T051 begin (FR-017, FR-020, FR-021).
- [X] T050 [US1] Extend `frontend/src/features/import/importApi.ts` so `ImportStatus` includes client-only `cancelled` and `importPackingSlip` accepts an `AbortSignal`, passes it to `fetch`, and rethrows/preserves an employee-initiated `AbortError` instead of translating it into `interrupted`. Preserve the current Interrupted derivation for unexpected transport, framing, and premature-EOF failures (depends on T049; FR-017, FR-021).
- [X] T051 [US1] Implement guarded cancellation in `frontend/src/features/import/ImportPage.tsx`: own one `AbortController` per submission/retry; guard application and browser-history navigation; register the browser-native `beforeunload` warning for refresh/tab close only while running; expose a visible Cancel Import action; and abort on confirmed cancellation, confirmed navigation, or final component cleanup. Declining keeps the import and progress stream active; confirmed navigation aborts before leaving; confirmed Cancel stays on the page and derives a `cancelled` snapshot from the last received state, explaining that completed orders remain imported and remaining work stopped while offering safe retry. Ensure settled attempts cannot be aborted by stale controllers and retries always receive a fresh controller (depends on T049, T050; FR-020, FR-021).
- [X] T052 [US1] Extend `frontend/e2e/order-import.spec.ts` with a paced multi-order import proving deliberate cancellation end to end: decline application/browser-history navigation once and observe progress continue; confirm leaving and verify abort occurs before navigation; exercise the browser-native refresh/tab-close guard; confirm already-completed orders remain browsable; and retry to import every remaining order without duplicates. Separately cover on-page Cancel Import, retained partial results, `cancelled` labeling, safe retry, and the absence of the unexpected connection-loss message (depends on T046, T051; FR-015–FR-017, FR-020, FR-021, SC-008).
- [X] T053 [P] [US1] Extend `backend/tools/LootSingles.FixtureGenerator` to generate a deterministic `large-200-order-batch.pdf` containing exactly 200 valid, uniquely identified orders with at least one valid product line each, document the fixture, and commit the generated PDF under `backend/tests/LootSingles.Fixtures/PackingSlips/` (SC-003).
- [X] T054 [US1] Add a production-path HTTP integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerScaleTests.cs` that uploads `large-200-order-batch.pdf` through the real `PdfPigPackingSlipParser`, `PackingSlipImportService`, `ImportRepository`, and database. Assert visible strictly increasing intermediate progress, exactly 200 successful terminal results, 200 uniquely persisted Orders with complete OrderLine graphs, and one terminal `Completed` snapshot; retain T042 as focused transport-coalescing coverage (depends on T053; FR-003, SC-003).
- [X] T055 Run final Phase 7 validation after T046, T048, T052, and T054: `dotnet build backend/LootSingles.sln`, `dotnet test backend/LootSingles.sln`, CSharpier check, `npm --prefix frontend test`, `npm --prefix frontend run build`, `npm --prefix frontend run lint`, `npm --prefix frontend run format:check`, and `npm --prefix frontend run test:e2e`. Confirm cancellation/Interrupted/Failed remain distinct, the real 200-order path passes, all prior quickstart scenarios remain green, and only explicitly documented pre-existing warnings remain.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No tasks — no dependencies.
- **Foundational (Phase 2)**: No dependencies on Setup content — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational (T001–T004) completion. No dependency on User Story 2.
- **User Story 2 (Phase 4)**: Depends on Foundational (T001–T004) completion. Independent of User Story 1's implementation, though its E2E task (T031) conveniently reuses T019's sign-in flow.
- **Polish (Phase 5)**: Depends on both user stories being complete.
- **Code Review Follow-ups (Phase 6)**: Depends on Phase 5 and resolves the review findings before convergence verification.
- **Full-Feature Review Follow-ups (Phase 7)**: Depends on Phase 6. T046 independently strengthens server cancellation evidence; T047 → T048 is a strict Red → Green information-disclosure fix; T049 → T050 → T051 → T052 implements deliberate client cancellation and guarded navigation; T053 → T054 adds real 200-order pipeline evidence; T055 is the final quality gate.

### Within Foundational

- T001, T002, T003 have no dependencies on each other or on T004.
- T004 (consolidating `IImportPersistence`) has no dependency on T001–T003 either, but touches feature-001 files only (`LootSinglesDbContext.cs`, `Program.cs`, `E2EHost/Program.cs`'s Import registration line, and the feature-001 Import test suite) — it's compatible with T003 landing first or in either order since both touch `E2EHost/Program.cs`, just different lines.

### Within User Story 1

- Tests (T005–T013) MUST be written and confirmed failing before implementation (T014–T019) begins; T033 adds the failing navigation component tests before T034 implementation.
- T014 → T015 → T016 (same file, sequential).
- T017 is independent of T014–T016 (different files/layers).
- T018 depends on T013 (its own failing tests) and T017 (the API it calls).
- T019 depends on T018 (the page it drives) and T003 (E2EHost wiring).
- T033 depends on T002 and T018; T034 depends on T033; T035 depends on T034 and T019.

### Within User Story 2

- Tests (T020–T022) MUST be written and confirmed failing before implementation (T023–T031) begins.
- T023 → T024 → {T025, T026} (in parallel) → T027 → T028 (T028 also completes T003's deferred E2EHost Orders registration).
- T029 is independent of the backend chain.
- T030 depends on T022, T029, and T002.
- T031 depends on T030, T019, and T028.

### Within Code Review Follow-ups

- T036 → T037 and T038 → T039 are strict Red → Green behavior-fix chains.
- T040 → T041 verifies and, if necessary, corrects end-to-end cancellation and retry behavior.
- T042, T043, and T044 are independent verification-gap tasks and can run in parallel with T036, T038, and T040.
- T045 depends on T037, T039, and T041–T044.

### Within Full-Feature Review Follow-ups

- T046 can proceed independently because it changes only cancellation-test infrastructure unless the production-path test exposes a real defect.
- T047 → T048 is a strict Red → Green chain.
- T049 supplies the failing frontend tests for T050 and T051; T050 must complete before T051, and T052 validates the completed client/server behavior.
- T053 → T054 supplies the real parser/service/persistence scale verification missing from T042.
- T055 depends on T046, T048, T052, and T054.
- T046, T047, T049, and T053 can be implemented in parallel.

### Parallel Opportunities

- All Foundational tasks (T001, T002, T003, T004) are marked [P] and can run in parallel — different files/concerns, no dependencies among them.
- All User Story 1 test tasks (T005–T013) can be written in parallel — 9 independent files.
- All User Story 2 test tasks (T020–T022) can be written in parallel — 3 independent files.
- T025 and T026 (OrdersService and OrderRepository) can be implemented in parallel once T024 exists.
- T036, T038, T040, T042, T043, and T044 can be implemented in parallel because they affect separate test files and behaviors.
- T046, T047, T049, and T053 can be implemented in parallel because they cover independent server cancellation, parser-error, client cancellation, and scale-fixture concerns.
- User Story 1 and User Story 2 can be implemented in parallel by different developers once Phase 2 is complete.

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all User Story 1 test-writing tasks together (each is an independent file):
Task: "Integration test: unauthenticated POST /api/imports in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerAuthTests.cs"
Task: "Integration test: multipart/size/type validation in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerValidationTests.cs"
Task: "Integration test: progress coalescing and mixed per-order outcomes in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerStreamingTests.cs"
Task: "Integration test: ~200-order transport-layer scale in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerScaleTests.cs"
Task: "Integration test: duplicate retry, unreadable file, summary mismatch in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerDuplicateAndWarningTests.cs"
Task: "Integration test: terminal Failed with safe message in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerFailureTests.cs"
Task: "Integration test: cancellation and duplicate-safe retry in backend/tests/LootSingles.IntegrationTests/ImportUi/ImportsControllerCancellationTests.cs"
Task: "Unit tests: NDJSON decoder and Interrupted derivation in frontend/tests/import/importApi.test.ts"
Task: "Component tests: ImportPage states in frontend/tests/import/ImportPage.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational (T001–T004).
2. Complete Phase 3: User Story 1 (T005–T019 and T033–T035).
3. **STOP and VALIDATE**: Run the User Story 1 slice of quickstart.md's end-to-end scenarios (1, 2, 3, 5, 6, 7, 9) independently.
4. Demo the import screen — this alone unblocks self-serve order entry (the "front door" gap called out in spec.md).

### Incremental Delivery

1. Foundational → both stories unblocked.
2. User Story 1 → validate independently → deploy/demo (MVP).
3. User Story 2 → validate independently (quickstart.md scenario 4) → deploy/demo.
4. Polish (T032) → full quickstart.md validation across both stories.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- Every implementation task's paired test(s) must be written and observed failing first (constitution Principle IV / CLAUDE.md).
- Commit after each task or logical group.
- Stop at each checkpoint to validate a story independently before moving on.
- No customer shipping/PII field appears anywhere in DTOs, responses, or UI for this feature (FR-012) — verify this explicitly while implementing T014–T018, T023, T027, T030.
