# Quickstart: Validate Card Image Enrichment for Order Picking Detail

This validates that the order picking detail view now shows a real card image when a confident
match exists for a Pokémon, Magic, or Lorcana product line, and continues to show the existing
neutral placeholder — never a guessed image — for every non-confident case, while remaining fully
usable if a catalog provider is unreachable.

## Prerequisites

- Same as feature 008's quickstart: .NET SDK 10.0.x, Node.js, a running SQL Server-family
  database with migrations applied
- Outbound internet access from the backend process (this feature calls live external APIs — no
  caching, per spec.md Assumptions)
- At least one Ready order containing a product line for each phase-1 game (Pokémon, Magic,
  Lorcana) whose set and collector number identify a real, known card — import via Import Orders
  or seed directly
- No API key is required for Pokémon (TCGdex, switched from the Pokémon TCG API 2026-08-25 —
  research.md §4); Scryfall and Lorcast also require no key for this feature's usage

## Run the app

```powershell
dotnet run --project backend/src/LootSingles.Api
npm --prefix frontend run dev
```

Log in, then open an order's detail view.

## Manual validation

1. **Confident match shows the real image** (US1/US3/US4, FR-001/FR-002, SC-001): for a line whose
   set + collector number exactly identify a known card in each of the three phase-1 games,
   confirm that card's real image is displayed, and that the line's textual fields (product name,
   set, collector number, rarity, variant, condition) remain fully visible alongside it.
2. **Never a guessed image** (US2, FR-003/FR-004, SC-002): construct or find a line with no
   catalog match (e.g. a nonsense or clearly invalid collector number) and confirm the existing
   neutral placeholder is shown, not an image — and confirm this for an unsupported game (e.g. a
   One Piece line, if one exists) too.
3. **Provider unavailability fails safe** (US2, FR-009, SC-004): simulate a catalog source being
   unreachable (e.g., temporarily block outbound access to one provider's domain, or point its
   configured base address at an invalid host) and confirm the order detail view still loads
   successfully, with only that game's lines showing the placeholder — no error is shown to the
   picker.
4. **Textual data unaffected either way** (FR-005/FR-006, SC-003): confirm a matched line's
   displayed text is identical to what it would show unmatched — the image never changes or
   supplements any text field.
5. **Still detail-view-only** (FR-008): confirm the dashboard's Available Orders list and Browse
   Orders remain exactly as they were before this feature — text-only, no images.
6. **Repeated views are consistent** (Edge Cases): reopen the same order's detail view again and
   confirm the same line shows the same result (image or placeholder) as before, given the
   underlying order data hasn't changed.

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
npm --prefix frontend run lint
npm --prefix frontend run test
npm --prefix frontend run build
npm --prefix frontend run format:check
npm --prefix frontend run test:e2e
```

Expected: all existing tests continue to pass; new backend unit tests cover each provider adapter
(exact match, ambiguous, no match, malformed/error response) against a stub `HttpMessageHandler` —
no live network call in any automated test (research.md §9); the extended `OrdersControllerTests`
integration tests substitute fake providers via `ConfigureWebHost`/`ConfigureServices` and assert
`imageUrl` end-to-end; new frontend component tests cover the image-vs-placeholder branch; the
existing Playwright order-detail scenario is extended to assert the image/placeholder behavior for
at least one confidently-matched line (using a fake provider or a known-stable real card, per the
task breakdown's testing decision).
