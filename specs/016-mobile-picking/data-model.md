# Data Model: Mobile Picking Experience

**Feature**: 016-mobile-picking | **Date**: 2026-09-20

## No persisted entity changes

This feature adds **no table, no column and no migration**. Every domain entity — `Order`,
`OrderLine`, `PickingIssue`, `Employee` — is untouched.

Everything grouping, progress and the unresolved-set guard need is already persisted and
already returned by the order detail endpoint. The only new server-side shape is one read-model
field on the dashboard response.

---

## Derived shapes (client-side, not persisted)

These exist only for presentation and are computed from `OrderDetail` on load.

### `SetGroup`

One storage box's worth of an order.

| Field | Type | Notes |
|---|---|---|
| `game` | string | From `OrderLineDetail.ProductLine`. Authoritative imported data. |
| `setName` | string | From `OrderLineDetail.Set`. |
| `isSetRecorded` | boolean | `false` when the line's set is missing or blank (FR-005). Such a group sorts last within its game and is labelled explicitly. |
| `lines` | `OrderLineDetail[]` | In their existing relative order. |
| `productCount` | number | `lines.length`. |
| `cardCount` | number | Sum of `Quantity` — physical cards, not lines. |
| `resolvedProductCount` | number | Lines whose `PickOutcome` is non-null. |
| `unresolvedLines` | `OrderLineDetail[]` | Drives the FR-016 guard listing. |
| `isComplete` | boolean | `unresolvedLines.length === 0`. Never true while any line is unresolved (FR-018). |

**Ordering rules** (FR-002, FR-003, FR-004):

1. Groups sort by `game`, alphabetically by name.
2. Within a game, groups sort by `setName`, alphabetically.
3. A group with `isSetRecorded === false` sorts after all named sets of its game.
4. Lines within a group keep their existing relative order.

**Validation**: every line in the order appears in exactly one group. A line is never dropped,
whatever its set value (FR-005, Constitution V).

### `OrderProgress`

| Field | Type | Notes |
|---|---|---|
| `resolvedProducts` / `totalProducts` | number | FR-020. |
| `accountedCards` / `totalCards` | number | FR-021. Sums `Quantity`, so a line of 3 counts 3. |
| `currentSetPosition` / `currentSetSize` | number | FR-022. Position within the current group. |
| `currentSetName` | string | The box the picker is standing at. |

A line counts toward `accountedCards` when its `PickOutcome` is non-null — picked *or*
carrying a reported issue. "Accounted for" means resolved, not successfully found.

### `ViewPreference`

| Value | Meaning |
|---|---|
| `'focused'` | One product at a time. |
| `'list'` | Whole order. |
| *absent* | No deliberate choice; the size-based default applies (FR-008). |

Stored per device in `localStorage` under one key (FR-010). Reads and writes are guarded; any
failure is treated as *absent*.

---

## Server-side change

### `DashboardData.activeClaim`

One nullable field added to the dashboard response.

| Field | Type | Notes |
|---|---|---|
| `activeClaim` | `OrderSummary \| null` | The order the **authenticated employee** currently holds, or `null`. |

**Computed** from the employee's active claim directly, **not** by filtering the In Progress
section. An order retains its claim when an issue is reported (feature 015), so a held order
may sit in Needs Attention; filtering In Progress would miss exactly the picker who most needs
sending back to their order.

**Privacy**: returns only the signed-in employee's own order. No other employee's identifier is
added to any payload (Constitution VII, PRD §27).

**Shape**: reuses the existing `OrderSummary` record (`orderId`, `tcgplayerOrderId`,
`productCount`, `totalQuantity`) rather than introducing a parallel type.

---

## State transitions

None. This feature records no new outcome and changes no status.

`PickOutcome` remains `null` → `Picked` | `HasIssue`, written only by the existing pick and
report-issue endpoints. **Navigation writes nothing** (FR-011) — the single most important
invariant in this feature, and the one the tests assert positively rather than by absence.

Order status derivation is untouched; it continues to follow `OrderStatusComputation`.
