# Contract: Packing and Label Endpoints

**Feature**: `017-pick-completion-handoff` | **Date**: 2026-09-21

Four additions to the existing HTTP API, and one field added to an existing response. All follow
the codebase's established conventions: authenticated employee from the session, problem responses
carrying a machine-readable code the frontend maps to a typed error, and no customer data in any
JSON payload.

---

## `GET /api/orders/{orderId}/label`

The content printed on an order's label. Every value is derived from the order, so a label cannot
disagree with what it identifies (FR-017), and a reprint matches the original (FR-016).

**Success — 200**

| Field | Meaning |
|---|---|
| `orderId` | The short order code printed on the label |
| `tcgplayerOrderId` | The full identifier, and the value the Code 128 encodes |
| `cardCount` | Physical cards — the only count on the label (FR-010) |
| `pickedBy` | **Every** employee who recorded a pick outcome, ordered by first contribution (FR-041) |
| `pickedAt` | When the most recent outcome was recorded — when picking finished (FR-042) |
| `isHeld` | Whether this is a hold label (FR-013) |
| `setAsideCount` | Cards set aside with the order; meaningful when `isHeld` |
| `shipsShort` | Always false in this feature; the marker exists so the label design is not reopened later (FR-014) |

**No product-line count is returned.** Its absence is deliberate and load-bearing, per PRD §22.1
as amended.

**No customer fields are returned, ever** (FR-015).

**Errors**

| Status | Code | When |
|---|---|---|
| 404 | `orderNotFound` | No such order |
| 409 | `orderNotStarted` | **No pick outcome has been recorded on any line**, so there is nothing to label |

> **The guard is "has picking happened", not "is the order Picked".** A held order's status is
> `NeedsAttention`, never `Picked`, and it must print a hold label (FR-013) — gating on the
> `Picked` status would make the needs-a-manager ending unable to print at all, which is half of
> User Story 1.

---

## `GET /api/orders/{orderId}/packing-slip`

The stored packing slip document, for printing. **This is the only endpoint in the application that
returns customer personal information.**

**Success — 200** — the slip document, as a file response.

Every successful retrieval writes a `PackingSlipAccess` row and an `ILogger<T>` line naming the
order and the employee — never slip content (FR-038).

**Errors**

| Status | Code | When |
|---|---|---|
| 404 | `orderNotFound` | No such order |
| 404 | `packingSlipUnavailable` | The order has no stored slip — imported before this feature, or extraction failed (FR-022) |

`packingSlipUnavailable` is a normal, expected outcome and the frontend states it plainly rather
than presenting it as a failure. It is distinct from `orderNotFound` precisely so the desk can say
"this order has no slip" rather than "this order does not exist".

**Access**: any authenticated employee (FR-037). No role check — the existing role split does not
describe packers, and a check here would obstruct packing rather than protect anything.

**Not linked from any picking surface** (FR-039). This is enforced by the slip living outside the
order payloads that picking screens consume, not by convention.

---

## `POST /api/orders/{orderId}/packed`

Records an order as packed. Terminal; this feature provides no inverse.

**Success — 200** — the order's updated packing view (see below).

Sets `PackedAt` and `PackedByEmployeeId` together, as a conditional write that succeeds only when
the order is not already packed (FR-033).

**Errors**

| Status | Code | When |
|---|---|---|
| 404 | `orderNotFound` | No such order |
| 409 | `orderAlreadyPacked` | Already packed — including the loser of a simultaneous attempt |
| 409 | `orderHasUnresolvedIssue` | An unresolved picking issue blocks packing (FR-034). Carries the unresolved products so the desk can name them (FR-028) |
| 409 | `orderNotAwaitingPacking` | The order is in a state that cannot be packed — still claimed and in progress, for instance |

`orderHasUnresolvedIssue` carries, for each unresolved line, the product name and the number of
cards set aside. Naming the product is required by FR-028; a bare conflict would leave the packer
unable to tell whether it is their problem.

---

## `GET /api/packing/orders/{code}`

Resolves a scanned or typed code to an order's packing view. One rule handles all three input
shapes a packing desk receives (research.md §10).

**Input**: `code` may be a bare order number, a full TCGplayer identifier, or a link produced by
scanning the label's QR. Whitespace and case are normalised at this boundary.

**Success — 200**

| Field | Meaning |
|---|---|
| `orderId`, `tcgplayerOrderId` | Identity |
| `cardCount` | Physical cards |
| `pickedBy` | **All** contributors — the desk has no space constraint, so it shows every one (FR-043) |
| `pickedAt` | When picking finished |
| `status` | The order's current status |
| `canPack` | Whether it can be packed now |
| `blockedReason` | Why not, when `canPack` is false |
| `unresolvedProducts` | Named products blocking packing, when applicable |
| `hasPackingSlip` | Whether a slip can be retrieved |

**The desk reflects the order's current state, not the sticker's.** An order whose issue was
resolved after a hold label was printed reports `canPack: true`, even though the sleeve carries a
hold band.

**Errors**

| Status | Code | When |
|---|---|---|
| 404 | `orderNotFound` | The code matches no order (FR-029) |

---

## `GET /api/packing/awaiting`

Orders currently awaiting packing (FR-030).

**Success — 200** — a list of the packing view above, one per order.

Picked, not yet packed. Projected to the fields the desk shows rather than materialising order
entities, and never includes slip bytes.

---

## Changed: the dashboard counts

The dashboard's picked count becomes a count of orders **awaiting packing** — picked and not yet
packed (FR-036).

This is a change in meaning, not just a label. The existing count only ever grew; the new one falls
when an order is packed, and answers the question anyone actually has: how many picked sleeves are
still on the shelf.

---

## Conventions this contract follows

- Every response above is derived server-side. The frontend renders; it does not compute counts
  that appear on a physical label.
- Error codes are the machine-readable discriminators the frontend maps to typed errors, matching
  the existing `ordersApi` pattern.
- No endpoint here returns customer data except `packing-slip`, which returns the document itself
  and nothing extracted from it (FR-040).
