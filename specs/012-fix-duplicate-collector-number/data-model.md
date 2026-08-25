# Phase 1 Data Model: Fix Duplicate Collector-Number Echo in Product Names

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

## Entities

No entity, field, or persisted schema changes. This feature is a parsing-rule correction to how
two already-existing `OrderLine` fields are *derived* from raw import text — it adds nothing new.

### Order Line (existing, unchanged shape)

| Field | Type | Change in this feature |
|-------|------|-------------------------|
| `Set` | `string` | None — still derived by the existing colon-split logic, just from a `setAndProductSegments` span that has had a confirmed duplicate echo excluded first. |
| `ProductName` | `string` | Corrected derivation only: when the raw description contains a redundant, bare collector-number echo immediately before the true `#`-prefixed collector-number segment, that echo no longer appears as a trailing fragment. |
| All other fields (`Quantity`, `CollectorNumber`, `Rarity`, `Condition`, `IsFoil`, etc.) | unchanged types | Untouched — extraction logic for these fields is unaffected. |

## Parsing rule (internal logic, not a data model, documented here for traceability)

```text
setAndProductSegments = segments[1 .. collectorIndex-1]   // existing, unchanged
IF setAndProductSegments is non-empty
   AND last(setAndProductSegments) == TrimStart(segments[collectorIndex], '#')  // ordinal exact match
THEN
   setAndProductSegments = setAndProductSegments[.. -1]   // drop the redundant echo
Set, ProductName = existing colon-split derivation, applied to the (possibly-trimmed) span
```

No new `FailureType` value. The existing `FailureType.MissingProductName` already covers the case
where the trimmed span is empty (spec.md edge case / FR-004).
