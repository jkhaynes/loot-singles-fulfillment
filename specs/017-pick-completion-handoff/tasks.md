---

description: "Task list for 017-pick-completion-handoff"
---

# Tasks: Pick Completion and Hand-off

**Input**: Design documents from `/specs/017-pick-completion-handoff/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/packing-api.md](contracts/packing-api.md),
[quickstart.md](quickstart.md)

**Tests**: **Required, not optional.** Constitution Principle IV is NON-NEGOTIABLE: every
behavioural change follows Red → Green → Refactor, and a behaviour's test task appears before its
implementation task. Tests written after the fact to satisfy coverage are a violation, not a
shortcut.

**Organization**: Grouped by user story so each can be implemented, tested and demonstrated
independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task serves (US1–US4)

## Path Conventions

Web application layout per plan.md: `backend/src/`, `backend/tests/`, `frontend/src/`,
`frontend/tests/`, `frontend/e2e/`.

---

## Phase 1: Setup (printer validation deferred)

**Purpose**: Get the dependencies in place, and record that the hardware gate has moved to the end
of the feature rather than the start.

- [ ] T001 **DEFERRED — validate the printed label on the real printer** (quickstart.md scenario 0): print the label at 1⅛ × 3½ inches on the shop's small label printer, measure it against the stock, and scan both codes off the physical label. Adjust the `label.css` tokens from T024 if the size is wrong
- [x] T002 [P] Record the deferral decision and its mitigation in `specs/017-pick-completion-handoff/research.md` §9 and in `spec.md`'s Risks section
- [X] T003 [P] Add a QR encoder and a Code 128 encoder to `frontend/package.json`, pinned, and record the chosen packages in `research.md` §8

> **Product Owner decision, 2026-09-21**: the printer is not available yet and the feature proceeds
> without waiting for it, on the explicit understanding that print problems are debugged or
> designed around later. T001 moves from a blocking gate to outstanding work that must be completed
> before the feature is done — it is deferred, not cancelled.
>
> **What makes the deferral affordable**: T024 requires every physical dimension to be a named
> token in one stylesheet. If the printer disagrees with our assumptions, the correction is a
> handful of values in `label.css`, not a redesign of the label, the ending screens or the desk.
> That containment is the reason this is a reasonable risk to carry rather than a gamble.
>
> **What the deferral does not make affordable**: if browser printing turns out to be unable to
> produce a correctly sized label *at all* — as opposed to needing different numbers — the label's
> form changes and the work resting on it moves. That possibility is not mitigated by tokens, and
> is the reason T001 stays on the list rather than being closed out.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The governance correction that must land before any slip code exists, and the schema
plus status change that US2 and US4 both depend on.

**⚠️ US2 and US4 may not begin until this phase is complete.** US1 depends only on Phase 1 — it's
the ending screens and the label, and needs neither the packed state nor slip storage.

### Governance

- [X] T004 Annotate `specs/001-tcgplayer-order-import/spec.md` FR-019, SC-004, User Story 3 and Assumptions as superseded by 017 and PRD v0.5 §27 (amendment A14), stating precisely what survives — FR-020 still forbids retaining the batch document — per research.md §1
- [X] T005 [P] Correct the doc comment on `backend/src/LootSingles.Application/Import/IPackingSlipParser.cs`, which currently instructs implementations never to persist the stream or any copy of it "(FR-019)"

> T004 and T005 are not housekeeping. Leaving two contradictory hard requirements in the repository
> misleads whoever reads the wrong one first, and `/branch-review` would be right to flag it.

### Schema and status

- [X] T006 [P] Add `Packed` to `backend/src/LootSingles.Domain/Orders/OrderStatus.cs`, documenting that it is the only value not derived from line outcomes
- [X] T007 [P] Add `PackedAt` and `PackedByEmployeeId` to `backend/src/LootSingles.Domain/Orders/Order.cs`, documenting the null-together invariant
- [X] T008 [P] Create `backend/src/LootSingles.Domain/Orders/OrderPackingSlip.cs` per data-model.md
- [X] T009 [P] Create `backend/src/LootSingles.Domain/Orders/PackingSlipAccess.cs` per data-model.md
- [X] T010 Add EF configurations for the two new entities in `backend/src/LootSingles.Infrastructure/Persistence/Configurations/`, with the slip in its own table so order queries never materialise its bytes
- [X] T011 Generate one migration covering T006–T010 in `backend/src/LootSingles.Infrastructure/Persistence/Migrations/`

### The status short-circuit (test first)

- [X] T012 Write failing integration tests in `backend/tests/LootSingles.IntegrationTests/Orders/` asserting a packed order's status survives **every** existing write path that recomputes status — claim, release, force-release, and recording a line outcome — per plan.md's named regression
- [X] T013 Write a failing test asserting an order with an unresolved issue cannot reach `Packed` (FR-034)
- [X] T014 Implement the packed short-circuit ahead of `OrderStatusComputation.FromCurrentLines` in `backend/src/LootSingles.Infrastructure/Persistence/`, leaving the existing derivation expression **unmodified** (research.md §6), making T012 and T013 pass

### A packed order is not claimable (FR-045, added 2026-09-21)

> Found while reviewing Phase 2, not by a test: status correctly stays `Packed`, but nothing stopped
> a picker claiming one. `ClaimSpecificAsync` filters on the claim being free, not on status, and
> Browse Orders has no status filter — so the order was listed, openable, and claimable. A **picked**
> order stays re-claimable by design (015 lets a picker revise lines); packing is where that ends.

- [X] T084 Write failing tests: an integration test that claiming a packed order is refused with a distinct reason (`backend/tests/LootSingles.IntegrationTests/Orders/PackedOrderStatusTests.cs`), and an RTL test that `OrderDetailPage` offers no claim action for a packed order (`frontend/tests/`)
- [X] T085 Add an `OrderAlreadyPacked` claim outcome and guard `ClaimSpecificAsync` on it in `backend/src/LootSingles.Application/Orders/OrderClaimResult.cs`, `backend/src/LootSingles.Infrastructure/Persistence/OrderRepository.cs` and `backend/src/LootSingles.Api/Controllers/OrdersController.cs` — server-side enforcement, per Constitution VI
- [X] T086 Hide the claim action for a packed order in `frontend/src/features/orders/OrderDetailPage.tsx`, and surface the refusal as a typed error in `frontend/src/features/orders/ordersApi.ts`

---

## Phase 3: User Story 1 — A finished pick produces a labelled sleeve (Priority: P1) 🎯 MVP

**Goal**: A pick ends on a screen stating the physical card count, and prints a label that ties the
sleeve to its order.

**Independent test**: Complete a pick and confirm the ending screen shows the correct card count
and a label prints. Complete a pick with an unresolved line and confirm the hold ending appears
with a monochrome-distinguishable label. Neither needs the packing desk to exist.

### Tests for User Story 1 (write first, watch fail)

- [ ] T015 [P] [US1] Unit tests for label content derivation in `backend/tests/LootSingles.UnitTests/Packing/` — card count, hold state, set-aside count, and that **no product-line count is produced** (FR-004)
- [ ] T016 [P] [US1] Integration tests for `GET /api/orders/{orderId}/label` in `backend/tests/LootSingles.IntegrationTests/Orders/` — success shape, `orderNotFound`, `orderNotStarted`, that a **held** order returns a label rather than a conflict (FR-013), and that **no customer field appears in the payload** (FR-015)
- [ ] T017 [P] [US1] RTL tests for the completion ending in `frontend/tests/` — states the card count, shows no product count, and opens no print dialog unprompted (FR-005)
- [ ] T018 [P] [US1] RTL tests for the needs-a-manager ending — cards pulled, cards set aside, and the unresolved products named (FR-003)
- [ ] T019 [P] [US1] RTL test asserting that after a label is requested, continuing to the next order becomes the primary action (FR-006)

### Implementation for User Story 1

- [ ] T080 [P] [US1] Unit tests for the contributor list in `backend/tests/LootSingles.UnitTests/Packing/` — one employee, two, and an order whose lines were recorded by three; distinct, ordered by first contribution; pick time is the **most recent** outcome (FR-041, FR-042)
- [ ] T081 [P] [US1] Unit tests for the label's name formatting in `frontend/tests/` — one name, two names, and three or more rendering as the first two plus a remainder count (FR-043)
- [ ] T020 [US1] Create `backend/src/LootSingles.Application/Packing/LabelContent.cs` deriving every printed value from the order (FR-017), including the contributor list as a distinct projection over line pick outcomes (FR-041, FR-044) — **not** a single picker, since an order released and re-claimed has more than one
- [ ] T021 [US1] Add the label endpoint to `backend/src/LootSingles.Api/Controllers/OrdersController.cs` per contracts/packing-api.md
- [ ] T022 [P] [US1] Add the label client and typed errors to `frontend/src/features/orders/ordersApi.ts`, matching the existing error-class pattern
- [ ] T023 [US1] Create `frontend/src/features/labels/OrderLabel.tsx` rendering text, QR and Code 128 — the QR encoding a link to the order's packing view, the Code 128 encoding the bare TCGplayer identifier with its printed value serving as the human-readable one (FR-011, FR-012)
- [ ] T024 [US1] Create `frontend/src/features/labels/label.css` — physical units and an explicit page size, with **every physical dimension expressed as a named custom property in one block at the top of the file** (stock width and height, page margins, QR module size, barcode height, type sizes), so that correcting T001 later is a change to those values and nothing else
- [ ] T025 [US1] Implement the hold variant's inverted band in `label.css`, distinguishable **in monochrome**, never by colour (FR-013)
- [ ] T026 [US1] Add the ships-short marker to `OrderLabel.tsx`, set by nothing in this feature (FR-014)
- [ ] T082 [US1] Create the name-formatting helper beside `frontend/src/features/labels/OrderLabel.tsx` — first two contributors then `+N`, making T081 pass (FR-043)
- [ ] T027 [US1] Create `frontend/src/features/orders/PickEnding.tsx` presenting both endings, with printing as an explicit action
- [ ] T028 [US1] Route finishing a pick to `PickEnding` in `frontend/src/features/orders/OrderDetailPage.tsx`, replacing the current navigation to `/orders`, preserving the existing claim release (FR-008)
- [ ] T029 [US1] Wire continuing to the next order to the existing pick-next path in `PickEnding.tsx` (FR-007), including the no-orders-available case
- [ ] T030 [US1] E2E coverage of quickstart.md scenarios 1 and 2 in `frontend/e2e/`

**Checkpoint**: A pick ends somewhere and the sleeve carries a label. Demonstrable on its own.

---

## Phase 4: User Story 2 — A packer finds the order and ships it (Priority: P1)

**Goal**: A labelled sleeve is scanned at the bench, its packing slip prints, and the order is
recorded as packed.

**Independent test**: Scan or type a picked order's code at the packing desk, confirm its details
and slip are produced, mark it packed, and confirm it leaves the awaiting-packing list.

### Tests for User Story 2 (write first, watch fail)

- [ ] T031 [P] [US2] Unit tests for slip slicing in `backend/tests/LootSingles.UnitTests/Import/` against `backend/tests/LootSingles.Fixtures/PackingSlips/valid-multi-order-batch.pdf` — one order's pages only, reopens cleanly, text intact
- [ ] T032 [P] [US2] Unit test for slicing a multi-page order using `multi-page-order-no-total-on-continuation-pages.pdf` — all of its pages, none of another order's
- [ ] T033 [P] [US2] Unit tests for `PackingCodeResolver` — a QR link, a bare order number, a full TCGplayer identifier, plus whitespace, case and scanner terminators (research.md §10)
- [ ] T034 [US2] Add a fixture to `backend/tests/LootSingles.Fixtures/PackingSlips/` whose slip cannot be sliced, for T035
- [ ] T035 [US2] Write a failing integration test asserting a slip that cannot be sliced **does not reject its order and does not fail its batch** (FR-021) — the regression plan.md names as most likely
- [ ] T036 [P] [US2] Integration tests for `GET /api/packing/orders/{code}` — all three input shapes, `orderNotFound`, and `canPack` reflecting the order's **current** state rather than the printed label's
- [ ] T037 [P] [US2] Integration tests for `POST /api/orders/{orderId}/packed` — success, `orderAlreadyPacked`, `orderNotAwaitingPacking`, and `orderHasUnresolvedIssue` **naming the unresolved products** (FR-028)
- [ ] T038 [US2] Write a failing concurrency test asserting two simultaneous pack attempts record exactly one pack (FR-033), following the existing `PickingConcurrencyTests` pattern
- [ ] T039 [P] [US2] Integration tests for `GET /api/orders/{orderId}/packing-slip` — success, `packingSlipUnavailable` as distinct from `orderNotFound` (FR-022), and that a durable access row naming employee and time is written (FR-038)
- [ ] T040 [P] [US2] Integration tests for `GET /api/packing/awaiting`
- [ ] T041 [P] [US2] RTL tests for the packing desk — resolve, refuse a held order by name, state plainly when no slip is stored, and show **every** contributor rather than a truncated list (FR-043)
- [ ] T083 [P] [US2] Write failing integration tests asserting an employee with the `Picker` role can retrieve a packing slip and mark an order packed (FR-037) — the codebase has `RequireManagerAdmin`, and a role check added reflexively to a PII endpoint would silently break packing

### Implementation for User Story 2

- [ ] T042 [US2] Add `PageNumbers` to `backend/src/LootSingles.Application/Import/RawOrderBlock.cs`
- [ ] T043 [US2] Record page numbers through parsing and continuation-page merging in `backend/src/LootSingles.Infrastructure/Import/PdfPigPackingSlipParser.cs`
- [ ] T044 [US2] Create `backend/src/LootSingles.Application/Import/IPackingSlipSlicer.cs` — bytes plus page numbers in, one document out, nothing else (research.md §2)
- [ ] T045 [US2] Implement `backend/src/LootSingles.Infrastructure/Import/PdfPigPackingSlipSlicer.cs` using `PdfMerger`, making T031 and T032 pass
- [ ] T046 [US2] Rewind the upload between parse and slice in `backend/src/LootSingles.Application/Import/PackingSlipImportService.cs`, buffering only when the stream cannot seek (research.md §3)
- [ ] T047 [US2] Slice and store each order's slip in `PackingSlipImportService.cs` as a **subordinate** step that records and swallows its own failure, making T035 pass
- [ ] T048 [US2] Extend `backend/src/LootSingles.Application/Import/IImportPersistence.cs` and its implementation to store a slip alongside its order
- [ ] T049 [P] [US2] Create `backend/src/LootSingles.Application/Packing/PackingCodeResolver.cs`, making T033 pass
- [ ] T050 [US2] Create `backend/src/LootSingles.Application/Packing/IPackingRepository.cs` and `PackingService.cs` — resolve, pack, awaiting, slip retrieval
- [ ] T051 [US2] Implement `backend/src/LootSingles.Infrastructure/Persistence/PackingRepository.cs`, projecting the awaiting list to the fields the desk shows and never loading slip bytes with it
- [ ] T052 [US2] Implement the packed transition as a conditional write in `PackingRepository.cs`, mirroring feature 013's claiming rather than a read-then-write check, making T037 and T038 pass
- [ ] T053 [US2] Create `backend/src/LootSingles.Api/Controllers/PackingController.cs` with the resolve and awaiting endpoints
- [ ] T054 [US2] Add the packed and packing-slip endpoints to `OrdersController.cs`
- [ ] T055 [US2] Write the access record and an `ILogger<T>` line on every slip retrieval — naming order and employee, never slip content — making T039 pass
- [ ] T056 [P] [US2] Create `frontend/src/features/packing/packingApi.ts` with typed errors matching the contract's codes
- [ ] T057 [US2] Create `frontend/src/features/packing/ScanBox.tsx` holding focus so a handheld scanner needs no clicking
- [ ] T058 [US2] Create `frontend/src/features/packing/PackingDeskPage.tsx` — resolve, details, print slip, mark packed, awaiting list
- [ ] T059 [US2] Add the packing route to `frontend/src/App.tsx`
- [ ] T060 [US2] E2E coverage of quickstart.md scenarios 3, 4 and 5 in `frontend/e2e/`

**Checkpoint**: The lifecycle closes. A sleeve can be picked, labelled, scanned, packed.

---

## Phase 5: User Story 3 — A lost or ruined label is replaced (Priority: P2)

**Goal**: Anyone can produce an order's label again without re-picking it.

**Independent test**: Print a label, reprint from the order, reprint from the desk; all three match,
including the original picker and time.

- [ ] T061 [P] [US3] Write a failing test asserting a reprint carries the **original** picker and pick time, not the reprinting employee or moment (FR-016)
- [ ] T062 [US3] Add a reprint action to `frontend/src/features/orders/OrderDetailPage.tsx` for any order whose picking has ended, **including a held one** — the label most likely to be reprinted (FR-016)
- [ ] T063 [US3] Add a reprint action to `frontend/src/features/packing/PackingDeskPage.tsx`
- [ ] T064 [US3] E2E coverage of quickstart.md scenario 6 in `frontend/e2e/`

---

## Phase 6: User Story 4 — The queue shows what is still on the shelf (Priority: P3)

**Goal**: The dashboard counts orders awaiting packing rather than orders ever picked.

**Independent test**: Pick an order and watch the count rise; pack it and watch the count fall.

- [ ] T065 [P] [US4] Write failing tests in `backend/tests/LootSingles.UnitTests/Dashboard/` and `backend/tests/LootSingles.IntegrationTests/Dashboard/` asserting the count excludes packed orders (FR-036)
- [ ] T066 [US4] Change the count's meaning in `backend/src/LootSingles.Application/Dashboard/DashboardService.cs` and `backend/src/LootSingles.Infrastructure/Persistence/DashboardRepository.cs`
- [ ] T067 [US4] Relabel the tile in `frontend/src/features/dashboard/DashboardPage.tsx` from *Picked* to *Awaiting packing*
- [ ] T068 [US4] E2E coverage of quickstart.md scenario 7 in `frontend/e2e/`

---

## Phase 7: Polish and Cross-Cutting Concerns

**Purpose**: Verify the bounds that make this feature's privacy position defensible, then the
ordinary gates.

### The PRD §27 bounds — these are the mitigation, not a checklist

- [ ] T069 Verify one order per file: assert a stored slip names exactly one customer (quickstart.md privacy check 1, FR-019)
- [ ] T070 Verify the batch is not retained: assert no stored artifact holds the whole batch document after an import (privacy check 2, FR-020)
- [ ] T071 Verify picking surfaces cannot reach a slip: assert no payload consumed by a picking screen carries slip content or customer fields, and no picking screen links to one (privacy check 3, FR-039)
- [ ] T072 Verify access is attributable: retrieve a slip and assert a durable record names the employee and time (privacy check 4, SC-010)
- [ ] T073 Verify logs are clean: assert no log line carries customer name, address or slip content, per the constitution's logging rule (privacy check 5)

> If any of T069–T073 cannot be made to pass, **stop**. PRD §27's four bounds hold together or not
> at all, and weakening one re-opens amendment A14 with the Product Owner — it is not an
> implementation decision (plan.md, Note on Principle VII).

### Ordinary gates

- [ ] T074 [P] Review whether the new behaviour warrants production logging beyond slip access and slip-extraction failure, per the constitution's Observability standard — adding none where none is warranted
- [ ] T075 [P] Run `npm --prefix frontend run build` and `npm --prefix frontend run lint`; `tsc --noEmit` checks nothing in this project
- [ ] T076 [P] Run `dotnet build backend/LootSingles.sln` and `dotnet test backend/LootSingles.sln`
- [ ] T077 [P] Run `npm --prefix frontend run format:check` and the C# formatting check
- [ ] T078 Walk quickstart.md end to end against the running application and correct any step that does not match what was built — **scenario 0 depends on T001 and stays outstanding until the printer is available**
- [ ] T079 Run `/branch-review` and resolve every Required finding before `/speckit-converge`, per CLAUDE.md's Branch Review Gate

> **T001 is still open at this point.** The feature is not done while it is, and `/speckit-converge`
> should not be treated as closing it out. Everything else can be finished, reviewed and merged;
> the printed label remains unverified against real hardware until someone prints one.

---

## Dependencies & Execution Order

### Phase dependencies

```text
Phase 1 (Setup + hardware gate)
   │  T001 gates every label task: T023, T024, T025, T026
   ▼
Phase 2 (Foundational)  ── T004/T005 gate all slip work; T006–T011 gate US2 and US4
   ▼
Phase 3 (US1, P1) 🎯 MVP ──┐
Phase 4 (US2, P1) ─────────┤  US1 and US2 are independent of each other
   ▼                       │
Phase 5 (US3, P2) ─────────┘  needs US1's label endpoint and US2's desk page
   ▼
Phase 6 (US4, P3)             needs US2's packed state to count against
   ▼
Phase 7 (Polish)
```

### User story dependencies

- **US1** depends only on Phase 1 and the frontend dependencies. It does **not** need Packed, slip
  storage, or the desk.
- **US2** depends on Phase 2's schema and status short-circuit.
- **US3** depends on US1 (the label endpoint) and US2 (the desk page).
- **US4** depends on US2, because there is nothing to exclude from the count until orders can be
  packed.

### Within each story

Tests first, always. Then domain → application → API → frontend → E2E.

### Parallel opportunities

- **Phase 2**: T006–T009 are four separate files and run together; T010 and T011 must follow them.
- **Phase 3**: T015–T019 are five independent test files. T022 runs alongside the backend work.
- **Phase 4**: T031–T033, T036–T037, T039–T041 are independent test files. T049 and T056 are
  independent of the import-pipeline work.
- **Phase 7**: T074–T077 all run together.
- **US1 and US2 can be built in parallel by two people** once Phase 2 is done — they share no files
  except `tasks.md` itself.

---

## Implementation Strategy

### MVP scope

**Phases 1–3 (through US1).** A pick ends on a screen and the sleeve carries a label. The sleeve is
identifiable by a human reading it, which is strictly better than today, and it is demonstrable
without the packing desk existing.

### Incremental delivery

1. **Phase 1** — the printer answers yes or no. Everything downstream assumes yes.
2. **Phases 2–3** — MVP: labelled sleeves.
3. **Phase 4** — the lifecycle closes; orders can be packed.
4. **Phases 5–6** — recovery from a lost label, and a count that means something.
5. **Phase 7** — the privacy bounds verified, then the gates.

### Notes

- **T001 first, genuinely.** It is the only task here that code quality cannot influence.
- **T035 is the regression to fear.** It guards an existing, working, safety-critical pipeline
  against a new subordinate step. A slip failure must never reject an order whose own data parsed
  fine.
- **T012 guards a silent failure.** A packed order recomputed back to `Picked` throws no error — it
  just reappears on the shelf.
- Commit after each completed task or coherent group; the Product Owner confirms each commit.
