# Quickstart: TCGplayer Packing Slip Order Import

Validation scenarios that prove this feature works end-to-end. These map directly to spec.md's User Stories and Success Criteria — see [spec.md](./spec.md) for the full acceptance scenarios and [data-model.md](./data-model.md) / [contracts/import-service.md](./contracts/import-service.md) for shapes.

## Prerequisites

- .NET 8 SDK installed
- A local SQL Server instance (LocalDB or containerized) with EF Core migrations applied
- A set of sanitized, representative packing-slip PDF fixtures under `backend/tests/LootSingles.Fixtures/PackingSlips/`, including at minimum:
  - A valid multi-order batch (mirroring the real example: several orders, one per page, ending in a summary/index page)
  - A batch containing one order with a missing order identifier, alongside otherwise-valid orders
  - An order with zero product lines
  - A product line with a blank/zero/negative/non-numeric quantity
  - A product line missing a required field (product name, set, collector number, or condition)
  - A product line whose condition and variant are combined in the source (e.g., "Near Mint Holofoil") and one with a parenthetical variant marker in the product name (e.g., "(Showcase)")
  - A duplicate of an already-imported order identifier
  - A corrupted/non-PDF file
  - A packing slip containing a customer's shipping name and address

## Run

```powershell
cd backend
dotnet test
```

## Validation scenarios

### 1. Valid import (spec User Story 1, SC-001, SC-002)

Call `IPackingSlipImportService.ImportAsync` with the valid multi-order fixture. Enumerate to completion.

**Expect**: the final `ImportAttempt.ImportOrderResult`s all report `Succeeded`; each resulting `Order` has the correct `TcgplayerOrderId`; each `OrderLine`'s `Quantity` matches the source exactly (including multi-quantity lines); no product lines are merged, dropped, or reassigned across orders.

### 2. Partial import — one bad order doesn't block the rest (spec User Story 1 scenario 4, FR-005)

Call `ImportAsync` with the fixture containing one order with a missing identifier alongside valid orders.

**Expect**: the valid orders' `ImportOrderResult`s report `Succeeded` with resulting `Order`s persisted; the bad order's result reports `Rejected` with `FailureCode = MissingOrderIdentifier`.

### 3. Defensive rejection, no partial data (spec User Story 2, SC-002, FR-007)

Run each malformed fixture (zero product lines, bad quantity, missing required field, corrupted file) individually.

**Expect**: no `Order` row exists for the rejected order in any case; each rejection's `FailureCode` matches the expected value from the FR-006 taxonomy; for the corrupted-file case, `ImportAttempt.AttemptFailureCode = UnreadablePdf` and zero `ImportOrderResult`s exist.

### 4. Duplicate detection, including the race (spec User Story 2 scenario 5, FR-008, SC-007)

Import a fixture, then import the same fixture again.

**Expect**: second import's result reports `Rejected` with `FailureCode = DuplicateOrder`; the original `Order` and its `OrderLine`s are byte-for-byte unchanged. Separately (integration test, not manual): fire two concurrent `ImportAsync` calls for the same new order identifier and confirm only one `Order` row exists afterward — the loser reports `DuplicateOrder`, not a raw database exception.

### 5. Condition/variant disambiguation (spec User Story 1 scenario 5, FR-018, SC-009)

Import the fixture with a combined condition+variant value and a parenthetical product-name marker.

**Expect**: `Condition` and `Variant` are separate, correct values (not one combined string); `Variant` includes both the condition-suffix-derived text and the parenthetical marker text.

### 6. No customer PII, anywhere (spec User Story 3, FR-009, FR-019, SC-004)

Import the fixture containing a customer's shipping name and address.

**Expect**: no field on any persisted `Order` or `OrderLine` contains that name/address; after the call completes, inspect the process's temp storage and the database for any retained copy of the source PDF bytes — none should exist. Repeat for a rejected import (e.g., the corrupted-file fixture) to confirm the file isn't retained even on failure.

### 7. Raw description retained (FR-017, SC-008)

For any successfully imported `OrderLine`, compare its `RawDescription` field against the fixture's source text for that line.

**Expect**: byte-for-byte match.

### 8. Progress is observable (FR-014, SC-006)

Enumerate `ImportAsync`'s updates for the ~200-order-scale fixture (or a synthetic large fixture if a real one isn't available) without discarding intermediate values.

**Expect**: `OrdersProcessed`, `SucceededCount`, and `FailedCount` increase monotonically across updates; `OrdersDetected` is stable and correct once the file has been scanned; the final update has `IsComplete = true`.

## Validation record

Validated on 2026-08-20 with `dotnet test backend/LootSingles.sln --no-restore`:

| Scenario | Automated coverage | Result |
|---|---|---|
| Valid import | `ValidImportTests` | Pass |
| Partial import | `PartialImportTests` | Pass |
| Defensive rejection | `RejectionTests`, `AtomicPersistenceTests` | Pass |
| Duplicate detection and race | `DuplicateOrderTests` | Pass |
| Condition/variant disambiguation | `ConditionVariantParserTests`, `OrderLineExtractionTests` | Pass |
| No retained customer PII or PDF | `PiiExclusionTests`, `PiiRetentionTests` | Pass |
| Raw description retained | `OrderLineExtractionTests`, `ValidImportTests` | Pass |
| Observable progress | `ProgressReportingTests` | Pass |

The resolved NuGet dependency graph was also reviewed from `project.assets.json` and each package's local `.nuspec`/license metadata. Dependencies use permissive MIT, Apache-2.0, BSD-style, or Microsoft redistributable terms; no AGPL or commercial-only dependency is present. PdfPig is Apache-2.0 licensed (its custom package's `.nuspec` omits the license field, so this was verified against the upstream repository).
