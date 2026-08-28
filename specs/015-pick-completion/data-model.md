# Phase 1 Data Model: Pick Completion

Builds directly on the `Order`/`OrderLine` entities established in features 001/007/013. See
`research.md` for the rationale behind each structural choice below.

## Order (existing entity — extended)

| Field | Type | Notes |
|---|---|---|
| `Status` | `OrderStatus` (existing column) | Gains two new values: `Picked = 2`, `NeedsAttention = 3`. Stored, recomputed on every write that could invalidate it (research.md §1) — never set directly to `Picked` or `NeedsAttention` by application code; always derived from current `OrderLines`. |

No new columns on `Order` itself. `ClaimedByEmployeeId`/`ClaimedAt` (013) are unchanged and continue
to gate who may record a line outcome (FR-010).

### `OrderStatus` enum (extended)

```csharp
public enum OrderStatus
{
    Ready = 0,
    InProgress = 1,
    Picked = 2,
    NeedsAttention = 3,
}
```

Plain int storage (no `.HasConversion`), consistent with the enum's existing convention.

### State transitions

| From | To | Trigger | Rule |
|---|---|---|---|
| `Ready` | `InProgress` | Claim (Pick Next or Choose Order) | Unchanged from feature 013. |
| `InProgress` | `Ready` | Release, no unresolved issue on any line | Existing feature-013 behavior, now expressed via the shared status computation instead of hardcoded. |
| `InProgress` | `NeedsAttention` | Release, ≥1 line has unresolved issue | **New.** FR-005. |
| `InProgress` | `Picked` | Last unconfirmed line recorded as picked, no line has unresolved issue | FR-007. |
| `InProgress` | `NeedsAttention` | Any line recorded with an issue while order is In Progress | FR-008. |
| `NeedsAttention` | `InProgress` | Claim (Choose Order only — never Pick Next, FR-005) | Re-claim keeps status `NeedsAttention` only until the claim actually resolves at least one thing; claiming itself immediately reflects "someone is now working it" as `InProgress` if the picker's first action doesn't yet touch the issue line — **clarification**: per FR-006, status is *always* re-derived from line outcomes, so a freshly re-claimed order whose issue line is still unresolved is recomputed as `NeedsAttention` immediately, even though it is claimed. `InProgress` only applies once the order is claimed *and* no line currently has an unresolved issue. |
| `NeedsAttention` (claimed) | `Picked` | Picker resolves the flagged line (re-records it as picked) and no other line has an unresolved issue | FR-004 acceptance scenario AC5. |
| `NeedsAttention` (claimed) | `NeedsAttention` | Released while still unresolved | Stays discoverable via Choose Order (FR-005). |

This clarifies research.md §5: "InProgress unless unresolved issue" applies at claim time too — a
claimed order's displayed status is always `NeedsAttention` while any line has an unresolved issue,
`InProgress` otherwise. This is one status derivation, applied uniformly (FR-006), not two separate
"claim status" vs. "issue status" concepts.

## OrderLine (existing entity — extended)

| Field | Type | Notes |
|---|---|---|
| `PickOutcome` | `PickOutcome?` (nullable) | `null` = not yet recorded. New. |
| `PickOutcomeRecordedByEmployeeId` | `int?` (FK → Employee) | Who last recorded this line's outcome. New. |
| `PickOutcomeRecordedAt` | `DateTimeOffset?` | When. New. |
| `CurrentPickingIssueId` | `int?` (FK → PickingIssue) | The line's current unresolved-or-most-recent issue, or `null` if picked / not yet recorded. New. |

### `PickOutcome` enum (new)

```csharp
public enum PickOutcome
{
    Picked = 0,
    HasIssue = 1,
}
```

Plain int storage, matching sibling `OrderStatus`'s convention (research.md §4).

### Validation rules

- A line's `PickOutcome` can only be written by the employee currently holding the parent order's
  claim (FR-010) — enforced server-side via the conditional `ExecuteUpdateAsync` in
  `PickingRepository.RecordOutcomeAsync` (research.md §2), not merely client-side.
- Revising an already-recorded line (e.g., Picked → HasIssue, or HasIssue → Picked once the card is
  found) is always allowed while holding the claim (FR-004) — there is no "locked once set" rule.
- Recording `HasIssue` always creates a new `PickingIssue` row (never mutates a prior one), even if
  the line already had a different unresolved issue — preserving full history per FR-011.

## PickingIssue (new entity)

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` (PK, identity) | |
| `OrderLineId` | `int` (FK → OrderLine, required) | |
| `IssueType` | `PickingIssueType` | See taxonomy below. |
| `RequiredQuantity` | `int?` | Populated when relevant to the issue type (e.g., insufficient quantity); `null` otherwise. |
| `FoundQuantity` | `int?` | Same. |
| `Note` | `string?` (max length TBD by implementation, e.g. 500) | Optional free text (per PRD §19.3 and spec.md FR-002). |
| `ReportedByEmployeeId` | `int` (FK → Employee, required) | |
| `ReportedAt` | `DateTimeOffset` (required) | |

Append-only: never updated or deleted once written (research.md §4). An `OrderLine`'s
`CurrentPickingIssueId` is what changes, not the `PickingIssue` rows themselves.

### `PickingIssueType` enum (new)

```csharp
public enum PickingIssueType
{
    CardNotFound,
    InsufficientQuantity,
    WrongCardInLocation,
    WrongVariant,
    WrongCondition,
    Damaged,
    InventoryDiscrepancy,
    InformationIncorrect,
    Other,
}
```

Taxonomy drawn from spec.md's Assumptions section (PRD §19). `.HasConversion<string>()` storage
(research.md §4).

### Relationships

```text
Order (1) ──< OrderLine (many)
OrderLine (1) ──< PickingIssue (many, historical)
OrderLine.CurrentPickingIssueId ──> PickingIssue (0..1, "current" pointer)
PickingIssue.ReportedByEmployeeId ──> Employee
OrderLine.PickOutcomeRecordedByEmployeeId ──> Employee
```

## Dashboard read models (extended)

`OrderSummary` (existing, non-persisted record) is reused unchanged for the new In Progress,
Needs Attention, and Picked sections. For Needs Attention specifically, FR-014 requires
identifying which line(s) are flagged without opening the full order — a new
`NeedsAttentionOrderSummary` (extends `OrderSummary` with a short list of flagged product names)
is used only for that one section; the other two sections use the plain `OrderSummary` shape.
