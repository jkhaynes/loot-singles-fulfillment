# Quickstart: Pick Completion

Manual validation scenarios exercising the acceptance scenarios in `spec.md`. Run after
`/speckit-implement` completes, using `scripts/start-dev.ps1` to start both servers against the
local LocalDB override (see project memory on the local-DB testing convention).

## Prerequisites

- Backend and frontend running (`scripts/start-dev.ps1`).
- At least one employee account claimed/logged in as a Picker.
- At least one `Ready` order imported with ≥2 lines (use an existing multi-line TCGplayer import
  fixture from features 001-006's test data, or claim any current `Ready` seeded order).

## Scenario 1 — Happy path: confirm every line, order becomes Picked (US1, FR-001, FR-007)

1. Pick Next Order (or Choose Order) to claim a `Ready` order with N lines.
2. Confirm the order detail screen shows a "Picked" control on every line, no line pre-marked.
3. Tap/click "Picked" on each line in turn.
4. **Expect**: after the last line, the order's status updates to `Picked` without navigating away
   from the order-detail screen (SC-001), and the Dashboard's Picked tile count increases by 1.

## Scenario 2 — Report an issue instead of a false confirmation (US2, FR-002, FR-003, FR-009)

1. Claim a `Ready` order with ≥2 lines.
2. On one line, choose "Report Issue," select an issue type (e.g., Card Not Found), leave the note
   blank, submit.
3. Confirm every other line as Picked.
4. **Expect**: order status is `NeedsAttention`, never `Picked`, even though every other line is
   confirmed (FR-009) — the Dashboard's Needs Attention tile shows this order with the flagged
   product name (FR-014), not the Picked tile.

## Scenario 3 — Release and re-claim a Needs Attention order later (US1 AC5, FR-004, FR-005, FR-006)

1. From Scenario 2's state, release the order.
2. **Expect**: order still shows as `NeedsAttention` (not reset to `Ready`) — Choose Order surfaces
   it; Pick Next Order does **not** offer it (FR-005).
3. As the same or a different picker, use Choose Order to re-claim it.
4. **Expect**: the previously-flagged line still shows its reported issue details (FR-011); order
   status reflects `NeedsAttention` immediately (not `InProgress`) until that line is resolved.
5. Change the flagged line's outcome to "Picked."
6. **Expect**: order status flips to `Picked` immediately (no other unresolved lines) — matches the
   clarified FR-008 numeric example (10-line/2-issue) at smaller scale.

## Scenario 4 — 10-line / 2-issue example from spec.md FR-008

1. Claim an order with 10 lines (or simulate by editing 8 lines' outcomes via repeated Scenario 1
   steps on a fixture order, then reporting issues on 2).
2. Report issues on exactly 2 lines; confirm the remaining 8 as Picked.
3. **Expect**: order status is `NeedsAttention` (not partially-Picked or any other state).
4. Resolve both flagged lines (mark Picked).
5. **Expect**: order status flips to `Picked` the moment the second line is resolved — no separate
   "finalize" action.

## Scenario 5 — Authorization: only the claim holder can record outcomes (FR-010)

1. As Picker A, claim an order.
2. As Picker B (separate browser/session), attempt to call `POST
   /api/orders/{orderId}/lines/{lineId}/pick` directly (e.g., via browser dev tools or a REST
   client) for that order's line.
3. **Expect**: `409 NotYourClaim`, and the line's outcome is unchanged.

## Scenario 6 — Dashboard live counts (US3, FR-013)

1. Note current Dashboard counts for In Progress, Needs Attention, Picked.
2. Perform Scenario 1 (creates one more Picked) and Scenario 2 (creates one more Needs Attention)
   in two different orders.
3. **Expect**: Dashboard counts update to reflect both changes without a manual refresh needed
   beyond the existing polling/refresh mechanism already used by the Ready tile.

## Scenario 7 — Mobile parity (SC-008)

1. Repeat Scenario 1 at a narrow viewport width (e.g., 375px, matching feature 007/008's existing
   responsive breakpoints) or on an actual mobile device.
2. **Expect**: identical controls and tap count as desktop — no separate mobile-only flow, no
   horizontal scrolling to reach a line's controls.
