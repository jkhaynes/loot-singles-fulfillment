# Phase 0 Research: Mobile Picking Experience

**Feature**: 016-mobile-picking | **Date**: 2026-09-20

The spec carried no `[NEEDS CLARIFICATION]` markers into planning — both were resolved by
Product Owner decision and recorded in the spec's Clarifications section. This document records
the technical decisions the plan rests on, and what was checked in the existing codebase to
reach them.

---

## R1. Where grouping is computed

**Decision**: On the client, from the order detail payload that already exists.

**Rationale**: `OrderLineDetail`
(`backend/src/LootSingles.Application/Orders/OrderDetail.cs`) already returns `ProductLine`,
`Set`, `Quantity` and `PickOutcome` for every line. Grouping by game and set, ordering, counting
physical cards and determining which products in a set are unresolved are all pure derivations
over data the client holds the moment the order loads.

Computing it server-side would mean a second representation of the same order, a new contract
to version, and a round trip to answer questions the client can answer instantly — while
changing no observable behaviour.

**Alternatives considered**:

- *Server-side grouped projection.* Rejected: new contract, no behavioural gain, and it would
  make navigating between products a network concern.
- *Database-level ordering (`ORDER BY` on the query).* Rejected: it fixes only ordering, not
  grouping, counts or the unresolved-set guard, so the client would still need the derivation.
  It also pushes a presentation rule into persistence.

**Consequence**: `orderGrouping.ts` is a plain module of pure functions, trivially unit-testable
without React or a server.

---

## R2. Ordering rules

**Decision**: Games alphabetically by name; sets alphabetically by name within a game; lines
keep their existing relative order within a set.

**Rationale**: Product Owner decision, 2026-09-20 (spec FR-003, FR-004). Release-date ordering
would match Loot's newest-to-oldest shelves more closely but was deliberately deferred by PRD
§13.1 — no release date exists in the order data, and the expensive walk is between game
sections rather than along one aisle.

**Verified**: the set catalogs (`TcgdexSetCatalog.cs:80`, `LorcastSetCatalog.cs:113`) deserialize
only an id and a name, so no release date is available today even from enrichment.

**Safe-failure note**: an imperfect sort costs the picker a few steps. It is explicitly *not* in
the same class as an uncertain card image — nothing about the ordering claims to be
authoritative, so PRD §17's "no image is better than the wrong image" rule does not transfer
(spec Assumptions).

---

## R3. Lines with a missing or unrecognised set

**Decision**: Group them under an explicit "set not recorded" group, placed last within their
game. They remain fully visible and pickable.

**Rationale**: FR-005 and Constitution V. Silently dropping a line from a grouped view would
hide work the picker must do, and an order missing a line is exactly the class of silent
corruption the constitution forbids. `Set` is non-nullable on `OrderLine` but may be empty or
unmatched in practice, so the case must be handled rather than assumed away.

**Alternatives considered**: *Fall back to the raw TCGplayer order.* Rejected — it mixes two
ordering schemes in one view and is harder to reason about than one explicit group.

---

## R4. Where the view preference lives

**Decision**: Browser `localStorage`, one key, read and written through a guarded helper.

**Rationale**: The Product Owner chose per-device (spec FR-010). `localStorage` is per-origin
and per-browser, which *is* per device — it needs no schema, no endpoint and no employee
record change, and it cannot leak between employees on the server.

**Failure handling**: `localStorage` can be absent or throw (private browsing, blocked site
data, some embedded webviews). Every read and write is wrapped, and any failure falls back to
the size-based default. The view must render correctly with no stored preference at all.

**Alternatives considered**:

- *Persist on the employee record.* Rejected: that is per-employee, which the Product Owner
  explicitly did not choose, and it would add a column and an endpoint for a UI preference.
- *URL parameter.* Rejected: does not persist across orders, which FR-010 requires.

**Carry-forward**: FR-010 refines PRD §8's "persist for that employee" wording. Recorded in the
spec; §8 needs a matching correction at the next PRD amendment.

---

## R5. Choosing the default view

**Decision**: `matchMedia` on a viewport width breakpoint, re-evaluated on change.

**Rationale**: FR-008 is written in terms of screen size, not device type. User-agent sniffing
answers a different question and is wrong for a tablet, a small window on a desktop, or a phone
in desktop mode. A media query also means a desktop user who narrows their window gets the
view that fits, which is the intent.

**Alternatives considered**: *User-agent / device detection.* Rejected as above.

---

## R6. How the dashboard learns the employee already holds an order

**Decision**: Add a single `activeClaim` field to the dashboard response, computed server-side
for the authenticated employee.

**Rationale**: This is the only genuine backend gap in the feature. The dashboard's
`OrderSummary` (`backend/src/LootSingles.Application/Dashboard/OrderSummary.cs`) carries no
claimant, so the client currently has no way to answer "do I already hold one?".

The important subtlety: **an employee can hold an order that is not in the In Progress
section.** Reporting an issue moves an order to Needs Attention while the claim is retained
(feature 015). Scanning In Progress rows for "mine" would therefore miss a picker who is
holding a flagged order — precisely the picker most in need of being sent back to it.
Computing the claim directly, independent of which section the order sits in, avoids that.

**Privacy**: the field returns only the signed-in employee's own order. No other employee's
identifier is added to any payload (Constitution VII, PRD §27).

**Alternatives considered**:

- *Add `claimedByEmployeeId` to every In Progress row and compare on the client.* Rejected:
  misses the Needs Attention case above, and exposes employee identifiers to every picker for
  no reason.
- *A separate `GET /api/orders/my-claim` endpoint.* Rejected: a second round trip on every
  dashboard load to answer a question the dashboard query can answer in the same pass.

---

## R7. Reusing the existing picking and claiming endpoints

**Decision**: No new picking endpoints. The focused view calls what already exists.

**Verified in `backend/src/LootSingles.Api/Controllers/OrdersController.cs`**:

| Need | Endpoint | Status |
|---|---|---|
| Record a pick | `POST /api/orders/{orderId}/lines/{lineId}/pick` | Exists (015) |
| Report an issue | `POST /api/orders/{orderId}/lines/{lineId}/report-issue` | Exists (015) |
| Claim from the order (FR-024) | `POST /api/orders/{orderId}/claim` | **Exists (013)** — never surfaced in the UI |
| Pick Next (FR-028) | `POST /api/orders/pick-next` | Exists (013), unchanged |
| Read the order | `GET /api/orders/{orderId}` | Exists, payload already sufficient |

**Consequence**: FR-024 — the headline fix for the dashboard dead end — is a frontend-only
change. Claim exclusivity is already concurrency-safe and server-enforced; this feature adds a
button, not a rule.

---

## R8. Testing approach

**Decision**: Push coverage to the lowest layer that can prove each behaviour.

| Behaviour | Layer | Why |
|---|---|---|
| Grouping, ordering, missing-set handling, progress counts, which products in a set are unresolved | Unit (Vitest), pure functions | No DOM or server needed; the highest-value tests in the feature |
| Default view by size, switching, preference persistence, storage unavailable | Component (RTL) | Needs a rendered component and a mockable `matchMedia`/storage |
| Navigation records nothing (FR-011) | Component (RTL) **and** E2E | The central safety rule; asserted both as "no request issued" and end-to-end as "outcome unchanged after navigating" |
| Unresolved-set guard | Component (RTL) + E2E | Logic is unit-testable, but the three-way choice is a user flow |
| Claim from the order, resume affordance | E2E (Playwright) | Crosses frontend, API and persistence |
| `activeClaim` projection, including the Needs Attention case | Integration (Testcontainers) | Needs a real database and a real claim |
| Two simultaneous claims | Already covered by feature 013 | Not re-tested; the rule is unchanged |

**Note on FR-011**: a test asserting "nothing happened" is weak if written carelessly. These
tests assert positively — that no pick request was issued, and that the line's recorded outcome
is unchanged after navigating away and back — rather than merely that no error appeared. This
follows the E2E lesson recorded during feature 015, where vacuous assertions masked a real
failure.
