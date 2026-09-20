# Contract: Dashboard API

**Feature**: 016-mobile-picking | **Date**: 2026-09-20

One additive change to one existing endpoint. No other API contract changes in this feature.

---

## `GET /api/dashboard`

**Auth**: authenticated employee (unchanged).

### Change

Adds one top-level nullable field, `activeClaim`. Every existing field is unchanged, so the
change is backward compatible for any client that ignores it.

### Response

```jsonc
{
  "ready":          { "count": 14, "orders": [ /* OrderSummary[] */ ] },
  "inProgress":     { "count": 2,  "orders": [ /* OrderSummary[] */ ] },
  "needsAttention": { "count": 1,  "orders": [ /* NeedsAttentionOrderSummary[] */ ] },
  "picked":         { "count": 6,  "orders": [ /* OrderSummary[] */ ] },

  // NEW — the order THIS employee holds, or null.
  "activeClaim": {
    "orderId": 1021,
    "tcgplayerOrderId": "F8433182-7B9B3B-BC75E",
    "productCount": 5,
    "totalQuantity": 8
  }
}
```

`activeClaim` is `null` when the employee holds no order.

### Behaviour

| Rule | Detail |
|---|---|
| Whose claim | Only the **authenticated** employee's. Never another employee's order. |
| How it is found | From the employee's active claim directly — **not** by filtering `inProgress`. |
| Section independence | A held order may appear in `needsAttention` rather than `inProgress`, because reporting an issue retains the claim (feature 015). `activeClaim` must still return it. |
| Shape | The existing `OrderSummary` record, reused rather than duplicated. |
| Cardinality | At most one. One active claim per employee is already enforced server-side (feature 013). |

### Privacy

No employee identifier for any *other* employee is added by this change (Constitution VII,
PRD §27). The field is the signed-in employee's own order or nothing.

### Consumer

`DashboardPage` uses it for FR-026: when `activeClaim` is non-null, the dashboard offers to
resume that order rather than offering to start another.

---

## Endpoints used but NOT changed

Listed so the implementation does not invent replacements for things that already work.

| Purpose | Endpoint | Introduced by |
|---|---|---|
| Read an order | `GET /api/orders/{orderId}` | Payload already carries `ProductLine`, `Set`, `Quantity`, `PickOutcome` — sufficient for all grouping and progress |
| Claim an order (FR-024) | `POST /api/orders/{orderId}/claim` | Feature 013. **Exists and is concurrency-safe; it was simply never surfaced on the order screen.** |
| Pick Next (FR-028) | `POST /api/orders/pick-next` | Feature 013, unchanged |
| Record a pick | `POST /api/orders/{orderId}/lines/{lineId}/pick` | Feature 015, unchanged |
| Report an issue | `POST /api/orders/{orderId}/lines/{lineId}/report-issue` | Feature 015, unchanged |
| Release a claim | `POST /api/orders/{orderId}/release` | Feature 013, unchanged |

### Claim response handling (FR-025, FR-027)

The claim endpoint's existing outcomes are surfaced, not re-implemented:

- **Granted** — picking actions become available on the order.
- **Already claimed by someone else** — the holder's name is shown (FR-025). This remains the
  authoritative answer; two simultaneous attempts still resolve server-side, and exactly one
  wins.
- **Caller already holds another order** — the application explains this rather than offering a
  claim action known to fail (FR-027).

FR-027's "don't offer an action that will fail" is a presentation improvement over an
authoritative server answer. It is **not** a client-side substitute for the server rule
(Constitution VI).
