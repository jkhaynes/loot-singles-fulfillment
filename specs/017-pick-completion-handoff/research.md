# Phase 0 Research: Pick Completion and Hand-off

**Feature**: `017-pick-completion-handoff` | **Date**: 2026-09-21

Nine decisions. The first is a direct conflict with an existing feature specification and is
resolved on Product Owner authority, not by preference.

---

## §1 — Feature 001's FR-019 forbids what this feature requires

**The conflict.** `specs/001-tcgplayer-order-import/spec.md` FR-019 reads:

> System MUST NOT retain the original packing slip PDF file, or any copy, scan, or export of it,
> beyond the scope of a single import operation… no artifact containing the customer's shipping
> name, address, or contact details from that file may remain in persistent storage. **This is a
> hard requirement, not an implementation detail to be decided later.**

The same position is carried in 001's Assumptions, SC-004, User Story 3, and in the
`IPackingSlipParser` doc comment: *"Implementations must not persist the supplied stream or any
copy of it anywhere durable (FR-019)."*

**Decision**: 017 supersedes that clause, on the authority of PRD v0.5 §27 (amendment A14, approved
by the Product Owner 2026-09-21). The constitution's Principle I hierarchy is explicit — confirmed
Product Owner decisions, then approved PRD, then feature specification. 001's FR-019 was written
under PRD §27 as it stood before A14, and a superseded PRD clause cannot keep a downstream
specification alive.

**The reversal is narrower than it first appears**, and the narrowness matters:

| 001 FR-019 required | 017 does |
|---|---|
| The original batch PDF is not retained | **Still true** — FR-020 requires exactly this |
| No copy or export of it is retained | **Reversed** — a per-order extract is retained |
| No artifact with customer shipping details persists | **Reversed** — bounded by PRD §27's four rules |

Half of FR-019 survives intact. What changes is that a *per-order* extract is now retained, which
is the narrowest form of the reversal that delivers the workflow.

**Required follow-up, to be tasked**: 001's FR-019, SC-004, User Story 3 and Assumptions must be
annotated as superseded by 017 and PRD §27, and the `IPackingSlipParser` doc comment corrected. The
repository must not hold two contradictory "hard requirements" — a later `/branch-review` would be
right to flag it, and a future implementer reading 001 first would be actively misled.

**Alternatives considered**: (a) Silently implement over the top of FR-019 — rejected; CLAUDE.md
forbids resolving an artifact conflict in favour of the lower-level artifact, and this would be
resolving it in favour of neither. (b) Return to `/speckit-clarify` — rejected; nothing is unclear,
the PRD was amended precisely to authorise this, and the amendment names the conflict it creates.

---

## §2 — Where the per-order slip is produced

**Decision**: the parser gains page numbers only. A second, narrow seam performs the extraction.

- `RawOrderBlock` gains the page numbers the block was assembled from. The parser already walks
  pages and already merges continuation pages, so it is the only component that knows this.
- A new Application-layer interface takes the original document bytes plus a set of page numbers and
  returns one PDF containing those pages. Its PdfPig implementation lives in Infrastructure beside
  the parser.

**Rationale**: `IPackingSlipParser` is the documented replaceable-integration seam (Constitution
IX) and its job is *reading* a document. Splitting is a different job against the same library, and
a future TCGplayer API integration would supply order data with no document to split — at which
point the parser is replaced and the slicer becomes unused rather than half-reimplemented. Two
small interfaces express that; one interface doing both does not.

**Alternatives considered**: (a) The parser returns slip bytes alongside blocks — one pass, no
second open, but it merges two responsibilities into the seam most likely to be replaced, and holds
every order's bytes in the parse result. (b) Reconstruct pages from parsed text — rejected outright;
it would produce a document that is not the packing slip.

**Cost accepted**: the document is opened twice. PdfPig's `Open` is lazy and the slicer never calls
`GetWords`, so the second pass copies page objects without the text extraction that dominates parse
time.

---

## §3 — Reading the upload twice

**Decision**: rewind the existing stream between parse and slice; buffer only if it cannot seek.

The import service receives a `Stream` that the parser consumes. `IFormFile.OpenReadStream()`
returns a stream over already-buffered request content and is seekable in practice, so the normal
path is to reset its position. Where `CanSeek` is false, copy to a `MemoryStream` first.

**Rationale**: the upload is already capped at 25 MB (`ImportsController.MaximumFileBytes`), so an
unconditional buffer would be bounded and safe — but it would also allocate 25 MB on a path that
usually does not need it. A guarded rewind costs one property check.

**Alternatives considered**: always buffer — simpler to reason about, rejected as an unnecessary
allocation on every import. Re-reading from disk — there is no file on disk to re-read.

---

## §4 — Where the slip is stored

**Decision**: a separate table holding one slip per order, not a column on the order.

**Rationale**: a `varbinary(max)` column on `Order` is loaded by any query that materialises an
`Order` entity, and the codebase materialises orders on the claim, release, and pick-recording
paths. The constitution's EF standards require loading relationships intentionally and preferring
projection; a separate one-to-one table means slip bytes are read only when something asks for a
slip. It also makes "no picking surface reaches a slip" (FR-039) structurally obvious rather than a
rule maintained by discipline in every projection.

A dedicated blob store was considered and rejected under Principle XIII: a few kilobytes per order
does not justify a second storage system, its configuration, its credentials, or its failure modes.
Measured size is ~3 KB per order against a 35 KB thirteen-order batch.

---

## §5 — Recording slip access

**Decision**: a durable access record, plus an `ILogger<T>` line.

**Rationale**: the constitution's observability rule confines production logging to `ILogger<T>`
writing to **console/stdout only**, with no log platform permitted. Stdout on the hosted
environment is ephemeral, so logging alone cannot satisfy FR-038 ("MUST record every packing slip
retrieval") or SC-010 ("attributable… after the fact"). An access row is the only durable
mechanism available under that constraint.

The row holds the order, the employee and the time. It holds no customer data, so it is not itself
a second copy of the PII problem. The `ILogger<T>` line is retained for operational visibility and
likewise names only the order and employee — never slip content, which the constitution forbids
logging.

**Alternatives considered**: logging only — rejected above. A general-purpose audit framework —
rejected under Principle XIII; one table with three meaningful columns serves one requirement.

---

## §6 — Packed, the first status that is not derived

**Decision**: preserve the existing derivation untouched; layer a recorded packed event over it.

Feature 015 established that status is never an independent flag — it is recomputed from current
line outcomes and claim state on every write that can change it, in one shared expression
(`OrderStatusComputation.FromCurrentLines`). Nothing about an order's lines changes when its sleeve
is posted, so `Packed` cannot join that derivation.

The shape is: if the order has a recorded packed event it is `Packed`; otherwise derive exactly as
today. The existing expression is not modified — a short-circuit is placed ahead of it.

**Rationale**: the alternative is to weaken the derivation so it can express a non-derived state,
which trades a rule that has held for two features against one new status. One documented exception
ahead of an unmodified rule is cheaper to reason about and cheaper to remove.

**Consequence to test explicitly**: a packed order must not have its status recomputed back to
`Picked` by any existing write path. Every caller of the derivation is a candidate regression, and
they are enumerated during implementation rather than assumed.

---

## §7 — Preventing a double pack

**Decision**: a conditional write that only succeeds when the order is not already packed, with the
caller told plainly when it loses.

**Rationale**: Constitution VI makes the backend the enforcement point for order state transitions,
and FR-033 forbids recording a pack twice under simultaneous attempts. This mirrors how feature 013
enforces exclusive claiming, so it follows an established pattern in this codebase rather than
inventing a second concurrency idiom. A read-then-write check is rejected: it has a race window by
construction, which is the defect the requirement names.

---

## §8 — Rendering the label's two codes

**Decision**: render both codes in the browser, from label data supplied by the server.

The server supplies the label's *content* — short code, full identifier, card count, picker, time,
hold state, set-aside count — so FR-017 holds and a label cannot disagree with its order. The
browser renders that content, including generating the QR and Code 128 images, and prints it.

**Chosen (T003)**: `qrcode-generator@1.4.4` and `jsbarcode@3.12.1`, both pinned exactly. Each ships
its own TypeScript declarations, so no `@types` packages are added, and both render to SVG — which
is what a print stylesheet needs, since SVG scales to physical units without resampling. `npm
install` reported no vulnerabilities and added two packages in total.

**Two new frontend dependencies**, against a dependency list currently holding only React, React
DOM and React Router. Each is justified separately:

- **QR encoding** is not a few lines of code. It requires Reed–Solomon error correction, mode
  selection and mask evaluation; a hand-rolled version would be a defect generator printed onto
  physical labels. A small single-purpose library is the correct rung.
- **Code 128** is more tractable by hand — a symbol table, a weighted checksum, a start/stop
  pattern — but an off-by-one in the checksum produces a barcode that looks right and scans wrong,
  which is the worst available failure for a sticker on a sealed sleeve.

**Alternatives considered**: generating both codes server-side as SVG. It would put all label
content behind one contract and add no frontend dependency, but it adds backend dependencies
instead, puts image rendering on the API for output that only ever renders in a browser, and adds a
round trip to every reprint. Rejected as moving the dependency rather than removing it.

**Not deferred to implementation**: the physical print behaviour (§9), which the library choice
does not influence.

---

## §9 — Printing at a true physical size

**Decision**: a print stylesheet expressing the label in physical units, proven on the real printer
**before** the rest of the label work is built.

Printing is necessarily client-side: the hosted API cannot reach a printer on the shop LAN. The
label is expressed in inches or millimetres rather than pixels, with an explicit page size matching
the stock, so the browser is asked for a physical size rather than a scaled approximation.

**This is the feature's largest risk and it is unresolved.** Whether a browser-generated label comes
out at 1⅛ × 3½ inches on the shop's small label printer depends on print scaling, driver defaults
and margin handling that cannot be determined from here. The owners confirm shipping labels have
been printed to that hardware from a computer before, which is encouraging and is not the same
thing.

**Deferred by Product Owner decision, 2026-09-21.** The printer is not available, and the feature
proceeds on the assumption that browser printing works, with print problems to be debugged or
designed around later. The validation is not cancelled — it moves from a gate at the start to
outstanding work at the end (tasks.md T001, quickstart.md scenario 0).

**What makes the deferral affordable**: the label stylesheet expresses every physical dimension as
a named token in one place — stock size, margins, QR module size, barcode height, type sizes. If
the printer disagrees with our assumed numbers, the correction is those values and nothing else.
The label's structure, the ending screens that print it, and the desk's reprint path are all
independent of the numbers.

**What it does not cover**: tokens absorb *wrong numbers*. They do not absorb a browser being
unable to produce a correctly sized label at all — a driver that always scales to fit, for
instance. That would change the label's form rather than its measurements, and the work resting on
it would move. This residual risk is carried knowingly rather than mitigated, which is why T001
stays on the task list until a physical label has been measured.

---

## §10 — Resolving a scanned code

**Decision**: one resolution rule, applied to whatever arrives in the packing desk's input.

Three things can arrive: a link produced by a scanner reading the label's QR, a bare order number
typed by a person, and the full TCGplayer identifier from a scanner reading the Code 128. The input
is trimmed, and a value carrying a link's structure is reduced to the order it points at before
resolution; the remainder is matched as an order number or a TCGplayer identifier.

**Rationale**: a scanner is a keyboard, so the desk cannot know which of the three it received.
Normalising once at the boundary keeps the rule in one place rather than spreading three input
shapes through the lookup.

**Case and whitespace are normalised** because a scanner may append a terminator and a person may
type inconsistently — this is input handling at a trust boundary, not a lazy match.
