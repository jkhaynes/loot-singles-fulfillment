# Research: Local Mock Orders and Order Deletion

All decisions below come from reading the current code (2026-10-10). No TCGplayer call was made, and no TCGplayer response was read.

## R1. Where the mock orders come from

**Decision**: Move the existing synthetic stub, `TcgplayerStubHandler`, from `backend/tests/LootSingles.IntegrationTests/ImportUi/` into Infrastructure as `backend/src/LootSingles.Infrastructure/Tcgplayer/Synthetic/SyntheticTcgplayerHandler.cs`. Move the synthetic JSON from `backend/tests/LootSingles.Fixtures/Tcgplayer/` into `Synthetic/Data/`, embedded as resources (the same precedent as the CardCatalog JSON in `LootSingles.Infrastructure.csproj:7-13`). The integration tests, unit tests and the E2E host then use the moved class and data instead of their linked copies.

**Rationale**:
- One set of invented TCGplayer data (spec Assumptions; constitution XIII). A second, local-only fixture set would duplicate the 020 fixtures the moment it was written.
- The stub is primary-handler-shaped, so the real auth, rate-limit, timeout and User-Agent handlers still run in front of it locally. FR-003 asks for the same import rules as real orders, so this exercises everything except the network.
- The published image carries only invented data. The fixtures README already requires that every value is hand-written from the published schema and never a recording.

**Alternatives considered**:
- *Debug-only compile link into the Api project.* This keeps the code out of the Release image, but it makes "local" a build configuration instead of an environment. A `dotnet run -c Release` on a developer machine with keys set would then call the real API, which breaks FR-005.
- *A separate in-memory `ITcgplayerOrderFeed`.* It would skip the translator's HTTP shape, paging, token and handler pipeline, so mock orders would no longer go "through the same import rules" (FR-003). It would also be a second fake to maintain beside the stub.

## R2. How "local" is decided, and why it can't leak

**Decision**: `Program.cs` checks `builder.Environment.IsDevelopment()`. When it's true, after every other configuration source, it adds an in-memory source that forces `Tcgplayer:PublicKey`, `PrivateKey` and `AccessToken` to obviously fake values and sets `Tcgplayer:BaseUrl` to the stub's host. It then registers the synthetic handler as the TCGplayer client's primary handler. Outside Development none of this is registered, and nothing in configuration can turn it on: there is no configuration key for it at all (FR-004).

**Rationale**:
- Stage and production never run as Development. Neither sets `ASPNETCORE_ENVIRONMENT`, so both report the default, Production (Dockerfile, both deploy workflows).
- Adding the override *last* matters. User secrets load in Development and may still hold real keys or a `BaseUrl`, and the override has to win over them (FR-005). The E2E host already does it this way (`E2EHost/Program.cs:80-90`).
- With no configuration switch, a wrong setting on stage or production has nothing to turn on (SC-002).

**Consequence for tests**: `WebApplicationFactory` defaults to Development, so most integration tests run as Development today. If nothing changed, they'd silently get the synthetic source. The shared integration factory (`AuthWebApplicationFactory`) and the other factories therefore run as a dedicated `Testing` environment. Only the tests for this feature opt into Development. The tests that already force `Production` are unchanged.

## R3. Which mock orders a press returns

**Decision**: Locally, the open-order list is SYN-0001 to SYN-0005 and SYN-0008 to SYN-0010, the same list the E2E host uses (`E2EHost/Program.cs:101-105`). SYN-0011 to SYN-0013 are still in the store as not-open orders, so the open-order rules run (spec Edge Cases). SYN-0006 and SYN-0007 are deliberately invalid and are left out, so a second press can reach "No new orders" (FR-007). Their rejection paths stay covered by the existing tests.

The local configuration also uses the stub's page size of 2, so paging runs too.

**High-risk coverage (SC-004)**:
- quantity greater than one: SYN-0002;
- foil: SYN-0003;
- non-English, and in-store pickup: SYN-0004;
- paged items: SYN-0005;
- condition and printing wording: SYN-0008;
- sealed product with no collector number: SYN-0009;
- catalog not found: SYN-0010.

## R4. Card images for mock orders

**Decision**: No change. Every synthetic image URL is on `img.example.test`, a reserved test domain, so the browser's request fails and the existing "no image" placeholder shows. A unit test pins that no embedded synthetic file contains a `tcgplayer` host, so a local browser never requests TCGplayer's CDN (SC-003, "no image is better than the wrong image").

## R5. Telling production from stage

**Finding**: Both environments run as ASP.NET "Production". The only setting that differs today is `HealthChecks__ExposeDatabaseEndpoint`, which is set on production.

**Decision**: Add a new setting, `OrderDeletion:AllowTouchedOrders` (bool, default **false**). It's set to `true` in `appsettings.Development.json` and, as a manual portal step, on the stage Container App (`OrderDeletion__AllowTouchedOrders=true`). With the setting absent, the production rule applies (FR-013).

**Rationale**: The safe direction is the default. A missing or mistyped setting gives the strictest behavior, so production can only become permissive if someone sets it there on purpose. The frontend never needs to know the environment's name. The server says, for each order, whether it can be deleted and why not (contracts/api.md).

**Alternatives considered**:
- *Set `ASPNETCORE_ENVIRONMENT=Staging` on stage.* That changes more than deletion, since any future environment branch would silently apply. It also turns "production" into "anything that isn't Staging", which is the unsafe default.
- *An environment-name endpoint for the UI.* Not needed once each order carries its own `canDelete`.

## R6. What "never claimed" means in the data

**Finding**: Nothing records a past claim. Release and force-release clear `ClaimedByEmployeeId` and `ClaimedAt` (`OrderRepository.cs:119-120, 144-145`), and the claim log lines are only on stdout.

**Decision**: Add `Orders.FirstClaimedAt` (nullable). It's set by both claim updates (`ClaimNextAvailableAsync` and `ClaimSpecificAsync`) only when it's null, and nothing ever clears it. An order is **touched** when `FirstClaimedAt` is not null.

The migration backfills existing orders. An order is marked touched, using the earliest time available, when it is currently claimed (`ClaimedAt`), any line has a pick outcome (`PickOutcomeRecordedAt`), any line has a picking issue (`ReportedAt`), or it is packed (`PackedAt`). An order that was claimed and released before this feature, with nothing recorded, can't be detected. It has no pick history to lose, so this is acceptable (FR-020 protects history, and that order has none).

## R7. Soft delete mechanics

**Decision**:
- **New columns:** `Orders.DeletedAt` and `Orders.DeletedByEmployeeId`. The foreign key to Employees is Restrict.
- **Hidden everywhere:** an EF global query filter `DeletedAt == null` on `Order` hides deleted orders from every screen query (the list, detail, Pick Next, claims, picking, packing lookup, dashboard counts and label). Every one of them reads through `context.Orders` (agent survey). `ExecuteUpdateAsync` honors the filter, so claiming, picking or packing a deleted order updates no rows, and the existing re-read then reports `order_not_found` (FR-018).
- **Unique order number:** the unique index on `TcgplayerOrderId` becomes filtered: `[DeletedAt] IS NULL`. Combined with the filtered `OrderExistsAsync` duplicate check, a deleted order is no longer "already imported", and a fresh copy can be inserted (FR-017). Both changes are needed. Without the index change, the second insert hits the unique-violation path and is reported as a duplicate.
- **One claim per employee:** the filtered unique index on `ClaimedByEmployeeId` adds `AND [DeletedAt] IS NULL`. Deleting a claimed order (local and stage) keeps its claim fields as history, as FR-016 requires, but frees the picker to claim another order.
- **Packing slip:** removed in the same transaction with `ExecuteDeleteAsync` on `OrderPackingSlips`. `PackingSlipAccesses` points at the order row, which still exists, so the retrieval log stays without any change.
- **Who and when (FR-015):** `DeletedAt` and `DeletedByEmployeeId` on the order are the deletion record. No new audit table is needed. An `ILogger` Information line records each deletion, and one summary line records each delete-all.

**Concurrency**: the delete is a single conditional `ExecuteUpdateAsync`, the same compare-and-swap shape as claims (`OrderRepository.ExecuteConditionalUpdateAsync`).
- Single delete condition: `Id == x && DeletedAt == null`, plus `FirstClaimedAt == null && ClaimedByEmployeeId == null` unless `AllowTouchedOrders`.
- A claim also sets `FirstClaimedAt` with a conditional update, so the two serialize on the row lock. If the order is claimed while the Manager/Admin is confirming, the delete is refused (spec Edge Cases).
- After a refusal, a re-read with `IgnoreQueryFilters()` words it: not found, already deleted, or claimed before.

**Delete all**: one set-based `ExecuteUpdateAsync` with the same condition plus `ImportedAt <= asOf`. `asOf` is the server time that the deletion summary returned when the confirmation opened, so orders imported after that stay (spec Edge Cases). The slip delete runs in the same transaction, for the orders the update just marked (`DeletedAt == now && DeletedByEmployeeId == actor`).

## R8. A shared confirmation dialog

**Finding**: `ImportPage.tsx:272-311` hand-rolls an `alertdialog` confirmation twice. Order deletion adds a third and fourth.

**Decision**: Extract `frontend/src/components/ConfirmationDialog.tsx` from the Import page's markup and CSS. It takes a title, body, keep and confirm labels, a destructive flag, and an optional "type to confirm" word. Move ImportPage's two uses onto it, then build both delete confirmations with it (constitution XIII: consolidate at the second use).

The per-row ⋯ menu is used only on Browse Orders, so it stays local to `features/orders/`. A shared menu component would have one user.

## R9. A picker whose order was deleted

**Finding**: Every order API call already maps a 404 to `OrderNotFoundError`, but `OrderDetailPage`'s action handlers ignore it. They show "Couldn't record that pick. Try refreshing the page." and leave the stale order on screen.

**Decision**: When a pick, issue report, release, finish or label action raises `OrderNotFoundError`, the page switches to the existing not-found state and shows "This order no longer exists" (FR-018). The not-found state's wording changes to fit both causes.

## R10. Out of scope, by Product Owner decision or the spec

The following are not part of this feature:
- checkboxes for picking several orders;
- an undo toast;
- a screen for viewing or restoring deleted orders;
- server-side enforcement of the typed `DELETE`. The typed word guards against a misclick in the UI; the server's protection is the Manager/Admin role and the production rule.
- any change to TCGplayer.
