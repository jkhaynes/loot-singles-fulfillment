# Data Model: Card Image Enrichment for Order Picking Detail

No database tables, columns, or migrations are added or changed by this feature. Every model
introduced here is a transient, non-persisted shape used only while building one
`GET /api/orders/{orderId}` response (research.md §11).

## Existing persisted entities (unchanged)

`Order` and `OrderLine` (feature 001) are unchanged. This feature reads `OrderLine.ProductLine`,
`Set`, `CollectorNumber`, `ProductName`, and `Variant` — all already persisted and already exposed
by feature 008's `OrderLineDetail` — as input to matching. No new field is added to either entity.

## New transient types (Application layer, not persisted)

### CardIdentity

| Field | Type | Rule |
|---|---|---|
| `ProductName` | string | From the order line's existing `ProductName` |
| `Set` | string | From the order line's existing `Set` (TCGplayer's set name text — provider adapters resolve this to their own set identifier internally, research.md §3–4) |
| `CollectorNumber` | string | From the order line's existing `CollectorNumber` |
| `Variant` | string? | From the order line's existing `Variant`; validated against provider-exposed finish data where obtainable (PRD §32 step 9) — TCGdex's per-print `variants` flags and Scryfall's `finishes` array; not obtainable from Lorcast, which exposes no reliable per-print finish field (research.md §5 update, 2026-08-26) |

Constructed once per line, from data `OrderDetail`/`OrderLineDetail` (feature 007/008) already
carries — no new data collection.

### ICardCatalogProvider (interface)

| Member | Shape | Rule |
|---|---|---|
| `ProductLine` | string (get) | The game this provider serves, matched against `OrderLine.ProductLine` (research.md §1 dispatch) |
| `TryMatchImageUrlAsync` | `(CardIdentity, CancellationToken) -> Task<string?>` | Returns a confident image URL, or `null` for any non-confident/unavailable outcome — callers never distinguish *why* it was `null` (FR-004 treats no-match, ambiguous-match, and provider-unreachable identically) |

Three implementations in this phase — TCGdex (Pokémon; switched from the Pokémon TCG API 2026-08-25, research.md §4), Scryfall, Lorcast — each confined to
`LootSingles.Infrastructure/CardCatalog/` (research.md §3–5). No shared base class; each
implements the interface directly, since the only shared contract is the interface itself
(Principle XIII — composition/interface over inheritance where no real shared behavior exists).

### CardImageEnrichmentService

Given a line's `ProductLine` string and its `CardIdentity`, finds the one registered
`ICardCatalogProvider` whose `ProductLine` matches (case-insensitively) and delegates; returns
`null` immediately, with no provider call attempted, when no provider serves that game (this is
how FR-010's "unsupported game" behavior falls out of ordinary dispatch rather than needing a
special case).

## Order detail (existing read projection, extended again)

Extends the same `OrderDetail`/`OrderLineDetail` records (Application) and
`OrderDetailResponse`/`OrderLineDetailResponse` records (Api) features 007 and 008 already built —
one new field on the line shape:

| Field | Type | Rule |
|---|---|---|
| `imageUrl` | string \| null | `null` when no confident match was found, the match was ambiguous, the line's game has no provider in this phase, or the provider was unreachable — every case renders identically (the existing feature-007 placeholder) |

No other field on `OrderDetail`/`OrderLineDetail` changes. `OrdersService.GetByIdAsync` populates
`imageUrl` by calling `CardImageEnrichmentService` once per line (concurrently, research.md §7)
after the repository returns the base (unenriched) detail — the repository itself never calls a
provider (keeping persistence and external enrichment as separate concerns, plan.md's Architecture
and Changeability Review).

## Card image (no longer a placeholder-only concept)

Feature 007 established a static, neutral placeholder as the *only* image-position content. This
feature adds a second, conditional state — a real `<img>` sourced from `imageUrl` — without
changing the placeholder's own behavior or the rule that governs when it still applies (FR-004).
No image byte data is ever stored or proxied by this application; `imageUrl` is the provider's own
hosted image URL, rendered directly by the browser.
