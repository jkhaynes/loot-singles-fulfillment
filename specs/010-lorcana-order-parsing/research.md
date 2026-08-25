# Research: Fix Lorcana Order Line Parsing

## 1. Fallback algorithm for the no-colon Set/ProductName split

**Decision**: In `OrderLineExtractor.Extract`, after computing `setAndProduct` (the joined span of
segments between the game and the collector number, exactly as today), add one fallback: if no
colon is found in that span, and it spans two or more `" - "`-delimited segments, treat the first
segment as `Set` and rejoin every remaining segment as `ProductName` (preserving any internal
`" - "` the card name legitimately contains). If the span is a single segment with no colon, it
remains genuinely ambiguous and is still rejected with `FailureType.MissingProductName`, exactly
as today.

**Rationale**: Traced by hand against the real sample —
`"Lorcana TCG - Disney Lorcana Promo Cards - Scrooge McDuck - S.H.U.S.H. Agent - #36 - Promo - Near Mint Holofoil"`
splits into `segments[0]="Lorcana TCG"`, `[1]="Disney Lorcana Promo Cards"`, `[2]="Scrooge McDuck"`,
`[3]="S.H.U.S.H. Agent"`, `[4]="#36"` (the collector number, found by the existing unchanged
detection logic), `[5]="Promo"`, `[6]="Near Mint Holofoil"`. Collector-number detection, rarity
extraction (`segments[5]` = `"Promo"`), and condition/variant parsing (`segments[6]` via the
existing, unchanged `ConditionVariantParser`) all already produce the correct result today —
**only** the `Set`/`ProductName` split needs a new path. The fallback is triggered by the *shape*
of the text (no colon present), not by checking `ProductLine == "Lorcana TCG"`, so it needs no
per-game branching that would have to grow for every future game (constitution Principle XII).

**Alternatives considered**: A `ProductLine`-keyed switch (`if (productLine == "Lorcana TCG") { ... }`)
— rejected as exactly the "central conditional logic that must be repeatedly modified" Principle
XII warns against; a shape-triggered fallback naturally extends to any future game with a similar
no-colon format without another branch. A new dedicated `FailureType` for "ambiguous, no colon,
single segment" — rejected; the existing `MissingProductName` already accurately describes that
outcome (Principle XIII: no new type without a concrete need).

## 2. The existing "Lorcana" test case was synthetic, not real-data-validated — and disproven

**Finding**: `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs` already
contains a Lorcana `[InlineData]` case:
`"Lorcana TCG - The First Chapter: John Silver - Alien Pirate - #82/204 - Legendary - Near Mint"`.
This string *does* contain a colon (`"The First Chapter: John Silver..."`), so it currently passes
under the existing colon-based path — but it was invented to fit the Pokémon-style assumption,
never validated against a real TCGplayer Lorcana packing slip. The real, sanitized sample provided
for this feature has **no colon anywhere**, directly disproving that invented shape.

**Decision**: Remove the disproven synthetic `InlineData` case and replace it with one built from
the real sample (confirmed directly with the Developer — see spec.md). Do not keep both as
alternate "valid" Lorcana shapes; the old one does not reflect anything TCGplayer actually sends
and would otherwise keep asserting a fictional contract, contradicting the constitution's
"validated against representative fixtures" requirement going forward.

**Rationale**: A test asserting behavior for an input shape that doesn't exist in reality is worse
than no test — it creates false confidence. Now that real evidence exists, the test should assert
against that evidence, not the earlier guess.

**Consequence for Magic**: `OrderLineExtractionTests.cs`'s two existing Magic `[InlineData]` cases
(lines 48–66) are, by the same reasoning, *also* unconfirmed against real data — they were written
on the same colon-based assumption that just proved wrong for Lorcana. This is exactly why spec.md
FR-005 already scopes Magic out of this fix rather than assuming its current tests are trustworthy.
No action is taken on the Magic cases in this feature; a future feature should revisit them once a
real Magic sample is available, the same way this feature is doing for Lorcana now.

## 3. Fixture sanitization check

**Finding**: The provided PDF was inspected directly. It uses obviously placeholder data — "Jamie
Sample", "123 Example Lane", "Sampleton, MI 48116", order number "F0000000-000AAA-00000" — matching
this project's existing fixture-sanitization conventions (e.g. `F0000001-ABC001-00001` used
elsewhere). No real customer data is present.

**Decision**: Safe to add to the repository as a project fixture
(`backend/tests/LootSingles.Fixtures/PackingSlips/`), consistent with constitution Principle VII
("Sensitive information MUST NOT be added to... fixtures... when synthetic or sanitized data can
serve the same purpose" — this data already is sanitized, confirmed by inspection rather than
taken on faith).

## 4. No new production logging

**Finding**: This is a pure parsing-correctness fix. The rejection path for genuinely ambiguous
input (`FailureType.MissingProductName`) already exists and is unchanged; no new failure mode is
introduced — a line that previously failed for a legitimate reason still fails the same way.

**Decision**: No new logging is added, consistent with the precedent already established for
routine, no-new-failure-mode changes (features 007/008; contrast with feature 009's genuine new
failure mode, which did warrant new logging).
