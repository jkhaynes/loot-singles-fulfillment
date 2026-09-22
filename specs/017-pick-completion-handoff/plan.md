# Implementation Plan: Pick Completion and Hand-off

**Branch**: `017-pick-completion-handoff` | **Date**: 2026-09-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/017-pick-completion-handoff/spec.md`

## Summary

A pick currently ends with a status changing and nothing else, and the sleeve becomes anonymous the
moment it leaves the picker's hand. This feature gives a pick an ending — a screen stating the
physical card count, and a printed label tying the sleeve to its order — then gives the labelled
sleeve somewhere to go: a packing desk that resolves a scanned code, prints that order's packing
slip, and records the order as packed.

Three things make this more than screens. The importer starts **keeping** a per-order packing slip,
which is the first customer personal information this application has ever stored and is permitted
only by PRD §27 as amended. `Packed` becomes the first order status **not derived** from line
outcomes, so feature 015's derivation is preserved and short-circuited rather than loosened. And
the label carries **two codes**, because a scanner types exactly what is encoded and the workflow
has two destinations — this application, and TCGplayer's search for the tracking-number step.

## Technical Context

**Language/Version**: C# / .NET 8, TypeScript 5 / React 19

**Primary Dependencies**: ASP.NET Core, EF Core, PdfPig 1.7.0 (already installed — `PdfMerger`
performs the slip extraction, verified against a real fixture), React Router 7. **Two new frontend
dependencies**: a QR encoder and a Code 128 encoder (research.md §8).

**Storage**: SQL Server. One migration: two `Order` columns, two new tables.

**Testing**: xUnit (unit + integration), Vitest + React Testing Library, Playwright for E2E

**Target Platform**: Azure-hosted web application; picking on phones, packing desk on desktop

**Project Type**: Web application — existing `backend/` and `frontend/` solution layout

**Performance Goals**: No regression in import throughput. Slip extraction adds one lazy document
open and a page copy per order (~3 KB per order, measured) to an operation already dominated by
text extraction.

**Constraints**: Printing is necessarily client-side — the hosted API cannot reach a printer on the
shop LAN. The label must render at a true physical size of approximately 1⅛ × 3½ inches.

**Scale/Scope**: Single business, a handful of concurrent employees. Batches observed up to 200
orders; ~3 KB of stored slip per order.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Product Owner Authority** | Every requirement traces to PRD v0.5 §20.1, §22, §22.1 or §27, or to a decision recorded in the 2026-09-21 discovery session. The one artifact conflict (001's FR-019) is resolved **upward** to the amended PRD and documented, not resolved silently in favour of the lower-level artifact. PASS |
| **II. No Invented Requirements** | The spec's Assumptions section names every default chosen; its Out of Scope section names what is deliberately not built. The label's ships-short marker is built but unset, traced to PRD §22.1 rather than anticipated need. PASS |
| **III. Small, Reviewable Changes** | Four user stories; US1 (ending screens and label) and US2 (packing desk) are each reviewable alone, and US3/US4 are small additions on top. No unrelated refactoring is bundled. PASS |
| **IV. Test-Driven Development** | Red → Green → Refactor for every behavioural change. Code resolution and label content are pure functions and get unit tests first; slip slicing gets fixture-backed tests first; the packed transition, its concurrency guard and its interaction with the 015 derivation get integration tests first; the two ending screens and the packing desk get RTL tests first; scenarios 1–7 of quickstart.md get E2E coverage. PASS |
| **V. Safe Failure Over Silent Corruption** | FR-021 is the load-bearing one: a slip that cannot be extracted must not reject its order or fail its batch, because the order data parsed successfully and is authoritative — slip absence is not order corruption. FR-022 makes a missing slip a stated, normal outcome rather than an error. The application never records "printed" as a fact, because it cannot detect whether paper emerged. PASS |
| **VI. Server-Enforced Critical Business Rules** | The packed transition is enforced server-side by a conditional write, mirroring feature 013's claiming rather than inventing a second concurrency idiom. `canPack` on the packing view is presentation over an authoritative answer, not a client-side gate — the server still refuses a losing attempt. PASS |
| **VII. Data Minimization and Credential Security** | **The principle this feature tests.** It is satisfied by PRD §27's four bounds, not by avoidance: one order per file, no picking surface reaching a slip, recorded access, retention deferred and recorded as open question 59. Structural rather than conventional — the slip lives outside every payload a picking screen consumes (research.md §4). No customer field is extracted from a slip into the data model (FR-040). No log line carries customer data. PASS, with the note below. |
| **VIII. One Responsive Product** | The packing desk is a desktop-oriented surface in the same application, sharing the same authentication and API conventions. No second app, no duplicated business logic. PASS |
| **IX. Replaceable Integrations** | Slip slicing is a **separate** seam from parsing, precisely so that replacing the parser with a future TCGplayer API integration leaves the slicer unused rather than half-reimplemented (research.md §2). PdfPig stays behind both interfaces in Infrastructure. PASS |
| **XI. Reliability During Fulfillment** | No new failure mode during picking: the ending screens read data that already exists, and printing failure degrades to printing later. Import gains a step that is explicitly forbidden from failing its caller. Logging is warranted for slip access and slip-extraction failure; the ending screens are reads and warrant none. PASS |
| **XII / XIII. Maintainable Design, Proportional Abstraction** | Two small interfaces for two genuinely different jobs, not one interface with a mode flag. The label is derived, not stored, so it cannot drift. No blob store for 3 KB files; no audit framework for one three-column table. Two frontend dependencies, each justified individually against hand-rolling (research.md §8). PASS |
| **EF Core Standards** | The slip's bytes live in a separate table so they are never materialised by the order-loading paths. Awaiting-packing is projected to the fields the desk shows rather than materialising order graphs. The packed write is a conditional set-based update, consistent with the existing claiming code. PASS |

### Note on Principle VII

This feature makes the application store customer personal information for the first time. That is
authorised by PRD §27 as amended (A14, approved 2026-09-21), and the four bounds in §27 are the
whole of the mitigation.

**Weakening any one of them re-opens the amendment rather than adjusting an implementation
detail.** If implementation finds a bound impractical, that returns to `/speckit-clarify` or to the
Product Owner — it is not a decision to be made in code.

## Conflict Resolved: feature 001's FR-019

`specs/001-tcgplayer-order-import/spec.md` FR-019 states, emphatically, that no artifact containing
the customer's shipping details may remain in persistent storage, and calls this "a hard
requirement, not an implementation detail to be decided later." The `IPackingSlipParser` doc comment
carries the same instruction.

This feature requires the opposite. The conflict is resolved **upward**, on Principle I's hierarchy:
001's FR-019 was written under PRD §27 as it stood before amendment A14, and a superseded PRD clause
cannot keep a downstream specification alive.

**The reversal is narrower than it looks.** FR-020 still forbids retaining the batch document — half
of 001's FR-019 survives intact. What changes is that a per-order extract is retained, which is the
narrowest form of the reversal that delivers the workflow.

**This must be tasked, not left implicit**: 001's FR-019, SC-004, User Story 3 and Assumptions need
superseded annotations, and the `IPackingSlipParser` doc comment needs correcting. Leaving two
contradictory hard requirements in the repository would mislead the next implementer and would be
correctly flagged by `/branch-review`. Full reasoning in research.md §1.

## Project Structure

### Documentation (this feature)

```text
specs/017-pick-completion-handoff/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — ten decisions
├── data-model.md        # Phase 1 — entities and invariants
├── quickstart.md        # Phase 1 — validation scenarios
├── contracts/
│   └── packing-api.md   # Phase 1 — endpoint contracts
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Created by /speckit-tasks, not here
```

### Source Code (repository root)

```text
backend/src/
├── LootSingles.Domain/Orders/
│   ├── Order.cs                          # + PackedAt, PackedByEmployeeId
│   ├── OrderStatus.cs                    # + Packed
│   ├── OrderPackingSlip.cs               # new
│   └── PackingSlipAccess.cs              # new
├── LootSingles.Application/
│   ├── Import/
│   │   ├── RawOrderBlock.cs              # + PageNumbers
│   │   ├── IPackingSlipSlicer.cs         # new seam
│   │   ├── IPackingSlipParser.cs         # doc comment corrected
│   │   └── PackingSlipImportService.cs   # slice + store, must not fail the import
│   └── Packing/                          # new
│       ├── PackingService.cs             # resolve, pack, awaiting
│       ├── PackingCodeResolver.cs        # pure: link / number / identifier
│       ├── LabelContent.cs               # derived label payload
│       └── IPackingRepository.cs
├── LootSingles.Infrastructure/
│   ├── Import/PdfPigPackingSlipSlicer.cs # new — PdfMerger
│   └── Persistence/
│       ├── PackingRepository.cs          # new
│       ├── OrderStatusComputation.cs     # unchanged; packed short-circuits ahead of it
│       ├── Configurations/               # + slip, access
│       └── Migrations/                   # one migration
└── LootSingles.Api/Controllers/
    ├── PackingController.cs              # new
    └── OrdersController.cs               # + label, packing-slip, packed

frontend/src/features/
├── orders/
│   ├── OrderDetailPage.tsx               # routes to the ending outcome
│   ├── PickEnding.tsx                    # new — the two endings
│   └── ordersApi.ts                      # + label, packed, typed errors
├── packing/                              # new
│   ├── PackingDeskPage.tsx
│   ├── ScanBox.tsx
│   └── packingApi.ts
├── labels/                               # new
│   ├── OrderLabel.tsx                    # QR + Code 128 + text
│   └── label.css                         # physical units, print stylesheet
└── dashboard/DashboardPage.tsx           # Picked → Awaiting packing

backend/tests/
├── LootSingles.UnitTests/                # resolver, label content, slicing
├── LootSingles.IntegrationTests/         # packed transition, concurrency, slip access
└── LootSingles.Fixtures/PackingSlips/    # + a fixture whose slip cannot be sliced
frontend/tests/ + e2e/                    # RTL for both new screens; Playwright scenarios 1–7
```

**Structure Decision**: The existing backend layering (Domain / Application / Infrastructure / Api)
and the frontend's feature-folder convention are followed exactly. Two new feature folders —
`Packing` on the backend, `packing` and `labels` on the frontend — because packing is a distinct
workflow from picking and the label is shared by both the ending screens and the desk. Nothing
existing is relocated.

## Phase 0: Research — key decisions

Full reasoning in [research.md](research.md).

1. **001's FR-019 conflict** — resolved upward to PRD §27 as amended; annotation tasked.
2. **Slip extraction** — parser records page numbers; a separate slicer seam does the cutting.
3. **Reading the upload twice** — rewind the stream; buffer only when it cannot seek.
4. **Slip storage** — a separate table, so slip bytes are never materialised by picking paths.
5. **Slip access** — a durable row plus a log line, because stdout-only logging cannot satisfy
   "attributable after the fact".
6. **`Packed`** — short-circuits ahead of the 015 derivation, which is left untouched.
7. **Double pack** — a conditional write, mirroring feature 013's claiming.
8. **The two codes** — rendered client-side from server-supplied content; two new dependencies,
   each justified against hand-rolling.
9. **Physical print size** — proven on the real printer **first**; it is the feature's largest risk.
10. **Scanned code resolution** — one normalising rule at the boundary for all three input shapes.

## Phase 1: Design — outputs

- **[data-model.md](data-model.md)** — `Order` gains two fields; `OrderPackingSlip` and
  `PackingSlipAccess` are new; `RawOrderBlock` gains page numbers; the label is deliberately **not**
  an entity, so it cannot drift from its order.
- **[contracts/packing-api.md](contracts/packing-api.md)** — four new endpoints and one changed
  dashboard count. `packingSlipUnavailable` is distinguished from `orderNotFound` so the desk can
  say which is true.
- **[quickstart.md](quickstart.md)** — ten validation scenarios, plus five privacy checks that
  verify PRD §27's four bounds directly.

## Task-Ordering Consequence

Proving a browser can produce a 1⅛ × 3½ inch label on the real printer is a hardware unknown, not a
code unknown — no amount of careful implementation retires it, only a printed label does.

**The Product Owner deferred it on 2026-09-21**: the printer is not available and the feature
proceeds without waiting, with print problems to be debugged or designed around later. The
validation moves from a gate at the start to outstanding work at the end (tasks.md T001,
quickstart.md scenario 0).

The design absorbs that deferral in one place: every physical dimension of the label lives as a
named token in a single stylesheet, so wrong numbers are corrected without touching the label's
structure, the ending screens, or the desk's reprint path. What tokens cannot absorb is a browser
that cannot produce a correctly sized label at all — that would change the label's form, and the
work resting on it would move. That residual is carried knowingly. See research.md §9.

## Architecture and Changeability Review

Six components carry real design risk. Each is evaluated against the constitution's review
questions; the rest of the feature is composition of existing patterns.

### `IPackingSlipSlicer` — cutting one order's pages out of a batch

**Responsibility**: given a document's bytes and a set of page numbers, produce a document
containing those pages. Nothing else.

**Boundaries**: it does not know what an order is, does not decide which pages belong together, and
does not store anything. Page association is the parser's knowledge (it already merges continuation
pages); storage is the persistence layer's job.

**Dependency direction**: the interface lives in Application and depends only on `byte[]` and page
numbers. The PdfPig implementation lives in Infrastructure. Application never references PdfPig —
the same containment the parser already has.

**Foreseeable variation**: the concrete one is the TCGplayer API integration that PRD §28 and
Principle IX anticipate. When order data arrives structurally, there is no document to slice, the
parser is replaced, and **the slicer becomes unused rather than half-reimplemented**. That is the
whole reason it is a separate seam and not an extra method on `IPackingSlipParser`.

**Extension cost**: a different document source means a different implementation of one two-input
method. A different storage target does not touch it at all.

**Failure modeling**: slicing fails as an application condition, not an exception escaping to the
caller. A page range that cannot be copied yields "no slip for this order", which
`PackingSlipImportService` is required to absorb (below).

**Domain integrity**: it produces bytes, not a domain object. The bytes become an
`OrderPackingSlip` only once associated with an order that has itself passed validation — so an
order that is rejected never acquires a slip.

**Testability**: a pure function of bytes plus page numbers. Testable against the existing
`LootSingles.Fixtures/PackingSlips` files with no database, no HTTP, and no import pipeline.

**Abstraction rationale**: it exists to keep PdfPig out of Application and to isolate the piece a
future integration deletes. Both are concrete, present problems.

**Simplicity**: the simpler design — the parser returns slip bytes alongside blocks — was
considered and rejected in research.md §2. It removes an interface but merges two responsibilities
into the seam most likely to be replaced.

### `PackingSlipImportService` — the step that must not fail its caller

**Responsibility**: orchestrate parse → validate → persist, now also slice → store.

**Boundaries**: the new step is explicitly **subordinate**. FR-021 requires that a slip which
cannot be sliced or stored must not reject its order, and must not fail any other order or the
batch.

**Failure modeling** — the design question that matters here. PRD §26 and Constitution V bias this
pipeline toward *rejection over silently importing questionable data*, and it would be easy to
apply that reflexively to slips. That would be wrong: **§26's concern is order-data integrity, and
slip absence is not order corruption.** The order's lines parsed successfully and are authoritative;
the slip is a convenience for a later workflow. So a slip failure is recorded and swallowed, while
an order-data failure continues to reject exactly as today.

This distinction is the single most likely place for this feature to introduce a regression in an
existing, working, safety-critical pipeline. It gets an explicit test with a fixture built for it.

**Extension cost**: storing something else derived per order follows the same subordinate-step
shape.

**Testability**: the existing import tests cover rejection behaviour; the new fixture covers a slip
that cannot be sliced. Neither needs the packing desk to exist.

### The packing-slip boundary — where the PII lives

**Responsibility**: hold one customer's shipping document, release it only to the packing workflow,
and record every release.

**Boundaries**: this is the **only** place in the application holding customer personal
information. `Order` continues to carry no customer fields, and FR-040 forbids extracting any out
of a slip.

**Dependency direction** — and the load-bearing design choice: the slip is a separate table rather
than a column on `Order`. That is not only an EF performance concern. It makes FR-039 — no picking
surface reaches a slip — **structural**: a picking payload cannot accidentally include slip content,
because loading an order does not load a slip. The alternative maintains that rule by discipline in
every present and future projection, which is exactly the kind of rule that decays.

**Foreseeable variation**: retention. PRD §41 open question 59 records that a deletion rule is owed.
Deleting slips later touches one table and one relationship, and nothing in the packing workflow
depends on a slip being permanent — the desk already handles absence as a normal state (FR-022),
which means **the retention rule, when it arrives, lands on a code path that already exists and is
already tested.** That is deliberate.

**Failure modeling**: absence is normal, not an error. `packingSlipUnavailable` is a distinct
outcome from `orderNotFound` so the desk can state which is true.

**Testability**: access recording is an integration test — retrieve a slip, assert a durable row
naming employee and time. The "no picking surface reaches a slip" property is tested by asserting
picking payloads carry no slip content.

**Simplicity**: one table with bytes, one table with three columns. A blob store and an audit
framework were both considered and rejected as disproportionate (research.md §4, §5).

### `Packed` — one exception ahead of an untouched rule

**Responsibility**: represent that an order's sleeve has been dispatched.

**Boundaries**: it is an *event*, recorded with who and when. Everything else about status stays
derived from line outcomes and claim state.

**Dependency direction**: the short-circuit sits ahead of `OrderStatusComputation.FromCurrentLines`,
which is **not modified**. Feature 015's one-derivation rule survives with one documented exception
rather than being loosened to accommodate a state it cannot express.

**Foreseeable variation**: PRD §20.1 defines `Awaiting Customer Decision` and `Cancelled`, both
belonging to the issue-resolution feature. `Awaiting Customer Decision` is plausibly derivable from
line state; `Cancelled` is plausibly another recorded event. This feature establishes the shape a
recorded state takes, so the next one has a pattern to follow rather than a precedent to argue
about.

**Extension cost**: another recorded state is another short-circuit and another pair of columns.

**Failure modeling**: the transition is a conditional write that fails as a stated conflict
(`orderAlreadyPacked`), never as a silent no-op — Constitution XI forbids reporting success when the
write did not happen.

**Testability** — and the regression worth naming: every existing write path that recomputes status
(claim, release, force-release, record outcome) must be unable to recompute a packed order back to
`Picked`. These are enumerated and tested rather than assumed safe, because the failure mode is
silent and would surface as an order reappearing on the shelf.

### `PackingCodeResolver` — the trust boundary at the scan box

**Responsibility**: turn whatever arrives in the desk's input into an order lookup key.

**Boundaries**: it normalises and classifies; it does not query. Three shapes arrive — a link from a
QR scan, a bare order number, a TCGplayer identifier from a Code 128 scan — because a scanner is a
keyboard and the desk cannot know which fired.

**Domain integrity**: this is where untrusted input becomes a lookup. Trimming, case folding and
reducing a link to the order it points at all happen here, once, rather than being spread across
the lookup path.

**Failure modeling**: an unrecognised shape resolves to nothing and the desk says the code matches
no order — it never guesses.

**Testability**: a pure function. Every input shape, plus malformed links and scanner terminators,
tested without a database.

**Simplicity**: one function, not a parser hierarchy or a chain of strategies for three cases.

### `OrderLabel` — derived, never stored

**Responsibility**: render label content at a true physical size, including both codes.

**Boundaries**: it renders; it does not compute. Every count comes from the server (FR-017), so a
label cannot disagree with the order it identifies and a reprint cannot drift from the original.

**Dependency direction**: it depends on a server-supplied payload and two encoding libraries. It
does not reach into order internals.

**Foreseeable variation**: the ships-short marker (FR-014) is built now and set by nothing, because
short shipments arrive with the issue-resolution feature. This is the one place the feature builds
ahead of need, and it is traced to PRD §22.1 rather than anticipated: **reopening the layout of a
physical label after stickers are in circulation is materially worse than printing an unused
marker.**

**Failure modeling**: the application cannot detect whether paper emerged from a printer, so it
never records "printed" as a fact about an order. FR-006 turns on the label being *requested*. A
failed print degrades to printing again later, never to a stuck order.

**Testability**: label content is a pure derivation, unit-testable without rendering. Physical size
is **not** unit-testable — it is hardware, and only quickstart.md scenario 0 retires it.

**Simplicity**: no label entity, no snapshot, no template engine. The strongest simplification
available is the one taken: the label has no state of its own.

### Simplicity check across the feature

What this feature deliberately does **not** build: no blob store, no audit framework, no label
entity, no un-pack action, no packed-order history view, no role for packers, no retention
scheduler, no second concurrency idiom, no strategy pattern for three input shapes.

What it adds beyond the minimum, and why each is justified: a second integration seam (isolates
what a future API integration deletes), a separate slip table (makes a privacy rule structural), an
access table (stdout logging cannot be durable), two frontend dependencies (hand-rolled encoders
print defects onto physical labels), and one unused label marker (reopening a printed label's
layout is worse).

## Complexity Tracking

> No constitutional violations require justification. Two decisions warrant recording because they
> add surface, and both were weighed against the simpler option.

| Decision | Why needed | Simpler alternative rejected because |
|---|---|---|
| A second integration seam for slicing, rather than extending the parser | Parsing and slicing are different jobs against the same library, and the parser is the seam most likely to be replaced by a future TCGplayer API | Extending `IPackingSlipParser` merges a document-reading contract with a document-producing one, and a replaced parser would drag slicing out with it |
| Two new frontend dependencies for the two codes | QR needs Reed–Solomon, mask evaluation and mode selection; Code 128 needs a checksum whose off-by-one scans wrong while looking right | Hand-rolling either puts a defect generator onto physical labels stuck to sealed sleeves; server-side rendering moves the dependency to the backend rather than removing it, and adds a round trip to every reprint |
