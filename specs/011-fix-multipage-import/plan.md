# Implementation Plan: Fix Multi-Page Packing-Slip Order Import

**Branch**: `011-fix-multipage-import` | **Date**: 2026-08-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-fix-multipage-import/spec.md`

## Summary

`PdfPigPackingSlipParser` (feature 001) was built on the assumption that one PDF page equals one
order — `RawOrderBlock` and `ParsedPackingSlip.OrderBlocks` are both documented as "one [block] per
order page." A real, sanitized 3-page packing slip proves that assumption wrong: TCGplayer prints a
multi-page order's grand-total row only once, on its final page, so `ExtractProductLines`'s
requirement of finding a "Total" row to bound the product rows returns zero lines for every
continuation page, and each page becomes its own (mostly empty) order block even though every page
shares the same "Order Number:" identifier. The fix has two cooperating parts, both required
(the second was discovered necessary at the TDD Red step, once the first alone proved insufficient
against the real fixture — research.md §1's "Correction"):

1. `ExtractProductLines` falls back to a page's "Order Number:" footer line as the lower bound for
   scanning product rows when no "Total" row exists on that page, instead of returning no rows at
   all — so a continuation page's real product lines are actually extracted.
2. One post-processing step in the parser, after all pages are read, merges any `RawOrderBlock`s
   that share the same non-null order identifier into a single block, concatenating their product
   lines in page/reading order. Blocks with a missing identifier are never merged with anything
   (each remains its own distinct failure, exactly as today).

No other component changes — `PackingSlipImportService`'s per-block loop already does the right
thing once it receives one block per real order instead of one block per page.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend) — unchanged; this feature touches no frontend code

**Primary Dependencies**: None new — the merge lives inside the existing `PdfPigPackingSlipParser`
(`UglyToad.PdfPig`, already used there) as a plain LINQ-free grouping pass over data already in memory

**Storage**: SQL Server via the existing `Order`/`OrderLine` entities — unchanged; this feature adds
no field, table, or migration, and changes no persisted schema

**Testing**: xUnit unit tests in the existing `backend/tests/LootSingles.UnitTests/Import/PdfPigPackingSlipParserTests.cs`,
which already exercises the parser end-to-end against real fixture PDFs (`valid-multi-order-batch.pdf`,
13 single-page orders plus a summary page; `duplicate-product-line-same-order.pdf`) — this feature adds
the real 3-page multi-page fixture and a new test asserting the merge; the existing `LootSingles.IntegrationTests`
import suite re-run to confirm no regression to the full import pipeline

**Target Platform**: ASP.NET Core backend only (no frontend, no API surface change)

**Project Type**: Backend-only, single-file-scoped fix within the existing web application

**Performance Goals**: No new targets — the merge is a single linear pass over the already-parsed,
in-memory list of order blocks (bounded by page count, never large for a packing slip)

**Constraints**: Zero behavior change for any file that currently parses successfully (spec.md
FR-003/FR-006) — merging only ever combines blocks that already share a real, non-null identifier;
a page whose identifier is missing, or whose product lines are genuinely absent for an unrelated
reason, continues to fail exactly as today (FR-004); no new `RawOrderBlock`/`ParsedPackingSlip`
field, no new failure type, no change to `ExtractOrderIdentifier`, `ExtractProductLines`, or
`OrderLineExtractor` (all three already produce the correct data for each individual page — the
defect is purely in how page-level blocks are assembled into orders afterward)

**Scale/Scope**: Three new private helpers in `PdfPigPackingSlipParser` — `ParseState.MergeContinuationPages`
(called once per parse) and `FindGrandTotalLine`/`FindOrderNumberFooterLine` (called once per page,
replacing `ExtractProductLines`'s inline total-row search); one new fixture (the provided sanitized
3-page sample); extended unit tests in the existing parser test file

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design — see below.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Traces directly to a real, sanitized 3-page packing-slip sample and to live application logs confirming the exact failure mode — this restores already-committed feature 001 import behavior (a multi-page order was always expected to import as one order), it invents nothing. | PASS |
| III Reviewability | Smallest possible diff: one new merge step in the parser, confined to the exact place that already owns page→order-block assembly. No unrelated refactoring bundled in. | PASS |
| IV TDD | The failing case (the real 3-page sample, whose continuation pages are currently dropped) becomes the Red test; the fix is the minimum change to turn it Green; the existing 13-order and duplicate-line fixture tests must remain Green throughout (see tasks.md, once generated). | PASS |
| V Safe failure | This fix corrects a false-positive "no product lines" rejection of *valid* continuation pages — it does not weaken rejection of genuinely malformed pages (FR-004): a page with no identifiable "Order Number:", or a merged order whose combined product lines are still empty, continues to fail with the same typed failure it does today. | PASS |
| VI Concurrency-safe claiming | Not applicable — this is a stateless parsing/assembly step, no claim/state-transition logic. | PASS (N/A) |
| VII PII minimization | The provided sample is already sanitized (placeholder buyer name/address, real order number format, consistent with this project's existing fixture conventions) — verified by inspection before use; no real customer data is added to the repository. | PASS |
| VIII One responsive PWA | Not applicable — backend-only fix, no UI surface. | PASS (N/A) |
| IX Replaceable integrations | Not applicable to this specific fix — no catalog/import adapter boundary changes; `PdfPigPackingSlipParser` remains the same source-specific adapter component feature 001 already established. | PASS (N/A) |
| X Single-business scope | No multi-tenant concept introduced. | PASS |
| XI Observability | Evaluated: `PackingSlipImportService`'s existing per-attempt completion log (detected/succeeded/failed counts, per-failure-type breakdown) already reports accurately once it receives correctly-merged blocks — no new logging is needed; the fix corrects an input to existing observability, it doesn't need new observability of its own. | PASS |
| XII Maintainable design | The merge triggers on the *shape* of the data (multiple blocks sharing one identifier), not on any per-game or per-PDF-layout special case, so it generalizes to any future packing-slip format without needing new branching (Principle XII: avoid central conditional logic that must be repeatedly modified). | PASS |
| XIII Simplicity | One grouping pass over already-in-memory data plus one alternate-lower-bound lookup, both reusing existing helpers (`ContainsSequence`) and shapes (`RawOrderBlock`/`ParsedPackingSlip`, `TextLine`) unchanged. No new class, no new failure type, no new configuration. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `PdfPigPackingSlipParser.ParseState.ToResult()` (Infrastructure, existing) | Gains one merge step over `OrderBlocks` before producing the final `ParsedPackingSlip` — the exact point that already owns turning per-page scan state into the parser's output shape. | The merge is shape-triggered (blocks sharing one non-null identifier), not layout- or game-specific, so it generalizes to any future multi-page packing-slip variant without a new branch. A block with a missing identifier is never merged, preserving today's `MissingOrderIdentifier` failure path exactly. Testable via the existing parser unit-test file with no new test infrastructure. |
| `PdfPigPackingSlipParser.ExtractProductLines` (Infrastructure, existing) | Gains a fallback lower bound (`FindOrderNumberFooterLine`) for when no "Total" row exists on a page, tried only after the original "Total" search (extracted unchanged into `FindGrandTotalLine`) fails — the exact point that already owns bounding a page's product rows. `ExtractOrderIdentifier` and table-header detection are completely untouched; `FindOrderNumberFooterLine` reuses the same `ContainsSequence` check `ExtractOrderIdentifier` already uses. | Also shape-triggered (absence of a "Total" line), not tied to page number or order size, so it applies uniformly to any continuation page regardless of how many pages an order spans. A page with neither a total row nor an "Order Number:" line still returns no rows, preserving today's safe-failure behavior. |
| `PackingSlipImportService` (Application, unchanged) | Continues to process `parsed.OrderBlocks` one block per real order — this component needs no change once it receives correctly-assembled blocks; its per-block validation, duplicate-detection, and persistence logic already treat each block as one logical order. | Untouched, so its own existing test coverage (duplicate-order handling, validation failures, summary-mismatch detection) continues to prove those behaviors are unaffected. |
| `PdfPigPackingSlipParserTests.cs` (existing, extended) | Gains the real multi-page case alongside its existing 13-order-batch and duplicate-line-same-order cases, in the same fixture-driven style — not a parallel test suite. | Directly proves FR-001/FR-002 (multi-page merge) and FR-003/FR-006 (existing single-page behavior unaffected) side by side, using only real, representative fixtures. |
| `backend/tests/LootSingles.Fixtures/PackingSlips/` (existing, extended) | Gains one new fixture file (already added) from the provided sanitized sample, alongside the existing fixtures — same directory, same convention. | Matches the constitution's explicit requirement that parser changes be validated against representative fixtures, using this feature's own real evidence rather than a synthetic guess. |

**Post-design gate**: No new abstraction, type, or component is introduced. The fix is confined to
the exact boundary that already owns this responsibility. No deviation required.

## Project Structure

### Documentation (this feature)

```text
specs/011-fix-multipage-import/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output (RawOrderBlock/ParsedPackingSlip doc-comment correction; no field changes)
└── quickstart.md        # Phase 1 output
```

No `contracts/` directory: this fix changes no external interface, HTTP endpoint, or service
contract (`IPackingSlipImportService`'s shape, and `IPackingSlipParser`'s shape, are both
unaffected) — it is purely internal parsing/assembly logic, so a contracts directory does not apply
per the plan template's own guidance ("skip if purely internal").

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Infrastructure/Import/PdfPigPackingSlipParser.cs   # add the continuation-page merge step
└── tests/
    ├── LootSingles.UnitTests/Import/PdfPigPackingSlipParserTests.cs   # add the real multi-page merge case
    └── LootSingles.Fixtures/PackingSlips/
        └── multi-page-order-no-total-on-continuation-pages.pdf        # already added: the sanitized 3-page sample
```

**Structure Decision**: No new project, layer, file, or abstraction beyond the one fixture already
added. The entire fix lives inside the one component that already owns page→order-block assembly.

## Complexity Tracking

No constitution violations require justification.
