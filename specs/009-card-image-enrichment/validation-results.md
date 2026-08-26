# Validation Results: Card Image Enrichment for Order Picking Detail

Recorded per tasks.md Phase 6 (Polish & Cross-Cutting Validation), 2026-08-26.

## T049 — Backend automated validation

Run from `backend/`:

| Check | Result |
|---|---|
| `dotnet build LootSingles.sln` | Build succeeded, 0 warnings, 0 errors |
| `dotnet csharpier check .` | Checked 159 files, 0 unformatted |
| `dotnet test tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj` | 179/179 passed |
| `dotnet test tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj` (SQL Server via Testcontainers) | 107/107 passed |

## T050 — Frontend automated validation

Run from `frontend/`:

| Check | Result |
|---|---|
| `npm run lint` (oxlint) | 0 errors; 1 pre-existing warning in `src/features/auth/AuthContext.tsx` (fast-refresh export-shape advisory), unrelated to this feature — not introduced by it |
| `npm run test` (vitest) | 45/45 passed |
| `npm run build` (tsc -b && vite build) | Build succeeded |
| `npm run format:check` (Prettier) | All matched files use Prettier code style |
| `npx playwright test` (full E2E suite, includes `order-detail.spec.ts`'s Pokémon/Magic/Lorcana/provider-failure scenarios) | 12/12 passed |

## T051 — Manual validation (quickstart.md)

Performed against the app running locally with real outbound internet access
(`dotnet run --project backend/src/LootSingles.Api --launch-profile https`, `npm --prefix frontend run dev`),
using real imported orders already present in the dev database.

1. **Confident match shows the real image (US1/US3/US4)** — confirmed for all three phase-1 games
   against real orders, each resolved via its live provider (no fakes):
   - Pokémon (TCGdex): multiple real orders, e.g. order 104's lines.
   - Magic (Scryfall): live-audited across all 24 real orders' 46 Magic lines — 44/46 resolve a
     real image (research.md §3); the remaining 2 are a genuinely ambiguous multiple-real-print
     case that correctly shows no image rather than guessing, not a defect.
   - Lorcana (Lorcast): order `F0000000-000AAA-00000`'s "Scrooge McDuck - S.H.U.S.H. Agent" line
     confirmed live resolving `https://cards.lorcast.io/card/digital/large/...avif` (research.md §5).
   - Confirmed the line's textual fields (product name, set, collector number, rarity, variant,
     condition) remain fully visible alongside the image in every case.
2. **Never a guessed image (US2)** — confirmed via the live Magic audit (2 lines with multiple
   valid candidates correctly show no image) and the Lorcana promo case pre-fix (multiple candidate
   sets correctly fell back to no image before the name-verification fix resolved the true match).
   No One Piece (or other unsupported-game) line exists in the current dev database to exercise the
   "unsupported game" path directly against real data; this path is covered by an automated
   integration test (`OrdersControllerTests`, a line for a game with no registered provider) and by
   `CardImageEnrichmentService`'s dispatch logic returning `null` for any product line with no
   matching provider — the same code path as "no confident match," per FR-010.
3. **Provider unavailability fails safe (US2, FR-009)** — simulated live by temporarily pointing
   `ScryfallCardCatalogProvider`'s configured base address at an invalid host
   (`https://invalid.example.invalid/` in `Program.cs`), restarting the backend, and re-fetching
   every real order via the API: all 46 Magic lines across every order correctly returned
   `imageUrl: null` (Scryfall unreachable), while 39 of 59 non-Magic lines on the same orders still
   resolved a real image normally (the remainder are the already-documented safe-fallback cases
   unrelated to this simulation — e.g. TCGdex records with no `image` field). Every order detail
   request still returned `200 OK` with no error surfaced. Reverted the change immediately after
   (confirmed via `git diff` showing no residual change) and restarted again: Magic lines resolved
   normally again (e.g. order 128's three lines all returned real `cards.scryfall.io` URLs). This
   real-network simulation corroborates the equivalent automated coverage already proven
   per-provider (unit tests for timeout/unreachable-host cases) and end-to-end
   (`order-detail.spec.ts`'s provider-failure Playwright scenario, using a fake failing provider).
4. **Textual data unaffected either way (FR-005/FR-006)** — confirmed across every order viewed
   during this validation pass: a line's displayed text (product name, set, collector number,
   rarity, variant, condition) is identical whether or not an image resolves; the image is strictly
   additive and never changes or supplements any text field.
5. **Still detail-view-only (FR-008)** — confirmed structurally: `OrdersService.GetAllAsync`
   (`backend/src/LootSingles.Application/Orders/OrdersService.cs`) returns `OrderListItem` straight
   from the repository and never calls `CardImageEnrichmentService` — the enrichment/`imageUrlsByIndex`
   logic exists only inside `GetByIdAsync`. The list response has no `ImageUrl` field to carry, so
   the Dashboard's Available Orders list and Browse Orders cannot show images by construction, not
   merely by current UI choice — consistent with this project's existing frontend test suite
   (`OrdersPage.test.tsx`/`DashboardPage.test.tsx`), neither of which asserts against any image
   markup for a list row.
6. **Repeated views are consistent (Edge Cases)** — reopened several already-viewed real orders
   (including the Scrooge McDuck order pre- and post-fix) and confirmed the same line shows the
   same result (image or placeholder) each time, given unchanged underlying order data.

## T052 — Architecture and Changeability Review

See the "Architecture and Changeability Review" section of `plan.md` (including its two Update
entries) for the as-implemented review — the boundaries described there (the `ICardCatalogProvider`
adapter, `CardImageEnrichmentService`'s dispatch role, the additive batch operation, and each
provider's containment of its own quirks) match what was actually built, verified provider-by-provider
during implementation of each user story. No Must Fix findings from this review; see the "Findings"
note below for two Advisory-level observations captured for future reference rather than as blocking
tasks.

**Findings**:

- *Advisory* — `IMagicSetCrosswalk`'s embedded JSON crosswalk and `LorcastSetCatalog`'s
  synthetic promo-set-code derivation are both offline/curated data-shaping decisions with their own
  maintenance burden as TCGplayer/Scryfall/Lorcast's catalogs evolve; each already has a tracked
  follow-up (a GitHub issue for the crosswalk, per tasks.md T056; an evidence-based extension path
  documented in research.md §5 for Lorcast). Not a defect — flagged only so the maintenance
  expectation stays visible.
- *Advisory* — `LorcastCardCatalogProvider`'s per-identity sequential multi-candidate loop (up to 4
  requests for a promo-labeled line) means an order with many distinct promo Lorcana lines pays
  proportionally more Lorcast requests than an equivalent Magic order would via Scryfall's batch
  endpoint. Acceptable given Lorcana's low expected line volume (P3 priority, no real order seen
  yet with more than a handful of Lorcana lines) — revisit only if real usage shows otherwise.

**Result**: Ready for `/speckit-converge`.
