# Data Model: Fix Lorcana Order Line Parsing

No entity, field, table, or migration is added or changed by this feature. `OrderLine` (feature
001) is unchanged — this feature only corrects how its existing `Set` and `ProductName` fields are
populated from raw packing-slip text for one previously-unsupported input shape.

## Parsing rule (updated)

`OrderLineExtractor.Extract` splits a product line's raw description on `" - "`, locates the
collector number (unchanged), then extracts `Set` and `ProductName` from the segment(s) between
the game name and the collector number:

| Input shape | Example | Set/ProductName extraction |
|---|---|---|
| Colon-delimited (Pokémon; assumed for Magic, unconfirmed) | `"Pokemon - SV: Black Bolt: Genesect ex - #067/086 - ..."` | Unchanged: split the combined span on the **last** colon — `Set` = everything before it, `ProductName` = everything after it |
| No colon, multiple segments (Lorcana, confirmed by real sample) | `"Lorcana TCG - Disney Lorcana Promo Cards - Scrooge McDuck - S.H.U.S.H. Agent - #36 - ..."` | **New**: `Set` = the first segment (`"Disney Lorcana Promo Cards"`); `ProductName` = every remaining segment before the collector number, rejoined with `" - "` (`"Scrooge McDuck - S.H.U.S.H. Agent"`) |
| No colon, single segment | (no known real example — still treated as malformed) | Unchanged: rejected as `FailureType.MissingProductName` — genuinely ambiguous, cannot determine where `Set` ends and `ProductName` begins |

`CollectorNumber`, `Rarity`, `Condition`, and `Variant` extraction are entirely unchanged — all
four already produce correct results for the real Lorcana sample under the existing logic, traced
by hand during planning (research.md §1).

## Test fixture (new)

A new fixture file is added to `backend/tests/LootSingles.Fixtures/PackingSlips/`, built from the
provided sanitized sample, alongside feature 001's existing Pokémon-scenario fixtures. It replaces
no existing fixture file — it is additive — but its corresponding `[InlineData]` case in
`OrderLineExtractionTests.cs` *replaces* the disproven synthetic Lorcana case (research.md §2).
