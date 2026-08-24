# Contract: Order Detail API

Backend HTTP surface for the order picking detail view (US1). Same-origin, cookie-based
(consistent with feature 002's `auth-api.md`), extending the existing `Orders` slice
(`orders-api.md`-equivalent behavior already established by `GET /api/orders`).

## `GET /api/orders/{orderId}`

**Auth required**: Yes — any authenticated employee, regardless of role, consistent with the
existing `GET /api/orders` and `GET /api/dashboard` endpoints. No `[Authorize(Roles = ...)]`
restriction.

**Purpose**: Returns the full detail for one order — its TCGplayer identifier and every product
line's card identity, set, variant, condition, and quantity (FR-001, FR-002) — so the frontend can
render the read-only picking detail view. This is strictly additive to the existing `Orders` slice;
it does not change `GET /api/orders`'s behavior or shape.

**Path parameter**: `orderId` — the order's internal integer identifier (`Order.Id`), the same
value already returned as `orderId` by `GET /api/orders` and `GET /api/dashboard`.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | An order with this id exists | `{ "orderId": 42, "tcgplayerOrderId": "F8433182-7B9B3B-BC75E", "lines": [ { "productName": "Genesect ex", "set": "SV: Black Bolt", "variant": "Holofoil", "condition": "Near Mint", "quantity": 3 }, ... ] }` — `variant` is `null` when the order line has none (FR-009); `lines` always has at least one entry, since an `Order` cannot exist without at least one `OrderLine` |
| `404 Not Found` | No order with this id exists | `{ "error": "order_not_found" }` — the frontend renders this as the "not found" state (FR-008), distinct from a generic load error |
| `401 Unauthorized` | No session, expired session, or session for a now-deactivated employee | `{ "error": "not_authenticated" }` (consistent with `GET /api/auth/me`'s existing shape) |

No request body. No query parameters. No field on this response ever contains a card image URL or
reference — the placeholder shown in its place is presentation-only and is not part of this
contract (research.md §4).
