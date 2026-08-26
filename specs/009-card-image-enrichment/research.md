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

**Update (Clarifications, 2026-08-25)**: `ICardCatalogProvider` gains an additive batch operation
(`IReadOnlyList<CardIdentity>` in, `IReadOnlyDictionary<CardIdentity, string?>` out) alongside the
original single-card one, as a default interface method that fans out to the single-card method
unless a provider overrides it — see §3's Scryfall rate-limit findings for the full rationale.
`TcgdexCardCatalogProvider` needs no changes; only `ScryfallCardCatalogProvider` overrides the
batch method. The adapter-per-game boundary itself, and `CardImageEnrichmentService`'s role in
picking the one provider for a line's game, are unchanged.

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

**Update (implementation, 2026-08-25 — set-name resolution confirmed live, PRD §41 Q29 resolved
for Magic)**: Resolved via a cached mapping from Scryfall's `/sets` list (`ScryfallSetCatalog`,
mirroring `TcgdexSetCatalog`'s design — see the sets-list caching addendum below), never
`/cards/search` (rate-limited at 2/second and a fuzzy-match endpoint, wrong shape for an exact
lookup per FR-003). Checked every real Magic `Set` text already present in this project's own
fixtures (`ValidImportTests.cs`) against Scryfall's live `/sets` data:

| Raw `Set` text | Result |
|---|---|
| `"The Hobbit"`, `"Commander Legends"`, `"Tarkir: Dragonstorm"`, `"Duskmourn: House of Horror"`, `"Modern Horizons 3"` | Direct match, no transformation needed — the majority case. Many real Magic set names already contain a colon (e.g. `"Tarkir: Dragonstorm"`), so a colon alone is not a signal that stripping is needed, unlike Pokémon's short-abbreviation-prefix pattern. |
| `"Commander: FINAL FANTASY"` → `"Final Fantasy Commander"` (`fic`); `"Commander: Marvel Super Heroes"` → `"Marvel Super Heroes Commander"` (`msc`) | TCGplayer's `"Commander: <Set>"` product-line prefix is reversed and de-colonized on Scryfall's side: `"<Set> Commander"`. Confirmed with two independent real examples. |
| `"Universes Beyond: Fallout"` → `"Fallout"` (`pip`); `"March of the Machine: Multiverse Legends"` → `"Multiverse Legends"` (`mul`) | TCGplayer prepends a marketing/crossover product-line label with no Scryfall equivalent at all — the real set name is just the suffix, prefix dropped entirely. Confirmed with two independent real examples across two different product lines. |
| `"The Hobbit: Eternal-Legal"` → `"The Hobbit Eternal"` (`hoc`, a genuinely distinct real set from `"The Hobbit"`/`hob`) | TCGplayer's `": Eternal-Legal"` suffix maps to a real, separate Scryfall set named `"<Set> Eternal"` — not a pseudo-category to ignore, an actual different print product. |

**Decision**: `MagicSetNameNormalizer.NormalizeCandidates(rawSetText)` (new,
`LootSingles.Infrastructure/CardCatalog/`, mirroring `PokemonSeriesPrefixNormalizer.NormalizeCandidates`'s
shape) returns an ordered list of candidates to try against `ScryfallSetCatalog`'s live-fetched
dictionary, stopping at the first that matches: (1) the raw text unchanged (covers the majority
direct-match case); (2) if the text starts with `"Commander: "`, the reversed, de-colonized form
(`"{suffix} Commander"`); (3) if the text starts with a known dropped-prefix (`"Universes Beyond: "`,
`"March of the Machine: "` — a curated list, extended as further real prefixes are confirmed), the
suffix alone with the prefix dropped; (4) if the text ends with `": Eternal-Legal"`, the prefix
with `" Eternal"` appended instead. Every candidate is only ever used if it exactly matches a real
entry already present in Scryfall's own fetched set-name dictionary — exactly like
`PokemonSeriesPrefixNormalizer`'s design — so a set-naming pattern not yet evidenced here simply
fails to match and falls back to the placeholder (Principle V), never risking a wrong image.

The dropped-prefix list is deliberately curated rather than a generic "drop everything before the
first colon" rule: that would risk a false match for a prefix already handled differently — e.g.
generically stripping `"Commander: FINAL FANTASY"`'s colon would produce `"FINAL FANTASY"`, which
would wrongly match the real but unrelated set `"Final Fantasy"` (`fin`) instead of the correct
`"Final Fantasy Commander"` (`fic`) the Commander-specific reversal rule already produces
correctly. Keeping this an explicit, evidence-only list avoids that collision risk entirely.

**Update (2026-08-25 — real-order defect report, `Ayara, First of Locthwain` not resolving)**: A
user manually testing a real imported order (`F8433182-571240-3C6C6`) reported this card's image
missing. Root cause confirmed live: `Set = "March of the Machine: Multiverse Legends"` needed the
same dropped-prefix treatment already implemented for `"Universes Beyond: "`, but for a different,
not-yet-evidenced prefix — the design's own safe-failure behavior (Principle V) meant this simply
fell back to the placeholder rather than a wrong image, exactly as intended, while still being a
real, fixable gap. Fixed by generalizing `UniversesBeyondPrefix` from a single constant into the
`DroppedPrefixes` curated list above and adding `"March of the Machine: "` to it, confirmed
resolves the exact card (`mul/13`, name verified) both in a unit test and live in the running app
against this real order — several other Magic lines in the same order (`Woodland Cemetery`,
`Hinterland Harbor`, `Foggy Bottom Swamp`) were already resolving correctly, confirming this was a
narrow gap, not a systemic failure.

**Rationale**: Same reasoning as the Pokémon precedent (§4's colon-to-space/promo-naming
corrections): reuse an already-proven, safe candidate-list pattern rather than inventing a new
mechanism, and only encode transformations backed by real, live-verified evidence rather than a
guessed general rule (a single incorrect guess at a "general" transformation risks silently
breaking the majority direct-match case, which trying literal, narrowly-scoped candidates avoids
entirely).

**Alternatives considered**: A single "smart" normalization function attempting to infer set-name
transformations generically (e.g., "always try reversing colon-delimited text") — rejected;
`"Tarkir: Dragonstorm"` and `"Duskmourn: House of Horror"` already directly match, so a
generic reversal rule would need its own exception list anyway, at which point an explicit,
narrowly-scoped candidate list is simpler and more auditable (Principle XIII).

**Update (implementation, 2026-08-25 — multi-faced card name verification, caught via live
Scryfall API check)**: Live-testing the real `POST /cards/collection` call against `hob/271`
("My Precious", an adventure-layout card) surfaced a genuine defect before it shipped: Scryfall's
top-level `name` field for a multi-faced card (adventure/split/transform/modal-double-faced
layouts) is the **combined** name — `"My Precious // Allure of Power"` — not the single front-face
name the packing slip's `ProductName` actually contains. Comparing against the raw top-level
`name` would have rejected every multi-faced card as a name mismatch, silently falling back to the
placeholder for a whole category of real cards. Fixed by comparing against
`card_faces[0].name` when `card_faces` is present, falling back to the top-level `name` for the
ordinary single-faced case. The image itself needed no equivalent fix: confirmed live that
Scryfall still populates the top-level `image_uris` for this adventure-layout example (only some
layouts, e.g. true double-faced transform cards, put images under `card_faces` instead) — the
existing `card.ImageUris?.Large ?? card.CardFaces?.FirstOrDefault()?.ImageUris?.Large` fallback
already covers both shapes correctly. Proven by a dedicated regression test
(`TryMatchImageUrlsAsync_MultiFacedCard_ComparesAgainstTheFirstFacesNameNotTheCombinedName`).

**Update (Clarifications, 2026-08-25 — pre-implementation provider/rate-limit audit)**: Before
starting this user story, and directly motivated by the Pokémon TCG API's earlier reliability
surprise (§4), Scryfall's actual terms and every credible free alternative were checked live
rather than assumed.

*Alternatives evaluated and rejected*:

- **magicthegathering.io** — confirmed live to return zero results for `Final Fantasy` (a real
  set already used in this project's own fixtures/tests — `"Commander: FINAL FANTASY"`), and its
  live rate-limit response header (`Ratelimit-Limit: 1000`) disagreed with its own published docs
  (5000/hour). An independent 2026 API comparison corroborates this: "Deprecated/unmaintained...
  Missing newer sets... Slower response times." Disqualified on data freshness alone.
- **TCG API (tcgapi.dev)** — a newer multi-game pricing API. Disqualified because it hosts no
  card images at all (only TCGPlayer product links, per its own docs), which this feature requires,
  and its free tier is capped at 100 requests/day.
- **MTGJSON** — bulk JSON downloads, not a live API, and explicitly has no images of its own
  ("get card images through Scryfall using the `scryfallId` property" per its own FAQ). Even this
  path terminates at Scryfall for the one thing this feature actually needs.
- **TCGdex** — confirmed to be Pokémon-only ("The Multilingual Pokémon TCG API"); not an option
  for Magic.

Scryfall remains the correct, and effectively only credible, choice.

*Rate limits — a materially different situation than TCGdex*: unlike TCGdex ("no published hard
rate limits"), Scryfall's docs are explicit that "endpoints (especially for card data) have
specific hard rate limits," confirmed on their Rate Limits page:

| Endpoint | Limit |
|---|---|
| `/cards/search`, `/cards/named`, `/cards/random`, `/cards/collection` | 2/second |
| `/cards/manifest` | 10/minute |
| All other methods (including `/cards/:code/:number` and `/sets`) | 10/second |
| Direct image files (`*.scryfall.io`) | No rate limit |

Exceeding the limit returns `429` and a 30-second lockout; "continuing to overload the API after
this point may result in a temporary or permanent ban of your application" — a harder, enforced
consequence than TCGdex's soft "please be considerate" ask. This project's own `Task.WhenAll`
concurrency design (§7), safe for TCGdex (no hard limit, proven via a 50-concurrent-request live
test), would not be safe unmodified against Scryfall: an order with more than ~10 Magic lines
resolving in the same instant could trigger `429`s, and repeated overage risks the shop's Scryfall
access being throttled or banned entirely.

*Decision — use `POST /cards/collection`*: Scryfall's batch endpoint accepts up to 75 card
identifiers per request (including the exact `{set, collector_number}` shape this feature needs)
and returns every matching card in one response. It sits in the 2/second bucket, which is
irrelevant once a single order's Magic lines fit in one (or, for an implausibly large order,
a small handful of chunked) request(s) rather than one request per card. This directly satisfies
Scryfall's own caching/bulk-use guidance ("we encourage you to cache the data you download from
Scryfall... at least for 24 hours" — matching the 24-hour duration already chosen for
`TcgdexSetCatalog`, §12) far better than per-card polling ever could, and sidesteps the hard
rate-limit risk entirely rather than managing it with a throttle.

**Decision — add a batch-shaped operation to `ICardCatalogProvider`, additively**: Using the batch
endpoint properly requires the provider boundary to accept a list of card identities and return a
mapping of results, not one identity in/one result out. `OrdersService.GetByIdAsync` already has
every line of an order available before enrichment starts, so it can group lines by `ProductLine`
and call each matching provider exactly once per group — no timing-based coalescing is needed to
"discover" the batch, unlike a request-scoped API that doesn't know its future callers in advance.

Rather than replacing the existing single-card `TryMatchImageUrlAsync` operation — which
`TcgdexCardCatalogProvider` already implements and `LorcastCardCatalogProvider` will implement,
and which has no batch-friendly endpoint to exploit on either provider — the new batch operation
is added to the interface as a C# default interface method whose body fans out to the single-card
one:

```csharp
public interface ICardCatalogProvider
{
    string ProductLine { get; }

    Task<string?> TryMatchImageUrlAsync(CardIdentity identity, CancellationToken cancellationToken);

    // Default: fan out to TryMatchImageUrlAsync, unchanged concurrency/outward behavior from
    // today. A provider overrides this only when it has a genuine batch endpoint to use instead.
    async Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
        IReadOnlyList<CardIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var distinctIdentities = identities.Distinct().ToArray();
        var results = await Task.WhenAll(
            distinctIdentities.Select(async identity =>
            {
                try
                {
                    return (identity, url: await TryMatchImageUrlAsync(identity, cancellationToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (identity, url: (string?)null);
                }
            })
        );
        return results.ToDictionary(r => r.identity, r => r.url);
    }
}
```

**Caught during implementation (isolation regression)**: an earlier version of this default method
let `Task.WhenAll` propagate a single identity's exception unfiltered, which faults the *entire*
`Task.WhenAll` — meaning one card's provider failure would wipe out every sibling card's
already-successful result within the same game group, not just that one card. This was caught by
the existing Playwright suite (the E2E "provider throws for one line" test started failing the
*other*, healthy Pikachu line too once dispatch moved from one call per line to one call per
game). Fixed by wrapping each identity's call in its own try/catch inside the fan-out, mapping
only the failing identity to `null` — restoring the same per-line failure isolation
`OrdersService` provided before this refactor (research.md §7's original guarantee). Proven by a
dedicated regression test
(`TryMatchImageUrlsAsync_DefaultImplementation_OneIdentityThrows_SiblingIdentityStillResolves`).
`CardImageEnrichmentService`'s own outer catch (§ existing) is still correct and unchanged for the
case a provider's own overridden batch implementation fails atomically for the whole group (e.g.
Scryfall's single `POST /cards/collection` request failing at the transport level, where there is
no way to know which cards would have individually succeeded) — that is a different, legitimately
group-level failure, not a per-card one.

`TcgdexCardCatalogProvider` needs **no code change at all** — it keeps implementing only
`TryMatchImageUrlAsync` exactly as it does today, and inherits a correct, behavior-identical batch
operation via the interface's default body. `LorcastCardCatalogProvider` will do the same when
built. Only `ScryfallCardCatalogProvider` overrides `TryMatchImageUrlsAsync` with real batch logic:
resolve set names to codes via a new `ScryfallSetCatalog` (mirroring `TcgdexSetCatalog`'s
singleton/24-hour-cache/`IHttpClientFactory`-per-fetch design from §12, including its
sticky-failure-cache fix from the code-design-review pass — the same defensive design applies here
from the start), then issue one `POST /cards/collection` request per ≤75-identity chunk, verify
each returned card's name (PRD §32), and populate the result dictionary. Its own
`TryMatchImageUrlAsync` (still required by the interface) simply delegates to the batch method with
a one-item list. A `CardIdentity` producing no confident match (set doesn't resolve, card isn't in
the response, or name doesn't verify) maps to `null`, same safe-fallback semantics as today.

**Rationale**: This is not reshaping a shared abstraction for a hypothetical future need —
Scryfall is a second, already-approved, concretely different provider whose efficient shape
genuinely differs from TCGdex's, which is exactly the situation Constitution Principle XII names
as warranting a real extension boundary rather than a workaround. Making the addition purely
additive (default interface method, not a breaking signature change) means the already-shipped,
already-tested `TcgdexCardCatalogProvider` — and every existing fake `ICardCatalogProvider` used
across the test suite, none of which implement more than the single-card method — needs zero
changes and zero re-verification beyond confirming the default method itself is correct, which is
a smaller, safer diff than forcing every provider to conform to a batch shape it doesn't need.
Duplicate `CardIdentity` values within one batch (e.g. two order lines that happen to describe the
identical card) are handled by deduping before building each provider's result dictionary — reads
against the resulting dictionary are unaffected by duplicates, since two lines describing the same
card correctly resolve to the same image.

**Alternatives considered**: A request-coalescing/"DataLoader" pattern kept entirely inside
`ScryfallCardCatalogProvider`, leaving `ICardCatalogProvider`, `TcgdexCardCatalogProvider`, and
`OrdersService` untouched — considered and rejected. It would need a buffering-window heuristic
(how long to wait for concurrent callers before flushing) to discover its own batch, when
`OrdersService` already knows the complete batch upfront with certainty; solving a problem that
doesn't exist (timing-based batch discovery) in exchange for avoiding a wider, but more honest and
more testable, diff was judged the worse trade. Unconditionally replacing the single-card method
with a batch-only signature (an earlier draft of this decision) — rejected once revisited: it would
have forced `TcgdexCardCatalogProvider` and its full test file to be rewritten for no behavioral
benefit, when a default interface method achieves the identical outward contract for zero diff on
every provider that doesn't need batching. A per-provider concurrency-limiting semaphore
(bounding concurrent per-card Scryfall calls under its rate limit) was also considered as a
smaller alternative to batching altogether — rejected because it still leaves an order with many
Magic lines taking multiple seconds to resolve and does not address Scryfall's own explicit
request to avoid fetching the same/similar data repeatedly; the batch endpoint solves both the
rate-limit risk and the latency at once.

**Update (implementation, 2026-08-25 — Product Owner replaced live Scryfall-derived set-name
resolution with a static TCGplayer-to-Scryfall crosswalk; supersedes `ScryfallSetCatalog` and
`MagicSetNameNormalizer` above)**: The live-fetched-`/sets`-plus-candidate-list design above
(`ScryfallSetCatalog` + `MagicSetNameNormalizer.NormalizeCandidates`) was replaced end-to-end with
a checked-in, static crosswalk built by cross-referencing TCGplayer's own product catalog
(`tcgcsv.com` groups endpoint) against Scryfall's bulk card data and `/sets` endpoint offline, ahead
of time, rather than inferring the mapping live from narrowly-scoped candidate-string guesses.
`ScryfallSetCatalog.cs`/`ScryfallSetCatalogTests.cs` and
`MagicSetNameNormalizer.cs`/`MagicSetNameNormalizerTests.cs` were deleted; no code outside the
Magic adapter changed.

**Decision**: `IMagicSetCrosswalk`/`MagicSetCrosswalk`
(`backend/src/LootSingles.Infrastructure/CardCatalog/{IMagicSetCrosswalk,MagicSetCrosswalk}.cs`) —
a singleton, loaded once at application startup from an embedded resource,
`Data/tcgplayer-magic-to-scryfall-set-codes.json`. Unlike the old design's *narrow* candidate list
(a handful of curated prefix/suffix transforms, each independently evidenced from one real card),
the crosswalk is *exhaustive*: every one of TCGplayer's 453 Magic product groups is pre-resolved
to its correct Scryfall set code(s) offline, where the mapping can be verified against TCGplayer's
own catalog rather than guessed one real-order failure at a time. `TryGetScryfallSetCodes(string
tcgplayerSetName, out IReadOnlyList<string> setCodes)` does a case-insensitive lookup after the
same NFKC-normalization/smart-quote/whitespace-collapse treatment `Normalize` applies internally,
validated eagerly at startup (`Program.cs`: `_ = app.Services.GetRequiredService<IMagicSetCrosswalk>();`)
so a malformed crosswalk file fails the app at boot, not on a picker's first order-detail request.
A TCGplayer set name genuinely maps to **more than one** Scryfall set code for some real entries
(e.g. a set that spans multiple physical Scryfall printings) — `ScryfallCardCatalogProvider`
already had to evaluate multiple set-code candidates per identity (§ above, the shared TCGdex
sets-list era), so this is not a new safety concern, just a different source for the candidate
list.

**Rationale**: The Product Owner judged an exhaustive, offline-verified mapping preferable to
narrowly-scoped, evidence-as-you-go candidate transforms discovered one real-order failure at a
time (as §3's `MagicSetNameNormalizer` history above shows happening repeatedly — Commander,
Universes Beyond, March of the Machine, Eternal-Legal, each added only after a real card failed to
resolve). A static crosswalk front-loads that verification instead of deferring it to production
failures, at the cost of needing a refresh process when TCGplayer or Scryfall add new sets (tracked
as its own follow-up GitHub issue — T056 below — rather than solved as part of this change).
`ScryfallSetCatalog`'s live `/sets` fetch (and its 24-hour cache, singleton lifetime, sticky-
failure-cache fix) is no longer needed at all: the crosswalk already knows the correct Scryfall set
code(s) directly, with no live "resolve this text against a fetched name list" step in between.

**Alternatives considered**: Keeping the live-fetched-plus-candidate-list design and continuing to
extend `MagicSetNameNormalizer`'s curated transform list as further real-order failures surface —
this is exactly what §3's history above already did three times (Commander reversal, dropped
Universes-Beyond-style prefixes, Eternal-Legal suffix) before this replacement; rejected by the
Product Owner as an approach that would keep discovering gaps reactively rather than covering the
whole TCGplayer Magic catalog up front.

**Update (implementation, 2026-08-25 — post-crosswalk real-order re-audit, four further matching
corrections)**: After the crosswalk replaced the old design, a live audit of all 24 real imported
orders' 46 Magic lines found the crosswalk itself resolved two cases the old design's candidate
list never covered (`"Brothers' War: Retro Frame Artifacts"`, `"Warhammer 40,000"`), taking real
failures from 8/46 to 6/46 automatically. The remaining 6 were each root-caused individually and
fixed:

1. **Diacritics.** A packing-slip `ProductName` and Scryfall's own canonical name can disagree on
   accenting the same real card (e.g. `"Jotun Grunt"` vs. Scryfall's `"Jötun Grunt"`). New shared
   `CardNameMatcher.Matches(productName, providerCardName)`
   (`backend/src/LootSingles.Infrastructure/CardCatalog/CardNameMatcher.cs`) strips a trailing
   parenthetical (as before, via `TrailingParentheticalStripper`) and then compares both sides with
   diacritics removed (Unicode NFD decomposition, strip `UnicodeCategory.NonSpacingMark`, recompose
   NFC) before an ordinal case-insensitive comparison. Both `TcgdexCardCatalogProvider` and
   `ScryfallCardCatalogProvider` now call this one shared helper instead of each doing its own
   direct `string.Equals` — the first time name-verification comparison logic has been shared
   across providers, since the same disagreement is provider-independent, not Magic-specific.
2. **Multiple trailing parentheticals.** `TrailingParentheticalStripper.Strip` used a single
   `Replace` call anchored to the end of the string, which only strips *one* trailing group — a
   card whose `ProductName` carries two (e.g. `"Carrion Feeder (Borderless) (Foil Etched)"`) was
   left with one still attached (`"Carrion Feeder (Borderless)"`), which then failed name
   verification against Scryfall's plain `"Carrion Feeder"`. Fixed by looping the same strip-and-
   trim step until it stops changing the string, so any number of consecutive trailing groups are
   removed, not just the last one.
3. **A mid-word hyphen line-wrap artifact in the Set text.** One real order's Set text for
   `"Universes Beyond: The Lord of the Rings: Tales of Middle-earth"` came through as `"...Tales of
   Middle- earth"` — a stray space after the hyphen, none before it — which does not match the
   crosswalk's exact key. The source PDF for this specific order was not available locally to run
   the same word-bounding-box diagnostic feature 012 used to confirm the Fomantis duplicate-echo
   was genuine PDF content rather than a parsing artifact (research.md §4's fourth category), so
   the true root cause (a genuine PDF line-wrap vs. an `OrderLineExtractor` artifact) is not
   confirmed the way that earlier case was. New `HyphenatedSetNameNormalizer.NormalizeCandidates`
   (`backend/src/LootSingles.Infrastructure/CardCatalog/HyphenatedSetNameNormalizer.cs`) offers the
   raw Set text as-is first, plus — only when a hyphen has no space before it but is followed by
   whitespace (the specific artifact shape observed; a genuine spaced dash separator like `"Foo -
   Bar"` has a space on *both* sides and is left untouched) — a second candidate with that
   whitespace collapsed. `ScryfallCardCatalogProvider` tries each candidate against the crosswalk
   in order, stopping at the first that resolves; the displayed, authoritative `Set` value itself
   is never altered, only this internal lookup. This is a defensive mitigation for the evidenced
   symptom, not a confirmed fix for a root cause in `OrderLineExtractor` — flagged as a gap in case
   the same artifact shape turns out to affect other, not-yet-seen set names too.
4. **Unfinity "Attraction" letter-suffixed collector numbers.** Some Unfinity Attraction cards
   print a letter-suffixed collector number on Scryfall (e.g. `"222a"`) that TCGplayer's packing
   slip omits (`"#222"`) — confirmed live via `POST /cards/collection` for `unf/222`
   (`not_found`) vs. `unf/222a` (`"Merry-Go-Round"`, found). This is not universal — other
   Attractions have a plain, unsuffixed number — and some base numbers have **multiple** distinct
   real prints sharing them with no unsuffixed version at all (confirmed live: `unf/222a` and
   `unf/222b` are both real, different `"Merry-Go-Round"` prints; `unf/231a`/`unf/231b` are both
   real, different `"Swinging Ship"` prints), so a letter can never simply be assumed. Added a
   second lookup phase to `ScryfallCardCatalogProvider.TryMatchImageUrlsAsync`: only for identities
   that found **zero** valid matches with their base collector number (not the ones that already
   resolved, and not the already-ambiguous ones — retrying those too would just waste a second
   round of requests for no benefit), retry with `a`/`b`/`c` suffixes appended, reusing the exact
   same "exactly one valid candidate across every candidate tried" safety check already used for
   multi-set-code candidates — so a base number with two genuinely different real letter-suffixed
   prints correctly still falls back to the placeholder (confirmed live for `"Merry-Go-Round"`
   `#222` and `"Swinging Ship"` `#231` in this project's own real order data) rather than guessing
   between them, exactly per Principle V. Only identities with zero matches get the extra
   candidates, so an order with many ordinary (non-Attraction) Magic lines does not pay for this
   quirk's extra request volume.

**Result**: Live audit against the same 24 real orders' 46 Magic lines after all four fixes: 44/46
resolve a real image; the remaining 2 (`"Merry-Go-Round"` #222, `"Swinging Ship"` #231) are the
genuinely-ambiguous-Attraction-prints case in point 4 above, correctly and safely showing no image
rather than a guess — not a remaining defect.

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

**Update (Clarifications, 2026-08-25 — Lorcast endpoint shape confirmed live; T042's uncertainty
resolved)**: Fetched Lorcast's own live API documentation (`lorcast.com/docs/api`) directly rather
than continuing to treat the shape as unconfirmed:

- **Card lookup**: `GET https://api.lorcast.com/v0/cards/:set/:number` — an exact, unique lookup by
  set code and collector number (e.g. `.../cards/1/207`), the same "direct id-keyed fetch, no
  multiple-candidate-match case" shape TCGdex already established (§4) — structurally impossible to
  return more than one card, unlike a text search. `image_uris.digital` (per Lorcast's Images docs)
  has `full` (1468×2048, JPG), `large` (674×940, AVIF), `normal` (488×681, AVIF), and `small`
  (146×204, AVIF) variants; `large` is this provider's chosen field, matching the resolution
  Scryfall's `large` (672×936) and TCGdex's `high` (600×825) already use for the same purpose.
  **Unlike TCGdex, the image URL must be taken verbatim from the response, never constructed**:
  Lorcast's own docs explicitly warn "you should not assume that the URL structure or domain will
  stay the same forever... use the card URIs returned from the API" (the CDN lives on a *separate*
  domain, `*.lorcast.io`, from the API itself, `api.lorcast.com`). This is actually simpler than
  TCGdex's shape (which requires appending `/{quality}.{extension}` to a returned base) — one field,
  no string-building.
- **Sets list**: `GET https://api.lorcast.com/v0/sets` returns `{id, code, name, released_at,
  prereleased_at}` per set — same "id/code-keyed, not name-queryable" shape as TCGdex's `/v2/en/sets`,
  so the same set-name-to-code resolution pattern (§4, §12: fetch once, cache, match by `name`
  case-insensitively) applies here too, not yet built.
- **No batch/bulk endpoint** exists (confirmed: the API overview page documents only Images, Sets,
  and Cards sections; no `card_ids`/collection-style endpoint is documented anywhere, unlike
  Scryfall's `POST /cards/collection`). Every card is one request.
- **Rate limit — real and enforced, unlike TCGdex, closer to Scryfall's model**: Lorcast's docs ask
  for "50–100 milliseconds of delay between requests... roughly 10 requests per second on average,"
  and state exceeding it returns `HTTP 429` with "potential IP bans" for continued overloading — a
  hard consequence, not TCGdex's "no published hard limit." The numeric limit itself (~10/sec) is
  5x more generous than Scryfall's 2/sec bucket for card-data endpoints, but because there is no
  batch endpoint to fall back on the way Scryfall's design does (§3), the existing default
  interface method's unthrottled per-card `Task.WhenAll` fan-out (safe for TCGdex only because
  TCGdex has no hard limit — confirmed via a live 50-concurrent-request load test, §4) would not be
  safe reused here unmodified: a Lorcana order with many lines firing all its lookups at once could
  trip the limit.
- **Maturity**: the API is versioned `v0` and explicitly labeled **beta** ("future versions of it
  will be backward compatible," but breaking changes are possible pre-stable) — a different risk
  profile than Scryfall (mature) or TCGdex (load-tested and proven reliable by this project). No
  live reliability data exists yet for Lorcast the way it does for the other two providers; worth a
  similar live check during T044 implementation, given that exact gap (assumed reliability, never
  verified under load) is what caused the original Pokémon TCG API choice to fail in production.
- **Cost/terms**: free, no API key, and operates under Ravensburger's Community Code Policy (Disney
  Lorcana's official fan-content policy), which explicitly prohibits Lorcast from ever charging for
  access — a stronger free-forever guarantee than either TCGdex or Scryfall's terms carry.

*Alternatives evaluated and rejected* (mirroring §3's Scryfall-alternatives audit):

- **Lorcana-api.com** — the only other genuine live REST API found; free, no key, and notably open
  source/self-hostable. Rejected for now: no documented rate limit anywhere in its public docs
  (unknown risk, not "no risk," and a worse-understood risk than Lorcast's explicit, documented
  one); its endpoints are named `/strict/`, `/fuzzy/`, `/lists/`, `/search/` — the presence of a
  `/fuzzy/` mode is a flag against PRD §32/FR-003's hard exact-match-only requirement, and public
  docs didn't confirm whether `/strict/` gives the same guaranteed-single-result shape Lorcast's
  direct `/cards/:set/:number` lookup does. Single hobbyist maintainer, no stated uptime track
  record — unverified the way TCGdex was live-load-tested before being trusted.
- **LorcanaJSON** — bulk JSON data dumps (hosted at `lorcanajson.org`), not a live API; the same
  category MTGJSON was for Magic (§3). Would require syncing and hosting our own dataset, directly
  conflicting with this feature's "live lookup only, no persistence" design (§11). Disqualified for
  the identical reason MTGJSON was.
- **TCG API (tcgapi.dev)** — already disqualified for Magic (§3); confirmed the same problems apply
  to Lorcana specifically: requires a paid API key, a 100-requests/day free-tier cap (unworkable for
  real order volume), and its "images" are generic TCGplayer marketplace product photos
  (`tcgplayer-cdn.tcgplayer.com`) rather than print-specific catalog card art — a real precision
  concern given PRD's emphasis on exact print/variant confidence, not just a rate-limit issue.
- **Scrydex** — no free tier at all ($29/month minimum). Disqualified on the same "no budget for a
  paid API" basis every other provider choice in this project has followed.

Lorcast remains the correct choice: the only free, live, exact-match API with a known, documented
rate-limit shape and a guaranteed-free license.

**Decision — bound concurrency with a `SemaphoreSlim` rather than pace with explicit delays**:
`LorcastCardCatalogProvider` overrides `TryMatchImageUrlsAsync` (the same default-interface-method
extension point Scryfall uses, but for throttling rather than batching, since no batch endpoint
exists to use instead) and gates each per-identity `TryMatchImageUrlAsync` call through a
`SemaphoreSlim` capping concurrent in-flight Lorcast requests to a small number safely under the
~10/sec guidance (a specific value to be confirmed against real observed latency during T044, not
guessed here) before dispatching all of a batch's calls via the same `Task.WhenAll` fan-out shape
already proven for TCGdex — per-identity exception isolation (the same fix applied to the default
interface method itself, §3) still applies, since a `SemaphoreSlim`-gated call can still throw or
time out independently of its siblings.

**Rationale**: This is the smallest fix proportional to the actual risk (Principle XIII): no new
NuGet package, no explicit sleep/delay bookkeeping to maintain, and it reuses the exact concurrent-
fan-out shape this codebase has already proven correct for TCGdex — only the addition of a
concurrency cap is new. A semaphore naturally self-paces reasonably well because each real HTTP
request carries real latency, without needing to compute or tune an explicit inter-request delay.
This also deliberately does not add retry-with-backoff for an occasional `429`: consistent with
research.md §6's existing rejection of a resilience/retry package for Scryfall, a request that hits
the limit despite the cap simply maps that one identity to `null` (the existing, already-proven
safe-fallback path), rather than adding new retry machinery FR-009 doesn't require.

**Alternatives considered**: Explicit paced dispatch (fire one request, `Task.Delay(~75ms)`, fire
the next, rather than concurrency-capping) — matches Lorcast's stated guidance more literally and
would be the safer choice if Lorcana orders were expected to carry many lines, but adds real,
compounding latency for a multi-line order (N lines × ~75ms minimum, serialized) and more
bookkeeping than a semaphore for a provider whose real-world line volume is expected to be low
(Lorcana is P3, sequenced last, and no real order data in this project's own fixtures has shown a
high-line-count Lorcana order the way the Pokémon 50-line order did) — reconsider this alternative
if T044's real-order testing shows otherwise. A `System.Threading.RateLimiter`-based token-bucket
limiter (available in the BCL since .NET 7) was also considered — a more precise fit for "N
requests per second" than a bare concurrency cap, but rejected as introducing a second concurrency-
control pattern into a codebase that has so far needed only `SemaphoreSlim`/`lock`-guarded
patterns (§4, §12), without a concrete demonstrated need for the token-bucket's extra precision.

**Status (implementation, 2026-08-25)**: Implemented and verified. `LorcastSetCatalog` mirrors
`TcgdexSetCatalog`/`ScryfallSetCatalog` exactly (including the sticky-failure-cache fix applied
from the start), keyed by set name → set code (not id, since the card-lookup path needs the short
code). `LorcastCardCatalogProvider.TryMatchImageUrlAsync` uses `image_uris.digital.large` verbatim
and shares `CardNameMatcher` with the other two providers. `TryMatchImageUrlsAsync` is overridden
with a `SemaphoreSlim(5)` gate around the same `Task.WhenAll` fan-out shape TCGdex's default
interface method uses, with per-identity exception isolation preserved. Since
`StubHttpMessageHandler` completes synchronously and never lets `Task.WhenAll` callers genuinely
interleave, the concurrency-cap test uses a small dedicated genuinely-async `HttpMessageHandler`
(real `Task.Delay`, `Interlocked` in-flight counter) local to that one test file rather than
touching the shared stub. Full regression clean: 175/175 backend unit, 107/107 integration, 45/45
frontend unit, 12/12 Playwright, CSharpier/oxlint/Prettier all clean.

**Update (implementation, 2026-08-26 — real-order defect report, `Scrooge McDuck - S.H.U.S.H.
Agent` not resolving)**: A user manually testing a real imported order (`F0000000-000AAA-00000`)
reported this card's image missing. Root cause confirmed live, and it was two separate defects,
not one:

1. **The promo set-name gap the design above already anticipated in principle, now confirmed with
   a real case.** `Set = "Disney Lorcana Promo Cards"` matched no entry in `LorcastSetCatalog`'s
   dictionary at all — Lorcast's real `/v0/sets` data has no set by that name; it publishes each
   promo drop separately (`P1`/"Promo Set 1", `P2`/"Promo Set 2", `P3`/"Promo Set 3",
   `cp`/"Challenge Promo", confirmed live). Worse, the same collector number recurs across them
   for different cards: `P1/36` = "Hidden Inkcaster", `P2/36` = "Mickey Mouse", `P3/36` = "Scrooge
   McDuck" — so this isn't a simple one-name-to-one-code fix. `LorcastSetCatalog` now derives a
   synthetic `"Disney Lorcana Promo Cards"` key at fetch time, populated with every real set code
   whose Lorcast name matches `^(Promo Set \d+|Challenge Promo)$` (evidence-based on the four
   confirmed real names, not a speculative general rule — a future differently-named promo drop
   would need its own evidence-based addition, same as every other pattern in this document). This
   mirrors `IMagicSetCrosswalk`'s 1-TCGplayer-name-to-many-real-codes shape exactly.
   `LorcastCardCatalogProvider.TryMatchImageUrlAsync` now loops every candidate code sequentially
   (Lorcast has no batch endpoint), evaluating each returned card's name and requiring exactly one
   valid match across all candidates before returning an image — the same "exactly one valid
   candidate" safety check already proven for Scryfall's multi-set-code candidates and its
   Attraction letter-suffix retry, so a base number with two genuinely different real prints across
   promo sets correctly falls back to no image rather than guessing (proven by a dedicated test).
   Since sequential per-identity requests stay inside the existing per-identity semaphore slot
   (§5's concurrency-cap decision), this does not change the overall concurrent-request bound —
   only ordinary-named-set lines still cost exactly one request; promo-labeled lines now cost up to
   four, sequentially, within their own slot.
2. **A genuinely new, more fundamental gap: Lorcast splits every card's printed name into separate
   `name` and `version` fields** (`{"name": "Scrooge McDuck", "version": "S.H.U.S.H. Agent"}`), but
   TCGplayer's `ProductName` is the combined `"Name - Version"` form — confirmed not just for this
   card but universally: even the unrelated `"Elsa"` line already checked into this project's own
   fixtures only ever appears as `"Elsa - Spirit of Winter"` style text (four different real Elsa
   printings checked live, all with a version). `TryMatchImageUrlAsync`'s name comparison now
   builds `card.Version is null/empty ? card.Name : $"{card.Name} - {card.Version}"` before passing
   to `CardNameMatcher`, so a plain card with no version still compares correctly (degrades to just
   `card.Name`) while every versioned card (the common case) now compares against the same combined
   form TCGplayer's `ProductName` actually uses. This is the more consequential of the two fixes:
   without it, most real Lorcana cards would fail name verification regardless of set resolution.

Also added an `ILogger<LorcastCardCatalogProvider>` (a genuinely new dependency, since this
provider previously had no ambiguity case to log — every prior lookup was a single, unique
set-code+number fetch) and a `LogWarning` for the multiple-valid-candidates case, mirroring
`ScryfallCardCatalogProvider`'s existing ambiguous-match logging for the identical scenario shape.

**Result**: Confirmed live against the real order — `imageUrl` now resolves to
`https://cards.lorcast.io/card/digital/large/....avif?...` for the previously-missing line. Full
regression re-run clean: 179/179 backend unit, CSharpier clean.

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

**Update (Clarifications, 2026-08-25)**: With `ICardCatalogProvider` gaining an additive batch
operation (§1, §3), concurrency now happens at two levels: `OrdersService.GetByIdAsync` groups an
order's lines by `ProductLine` and calls `Task.WhenAll` **once per distinct game present**
(unchanged in spirit — still concurrent, still independently try/caught per group), and each
provider decides its own internal concurrency for resolving its group's identities (TCGdex's
default-interface-method fan-out is per-card concurrent via `Task.WhenAll`, byte-for-byte
identical to today's behavior; Scryfall issues one batched request per ≤75-identity chunk instead
of fanning out at all). An order with only Pokémon and Magic lines now makes at most two top-level
provider calls total (one per game), each independently safe-failing, rather than one call per
line.

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
