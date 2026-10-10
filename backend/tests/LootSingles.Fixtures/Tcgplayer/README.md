# Synthetic TCGplayer fixtures

Every file here is **synthetic**: written by hand from TCGplayer's published v1.39.0 schema (see `specs/020-tcgplayer-api-import/contracts/tcgplayer-upstream.md`), with invented values only. None of it is a recording of a live response.

Why: the TCGplayer API agreement forbids putting API data into third-party generative AI tools, including AI coding assistants (CLAUDE.md, "TCGplayer API Agreement"; research.md section 4f). Real responses must never be pasted, saved or copied into this folder. Order numbers are `SYN-NNNN-A1`, SKUs are `70000nn`, product ids are `80000nn`, and names are invented.

Internal consistency: order numbers in the search pages match `order-details.json` and the `items-*` files; SKUs in the `items-*` files match `skus.json` (except the deliberately unknown one); product ids in `skus.json` match `products.json`.

Counting rules the fixtures follow: `productCount` in order details is the sum of line quantities (units); `totalItems` on an items endpoint counts lines; `totalItems` on the search endpoint counts orders.

| File | Purpose |
|---|---|
| `token.json` | `POST /token` response: `access_token`, `expires_in`, `.issued`, `.expires`. |
| `stores-self.json` | `GET /stores/self`: the `sellerKey` (store key). |
| `manifest.json` | Orders manifest: `orderStatusTypes` including `Ready To Ship` (id 2). |
| `search-page1.json` | Order search, first page: 5 order numbers, `totalItems` 10. |
| `search-page2.json` | Order search, second page: the other 5 order numbers, `totalItems` 10. |
| `order-details.json` | Order details for all 10 orders, with invented `customer`, `shippingAddress` and `orderValue` objects that must never be stored (SC-004). `SYN-0006-A1` has `productCount` 5 against a line quantity of 2. |
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
