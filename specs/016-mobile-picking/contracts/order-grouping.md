# Contract: Order Grouping and Progress

**Feature**: 016-mobile-picking | **Date**: 2026-09-20

This is not a network contract. It is the internal contract of `orderGrouping.ts`, documented
here because it carries most of this feature's logic and therefore most of its tests.

It is a module of **pure functions**: same input, same output, no I/O, no React, no clock.

---

## `groupOrderLines(lines): SetGroup[]`

Groups an order's lines into storage boxes and orders them for the walk.

### Rules

| # | Rule | Requirement |
|---|---|---|
| 1 | Lines sharing a game and set form one group | FR-001 |
| 2 | Groups of one game sort before groups of another | FR-002 |
| 3 | Games sort alphabetically by name | FR-004 |
| 4 | Within a game, sets sort alphabetically by name | FR-003 |
| 5 | A line with a missing or blank set forms a group marked `isSetRecorded: false`, sorted last within its game | FR-005 |
| 6 | Lines keep their existing relative order within a group | — |
| 7 | Only `ProductLine`, `Set` and `Quantity` are read; enrichment fields are never consulted | FR-006 |

### Invariants — assert these directly

- **Totality**: every input line appears in exactly one output group. Concatenating all groups'
  lines yields a permutation of the input, never a subset.
- **Determinism**: the same input always yields the same order.
- **Same-name sets in different games never merge.** "Promo" in Magic and "Promo" in Pokémon are
  two boxes in two sections of the shop.
- `isComplete` is `true` only when `unresolvedLines` is empty (FR-018).

### Cases the tests must cover

- Empty order.
- One line.
- One game, one set.
- One game, several sets — check alphabetical ordering.
- Several games — check game grouping and ordering.
- Same set name in two games — must not merge.
- Blank, whitespace-only, and missing set values — line still present, group marked.
- Set names differing only by case or leading article — ordering must be stable and defined.
- Every line resolved; no line resolved; some resolved.

---

## `computeProgress(groups, currentLineId): OrderProgress`

### Rules

| # | Rule | Requirement |
|---|---|---|
| 1 | `resolvedProducts` counts lines with a non-null `PickOutcome` | FR-020 |
| 2 | `accountedCards` sums `Quantity` of resolved lines — a line of 3 contributes 3 | FR-021 |
| 3 | `totalCards` sums `Quantity` across all lines | FR-021 |
| 4 | Position within the current set is reported against that set, not the order | FR-022 |

### Notes

"Accounted for" means **resolved**, not successfully found: a line carrying a reported issue is
accounted for. A picker who cannot find 2 of 3 copies has accounted for that product without
having pulled it.

### Cases the tests must cover

- Nothing resolved → `0 of N`.
- Everything resolved → `N of N`.
- A quantity-greater-than-one line — the card count must not equal the product count.
- A line resolved as `HasIssue` — counts as accounted for.
- Position reported while in the first, middle and last group.

---

## `findNextUnresolved(groups, fromLineId)` and the set guard

Supports FR-015 – FR-019.

### Rules

| # | Rule | Requirement |
|---|---|---|
| 1 | Advancing past the last line of a **complete** set yields a transition naming the next set, its product count and its card count | FR-015 |
| 2 | Advancing past the last line of an **incomplete** set yields the guard, carrying that set's `unresolvedLines` | FR-016 |
| 3 | The guard offers exactly three choices: return to an unresolved product, report what is missing, leave the set | FR-017 |
| 4 | A set is never reported complete while any line in it is unresolved | FR-018 |
| 5 | Leaving a set changes no outcome | FR-019, FR-011 |
| 6 | Advancing past the last line of the **last** set yields no transition and no empty next-set panel | Edge case |
| 7 | Resolving the last outstanding line of an earlier set makes that set stop reporting as incomplete | Edge case |

---

## What these functions must never do

- **Never record an outcome.** This module is pure; it cannot issue a request. FR-011's
  guarantee is structural, not a matter of discipline — which is the main reason the logic lives
  here rather than inside a component.
- **Never drop a line** for any reason, including an unrecognised set (Constitution V).
- **Never read enrichment data.** Grouping derives from authoritative imported order fields only
  (FR-006, PRD §33).
