# Phase 0 Research: Pick Completion

No `NEEDS CLARIFICATION` markers remain in the Technical Context — `/speckit-clarify` already
resolved the one genuine open question (order-status derivation and its persistence across
release/re-claim). This research covers the concrete technical decisions needed to implement the
spec consistently with this codebase's existing patterns, and identifies the exact points in
feature 013's existing order-claiming code this feature must correct to satisfy FR-005/FR-006.

## 1. Order status is a stored column, kept correct by recomputing it inside every write that could change it

**Decision**: `Order.Status` remains a stored column (unchanged from feature 001/013) rather than
becoming a computed-at-read-time value. Every database write that could change whether any of an
order's lines has an unresolved issue — recording a line outcome, releasing, force-releasing, or
claiming a specific (possibly Needs Attention) order — recomputes `Status` from the order's
current line outcomes as part of that same write, via a correlated subquery:

```csharp
order.OrderLines.Any(line => line.PickOutcome == PickOutcome.HasIssue)
    ? OrderStatus.NeedsAttention
    : OrderStatus.Ready // or InProgress, depending on claim state — see research.md §5
```

**Rationale**: `Order.Status` is already an indexed, queried column
(`ClaimNextAvailableAsync`'s `WHERE order.Status == OrderStatus.Ready`) — switching to a fully
computed-at-read-time value would require reworking that existing query and lose the index.
Recomputing on every write that could invalidate it keeps the column always correct without
that rework, and — critically — every write that touches line outcomes happens only while the
actor holds the order's exclusive claim (FR-010, already enforced), so there is exactly one writer
at a time per order for this feature's own operations; no additional locking beyond what claiming
already provides is needed for the recompute itself to stay race-free.

**Alternatives considered**:

- *Compute status at read time in every query that returns an order*: avoids any risk of a stale
  stored value, but would require reworking `ClaimNextAvailableAsync`'s indexed `WHERE
  Status == Ready` filter into a correlated-subquery filter (losing the index) and duplicating the
  same "does this order have an unresolved issue" expression at every read site instead of once at
  each write site. Rejected as a larger, riskier change than this feature calls for.
- *A separate stored "HasUnresolvedIssue" boolean flag on Order, independently maintained*: this is
  exactly the "independently-tracked flag that could drift out of sync" the clarification session
  explicitly ruled out (spec.md Clarifications) — rejected on that basis alone.

## 2. Recording a line outcome: one explicit transaction, mirroring the existing conditional-update pattern

**Decision**: A new `IPickingRepository.RecordOutcomeAsync(int orderLineId, int actorEmployeeId,
PickOutcomeChange change, CancellationToken)` performs, inside one explicit transaction
(`context.Database.BeginTransactionAsync`):

1. If `change` is an issue report: `Add` a new `PickingIssue` row (append-only) and
   `SaveChangesAsync` once, capturing its generated `Id`.
2. `ExecuteUpdateAsync` the target `OrderLine`, setting `PickOutcome`,
   `PickOutcomeRecordedByEmployeeId`, `PickOutcomeRecordedAt`, and `CurrentPickingIssueId` (the new
   `PickingIssue`'s `Id`, or `null` if marking picked) — **conditioned on** the line's parent
   `Order.ClaimedByEmployeeId == actorEmployeeId`. If this affects 0 rows, the actor doesn't
   currently hold the order's claim (or the line doesn't exist); roll back and report that instead
   of committing a mutation that shouldn't have happened.
3. If step 2 affected 1 row: `ExecuteUpdateAsync` the parent `Order`'s `Status` via the
   research.md §1 subquery, in the same transaction.
4. Re-read the updated `Order` (with lines), commit, return it.

**Rationale**: Mirrors `OrderRepository.ExecuteConditionalUpdateAsync` (013, hardened by that
feature's own code-design-review M1 finding) exactly: the authorization check and the mutation are
one conditional `ExecuteUpdateAsync`, not a separate "check, then write" pair — the row lock held
from that `UPDATE` until `COMMIT` is what makes it race-free against a concurrent release/reclaim,
not application-level reasoning about timing. The one addition this feature needs beyond 013's
shape — inserting the `PickingIssue` row — happens first, inside the same transaction, so if the
authorization check in step 2 fails, the whole transaction rolls back and that insert never
persists either.

**Alternatives considered**:

- *Load the `OrderLine` as a tracked entity, check its parent order's claim in application code,
  then mutate and `SaveChangesAsync`*: this is exactly the "check, then write" shape as two
  separate round trips that feature 013's M1 finding already proved unsafe under concurrency (a
  release/reclaim could land between the check and the save). Rejected.

## 3. `OrderLine` gains its pick-outcome fields directly, not a separate 1:1 entity

**Decision**: `PickOutcome` (nullable enum), `PickOutcomeRecordedByEmployeeId` (nullable),
`PickOutcomeRecordedAt` (nullable), and `CurrentPickingIssueId` (nullable FK) are added directly to
the existing `OrderLine` entity, not split into a separate `OrderLinePickOutcome` table.

**Rationale**: Mirrors the exact precedent feature 013 already set for `Order` itself —
`ClaimedByEmployeeId`/`ClaimedAt` were added directly to `Order` rather than a separate
`OrderClaim` entity, for the same reason: it's a genuine 1:1 "current state" relationship, and a
join for the single hottest-path query (view an order's lines with their current outcome) buys
nothing a direct nullable column doesn't already give more simply.

**Alternatives considered**:

- *A separate `OrderLinePickOutcome` table, 1:1 with `OrderLine`*: would keep `OrderLine` (TCGplayer
  import data) conceptually "pure," but this codebase doesn't apply that separation to the
  structurally identical `Order`/claim relationship, and introducing it only here would be an
  inconsistent, narrower convention for the same kind of relationship. Rejected for consistency
  (constitution Principle XII).

## 4. `PickingIssue` is a separate, append-only entity — this one genuinely needs its own table

**Decision**: Unlike the current-outcome fields (§3), the actual issue *reports* are a separate
`PickingIssue` entity: one new row per report, never updated or deleted, linked from `OrderLine`
via `CurrentPickingIssueId` (which points at whichever `PickingIssue` row is the line's current
one, or `null`).

**Rationale**: FR-011 requires every reported issue's details to survive a later revision of that
line's outcome — a requirement no "current state" field can satisfy on its own. This is not the
same relationship as §3; it's a genuine one-line-to-many-historical-reports relationship that has
no existing single-row analogue elsewhere in this codebase to reuse.

**Issue type storage**: `PickingIssueType` uses `.HasConversion<string>()` into the enum's
column, matching `EmployeeAuditActionType`'s established precedent for a taxonomy-style enum
whose values are worth being human-readable directly in the database (unlike the plain-int
`OrderStatus`/new `PickOutcome`, which are simple two/four-value state enums matching `OrderStatus`'s
own existing plain-int convention).

## 5. Feature 013's `ClaimSpecificAsync`, `ReleaseAsync`, and `ForceReleaseAsync` must stop hardcoding `Status`

**Decision**: `OrderRepository.ClaimSpecificAsync`, `ReleaseAsync`, and `ForceReleaseAsync`
currently `SetProperty(order => order.Status, OrderStatus.InProgress)` (claim) or `SetProperty(...,
OrderStatus.Ready)` (release/force-release) unconditionally. Both are replaced with the research.md
§1 subquery: claiming sets `InProgress` unless the order's lines already have an unresolved issue
(a re-claimed Needs Attention order stays Needs Attention, per spec.md's clarified FR-006/FR-008);
releasing/force-releasing sets `Ready` unless an unresolved issue remains, in which case it stays
`NeedsAttention` (per FR-005 — a released Needs Attention order must remain discoverable and
correctly labeled, not silently reset to looking like fresh, unstarted work).

`ClaimNextAvailableAsync` (Pick Next Order) needs **no change** — its candidate query already
filters `WHERE order.Status == OrderStatus.Ready`, so it structurally never touches a Needs
Attention order, matching FR-005's requirement that automatic assignment keeps prioritizing fresh
work over resuming a flagged one.

**Rationale**: This is a required correction to already-existing code, not unrelated refactoring
(constitution Principle III) — without it, FR-005/FR-006/FR-008 (a re-claimed or released Needs
Attention order must stay correctly labeled) cannot be satisfied; the current hardcoded values
would silently erase a still-unresolved issue's visibility the moment anyone claims or releases
that order.

**Shared implementation note**: the research.md §1 subquery expression is needed in both
`OrderRepository` (claim/release/force-release) and the new `PickingRepository` (recording an
outcome). It is extracted once as an internal static helper
(`OrderStatusComputation.FromCurrentLines`, `LootSingles.Infrastructure.Persistence`) building the
reusable `Expression<Func<Order, OrderStatus>>`, rather than duplicating the same LINQ expression
in two repository classes (constitution Principle XIII — a second concrete use case of the same
expression is exactly the signal to share it, not duplicate it, from the start this time).

## 6. Frontend: extend the existing responsive order-detail screen, not a new one

**Decision**: Per-line "Picked" and "Report Issue" controls are added directly to the existing
`OrderDetailPage.tsx`/`OrderDetailPage.css` (features 007/008/013), reusing that screen's
already-responsive layout. "Picked" is a single always-visible primary button per line (the
one-tap happy path). "Report Issue" reveals a small inline form (issue type select, optional
required/found quantity fields, optional note, submit) directly beneath that line — matching the
inline-reveal-form pattern already established for PIN reset on the manager admin screen (feature
014) — rather than a separate page, modal, or route, so a picker never navigates away from the
order they're working.

**Rationale**: Directly satisfies spec.md SC-008 (a small, consistent number of interactions,
identical on mobile and desktop, no device-specific workarounds) and the constitution's Principle
VIII (one responsive product, not separate device-specific codebases) by construction — there is
no new screen to make responsive, only new controls added to a screen that already is.

**Alternatives considered**: A dedicated full-screen "record outcome" step per line (swipe/paginate
through lines one at a time) — closer to the PRD's illustrative "Product 3 of 5" framing, but a
materially larger UI undertaking than this feature's spec calls for (SC-001 explicitly wants
picking to happen "without leaving the order-detail screen"); rejected as disproportionate to the
approved requirements, not ruled out for a future feature if picker feedback wants it.

## 7. Dashboard: extend `IDashboardRepository`/`DashboardService` exactly where they already anticipated it

**Decision**: `IDashboardRepository` gains `GetInProgressOrderSummariesAsync`,
`GetNeedsAttentionOrderSummariesAsync` (including, per line-level FR-014, which specific line(s)
have the unresolved issue), and `GetPickedOrderSummariesAsync`/count — mirroring the existing
`GetReadyOrderSummariesAsync` shape exactly. `DashboardService` gains matching pass-through
methods, exactly like its existing one.

**Rationale**: Both types' existing doc comments already name this as their intended extension
point ("the future In Progress/Needs Attention/Picked sections will attach to once those order
states exist") — this is not a new pattern being introduced, it's the already-anticipated one being
used.
