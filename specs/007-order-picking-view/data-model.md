# Data Model: Order Picking Detail View

No database tables or columns are added or changed. This feature exposes the existing `Order`
and `OrderLine` entities (feature 001) through one new narrow read projection.

## Existing persisted entities (read-only in this feature)

### Order

| Field | Type | Rule |
|---|---|---|
| `Id` | integer | Stable internal identifier; used as the detail view's `orderId` |
| `TcgplayerOrderId` | string | Required, unique, displayed |
| `Status` | `OrderStatus` | Displayed as-is; this feature does not change it |
| `OrderLines` | collection | Loaded and fully exposed by this feature (unlike the existing list views, which do not load it) |

No customer/shipping field exists on `Order` — nothing new is exposed to pickers by loading `OrderLines` here.

### OrderLine

| Field | Type | Rule |
|---|---|---|
| `Id` | integer | Internal identifier; used as the React list key, not otherwise displayed |
| `ProductName` | string | Card identity — required, always displayed |
| `Set` | string | Required, always displayed |
| `CollectorNumber` | string | Required; not called out in the spec's requirements, but harmless/available if useful for card identity |
| `Rarity` | string? | Optional on the domain entity; not part of this feature's read projection or FR-009 — not displayed by this view at all |
| `Condition` | string | Required, always displayed |
| `Variant` | string? | Optional — omitted from the line when absent (FR-009) |
| `Quantity` | integer | Required; drives the >1 emphasis (FR-003) |

## Order detail (new read projection)

| Field | Type | Rule |
|---|---|---|
| `orderId` | integer | Existing `Order.Id` |
| `tcgplayerOrderId` | string | Existing identifier |
| `lines` | array of order line detail | One entry per `OrderLine`, in a stable order (e.g., by `Id`) |

### Order line detail (nested in the above)

| Field | Type | Rule |
|---|---|---|
| `productName` | string | From `OrderLine.ProductName` |
| `set` | string | From `OrderLine.Set` |
| `variant` | string \| null | From `OrderLine.Variant`; `null` when absent — frontend omits the field rather than rendering an empty value (FR-009) |
| `condition` | string | From `OrderLine.Condition` |
| `quantity` | integer | From `OrderLine.Quantity`; frontend applies the >1 emphasis (color + non-color cue) when this exceeds 1 |

`OrderDetail` (and its nested line shape) is a new read-only Application-layer record returned by
`IOrderRepository.GetByIdAsync`, mirroring the existing `OrderListItem` projection pattern
(`OrderRepository.GetAllAsync`). It is not a Domain entity, is never tracked, and implies no new
persisted concept — it exists solely to shape this feature's read.

`GetByIdAsync` returns `null` when no `Order` with the given `Id` exists; the controller translates
that into an HTTP `404` (see `contracts/order-detail-api.md`), and the frontend renders a distinct
"not found" state (FR-008) rather than the generic load-error state.

## Card image (not a data field)

No card-image field is added to any model. The detail view renders a static, neutral placeholder
element in the position a card image would occupy, for every line — this is presentation only, not
data sourced from the order.
