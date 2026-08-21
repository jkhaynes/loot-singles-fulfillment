# Contract: Dashboard API

Backend HTTP surface for the Dashboard's live data (US2). Same-origin, cookie-based (consistent with feature 002's auth-api.md).

## `GET /api/dashboard`

**Auth required**: Yes — any authenticated employee, regardless of role (FR-009). No `[Authorize(Roles = ...)]` restriction, unlike the Manager/Admin-only employee-management endpoints.

**Purpose**: Returns the Ready section's live count and per-order summary (FR-004). Does not return anything for In Progress, Needs Attention, or Picked — those have no backing data yet (`/speckit-clarify` 2026-08-21, data-model.md) and are rendered as static placeholders entirely on the frontend.

**Responses**:

| Status | Condition | Body |
|---|---|---|
| `200 OK` | Valid session | `{ "ready": { "count": 3, "orders": [ { "orderId": 42, "tcgplayerOrderId": "F8433182-7B9B3B-BC75E", "productCount": 2, "totalQuantity": 5 } ] } }` — `orders` is `[]` when `count` is `0` (empty state, US2 AC3) |
| `401 Unauthorized` | No session, expired session, or session for a now-deactivated employee | `{ "error": "not_authenticated" }` (consistent with `GET /api/auth/me`'s existing shape) |

No request body. No query parameters — this endpoint is scoped to exactly what the Dashboard's Ready section needs (research.md §1); it is not a general, filterable Orders API.
