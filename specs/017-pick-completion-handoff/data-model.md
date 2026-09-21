# Phase 1 Data Model: Pick Completion and Hand-off

**Feature**: `017-pick-completion-handoff` | **Date**: 2026-09-21

Two new entities, two new fields on `Order`, one new status value, and one new field on the raw
parse result. Nothing else in the existing model changes.

---

## `Order` — two recorded fields

The order gains the packed event. Both fields are null until the order is packed, and both are set
together: one is never meaningful without the other.

| Field | Meaning |
|---|---|
| `PackedAt` | When the order was recorded as packed. Null until packed. |
| `PackedByEmployeeId` | Which employee recorded it. Null iff `PackedAt` is null. |

`Order` continues to carry **no customer fields**, as its existing documentation states. That
remains true: the customer's information lives only in the stored slip (below), never on the order.

**Invariants**

- `PackedAt` is null if and only if `PackedByEmployeeId` is null.
- Once set, neither is ever cleared. Packed is terminal (FR-031) and this feature provides no
  action that unsets them.
- An order with any line holding an unresolved issue must never reach a packed state (FR-034).

---

## `OrderStatus` — one new value

`Packed` joins the existing `Ready`, `InProgress`, `Picked` and `NeedsAttention`.

**It is the only value not derived from line outcomes.** Feature 015 established that status is
recomputed on every write from the order's current lines and claim state, never stored as an
independent flag. That derivation is unchanged. `Packed` sits ahead of it:

```text
order has a recorded packed event   → Packed
otherwise                           → the existing 015 derivation, untouched:
                                        any line has an unresolved issue → NeedsAttention
                                        else every line picked           → Picked
                                        else claimed                     → InProgress
                                        else                             → Ready
```

**The regression this creates**: every existing write path that recomputes status — claiming,
releasing, force-releasing, and recording a line outcome — must not be able to recompute a packed
order back to `Picked`. These paths are enumerated and covered by test during implementation rather
than assumed safe.

---

## `OrderPackingSlip` — new entity

One stored packing slip per order, holding exactly that order's pages from the imported batch
document.

| Field | Meaning |
|---|---|
| `OrderId` | The order this slip belongs to. One slip per order at most. |
| `Content` | The slip document's bytes. |
| `PageCount` | How many pages of the batch document the order occupied. |
| `StoredAt` | When it was extracted and stored. |

**This is the only place in the application that holds customer personal information.** It carries
the customer's name and shipping address, because that is what a packing slip is.

**Invariants**

- A slip contains exactly one order's pages and never another order's (FR-019).
- The batch document from which it was extracted is not stored anywhere (FR-020).
- An order may have no slip. Orders imported before this feature have none, and an order whose slip
  could not be extracted has none (FR-021, FR-022). Absence is a normal state, not an error.

**Why a separate table rather than a column on `Order`**: a large binary column on `Order` is
materialised by every query that loads an order entity, including the claim and pick-recording
paths. Keeping it separate means slip bytes are read only when a slip is asked for, and makes
FR-039 — no picking surface reaches a slip — a structural property rather than a rule that every
future projection has to remember. See research.md §4.

---

## `PackingSlipAccess` — new entity

One row per retrieval of a packing slip.

| Field | Meaning |
|---|---|
| `OrderId` | Whose slip was retrieved. |
| `EmployeeId` | Who retrieved it. |
| `RetrievedAt` | When. |

**Invariants**

- A row is written on every successful retrieval (FR-038).
- Rows are append-only. Nothing in this feature edits or deletes one.
- A row holds **no customer data** — it records that an access happened, never what was in the slip.

**Why a table and not just a log line**: the constitution confines production logging to
`ILogger<T>` writing to console/stdout only, with no log platform permitted. Stdout is ephemeral on
the hosted environment, so it cannot satisfy "attributable after the fact" (SC-010). A log line is
still written for operational visibility; the row is what makes the record durable. See
research.md §5.

---

## `RawOrderBlock` — one new field

| Field | Meaning |
|---|---|
| `PageNumbers` | The page numbers of the batch document this block was assembled from, in order. |

The parser already walks pages and already merges a multi-page order's blocks into one, so it is
the only component that knows which pages belong to which order. Carrying the numbers out is what
lets the slip be sliced without re-deriving the association.

A block whose identifier could not be read still carries its page numbers; whether it becomes an
order is decided downstream, unchanged.

---

## Label — deliberately not stored

The label is **not** an entity. It is produced on demand from the order's own data every time it is
printed.

| Printed | Source |
|---|---|
| Short order code | The order's own number |
| Full TCGplayer identifier | `Order.TcgplayerOrderId` |
| Physical card count | Computed from the order's lines |
| Picker and pick time | The order's existing pick record |
| Hold state, set-aside count | The order's current line outcomes |
| Ships-short marker | Not set by anything in this feature (FR-014) |

**Why nothing is stored**: FR-017 requires a label to agree with the order it identifies, and
FR-016 requires a reprint to match the original. A stored label snapshot could drift from its
order; a derived one cannot. The one field that looks like a snapshot — the original picker and
pick time — is already recorded on the order by feature 015, so a reprint reads the same values the
first print did without a second copy existing.

---

## Entity relationships

```text
Order 1 ──── 0..1 OrderPackingSlip      removing an order removes its slip
Order 1 ──── 0..* PackingSlipAccess     append-only access history
Order * ──── 0..1 Employee (packed by)  alongside the existing claimed-by relationship
```

*(`Order`–`OrderLine` and `Order`–`PickingIssue` are unchanged by this feature.)*
