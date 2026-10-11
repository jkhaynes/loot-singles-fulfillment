---

description: "Task list for Local Mock Orders and Order Deletion (021)"
---

# Tasks: Local Mock Orders and Order Deletion

**Input**: Design documents from `/specs/021-local-mock-orders/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md, quickstart.md

**Tests**: These tests are **required**. Constitution Principle IV (TDD) applies, and it overrides the template's "tests are optional". Every behavioural task follows the failing test that shows it. Two tasks are refactors instead: moving the synthetic stub (T002–T004) and extracting the dialog (T023). The existing suites, unchanged, are their proof.

**Test data**: All TCGplayer data is **synthetic**, written by hand from the published schema. No live call is ever made, and no real response is ever read (`CLAUDE.md`, TCGplayer API Agreement).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US4 from spec.md

## Path Conventions

The existing web-app layout: `backend/src/`, `backend/tests/`, `frontend/src/`, `frontend/tests/`, `frontend/e2e/`.

---

## Phase 1: Setup

- [X] T001 Confirm a clean baseline on branch `021-local-mock-orders`. Run each of these and record its pass count in this task:
  - `dotnet build backend/LootSingles.sln`
    - 0 warnings, 0 errors
  - both backend test projects
    - UnitTests: 496 passed, 0 failed, 0 skipped
    - IntegrationTests: 327 passed, 0 failed, 0 skipped
  - `npm --prefix frontend run test`
    - 333 passed, 0 failed (20 files)
  - `npm --prefix frontend run build`
    - succeeded, no errors
  - `npm --prefix frontend run lint`
    - 0 errors, 2 pre-existing warnings (AuthContext.tsx only-export-components; PackingDeskPage.tsx set-state-in-effect)
  
  This is the reference that the refactors in Phase 2 and T023 must keep.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### 2a. Move the synthetic stub into Infrastructure (refactor, no behaviour change; research R1)

The 020 TCGplayer suites (`backend/tests/LootSingles.UnitTests/Tcgplayer/`, `…/Import/`, `backend/tests/LootSingles.IntegrationTests/ImportUi/`, `…/Import/`, `…/Packing/TcgplayerPackingTests.cs`) and `frontend/e2e/tcgplayer-import.spec.ts` must stay green after each task, with no assertion edits.

- [X] T002 Move every file in `backend/tests/LootSingles.Fixtures/Tcgplayer/`, including `README.md`, to `backend/src/LootSingles.Infrastructure/Tcgplayer/Synthetic/Data/`. Embed the JSON files as `EmbeddedResource` in `backend/src/LootSingles.Infrastructure/LootSingles.Infrastructure.csproj`, following the CardCatalog precedent at lines 7–13. In the README, add one line saying the data now also feeds local Development runs.
- [X] T003 Move `backend/tests/LootSingles.IntegrationTests/ImportUi/TcgplayerStubHandler.cs` to `backend/src/LootSingles.Infrastructure/Tcgplayer/Synthetic/SyntheticTcgplayerHandler.cs`:
  - Rename the class to `SyntheticTcgplayerHandler` and make it `public sealed`. Its nested `RecordedRequest` becomes `public` too.
  - Read the fixtures from the embedded resources instead of `AppContext.BaseDirectory/Fixtures/Tcgplayer`.
  - Change `BaseUrl` to `https://tcgplayer-synthetic.invalid/`. `.invalid` is a reserved name that never resolves.
  - Keep the rest of the public surface unchanged: `OpenOrderNumbers`, `LeakedOrderNumbers`, `Override`, `Requests`, `OrderNumber`, `Classify`.
- [X] T004 Repoint every consumer at the moved handler and data, and delete the old copies:
  - In `backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj` and `backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj`, remove the Tcgplayer `<None … CopyToOutputDirectory>` links. Tests that read a fixture file directly read it through a small helper on the handler, for example `SyntheticTcgplayerHandler.ReadData(name)`.
  - In `backend/tests/LootSingles.E2EHost/LootSingles.E2EHost.csproj`, remove the `<Compile Include=…TcgplayerStubHandler.cs>` link and the JSON copy.
  - Update the `using` lines and type names in `backend/tests/LootSingles.E2EHost/Program.cs`, `backend/tests/LootSingles.IntegrationTests/ImportUi/ImportUiTestSupport.cs`, `TcgplayerStubHandlerTests.cs` (rename it `SyntheticTcgplayerHandlerTests.cs` and move it to `backend/tests/LootSingles.IntegrationTests/Tcgplayer/`), and every other user found by searching for `TcgplayerStubHandler`.
  - Delete the empty `backend/tests/LootSingles.Fixtures/Tcgplayer/` folder. Confirm the Phase 2a suites are green.
- [X] T005 [P] Write `backend/tests/LootSingles.UnitTests/Tcgplayer/SyntheticDataTests.cs`. Assert that no embedded synthetic resource contains the text `tcgplayer.com` or `tcgplayer-cdn`, and that every image URL host ends in `.test` or `.invalid` (research R4, SC-003). This guards existing behaviour, so it should pass at once. If it fails, fix the data, not the test.

### 2b. Keep Development-only behaviour out of the test hosts (research R2)

- [X] T006 Make `WebApplicationFactory` hosts run as a `Testing` environment by default. In `backend/tests/LootSingles.IntegrationTests/Auth/AuthWebApplicationFactory.cs`, and in any other `WebApplicationFactory<Program>` subclass or `WithWebHostBuilder` helper that doesn't already call `UseEnvironment`, add `builder.UseEnvironment("Testing")`. Confirm the whole integration suite is still green.
  - A test that *needs* Development (US1) sets it explicitly.
  - If any test relied on Development-only behaviour, record what it was here and fix it by configuring the test explicitly. Don't rely on the environment.
  - Check that the E2E host (`backend/tests/LootSingles.E2EHost`, run without a launch profile) still reports Production, and leave it unchanged.

**Checkpoint**: One copy of the synthetic data and handler exists. Every existing suite is green, and the test hosts are not Development.

---

## Phase 3: User Story 1 – Import mock orders locally (Priority: P1) 🎯 MVP

**Goal**: In Development, **Get new orders** imports the fixed synthetic orders through the real import pipeline.

**Independent Test**: Run the app as Development with no keys and press **Get new orders**. Eight SYN orders import. A second press says "8 already imported" (quickstart §B steps 1–4).

- [X] T007 [US1] Write `backend/tests/LootSingles.IntegrationTests/Import/SyntheticTcgplayerSourceTests.cs` with a factory that calls `UseEnvironment("Development")` and sets no TCGplayer keys. Tests:
  - `POST /api/imports/tcgplayer`, run as a Manager/Admin and as a picker, imports exactly SYN-0001…0005 and SYN-0008…0010, with source `TcgplayerApi`.
  - SYN-0011…0013 are not imported.
  - The quantity-2 line of SYN-0002 and the null collector number of SYN-0009 are stored as the translator produces them (FR-003).
  - A second press records all eight as already imported and creates no order (FR-007).
  - The response's outcome codes are the same ones a stubbed 020 import returns (FR-008).
  - SYN-0001 can be claimed, have every line recorded as picked, be found at the packing desk, and be marked packed, through the existing endpoints and the 020 packing test helpers (FR-006, SC-001).
  
  Confirm red: today the response is `tcgplayerNotConfigured`.
- [X] T008 [US1] Add `AddSyntheticTcgplayer(this IHttpClientBuilder builder)` in `backend/src/LootSingles.Infrastructure/Tcgplayer/Synthetic/SyntheticTcgplayerServiceCollectionExtensions.cs`. It sets the primary handler to a `SyntheticTcgplayerHandler` whose `OpenOrderNumbers` are {1,2,3,4,5,8,9,10} (research R3), and it doesn't use `Override`.
  
  In `backend/src/LootSingles.Api/Program.cs`, when `builder.Environment.IsDevelopment()`, add an in-memory configuration source **after all others**, before `AddTcgplayer` reads its options. It holds:
  - `Tcgplayer:PublicKey=synthetic-public-id`
  - `Tcgplayer:PrivateKey=synthetic-private-id`
  - `Tcgplayer:AccessToken=synthetic-store-access`
  - `Tcgplayer:StoreKey=`
  - `Tcgplayer:BaseUrl=<SyntheticTcgplayerHandler.BaseUrl>`
  - `Tcgplayer:PageSize=2`
  
  Then chain `.AddSyntheticTcgplayer()` onto the `AddTcgplayer` call. Log one Information line at startup through `ILogger<Program>`: `"TCGplayer synthetic orders are active (Development)."`. Confirm T007 is green.
- [X] T009 [P] [US1] Write `frontend/tests/import/ImportPage.synthetic.test.tsx`. Mock `importApi` to return a stream shaped like T007's results, and assert the results render with no "mock" or "synthetic" wording (FR-008). If this only re-proves existing behaviour, keep it as a guard and note that in the task. No frontend change is expected. Guard: passed as soon as it was written (no frontend change was needed), so there was no red step.

**Checkpoint**: US1 works locally (quickstart §B steps 1–4).

---

## Phase 4: User Story 2 – Stage and production never use mock data (Priority: P1)

**Goal**: Outside Development, the synthetic handler is never registered, whatever the configuration says.

**Independent Test**: Start the app as Production and as Testing with no keys, plus every setting that might look like a switch. The import says "not set up here".

- [X] T010 [US2] Add tests to `backend/tests/LootSingles.IntegrationTests/Import/SyntheticTcgplayerSourceTests.cs`, with factories using `UseEnvironment("Production")` and `UseEnvironment("Testing")`:
  - With no keys, the import returns `tcgplayerNotConfigured` and creates no order (FR-004, US2 scenario 1).
  - With `Tcgplayer:BaseUrl` set to the synthetic host and fake keys, the TCGplayer `HttpClient`'s primary handler (resolved through `IHttpMessageHandlerFactory` for `TcgplayerServiceCollectionExtensions.HttpClientName`, then unwrapping `DelegatingHandler.InnerHandler`) is **not** a `SyntheticTcgplayerHandler` (US2 scenario 2, SC-002).
  - The startup log does not contain "synthetic orders are active".
  
  These should pass as soon as T008 lands. Write them before T008 if you're working in order, and confirm they fail for the expected reason if the `IsDevelopment()` check is removed.

**Checkpoint**: US2 is proved.

---

## Phase 5: User Story 3 – A local machine never calls the real API (Priority: P1)

**Goal**: In Development, configured keys and a configured `BaseUrl` can't send a request anywhere but the synthetic handler.

**Independent Test**: As Development, with real-looking keys and `Tcgplayer:BaseUrl=https://api.tcgplayer.com/` configured, an import reaches only the synthetic handler.

- [X] T011 [US3] Add tests to `backend/tests/LootSingles.IntegrationTests/Import/SyntheticTcgplayerSourceTests.cs`. As Development, configure (through `UseSetting` and an environment-variable-style in-memory source) `Tcgplayer:PublicKey`, `PrivateKey` and `AccessToken` with non-synthetic values and `Tcgplayer:BaseUrl=https://api.tcgplayer.com/`. Then:
  - the resolved `TcgplayerOptions.BaseUrl` is the synthetic host, and the keys are the synthetic ones;
  - after a press, every request the synthetic handler recorded used the synthetic host, and the import succeeded (FR-005, SC-003).
  
  To reach the handler instance, resolve it the same way as T010. Confirm red by temporarily adding the override *before* the user-secrets and environment sources, then restore T008's ordering.

**Checkpoint**: US1–US3 are complete. The mock-orders MVP is shippable on its own.

---

## Phase 6: User Story 4 – A Manager/Admin deletes orders (Priority: P2)

**Goal**: Soft delete of one order or all orders. Manager/Admin only. Production refuses touched orders.

**Independent Test**: quickstart §B steps 5–6 and §C. As a picker, nothing is offered and the server refuses.

### 6a. Schema and "touched"

- [X] T012 [US4] Write `backend/tests/LootSingles.IntegrationTests/Orders/FirstClaimedAtTests.cs`:
  - Pick Next and claiming a specific order set `FirstClaimedAt`.
  - Release and force-release leave it unchanged.
  - A second claim after a release doesn't move it.
  
  Confirm red: the property doesn't exist yet.
- [X] T013 [US4] Add `FirstClaimedAt`, `DeletedAt` and `DeletedByEmployeeId` to `backend/src/LootSingles.Domain/Orders/Order.cs`, per data-model.md. Then:
  - In `backend/src/LootSingles.Infrastructure/Persistence/Configurations/OrderConfiguration.cs`, add `HasQueryFilter(o => o.DeletedAt == null)`. Make the `TcgplayerOrderId` unique index filtered `[DeletedAt] IS NULL`, extend the `ClaimedByEmployeeId` filter with `AND [DeletedAt] IS NULL`, and give `DeletedByEmployeeId` a Restrict foreign key to `Employees`.
  - In `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs`, set `FirstClaimedAt` in both claim `ExecuteUpdateAsync` setters (`ClaimNextAvailableAsync` and `ClaimSpecificAsync`), as `o.FirstClaimedAt ?? now`.
  - Generate the migration `AddOrderSoftDelete` in `backend/src/LootSingles.Infrastructure/Persistence/Migrations/`. Add a SQL backfill to `Up` for `FirstClaimedAt`, per data-model.md "Backfill".
  
  Confirm T012 is green and the whole suite stays green.
- [X] T014 [US4] Write `backend/tests/LootSingles.IntegrationTests/Persistence/OrderSoftDeleteSchemaTests.cs`. Use a raw `UPDATE` to set `DeletedAt` and `DeletedByEmployeeId` on an order, then assert:
  - the order is absent from `GET /api/orders`, `GET /api/orders/{id}` (404 `order_not_found`), Pick Next, every dashboard count, the packing desk lookup by order number, and `GET /api/orders/{id}/label` (FR-016, FR-018);
  - it is absent from the packing desk's awaiting-packing list (`PackingRepository.GetAwaitingPackingAsync`);
  - claim, release, force-release, record-pick, report-issue, mark-packed and the packing-slip retrieval on it return 404 `order_not_found` (or the packing desk's not-found answer);
  - its lines, pick outcomes, issues and claim fields are unchanged in the database (SC-009);
  - a second order with the same `TcgplayerOrderId` can be inserted;
  - an employee holding the deleted order's claim can claim another order;
  - the `FirstClaimedAt` backfill in the migration marks an order with a recorded pick outcome and leaves an untouched one null. Seed both before the migration step, or call the backfill SQL directly.
  
  Most of this should pass after T013. Fix any reader the global filter misses (for example, a raw SQL query) test-first within this task.

### 6b. Deletion rules and service

- [X] T015 [P] [US4] Write `backend/tests/LootSingles.UnitTests/Orders/OrderDeletionServiceTests.cs` against a fake `IOrderDeletionRepository`:
  - it maps repository outcomes to `OrderDeletionResult.Deleted`, `NotFound`, `AlreadyDeleted` and `ClaimedBefore`;
  - it passes `AllowTouchedOrders` from `OrderDeletionOptions`, with a default of `false`;
  - it uses `TimeProvider` for `DeletedAt`;
  - it logs one Information entry per deletion, with `ManagerId`, `OrderId` and `TcgplayerOrderId`, and one summary entry for delete-all, with `DeletedCount` and `KeptCount`;
  - it logs no customer fields;
  - `DeleteAllAsync` rejects an `asOf` later than now with `InvalidAsOf`.
  
  Confirm red.
- [X] T016 [US4] Create, in `backend/src/LootSingles.Application/Orders/`:
  - `OrderDeletionOptions.cs`: `AllowTouchedOrders`, bound from the `OrderDeletion` section.
  - `IOrderDeletionRepository.cs`, with these members:
    - `DeleteAsync(orderId, actorId, now, allowTouched, ct)` returns an outcome plus the `TcgplayerOrderId`;
    - `DeleteAllAsync(actorId, now, asOf, allowTouched, ct)` returns the deleted and kept counts;
    - `GetSummaryAsync(now, allowTouched, ct)`.
  - `OrderDeletionService.cs`, with its result types.
  
  Confirm T015 is green.
- [X] T017 [US4] Write `backend/tests/LootSingles.IntegrationTests/Orders/OrderDeletionRepositoryTests.cs` on Testcontainers SQL:
  - With `allowTouched=false`, an untouched order is deleted. Orders that were claimed, claimed then released, picked, have an issue, or are packed are refused as `ClaimedBefore`, and their rows are unchanged (FR-013, FR-020, SC-008).
  - With `allowTouched=true`, all of them are deleted, and the claim fields stay (FR-016).
  - After a single delete and after delete-all, every deleted row has `DeletedAt` equal to the injected `TimeProvider` time and `DeletedByEmployeeId` equal to the acting Manager/Admin (FR-015, SC-007).
  - A deleted order's `OrderPackingSlips` row is gone, and its `PackingSlipAccesses` rows remain.
  - Deleting a deleted order returns `AlreadyDeleted`. An unknown id returns `NotFound`.
  - **Race**: run the claim and the delete of one untouched order concurrently with `allowTouched=false`, 20 times. Each time, exactly one of them succeeds (research R7).
  - `DeleteAllAsync` deletes only deletable orders imported at or before `asOf`, leaves an order imported after `asOf`, and returns correct counts.
  - `GetSummaryAsync` returns `deletableCount`, `keptCount`, `claimedCount` and `withUnresolvedIssueCount` as defined in contracts/api.md.
  
  Confirm red.
- [X] T018 [US4] Implement `backend/src/LootSingles.Infrastructure/Persistence/OrderDeletionRepository.cs`:
  - One transaction per call.
  - A conditional `ExecuteUpdateAsync` setting `DeletedAt` and `DeletedByEmployeeId`. The condition is in research R7.
  - `ExecuteDeleteAsync` on `OrderPackingSlips` for the orders just marked.
  - On a refusal, a re-read with `IgnoreQueryFilters()` classifies it.
  - Follow the transaction shape of `OrderRepository.ExecuteConditionalUpdateAsync`.
  
  Register the repository, the service and the bound options in `backend/src/LootSingles.Api/Program.cs` and `backend/tests/LootSingles.E2EHost/Program.cs`. Add `"OrderDeletion": { "AllowTouchedOrders": true }` to `backend/src/LootSingles.Api/appsettings.Development.json`. Do not add it to `appsettings.json`. Confirm T017 is green.

### 6c. Endpoints

- [ ] T019 [US4] Write `backend/tests/LootSingles.IntegrationTests/Orders/OrderDeletionEndpointTests.cs` per contracts/api.md:
  - `DELETE /api/orders/{id}` returns 204, 404 `order_not_found`, 404 `order_already_deleted` or 409 `order_claimed_before`.
  - `GET /api/orders/deletion-summary` returns its shape.
  - `POST /api/orders/delete-all` returns `{ deletedCount, keptCount }`, or 400 `invalid_as_of` when `asOf` is missing or in the future.
  - All three return 403 for a picker (FR-010, SC-006).
  - `GET /api/orders` returns `canDelete`/`deleteBlockedReason` correctly for a Manager/Admin with each setting value, and `canDelete=false` with a null reason for a picker.
  - Re-import after delete: delete an imported order, then re-import it through the PDF path (an existing fixture slip) and through the API path (stub). Each creates a new visible order, and the deleted row is unchanged (FR-017).
  
  Confirm red.
- [ ] T020 [US4] In `backend/src/LootSingles.Api/Controllers/OrdersController.cs`, add the three endpoints with `[Authorize(Roles = nameof(EmployeeRole.ManagerAdmin))]`. Also:
  - Add `CanDelete` and `DeleteBlockedReason` to `backend/src/LootSingles.Application/Orders/OrderListItem.cs`, and compute them in `OrderRepository.GetAllAsync`'s projection from the caller's role and `OrderDeletionOptions`.
  - Map `OrderDeletionResult` to the contract's status codes.
  
  Confirm T019 is green.

### 6d. Frontend

- [ ] T021 [P] [US4] Write `frontend/tests/orders/ordersApi.deletion.test.ts` for the new functions in `ordersApi.ts`:
  - `getDeletionSummary()`;
  - `deleteOrder(id)`, which throws `OrderNotFoundError`, `OrderAlreadyDeletedError` or `OrderClaimedBeforeError` by `error` code;
  - `deleteAllOrders(asOf)`.
  
  Also cover the new `OrderListItem` fields. Confirm red.
- [ ] T022 [US4] Implement T021 in `frontend/src/features/orders/ordersApi.ts`, following the `claimOrder` pattern (lines 325–351). Confirm green.
- [ ] T023 [US4] Extract `frontend/src/components/ConfirmationDialog.tsx` and `ConfirmationDialog.css` from the `import-confirmation` markup and CSS in `frontend/src/features/import/ImportPage.tsx` (lines 272–311) and `ImportPage.css` (lines 202–232). It takes only what the two existing uses need: `title`, `children` (the body), `keepLabel`, `confirmLabel`, `onKeep` and `onConfirm`. The dialog uses `role="alertdialog"`. Move ImportPage's two confirmations onto it. **This is a pure refactor with no new behaviour**: `frontend/tests/import/` must stay green with no assertion edits.
- [ ] T024 [US4] Write `frontend/tests/components/ConfirmationDialog.test.tsx`:
  - The labels render as given.
  - Focus starts on the keep button, not the confirm button (FR-022).
  - The confirm button has the destructive styling class when `destructive`.
  - With `confirmWord="DELETE"`, the confirm button stays disabled until exactly `DELETE` is typed (FR-023).
  - Keep and confirm each call their handler once.
  
  The label and handler cases pass against T023's extraction. Confirm that the focus, `destructive` and `confirmWord` cases are red.
- [ ] T025 [US4] Implement T024's red cases in `frontend/src/components/ConfirmationDialog.tsx` and `ConfirmationDialog.css`:
  - add the `destructive`, `confirmWord?` and `busy?` props;
  - focus starts on the keep button when the dialog opens;
  - while `confirmWord` isn't typed exactly, the confirm button is disabled.

  ImportPage's two uses don't pass the new props, so their behaviour is unchanged. Confirm T024 is green and `frontend/tests/import/` still is.
- [ ] T026 [US4] Write `frontend/tests/orders/OrdersPage.deletion.test.tsx`. Wrap it in `AuthProvider`, and mock `authApi.me` the way `OrderDetailPage.test.tsx:30,83` does. Tests:
  - A picker sees no ⋯ delete option and no **Delete orders…** (FR-010).
  - A Manager/Admin's ⋯ menu offers **Delete order…** when `canDelete`, and shows "Can't delete on production: this order has been claimed, so its pick history is kept." when `deleteBlockedReason === 'claimed_before'` (FR-021).
  - The single confirmation reads "Delete order SYN-…?", with the claim warning only when `claimedByEmployeeName` is set and the issue warning only when `status === 'NeedsAttention'` (FR-014, FR-022).
  - **Delete order** removes the row. `OrderAlreadyDeletedError` also removes the row and shows "That order was already deleted." `OrderClaimedBeforeError` shows "Someone claimed this order, so it can't be deleted." and keeps the row.
  - **Delete orders…** calls `getDeletionSummary`, shows "Delete N orders?" with the kept count when it's greater than 0, requires `DELETE`, calls `deleteAllOrders(asOf)`, then reloads the list and shows "N orders deleted" (plus "· M kept" when M > 0).
  
  Confirm red.
- [ ] T027 [US4] Implement T026:
  - Create `frontend/src/features/orders/OrderRowMenu.tsx` (a ⋯ button with a small popover, closed by Escape or an outside click).
  - In `frontend/src/features/orders/OrdersPage.tsx` and `OrdersPage.css`: add `useAuth`, the Actions-cell menu, a quiet **Delete orders…** header button for Manager/Admins, both `ConfirmationDialog` uses, and the result message.
  
  Use the copy from the mockup and spec (FR-021–FR-023). Confirm green.
- [ ] T028 [US4] Write tests in `frontend/tests/orders/OrderDetailPage.test.tsx`. When `recordPicked`, `reportIssue`, `releaseOrder`, the finish action or `getOrderLabel` rejects with `OrderNotFoundError`, the page shows the not-found state "This order no longer exists. It may have been deleted." and no longer shows the lines (FR-018, US4 scenario 6). Confirm red.
- [ ] T029 [US4] Implement T028 in `frontend/src/features/orders/OrderDetailPage.tsx`. In each action handler's catch block, an `OrderNotFoundError` sets `loadState` to `'not-found'`. Update the not-found text (line ~441) to the wording in T028. Confirm green, and confirm the existing detail-page tests are still green.

### 6e. End to end

- [ ] T030 [US4] Write `frontend/e2e/order-deletion.spec.ts`. In `backend/tests/LootSingles.E2EHost/Program.cs`, seed its own orders (`E2E-DELETE-000NN`), one untouched and one claimed by a dedicated new picker, and set `OrderDeletion:AllowTouchedOrders` to `false` in the host's in-memory configuration, so the production rule is the one exercised end to end. Scenarios:
  - As `e2emanager`, the claimed order's ⋯ menu says it can't be deleted.
  - Deleting the untouched order through the menu and confirmation removes it from the list.
  - As the picker, the ⋯ menu offers no delete.
  
  Use a fresh browser context per user (`order-claiming.spec.ts:4-17` pattern). Don't run **Delete orders…** in E2E: it would remove other specs' orders under `fullyParallel`. Its behaviour is covered by T017, T019 and T026. Confirm the spec passes alongside the full E2E suite.

**Checkpoint**: US4 is complete (quickstart §A, §B steps 5–6, §C).

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T031 Update the docs:
  - In `docs/state.md`, if it exists, say what 021 adds.
  - In `specs/020-tcgplayer-api-import/quickstart.md` §C, replace "Without secrets, 'Get new orders' shows the not set up here message" with a pointer to 021: locally it imports synthetic orders.
  - Any doc that references `backend/tests/LootSingles.Fixtures/Tcgplayer` now points to the new data folder. Search the repo for that path.
- [ ] T032 Run the full suites (both backend test projects, plus frontend test, build, lint and `test:e2e`) and record the counts here, compared with T001.
- [ ] T033 Walk through quickstart §B and §C on LocalDB, using the connection-string override from the local-DB setup note, never the developer's own dev database. Record what was seen.
- [ ] T034 [MANUAL] **Before merging to `main`** (a merge deploys to stage), add `OrderDeletion__AllowTouchedOrders=true` to the stage Container App (quickstart §D). Until it's set, stage applies the strict production rule, which is safe but doesn't match FR-013 for stage. After the stage deploy, check that a claimed order can be deleted on stage. After the production deploy, check that a claimed order can't be deleted on production (quickstart §E).

---

## Dependencies & Execution Order

- **Setup (T001)** comes first.
- **Foundational**:
  - 2a (T002 → T003 → T004) runs in sequence. T005 can run in parallel once T002 is done.
  - 2b (T006) follows T004.
- **US1 (T007–T009)** needs Phase 2. **US2 (T010)** and **US3 (T011)** need T008. US1–US3 are one deliverable, the MVP.
- **US4** needs Phase 2b only, not US1–US3. Within US4:
  - T012 → T013 → T014;
  - T015 → T016 → T017 → T018;
  - T019 → T020;
  - frontend: T021 → T022, then T023 → T024 → T025, then T026 → T027, then T028 → T029;
  - T030 comes last.
- **Polish (T031–T034)** comes after the stories.

## Parallel Example: User Story 4

```text
# After T013:
T015 deletion service unit tests   ∥  T021 ordersApi deletion tests
T023 ConfirmationDialog extraction ∥  T028 OrderDetailPage not-found tests
```

## Implementation Strategy

1. **MVP**: Phases 1–5. Mock orders work locally, and they can't reach stage, production or the real API. This can merge on its own.
2. **Add US4**: schema → rules → endpoints → UI → E2E.
3. **Polish**, code review (requesting-code-review), the stage setting (T034), then merge.
