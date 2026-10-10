---

description: "Task list for TCGplayer API Order Import (020)"
---

# Tasks: TCGplayer API Order Import

**Input**: Design documents from `/specs/020-tcgplayer-api-import/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (import-api.md, tcgplayer-upstream.md, configuration.md), quickstart.md, tcgplayer-setup.md

**Tests**: These are included and **required**. Constitution Principle IV (TDD, non-negotiable) requires Red → Green → Refactor for every new or modified behaviour, which overrides the template's "tests are optional". Every behavioural task is preceded by its failing test. Phase 2's extraction is the one exception: it is a refactor, and the existing PDF import suite, unchanged, is its proof.

**Test data**: All TCGplayer data in tests is **synthetic**, written by hand from TCGplayer's published schema (FR-024; Legacy Addendum §4f; `CLAUDE.md`). No live response, real order, real key or real token appears anywhere in the repository. No AI agent makes live TCGplayer calls.

**Live confirmation done (2026-10-09)**: the probe confirmed every assumption tagged **⚠ LIVE** below: the open status "Ready To Ship", `productCount` counting units, the `extendedData` name `Number`, and the condition and printing wording (research.md §14, T061). The tags are kept to show where each assumption lives in the code.

**Organization**: Setup first, then the Foundational phase. That phase extracts the shared import core, makes the schema changes, and builds the TCGplayer client with its agreement guards, all of which US1 needs. The user stories follow in priority order: US1 (P1), US2 (P2), US3 and US4 (P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1–US4 from spec.md

## Path Conventions

Existing web-app layout: `backend/src/`, `backend/tests/`, `frontend/src/`, `frontend/tests/`, `frontend/e2e/`. New TCGplayer adapter code lives in `backend/src/LootSingles.Infrastructure/Tcgplayer/`, and its unit tests in `backend/tests/LootSingles.UnitTests/Tcgplayer/`.

---

## Phase 1: Setup

- [x] T001 Confirm a clean baseline on branch `020-tcgplayer-api-import`. Run `dotnet build backend/LootSingles.sln`, both backend test projects, `npm --prefix frontend run test`, `npm --prefix frontend run build` and `npm --prefix frontend run lint`, and record the pass counts here. This is the "existing tests pass" reference for the Phase 2 refactor. **Baseline 2026-10-09**: build OK (0 warnings); unit 262 passed; integration 252 passed (0 skipped); frontend test 269 passed (18 files); build OK; lint OK (0 errors, 2 warnings).
- [x] T002 [P] Create `backend/tests/LootSingles.Fixtures/Tcgplayer/README.md`. It states that every file in the folder is synthetic, written from TCGplayer's published v1.39.0 schema (contracts/tcgplayer-upstream.md), and why: §4f, no API data in AI tools. Link the folder into both test projects the same way `Fixtures/PackingSlips` is linked, in `backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj` and `backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj`.
- [x] T003 [P] Write the synthetic fixtures in `backend/tests/LootSingles.Fixtures/Tcgplayer/`. Use invented order numbers (`SYN-0001-…`), SKUs, product ids and card names throughout:
  - `token.json`, with `access_token`, `.expires` and `expires_in`
  - `stores-self.json`
  - `manifest.json`, whose `orderStatusTypes` include `Ready To Ship` and others
  - `search-page1.json` and `search-page2.json`, which page through `totalItems`
  - `order-details.json`: **include invented `customer`, `shippingAddress` and `orderValue` objects**, so tests can prove they never persist (SC-004)
  - `items-*.json`, covering:
    - a normal order;
    - a line with quantity greater than 1;
    - a foil line;
    - a non-English line;
    - an order whose items page in two;
    - an order whose `productCount` mismatches its quantities;
    - a line missing `productName`
  - `skus.json`
  - `products.json`: one product without a `Number` extendedData entry, and one SKU or product listed as not found

**Checkpoint**: The baseline is recorded and the synthetic fixtures exist.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### 2a. Extract the source-neutral import core (refactor, no behaviour change)

Research.md §8 and data-model.md cover this. The existing PDF suites in `backend/tests/LootSingles.UnitTests/Import/`, `backend/tests/LootSingles.IntegrationTests/Import/` and `…/ImportUi/` must stay green after **every** task here, with no assertion edits. Only construction may change.

- [x] T004 Add the `OrderCandidate` and `OrderLineCandidate` records, with fields exactly as in data-model.md, including `RejectedBySource`, to `backend/src/LootSingles.Application/Import/OrderCandidate.cs`.
- [x] T005 Write unit tests for `OrderCandidateValidator` in `backend/tests/LootSingles.UnitTests/Import/OrderCandidateValidatorTests.cs`. They cover each rule and its existing `FailureType` and message, in data-model.md's order:
  - `MissingOrderIdentifier`
  - `NoProductLines`
  - `InvalidQuantity` (null or ≤ 0)
  - `MissingProductName`
  - `MissingSet`
  - `MissingCondition`

  Also assert that a candidate with a **null `CollectorNumber` is valid**, and that a candidate with `RejectedBySource` set returns that rejection without running the rules. Confirm red.
- [x] T006 Implement `OrderCandidateValidator` in `backend/src/LootSingles.Application/Import/OrderCandidateValidator.cs`, reusing the existing failure messages verbatim. Confirm T005 is green.
- [x] T007 Change `OrderLineExtractor` in `backend/src/LootSingles.Application/Import/OrderLineExtractor.cs` so a `RawOrderBlock` becomes an `OrderCandidate`, with `RawDescription` kept verbatim. The PDF-only "slip line must have a collector number" check stays here and becomes `RejectedBySource(MissingCollectorNumber, <existing message>)`. Fold the text-quantity parsing from `OrderLineValidator.cs` into the extractor, so an unparseable quantity becomes `Quantity = null`. Delete `OrderLineValidator.cs` once nothing uses it. Update `OrderLineExtractionTests.cs` and `OrderValidationTests.cs` so they target the extractor plus the validator. Assertions about outcomes, failure types and messages stay identical.
- [x] T008 Extract `OrderImporter` from `PackingSlipImportService.cs` (L105–184 and helpers) into `backend/src/LootSingles.Application/Import/OrderImporter.cs`:
  - `Task<ImportOrderResult> ImportAsync(ImportAttempt attempt, OrderCandidate candidate, Action<Order>? beforeSave, CancellationToken ct)`. The source parameter is added in T014.
  - It runs: validate, pre-check for a duplicate, create the `Order`, call `beforeSave`, `AddOrder` and `SaveChangesAsync`, catch `UniqueConstraintViolationException` (DuplicateOrder, "concurrent operation") and `OrderPersistenceException` (PersistenceFailure), and save the `ImportOrderResult`.
  - Behaviour and messages are unchanged. Register it as scoped in `backend/src/LootSingles.Api/Program.cs` **and** `backend/tests/LootSingles.E2EHost/Program.cs`.
- [x] T009 Extract `LogCompletion` from `PackingSlipImportService.cs` into `backend/src/LootSingles.Application/Import/ImportAttemptLog.cs`, with an unchanged template. The existing `ImportLoggingTests.cs` assertions must still pass.
- [x] T010 Make `PackingSlipImportService` (`backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`) use `OrderLineExtractor` and `OrderImporter` per block, passing a `beforeSave` that attaches the sliced slip. Parsing, the summary-mismatch check and slicing stay here.
  - Also check the explorer's lead on `ImportRepository.DiscardOrder` (`backend/src/LootSingles.Infrastructure/Persistence/ImportRepository.cs`): does it detach `order.PackingSlip` after a duplicate race? If a failing test proves a leak, fix it test-first. If it doesn't leak, note that here.
  - **Done**: wiring landed in T008 and was confirmed (parse, summary-mismatch check and slicing stay in the service; `OrderLineExtractor` + `OrderImporter` per block with a slip-attaching `beforeSave`). `DiscardOrder` does not leak: `DiscardOrderTests` proves the slip is tracked after `AddOrder` and untracked after `DiscardOrder`, and a later save inserts nothing. No fix needed.
- [x] T011 Run the full baseline from T001. Every count must match, plus the new T005 and T009 tests. **The refactor is not done until this passes.** **Result 2026-10-09**: build OK (0 warnings); unit 288 passed (262 + 26 new from T004-T007: OrderCandidateValidatorTests, OrderLineFailureMessageTests, extractor/validation test changes); integration 253 passed, 0 skipped (252 + 1 new `DiscardOrderTests`); frontend test 269 passed (18 files); build OK; lint OK (0 errors, 2 warnings, unchanged); `csharpier check backend` clean. Every pre-existing test passes. **Known, ruled deviation (controller ruling R6)**: a PDF quantity text like " 0 " or "+0" now echoes the parsed integer ('0') in its message instead of the raw text.

### 2b. Schema (data-model.md)

- [x] T012 [P] Write an integration test in `backend/tests/LootSingles.IntegrationTests/Persistence/TcgplayerImportSchemaTests.cs` against Testcontainers SQL. It checks that:
  - an `Order` round-trips `ImportSource`, and existing rows default to `PackingSlipPdf`;
  - an `OrderLine` saves a null `CollectorNumber`, plus `Language` (≤ 50) and `ImageUrl` (≤ 2048);
  - `ImportAttempt.Source` round-trips.

  Confirm red.
- [x] T013 [P] Append the new `FailureType` values `IncompleteOrder`, `TcgplayerUnavailable`, `TcgplayerAccessRefused`, `TcgplayerResponseInvalid` and `TcgplayerNotConfigured` in `backend/src/LootSingles.Application/Import/FailureType.cs`, keeping the existing integers.
- [x] T014 Add `backend/src/LootSingles.Domain/Orders/OrderImportSource.cs` (`PackingSlipPdf = 0`, `TcgplayerApi = 1`). Then make these model changes:
  - `Order.ImportSource` in `backend/src/LootSingles.Domain/Orders/Order.cs`;
  - in `backend/src/LootSingles.Domain/Orders/OrderLine.cs`, `CollectorNumber` becomes `string?`, and `Language` and `ImageUrl` are added;
  - `ImportAttempt.Source` in `backend/src/LootSingles.Application/Import/ImportAttempt.cs`;
  - EF configuration in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/`: `OrderConfiguration.cs`, `OrderLineConfiguration.cs` and `ImportAttemptConfiguration.cs`, with defaults of 0 and max lengths;
  - add an `OrderImportSource source` parameter to `OrderImporter.ImportAsync`, which sets `Order.ImportSource`, and pass `PackingSlipPdf` from `PackingSlipImportService`, which also sets `ImportAttempt.Source`;
  - add `{Source}` to `ImportAttemptLog`'s template, with a test-first assertion in `ImportLoggingTests.cs` that it is `PackingSlipPdf` for PDF imports.
- [x] T015 Generate the additive migration `AddTcgplayerApiImport` in `backend/src/LootSingles.Infrastructure/Persistence/Migrations/`. Review its `Up`/`Down`. Make the code that now sees a nullable `CollectorNumber` compile:
  - `OrdersService.cs` and `OrderDetailProjection.cs` pass `line.CollectorNumber` through as nullable;
  - for PDF orders only, `CardIdentity` gets `line.CollectorNumber!`, with a comment saying PDF lines always have one.

  Confirm T012 is green and the T001 baseline still holds.

### 2c. TCGplayer client and the agreement guards (research.md §1, §9–§12; contracts/)

- [x] T016 [P] Write unit tests for `TcgplayerOptions` in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerOptionsTests.cs`. They cover the defaults from contracts/configuration.md, including `OpenOrderStatuses = ["Ready To Ship"]` and `CallsPerMinute = 120`, and check that:
  - binding a `CallsPerMinute` below 1 or **above 150** throws at startup (stage and production share the keys, so two environments at the cap total 300; research.md §9);
  - `IsConfigured` is false when any of the three secrets is blank.

  Confirm red.
- [x] T017 [P] Implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerOptions.cs`, bound manually with validation by hand like `LockoutOptions`. Add the **non-secret** defaults to `backend/src/LootSingles.Api/appsettings.json`. No key, token or placeholder secret goes into any appsettings file.
- [x] T018 [P] Write unit tests for `TcgplayerRateLimiter` in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerRateLimiterTests.cs`, driven by the existing `FakeTimeProvider`. They check that:
  - N calls go through at once, and call N+1 waits until the oldest call is 60 seconds old;
  - over a long simulated run, **no 60-second window ever holds more than N**;
  - concurrent callers share one budget;
  - cancellation while waiting throws `OperationCanceledException`.

  Confirm red.
- [x] T019 [P] Implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerRateLimiter.cs` (a sliding log with `TimeProvider`, about 40 lines; research.md §9) and `TcgplayerRateLimitHandler.cs` (a `DelegatingHandler` that awaits the limiter before every request). Confirm T018 is green.
- [x] T020 [P] Write unit tests for the auth handler in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerAuthenticationHandlerTests.cs`, using the existing `StubHttpMessageHandler`. They check that:
  - the first API call triggers exactly one `POST /token`, with form `grant_type=client_credentials` plus `client_id` and `client_secret`, and the header `X-Tcg-Access-Token`;
  - later calls reuse the cached token until 24 hours before `.expires`;
  - a 401 refreshes the token once and retries once, and a second 401 throws `TcgplayerFeedException(AccessRefused)`;
  - a rejected token request throws `AccessRefused`;
  - **no request ever targets `/app/authorize`**.

  Confirm red.
- [x] T021 [P] Implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerTokenCache.cs` (a singleton guarded by a `SemaphoreSlim`) and `TcgplayerAuthenticationHandler.cs`. Confirm T020 is green.
- [x] T022 Add the application port `backend/src/LootSingles.Application/Import/ITcgplayerOrderFeed.cs`:
  - `Task<IReadOnlyList<string>> GetOpenOrderNumbersAsync(CancellationToken)`
  - `Task<IReadOnlyList<OrderCandidate>> GetOrdersAsync(IReadOnlyList<string> orderNumbers, CancellationToken)`
  - a `TcgplayerFeedFailure` enum (`NotConfigured`, `Unavailable`, `AccessRefused`, `ResponseInvalid`)
  - a `TcgplayerFeedException` carrying the failure plus a safe message, with no response content
- [x] T023 Write a unit test for the version in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerUserAgentTests.cs`. `TcgplayerUserAgent.Value` must equal `LootSinglesFulfillment/{InformationalVersion without the +hash} (Loot Investments LLC)`. Confirm red, then implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerUserAgent.cs`.
- [x] T024 Implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerServiceCollectionExtensions.cs`, `AddTcgplayer(IServiceCollection, IConfiguration)`. It binds the options, registers the singleton limiter and token cache, and adds a typed `HttpClient` with:
  - the base address and API-version prefix;
  - an infinite `HttpClient.Timeout` (each attempt is bounded by a 30-second handler instead);
  - the `User-Agent` default header;
  - the handler chain auth → rate limit → per-attempt timeout (30 s) → primary, so the token request is counted too.

  **R15 (2026-10-09)**: per-attempt 30 s timeout moved inside the rate limiter; HttpClient.Timeout infinite. Wording corrected 2026-10-10 to match the code.

  Call it from `backend/src/LootSingles.Api/Program.cs` **and** `backend/tests/LootSingles.E2EHost/Program.cs`. This one registration is shared on purpose (plan.md, Structure Decision).

**Checkpoint**: The shared import core is extracted with the PDF behaviour proven unchanged, the schema is migrated, and every TCGplayer request is throttled, identified and authenticated with the configured credentials only.

---

## Phase 3: User Story 1: Employee Gets New Orders With One Press (Priority: P1) 🎯 MVP

**Goal**: "Get new orders" imports every new open order from TCGplayer, with per-order results, progress, and duplicate safety.

**Independent Test**: Against the stub serving the synthetic fixtures, press the button. Every valid open order arrives as Ready with the correct lines, quantities, sets, conditions and variants, and a per-order result is shown (quickstart §A and §B).

### Translator and client

- [x] T025 [P] [US1] Write unit tests for `TcgplayerOrderTranslator` in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerOrderTranslatorTests.cs`, covering every row of research.md §7:
  - `categoryName`, `productName` and `groupName` pass through verbatim;
  - `condition` goes through `ConditionVariantParser`, so "Near Mint Foil" gives Condition "Near Mint" and Variant "Foil", and an unknown condition gives `Condition = null`, which the validator then rejects;
  - a `printing` of "Normal" is dropped, other printings are appended, and `isFoil` with no foil text adds "Foil";
  - foil reported three ways at once, as live data does (condition "Near Mint Foil", `printing` "Foil", `isFoil` true), gives Condition "Near Mint" and Variant "Foil" **exactly once**, never "Foil Foil" (research.md §14);
  - a sealed or accessory line (no `Number` in its product's `extendedData`) translates like any card without a number: `CollectorNumber` null, product name and image kept, and the order is not rejected (spec Edge Cases, decided 2026-10-09);
  - `rarity` falls back to the catalog `Rarity`;
  - catalog `Number` "067/086" becomes `CollectorNumber` "#067/086", and a missing `Number` gives null;
  - `language` and `ImageUrl` come from `productImageUrl`, falling back to the product `imageUrl`;
  - `RawDescription` is composed from the fields;
  - the line quantities summing to a different total than `productCount`, or fewer items than `totalItems`, gives `RejectedBySource(IncompleteOrder, specific message)`. ⚠ LIVE: units vs lines, research.md §5.

  Confirm red.
- [x] T026 [US1] Implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerOrderTranslator.cs` as a pure static class. The `extendedData` names come from `TcgplayerOptions.CollectorNumberField` and `RarityField`. ⚠ LIVE: the names, research.md §6. Confirm T025 is green.
- [x] T027 [P] [US1] Write unit tests for `TcgplayerApiClient` in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerApiClientTests.cs`, using `StubHttpMessageHandler` and the T003 fixtures. They check that:
  - `GetStoreKeyAsync` uses `Tcgplayer:StoreKey` when it is set and otherwise calls `/stores/self` **once** per process;
  - open status **names** resolve through the manifest, and a configured name that is missing throws `ResponseInvalid`, naming the status (⚠ LIVE: "Ready To Ship");
  - search pages advance by the number of results actually returned until `totalItems`, and a zero-result page before the total throws `ResponseInvalid`;
  - item paging works the same way;
  - SKU and product lookups are chunked at 50, and ids listed in `errors` or a 404 mean "not found";
  - 5xx, 429, a timeout or a network error throws `Unavailable`, and 403 throws `AccessRefused`;
  - an unparseable body throws `ResponseInvalid`;
  - **the request paths and methods match the allowed list in contracts/tcgplayer-upstream.md and nothing else**.

  Confirm red.
- [x] T028 [US1] Implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerApiClient.cs` and `TcgplayerDtos.cs`. The DTOs declare **only** the fields listed in data-model.md: **no** customer, shipping, email, name or `orderValue` properties. Never log a request or response body. Confirm T027 is green.
- [x] T029 [US1] Write a unit test for `TcgplayerOrderFeed` in `backend/tests/LootSingles.UnitTests/Tcgplayer/TcgplayerOrderFeedTests.cs`, with a stubbed client. It checks that:
  - `GetOrdersAsync` fetches details per batch, items per order, then batched SKU and product lookups;
  - it translates each order, and a per-order `ResponseInvalid` (for example, a malformed items body) becomes `RejectedBySource(TcgplayerResponseInvalid, …)` for that order only;
  - `NotConfigured` is thrown before any request when `IsConfigured` is false.

  Confirm red. Then implement `backend/src/LootSingles.Infrastructure/Tcgplayer/TcgplayerOrderFeed.cs` and register it as `ITcgplayerOrderFeed` in `AddTcgplayer`.

### Import service and endpoint

- [x] T030 [US1] Write unit tests for `TcgplayerApiImportService` in `backend/tests/LootSingles.UnitTests/Import/TcgplayerApiImportServiceTests.cs`, using a fake `ITcgplayerOrderFeed` and the existing fake persistence. They check that:
  - an attempt is created with `Source = TcgplayerApi`, and `OrdersDetected` equals the number of open orders;
  - orders that already exist are reported as `DuplicateOrder` ("Already imported") **without being passed to `GetOrdersAsync`**;
  - new orders are fetched in batches of `PageSize`, each goes through `OrderImporter` with `OrderImportSource.TcgplayerApi` and **no** `beforeSave`, and a progress update follows each order;
  - zero open orders completes with `OrdersDetected = 0`;
  - a `TcgplayerFeedException` sets the matching attempt-wide `FailureType` and message from contracts/import-api.md, keeps committed orders, and ends the stream;
  - cancellation stops before the next order.

  Confirm red.
- [x] T031 [US1] Implement `backend/src/LootSingles.Application/Import/TcgplayerApiImportService.cs`, returning `IAsyncEnumerable<ImportProgressUpdate>`. Use `ImportAttemptLog` for the single completion log, with `{Source}`, the counts, `{CallCount}` (exposed by the limiter) and the failure category. There is **no per-order logging**. Register it as scoped in `Program.cs` and `E2EHost/Program.cs`. Confirm T030 is green.
- [x] T032 [US1] Write controller tests in `backend/tests/LootSingles.IntegrationTests/ImportUi/TcgplayerImportControllerTests.cs`. Use `WebApplicationFactory` with fake keys and `BaseUrl` pointing at a stub primary handler that serves the T003 fixtures (add a `TcgplayerStubHandler` helper to `ImportUiTestSupport.cs`). Cover these scenarios:
  - `POST /api/imports/tcgplayer` requires authentication (401 when signed out);
  - it streams NDJSON `ImportSnapshot`s in the **same shape** as `POST /api/imports`;
  - a full import creates Ready orders whose lines match the fixtures, including a quantity of 3 and a null collector number;
  - a second call reports every order as `duplicateOrder` and makes **no** items calls;
  - an order first imported through the PDF route is reported as a duplicate (FR-010);
  - **two concurrent calls** produce each order exactly once (SC-003);
  - the stub fails with a 500 after the second order: the stream ends `failed` with `tcgplayerUnavailable`, two orders stay, and a retry imports the rest once (FR-013);
  - the stub returns 401 twice: the stream ends `tcgplayerAccessRefused`;
  - with no secrets configured: `tcgplayerNotConfigured`, and the stub records **zero** requests;
  - every stub-received request carries the exact User-Agent, and none targets `/app/authorize` or uses a method other than GET apart from `/token`.

  Confirm red.
- [x] T033 [US1] In `backend/src/LootSingles.Api/Controllers/ImportsController.cs`, generalise `StreamSnapshotsAsync` to accept any `IAsyncEnumerable<ImportProgressUpdate>` and add `[HttpPost("tcgplayer")]`, keeping the PDF route behaviour identical. Confirm T032 and the existing `ImportUi/` suite are green.
- [x] T034 [US1] Write an integration test in `backend/tests/LootSingles.IntegrationTests/Import/TcgplayerPiiTests.cs` with log capture through `ImportTestSupport.CapturingLogger<T>`. After a full stub-backed import, no invented customer or shipping value from `order-details.json` appears in **any** column of **any** table, and none appears in any captured log message (FR-017, SC-004). The test must also confirm that no token, key or raw body was logged. Confirm it passes. It guards the DTO design, so it may pass on first run; if so, mutate the details DTO temporarily to prove it can fail, and record that here. Done: passed on first run, so the details DTO was mutated to prove it can fail: adding a `customer.email` property and appending it to the first line's `Variant` made the test fail with `Collection: ["OrderLines.Variant"]` (a stored leak found by the all-columns scan). A first mutation that put it in `ProductName` made the import itself reject, so that run failed on the succeededCount sanity check instead. The mutation was reverted.

### Frontend

- [x] T035 [P] [US1] Write tests in `frontend/tests/import/importApi.test.ts`:
  - `getNewOrdersFromTcgplayer(signal)` POSTs to `/api/imports/tcgplayer` with credentials and yields snapshots;
  - it shares the NDJSON reader with `importPackingSlip`, so Interrupted and Cancelled behave identically;
  - existing `importPackingSlip` tests stay unchanged and green.

  Confirm red.
- [x] T036 [US1] In `frontend/src/features/import/importApi.ts`, extract the stream reader, add `getNewOrdersFromTcgplayer`, and add the new `failureCode` and `attemptFailureCode` literals. Confirm T035 is green.
- [x] T037 [P] [US1] Write tests in `frontend/tests/import/ImportPage.test.tsx`:
  - **Get new orders** is the primary action, with packing-slip upload below it, labelled as the fallback;
  - pressing it shows progress and then the per-order results;
  - `completed` with zero detected, or with every result `duplicateOrder`, shows **"No new orders"**, distinct from a failure;
  - each attempt-wide code (`tcgplayerNotConfigured`, `tcgplayerUnavailable`, `tcgplayerAccessRefused`, `tcgplayerResponseInvalid`) shows its message from contracts/import-api.md, with Retry and a pointer to PDF upload;
  - Cancel, navigation guard and Interrupted work as for PDF.

  Confirm red.
- [x] T038 [US1] Implement the button, banners and "No new orders" state in `frontend/src/features/import/ImportPage.tsx` and `ImportPage.css`, reusing the existing running, cancel and guard machinery for both sources. Confirm T037 is green, then run `npm --prefix frontend run build`.

### End to end

- [x] T039 [US1] Register a stub TCGplayer primary handler serving the T003 fixtures, with fake keys, in `backend/tests/LootSingles.E2EHost/Program.cs`, wrapping the service the same way `ObservableProgressImportService` delays progress.
- [x] T040 [US1] Write `frontend/e2e/tcgplayer-import.spec.ts` (quickstart §B steps 1–3):
  - sign in as `e2epicker`;
  - **Get new orders** shows progress, then every synthetic order imported;
  - a second press shows **No new orders**;
  - the dashboard lists the new Ready orders.

  Run it through the E2E host (`--artifacts-path` per the project's E2E notes).

**Checkpoint**: US1 is fully functional and independently testable. This is the MVP.

---

## Phase 4: User Story 2: Picker Sees Correct Card Images for API Orders (Priority: P2)

**Goal**: API lines show TCGplayer's own image, or none, and no third-party catalog is ever asked about them. Lines with no number show "No number", and the language is shown when it isn't English.

**Independent Test**: Open an imported synthetic order. Lines with an image show it, the line without one shows none, the no-number line reads "No number", and no `ICardCatalogProvider` is called (quickstart §A).

- [x] T041 [P] [US2] Write integration tests in `backend/tests/LootSingles.IntegrationTests/Orders/TcgplayerOrderImageTests.cs`, using a **recording** fake `ICardCatalogProvider` for every product line (the `OrdersControllerTests.cs` pattern):
  - `GET /api/orders/{id}` for a `TcgplayerApi` order returns each line's stored `imageUrl`, or `null`, and **the fake records zero calls**, even for a line with no image (FR-019, SC-005);
  - `collectorNumber` is `null` for the no-number line;
  - `language` is returned;
  - a `PackingSlipPdf` order still calls the provider and resolves images as before.

  Confirm red.
- [x] T042 [US2] In `backend/src/LootSingles.Application/Orders/OrdersService.cs` (`GetByIdAsync`), branch once on `Order.ImportSource`: API orders take `ImageUrl` from the line and never call `CardImageEnrichmentService`. Add `Language` to `OrderDetail.cs`, `OrderDetailProjection.cs` and `OrderLineDetailResponse` in `backend/src/LootSingles.Api/Controllers/OrdersController.cs`, and project `ImportSource` and `ImageUrl` as needed. Confirm T041 is green.
- [x] T043 [P] [US2] Write tests in `frontend/tests/orders/FocusedPickView.test.tsx`, `OrderDetailPage.test.tsx` and `OrderFinish.test.tsx`:
  - a line with a `null` collector number renders **"No number"**, never blank or "null";
  - a line with `language` "Japanese" shows "Japanese" next to the condition, and "English" or `null` shows nothing extra;
  - quantity-greater-than-one emphasis is unchanged on API lines.

  Confirm red.
- [x] T044 [US2] Implement the tested changes:
  - `collectorNumber: string | null` and `language: string | null` in `frontend/src/features/orders/ordersApi.ts`;
  - the render changes in `FocusedPickView.tsx` (L173), `OrderDetailPage.tsx` (L508) and `OrderFinish.tsx` (L96).

  Confirm T043 is green, then run `npm --prefix frontend run build`.
- [x] T045 [US2] Extend `frontend/e2e/tcgplayer-import.spec.ts` (quickstart §B step 4): open an imported order and check that the lines show images, the seeded no-number line reads "No number", and the quantity-3 line is emphasised. (Ruling: the fixtures hold no quantity-3 line, so the check uses SYN-0002's quantity-2 line; SYN-0009 is the no-number sealed line and SYN-0004 the Japanese one.)

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3: Fall Back to PDF When the API Is Unavailable (Priority: P3)

**Goal**: TCGplayer failures are clear and safe, and PDF upload keeps working unchanged.

**Independent Test**: With the stub returning an outage, and separately a refusal, nothing is created, the right message shows, and a PDF upload then imports normally (quickstart §A).

- [x] T046 [US3] Write an integration test in `backend/tests/LootSingles.IntegrationTests/ImportUi/TcgplayerFallbackTests.cs`: after `tcgplayerUnavailable`, and separately after `tcgplayerAccessRefused`, a `POST /api/imports` PDF upload of an existing fixture imports normally **and stores each order's packing slip** (FR-016). Most of the behaviour exists already, so confirm the test is red or green and record which. (Recorded: GREEN on first run, both cases. Proven able to fail: with the slicer temporarily returning null, both cases failed on `Assert.NotNull(order.PackingSlip)` for 13 of 13 orders; reverted.)
- [x] T047 [US3] Extend `frontend/e2e/tcgplayer-import.spec.ts`. Add an E2E-host switch, for example a test-only header or a fixture toggle in `E2EHost/Program.cs`, that makes the stub return 503. Then check that **Get new orders** shows the "Couldn't reach TCGplayer" banner pointing to packing-slip upload, and that a PDF upload on the same screen succeeds. (Switch: the E2E host stub answers 503 to TCGplayer calls made while serving a request that carries `X-E2E-Tcgplayer-Outage: unavailable`; the spec adds it with `page.route` to its own page only, so it cannot reach the parallel import test. The upload uses `multi-page-order-no-total-on-continuation-pages.pdf`, which no other spec imports.)

**Checkpoint**: Failure paths are verified end to end.

---

## Phase 6: User Story 4: Packer Handles an Order With No Stored Slip (Priority: P3)

**Goal**: API orders pack normally, with the existing "print from TCGplayer" guidance.

**Independent Test**: An API-imported, picked order scanned at the packing desk shows the no-slip guidance with its order number, and can be recorded as packed (quickstart §B step 5).

- [x] T048 [US4] Write an integration test in `backend/tests/LootSingles.IntegrationTests/Packing/TcgplayerPackingTests.cs`:
  - for an API-imported order, `GET /api/packing/orders/{code}` returns `hasPackingSlip: false`;
  - `GET /api/orders/{id}/packing-slip` returns 404;
  - recording the order as packed succeeds;
  - no `OrderPackingSlip` row exists for any `TcgplayerApi` order (data-model.md invariant, FR-018).

  Confirm red or green, and record which.
- [x] T049 [US4] Extend `frontend/e2e/tcgplayer-import.spec.ts` (quickstart §B step 5): pick an API-imported order to Picked, scan it at the packing desk, and check the "No packing slip is stored for this order. Print it from TCGplayer…" text and order number, then record it as packed.

**Checkpoint**: All four stories work independently.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T050 [P] Extend the credential scan, in `backend/tests/LootSingles.IntegrationTests/Configuration/DeploymentConfigurationTests.cs` and in the `credential_pattern` step of `.github/workflows/pr-quality-gate.yml` together, to catch `PrivateKey`, `client_secret` and `X-Tcg-Access-Token` value assignments (FR-023, SC-008).
  - Make test fakes avoid the pattern, following the precedent in commit `ff87871`.
  - Confirm the 020 probe and authorize scripts under `specs/020-tcgplayer-api-import/probe/` don't trip it: they interpolate variables and contain no values.
- [x] T051 [P] Add `ARG APP_VERSION=0.0.0-local` to `Dockerfile` and pass `-p:InformationalVersion=$APP_VERSION` to `dotnet publish`. In `.github/workflows/deploy-stage.yml`, pass `--build-arg APP_VERSION=1.0.0-sha.${GITHUB_SHA::7}` to the image build (research.md §11). (Amended after the final review: the earlier `1.0.0+sha` form lost the SHA to the `+hash` trim, so every build reported `1.0.0`.) Production reuses stage's image, so it carries the same version.
- [x] T052 [P] Write `docs/prd/Loot_Singles_Fulfillment_PRD_v0.7.md` (research.md §16), changing these sections:
  - **§25 and §40.6:** the API is primary and PDF is the fallback;
  - **§27:** API orders carry no slip;
  - **§28:** access is granted under the 2026 addendum, with its rules;
  - **§41:** questions 24 and 26 are answered, and 20 and 21 stay open.

  Add a changelog entry like prior amendments (A17). Update the PRD link in `CLAUDE.md` and `README.md`.
- [x] T053 [P] Rewrite the "TCGplayer Integration" section of `README.md`: API import is primary, PDF is the fallback, and it links to `specs/020-tcgplayer-api-import/tcgplayer-setup.md` and the `CLAUDE.md` agreement rules.
- [x] T054 [P] Update the 019 deployment docs for the repository's first deployment secrets:
  - in `specs/019-automated-deployment/contracts/deployment.md`, change "Secrets contract: there are none" to the three `tcgplayer-*` Container Apps secrets;
  - add a runbook step to `specs/019-automated-deployment/quickstart.md` pointing to `tcgplayer-setup.md` Part 3.
- [x] T055 Evaluate logging per the constitution's Observability standard: one completion log per API attempt with `{Source}`, the counts, `{CallCount}` and the failure category, and **no** per-order, body, token or PII logging. Confirm `ImportLoggingTests.cs` covers the API attempt; add the assertion if it is missing. Outcome: logging already conforms, no production change. ImportLoggingTests already covers the API attempt (success: Information, Source, counts, CallCount=5, no leak, no OrderImporter entries; attempt-wide failure: Warning, TcgplayerUnavailable, CallCount=2); proven able to fail by temporarily passing a null CallCount (test failed Expected 5, Actual null), then reverted.
- [x] T056 Run CSharpier over the changed C# files (`dotnet csharpier format` on this branch's files only) and `npm --prefix frontend run format`, and confirm `dotnet csharpier check backend` and `npm --prefix frontend run format:check` pass. **Result 2026-10-10**: `dotnet csharpier format` over the branch's 73 changed C# files and `npm run format` changed nothing; `csharpier check backend` clean (255 files); `format:check` clean. No pre-existing drift.
- [x] T057 Run the full regression: both backend test projects, `npm --prefix frontend run test`, `build` and `lint`, and the Playwright suite (`npm --prefix frontend run test:e2e`). Compare against the T001 baseline. Every pre-existing test must still pass. **Result 2026-10-10**: build OK (0 warnings); unit 465 passed (baseline 262); integration 294 passed, 0 skipped (baseline 252); frontend test 302 passed (18 files; baseline 269 in 18); frontend build OK; lint OK (0 errors, 2 warnings, unchanged); Playwright 36 passed (`npx playwright test --reporter=list`). Every pre-existing test passes; no new warnings.
- [ ] T058 Run Superpowers requesting-code-review on the branch and fix Critical and Important findings test-first (`CLAUDE.md`, Workflow). Then finish the branch with finishing-a-development-branch or `/ship`.

### Amendment 2026-10-10: already-imported orders on API presses (spec Clarifications 2026-10-10, FR-011, FR-025)

Open orders stay open on TCGplayer until they ship, so every press sees the already-imported ones again. On the API path they are a normal outcome, not a failure. PDF behaviour is unchanged. Run these before T058's finish step.

- [x] T063 Update the contracts and plan text:
  - `contracts/import-api.md`: the API results list keeps a `duplicateOrder` result per already-imported order (the wire format is unchanged), and the UI collapses them into one "N already imported" line on API presses; "No new orders" is unchanged;
  - the completion-log wording in `plan.md` and `research.md` wherever it describes FR-025, to match the amended FR-025;
  - the doc drift left from U14b: `plan.md`, `tasks.md` T024 and `contracts/` still describe a 30 s HttpClient timeout and "rate limit → auth"; the pipeline is auth → rate limit → per-attempt timeout (30 s) → primary, with an infinite HttpClient timeout.
- [x] T064 Write tests in `backend/tests/LootSingles.IntegrationTests/Logging/ImportLoggingTests.cs` (or wherever the API completion-log tests live) for FR-025 as amended:
  - an API attempt whose results are only imported and already-imported orders logs exactly one **Information** entry, with the already-imported count and **no** already-imported order numbers;
  - an API attempt with already-imported orders plus a real rejection logs one **Warning** whose per-type breakdown lists only the real failure type with its order numbers, with the already-imported count alongside;
  - an attempt-wide API failure still logs one Warning as before;
  - the existing PDF duplicate tests stay unchanged and green (006 FR-004).

  Confirm red.
- [x] T065 Implement T064 in `backend/src/LootSingles.Application/Import/ImportAttemptLog.cs`, branching once on the attempt's source. Keep the PDF path byte-for-byte as it is. Confirm T064 is green and every existing logging test passes.
- [ ] T066 Write tests in `frontend/tests/import/ImportPage.test.tsx`: after an API press, already-imported results render as one "N already imported" line and not as individual rows; imported and rejected orders still render one row each; "No new orders" still shows when every result is already imported; a PDF import still shows one row per duplicate. Confirm red, then implement in `ImportPage.tsx`, and run `npm --prefix frontend run build`. Update `frontend/e2e/tcgplayer-import.spec.ts` where the second press or later assertions depend on the old per-row display (always `--reporter=list`).

### Manual: live confirmation (a person, never an AI tool)

- [x] T059 [MANUAL] Complete the Store Authorization Workflow (`tcgplayer-setup.md` Part 1). **Authorization done 2026-10-08**: the store access token is saved in user-secrets. TCGplayer enabled access after an email; on 2026-10-09 `Check-TcgplayerAccess.ps1` found 62 of 68 read-only endpoints open, including every call this feature makes (research.md §14).
- [x] T060 [MANUAL] Run `Probe-Tcgplayer.ps1` (`tcgplayer-setup.md` Part 2) and record its findings **in words** in research.md §14. Don't paste the report.
- [x] T061 [MANUAL] Close out every ⚠ LIVE assumption using T060's findings. **Done 2026-10-09**: every assumption held. The one correction is the status spelling, "Ready To Ship", now the default everywhere. `productCount` counts units, the collector number is `extendedData` `Number`, and the condition carries the foil suffix while `printing` is "Normal" or "Foil". No translator or shape change is needed. The original checklist:
  - the open status name (T027): a configuration change if it differs;
  - `productCount` meaning (T025): a translator change through `/speckit-implement` if it means lines;
  - the `extendedData` names (T026): a configuration change;
  - condition and printing wording (T025).

  Write the synthetic fixtures (T003) to match any shape corrections, with invented values only. Record each outcome here.
- [ ] T062 [MANUAL] Add the stage secrets (`tcgplayer-setup.md` Part 3), deploy, and press **Get new orders** on stage. Check:
  - imported orders against the TCGplayer seller portal: lines, quantities, conditions, variants (foil shown once) and images;
  - **cross-source duplicates (FR-010)**: before pressing, upload one open order's packing slip by PDF. The press must report that order "Already imported", which proves the API's order number matches the slip's text exactly;
  - **timing (SC-001)**: a typical batch shows its results within 30 seconds;
  - the logs show `{CallCount}` within budget.

  Then repeat on production.

---

## Dependencies & Execution Order

- **Setup (T001–T003)**: start immediately. T002 and T003 run in parallel.
- **Foundational**: blocks every story.
  - **2a (T004–T011)** runs in sequence. T011 is the gate.
  - **2b (T012–T015)** follows T011, so the refactor lands green before the schema changes.
  - **2c (T016–T024)** is independent of 2a and 2b except for T022, which needs T004. Within 2c, the options, limiter, auth and user-agent pairs run in parallel, and T024 comes last.
- **US1 (T025–T040)** needs all of Phase 2:
  - the translator (T025–T026) and client (T027–T028) run in parallel;
  - then the feed (T029), the service (T030–T031), the endpoint (T032–T033) and the PII test (T034);
  - the frontend (T035–T038) can start once the contract is fixed and runs alongside the backend;
  - E2E (T039–T040) comes last.
- **US2 (T041–T045)** needs US1's import, so there are API orders to view. Its frontend tasks (T043–T044) can run in parallel with T041–T042.
- **US3 (T046–T047)** and **US4 (T048–T049)** need US1 and are independent of each other and of US2.
- **Polish (T050–T058)** comes after the desired stories. T050–T054 run in parallel. T057 then T058 are last.
- **Manual (T059–T062)**: T059–T061 are done (2026-10-09). T062 needs a deployment.

## Parallel Example: User Story 1

```text
# After Phase 2, in parallel:
T025 translator tests → T026 translator
T027 client tests     → T028 client
T035 importApi tests  → T036 importApi
T037 ImportPage tests → T038 ImportPage
```

## Implementation Strategy

1. **MVP**: Phases 1–3, so "Get new orders" works end to end against the synthetic stub. Stop and validate with quickstart §A and §B steps 1–3.
2. **Add US2**: the images and "No number" that make API orders pickable visually.
3. **Add US3 and US4**: proof of the failure paths and packing.
4. **Polish and code review.**
5. **Go live** with T062. Production is the last step, after stage is verified against the seller portal.
