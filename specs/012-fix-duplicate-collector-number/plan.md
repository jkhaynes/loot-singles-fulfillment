# Implementation Plan: Fix Duplicate Collector-Number Echo in Product Names

**Branch**: `012-fix-duplicate-collector-number` | **Date**: 2026-08-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/012-fix-duplicate-collector-number/spec.md`

## Summary

`OrderLineExtractor.Extract` (feature 001) computes `setAndProductSegments` as every segment
between the game name and the matched, `#`-prefixed collector-number segment, then derives `Set`
and `ProductName` from that combined span. A real, sanitized 3-page packing slip (already a
fixture from feature 011) proves that TCGplayer sometimes prints a product's collector number
*twice* — once bare, immediately before the real `#`-prefixed one (e.g. `"Fomantis - 085/084 -
#085/084 - ..."`) — confirmed via direct inspection of the source PDF's word bounding-box
positions to be genuine, separately-positioned content, not a parsing artifact. Today, that bare
echo gets swept into `setAndProductSegments` and ends up appended to `ProductName`
(`"Fomantis - 085/084"` instead of `"Fomantis"`). The fix adds one exact-match check: when the
segment immediately before the matched collector-number segment is identical to it (minus the
`#`), exclude that segment from `setAndProductSegments` before deriving `Set`/`ProductName`. No
other part of the method changes.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend) — unchanged; this feature touches no frontend code

**Primary Dependencies**: None new — the fix is an additional conditional inside the existing
`OrderLineExtractor` static class, using data (`segments`, `collectorIndex`) it already computes

**Storage**: SQL Server via the existing `Order`/`OrderLine` entities — unchanged; this feature
adds no field, table, or migration, and changes no persisted schema

**Testing**: xUnit unit tests in the existing `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`,
which already covers Pokémon, Magic, and Lorcana cases — this feature adds the real duplicate-echo
case (and a boundary case proving a non-duplicate number-like segment is never dropped); the
existing `LootSingles.IntegrationTests` import suite re-run to confirm no regression

**Target Platform**: ASP.NET Core backend only (no frontend, no API surface change)

**Project Type**: Backend-only, single-method-scoped fix within the existing web application

**Performance Goals**: No new targets — one additional string comparison per line, negligible cost

**Constraints**: Zero behavior change for any line that doesn't have the exact duplicate-echo shape
(spec.md FR-003/FR-006) — the new exclusion only ever activates on an *exact* match against the
already-confirmed collector number, never a heuristic guess; no new `OrderLine` field, no new
failure type (the existing `MissingProductName` already fits the "echo was the only content"
edge case)

**Scale/Scope**: One method (`OrderLineExtractor.Extract`) gains one conditional check reusing
values it already computes; no new fixture needed (feature 011's real 3-page sample already
covers this); extended unit tests in the existing extraction test file

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design — see below.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Traces directly to a real, sanitized packing-slip sample already in the repo (feature 011) and to feature 001's own already-committed responsibility for correctly extracting `ProductName` — this restores intended behavior, it invents nothing. | PASS |
| III Reviewability | Smallest possible diff: one additional conditional in one existing method, reusing already-computed values. No unrelated refactoring bundled in. | PASS |
| IV TDD | The failing case (the real duplicate-echo lines, currently producing a wrong `ProductName`) becomes the Red test; the fix is the minimum change to turn it Green; existing Pokémon/Magic/Lorcana/multi-page tests must remain Green throughout (see tasks.md, once generated). | PASS |
| V Safe failure | This fix corrects a silent *acceptance of wrong data* (the line imports, but with an incorrect name) — it does not weaken rejection of genuinely malformed data (FR-004): a line where excluding the echo leaves nothing else still fails with `MissingProductName`, exactly as an already-empty span does today. | PASS |
| VI Concurrency-safe claiming | Not applicable — this is a stateless parsing function, no claim/state-transition logic. | PASS (N/A) |
| VII PII minimization | No new fixture is added — the existing, already-verified-sanitized feature 011 fixture is reused. | PASS |
| VIII One responsive PWA | Not applicable — backend-only fix, no UI surface. | PASS (N/A) |
| IX Replaceable integrations | Not applicable to this specific fix — no catalog/import adapter boundary changes; `OrderLineExtractor` remains the same source-specific adapter component feature 001 already established. | PASS (N/A) |
| X Single-business scope | No multi-tenant concept introduced. | PASS |
| XI Observability | Evaluated: this is a pure parsing-correctness fix with no new failure mode — the line still imports successfully either way (before: with a wrong name; after: with the right one), so there's no new success/failure outcome to log differently. No new logging warranted. | PASS |
| XII Maintainable design | The exclusion check is exact-match-triggered (identical digits against the already-confirmed collector number), not name-, game-, or set-specific, so it generalizes to any future product line with this same TCGplayer echo pattern without a new branch. | PASS |
| XIII Simplicity | One conditional, reusing existing computed values (`segments`, `collectorIndex`). No new type, no new failure classification, no new configuration. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `OrderLineExtractor.Extract` (Application, existing) | Gains one exact-match exclusion check on `setAndProductSegments`, immediately after it's computed and before the existing `Set`/`ProductName` derivation logic — the exact point that already owns this responsibility. Collector-number detection, colon-splitting, rarity extraction, and condition/variant parsing are all completely untouched. | Shape-triggered (an exact duplicate of the already-confirmed collector number), not game- or set-specific, so it generalizes to any product line with this echo pattern. Testable via the existing unit-test file with no new test infrastructure, using the real fixture already present from feature 011. |
| `OrderLineExtractionTests.cs` (existing, extended) | Gains the real duplicate-echo case and a boundary case (a number-like-but-non-duplicate segment is never dropped), alongside its existing Pokémon/Magic/Lorcana cases, in the same style — not a parallel test suite. | Directly proves FR-001/FR-002 (echo excluded, correct `ProductName`) and FR-003/FR-006 (nothing else is affected) side by side. |

**Post-design gate**: No new abstraction, type, or component is introduced. The fix is confined to
the exact boundary that already owns this responsibility. No deviation required.

## Project Structure

### Documentation (this feature)

```text
specs/012-fix-duplicate-collector-number/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output (parsing-rule documentation; OrderLine itself is unchanged)
└── quickstart.md        # Phase 1 output
```

No `contracts/` directory: this fix changes no external interface, HTTP endpoint, or service
contract — it is purely internal parsing logic, so a contracts directory does not apply per the
plan template's own guidance ("skip if purely internal").

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Application/Import/OrderLineExtractor.cs   # add the redundant-echo exclusion check
└── tests/
    └── LootSingles.UnitTests/Import/OrderLineExtractionTests.cs   # add the real duplicate-echo case and a boundary case
```

No new fixture: feature 011's `multi-page-order-no-total-on-continuation-pages.pdf` already
contains this feature's validation evidence.

**Structure Decision**: No new project, layer, file, or abstraction. The entire fix lives inside
the one method that already owns this responsibility.

## Complexity Tracking

No constitution violations require justification.
