# Research: Card Image Enrichment for Order Picking Detail

## 1. Provider adapter boundary

**Decision**: A new `ICardCatalogProvider` interface in `LootSingles.Application/CardCatalog/`,
one implementation per game in `LootSingles.Infrastructure/CardCatalog/`, dispatched by a small
`CardImageEnrichmentService` that picks the one provider serving a line's game (or none).

**Rationale**: This is the exact adapter role `IPackingSlipParser` already plays for TCGplayer
import parsing (constitution Principle IX: "Provider-specific quirks MUST remain contained within
the appropriate adapter boundary rather than leaking throughout the application"). Reusing an
already-proven pattern in this codebase, rather than inventing a new one, satisfies Principle
XIII's "no abstraction without a concrete purpose" — the purpose here is isolating three (soon a
fourth) genuinely different external integrations from the picking domain and UI.

**Alternatives considered**: A single "universal" provider with a big internal switch statement —
rejected; this is exactly the "central conditional/orchestration code that must be repeatedly
modified" Principle XII warns against, since adding One Piece would mean editing that switch
rather than adding a file.

## 2. Matching algorithm

**Decision**: Implement PRD §32's conservative algorithm literally, inside each provider adapter:
prefer an exact match on set + collector number; verify the returned card's name against the
imported product name; validate variant/printing information where the provider exposes it;
accept only when confident; otherwise return `null`. Never accept a match based on name-only
similarity (FR-003).

**Rationale**: This is not a design choice this feature is free to reinterpret — PRD §17.1/§32 and
spec.md FR-002/FR-003 state it as a hard rule. "Confident" is deliberately not reduced to a single
numeric threshold here; each provider's adapter defines what "exact set + collector number, name
verified" means against that provider's own data shape, which is the correct place for that
provider-specific judgment to live (Principle IX).

**Alternatives considered**: A shared, provider-agnostic scoring/confidence function — rejected;
the three providers' data shapes differ enough (see §3–5 below) that a shared scorer would either
be trivial (in which case it adds nothing) or would leak provider-specific assumptions into a
place that's supposed to be provider-neutral, working against Principle IX.

## 3. Magic: The Gathering — Scryfall

**Decision**: Query Scryfall's exact set-code + collector-number lookup
(`GET https://api.scryfall.com/cards/{set_code}/{collector_number}`), which returns at most one
card — there is no ambiguity to resolve once the correct Scryfall set code is known. Verify the
returned card's name against the imported product name before accepting. Use the card's
`image_uris` (or, for double-faced cards, the first face's `image_uris`) as the image.

**Open item for implementation**: the imported `Set` field is TCGplayer's set *name* text (e.g.
"SV: Black Bolt"), not Scryfall's short set *code*. Resolving name → code needs either a
search-based lookup (Scryfall's `/cards/search` accepts a set-name filter) or a cached mapping
built from Scryfall's `/sets` endpoint. This is PRD §41 Q29 ("How should set-name differences
between TCGplayer and catalog providers be normalized?") applied concretely to this provider —
confirm the exact approach against Scryfall's live API documentation during implementation of the
Magic user story, rather than guessing the mapping now. A failed or ambiguous set resolution is
itself just another "no confident match" outcome (returns `null`), so getting this wrong fails
safe rather than fails dangerous.

**Rationale**: Scryfall's own documentation recommends exactly this exact-identity lookup pattern
for print-level accuracy, matching PRD §31's assessment of it as "well suited to matching using
set and collector number." No API key is required for read access.

## 4. Pokémon — TCGdex (switched from the Pokémon TCG API)

**Update (Clarifications, 2026-08-25 — provider switched)**: During manual testing against a real
50-line multi-page order, the Pokémon TCG API (this section's original choice, below) was found to
fail roughly half of all requests — and not because of our own concurrency. Direct, controlled
testing against the live API with the app's configured key showed:

- 8 truly concurrent requests: 6 of 8 failed with `500`.
- 6 fully sequential requests (one at a time, no concurrency at all): 3 of 6 failed with `500`/`502`.
- 6 sequential requests with a full 1-second pause between each (eliminating any burst effect):
  4 of 6 still failed.
- Retrying one identical failing query 5 times succeeded on the 3rd attempt — failures are
  transient/random, not deterministic per-query, and not a documented rate-limit rejection (no
  `429` was ever observed; the API's own docs describe `429`s only for a documented
  concurrent-request race condition, which is a different failure mode than what was observed).

The same 50-concurrent-request load that broke the Pokémon TCG API was run against TCGdex
(`https://api.tcgdex.net/v2/en/`) and succeeded 50/50 (100%), the great majority in ~250ms, worst
case ~1.4s. No API key is required, and no hard rate limit is published (TCGdex's guidance is simply
"be considerate; cache responses locally rather than fetching the same data repeatedly" — see the
sets-list caching decision below, which follows this guidance).

**Decision**: Pokémon's `ICardCatalogProvider` implementation is `TcgdexCardCatalogProvider`,
querying TCGdex instead of the Pokémon TCG API. Per Constitution Principle IX (Replaceable
Integrations), this changes only the provider implementation — `ICardCatalogProvider`'s contract,
`CardImageEnrichmentService`, `OrdersService`, the API response shape, and the frontend are all
unaffected.

**TCGdex integration shape** (confirmed live, using the real "Genesect ex" / Black Bolt #67
example already used to confirm the series-prefix normalization in the Clarifications above):

- Sets are identified by a short id (e.g. `sv10.5b` for "Black Bolt"), not by name — unlike the
  Pokémon TCG API's `set.name:"..."` text query, there is no direct "query by set name" endpoint.
  `GET /v2/en/sets` returns every set as `{ id, name, ... }`; the (already series-prefix-normalized,
  per this feature's earlier clarification) `Set` text is matched against this list's `name` field
  (case-insensitive exact match) to resolve a set id.
- Card lookup is then a direct, unique fetch: `GET /v2/en/sets/{setId}/{localNumber}` — e.g.
  `GET /v2/en/sets/sv10.5b/67`. Confirmed live that TCGdex accepts the collector number both with
  and without leading zeros (`67` and `067` both resolve to the same card, `localId: "067"`), so —
  unlike the Pokémon TCG API — no leading-zero-stripping is strictly required, though the existing
  `#`/`/<total>`-stripping is still needed (`"#067/086"` → `"067"` or `"67"`, either works).
  Because this is a direct id-keyed lookup rather than a text search, there is no
  multiple-candidate-match case to handle the way the Pokémon TCG API's query needed
  (`FR-003`/multiple-plausible-matches is structurally impossible here) — a `404` (no card at that
  number) is the only "no match" outcome, and is treated as `null` exactly like any other failure.
- Response fields used: `name` (verified against the imported product name, same as before) and
  `image` — a base URL with **no file extension**, e.g.
  `"https://assets.tcgdex.net/en/sv/sv10.5b/067"`. A quality and format suffix must be appended to
  get a real image: `{quality}.{extension}`, where quality is `high` (600×825) or `low` (245×337),
  and extension is `webp` (recommended, smaller with transparency), `png` (transparent), or `jpg`
  (opaque background, not recommended). This provider uses `high.webp`.
- **Sets-list caching**: `GET /v2/en/sets` returns the full set list (a few hundred entries,
  low-churn reference data — a different concept from the "no caching of catalog *match results*"
  decision in §11, which is about not caching which image resolves for a given order line). Fetching
  it once per line (50 times for a 50-line order) would be wasteful and directly contradicts
  TCGdex's own "cache responses locally" guidance, so `TcgdexCardCatalogProvider` caches the
  resolved id-by-name map **for the lifetime of the provider instance**, not statically across
  requests. Confirmed via the DI registration (`AddScoped<ICardCatalogProvider>` wrapping the
  `AddHttpClient`-registered, otherwise-transient `TcgdexCardCatalogProvider`) that exactly one
  provider instance is created per HTTP request/scope, and `CardImageEnrichmentService` (also
  scoped) holds that same instance for every line's lookup within that request — so a 50-line order
  makes one sets-list fetch plus up to 50 per-card fetches, not 50 of each, and the cache is
  discarded (not left stale) once the request completes. The concurrent-caller de-duplication uses
  the "cache the in-flight `Task`, not just the eventual result" pattern, since `CardImageEnrichmentService`
  dispatches all of a request's lines concurrently (research.md §7) and multiple lines' lookups can
  race to populate this cache on the same instance.

**Alternatives considered**: Adding retry-with-backoff around the Pokémon TCG API instead of
switching providers — rejected as treating a symptom rather than the cause; the underlying API's
~50% baseline failure rate would still mean materially slower page loads (retries add latency per
retried line) for a worse overall result than a provider that simply doesn't fail this way. Throttling
concurrency to the Pokémon TCG API — rejected once sequential (fully non-concurrent) testing showed
nearly the same failure rate as concurrent testing; the problem was never primarily concurrency-driven.

**Update (Clarifications, 2026-08-25 — three implementation-level corrections found via full
evaluation of the real 50-line order against TCGdex)**: After the provider switch above, the same
order still resolved only a handful of images. Every failing line was checked directly against
TCGdex to find the cause. Two categories turned out to be genuinely correct, safe behavior (not
bugs): TCGdex has no `image` field at all for some Trainer Gallery alt-art cards (confirmed:
absent from the raw JSON response entirely, not merely null) — e.g. Roserade, Spiritomb — and a
`"Miscellaneous Cards & Products"` line (Charmeleon) has no real corresponding TCGdex set to
resolve at all. Both correctly and safely fall back to the placeholder; there is nothing to fix.
Three other categories were genuine, fixable defects in this feature's own not-yet-shipped code:

1. **Collector-number leading zeros** (confirmed via direct testing: `GET /v2/en/sets/cel25/005`
   404s, `GET /v2/en/sets/cel25/5` 200s for the same Pikachu card; same pattern confirmed for
   Evolving Skies `028`/`28`, while other sets like Black Bolt and Pitch Black accept both forms).
   `NormalizeCollectorNumber` now always strips leading zeros — restoring the same stripping the
   original Pokémon TCG API provider already had, which was incorrectly dropped when this provider
   was first written for TCGdex on the (wrong, single-example) assumption that TCGdex accepted
   both forms universally.
2. **Set names with an unrecognized colon-delimited prefix** (`"Celebrations: Classic
   Collection"` — `"Celebrations"` is itself a real, standalone TCGdex set name used as a
   sub-collection prefix, not a short series-era code, so it was never going to be in the ~10-entry
   abbreviation table by design). `PokemonSeriesPrefixNormalizer.NormalizeCandidates` now returns
   an ordered list of candidates to try against the sets dictionary: the abbreviation-stripped
   result first, and — only when no abbreviation was recognized — a second candidate with the
   first `": "` replaced by a plain space (confirmed live: TCGdex's real name is `"Celebrations
   Classic Collection"`, no colon). The second candidate is never tried when the first already
   matched, so this can't produce a worse match for the many cases the abbreviation table already
   handles correctly.
3. **Trailing printing/variant descriptors in the product name** (`"Alakazam V (Full Art)"`,
   `"Galarian Obstagoon (Secret)"` vs TCGdex's plain `"Alakazam V"`/`"Galarian Obstagoon"`).
   `TcgdexCardCatalogProvider` now strips a trailing parenthetical from the imported product name
   *only for the name-verification comparison* — `identity.ProductName` itself, and everything
   displayed to the picker, is completely unchanged (TCGplayer data stays authoritative). This
   reuses the same trailing-parenthetical shape `OrderLineExtractor` (feature 001) already
   recognizes as a printing/variant marker for `Variant` parsing, so it isn't a new, invented
   concept — just applying the already-established marker recognition to the matching step too.

All three are additive corrections to `TcgdexCardCatalogProvider`/`PokemonSeriesPrefixNormalizer`
(this feature's own code, not yet merged) — no change to `CardIdentity`, `ICardCatalogProvider`,
`CardImageEnrichmentService`, or anything outside the Pokémon adapter boundary.

A fourth, much larger category — TCGplayer's product title containing a redundant, duplicated
collector-number echo baked into the raw description itself (e.g. `"Fomantis - 085/084 - #085/084
- ..."`, confirmed via direct inspection of the PDF's word positions to be genuine source content,
not a parsing artifact) — causes `OrderLineExtractor` (feature 001) to absorb the echo into
`ProductName` (`"Fomantis - 085/084"` instead of `"Fomantis"`). This accounted for the large
majority of this order's remaining non-matches. It was **not** fixed here: it was a defect in
already-shipped feature 001 import behavior, the same category as the Lorcana (010) and multi-page
(011) fixes, and was addressed as its own Spec Kit feature (012) using this same real order as
validation, then merged back into this branch.

**Update (Clarifications, 2026-08-25 — promo-set naming mismatch found via post-012 re-evaluation;
design only, implementation pending sign-off)**: After feature 012 merged in, the same real order
was re-checked end-to-end (manual picking-detail view, corroborated against the live backend). The
`OrderLineExtractor` fix resolved the large majority of remaining lines (confirmed: Fomantis,
Manectric, Clefairy and others now show correct names and real images). Two further categories of
non-match were then investigated:

1. **Roserade / Trainer Gallery — already-known, correct safe-fallback behavior.** Re-confirmed
   live: `GET /v2/en/sets/swsh11tg/TG02` (the exact card the order line identifies — set resolves
   correctly via `"SWSH11: Lost Origin Trainer Gallery"` → `"Lost Origin Trainer Gallery"` →
   `swsh11tg`) returns a card named exactly `"Roserade"`, but the JSON response has no `"image"`
   key at all. This is the same category already documented above (§4, "correct, safe behavior")
   — not a new finding, just re-verified against this specific card on request.

2. **Promo-card set names — a new, genuine defect.** Five lines in the real order belong to
   Black Star Promo sets across three different eras:

   | Raw `Set` text | Era abbreviation | Normalized (today) | TCGdex's real set name | TCGdex set id |
   |---|---|---|---|---|
   | `"ME: Mega Evolution Promo"` (Sobble) | `ME` | `"Mega Evolution Promo"` | `"MEP Black Star Promos"` | `mep` |
   | `"SV: Scarlet & Violet Promo Cards"` (×3, incl. Charizard ex) | `SV` | `"Scarlet & Violet Promo Cards"` | `"SVP Black Star Promos"` | `svp` |
   | `"SWSH: Sword & Shield Promo Cards"` (×1) | `SWSH` | `"Sword & Shield Promo Cards"` | `"SWSH Black Star Promos"` | `swshp` |

   Confirmed live against TCGdex's complete `/v2/en/sets` listing that every Black Star Promo set
   follows one of two shapes, and that shape is **not** a deterministic function of the era
   abbreviation alone:

   | TCGdex set id | TCGdex set name |
   |---|---|
   | `dpp` | `DP Black Star Promos` |
   | `hgssp` | `HGSS Black Star Promos` |
   | `bwp` | `BW Black Star Promos` |
   | `xyp` | `XY Black Star Promos` |
   | `smp` | `SM Black Star Promos` |
   | `swshp` | `SWSH Black Star Promos` |
   | `svp` | `SVP Black Star Promos` |
   | `mep` | `MEP Black Star Promos` |

   Six eras (DP, HGSS, BW, XY, SM, SWSH) use the plain abbreviation; two (SV, ME) append a `P`.
   There is no rule evidenced in this data that predicts which form a given era uses.

**Decision**: Extend `PokemonSeriesPrefixNormalizer.NormalizeCandidates` so that, only when
`Normalize` *did* recognize a known abbreviation prefix (reusing that same recognized abbreviation
— no independent detection), and the resulting stripped name ends with `" Promo"` or
`" Promo Cards"` (the exact trailing shape evidenced above — narrow and evidence-based, not a
speculative general rule), it appends two more candidates ahead of any existing colon-to-space
fallback: `"{abbreviation} Black Star Promos"` and `"{abbreviation}P Black Star Promos"`. Both are
tried against TCGdex's real, live-fetched set-name dictionary exactly like every other candidate —
never asserted directly — so a wrong guess for some future, unevidenced era just fails to match
(falls back to the placeholder, per Principle V) rather than risking a wrong image.

**Rationale**: This mirrors the exact design already used for the Celebrations colon-to-space
fallback (§4 above, correction 2) — reuse already-computed state (the recognized abbreviation),
add a narrowly-triggered additional candidate, and let the existing "verify against the live set
list" step be the only thing that decides whether a candidate is actually used. Trying both known
suffix shapes rather than picking one is safe specifically because every produced candidate is
checked against the authoritative fetched dictionary before ever being used — this is not a guess
that reaches the picker, only a guess about which literal string to look up.

**Alternatives considered**: A per-era explicit mapping table (era abbreviation → TCGdex promo set
id), similar in shape to the existing `pokemon-series-abbreviations.json` — rejected for this fix:
it would require the exact same two literal forms this decision already tries, just spelled out
once per era instead of computed from the abbreviation already on hand; the two-candidate approach
covers every era observed today without needing a new data file, and remains exactly as safe
because both candidates are still verified against the live set list before use. If a future era
were discovered that uses neither shape, that would need its own table entry at that time — not
a reason to add one preemptively now (Principle XIII).

**Status**: Implemented and verified (T028o–T028r). Unit tests confirm both candidate forms
resolve correctly and non-promo-shaped names are unaffected; a live manual check against the real
order confirmed Pecharunt and Terapagos ex (`"SV: Scarlet & Violet Promo Cards"`) now display real
images. Sobble and Charizard ex (`"ME: Mega Evolution Promo"` / `"SV: Scarlet & Violet Promo
Cards"`) still correctly show the placeholder — confirmed live that TCGdex's own card records for
`mep-054` and `svp-196` have no `image` field at all, the same safe-fallback category already
documented above for Roserade/Spiritomb, not a defect in this fix.

---

## 4a. Historical: Pokémon TCG API research (superseded 2026-08-25 — see above)

Kept for context on how the original choice was evaluated; no longer the active decision.

**Decision**: Query by set and collector number (the API supports structured, Lucene-style query
parameters including `number` and set-scoped fields). Verify the returned card's name before
accepting. Use the card's `images.large` URL.

**Update (confirmed during T023 implementation)**: The open item below is resolved. `set.name` is
**directly queryable as text** in the `q` parameter — no separate set-id lookup step is needed.
Confirmed shape (base endpoint `https://api.pokemontcg.io/v2/cards`, Lucene-like query syntax):

```
GET https://api.pokemontcg.io/v2/cards?q=set.name:"Base Set" number:4
Header (optional): X-Api-Key: <key>
```

- `q` combines space-separated `field:value` terms (implicit AND); a value containing spaces must
  be quoted, e.g. `set.name:"Base Set"`.
- Collector number field is `number` (matches the packing slip's numeric portion of
  `CollectorNumber`, e.g. `"4"` from `"#4/102"` — the leading `#`, any `/<total>` suffix, **and any
  leading zeros** must be stripped before querying: confirmed live that the API stores `"67"`, not
  `"067"`, so `"#067/086"` must normalize to `"67"`, not `"067"`).
- API key is optional; sent via the `X-Api-Key` request header when present. Unauthenticated
  requests work but at a lower rate limit.
- Response is a JSON object with a `data` array (search) or `data` object (get-by-id) of card
  records; each record has `name`, `set.name`, `number`, and an `images` object with `small` and
  `large` URL fields (`images.large` is the field this provider uses, per the Decision above).

Source: could not reach `docs.pokemontcg.io` directly (WebFetch returned HTTP 403 on that host for
both the search-cards reference page and the site root); confirmed instead via web search results
quoting and citing that same official documentation (search-cards and get-card reference pages,
and the X-Api-Key auth note), cross-checked across multiple independent search results before
being treated as confirmed rather than guessed.

**Original open item for implementation** (resolved above): same set-name-normalization gap as
Scryfall (PRD Q29) — the Pokémon TCG API's set identifiers/names may not textually match the
packing slip's `Set` value exactly (e.g. abbreviations or "SV:" prefixes). This textual-mismatch
risk is still real (confirming the query *mechanism* doesn't guarantee every packing-slip `Set`
string matches a `set.name` value exactly) — a non-matching query legitimately returns zero
results, which the provider must treat as "no confident match" (`null`), not an error.

**Update (Clarifications, 2026-08-25 — PRD §41 Q29 resolved for this feature)**: Confirmed live
against a real manually-tested Pokémon order: the imported `Set` text for a "Genesect ex" line was
`"SV: Black Bolt"`, but the Pokémon TCG API's real `set.name` is `"Black Bolt"` (no prefix) —
`q=set.name:"SV: Black Bolt" number:67` returns zero results; `q=set.name:"Black Bolt" number:67`
returns the card. Per the Product Owner's decision, this is resolved via **series-abbreviation
prefix stripping**, deliberately chosen over a per-set/per-group lookup table so the data only
needs updating when Pokémon starts an entirely new naming era, not with every new set release (a
per-group table was considered and rejected for this reason — TCGplayer publishes new groups far
more often than new series).
`backend/src/LootSingles.Infrastructure/CardCatalog/Data/pokemon-series-abbreviations.json` is a
small (~10-entry), Product-Owner-supplied table of known series abbreviations, e.g. `"SV"` →
`"Scarlet & Violet"`, `"ME"` → `"Mega Evolution"`, `"SWSH"` → `"Sword & Shield"`. Before querying,
the provider checks whether the raw `Set` text starts with one of these abbreviations immediately
followed by optional digits and then `": "` (e.g. `"SV: "`, `"SV10: "`, `"ME05: "`); on a match, it
strips that whole prefix and queries using the remaining text (e.g. `"SV: Black Bolt"` → strip
`"SV: "` → query `"Black Bolt"`); on no match, it queries the raw `Set` text unchanged, exactly as
before this update — an unrecognized prefix never blocks the exact-match fallback path, it just
doesn't benefit from normalization yet (e.g. `"Celebrations: Classic Collection"` or
`"SM Trainer Kit: ..."`-style products, whose prefix isn't one of the ~10 known series
abbreviations, fall through unnormalized). A companion, much larger per-group table
(`tcgplayer_pokemon_groups_and_abbreviations.json`, ~218 entries, one per TCGplayer product group)
was also supplied and considered, but was rejected in favor of the small abbreviation table
specifically for this lower-maintenance-burden reason — the Product Owner does not want to update a
data file with every new set release. The abbreviation table's `name` field (e.g. `"Scarlet &
Violet"`) is not currently consumed by this feature — only the `abbreviation` key is used, for
prefix matching — but is intentionally kept in the data file as the input a separate, future
feature would use to persist and display `Series`/`Set Number` as their own order-line fields,
which is explicitly out of scope here.

**Rationale**: PRD §31 identifies this as the leading candidate; it exposes structured card/set
data and image URLs directly.

## 5. Disney Lorcana — Lorcast

**Decision**: Implement the same adapter contract (`ICardCatalogProvider`) against Lorcast, but
treat its exact endpoint shapes and response schema as **unconfirmed** until verified against
Lorcast's live API documentation during implementation of the Lorcana user story (lowest priority,
P3 — sequenced last specifically so this confirmation doesn't block Pokémon or Magic).

**Update (confirmed by feature 010)**: The packing-slip `ProductLine` value for Lorcana lines is
now known from a real, sanitized TCGplayer sample: `"Lorcana TCG"` (not `"Lorcana"` — the earlier
placeholder guess this section originally carried). `LorcastCardCatalogProvider.ProductLine` MUST
be `"Lorcana TCG"` for dispatch to actually match real order lines (`OrderLineExtractor` and its
tests use this exact value; see `backend/tests/LootSingles.UnitTests/Import/OrderLineExtractionTests.cs`
and the `lorcana-dashed-card-name.pdf` fixture). Separately, that same sample's collector number
was `"#36"` — a promo card with no `/<total>` suffix, unlike Pokémon's `"#067/086"` style — worth
accounting for when designing the Lorcast query in T044, since promo cards are sometimes modeled
as a distinct set/collection by catalog providers rather than sharing a mainline set's numbering.
The endpoint/response schema itself is still unconfirmed and remains T042's job.

**Rationale**: Unlike Scryfall (mature, extensively documented) and Pokémon TCG API (documented,
used elsewhere), this project has no prior integration experience with Lorcast, and inventing
specific endpoint paths or field names here would risk the implementation phase building against
a fabricated contract. The adapter *boundary* (what `ICardCatalogProvider` requires: given a
`CardIdentity`, return a confident image URL or `null`) does not depend on Lorcast's specific
shape, so this uncertainty does not block architecture or the other two providers.

**Alternatives considered**: Guessing a plausible REST shape now to keep this document "complete"
— rejected as exactly the kind of invented specificity that misleads implementation; PRD §31 itself
already flags Lorcast's selection as "not yet permanent architecture commitments."

## 6. HTTP client setup

**Decision**: Register each provider as a typed `HttpClient` via `AddHttpClient<TProvider>()` in
`Program.cs`, with a short configured request timeout (a few seconds) so one slow provider cannot
stall the whole detail-view request. No new NuGet package — `IHttpClientFactory` and
`System.Text.Json` are already part of the ASP.NET Core shared framework this project already
targets.

**Rationale**: This is the standard, minimal .NET pattern for outbound HTTP from an ASP.NET Core
app; `AddHttpClient<T>` gives each provider its own configured client (base address, timeout,
default headers such as a descriptive User-Agent, which Scryfall's terms request) without any
shared/global HTTP configuration leaking between providers.

**Alternatives considered**: `Microsoft.Extensions.Http.Resilience` (Polly-based retry/circuit
breaker) — rejected for this phase as unnecessary complexity beyond what FR-009 actually requires:
a single safe fallback to "no image," not retries, backoff, or circuit-breaking. Can be added
later without changing the adapter contract if real-world provider flakiness justifies it
(Principle XIII: no abstraction for a hypothetical future need).

## 7. Concurrency across lines

**Decision**: `OrdersService.GetByIdAsync` enriches all of an order's lines concurrently
(`Task.WhenAll`) rather than sequentially, each call independently timed-out and try/caught so one
line's provider failure or slowness cannot block or fail the others.

**Rationale**: An order can have many lines; serializing N external HTTP round trips would make
detail-view load time scale linearly with order size, which nothing in the spec requires and which
directly works against a picker's ability to use the view promptly.

**Alternatives considered**: Sequential enrichment — rejected as the simplest-but-slowest option
with no offsetting benefit; concurrent calls to independent external services carry no shared-state
risk here since each line's enrichment is fully independent.

## 8. Secrets and configuration

**Decision**: Any provider API key (only the Pokémon TCG API optionally uses one) is read from
`IConfiguration` (User Secrets locally — `LootSingles.Api` already has a `UserSecretsId`
configured — and application configuration in deployed environments), mirroring the existing
`Authentication:Lockout` options-binding pattern already in `Program.cs`. No key is required for
Scryfall or Lorcast reads. No key of any kind is ever committed to the repository.

**Rationale**: Directly required by constitution Principle VII ("No credentials... API keys...
may ever be committed to this repository") and consistent with the project's one existing
configuration-binding precedent.

## 9. Testing without live network calls

**Decision**: Provider unit tests use a small stub `HttpMessageHandler` (a hand-rolled subclass
returning a canned `HttpResponseMessage` per test case — exact match, ambiguous/ multiple results,
no match, malformed/error response) injected into that provider's `HttpClient`. Controller-level
integration tests substitute fake `ICardCatalogProvider` implementations via
`WebApplicationFactory.ConfigureWebHost`/`ConfigureServices`, the same override pattern
`ImportsControllerFailureTests`/`ImportsControllerScaleTests` already use in this codebase.

**Rationale**: This codebase has no HTTP-mocking or general mocking library (xUnit only, hand-written
fakes such as `FakeOrderRepository`); introducing one now for three provider tests is not justified
by Principle XIII when the standard hand-rolled `HttpMessageHandler` stub — a well-known,
zero-dependency .NET testing pattern — does the job. No automated test makes a live call to any
external provider, so CI never depends on third-party API availability or rate limits.

## 10. Production logging

**Finding**: Unlike features 007/008 (routine reads with no new failure mode), this feature
introduces a genuine new failure mode: an external provider being unreachable or timing out is an
operational condition worth diagnosing, distinct from the routine "no confident match found"
outcome.

**Decision**: Log one `LogWarning` (via constructor-injected `ILogger<T>` in each provider,
structured, no PII, no raw response bodies) when a provider call fails or times out. Log nothing
for a normal no-match/ambiguous-match result — that is expected, benign behavior, not a failure.

**Rationale**: Matches constitution Principle XI (as amended by feature 006) exactly: evaluate
whether a new failure mode warrants logging, and where it does, log proportionally via `ILogger<T>`
only, console/stdout only, no PII.

## 11. No caching or persistence (reaffirmed)

**Decision**: Reaffirming spec.md's Assumptions — this feature adds no table, column, or migration.
Every enrichment call is a live lookup made while building the `GET /api/orders/{orderId}`
response; nothing about a match is retained between requests.

**Rationale**: Already decided directly with the Developer before specification (see spec.md
Assumptions); restated here because it materially shapes this plan's data model (§ data-model.md)
and rules out any design that would otherwise seem natural for "enrichment," such as an
`OrderLineImage` table.

**Scope note (2026-08-25, §12 below)**: §12's sets-list cache is a narrower, infrastructure-layer
concern than this decision — an in-memory cache of TCGdex's own static *reference data* (set name
→ set id), not of any order's resolved card match or image URL. This decision is unaffected: every
order-detail request still performs a live per-card lookup and nothing about a match is persisted
or reused between requests.

## 12. Bulk reference-data caching (API citizenship)

**Finding (Clarifications, 2026-08-25)**: TCGdex's API guidance explicitly asks consumers to
"cache responses locally rather than fetching the same data repeatedly" for bulk data. Auditing
the actual call pattern found the sets list — 218 sets, ~35KB, confirmed live to change only a
handful of times a year — was being refetched in full on every single order-detail request. The
Lock-guarded cached-`Task` pattern introduced in §4 correctly de-duplicates concurrent lookups
*within* one request (`CardImageEnrichmentService` resolves every line concurrently via
`Task.WhenAll`), but the cache lived on `TcgdexCardCatalogProvider`, which is Scoped
(`AddScoped<ICardCatalogProvider>` wrapping `AddHttpClient<TcgdexCardCatalogProvider>`,
`Program.cs`) — a fresh instance, and therefore a fresh empty cache, on every request. The
frontend has no polling (`OrderDetailPage.tsx` fetches once per mount), so this isn't a runaway
loop, but any picker opening, leaving, and reopening an order — or simply refreshing the page —
re-triggers a full bulk fetch of effectively static data.

**Decision**: Extract the sets-list cache into a new singleton component, `TcgdexSetCatalog`
(`LootSingles.Infrastructure/CardCatalog/TcgdexSetCatalog.cs`), registered once for the lifetime of
the application rather than per-request. It keeps the same Lock-guarded cached-`Task`
de-duplication shape already proven in §4/§9's tests, plus a 24-hour cache duration — tracked via
an injected `TimeProvider` (`TimeProvider.System` in production) rather than `DateTimeOffset.UtcNow`
directly, specifically so the expiry behavior itself can be driven by a controllable fake time in
tests (Constitution Principle XIII: a seam is justified here by a concrete testability need, not
speculatively). On each access, if the cache is empty or older than 24 hours, it refetches; otherwise
it returns the already-resolved dictionary with no HTTP call. `TcgdexCardCatalogProvider` no longer
owns the sets-list fetch/cache itself — it takes `TcgdexSetCatalog` as an added constructor
dependency (a Singleton injected into a Scoped consumer, which is always safe — the reverse would
not be) and calls `GetSetIdsByNameAsync` on it.

To avoid holding one `HttpClient` (and its underlying handler) for the application's entire
lifetime — which would defeat `IHttpClientFactory`'s periodic DNS/handler refresh — `TcgdexSetCatalog`
takes an `IHttpClientFactory` (itself already a Singleton service, safe to inject into another
Singleton) and calls `CreateClient("tcgdex")` fresh only on the rare occasion it actually needs to
fetch (at most a few times a day given the 24-hour cache duration), rather than storing one
`HttpClient` instance as a field. `Program.cs` registers a named client, `AddHttpClient("tcgdex",
...)`, with the same base address and timeout `TcgdexCardCatalogProvider`'s own typed client
already uses, alongside `AddSingleton<TcgdexSetCatalog>()` and `AddSingleton(TimeProvider.System)`.

**Rationale**: This directly targets TCGdex's own stated guidance for exactly the case it
describes — bulk, slow-changing reference data, fetched repeatedly for no benefit. A 24-hour
duration is a deliberate middle ground: short enough that a newly-released TCGdex set becomes
usable within a bounded window without a redeploy, long enough that the ~35KB, ~218-entry list is
fetched at most a small, fixed number of times per day regardless of how many orders or pickers
are active, rather than once per page load. Reusing the existing Lock-guarded cached-`Task` shape
(rather than reaching for `IMemoryCache`) keeps the concurrency guarantee this codebase has already
proven correct (§9's `TryMatchImageUrlAsync_CalledTwice_FetchesTheSetsListOnlyOnce`-style tests) —
`IMemoryCache`'s `GetOrCreateAsync` does not by itself guarantee only one factory execution under
concurrent access to a missing/expired key, so an equivalent lock would be needed on top of it
anyway, at which point introducing a second caching abstraction alongside the one already proven in
this codebase adds no value.

**Alternatives considered**:
- *`IMemoryCache` with an absolute expiration* — considered and not rejected outright (it's a
  reasonable idiomatic choice), but not selected because it still needs an equivalent lock for
  single-flight de-duplication (a naive `GetOrCreateAsync` call does not prevent concurrent cache
  stampede), at which point it doesn't simplify anything over reusing this codebase's own
  already-proven Lock+cached-`Task` pattern, just promoted to Singleton lifetime.
- *An HTTP-level caching `DelegatingHandler` honoring `Cache-Control`* — rejected: TCGdex's `/sets`
  response sends `Cache-Control: no-cache, no-store, must-revalidate` (confirmed live), a
  deliberate signal that they do not want this handled via standard HTTP/CDN caching semantics;
  their own guidance is asking for an *application-level* cache instead, which is what this
  decision implements.
- *Holding one long-lived `HttpClient` as a Singleton field* — rejected per standard
  `IHttpClientFactory` guidance: a Singleton holding one `HttpClient` forever forfeits the
  factory's periodic handler rotation/DNS-refresh benefit. Calling `IHttpClientFactory.CreateClient`
  fresh only when an actual fetch is needed (at most a few times a day here) avoids this entirely.
- *Also caching individual per-card lookups in this same change* — rejected as out of scope for
  this fix; it's a related but separate, smaller optimization (each call is small and targeted
  rather than a bulk listing, though still redundant across repeat views of the same order/card),
  tracked as a follow-up GitHub issue instead of bundled in here.
