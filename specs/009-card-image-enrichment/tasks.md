# Tasks: Card Image Enrichment for Order Picking Detail

**Input**: Design documents from `/specs/009-card-image-enrichment/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/order-detail-api.md, quickstart.md

**Tests**: Required by constitution Principle IV (TDD is non-negotiable for new/modified application behavior). For every behavior-changing task below, write and run the test first, confirm the expected failure, then implement only enough to pass.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file (or is a logically independent addition to a shared new file) and has no unmet dependency
- **[Story]**: Traceability to US1, US2, US3, or US4

---

## Phase 1: Foundational — Provider Abstraction, Response Wiring, and Frontend Rendering

**Purpose**: Build the dispatch mechanism (`ICardCatalogProvider`, `CardImageEnrichmentService`), extend
`OrderDetail`/`OrderLineDetail`/`OrdersController`'s response with `ImageUrl`, and update the frontend
to render a real image vs. the existing placeholder — with **zero concrete providers registered yet**.
Every user story below adds one real provider on top of this; none of them touch this plumbing again.

### Tests

- [ ] T001 [P] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/CardImageEnrichmentServiceTests.cs`: given a `ProductLine` with no registered provider, `TryGetImageUrlAsync` returns `null` without invoking any provider; given a registered provider whose `ProductLine` matches (case-insensitively), delegates to it and returns its result; given a registered provider that throws, returns `null` rather than propagating (each case using a small inline fake `ICardCatalogProvider`)
- [ ] T002 Run T001 and confirm it fails for the expected reason (`CardImageEnrichmentService`/`ICardCatalogProvider`/`CardIdentity` don't exist yet)

### Implementation — dispatch mechanism

- [ ] T003 [P] Add `CardIdentity` record to `backend/src/LootSingles.Application/CardCatalog/CardIdentity.cs` per data-model.md: `ProductName`, `Set`, `CollectorNumber`, `Variant`
- [ ] T004 [P] Add `ICardCatalogProvider` interface to `backend/src/LootSingles.Application/CardCatalog/ICardCatalogProvider.cs` per data-model.md: `string ProductLine { get; }`, `Task<string?> TryMatchImageUrlAsync(CardIdentity identity, CancellationToken cancellationToken)`
- [ ] T005 Implement `CardImageEnrichmentService` in `backend/src/LootSingles.Application/CardCatalog/CardImageEnrichmentService.cs`: constructor-injects `IEnumerable<ICardCatalogProvider>` and `ILogger<CardImageEnrichmentService>`; `TryGetImageUrlAsync(string productLine, CardIdentity identity, CancellationToken)` finds the provider whose `ProductLine` matches case-insensitively and delegates, catching any exception from the provider call, logging one `LogWarning` (no PII, per research.md §10) and returning `null`; returns `null` immediately with no provider call when no provider matches, to make T001 pass
- [ ] T006 Run T001, confirm it passes, and run the full backend unit test suite to confirm no regression

### Tests — response wiring

- [ ] T007 [P] Extend `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: register a fake `ICardCatalogProvider` for `"Pokemon"` returning a known image URL via `WebApplicationFactory.ConfigureWebHost`/`ConfigureServices` (mirroring `ImportsControllerFailureTests`' override pattern), seed a Pokémon line, and assert the response's `imageUrl` matches; seed a second line for a game with no registered provider and assert its `imageUrl` is explicitly `null`
- [ ] T008 Run T007 and confirm it fails for the expected reason (no `imageUrl` in the response yet; `OrdersService` doesn't call any enrichment yet)

### Implementation — response wiring

- [ ] T009 Add `ImageUrl` (`string?`) to `OrderLineDetail` in `backend/src/LootSingles.Application/Orders/OrderDetail.cs` per data-model.md
- [ ] T010 Update `OrdersService.GetByIdAsync` in `backend/src/LootSingles.Application/Orders/OrdersService.cs` to, after loading `OrderDetail` from the repository, enrich each line's `ImageUrl` concurrently (`Task.WhenAll`) via `CardImageEnrichmentService.TryGetImageUrlAsync`, building a `CardIdentity` from each line's existing fields, per research.md §7 — `GetAllAsync` (list views) is untouched
- [ ] T011 Add `ImageUrl` (`string?`) to `OrderLineDetailResponse` and wire the `GetById` mapping in `backend/src/LootSingles.Api/Controllers/OrdersController.cs`, matching contracts/order-detail-api.md, to make T007 pass
- [ ] T012 Register `CardImageEnrichmentService` in `backend/src/LootSingles.Api/Program.cs` — no `ICardCatalogProvider` implementations exist yet; each user story below registers its own
- [ ] T013 Run T007, confirm it passes, and run the full backend test suite to confirm no regression

### Tests — frontend rendering

- [ ] T014 [P] Add a failing test case to `frontend/tests/orders/OrderDetailPage.test.tsx`: a line with `imageUrl` set renders an `<img>` with that `src` (and `alt` identifying the card) in place of the neutral placeholder; a line with `imageUrl: null` continues to render the existing placeholder exactly as before
- [ ] T015 Run T014 and confirm it fails for the expected reason (`OrderDetailPage` doesn't render an `<img>` yet)

### Implementation — frontend rendering

- [ ] T016 [P] Extend `OrderLineDetail` in `frontend/src/features/orders/ordersApi.ts`: add `imageUrl: string | null`
- [ ] T017 Update `frontend/src/features/orders/OrderDetailPage.tsx`: render `<img src={line.imageUrl} alt={line.productName}>` in place of the existing placeholder when `line.imageUrl` is not `null`, leaving the placeholder's own markup/behavior unchanged for every other case, to make T014 pass
- [ ] T018 Adjust `frontend/src/features/orders/OrderDetailPage.css` as needed so a real card image renders at the same size/position the placeholder already occupies, remaining legible at mobile width
- [ ] T019 Run T014, confirm it passes, and run the full frontend test suite to confirm no regression

**Checkpoint**: The enrichment pipeline and UI rendering are fully wired end-to-end, but with zero
real providers registered — every line still falls back to the placeholder today, exactly as
before this feature. Each user story below adds one real provider on top of this, unchanged.

---

## Phase 2: User Story 1 - See a Pokémon Card's Real Image When Confidently Matched (Priority: P1) 🎯 MVP

**Goal**: A Pokémon product line whose set and collector number exactly identify a known card shows that card's real image.

**Independent Test**: Open the detail view for an order containing a Pokémon line with an exact set+collector-number identity and confirm the real image is displayed, with all textual fields still visible.

### Tests for User Story 1

- [ ] T020 [P] [US1] Add a stub `HttpMessageHandler` test helper to `backend/tests/LootSingles.UnitTests/CardCatalog/StubHttpMessageHandler.cs`: a small hand-rolled `HttpMessageHandler` returning a canned `HttpResponseMessage` (or throwing, for the unavailable case) per test, per research.md §9 — no mocking library
- [ ] T021 [P] [US1] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/PokemonTcgApiCardCatalogProviderTests.cs` using `StubHttpMessageHandler`: an exact set+collector-number match with a verified name returns the card's image URL; a response with no matching card returns `null`; a response with multiple candidate cards returns `null` (FR-003); a response whose card name doesn't match the imported product name returns `null`
- [ ] T022 [US1] Run T020–T021 and confirm they fail for the expected reason (`PokemonTcgApiCardCatalogProvider` doesn't exist yet)

### Implementation for User Story 1

- [ ] T023 [US1] Implement `PokemonTcgApiCardCatalogProvider` in `backend/src/LootSingles.Infrastructure/CardCatalog/PokemonTcgApiCardCatalogProvider.cs`: `ProductLine = "Pokemon"`; first confirm against the Pokémon TCG API's live documentation how set name is queried or resolved (research.md §4's open item — use a set-name query parameter if directly queryable, or a set-lookup step otherwise); query by collector number and resolved set; verify the returned card's name; return the image URL only on a single confident match, to make T021 pass
- [ ] T024 [US1] Register an `HttpClient` for `PokemonTcgApiCardCatalogProvider` via `AddHttpClient<PokemonTcgApiCardCatalogProvider>()` with a short timeout in `backend/src/LootSingles.Api/Program.cs`, register it as `ICardCatalogProvider`, and bind an optional API key from configuration (`CardCatalog:PokemonTcgApiKey`, User Secrets locally) attached as a request header when present, per research.md §8
- [ ] T025 [US1] Run T020–T022, confirm they pass, and run the full backend test suite to confirm no regression

### E2E for User Story 1

- [ ] T026 [P] [US1] Register a deterministic fake `ICardCatalogProvider` for `"Pokemon"` in `backend/tests/LootSingles.E2EHost/Program.cs` (returning a fixed test image URL for the seeded E2E order's Pikachu line), replacing the real `PokemonTcgApiCardCatalogProvider` registration for this host only, so Playwright never makes a live external call
- [ ] T027 [US1] Add a failing Playwright assertion to `frontend/e2e/order-detail.spec.ts`: the Pikachu line renders an `<img>` with the fake provider's known `src`, in place of the neutral placeholder
- [ ] T028 [US1] Run T027, confirm it fails for the expected reason then passes once T026 is in place, and run the full Playwright suite to confirm no regression

**Checkpoint**: A picker opens a Pokémon order and sees the real card image for a confidently
matched line — User Story 1 (MVP) is independently complete and testable.

---

## Phase 3: User Story 2 - Never See a Wrong or Guessed Card Image (Priority: P1)

**Goal**: No match, an ambiguous match, an unreachable provider, or an unsupported game always falls back to the existing neutral placeholder — never a guessed image, and never a picker-visible error.

**Independent Test**: Simulate a provider failure and confirm the order detail view still loads successfully with the placeholder shown for that line and no error.

### Tests for User Story 2

- [ ] T029 [P] [US2] Add a failing unit test to `backend/tests/LootSingles.UnitTests/CardCatalog/CardImageEnrichmentServiceTests.cs` asserting an unavailable/throwing provider returns `null` without propagating (this is expected to pass immediately from T005's implementation — this test exists to give the hard safety rule its own independent verification, not to drive additional production code, mirroring feature 007 US3's precedent)
- [ ] T030 [P] [US2] Add a failing integration test to `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: an order line whose registered provider throws still yields `200 OK` for the whole order, with that line's `imageUrl` explicitly `null` and no error surfaced
- [ ] T031 [US2] Run T029–T030 and confirm they pass immediately (Foundational's `CardImageEnrichmentService` already implements this fallback per T005); if either fails, fix `CardImageEnrichmentService` to satisfy it before proceeding

### E2E for User Story 2

- [ ] T032 [P] [US2] Add a failing Playwright test to `frontend/e2e/order-detail.spec.ts`: with the E2EHost's fake Pokémon provider configured to simulate an unavailable/throwing provider for one line, confirm the order detail view still loads successfully, that line shows the existing neutral placeholder, and no error state is shown to the picker
- [ ] T033 [US2] Extend `backend/tests/LootSingles.E2EHost/Program.cs`'s fake Pokémon provider (from T026) to support simulating a failure for a specific line (e.g. via a distinguishing collector-number value), to make T032 pass; run the full Playwright suite to confirm no regression

**Checkpoint**: The hard "no image is better than the wrong image" rule is independently verified
for the unavailable-provider and unsupported-game paths — User Story 2 is complete.

---

## Phase 4: User Story 3 - See a Magic: The Gathering Card's Real Image When Confidently Matched (Priority: P2)

**Goal**: The same confident-match image behavior proven for Pokémon also works for Magic product lines, via Scryfall.

**Independent Test**: Open the detail view for an order containing a Magic line with an exact set+collector-number identity and confirm the real image is displayed.

### Tests for User Story 3

- [ ] T034 [P] [US3] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/ScryfallCardCatalogProviderTests.cs` using `StubHttpMessageHandler`: an exact set-code+collector-number match with a verified name returns the card's image URL; no-match, ambiguous-match, and name-mismatch responses each return `null`
- [ ] T035 [US3] Run T034 and confirm it fails for the expected reason (`ScryfallCardCatalogProvider` doesn't exist yet)

### Implementation for User Story 3

- [ ] T036 [US3] Implement `ScryfallCardCatalogProvider` in `backend/src/LootSingles.Infrastructure/CardCatalog/ScryfallCardCatalogProvider.cs`: `ProductLine = "Magic"`; first confirm against Scryfall's live API documentation how to resolve the imported set name to Scryfall's short set code (research.md §3's open item); query the exact set-code + collector-number endpoint; verify the returned card's name; return the image URL only on a single confident match, to make T034 pass
- [ ] T037 [US3] Register an `HttpClient` for `ScryfallCardCatalogProvider` via `AddHttpClient<ScryfallCardCatalogProvider>()` with a short timeout and a descriptive `User-Agent` header (per Scryfall's usage terms, research.md §8) in `backend/src/LootSingles.Api/Program.cs`, and register it as `ICardCatalogProvider`
- [ ] T038 [US3] Run T034–T035, confirm they pass, and run the full backend test suite to confirm no regression

### E2E for User Story 3

- [ ] T039 [P] [US3] Extend `backend/tests/LootSingles.E2EHost/Program.cs` to seed a Magic product line on the E2E test order and register a deterministic fake `ICardCatalogProvider` for `"Magic"`, reusing the pattern T026 established
- [ ] T040 [US3] Add a failing Playwright assertion to `frontend/e2e/order-detail.spec.ts`: the Magic line renders an `<img>` with the fake provider's known `src`; confirm it fails then passes, and run the full Playwright suite to confirm no regression

**Checkpoint**: Magic orders get the same confident-match image behavior already proven for
Pokémon — User Story 3 is independently complete and testable.

---

## Phase 5: User Story 4 - See a Disney Lorcana Card's Real Image When Confidently Matched (Priority: P3)

**Goal**: The same confident-match image behavior also works for Lorcana product lines, via Lorcast.

**Independent Test**: Open the detail view for an order containing a Lorcana line with an exact set+collector-number identity and confirm the real image is displayed.

### Tests for User Story 4

- [ ] T041 [P] [US4] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/LorcastCardCatalogProviderTests.cs` using `StubHttpMessageHandler`, following the same exact-match/no-match/ambiguous/name-mismatch coverage as T021/T034, against the shape confirmed in T042
- [ ] T042 [US4] Confirm Lorcast's live API documentation for its exact set+collector-number lookup endpoint and response schema (research.md §5's explicitly unconfirmed item); update research.md §5 with the confirmed shape before implementing
- [ ] T043 [US4] Run T041 and confirm it fails for the expected reason (`LorcastCardCatalogProvider` doesn't exist yet)

### Implementation for User Story 4

- [ ] T044 [US4] Implement `LorcastCardCatalogProvider` in `backend/src/LootSingles.Infrastructure/CardCatalog/LorcastCardCatalogProvider.cs` per the shape confirmed in T042: `ProductLine = "Lorcana TCG"` (confirmed by feature 010's real sample — research.md §5 update); query by set + collector number, accounting for promo-style collector numbers with no `/<total>` suffix (e.g. `"#36"`); verify the returned card's name; return the image URL only on a single confident match, to make T041 pass
- [ ] T045 [US4] Register an `HttpClient` for `LorcastCardCatalogProvider` via `AddHttpClient<LorcastCardCatalogProvider>()` with a short timeout in `backend/src/LootSingles.Api/Program.cs`, and register it as `ICardCatalogProvider`
- [ ] T046 [US4] Run T041, T043, confirm they pass, and run the full backend test suite to confirm no regression

### E2E for User Story 4

- [ ] T047 [P] [US4] Extend `backend/tests/LootSingles.E2EHost/Program.cs` to seed a Lorcana product line (`ProductLine = "Lorcana TCG"`, matching real data per research.md §5) on the E2E test order and register a deterministic fake `ICardCatalogProvider` for `"Lorcana TCG"`, reusing the T026/T039 pattern
- [ ] T048 [US4] Add a failing Playwright assertion to `frontend/e2e/order-detail.spec.ts`: the Lorcana line renders an `<img>` with the fake provider's known `src`; confirm it fails then passes, and run the full Playwright suite to confirm no regression

**Checkpoint**: All three phase-1 games are independently complete and testable. One Piece
continues to show the placeholder (FR-010), ready for a future feature to add its adapter once a
provider is selected.

---

## Phase 6: Polish & Cross-Cutting Validation

**Purpose**: Confirm full regression across both stacks and complete manual validation — including, for the first time in this project, a real end-to-end check against live external providers.

- [ ] T049 [P] Run backend build, full unit suite, full SQL Server integration suite, and CSharpier; confirm all existing tests remain green and record results in `specs/009-card-image-enrichment/validation-results.md`
- [ ] T050 [P] Run frontend lint, unit test suite, production build, Prettier check, and the full Playwright suite; confirm all existing tests remain green and record results in `specs/009-card-image-enrichment/validation-results.md`
- [ ] T051 Perform the quickstart.md manual validation (run the app locally with real outbound internet access, exercise every scenario including at least one real confident match per game against the live providers and a simulated provider-unavailable case) and record observations in `specs/009-card-image-enrichment/validation-results.md`
- [ ] T052 Perform the constitution Architecture and Changeability Review against the implemented boundaries, capture any Must Fix findings as new tasks in `specs/009-card-image-enrichment/tasks.md`, and do not proceed to convergence until resolved

---

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 (Foundational) has no dependency and blocks every user story (they all render on top of its dispatch mechanism and response/UI wiring).
- US1 (Phase 2) depends only on Phase 1.
- US2 (Phase 3) depends only on Phase 1 — it verifies behavior Phase 1 already implements, and reuses US1's E2EHost fake-provider mechanism (T026) for its own E2E case, so in practice runs after US1.
- US3 (Phase 4) depends only on Phase 1, and reuses the E2EHost fake-provider pattern US1 established (T026) for its own E2E task (T039).
- US4 (Phase 5) depends only on Phase 1, similarly reusing the established E2EHost pattern; sequenced last since it also carries the Lorcast API-shape confirmation task (T042).
- Phase 6 (Polish) depends on all four user stories being complete.

### Red-Green ordering

- T001–T002 → T003–T005 → T006
- T007–T008 → T009–T012 → T013
- T014–T015 → T016–T018 → T019
- T020–T022 → T023–T024 → T025
- T026 → T027 → T028
- T029–T030 → T031
- T032 → T033
- T034–T035 → T036–T037 → T038
- T039 → T040
- T041, T042 → T043 → T044–T045 → T046
- T047 → T048

### Parallel opportunities

- T001 and T003/T004 are independent additions (test file vs. new type files) and can be authored in parallel.
- T007 and T009 are independent (integration test vs. Application record) and can be authored in parallel, though T007 won't pass until T009–T011 land.
- T014 and T016 are independent (frontend test vs. type) and can be authored in parallel.
- T020 and T021 can be authored together once the stub handler's shape is agreed.
- T026, T039, and T047 (E2EHost fake-provider registrations for each game) touch the same file sequentially in practice, but are logically independent additions.
- T049 and T050 are independent (different stacks) and can run in parallel.

## Implementation Strategy

### MVP first

1. Complete Phase 1 (dispatch mechanism + response/UI wiring, zero real providers).
2. Complete Phase 2 (User Story 1) — Pokémon lines show real images.
3. Stop and validate: a confident Pokémon match shows the correct image; everything else still shows the placeholder.

### Incremental delivery

1. Phase 1 makes the whole pipeline safe-by-default (every line falls back to the placeholder) before any real provider exists.
2. US1 proves the pipeline end-to-end with one real, well-documented provider (Pokémon TCG API).
3. US2 proves the safety rule explicitly, independent of any single provider's correctness.
4. US3 and US4 extend the already-proven pattern to Magic (Scryfall) and Lorcana (Lorcast) — each is one adapter, reusing everything else unchanged, directly demonstrating FR-007's extensibility requirement.
5. Polish proves zero regressions and completes manual validation, including a first real check against live external providers.

## Notes

- No database schema or migration changes are needed anywhere in this feature (data-model.md) — every task builds a transient, non-persisted enrichment result.
- No new backend NuGet package is introduced (research.md §6, §9) — `IHttpClientFactory` and `System.Text.Json` are already part of the ASP.NET Core shared framework this project targets; no mocking library is added.
- This feature adds new production logging for the first time since feature 006/007/008's precedent of adding none — a single `LogWarning` per provider failure (research.md §10), proportional and PII-free.
- Assert against the JSON response shape and rendered DOM content precisely as specified in contracts/order-detail-api.md and data-model.md — not just that a value appears somewhere on the page.
- T023, T036, and T044 each carry a genuine, explicitly-flagged research uncertainty (research.md §3–5) — confirm the real provider API shape against live documentation as part of that task rather than guessing; a wrong guess here fails safe (returns no image) rather than fails dangerous, but still wastes implementation time if not verified first.
