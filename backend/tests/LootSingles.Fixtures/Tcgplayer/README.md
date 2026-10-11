# Synthetic TCGplayer fixtures

Every file here is **synthetic**: written by hand from TCGplayer's published v1.39.0 schema (see `specs/020-tcgplayer-api-import/contracts/tcgplayer-upstream.md`), with invented values only. None of it is a recording of a live response.

Why: the TCGplayer API agreement forbids putting API data into third-party generative AI tools, including AI coding assistants (CLAUDE.md, "TCGplayer API Agreement"; research.md section 4f). Real responses must never be pasted, saved or copied into this folder. Order numbers are `SYN-NNNN-A1`, SKUs are `70000nn`, product ids are `80000nn`, and names are invented.

Internal consistency: order numbers in the search pages match `order-details.json`, and the ten open ones match the `items-*` files; SKUs in the `items-*` files match `skus.json` (except the deliberately unknown one); product ids in `skus.json` match `products.json`.

Counting rules the fixtures follow: `productCount` in order details is the sum of line quantities (units); `totalItems` on an items endpoint counts lines; `totalItems` on the search endpoint counts orders.

| File | Purpose |
|---|---|
| `token.json` | `POST /token` response: `access_token`, `expires_in`, `.issued`, `.expires`. |
| `stores-self.json` | `GET /stores/self`: the `sellerKey` (store key). |
| `manifest.json` | Orders manifest: `orderStatusTypes` (`Processing` 1, `Ready To Ship` 2, `Delivered` 4, …), `orderPickupStatusTypes` (`Received` 1, `Picked Up` 4, …), `orderDeliveryTypes` (`InStorePickup` 4; the import doesn't read it, the integration stub uses its id) and `orderTypes` (`Normal` 1, `Direct` 2). |
| `search-page1.json` | Order search with all three filters, first page (offset 0): 5 order numbers, `totalItems` 10. |
| `search-page2.json` | Order search with all three filters, second page (offset 5): 5 order numbers, `totalItems` 10. |
| `order-details.json` | Order details for all 13 orders, with invented `customer`, `shippingAddress` and `orderValue` objects that must never be stored (SC-004). Each row carries `orderStatusTypeId`, `orderDeliveryTypeId`, `orderPickupStatusTypeId` (null or absent on shipped orders) and `orderTypeId`, which the import doesn't bind; the integration stub's search filters on them. `SYN-0004-A1` is an in-store pickup order that is Received (order status Processing); `SYN-0005-A1` is a shipped order in Processing. `SYN-0006-A1` has `productCount` 5 against a line quantity of 2. |
| (leaked orders) | `SYN-0011-A1` (shipped, Delivered), `SYN-0012-A1` (in-store pickup, Picked Up, order status Processing) and `SYN-0013-A1` (Direct, Ready To Ship) are **not open**. TCGplayer's search filters are each partial (contracts/tcgplayer-upstream.md), so a search missing the order-status, pickup-status or order-type filter respectively would return one; `TcgplayerStubHandler` models that. A correct search never returns them, so they have no `items-*` file. |
| `items-normal.json` | `SYN-0001-A1`: one ordinary line. |
| `items-quantity-greater-than-one.json` | `SYN-0002-A1`: lines with quantities 1 and 2 (productCount 3). |
| `items-foil.json` | `SYN-0003-A1`: a foil line as live data reports it: condition "Near Mint Foil", printing "Foil", `isFoil` true. |
| `items-non-english.json` | `SYN-0004-A1`: a Japanese line. |
| `items-paged-page1.json` | `SYN-0005-A1` items, first page: 2 lines, `totalItems` 3. |
| `items-paged-page2.json` | `SYN-0005-A1` items, second page: the third line. |
| `items-count-mismatch.json` | `SYN-0006-A1`: one line of quantity 2, while order details say `productCount` 5. |
| `items-missing-product-name.json` | `SYN-0007-A1`: a line with no `productName`. |
| `items-normal-printing-lightly-played.json` | `SYN-0008-A1`: printing "Normal" with condition "Lightly Played". |
| `items-sealed.json` | `SYN-0009-A1`: an invented sealed booster box whose catalog product has no `Number`. |
| `items-catalog-not-found.json` | `SYN-0010-A1`: one line whose SKU (7000099) is unknown to the catalog, and one whose product (8000011) is. |
| `skus.json` | `GET /catalog/skus/...`: SKU to product id; 7000099 is reported in `errors` as not found. |
| `products.json` | `GET /catalog/products/...?getExtendedFields=true`: image, `Rarity` and `Number` entries; the sealed box (8000013) has no `Number`; 8000011 is reported in `errors` as not found. |

`token.json`'s `.expires` is a fixed date (2026-10-23). A test that serves it on the real clock will see an expired token once that date passes, so it must shift `.expires` forward first (as `TcgplayerStubHandler` does). A test on a `FakeTimeProvider` anchored to `.issued` needs no shift.
