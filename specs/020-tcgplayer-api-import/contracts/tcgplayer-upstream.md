# Contract: Calls to the TCGplayer Seller API

**Feature**: `020-tcgplayer-api-import`. This is the complete, closed list of TCGplayer requests the application makes. Any request not listed here is a defect. Source: TCGplayer's published documentation, v1.39.0 (research.md); never live responses.

## Rules for every request (Legacy Qualified Addendum; `CLAUDE.md`)

- **`User-Agent: LootSinglesFulfillment/{version} (Loot Investments LLC)`**: set on the client, so no request can leave without it (FR-022).
- **Counted by the shared rate limiter**: at most `Tcgplayer:CallsPerMinute` (default 120, hard ceiling 300) in any 60-second window, across all employees and concurrent imports in the process. A request waits rather than exceeding the budget (FR-021).
- **`Authorization: bearer {token}`**: on everything except the token request itself.
- **Base address**: `Tcgplayer:BaseUrl` (default `https://api.tcgplayer.com/`). API paths take the `Tcgplayer:ApiVersion` prefix (default `v1.39.0`). The token path has none.
- **Read-only**: apart from the token request, every call is a `GET`. The application never writes to TCGplayer.
- **Never logged**: request or response bodies, tokens, keys.

## Allowed requests

| # | Request | Purpose | When | Calls per import |
|---|---|---|---|---|
| 1 | `POST /token`, form `grant_type=client_credentials&client_id={PublicKey}&client_secret={PrivateKey}`, header `X-Tcg-Access-Token: {AccessToken}` | Bearer token from the **existing** credentials | No cached token, the cached token is within 24 h of expiry, or a 401 occurred (once) | Usually 0, and 1 per ~14 days |
| 2 | `GET /{v}/stores/self` | Resolve the store key | Only when `Tcgplayer:StoreKey` is unset; cached for the process lifetime | 0 or 1 |
| 3 | `GET /{v}/stores/{storeKey}/orders/manifest` | Map open status names to ids | Each import | 1 |
| 4 | `GET /{v}/stores/{storeKey}/orders?orderStatusIds={ids}&offset={o}&limit={PageSize}` | Open order numbers | Each import, paged | ⌈open orders ÷ PageSize⌉ |
| 5 | `GET /{v}/stores/{storeKey}/orders/{n1,n2,…}` | `productCount` cross-check; customer fields are **not bound** | Per search page, **new** orders only | ⌈new ÷ PageSize⌉ |
| 6 | `GET /{v}/stores/{storeKey}/orders/{orderNumber}/items?includeItemDetails=true&offset={o}&limit={PageSize}` | Lines | Per **new** order, paged | ≥ 1 per new order |
| 7 | `GET /{v}/catalog/skus/{sku1,sku2,…}` | SKU → product | Per batch of 50 distinct SKUs on new orders | ⌈SKUs ÷ 50⌉ |
| 8 | `GET /{v}/catalog/products/{p1,p2,…}?getExtendedFields=true` | Collector number, rarity, image | Per batch of 50 distinct products | ⌈products ÷ 50⌉ |

**Typical day** (50 new orders, about 120 distinct cards): 1 + 1 + 1 + 50 + 3 + 3 ≈ **59 calls**, within one window.
**Large backlog** (200 new orders): about 225 calls, roughly 2 minutes at 120 a minute.

## Forbidden requests

These are asserted by tests against the stub handler:

- `POST /app/authorize/{code}`, or any other request that creates or replaces credentials, store authorizations or integrations. Store authorization is a one-time human setup step (research.md §1), never something the application does.
- Any `POST`, `PUT`, `PATCH` or `DELETE` other than #1.
- Any request about an API-imported line to a host other than the TCGplayer API, including Scryfall, TCGdex and Lorcast. The image URL stored on a line is loaded later by the employee's browser from TCGplayer's own CDN; the server never fetches it.

## Response handling

| Outcome | Treatment |
|---|---|
| 200, or 207 for catalog partial results | Parse into DTOs that declare only the fields read (data-model.md). IDs listed in `errors` mean "not found". |
| 404 on catalog #7 or #8 | Every ID in that batch is not found, so those lines get no number and no image. |
| 401 | Refresh the token once and retry the request once. A second 401 means `AccessRefused`. |
| 403 | `AccessRefused` |
| 429, 5xx, timeout (30 s per request), network error | `Unavailable` |
| Unparseable body, missing required field, paging stall | `ResponseInvalid` (attempt-wide or per order; data-model.md) |
