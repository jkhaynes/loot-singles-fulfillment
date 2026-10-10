# Contract: Calls to the TCGplayer Seller API

**Feature**: `020-tcgplayer-api-import`. This is the complete, closed list of TCGplayer requests the application makes. Any request not listed here is a defect. Source: TCGplayer's published documentation, v1.39.0 (research.md); never live responses.

## Rules for every request (Legacy Qualified Addendum; `CLAUDE.md`)

- **`User-Agent: LootSinglesFulfillment/{version} (Loot Investments LLC)`**: set on the client, so no request can leave without it (FR-022).
- **Handler pipeline**: authentication, then rate limit, then a per-attempt timeout of 30 seconds, then the primary handler. `HttpClient.Timeout` is infinite, so waiting for a rate-limit slot never times a request out. The token request and a 401 retry each pass through the limiter and the timeout.
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
| 3 | `GET /{v}/stores/{storeKey}/orders/manifest` | Map the configured open order status, open pickup status and order type names, and the `InStorePickup` delivery type, to ids | Each import | 1 |
| 4 | `GET /{v}/stores/{storeKey}/orders?orderStatusIds={ids}&pickupStatusIds={ids}&orderTypeIds={ids}&offset={o}&limit={PageSize}` | Candidate order numbers (the filters are partial; see below) | Each import, paged | ⌈searched orders ÷ PageSize⌉ |
| 5 | `GET /{v}/stores/{storeKey}/orders/{n1,n2,…}` | Openness (FR-004) for **every** searched order, then the `productCount` cross-check for **new** open orders; customer fields are **not bound** | Each import, in batches of PageSize: once over all searched orders, again over the new open ones | ⌈searched ÷ PageSize⌉ + ⌈new ÷ PageSize⌉ |
| 6 | `GET /{v}/stores/{storeKey}/orders/{orderNumber}/items?includeItemDetails=true&offset={o}&limit={PageSize}` | Lines | Per **new** order, paged | ≥ 1 per new order |
| 7 | `GET /{v}/catalog/skus/{sku1,sku2,…}` | SKU → product | Per batch of 50 distinct SKUs on new orders | ⌈SKUs ÷ 50⌉ |
| 8 | `GET /{v}/catalog/products/{p1,p2,…}?getExtendedFields=true` | Collector number, rarity, image | Per batch of 50 distinct products | ⌈products ÷ 50⌉ |

**Typical day** (50 new orders, about 120 distinct cards): 1 + 1 + 1 + 1 + 1 + 50 + 3 + 3 ≈ **61 calls**, within one window.
**Large backlog** (200 new orders): about 235 calls, roughly 2 minutes at 120 a minute.

## The search filters are partial

Search (#4) takes comma-separated id lists named `orderStatusIds`, `pickupStatusIds` and `orderTypeIds` (TCGplayer documentation, "Search Orders"). Live finding, 2026-10-10, from a counts-only diagnostic a person ran:

- `orderStatusIds` narrows only orders that are **not** in-store pickup, and returns every in-store pickup order whatever its status;
- `pickupStatusIds` narrows only in-store pickup orders, and returns every shipped order;
- `orderTypeIds` is partial in the same way.

Sending only `orderStatusIds` imported about 1,568 orders that were not open, mostly long-collected pickup orders. Sent together, `orderStatusIds` (open order statuses) and `pickupStatusIds` (open pickup statuses) matched the seller portal's open-orders count. So the search always sends all three, and the search result is only a candidate list: each order's own details row (#5) decides whether it is open (FR-004, research.md §3) before anything else is fetched for it. An order that is not open is skipped silently: no items call, no result, no count.

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
