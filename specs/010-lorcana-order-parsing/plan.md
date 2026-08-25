# Implementation Plan: Fix Lorcana Order Line Parsing

**Branch**: `010-lorcana-order-parsing` | **Date**: 2026-08-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-lorcana-order-parsing/spec.md`

## Summary

`OrderLineExtractor.Extract` (feature 001) currently requires a colon to separate a product
line's `Set` from its `ProductName`, because that's how Pokémon's packing-slip text is shaped
(`"SV: Black Bolt: Genesect ex"`). A real, sanitized Lorcana packing-slip sample proves Lorcana's
shape has no colon at all, and its card names routinely contain their own `" - "` (e.g.
`"Scrooge McDuck - S.H.U.S.H. Agent"`), so the parser currently rejects every real Lorcana line
with a false-positive `MissingProductName` failure. The fix adds one fallback path to the existing
`Set`/`ProductName` extraction: when no colon is found, treat the first segment after the game as
`Set` and rejoin every remaining segment before the collector number as `ProductName` (preserving
any internal `" - "`). The colon-based path — and therefore Pokémon's behavior — is completely
unchanged; the new path only ever activates for a line that would previously have been rejected,
so no currently-succeeding line can regress. Nothing about `OrderLine`'s shape, the import
service's contract, or any other feature changes.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend) — unchanged; this feature touches no frontend code

**Primary Dependencies**: None new — this is a change inside the existing `OrderLineExtractor` static class (`System.Text.RegularExpressions`, already used there)

**Storage**: SQL Server via the existing `Order`/`OrderLine` entities — unchanged; this feature adds no field, table, or migration, and changes no persisted schema

**Testing**: xUnit unit tests in the existing `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`, which already covers Pokémon and Magic cases via `[Theory]`/`[InlineData]` — this feature adds the real Lorcana case and **replaces** the file's existing synthetic, colon-based Lorcana `InlineData` (`"Lorcana TCG - The First Chapter: John Silver - Alien Pirate - ..."`), discovered during planning to have been invented rather than validated against real data, and which the real sample now disproves (research.md §2); the existing `LootSingles.IntegrationTests` import suite re-run to confirm no regression

**Target Platform**: ASP.NET Core backend only (no frontend, no API surface change)

**Project Type**: Backend-only, single-file-scoped fix within the existing web application

**Performance Goals**: No new targets — this is a pure string-parsing change with no added I/O, no change to algorithmic complexity beyond one additional conditional branch

**Constraints**: Zero behavior change for any line that currently parses successfully (spec.md FR-002/FR-005) — the new fallback must only ever activate on the code path that previously returned `Invalid`; no new `OrderLine` field, no new failure type unless genuinely needed, no change to `ConditionVariantParser` or collector-number detection (both already work correctly for the Lorcana sample, confirmed by tracing it through the current algorithm)

**Scale/Scope**: One method (`OrderLineExtractor.Extract`) gains one conditional branch; one new test fixture (the provided sanitized Lorcana packing slip); extended unit tests in the existing `OrderLineExtractorTests`-equivalent file

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design — see below.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Traces directly to a real, sanitized packing-slip sample the Developer provided, and to the PRD's own already-committed V1 game list (§30) which already includes Disney Lorcana — this restores intended behavior, it invents nothing. | PASS |
| III Reviewability | Smallest possible diff: one method's internal branching logic, one new test fixture, extended unit tests. No unrelated refactoring bundled in. | PASS |
| IV TDD | The failing case (the real Lorcana sample currently rejected) becomes the Red test; the fix is the minimum change to turn it Green; existing Pokémon tests must remain Green throughout (see tasks.md, once generated). | PASS |
| V Safe failure | This fix corrects a false-positive rejection of *valid* data — it does not weaken rejection of genuinely malformed data (FR-003): a line with no collector number, or a no-colon line with only one segment before the collector number (still structurally ambiguous), continues to be rejected exactly as before. | PASS |
| VI Concurrency-safe claiming | Not applicable — this is a stateless parsing function, no claim/state-transition logic. | PASS (N/A) |
| VII PII minimization | The provided sample is already sanitized (placeholder name/address/order number, consistent with this project's existing fixture conventions) — verified by inspection before use; no real customer data is added to the repository. | PASS |
| VIII One responsive PWA | Not applicable — backend-only fix, no UI surface. | PASS (N/A) |
| IX Replaceable integrations | Not applicable to this specific fix — no catalog/import adapter boundary changes; `OrderLineExtractor` remains the same source-specific adapter component feature 001 already established. | PASS (N/A) |
| X Single-business scope | No multi-tenant concept introduced. | PASS |
| XI Observability | Evaluated: this is a pure parsing-correctness fix with no new failure mode — a no-colon, single-segment line was already a defined rejection path before this fix and remains one; no new production logging is warranted. | PASS |
| XII Maintainable design | The new fallback is added at the exact point the existing colon-based split already lives, using the same `segments`/`collectorIndex` values already computed — no new class, no new abstraction, no duplication of the existing collector-number-detection or condition/variant-parsing logic (both are reused untouched). | PASS |
| XIII Simplicity | One conditional branch, reusing existing computed values. No new failure type (the existing `FailureType.MissingProductName` already fits the still-ambiguous case). No game-name-keyed special case — the fallback triggers on the *shape* of the text (no colon found), not on `ProductLine == "Lorcana TCG"`, so it needs no per-game branching that would have to grow for every future game (Principle XII: avoid central conditional logic that must be repeatedly modified). | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `OrderLineExtractor.Extract` (Application, existing) | Gains one fallback branch in its existing `Set`/`ProductName` extraction step. Everything else in the method — segment splitting, collector-number detection, rarity extraction, condition/variant parsing — is untouched and already correct for the sample (traced by hand against the real text). | The fallback is shape-triggered (no colon found in the combined set+product span), not game-name-triggered, so it generalizes to any future game with a similar no-colon, dash-containing-name format without needing a new branch — while a line that's still ambiguous (a single segment, still no colon) continues to be rejected exactly as today, preserving FR-003's defensive behavior. Testable via the existing unit-test file with no new test infrastructure. |
| `OrderLineExtractionTests.cs` (existing, extended) | Gains the real Lorcana case (replacing the disproven synthetic one) alongside its existing Pokémon and Magic cases, in the same `[Theory]`/`[InlineData]` table — not a parallel test suite. | Directly proves FR-001 (Lorcana succeeds) and FR-002/FR-005 (Pokémon/Magic-shaped inputs are unaffected) side by side, using only inputs actually confirmed real (Pokémon, now Lorcana) or explicitly left untouched (Magic, still synthetic and out of scope per FR-005). |
| `backend/tests/LootSingles.Fixtures/PackingSlips/` (existing, extended) | Gains one new fixture file built from the provided sanitized sample, alongside the existing Pokémon-scenario fixtures — same directory, same convention (feature 001's existing fixture set). | Matches the constitution's explicit requirement that parser changes be validated against representative fixtures, using this feature's own real evidence rather than a synthetic guess. |

**Post-design gate**: No new abstraction, type, or component is introduced. The fix is confined
to the exact boundary that already owns this responsibility. No deviation required.

## Project Structure

### Documentation (this feature)

```text
specs/010-lorcana-order-parsing/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (parsing-rule documentation; OrderLine itself is unchanged)
└── quickstart.md        # Phase 1 output
```

No `contracts/` directory: this fix changes no external interface, HTTP endpoint, or service
contract (`IPackingSlipImportService`'s shape, documented in feature 001's
`contracts/import-service.md`, is unaffected) — it is purely internal parsing logic, so a
contracts directory does not apply per the plan template's own guidance ("skip if purely
internal").

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Application/Import/OrderLineExtractor.cs   # add the no-colon fallback branch
└── tests/
    ├── LootSingles.UnitTests/Import/OrderLineExtractionTests.cs   # replace the synthetic Lorcana case with the real one
    └── LootSingles.Fixtures/PackingSlips/                         # add the sanitized Lorcana sample as a new fixture
```

**Structure Decision**: No new project, layer, file, or abstraction beyond one new test fixture.
The entire fix lives inside the one method that already owns this responsibility.

## Complexity Tracking

No constitution violations require justification.
