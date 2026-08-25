# Implementation Plan: Card Image Enrichment for Order Picking Detail

**Branch**: `009-card-image-enrichment` | **Date**: 2026-08-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-card-image-enrichment/spec.md`

## Summary

Add confident-match card images to the order picking detail view for Pokémon, Magic: The
Gathering, and Disney Lorcana product lines, resolved live (no caching of match results) from
three external per-game catalog sources — TCGdex (Pokémon; switched from the Pokémon TCG API
2026-08-25 per research.md §4, once live testing under real order volumes found the original
choice unreliable), Scryfall, and Lorcast. Matching is conservative
(exact set + collector number, verified by name) per PRD §32; any non-confident outcome — no
match, ambiguous match, unsupported game, or a provider being unreachable — falls back to the
existing neutral placeholder feature 007 already built and tested, unchanged. The design is a
provider-abstraction adapter per game (mirroring the existing `IPackingSlipParser` boundary that
already isolates TCGplayer-specific import parsing from the picking domain), so adding a future
game (e.g. One Piece, once a provider is selected) means adding one new adapter — no change to
the picking domain, the API contract's shape, or the frontend. This is the first outbound HTTP
integration in the codebase; no persisted schema change is introduced anywhere in this feature.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.400 (backend); TypeScript / React 19 (frontend) — both unchanged

**Primary Dependencies**: ASP.NET Core + EF Core (unchanged); `IHttpClientFactory` / typed `HttpClient` (built into the ASP.NET Core shared framework — no new NuGet package) for the three provider adapters; `System.Text.Json` (already used throughout) for response parsing — no new package required on either stack

**Storage**: SQL Server via the existing `Order`/`OrderLine` entities — unchanged, read-only; catalog match results are never persisted (spec.md Assumptions: live lookup only, no caching in this phase) — no new table, column, or migration

**Testing**: xUnit (backend unit tests using a stub `HttpMessageHandler` per provider — this codebase has no HTTP mocking library and none is being added; SQL Server Testcontainers integration tests with the catalog providers substituted for fakes at the DI level, following the existing `WebApplicationFactory.ConfigureWebHost`/`ConfigureServices` override pattern already used by `ImportsControllerFailureTests`/`ImportsControllerScaleTests`), Vitest + React Testing Library (frontend component tests), Playwright (extends the existing order-detail scenario)

**Target Platform**: ASP.NET Core Web API + React SPA (unchanged)

**Project Type**: Web application (backend + frontend) — this feature is backend-heavy; the frontend change is a single conditional (`imageUrl` present → real `<img>`, absent → today's existing placeholder)

**Performance Goals**: Product lines are enriched concurrently (`Task.WhenAll`, each call independently timed-out and try/caught) rather than sequentially, so an order with many lines does not serialize N external round trips; no specific latency target is mandated by the spec, but a per-provider-call timeout is required so one slow/unreachable provider cannot make the whole detail view hang (FR-009)

**Constraints**: Live lookup only, no caching/persistence (spec.md Assumptions); detail-view-only scope — Browse Orders/Dashboard unchanged (FR-008); hard safe-failure rule — no image unless confident, no error surfaced to the picker on provider failure (FR-004/FR-009); catalog data must never overwrite or outrank imported order text (FR-006); no credentials committed to the repository (constitution Principle VII) — any provider API key is configuration-only

**Scale/Scope**: One new Application-layer adapter interface + a small dispatch service; three new Infrastructure-layer provider adapters (one per game); additive extension of the existing `OrderDetail`/`OrderLineDetail` records and `OrdersController`'s response DTOs (same pattern features 007→008 already established) with one new field, `ImageUrl`; one frontend conditional in the already-existing `OrderDetailPage.tsx` placeholder branch — no new endpoint, route, or page

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design — see below.*

| Principle | Evidence | Result |
|-----------|----------|--------|
| I/II Authority and scope | Every requirement traces to spec.md, which traces to the PRD's own provider research (§31, §40.7) and explicit V1-games/open-discovery framing (§30, §42 Priority 1 for One Piece), plus two decisions confirmed directly with the Developer (no caching; detail-view-only scope). No invented functionality. | PASS |
| III Reviewability | Larger than 007/008 but still one feature branch; the Architecture and Changeability Review below keeps each new component single-purpose so the diff stays reviewable despite three provider adapters. | PASS |
| IV TDD | New provider adapters, dispatch service, and response field all get Red→Green tests per constitution Principle IV (see tasks.md, once generated); stub-`HttpMessageHandler`/fake-provider patterns chosen specifically so these are testable without live network calls. | PASS |
| V Safe failure | This feature exists to implement PRD §17's hard rule directly: FR-002/FR-003/FR-004 require displaying an image only when confident, never selecting among ambiguous candidates, and falling back to the existing placeholder on no-match/ambiguous/unreachable — "no image is better than the wrong image" is the feature's central design constraint, not an afterthought. | PASS |
| VI Concurrency-safe claiming | Not applicable — this feature is read-only and adds no claim/state-transition logic. | PASS (N/A) |
| VII PII minimization | The only data sent to external providers is card-identifying information already public on the packing slip (set, collector number, product name, variant) — never customer name/address, which `Order`/`OrderLine` don't contain in the first place. No provider API key is committed to the repository; any key lives in User Secrets locally (existing `UserSecretsId` on `LootSingles.Api`) and in production configuration, never source. | PASS |
| VIII One responsive PWA | The frontend change is a single conditional inside the existing responsive `OrderDetailPage`/`.css` feature 007/008 already validated at desktop and mobile widths; an `<img>` element sized identically to the existing placeholder introduces no new responsive surface. | PASS |
| IX Replaceable integrations | This is the principle's direct subject: `ICardCatalogProvider` is the adapter boundary; the picking domain and UI depend only on `OrderLineDetail.ImageUrl` being present or absent, never on which provider (or whether any provider) supplied it. Adding One Piece later is one new adapter class plus one DI registration. | PASS |
| X Single-business scope | No multi-tenant concept introduced. | PASS |
| XI Observability | Evaluated: unlike 007/008 (routine reads with no new failure mode), this feature introduces a genuine new failure mode — an external provider being unreachable or timing out is an operational condition worth being able to diagnose, distinct from the benign "no confident match" outcome. Decision: log a single `LogWarning` (via `ILogger<T>`, no PII, no raw response bodies) when a provider call fails or times out; log nothing for the routine no-match/ambiguous-match outcome, keeping volume proportional to actual failures rather than every lookup. Console/stdout only, `ILogger<T>` only, per Principle XI. | PASS |
| XII Maintainable design | `ICardCatalogProvider` (Application) mirrors `IPackingSlipParser`'s existing role exactly: a source-specific adapter interface with implementations confined to Infrastructure. Dispatch-by-game lives in one small service so adding a provider never requires touching `OrdersService`, `OrdersController`, the repository, or the frontend. | PASS |
| XIII Simplicity | No mocking library, no resilience/retry package, no rich "catalog metadata" object beyond the one field (`ImageUrl`) the spec actually requires, no persistence layer for catalog data — every abstraction introduced (the provider interface, the dispatch service) has the concrete purpose Principle XIII requires: isolating three genuinely different external integrations and enforcing the safe-failure boundary in one place instead of three. | PASS |

### Architecture and Changeability Review

| Component | Responsibility/boundary and dependency direction | Variation, failure, testability, and simplicity |
|-----------|--------------------------------------------------|-----------------------------------------------|
| `CardIdentity` (Application, new) | A small input value (`Set`, `CollectorNumber`, `ProductName`, `Variant`) derived from an already-loaded `OrderLineDetail` — not a new persisted concept, just the shape a matcher needs. | No optionality beyond what `OrderLineDetail` already has (`Variant` nullable, matching feature 007's existing handling). |
| `ICardCatalogProvider` (Application, new) | One adapter boundary per game: exposes which game it serves and a single `TryMatchImageUrlAsync(CardIdentity, CancellationToken) -> string?` operation. Implementations are confined to Infrastructure and know nothing about EF Core, HTTP concerns aside from their own outbound call, or the picking domain. | The variation point this feature exists to isolate. Adding a game = one new implementation; no existing implementation or consumer changes. Returns `null` for any non-confident outcome — callers never need provider-specific failure handling. |
| `CardImageEnrichmentService` (Application, new) | Given a product line's game and `CardIdentity`, finds the one provider that serves that game (or none) and delegates; owns nothing about EF Core or HTTP. A component that primarily orchestrates rather than absorbs provider logic itself (Principle XII). | Unsupported game (including One Piece, and any future game with no adapter yet) returns `null` — the same code path as "no confident match," requiring no special-casing (FR-010 falls out of this naturally rather than needing its own branch). |
| Provider adapters — `TcgdexCardCatalogProvider` (switched from `PokemonTcgApiCardCatalogProvider` 2026-08-25, research.md §4), `ScryfallCardCatalogProvider`, `LorcastCardCatalogProvider` (Infrastructure, new) | Each owns exactly one external API's request shape, response parsing, and confidence verification (exact set + collector number, then name/variant verification per PRD §32) — provider-specific quirks stay contained here, never leaking into the Application layer or UI (Principle IX). Each is a typed `HttpClient` consumer registered via `AddHttpClient<T>`, with a configured timeout. | A provider that throws, times out, or returns an inconclusive result returns `null` rather than propagating — the dispatch service and everything above it sees only "no image," never a provider-specific exception type. Testable via a stub `HttpMessageHandler` returning canned responses (exact match, ambiguous, no match, malformed/error) — no live network call in any automated test. Swapping `TcgdexCardCatalogProvider` in for `PokemonTcgApiCardCatalogProvider` changed no file outside this provider and its DI registration, confirming this boundary's replaceability in practice, not just in principle. |
| `OrderRepository` (Infrastructure, existing) | Unchanged in responsibility — still a pure `AsNoTracking()` persistence read. Does not call any catalog provider; enrichment happens one layer up. | Keeps the "no external HTTP inside a repository" boundary the existing codebase already implies. |
| `OrdersService.GetByIdAsync` (Application, existing) | Gains one responsibility: after loading `OrderDetail` from the repository, enrich each line's `ImageUrl` via `CardImageEnrichmentService`, concurrently (`Task.WhenAll`) rather than serially, so N lines don't serialize N external calls. Still orchestrates rather than absorbs — the actual matching logic lives entirely in the providers. | Existing `GetAllAsync` (list views) is untouched — enrichment only runs on the single-order read path, directly satisfying FR-008's detail-view-only scope without needing a separate flag or branch. |
| `OrdersController`/`OrderDetailResponse`/`OrderLineDetailResponse` (Api, existing) | Gains one field, `ImageUrl` (string?), mapped straight through from `OrderLineDetail` — same additive-response pattern features 007→008 already established twice. No new action, route, or authorization concept. | Testable via the same `WebApplicationFactory` pattern already used, with the catalog providers substituted for fakes via `ConfigureWebHost`/`ConfigureServices` (the same override pattern `ImportsControllerFailureTests`/`ImportsControllerScaleTests` already use), so integration tests never make a live external call. |
| `orders` frontend feature (React, existing) | `OrderDetailPage.tsx`'s existing placeholder branch (`{...existing placeholder...}`) gains one conditional: render a real `<img>` when `line.imageUrl` is present, the existing neutral placeholder otherwise. `ordersApi.ts`'s `OrderLineDetail` type gains `imageUrl: string | null`. No new component, no new loading state — the placeholder's existing safe-failure behavior (feature 007) is reused unchanged for every non-confident case. | Directly reuses the already-tested "no image ever guessed" property from feature 007 (US3) rather than reimplementing it — this feature can only ever *add* an image where the placeholder already correctly appeared, never remove the placeholder's correctness. |

**Post-design gate**: The only genuinely new abstractions are `ICardCatalogProvider` and the small
dispatch service, each justified by a concrete, already-known variation point (three games now,
a fourth already anticipated by the PRD) and by testability (isolating the codebase's first
external HTTP dependency). No persistence, resilience/retry, or metadata abstraction was added
beyond what FR-002/FR-004/FR-007 actually require. No deviation required.

**Update (Clarifications, 2026-08-25 — `ICardCatalogProvider` gains an additive batch operation)**:
Pre-implementation research for User Story 3 found Scryfall enforces real, hard rate limits
(unlike TCGdex) and offers a batch lookup endpoint (`POST /cards/collection`, up to 75 identities
per request) that both avoids that risk and directly satisfies Scryfall's own bulk-data caching
guidance — see research.md §1/§3/§7 for the full evidence and alternatives considered. This
changes three rows of the table above:

- `ICardCatalogProvider` gains a second operation,
  `TryMatchImageUrlsAsync(IReadOnlyList<CardIdentity>, CancellationToken) -> IReadOnlyDictionary<CardIdentity, string?>`,
  **added alongside** the original single-card `TryMatchImageUrlAsync`, as a C# default interface
  method whose body fans out to the single-card operation unless a provider overrides it. This is
  still the same adapter boundary and the same variation point (one implementation per game); the
  addition responds to a second concrete provider (Scryfall) revealing a genuinely different
  efficient shape than the first (TCGdex), which is exactly the situation Constitution Principle
  XII names as warranting a real extension boundary — but making it additive rather than a breaking
  signature change means the extension costs nothing for a provider that doesn't need it.
- `TcgdexCardCatalogProvider` (and `LorcastCardCatalogProvider` once implemented) require **no
  code change at all** — they keep implementing only the single-card operation exactly as today,
  and inherit a correct, behavior-identical batch operation via the interface's default method.
  Nothing about the picking domain, the API contract, or the frontend changes, and nothing about
  Pokémon's already-shipped implementation or test coverage needs to move.
- `OrdersService.GetByIdAsync` now groups an order's lines by `ProductLine` before enriching,
  and calls `CardImageEnrichmentService` (and, transitively, the one matching provider's batch
  operation) once per distinct game present rather than once per line — still concurrent across
  groups (`Task.WhenAll`), still independently try/caught per group, still untouched for
  `GetAllAsync`. Only `ScryfallCardCatalogProvider` overrides the batch operation with real
  `POST /cards/collection` logic; its own single-card operation (still required by the interface)
  delegates to the batch one with a one-item list.

No other row in the table changes. This was chosen over an alternative that would have left
`ICardCatalogProvider` untouched entirely and hidden a timing-based request-coalescing buffer
inside `ScryfallCardCatalogProvider` — rejected because `OrdersService` already knows an order's
complete set of lines upfront with certainty, so inferring the batch from concurrent-call timing
would solve a problem that doesn't exist, in exchange for a narrower diff that leaves a trickier,
harder-to-test mechanism as the only thing standing between this feature and Scryfall's rate
limits. An earlier draft of this same decision replaced the single-card operation outright rather
than adding to it — revisited once it was clear that would force `TcgdexCardCatalogProvider` and
its full test file to be rewritten for no behavioral benefit.

## Project Structure

### Documentation (this feature)

```text
specs/009-card-image-enrichment/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/
│   └── order-detail-api.md   # Phase 1 output (documents the additive `imageUrl` response change)
├── quickstart.md        # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/LootSingles.Application/
│   └── CardCatalog/
│       ├── CardIdentity.cs                    # new — matcher input value
│       ├── ICardCatalogProvider.cs             # new — per-game adapter boundary
│       └── CardImageEnrichmentService.cs       # new — dispatch by game
├── src/LootSingles.Application/Orders/
│   ├── OrderDetail.cs                          # add ImageUrl to OrderLineDetail
│   └── OrdersService.cs                        # enrich lines concurrently in GetByIdAsync
├── src/LootSingles.Infrastructure/CardCatalog/
│   ├── TcgdexCardCatalogProvider.cs            # new (replaces PokemonTcgApiCardCatalogProvider, 2026-08-25)
│   ├── PokemonSeriesPrefixNormalizer.cs        # new — shared set-name normalization
│   ├── ScryfallCardCatalogProvider.cs          # new
│   └── LorcastCardCatalogProvider.cs           # new
├── src/LootSingles.Api/Controllers/OrdersController.cs   # add ImageUrl to response records
└── tests/
    ├── LootSingles.UnitTests/CardCatalog/      # new — one test file per provider + dispatch service
    └── LootSingles.IntegrationTests/Orders/    # extend OrdersControllerTests with fake providers

frontend/
├── src/features/orders/
│   ├── ordersApi.ts               # add imageUrl to OrderLineDetail
│   └── OrderDetailPage.tsx        # render real <img> when imageUrl present, else existing placeholder
├── tests/orders/OrderDetailPage.test.tsx   # extend for the image/placeholder branch
└── e2e/order-detail.spec.ts                # extend for the image/placeholder branch
```

**Structure Decision**: One new Application namespace (`CardCatalog`) housing the adapter
boundary and dispatch service, one new Infrastructure namespace housing the three provider
implementations — both mirroring the existing `Import`/`IPackingSlipParser` precedent rather than
inventing a new architectural pattern. Everything else is an additive extension of the exact
files features 007→008 already established. No new project or layer.

## Complexity Tracking

No constitution violations require justification.
