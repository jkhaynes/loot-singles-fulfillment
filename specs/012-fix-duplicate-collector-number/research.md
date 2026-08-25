# Phase 0 Research: Fix Duplicate Collector-Number Echo in Product Names

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

No `NEEDS CLARIFICATION` markers exist in the Technical Context — this feature's Technical
Context contains no open unknowns, so this document instead records the evidence and design
decision the plan depends on.

## 1. Root cause: where the echo enters `ProductName`

`OrderLineExtractor.Extract` splits a line's raw description on `" - "` into `segments`, finds
`collectorIndex` (the index of the first segment matching `CollectorNumberPattern`, which requires
a leading `#`), and computes:

```csharp
var setAndProductSegments = segments.Skip(1).Take(collectorIndex - 1).ToArray();
var setAndProduct = string.Join(" - ", setAndProductSegments);
```

Every segment strictly between the game name (`segments[0]`) and the matched collector-number
segment is treated as "set + product name" text, later split on the last `:` into `Set` and
`ProductName`. This is correct for the common case (`"Pokemon - ME05: Pitch Black: Meowth -
#106/094 - ..."` → `setAndProductSegments = ["ME05: Pitch Black: Meowth"]`), but for a line whose
raw description contains the collector number twice, the bare echo segment is swept up into this
same span with no way to distinguish it from real set/product text.

## 2. Evidence the duplicate is genuine source content, not a parsing artifact

Using `PdfPig`'s `page.GetWords()` with bounding-box filtering (a temporary diagnostic test, not
committed — see the feature 009 session notes), the Fomantis line's two number-looking tokens were
confirmed to be **two distinct, separately-positioned words** in the source PDF:

| Token | X range | Y |
|-------|---------|---|
| `085/084` | 289.6–325.8 | 454.5 |
| `#085/084` | 334.6–376.3 | 454.5 |

Same row, different X position — this is TCGplayer's own packing-slip rendering, not a
`GroupIntoLines`/`ExtractProductLines` bug duplicating a single word. `OrderLineExtractor` receives
this pre-joined text (`"... Fomantis - 085/084 - #085/084 - Illustration Rare - ..."`) faithfully
and must handle it at the segment level.

## 3. Decision: exact-match exclusion, keyed off the already-confirmed collector number

**Decision**: After computing `setAndProductSegments`, check whether its last element is
`string.Equals` (ordinal) to `segments[collectorIndex].TrimStart('#')`. If so, drop that last
element before deriving `Set`/`ProductName`.

**Rationale**:
- Reuses `collectorIndex`, which `CollectorNumberPattern` has already validated as a genuine
  collector number for *this* line — no new independent number-detection heuristic, no new
  regex, no risk of misclassifying an unrelated numeric-looking set/product segment (FR-003).
  A false positive is structurally impossible: the check requires the two segments to be
  byte-identical once the `#` is stripped, and `collectorIndex` is already known-correct.
- Confines the change to exactly the point in `Extract` that already owns computing
  `setAndProductSegments`, matching Constitution Principle XIII (Simplicity) and III
  (Reviewability): one `if`, no new type, no new failure classification.
- Generalizes to any product line with this shape regardless of game, set, or rarity — it is
  shape-triggered, not name- or game-specific — satisfying Constitution Principle XII
  (Maintainable design) without a per-game branch.

**Alternatives considered**:
- *Strip a duplicated trailing number via a dedicated regex independent of `collectorIndex`* —
  rejected: reintroduces a second number-parsing path that could disagree with
  `CollectorNumberPattern`, and duplicates logic the method already has.
- *Filter during `ExtractProductLines`/`GroupIntoLines` (feature 001's PDF-word-grouping layer)
  instead of `OrderLineExtractor`* — rejected: the duplication is a legitimate feature of the
  raw text (both tokens really exist in the PDF at that row); the correct place to resolve it
  is where the text is already being interpreted into structured fields, not by discarding a
  real PDF word during grouping.
- *Only strip the echo if it's the immediately-preceding segment (vs. scanning all
  `setAndProductSegments` for any duplicate)* — **selected**: TCGplayer's evidenced pattern
  places the echo immediately before the true collector-number segment; restricting the check to
  the last element (rather than scanning the whole span) is both sufficient for every observed
  case and avoids ever discarding a legitimately duplicated number that might appear earlier in a
  set or product name for unrelated reasons.

## 4. Validation data

**Positive cases** (from the real, sanitized 3-page order, `multi-page-order-no-total-on-continuation-pages.pdf`,
already a fixture from feature 011) — lines exhibiting the exact duplicate-echo shape, expected to
have the echo removed, e.g.:
- `"Pokemon - ME05: Pitch Black: Fomantis - 085/084 - #085/084 - Illustration Rare - Near Mint Holofoil"`
  → `Set = "ME05: Pitch Black"`, `ProductName = "Fomantis"`

**Negative-control cases** (same order, no duplicate shape — must be byte-for-byte unaffected):
- `"Pokemon - ... Meowth - #106/094 - ..."`
- `"Pokemon - ... Blastoise ex - #030/142 - ..."`

**Regression corpus** (must remain 100% passing, unchanged): existing Pokémon, Magic, and Lorcana
fixtures/unit tests in `OrderLineExtractionTests.cs`, none of which contain this duplicate shape.

## 5. Boundary case: echo-only span

Per spec.md's edge cases and FR-004, if excluding the echo leaves `setAndProductSegments` empty,
the line must still fail with the existing `FailureType.MissingProductName` — this already happens
today whenever `setAndProductSegments` is empty (or yields no usable set/product text), so no new
failure branch is needed; the exclusion is simply applied before that existing check runs.
