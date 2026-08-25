# Contract: Order Detail API (extended)

This extends feature 007's `specs/007-order-picking-view/contracts/order-detail-api.md` — same
endpoint, same auth, same error shapes. Only the `200 OK` response body's fields change: four
fields are added, nothing existing is removed, renamed, or reshaped.

## `GET /api/orders/{orderId}` (unchanged route, auth, and error responses)

**Auth required**: Yes — unchanged from feature 007 (any authenticated employee, no role
restriction).

**Path parameter**: `orderId` — unchanged.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | An order with this id exists | `{ "orderId": 42, "tcgplayerOrderId": "F8433182-7B9B3B-BC75E", "status": "ready", "lines": [ { "productName": "Genesect ex", "productLine": "Pokemon", "set": "SV: Black Bolt", "collectorNumber": "#067/086", "rarity": "Double Rare", "variant": "Holofoil", "condition": "Near Mint", "quantity": 3 }, ... ] }` — new fields relative to feature 007: `status` (order level); `productLine`, `collectorNumber` (always present strings), and `rarity` (`null` when absent, same treatment as `variant`) per line |
| `404 Not Found` | No order with this id exists | Unchanged from feature 007: `{ "error": "order_not_found" }` |
| `401 Unauthorized` | No session, expired session, or session for a now-deactivated employee | Unchanged from feature 007: `{ "error": "not_authenticated" }` |

No request body. No query parameters. No field on this response ever contains a card image URL or
reference — unchanged from feature 007; this feature adds no image work.

`status` serializes using the app's existing global `JsonStringEnumConverter` (camelCase), the
same convention already used by `GET /api/orders`'s `status` field — no new serialization
behavior is introduced.
