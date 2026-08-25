# Data Model: Order Picking Detail — Additional Packing-Slip Fields

No database tables or columns are added or changed. This feature exposes four fields that
already exist on the persisted `Order`/`OrderLine` entities (feature 001) but were excluded from
feature 007's `OrderDetail`/`OrderLineDetail` read projection.

## Existing persisted entities (read-only in this feature, unchanged from feature 001)

### Order

| Field | Type | Rule | Change in this feature |
|---|---|---|---|
| `Id` | integer | Stable internal identifier | Unchanged |
| `TcgplayerOrderId` | string | Required, unique, displayed | Unchanged |
| `Status` | `OrderStatus` | Already persisted and already returned by `GET /api/orders` (`OrderResponse.Status`) | **Newly exposed** by `GET /api/orders/{orderId}` and displayed on the detail view (FR-004) |
| `OrderLines` | collection | Loaded and fully exposed since feature 007 | Unchanged |

### OrderLine

| Field | Type | Rule | Change in this feature |
|---|---|---|---|
| `ProductName` | string | Card identity — required, always displayed | Unchanged |
| `Set` | string | Required, always displayed | Unchanged |
| `ProductLine` | string | The game (e.g., "Pokemon", "Magic"); required on every line | **Newly exposed and displayed unconditionally** (FR-001) |
| `CollectorNumber` | string | e.g., "#067/086"; required on every line | **Newly exposed and displayed unconditionally** (FR-002) |
| `Rarity` | string? | Optional — absence does not prevent an OrderLine from existing | **Newly exposed and displayed, omitted when absent** (FR-003), following the same treatment `Variant` already has |
| `Condition` | string | Required, always displayed | Unchanged |
| `Variant` | string? | Optional — omitted from the line when absent (feature 007 FR-009) | Unchanged |
| `Quantity` | integer | Required; drives the >1 emphasis | Unchanged |

## Order detail (existing read projection, extended)

| Field | Type | Rule | Change in this feature |
|---|---|---|---|
| `orderId` | integer | Existing `Order.Id` | Unchanged |
| `tcgplayerOrderId` | string | Existing identifier | Unchanged |
| `status` | `OrderStatus` (serialized as its existing lowercase camelCase string, e.g. `"ready"`) | Existing `Order.Status`, using the same `JsonStringEnumConverter` already applied globally (`Program.cs`) | **New field** on `OrderDetail`/`OrderDetailResponse` |
| `lines` | array of order line detail | One entry per `OrderLine`, in the existing stable order (by `Id`) | Unchanged |

### Order line detail (nested in the above, extended)

| Field | Type | Rule | Change in this feature |
|---|---|---|---|
| `productName` | string | From `OrderLine.ProductName` | Unchanged |
| `productLine` | string | From `OrderLine.ProductLine` | **New field** — always present |
| `set` | string | From `OrderLine.Set` | Unchanged |
| `collectorNumber` | string | From `OrderLine.CollectorNumber` | **New field** — always present |
| `rarity` | string \| null | From `OrderLine.Rarity`; `null` when absent — frontend omits the field rather than rendering an empty value, mirroring `variant` | **New field** — nullable |
| `variant` | string \| null | From `OrderLine.Variant`; `null` when absent — frontend omits the field (feature 007 FR-009) | Unchanged |
| `condition` | string | From `OrderLine.Condition` | Unchanged |
| `quantity` | integer | From `OrderLine.Quantity`; frontend applies the >1 emphasis | Unchanged |

`OrderDetail`/`OrderLineDetail` remain the same read-only Application-layer records feature 007
introduced (`OrderRepository.GetByIdAsync`'s projection). Extending them with four more properties
does not change their nature: still never tracked, still implying no new persisted concept, still
existing solely to shape this feature's read.

## Card image (still not a data field)

Unchanged from feature 007: no card-image field exists on any model. This feature adds no image
work of any kind — it is a pure additional-field display change.
