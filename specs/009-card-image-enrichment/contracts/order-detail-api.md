# Contract: Order Detail API (extended again)

This extends feature 008's `specs/008-picking-detail-additional-fields/contracts/order-detail-api.md`
(itself an extension of feature 007's original contract) — same endpoint, same auth, same error
shapes. Only the `200 OK` response body's fields change: one field is added, nothing existing is
removed, renamed, or reshaped.

## `GET /api/orders/{orderId}` (unchanged route, auth, and error responses)

**Auth required**: Yes — unchanged (any authenticated employee, no role restriction).

**Path parameter**: `orderId` — unchanged.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | An order with this id exists | `{ "orderId": 42, "tcgplayerOrderId": "F8433182-7B9B3B-BC75E", "status": "ready", "lines": [ { "productName": "Genesect ex", "productLine": "Pokemon", "set": "SV: Black Bolt", "collectorNumber": "#067/086", "rarity": "Double Rare", "variant": "Holofoil", "condition": "Near Mint", "quantity": 3, "imageUrl": "https://images.pokemontcg.io/sv10/67_hires.png" }, ... ] }` — new field relative to feature 008: `imageUrl` (string, nullable) per line. `null` when no confident match was found, the match was ambiguous, the line's game has no catalog source in this phase (e.g. One Piece), or the game's catalog source was unreachable — the API makes no distinction between these cases; all render identically on the frontend (the existing feature-007 placeholder) |
| `404 Not Found` | No order with this id exists | Unchanged: `{ "error": "order_not_found" }` |
| `401 Unauthorized` | No session, expired session, or session for a now-deactivated employee | Unchanged: `{ "error": "not_authenticated" }` |

No request body. No query parameters. `imageUrl`, when present, is the external catalog
provider's own hosted image URL — this application never stores, proxies, or re-hosts image
bytes. This endpoint's response time may vary with external catalog provider latency; each
provider call is independently timed out and enriched concurrently across lines
(research.md §6–7), so no single slow/unreachable provider should dominate the response time for
an order with multiple lines across different games.
