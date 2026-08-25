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

## 4. Pokémon — Pokémon TCG API

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
