# Data Model: TCGplayer API Order Import

**Feature**: `020-tcgplayer-api-import` | **Date**: 2026-10-08

One EF Core migration, `AddTcgplayerApiImport`. Every change can be applied while the previous image is still running: three nullable columns are added, one column is relaxed from `NOT NULL` to `NULL`, and two non-null columns are added with a default. The previous image keeps working against the new schema until an API-imported order with a null collector number exists. A rollback after API imports have run would therefore need those orders' detail views checked. The quickstart's rollback note says so.

No customer field is added anywhere (PRD §27, FR-017).

---

## Persisted changes

### `Order` (Domain): changed

| Field | Type | Change | Rule |
|---|---|---|---|
| `ImportSource` | `OrderImportSource` enum, stored as int, non-null, default `0` | **New** | Set once at creation by `OrderImporter`, and never changed afterwards. Existing rows become `PackingSlipPdf`. |

`OrderImportSource` (Domain/Orders):
- `PackingSlipPdf = 0`
- `TcgplayerApi = 1`

Invariant: an order with `ImportSource = TcgplayerApi` never has an `OrderPackingSlip` (FR-018). The API path has no hook that attaches one, and an integration test asserts it.

### `OrderLine` (Domain): changed

| Field | Type | Change | Rule |
|---|---|---|---|
| `CollectorNumber` | `string?` | **Now nullable** (was `required string`) | PDF lines always have one; the PDF extractor still rejects a slip line without one as `MissingCollectorNumber`. API lines have one when TCGplayer's catalog provides it. A null value means "no number available", and every picking surface shows **"No number"** for it (FR-008), never a blank. |
| `Language` | `string?`, max 50 | **New** | TCGplayer's `language` for API lines. Null for PDF lines. Picking surfaces show it next to the condition when it is present and not `English`. |
| `ImageUrl` | `string?`, max 2048 | **New** | TCGplayer's product image URL for API lines. Always null for PDF lines, whose images are still resolved at view time. Read only for orders whose `ImportSource` is `TcgplayerApi` (FR-019). |

Every other `OrderLine` field is unchanged. For how API lines fill `ProductLine`, `ProductName`, `Set`, `Rarity`, `Condition`, `Variant`, `Quantity` and `RawDescription`, see research.md §7.

### `ImportAttempt` (Application): changed

| Field | Type | Change | Rule |
|---|---|---|---|
| `Source` | `OrderImportSource`, int, non-null, default `0` | **New** | Set at creation. The employee-facing result can then say where the orders came from, and logs carry `{Source}`. |

### `FailureType` (Application): extended

New values are appended, so existing stored integers keep their meaning.

| Value | Scope | Meaning |
|---|---|---|
| `IncompleteOrder` | Per order | The order's lines could not all be retrieved, or their quantities don't match its product count. Nothing is created for that order. |
| `TcgplayerUnavailable` | Attempt | TCGplayer could not be reached, timed out, or returned 5xx or 429. |
| `TcgplayerAccessRefused` | Attempt | TCGplayer rejected the store's credentials (401 after one refresh, or 403). |
| `TcgplayerResponseInvalid` | Attempt or per order | A response didn't have the documented shape, a configured open order status, open pickup status or order type name is missing from the manifest, or paging stalled. It is attempt-wide when search or manifest is affected, and per order when only that order's items or details are (including TCGplayer returning no details for it). |
| `TcgplayerNotConfigured` | Attempt | No TCGplayer credentials are configured in this environment. No call was made. |

`ImportOrderResult` is unchanged. API orders record `SourceOrderIdentifier` = the TCGplayer order number.

---

## Source-neutral import input (Application/Import, not persisted)

These records replace the direct use of `RawOrderBlock` and `RawProductLine` inside the shared pipeline. Each adapter produces them, and `OrderCandidateValidator` decides whether they can become a domain `Order` (Principle XII: untrusted input becomes a domain object only after validation).

```text
OrderCandidate
  SourceOrderIdentifier : string?          // TCGplayer order number
  Lines                 : IReadOnlyList<OrderLineCandidate>
  RejectedBySource      : (FailureType, string)?   // adapter-level rejection, e.g. IncompleteOrder;
                                                   // when set, the validator does not run

OrderLineCandidate
  RawDescription  : string
  ProductLine     : string?
  ProductName     : string?
  Set             : string?
  CollectorNumber : string?
  Rarity          : string?
  Condition       : string?
  Variant         : string?
  Language        : string?
  ImageUrl        : string?
  Quantity        : int?          // null = unreadable quantity (PDF text) or missing (API)
```

**Validation** (`OrderCandidateValidator`, shared by both sources). Each check rejects with the existing `FailureType` and message:

1. `SourceOrderIdentifier` is present, else `MissingOrderIdentifier`.
2. At least one line, else `NoProductLines`.
3. For each line, in order:
   - `Quantity` is greater than 0, else `InvalidQuantity`;
   - `ProductName` is present, else `MissingProductName`;
   - `Set` is present, else `MissingSet`;
   - `Condition` is present, else `MissingCondition`.

The PDF-only check (a slip line must have a collector number, else `MissingCollectorNumber`) stays in `OrderLineExtractor`. The extractor turns a `RawProductLine` into an `OrderLineCandidate` and marks the block rejected when the check fails, so PDF behaviour and messages are unchanged.

**Becoming domain objects**: `OrderImporter` creates `Order` and `OrderLine` only after validation succeeds, with `Status = Ready`, `ImportedAt = now` and `ImportSource` from the calling service.

---

## TCGplayer adapter types (Infrastructure/Tcgplayer, not persisted)

Response DTOs declare **only** the fields read (research.md §5–§7). There is no `customer`, `shippingAddress`, `orderValue`, `email` or name property anywhere, so those values are never bound into objects.

| DTO | Fields declared |
|---|---|
| Envelope `TcgplayerResponse<T>` | `success`, `errors`, `results`, `totalItems?` |
| Token | `access_token`, `.expires` |
| Store self | `sellerKey` |
| Manifest | `orderStatusTypes[]`, `orderPickupStatusTypes[]`, `orderTypes[]`, each `{id, name}` |
| Order details | `orderNumber`, `productCount` |
| Order item | `skuId`, `categoryName`, `productName`, `groupName`, `condition`, `printing`, `isFoil`, `language`, `rarity`, `quantity`, `productImageUrl` |
| SKU | `skuId`, `productId` |
| Product | `productId`, `imageUrl`, `extendedData[] {name, value}` |

**Open orders** (FR-004, research.md §3): `TcgplayerOpenOrderIds` holds the ids the manifest resolved for the configured open order statuses, open pickup statuses and order types. The search sends all three as its filters, and its result is the list of open orders; no order's details are re-checked.

`TcgplayerOptions` (configuration section `Tcgplayer`) is defined in [contracts/configuration.md](contracts/configuration.md).

---

## State and lifecycle

API-imported orders enter the existing lifecycle at **Ready** and move through it exactly as PDF orders do: Ready → InProgress (claimed) → Picked or NeedsAttention → Packed. This feature adds no order state.

It also never updates or deletes an existing order. Changes and cancellations on TCGplayer after import are out of scope (spec, Clarifications 2026-10-08).
