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

- [X] T001 [P] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/CardImageEnrichmentServiceTests.cs`: given a `ProductLine` with no registered provider, `TryGetImageUrlAsync` returns `null` without invoking any provider; given a registered provider whose `ProductLine` matches (case-insensitively), delegates to it and returns its result; given a registered provider that throws, returns `null` rather than propagating (each case using a small inline fake `ICardCatalogProvider`)
- [X] T002 Run T001 and confirm it fails for the expected reason (`CardImageEnrichmentService`/`ICardCatalogProvider`/`CardIdentity` don't exist yet)

### Implementation — dispatch mechanism

- [X] T003 [P] Add `CardIdentity` record to `backend/src/LootSingles.Application/CardCatalog/CardIdentity.cs` per data-model.md: `ProductName`, `Set`, `CollectorNumber`, `Variant`
- [X] T004 [P] Add `ICardCatalogProvider` interface to `backend/src/LootSingles.Application/CardCatalog/ICardCatalogProvider.cs` per data-model.md: `string ProductLine { get; }`, `Task<string?> TryMatchImageUrlAsync(CardIdentity identity, CancellationToken cancellationToken)`
- [X] T005 Implement `CardImageEnrichmentService` in `backend/src/LootSingles.Application/CardCatalog/CardImageEnrichmentService.cs`: constructor-injects `IEnumerable<ICardCatalogProvider>` and `ILogger<CardImageEnrichmentService>`; `TryGetImageUrlAsync(string productLine, CardIdentity identity, CancellationToken)` finds the provider whose `ProductLine` matches case-insensitively and delegates, catching any exception from the provider call, logging one `LogWarning` (no PII, per research.md §10) and returning `null`; returns `null` immediately with no provider call when no provider matches, to make T001 pass
- [X] T006 Run T001, confirm it passes, and run the full backend unit test suite to confirm no regression

### Tests — response wiring

- [X] T007 [P] Extend `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: register a fake `ICardCatalogProvider` for `"Pokemon"` returning a known image URL via `WebApplicationFactory.ConfigureWebHost`/`ConfigureServices` (mirroring `ImportsControllerFailureTests`' override pattern), seed a Pokémon line, and assert the response's `imageUrl` matches; seed a second line for a game with no registered provider and assert its `imageUrl` is explicitly `null`
- [X] T008 Run T007 and confirm it fails for the expected reason (no `imageUrl` in the response yet; `OrdersService` doesn't call any enrichment yet)

### Implementation — response wiring

- [X] T009 Add `ImageUrl` (`string?`) to `OrderLineDetail` in `backend/src/LootSingles.Application/Orders/OrderDetail.cs` per data-model.md
- [X] T010 Update `OrdersService.GetByIdAsync` in `backend/src/LootSingles.Application/Orders/OrdersService.cs` to, after loading `OrderDetail` from the repository, enrich each line's `ImageUrl` concurrently (`Task.WhenAll`) via `CardImageEnrichmentService.TryGetImageUrlAsync`, building a `CardIdentity` from each line's existing fields, per research.md §7 — `GetAllAsync` (list views) is untouched
- [X] T011 Add `ImageUrl` (`string?`) to `OrderLineDetailResponse` and wire the `GetById` mapping in `backend/src/LootSingles.Api/Controllers/OrdersController.cs`, matching contracts/order-detail-api.md, to make T007 pass
- [X] T012 Register `CardImageEnrichmentService` in `backend/src/LootSingles.Api/Program.cs` — no `ICardCatalogProvider` implementations exist yet; each user story below registers its own
- [X] T013 Run T007, confirm it passes, and run the full backend test suite to confirm no regression

### Tests — frontend rendering

- [X] T014 [P] Add a failing test case to `frontend/tests/orders/OrderDetailPage.test.tsx`: a line with `imageUrl` set renders an `<img>` with that `src` (and `alt` identifying the card) in place of the neutral placeholder; a line with `imageUrl: null` continues to render the existing placeholder exactly as before
- [X] T015 Run T014 and confirm it fails for the expected reason (`OrderDetailPage` doesn't render an `<img>` yet)

### Implementation — frontend rendering

- [X] T016 [P] Extend `OrderLineDetail` in `frontend/src/features/orders/ordersApi.ts`: add `imageUrl: string | null`
- [X] T017 Update `frontend/src/features/orders/OrderDetailPage.tsx`: render `<img src={line.imageUrl} alt={line.productName}>` in place of the existing placeholder when `line.imageUrl` is not `null`, leaving the placeholder's own markup/behavior unchanged for every other case, to make T014 pass
- [X] T018 Adjust `frontend/src/features/orders/OrderDetailPage.css` as needed so a real card image renders at the same size/position the placeholder already occupies, remaining legible at mobile width
- [X] T019 Run T014, confirm it passes, and run the full frontend test suite to confirm no regression

### Batch-Shaped Provider Contract (added pre-User-Story-3, per Clarifications 2026-08-25 — Scryfall rate-limit research; design confirmed, see plan.md/research.md)

**Purpose**: Give `ICardCatalogProvider` an additive batch operation (a list of identities in, a
dictionary of results out) alongside its existing single-card one, via a C# default interface
method that fans out to the single-card operation unless a provider overrides it — so
`OrdersService` can call each provider once per game instead of once per line, required for User
Story 3 to use Scryfall's `POST /cards/collection` batch endpoint safely. This is additive, not a
breaking signature change: `TcgdexCardCatalogProvider` and every existing fake
`ICardCatalogProvider` in the test suite implement only the single-card operation today and need
**no changes** — they inherit correct batch behavior automatically.

- [X] T028y Add a failing unit test to a new `backend/tests/LootSingles.UnitTests/CardCatalog/CardCatalogProviderDefaultBatchTests.cs`
  proving the interface's default `TryMatchImageUrlsAsync` body is correct against a minimal fake
  that implements only `TryMatchImageUrlAsync`: a batch of multiple distinct identities resolves
  each independently (fanned out, not serialized — assert via a fake that records concurrent
  in-flight calls); duplicate `CardIdentity` values within one batch don't throw and both resolve
  to the same result. Add `TryMatchImageUrlsAsync` to `ICardCatalogProvider` (Application) as a
  default interface method per research.md §3's design to make it pass — no other provider file
  changes
- [X] T028z Add failing unit tests to `CardImageEnrichmentServiceTests.cs` for the new
  `TryGetImageUrlsAsync(productLine, IReadOnlyList<CardIdentity>, CancellationToken) -> IReadOnlyDictionary<CardIdentity, string?>`
  shape (replacing the old single-identity `TryGetImageUrlAsync`): no registered provider for the
  game returns every identity mapped to `null` without invoking any provider; a registered provider
  delegates and its returned dictionary passes through (using the existing hand-rolled fakes
  unchanged — they still implement only the single-card method); a provider that throws returns
  every identity mapped to `null` rather than propagating. Implement
  `CardImageEnrichmentService.TryGetImageUrlsAsync` to make them pass
- [X] T028aa Add a failing unit test to `OrdersServiceTests.cs` proving `GetByIdAsync` groups lines
  by `ProductLine`, calls the enrichment service once per distinct game present (not once per
  line), and — critically — preserves each line's original position/order in the response
  regardless of grouping. Update `OrdersService.GetByIdAsync` to group `order.Lines` by
  `ProductLine`, call `CardImageEnrichmentService.TryGetImageUrlsAsync` once per group via
  `Task.WhenAll` across groups, and map each group's dictionary result back to its lines by
  original index (not by `CardIdentity`, so duplicate identities across different lines each
  resolve independently and safely) to make it pass
- [X] T028ab Run the full backend test suite (unit + integration), the full frontend suite
  (vitest + Playwright), and CSharpier/Prettier/oxlint to confirm zero behavior change for
  Pokémon — `TcgdexCardCatalogProviderTests.cs`, `OrdersControllerTests.cs`'s fake providers, and
  `backend/tests/LootSingles.E2EHost/Program.cs`'s fake provider needed no changes and required no
  code changes to build or pass, exactly as designed. **Caught a real regression via the existing
  Playwright suite**: the default interface method's `Task.WhenAll` faulted the *entire* batch
  when any one identity's `TryMatchImageUrlAsync` threw, so E2E's intentionally-failing line was
  wiping out its sibling Pikachu line's already-successful image too — a real loss of the "one
  line's failure can't block others" guarantee from Phase 3 (US2). Fixed by wrapping each
  identity's call in its own try/catch inside the default method (mapping only that identity to
  `null`), added a new regression test
  (`TryMatchImageUrlsAsync_DefaultImplementation_OneIdentityThrows_SiblingIdentityStillResolves`)
  proving per-identity isolation. Full suite re-run clean after the fix: 122/122 unit, 107/107
  integration, 45/45 frontend unit, 10/10 Playwright, CSharpier/Prettier/oxlint all clean

**Checkpoint**: The enrichment pipeline and UI rendering are fully wired end-to-end, but with zero
real providers registered — every line still falls back to the placeholder today, exactly as
before this feature. Each user story below adds one real provider on top of this, unchanged.

---

## Phase 2: User Story 1 - See a Pokémon Card's Real Image When Confidently Matched (Priority: P1) 🎯 MVP

**Goal**: A Pokémon product line whose set and collector number exactly identify a known card shows that card's real image.

**Independent Test**: Open the detail view for an order containing a Pokémon line with an exact set+collector-number identity and confirm the real image is displayed, with all textual fields still visible.

### Tests for User Story 1

- [X] T020 [P] [US1] Add a stub `HttpMessageHandler` test helper to `backend/tests/LootSingles.UnitTests/CardCatalog/StubHttpMessageHandler.cs`: a small hand-rolled `HttpMessageHandler` returning a canned `HttpResponseMessage` (or throwing, for the unavailable case) per test, per research.md §9 — no mocking library
- [X] T021 [P] [US1] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/PokemonTcgApiCardCatalogProviderTests.cs` using `StubHttpMessageHandler`: an exact set+collector-number match with a verified name returns the card's image URL; a response with no matching card returns `null`; a response with multiple candidate cards returns `null` (FR-003); a response whose card name doesn't match the imported product name returns `null`
- [X] T022 [US1] Run T020–T021 and confirm they fail for the expected reason (`PokemonTcgApiCardCatalogProvider` doesn't exist yet)

### Implementation for User Story 1

- [X] T023 [US1] Implement `PokemonTcgApiCardCatalogProvider` in `backend/src/LootSingles.Infrastructure/CardCatalog/PokemonTcgApiCardCatalogProvider.cs`: `ProductLine = "Pokemon"`; first confirm against the Pokémon TCG API's live documentation how set name is queried or resolved (research.md §4's open item — use a set-name query parameter if directly queryable, or a set-lookup step otherwise); query by collector number and resolved set; verify the returned card's name; return the image URL only on a single confident match, to make T021 pass
- [X] T024 [US1] Register an `HttpClient` for `PokemonTcgApiCardCatalogProvider` via `AddHttpClient<PokemonTcgApiCardCatalogProvider>()` with a short timeout in `backend/src/LootSingles.Api/Program.cs`, register it as `ICardCatalogProvider`, and bind an optional API key from configuration (`CardCatalog:PokemonTcgApiKey`, User Secrets locally) attached as a request header when present, per research.md §8
- [X] T025 [US1] Run T020–T022, confirm they pass, and run the full backend test suite to confirm no regression

### E2E for User Story 1

- [X] T026 [P] [US1] Register a deterministic fake `ICardCatalogProvider` for `"Pokemon"` in `backend/tests/LootSingles.E2EHost/Program.cs` (returning a fixed test image URL for the seeded E2E order's Pikachu line), replacing the real `PokemonTcgApiCardCatalogProvider` registration for this host only, so Playwright never makes a live external call
- [X] T027 [US1] Add a failing Playwright assertion to `frontend/e2e/order-detail.spec.ts`: the Pikachu line renders an `<img>` with the fake provider's known `src`, in place of the neutral placeholder
- [X] T028 [US1] Run T027, confirm it fails for the expected reason then passes once T026 is in place, and run the full Playwright suite to confirm no regression

### Set-Name Normalization for User Story 1 (added post-manual-testing, per FR-011 / Clarifications 2026-08-25)

- [X] T028a [US1] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/PokemonSeriesPrefixNormalizerTests.cs`: a known series-abbreviation prefix (e.g. `"SV: Black Bolt"`, `"SV10: Destined Rivals"`, `"ME05: Pitch Black"`) is stripped to the remaining set name; text with no recognized prefix (including an unrecognized colon-delimited prefix) is returned unchanged
- [X] T028b [US1] Implement `PokemonSeriesPrefixNormalizer` in `backend/src/LootSingles.Infrastructure/CardCatalog/PokemonSeriesPrefixNormalizer.cs`, loading the Product-Owner-supplied series abbreviation table embedded from `backend/src/LootSingles.Infrastructure/CardCatalog/Data/pokemon-series-abbreviations.json`, to make T028a pass
- [X] T028c [US1] Add a failing unit test to `PokemonTcgApiCardCatalogProviderTests.cs` asserting the provider queries `set.name` using the normalized set text (prefix stripped) rather than the raw packing-slip `Set` value; wire `PokemonSeriesPrefixNormalizer.Normalize` into `PokemonTcgApiCardCatalogProvider.TryMatchImageUrlAsync` to make it pass
- [X] T028d [US1] Fix `NormalizeCollectorNumber` to also strip leading zeros (discovered during manual testing: the Pokémon TCG API stores `number` as `"67"`, not `"067"`), via a failing-first unit test
- [X] T028e [US1] Run the full backend test suite to confirm no regression

### Provider Switch to TCGdex for User Story 1 (added post-manual-testing, per Clarifications 2026-08-25)

- [X] T028f [US1] Diagnose slow/missing images on a real 50-line multi-page order: backend logs showed the Pokémon TCG API returning `500`/`502` for roughly half of all requests; controlled live testing (8 concurrent, 6 sequential one-at-a-time, 6 sequential with a 1s pause between each) confirmed the failure rate was ~50-65% independent of concurrency — a baseline reliability problem with the provider itself, not our request pattern (research.md §4)
- [X] T028g [US1] Evaluate TCGdex as a replacement: confirmed live that the identical 50-concurrent-request load succeeded 50/50 against TCGdex (~250ms typical), confirmed its card-by-set-and-number lookup shape, image URL construction (`{image}/high.webp`), and that its set `name` field also requires the same series-prefix normalization (provider-independent packing-slip quirk)
- [X] T028h [US1] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/TcgdexCardCatalogProviderTests.cs` covering: exact match returns the constructed image URL; no card at that number (404) returns null; name mismatch returns null; a known series-abbreviation-prefixed `Set` resolves correctly; an unknown set name returns null without attempting a card lookup; the sets list is fetched only once across multiple calls on the same provider instance. Extended `StubHttpMessageHandler` with a `RespondingPerRequest` factory to support TCGdex's two-endpoint-shape lookup (sets list, then card-by-id) in tests
- [X] T028i [US1] Implement `TcgdexCardCatalogProvider` in `backend/src/LootSingles.Infrastructure/CardCatalog/TcgdexCardCatalogProvider.cs` per research.md §4 (resolve set name to id via the cached sets list, fetch the card directly by set id + collector number, verify name, construct the image URL), to make T028h pass; remove `PokemonTcgApiCardCatalogProvider.cs` and its test file (superseded, not kept alongside); update `Program.cs`'s DI registration (`https://api.tcgdex.net/v2/en/`, no API key)
- [X] T028j [US1] Run the full backend test suite (unit + integration) and CSharpier to confirm no regression

### TCGdex Matching Corrections for User Story 1 (added post-manual-testing, per Clarifications 2026-08-25)

- [X] T028k [US1] Evaluate every non-matching line in the same real 50-line order directly against TCGdex to find root causes: confirmed two categories are correct, safe behavior (TCGdex has no `image` field at all for some Trainer Gallery cards — Roserade, Spiritomb; `"Miscellaneous Cards & Products"` has no real corresponding set — Charmeleon) and three are genuine defects in this feature's own code (research.md §4) — leading zeros not stripped, an unrecognized-prefix set name, and a trailing printing/variant descriptor blocking name verification
- [X] T028l [US1] Add a failing unit test to `PokemonSeriesPrefixNormalizerTests.cs` for a new `NormalizeCandidates` method: a known abbreviation prefix returns only the abbreviation-stripped candidate; an unrecognized colon-delimited prefix (e.g. `"Celebrations: Classic Collection"`) also offers a colon-to-space candidate (`"Celebrations Classic Collection"`); no colon returns only the raw text. Implement `NormalizeCandidates` in `PokemonSeriesPrefixNormalizer.cs` to make it pass
- [X] T028m [US1] Add failing unit tests to `TcgdexCardCatalogProviderTests.cs`: a zero-padded collector number is queried without the leading zeros; a returned card name differing from the imported product name only by a trailing parenthetical still matches; a set name needing the colon-to-space fallback still resolves. Fix `NormalizeCollectorNumber` to strip leading zeros, wire `NormalizeCandidates` into the set-resolution step (trying each candidate against the sets dictionary until one resolves), and strip a trailing parenthetical from the imported product name for the name-verification comparison only (never altering the displayed, authoritative `ProductName`) in `TcgdexCardCatalogProvider.cs`, to make them pass
- [X] T028n [US1] Run the full backend test suite (unit + integration) and CSharpier to confirm no regression

### Promo Set-Name Matching Correction for User Story 1 (added post-feature-012-merge, per Clarifications 2026-08-25 — design confirmed, implementation deferred pending sign-off)

- [X] T028o [US1] Evaluate every promo-card line in the real 50-line order against TCGdex directly: confirmed 5 lines across 3 eras (Sobble/`ME`, 3× including Charizard ex/`SV`, 1×/`SWSH`) fail to resolve because TCGdex names these sets `"MEP Black Star Promos"`, `"SVP Black Star Promos"`, `"SWSH Black Star Promos"` — a convention today's abbreviation-stripping normalization never produces — and confirmed via TCGdex's full `/v2/en/sets` listing that the `P`-suffix is inconsistent per era (present for SV/ME, absent for DP/HGSS/BW/XY/SM/SWSH), so no single deterministic transform of the abbreviation alone is correct (research.md §4 promo-set-naming addendum)
- [X] T028p [P] [US1] Add failing unit tests to `PokemonSeriesPrefixNormalizerTests.cs` for `NormalizeCandidates`: a recognized-abbreviation set name ending in `" Promo"` or `" Promo Cards"` (e.g. `"ME: Mega Evolution Promo"`, `"SV: Scarlet & Violet Promo Cards"`, `"SWSH: Sword & Shield Promo Cards"`) yields the existing stripped candidate plus `"{abbreviation} Black Star Promos"` and `"{abbreviation}P Black Star Promos"`, in that order, with no duplicates; a recognized-abbreviation set name that does *not* end in that shape (e.g. the existing `"SV: Black Bolt"` case) is unaffected and yields only the stripped candidate, exactly as today
- [X] T028q [US1] Implement the promo-shaped detection in `PokemonSeriesPrefixNormalizer.NormalizeCandidates` per research.md §4's decision: reuse the abbreviation already recognized by the existing scan (refactor the shared abbreviation-match step so both `Normalize` and `NormalizeCandidates` use it, rather than duplicating the match loop), and append the two additional `"Black Star Promos"` candidates only when triggered, to make T028p pass
- [X] T028r [US1] Add a failing unit test to `TcgdexCardCatalogProviderTests.cs` proving the provider resolves a card whose sets dictionary only contains the `"...P Black Star Promos"` form (not the naively-stripped form) by falling through `NormalizeCandidates`'s ordered list, then run the full backend test suite (unit + integration) and CSharpier to confirm no regression; manually re-verify against the live real order that Sobble, the Scarlet & Violet promo lines (including Charizard ex), and the Sword & Shield promo line now resolve real images — confirmed live: Pecharunt and Terapagos ex (both `"SV: Scarlet & Violet Promo Cards"`) now show real images; Sobble and Charizard ex still correctly show the placeholder because TCGdex itself has no `image` field for those two specific cards (same safe-fallback category as Roserade, research.md §4) — the naming fix itself is confirmed working end-to-end

### Sets-List Cache Lifetime for User Story 1 (added post-manual-testing, per Clarifications 2026-08-25 — TCGdex API-citizenship audit)

- [X] T028s [US1] Audit the actual TCGdex call pattern against TCGdex's own "cache bulk data locally" guidance: confirmed the sets-list cache (§4) lives on the Scoped `TcgdexCardCatalogProvider`, recreated per request, so the full 218-entry/~35KB sets list is refetched on every order-detail page load despite changing only a handful of times a year; confirmed no frontend polling (one fetch per page mount) and confirmed TCGdex's `/sets` response sends `Cache-Control: no-cache, no-store, must-revalidate`, a deliberate signal to build an application-level cache rather than rely on HTTP caching (research.md §12)
- [X] T028t [P] [US1] Add failing unit tests to a new `backend/tests/LootSingles.UnitTests/CardCatalog/TcgdexSetCatalogTests.cs`: two calls within the 24-hour cache duration fetch the sets list only once; a call after the cache duration has elapsed (using a hand-rolled fake `TimeProvider`, no new NuGet package) refetches; the resolved dictionary is correctly keyed by set name to set id
- [X] T028u [US1] Implement `TcgdexSetCatalog` in `backend/src/LootSingles.Infrastructure/CardCatalog/TcgdexSetCatalog.cs` per research.md §12: singleton-lifetime, Lock-guarded cached-`Task<IReadOnlyDictionary<string,string>>` (reusing the shape already proven in `TcgdexCardCatalogProvider`) with a 24-hour duration tracked via an injected `TimeProvider`, obtaining its `HttpClient` fresh per fetch via `IHttpClientFactory.CreateClient("tcgdex")` rather than holding one long-lived, to make T028t pass
- [X] T028v [US1] Update `TcgdexCardCatalogProvider` to take `TcgdexSetCatalog` as a constructor dependency and delegate to `GetSetIdsByNameAsync` instead of owning its own sets-list cache/fetch; added a new test proving sharing *across* two separately-constructed `TcgdexCardCatalogProvider` instances backed by the same `TcgdexSetCatalog` (the actual cross-request scenario this fix targets, alongside keeping the original single-instance test); updated `Program.cs` to register a named `"tcgdex"` `HttpClient` and `AddSingleton<TcgdexSetCatalog>()`/`AddSingleton(TimeProvider.System)`; full backend suite (115/115 unit, 106/106 integration) and CSharpier both clean; manually re-verified live — Pecharunt still resolves a real image after the refactor

### Code-Design-Review Remediation: Sticky Failure Cache (2026-08-25)

- [X] T028w [US1] Add a failing unit test to `TcgdexSetCatalogTests.cs` proving a failed sets-list fetch is retried on the very next call, not cached for the full 24-hour duration: a first call whose `HttpMessageHandler` throws leaves the catalog in a state where a second call (still well within the cache window) attempts a fresh fetch rather than rethrowing the same cached exception
- [X] T028x [US1] Fix `TcgdexSetCatalog.GetSetIdsByNameAsync` so a faulted fetch is not retained as the cached task for the remainder of the cache duration — the next call must attempt a fresh fetch instead of immediately rethrowing a stale failure, to make T028w pass; full backend suite (116/116 unit, 106/106 integration) and CSharpier both clean

**Checkpoint**: A picker opens a Pokémon order and sees the real card image for a confidently
matched line — User Story 1 (MVP) is independently complete and testable.

---

## Phase 3: User Story 2 - Never See a Wrong or Guessed Card Image (Priority: P1)

**Goal**: No match, an ambiguous match, an unreachable provider, or an unsupported game always falls back to the existing neutral placeholder — never a guessed image, and never a picker-visible error.

**Independent Test**: Simulate a provider failure and confirm the order detail view still loads successfully with the placeholder shown for that line and no error.

### Tests for User Story 2

- [X] T029 [P] [US2] Add a failing unit test to `backend/tests/LootSingles.UnitTests/CardCatalog/CardImageEnrichmentServiceTests.cs` asserting an unavailable/throwing provider returns `null` without propagating (this is expected to pass immediately from T005's implementation — this test exists to give the hard safety rule its own independent verification, not to drive additional production code, mirroring feature 007 US3's precedent) — already satisfied verbatim by T001's `TryGetImageUrlAsync_ProviderThrows_ReturnsNullRatherThanPropagating`; no duplicate test added
- [X] T030 [P] [US2] Add a failing integration test to `backend/tests/LootSingles.IntegrationTests/Orders/OrdersControllerTests.cs`: an order line whose registered provider throws still yields `200 OK` for the whole order, with that line's `imageUrl` explicitly `null` and no error surfaced
- [X] T031 [US2] Run T029–T030 and confirm they pass immediately (Foundational's `CardImageEnrichmentService` already implements this fallback per T005); if either fails, fix `CardImageEnrichmentService` to satisfy it before proceeding — both passed immediately, no production code change needed

### E2E for User Story 2

- [X] T032 [P] [US2] Add a failing Playwright test to `frontend/e2e/order-detail.spec.ts`: with the E2EHost's fake Pokémon provider configured to simulate an unavailable/throwing provider for one line, confirm the order detail view still loads successfully, that line shows the existing neutral placeholder, and no error state is shown to the picker
- [X] T033 [US2] Extend `backend/tests/LootSingles.E2EHost/Program.cs`'s fake Pokémon provider (from T026) to support simulating a failure for a specific line (e.g. via a distinguishing collector-number value), to make T032 pass; run the full Playwright suite to confirm no regression — all 10 Playwright tests pass

**Checkpoint**: The hard "no image is better than the wrong image" rule is independently verified
for the unavailable-provider and unsupported-game paths — User Story 2 is complete.

---

## Phase 4: User Story 3 - See a Magic: The Gathering Card's Real Image When Confidently Matched (Priority: P2)

**Goal**: The same confident-match image behavior proven for Pokémon also works for Magic product lines, via Scryfall.

**Independent Test**: Open the detail view for an order containing a Magic line with an exact set+collector-number identity and confirm the real image is displayed.

### Tests for User Story 3

- [X] T034a [P] [US3] Add failing unit tests to a new `backend/tests/LootSingles.UnitTests/CardCatalog/ScryfallSetCatalogTests.cs`, mirroring `TcgdexSetCatalogTests.cs` exactly (research.md §3/§12): two calls within the 24-hour cache duration fetch the sets list only once; a call after the duration elapses refetches; a failed fetch is retried on the next call rather than cached for the remainder of the duration (the sticky-failure-cache fix from the TCGdex code-design-review pass, applied here from the start); the resolved dictionary is keyed by set name to set code
- [X] T034b [P] [US3] Add failing unit tests to a new `backend/tests/LootSingles.UnitTests/CardCatalog/MagicSetNameNormalizerTests.cs`, mirroring `PokemonSeriesPrefixNormalizerTests.cs`'s `NormalizeCandidates` coverage style, per research.md §3's live-verified table: raw text with no special shape returns only itself (e.g. `"Tarkir: Dragonstorm"`, `"The Hobbit"`); a `"Commander: X"` prefix also offers `"X Commander"`; a `"Universes Beyond: X"` prefix also offers `X` alone; a `": Eternal-Legal"` suffix also offers `"X Eternal"`; no duplicate candidates
- [X] T034c [P] [US3] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/ScryfallCardCatalogProviderTests.cs` using `StubHttpMessageHandler`, exercising `TryMatchImageUrlsAsync` with a batch of identities against a stubbed `POST /cards/collection` response: a batch of exact set+collector-number matches with verified names returns each card's image URL, in one outbound request regardless of batch size (up to 75); a card absent from the response's `data` array (Scryfall's `not_found` case) maps to `null`; a returned card whose name doesn't match the imported product name maps to `null`; an unresolvable set name (no match in `ScryfallSetCatalog` via any `MagicSetNameNormalizer` candidate) maps to `null` without being included in the outbound request at all; a set name needing a `MagicSetNameNormalizer` candidate (e.g. the `"Commander: "` case) still resolves; a batch larger than 75 identities is split into multiple chunked requests; duplicate `CardIdentity` values within one batch don't produce a duplicate-key error and both resolve to the same result
- [X] T034d [US3] Run T034a–T034c and confirm they fail for the expected reason (`ScryfallSetCatalog`/`MagicSetNameNormalizer`/`ScryfallCardCatalogProvider` don't exist yet)

### Implementation for User Story 3

- [X] T035 [US3] Confirm against Scryfall's live API documentation (research.md §3) the exact `/sets` list shape (`code`/`name` fields) and the `/cards/collection` request/response shape (`identifiers` array of `{set, collector_number}` objects, `data` array of matched cards, `not_found` array) before implementing, per research.md §3's confirmed findings — this task exists to keep the confirmation traceable alongside implementation, not to re-derive it
- [X] T036a [US3] Implement `ScryfallSetCatalog` in `backend/src/LootSingles.Infrastructure/CardCatalog/ScryfallSetCatalog.cs`: singleton-lifetime, copied from `TcgdexSetCatalog`'s proven design (Lock-guarded cached `Task<IReadOnlyDictionary<string,string>>`, 24-hour duration via injected `TimeProvider`, `IHttpClientFactory`-per-fetch, and the sticky-failure-cache fix applied from the start rather than discovered later) to make T034a pass
- [X] T036b [US3] Implement `MagicSetNameNormalizer.NormalizeCandidates` in `backend/src/LootSingles.Infrastructure/CardCatalog/MagicSetNameNormalizer.cs` per research.md §3's live-verified candidate table, to make T034b pass
- [X] T036c [US3] Implement `ScryfallCardCatalogProvider` in `backend/src/LootSingles.Infrastructure/CardCatalog/ScryfallCardCatalogProvider.cs` per research.md §3: `ProductLine = "Magic"`; `TryMatchImageUrlsAsync` dedupes incoming identities, resolves each identity's set name via `MagicSetNameNormalizer.NormalizeCandidates` tried against `ScryfallSetCatalog` (dropping unresolvable ones from the request, mapped to `null`), chunks the remainder at 75 identifiers per `POST /cards/collection` request, verifies each returned card's name against the imported product name (PRD §32), and returns the image URL (`image_uris` or, for double-faced cards, the first face's) only for confident matches, to make T034c pass
- [X] T037 [US3] Register a named `"scryfall"` `HttpClient` via `AddHttpClient("scryfall", ...)` with a short timeout and a descriptive `User-Agent` header (per Scryfall's usage terms, research.md §8) in `backend/src/LootSingles.Api/Program.cs`; register `ScryfallSetCatalog` as a singleton and `ScryfallCardCatalogProvider` as `ICardCatalogProvider`, following the same registration shape `TcgdexSetCatalog`/`TcgdexCardCatalogProvider` already establish
- [X] T038 [US3] Run T034a–T034d, confirm they pass, and run the full backend test suite to confirm no regression

### E2E for User Story 3

- [X] T039 [P] [US3] Extend `backend/tests/LootSingles.E2EHost/Program.cs` to seed a Magic product line on the E2E test order and register a deterministic fake `ICardCatalogProvider` for `"Magic"`, reusing the pattern T026 established
- [X] T040 [US3] Add a failing Playwright assertion to `frontend/e2e/order-detail.spec.ts`: the Magic line renders an `<img>` with the fake provider's known `src`; confirm it fails then passes, and run the full Playwright suite to confirm no regression — all 11 Playwright tests pass
- [X] T040a [US3] Live-verify the real `POST /cards/collection` request/response cycle directly against Scryfall (not just stubs) before considering the provider done. **Caught a real defect**: a multi-faced card's (adventure/split/transform layout) top-level `name` is the combined name (e.g. `"My Precious // Allure of Power"`), not the single front-face name the packing slip's `ProductName` contains — comparing against it would have silently rejected every multi-faced card. Fixed by comparing against `card_faces[0].name` when present, falling back to the top-level name otherwise; confirmed live the image extraction already handled both shapes correctly via its existing fallback. Added a dedicated regression test; research.md §3 updated
- [X] T040b [US3] User-reported defect: `Ayara, First of Locthwain` (real order `F8433182-571240-3C6C6`) not resolving an image. Root cause confirmed live: `Set = "March of the Machine: Multiverse Legends"` needed the same dropped-prefix treatment as `"Universes Beyond: "`, for a different, not-yet-evidenced prefix — correctly failed safe to the placeholder per Principle V rather than a wrong image, but still a real, fixable gap. Generalized `MagicSetNameNormalizer`'s single `UniversesBeyondPrefix` constant into a curated `DroppedPrefixes` list (deliberately not a generic "drop everything before the first colon" rule — that would risk a false match, e.g. colliding with the real but unrelated set `"Final Fantasy"` for the already-correctly-handled `"Commander: FINAL FANTASY"` case) and added `"March of the Machine: "` to it. Added a regression test; confirmed live in the running app against the real order (also confirmed several other Magic lines in the same order were already resolving correctly, so this was a narrow gap, not systemic); full suite re-run clean (148/148 unit, 107/107 integration); research.md §3 updated

**Checkpoint**: Magic orders get the same confident-match image behavior already proven for
Pokémon — User Story 3 is independently complete and testable.

---

## Phase 5: User Story 4 - See a Disney Lorcana Card's Real Image When Confidently Matched (Priority: P3)

**Goal**: The same confident-match image behavior also works for Lorcana product lines, via Lorcast.

**Independent Test**: Open the detail view for an order containing a Lorcana line with an exact set+collector-number identity and confirm the real image is displayed.

### Tests for User Story 4

- [X] T042 [US4] Confirm Lorcast's live API documentation for its exact set+collector-number lookup endpoint and response schema (research.md §5's explicitly unconfirmed item); update research.md §5 with the confirmed shape before implementing — **done**: `GET /v0/cards/:set/:number` (exact, unique), `GET /v0/sets` (id/code-keyed, not name-queryable, same shape as TCGdex), `image_uris.digital.large` used verbatim (never constructed, unlike TCGdex), no batch endpoint, a real documented ~10 req/sec limit with 429/ban risk (research.md §5 update, 2026-08-25); also confirmed and documented the alternatives evaluated (Lorcana-api.com, LorcanaJSON, TCG API, Scrydex — all rejected) and the concurrency-capping decision below
- [ ] T041 [P] [US4] Add failing unit tests to `backend/tests/LootSingles.UnitTests/CardCatalog/LorcastCardCatalogProviderTests.cs` using `StubHttpMessageHandler`, following the same exact-match/no-match/ambiguous/name-mismatch coverage as T021/T034, against the shape confirmed in T042; assert the image URL is taken verbatim from `image_uris.digital.large` in the response, never constructed from a base URL
- [ ] T042a [P] [US4] Add failing unit tests to a new `backend/tests/LootSingles.UnitTests/CardCatalog/LorcastSetCatalogTests.cs`, mirroring `TcgdexSetCatalogTests.cs`/`ScryfallSetCatalogTests.cs` exactly (research.md §4/§12/§5): two calls within the 24-hour cache duration fetch the sets list only once; a call after the duration elapses refetches; a failed fetch is retried on the next call rather than cached for the remainder of the duration (the sticky-failure-cache fix applied here from the start); the resolved dictionary is keyed by set name to set code
- [ ] T042b [P] [US4] Add a failing unit test to `LorcastCardCatalogProviderTests.cs` proving `TryMatchImageUrlsAsync` never exceeds a bounded number of concurrent in-flight requests to Lorcast for a batch larger than that bound (a stub handler that records concurrent in-flight call count via an increment/decrement around an artificial delay, per research.md §5's semaphore decision); a second test proves per-identity exception isolation still holds through the semaphore gate (one identity's failure doesn't block or fail siblings), mirroring the same isolation test already proven for the default interface method (§3)
- [ ] T043 [US4] Run T041, T042a, T042b and confirm they fail for the expected reason (`LorcastCardCatalogProvider`/`LorcastSetCatalog` don't exist yet)

### Implementation for User Story 4

- [ ] T044 [US4] Implement `LorcastSetCatalog` in `backend/src/LootSingles.Infrastructure/CardCatalog/LorcastSetCatalog.cs`: singleton-lifetime, copied from `TcgdexSetCatalog`/`ScryfallSetCatalog`'s proven design (Lock-guarded cached `Task<IReadOnlyDictionary<string,string>>`, 24-hour duration via injected `TimeProvider`, `IHttpClientFactory`-per-fetch, sticky-failure-cache fix applied from the start) to make T042a pass
- [ ] T044a [US4] Implement `LorcastCardCatalogProvider.TryMatchImageUrlAsync` in `backend/src/LootSingles.Infrastructure/CardCatalog/LorcastCardCatalogProvider.cs` per the shape confirmed in T042: `ProductLine = "Lorcana TCG"` (confirmed by feature 010's real sample — research.md §5 update); resolve the set name to a code via `LorcastSetCatalog`; query by set code + collector number, accounting for promo-style collector numbers with no `/<total>` suffix (e.g. `"#36"`); verify the returned card's name; use `image_uris.digital.large` verbatim as the image URL, never constructed; return the image URL only on a single confident match, to make T041 pass
- [ ] T044b [US4] Override `TryMatchImageUrlsAsync` in `LorcastCardCatalogProvider` per research.md §5's decision: gate each per-identity `TryMatchImageUrlAsync` call through a `SemaphoreSlim` capping concurrent in-flight Lorcast requests to a small number safely under the ~10/sec guidance (confirm the specific value against real observed latency, not guessed), dispatching all of a batch's calls via the same `Task.WhenAll` fan-out shape already proven for TCGdex, with per-identity exception isolation preserved through the semaphore gate, to make T042b pass
- [ ] T045 [US4] Register a named `"lorcast"` `HttpClient` via `AddHttpClient("lorcast", ...)` with a short timeout in `backend/src/LootSingles.Api/Program.cs`, following the same registration shape `TcgdexSetCatalog`/`ScryfallSetCatalog`'s named clients already establish; register `LorcastSetCatalog` as a singleton and `LorcastCardCatalogProvider` as `ICardCatalogProvider`
- [ ] T046 [US4] Run T041, T042a, T042b, T043, confirm they pass, and run the full backend test suite to confirm no regression

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
- T042 (done) → T041, T042a, T042b → T043 → T044–T044a–T044b–T045 → T046
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
## Phase 7: TCGplayer-to-Scryfall Magic Set Crosswalk

- [X] T050 Copy the supplied crosswalk into `backend/src/LootSingles.Infrastructure/CardCatalog/Data/tcgplayer-magic-to-scryfall-set-codes.json` and include it in build and publish output
- [X] T051 Add failing unit tests for crosswalk loading, exact documented normalization, validation, missing mappings, and the checked-in `March of the Machine: Multiverse Legends` mapping
- [X] T052 Add failing provider tests for zero, one, and multiple candidate codes; exhaustive candidate evaluation; exactly-one-valid acceptance; ambiguity rejection; wrong-name rejection; and no semantic guessing or live external calls
- [X] T053 Implement a singleton `IMagicSetCrosswalk`/`MagicSetCrosswalk` that loads once, validates the JSON, and queries an in-memory normalized-name dictionary
- [X] T054 Replace Scryfall set-name guessing with crosswalk candidates, preserve exact collector/name verification, add safe structured logging, and remove obsolete set-list/semantic-normalizer code proven unnecessary by T051-T052
- [X] T055 Verify the JSON is present in build and publish output; run formatter, relevant tests, full backend tests, and build
- [X] T056 Create the future crosswalk-automation GitHub issue using only existing labels

## Phase 8: Post-Crosswalk Real-Order Matching Corrections (2026-08-25)

**Purpose**: A live audit of all 24 real imported orders' 46 Magic lines against the new crosswalk
(Phase 7) found 6 remaining failures beyond the 2 the crosswalk itself already fixed automatically
(8/46 → 6/46). Each of the 6 was root-caused and fixed; research.md §3 has full detail per fix.

- [X] T057 [P] Add a failing unit test to `TrailingParentheticalStripperTests.cs` for a name with
  two trailing parenthetical groups (`"Carrion Feeder (Borderless) (Foil Etched)"` →
  `"Carrion Feeder"`); fix `TrailingParentheticalStripper.Strip` to loop the strip-and-trim step
  until it stops changing the string, rather than a single anchored `Replace` call that only
  removes the last group, to make it pass
- [X] T058 [P] Add a new `CardNameMatcherTests.cs` covering diacritic-only differences in either
  direction (e.g. `"Seance"`/`"Séance"`), case-only differences, and the existing trailing-
  parenthetical-stripping behavior. Implement shared `CardNameMatcher.Matches(productName,
  providerCardName)` (`backend/src/LootSingles.Infrastructure/CardCatalog/CardNameMatcher.cs`):
  strip a trailing parenthetical (via T057's fixed stripper) then compare with diacritics removed
  (NFD decomposition, strip non-spacing marks, recompose NFC) case-insensitively. Wire it into both
  `TcgdexCardCatalogProvider` and `ScryfallCardCatalogProvider`'s name-verification step, replacing
  each provider's own direct `string.Equals` comparison
- [X] T059 [P] Add a new `HyphenatedSetNameNormalizerTests.cs`: the raw Set text is always the
  first candidate; a hyphen with no space before it but whitespace after (the observed artifact
  shape — e.g. `"...Tales of Middle- earth"`) also yields a second candidate with that whitespace
  collapsed; a genuine spaced dash separator (space on both sides, e.g. `"Foo - Bar"`) yields no
  second candidate. Implement `HyphenatedSetNameNormalizer.NormalizeCandidates` and wire it into
  `ScryfallCardCatalogProvider`'s set-code resolution step, trying each candidate against
  `IMagicSetCrosswalk` in order and stopping at the first that resolves — the displayed,
  authoritative `Set` value itself is never altered, only this internal crosswalk lookup. The
  source PDF for the affected real order was not available locally to confirm via the same
  word-position diagnostic feature 012 used for the Fomantis case, so this is a defensive
  mitigation for the evidenced symptom, not a confirmed `OrderLineExtractor` root-cause fix
- [X] T060 Add failing unit tests to `ScryfallCardCatalogProviderTests.cs` for the Unfinity
  Attraction letter-suffix retry: an identity with zero matches on its base collector number
  resolves via a single letter-suffixed match found on retry (`"Ferris Wheel"` #222 → `unf/222a`
  only); an identity whose base number has zero matches but *multiple* letter-suffixed variants
  are all valid (e.g. two distinct real `unf/226a`/`unf/226b`/`unf/226c` prints) still returns
  `null` rather than guessing; an identity that already resolves on its base number does not
  attempt the retry at all (no extra `/cards/collection` request). Restructure
  `ScryfallCardCatalogProvider.TryMatchImageUrlsAsync` into a two-phase lookup: phase one resolves
  every identity against its base collector number as today; phase two retries only the
  zero-match identities with `a`/`b`/`c`-suffixed collector numbers, reusing the same
  "exactly one valid candidate across every candidate tried" safety check for both phases, to make
  the new tests pass. Also updated the existing `TryMatchImageUrlsAsync_BatchLargerThan75_...`
  test's stub to return per-chunk-matching card data (a fixed response echoed to every chunk would
  duplicate every card across chunks under the new two-phase logic and incorrectly trigger phase
  two for all 80 identities)
- [X] T061 Run the full backend test suite (163/163 unit, 107/107 integration) and CSharpier to
  confirm no regression; re-run the live audit against all 24 real orders' 46 Magic lines via the
  running app: 44/46 now resolve a real image. Manually confirmed via a direct `POST
  /cards/collection` call that the remaining 2 (`"Merry-Go-Round"` #222, `"Swinging Ship"` #231)
  are exactly the genuinely-ambiguous-multiple-real-prints case T060's safety check is designed to
  reject — correct, safe "no image" behavior per Principle V, not a remaining defect
