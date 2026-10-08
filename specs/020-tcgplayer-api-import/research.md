# Research: TCGplayer API Order Import

**Feature**: `020-tcgplayer-api-import` | **Date**: 2026-10-08

Every fact about the TCGplayer API below comes from TCGplayer's **published documentation** (docs.tcgplayer.com, v1.39.0). None comes from a live response. Under the Legacy Addendum §4f (`CLAUDE.md`, "TCGplayer API Agreement"), live API data may not pass through an AI tool. Where the documentation is ambiguous, this document says so, and the item goes on the **live-verification list** (§14). A person works through that list on the first real run and describes what they find without pasting API data.

---

## 1. Authentication with the existing credentials

**Decision**: Obtain a bearer token with `POST https://api.tcgplayer.com/token`, sending the form body `grant_type=client_credentials&client_id={PublicKey}&client_secret={PrivateKey}` and the store's existing access token in the `X-Tcg-Access-Token` header. Cache the token in memory until 24 hours before its `.expires` time. On a `401`, discard it and fetch a new token once. A second `401` means the store's connection has been refused.

**Rationale**:
- This is TCGplayer's documented flow ("Getting Started" and "Store Authorization Workflow").
- The keys and the access token already exist. Fetching a short-lived bearer token from them is routine use and does not create a new credential.
- The documented `expires_in` (1,209,599, with `.issued` and `.expires` two weeks apart) means a token lasts about 14 days. One token fetch per process lifetime is typical, because the container scales to zero.

**Done once, by a person, outside the application**: TCGplayer's Store Authorization Workflow. Loot approves its own app at `store.tcgplayer.com/admin/Apps/{PublicKey}`, and the resulting code is exchanged with `POST /app/authorize/{code}` for the store access token, which goes straight into secrets. The keys are new, issued in 2026, and Loot had no earlier connection, so this is the normal setup step rather than a replacement of anything (corrected 2026-10-08). The application never calls `/app/authorize`, and a test asserts it, because authorization is a one-off human act, not runtime behaviour.

**Alternatives considered**:
- Fetching a new token per import: rejected. It wastes calls against the rate limit and gains nothing.
- Persisting the token in the database: rejected. It would store a secret, and a fresh process can fetch one with a single call.

## 2. Store key

**Decision**: Read `Tcgplayer:StoreKey` from configuration. If it is unset, resolve it once per process with `GET /stores/self` (the documented `SellerKey` field) and cache it.

**Rationale**: The user doesn't yet know the store key. Self-resolution costs one call per process lifetime, and setting the key explicitly removes even that. The documentation calls the field "SellerKey" rather than "storeKey", so the mapping goes on the live-verification list.

## 3. Which orders are "open" (FR-004)

**Decision**: Configure status **names** (`Tcgplayer:OpenOrderStatuses`, default `["Ready to Ship"]`). Each import resolves them to status ids with `GET /stores/{storeKey}/orders/manifest` (`orderStatusTypes`), then searches with `orderStatusIds`. If a configured name is not in the manifest, the import stops with a typed `TcgplayerResponseInvalid` failure that names the missing status. It never falls back to searching every order.

**Rationale**:
- The spec requires the status set to change through configuration without code (Clarification 2026-10-08).
- Names are what a person sees in the seller portal. The ids appear only at runtime, and we can't look at them.
- The manifest call costs one request per import.

**Alternative rejected**: configuring numeric ids. Nobody here can read the ids without seeing live data.

## 4. Paging and completeness (FR-005)

**Decision**:
- **Search:** `GET /stores/{storeKey}/orders?orderStatusIds=…&offset=…&limit=…` returns order numbers plus `totalItems`. Request pages of `Tcgplayer:PageSize` (default 50), advancing `offset` by the number of results actually returned, until it reaches `totalItems`.
- **Line items:** `GET /stores/{storeKey}/orders/{orderNumber}/items?includeItemDetails=true` is paged the same way.
- **Guard:** a page that returns zero results before `totalItems` is reached is treated as a malformed response, so a loop can never spin forever.
- **Completeness:** an order is complete only when the number of fetched items equals the `totalItems` reported for its items. Otherwise it is rejected as `IncompleteOrder`.

**Rationale**: The documentation gives a default `limit` of 10 but no maximum. Advancing by the count actually returned stays correct whatever cap the server applies. Comparing against `totalItems` is the documented, reliable completeness signal.

## 5. Product-count cross-check (spec edge case)

**Decision**: Read each order's details with `GET /stores/{storeKey}/orders/{orderNumbers}` (a comma-separated batch per search page) and compare `productCount` with the **sum of line quantities**. A mismatch rejects the order as `IncompleteOrder`.

**Uncertainty**: The documentation doesn't say whether `productCount` counts distinct lines or units. The plan assumes units, because that is the meaning a packing slip's "items" total has. If the assumption is wrong, every order fails loudly with a specific reason rather than importing wrongly, which is the safe direction (Principle V). This is on the live-verification list. If it turns out to count lines, the fix is a one-line change in the translator, covered by its unit test.

**Customer data**: the details DTO declares **only** `orderNumber`, `orderStatusTypeId` and `productCount`. The `customer`, `shippingAddress` and `orderValue` objects are never bound into objects, so they cannot reach storage, logs or the UI (FR-017). Raw response bodies are never logged.

## 6. Collector number, rarity and image (FR-008, FR-019)

**Decision**:
1. Each line item carries a `skuId`. Batch every new line's SKUs into `GET /catalog/skus/{skuIds}`, chunked at 50, to get each SKU's `productId`.
2. Batch the product ids into `GET /catalog/products/{productIds}?getExtendedFields=true`, also chunked at 50. From each product take:
   - the `extendedData` entry named `Number` as the collector number, stored with a `#` prefix to match PDF lines, for example `#067/086`;
   - the `Rarity` entry, used when the line item has no `rarity`;
   - `imageUrl`.
3. A line's image is the item's `productImageUrl` (present with `includeItemDetails=true`), or the product's `imageUrl` if the item has none. Both are TCGplayer's own data for the exact product sold.
4. A SKU or product reported as not found, or a product with no `Number` entry, leaves that line with **no** collector number and/or **no** image. The line is still imported (FR-008), and the UI shows "No number".
5. A **transport** failure during catalog lookup (unreachable, 5xx, refused) is not treated as "not found". It stops the import as unavailable (§10), so a temporary outage never permanently stores lines with no number.

**Rationale**: Line items carry no collector number. TCGplayer's catalog is the only source of it that keeps API data inside TCGplayer (decision 4). The SKU → product hop is needed because the line-item schema has no `productId`. Batching keeps the cost to about two calls for a typical import.

**Uncertainty**: The documentation names `extendedData` items as `{name, displayName, value}` but doesn't list the names. `Number` and `Rarity` are the names TCGplayer's catalog is widely reported to use. If the real name differs, every line shows "No number" (safe: a missing value, never a wrong one), and the fix is a configuration change, because the key names are options. This is on the live-verification list. The SKU response's `printingId` vs `variantId` discrepancy is irrelevant here, since only `productId` is read.

**Alternatives rejected**:
- Scryfall, TCGdex or Lorcast by name: forbidden by decision 4 and §4a.
- Parsing a number out of `productName`: guesswork, which Principle V forbids.

## 7. Mapping line fields

**Decision**: The translator, a pure function in Infrastructure, maps:

| OrderLine field | Source | Notes |
|---|---|---|
| `ProductLine` | item `categoryName` | Verbatim, as the PDF path stores the slip's game text. API lines never use the name-keyed catalog providers, so no mapping table is needed. |
| `ProductName` | item `productName` | Verbatim. |
| `Set` | item `groupName` | Verbatim. |
| `Condition`, part of `Variant` | item `condition`, through the existing **`ConditionVariantParser`** | TCGplayer condition names can carry a printing suffix, for example "Near Mint Foil", exactly as the slip does. Reusing the parser keeps one condition vocabulary, and an unknown condition rejects the order as `MissingCondition`, the same as the PDF path. |
| `Variant` | parser suffix, then item `printing` if it isn't `Normal` and isn't already in the suffix | Produces the same vocabulary as the PDF path ("Foil", "Holofoil", "Reverse Holofoil", …). If `isFoil` is true and neither source mentions a foil, `Foil` is added, so a foil flag is never lost. |
| `Rarity` | item `rarity`, else the catalog `Rarity` entry | Optional, as today. |
| `CollectorNumber` | catalog `Number` (§6) | Now nullable. |
| `Language` | item `language` | New, nullable. PDF lines leave it null. |
| `ImageUrl` | §6 | New, nullable. Used only for API orders. |
| `Quantity` | item `quantity` | Must be greater than zero, the same rule as PDF lines. |
| `RawDescription` | composed from the fields above, joined with " - " | Keeps the existing required column meaningful. It is never parsed back. |

## 8. A source-neutral import core (Principles IX, XII, XIII)

**Finding**: `PackingSlipImportService` holds the whole per-order pipeline in one method: validate, check for a duplicate, create, attach the slip, save, handle a duplicate race, record the result, log the completion. Its first lines are PDF-shaped, and `OrderLineValidator` validates **text** (`RawProductLine`).

**Decision**: A second source now exists, so Principle XIII requires consolidating rather than copying. Extract:

- **`OrderCandidate` / `OrderLineCandidate`**: the source-neutral input records that each adapter produces (data-model.md).
- **`OrderCandidateValidator`**: shared business validation:
  - an order identifier is present;
  - there is at least one line;
  - each line's quantity is greater than zero;
  - product name, set and condition are present.
  The PDF-only check that a slip line had a collector number stays in the PDF text extractor, because a PDF line without one is a parse failure. For the API it is a legitimate absence.
- **`OrderImporter`**: the per-order unit of work, moved out of `PackingSlipImportService` unchanged in behaviour. It validates, checks for a duplicate, creates the `Order` with its `ImportSource`, runs an optional per-order hook (the PDF path uses it to attach the slip), saves, handles the unique-violation race and persistence failure, and records the `ImportOrderResult`.
- **`ImportAttemptLog`**: the existing `LogCompletion` logic, shared by both services.

`PackingSlipImportService` keeps its PDF-specific work: buffering, parsing, summary mismatch and slicing. It now calls `OrderImporter` per block, after converting the block with `OrderLineExtractor`. The new `TcgplayerApiImportService` does the same with the API feed's candidates.

**Rationale**: Both sources share every rule except how input is read. Without the extraction, the duplicate race, the result bookkeeping and the logging would be copied, which Principles IX and XIII both forbid. Behaviour for PDF imports is unchanged, and the existing PDF tests are the safety net for the refactor. They must pass before and after the move, with no test edits beyond construction.

**Alternative rejected**: implementing `IPackingSlipParser` for the API, as its doc comment once suggested. It is a stream-in contract built around `PageNumbers`, summary pages and slicing. An API feed has none of these, so forcing it through would leave meaningless fields and a fake `Stream`.

## 9. Rate limit: 300 calls a minute (FR-021)

**Decision**: Add a singleton `TcgplayerRateLimiter` that keeps a sliding 60-second log of call timestamps, using `TimeProvider`. It sits in the TCGplayer `HttpClient` pipeline as a `DelegatingHandler`, so **every** request is counted, including token and catalog calls. When the window is full, a request waits until the oldest call leaves the window. The budget is `Tcgplayer:CallsPerMinute`, defaulting to **120** per environment.

**Rationale**:
- Production and stage each run at most one replica (019 runbook: max replicas 1), so an in-process limiter is the whole of each environment's traffic. Both environments may use the same TCGplayer account, so 120 each keeps the combined peak at 240, under 300 with margin.
- At 120 a minute, a 200-order backlog takes about 2 minutes, which SC-002 allows. A typical 50-order day takes about 56 calls, which fits in one window and so is not throttled at all (SC-001).
- A sliding log is exact. A fixed window would allow a 2× burst across the minute boundary.

**Why a small custom class rather than `System.Threading.RateLimiting.SlidingWindowRateLimiter`**: the built-in limiter approximates with segments and takes no `TimeProvider`. A unit test could not prove "never more than N in any 60-second window" without waiting in real time. The custom class is about 40 lines, isolates one external constraint, and is directly testable (Principle XIII: a concrete purpose).

## 10. Failure model (FR-014, FR-015)

**Decision**: The Infrastructure feed turns HTTP outcomes into a typed `TcgplayerFeedException` with a `TcgplayerFeedFailure` value:

| Condition | Failure | Scope | Employee sees |
|---|---|---|---|
| Unreachable, timeout, 5xx, 429 | `Unavailable` | Attempt: stops the import | "Couldn't reach TCGplayer. Try again later, or upload a packing slip." |
| 401 after one token refresh, 403, token request rejected | `AccessRefused` | Attempt | "TCGplayer refused the store's connection. A manager needs to check it. You can upload a packing slip meanwhile." |
| Body unreadable, required fields missing, configured status not in manifest, paging stalled | `ResponseInvalid` | Attempt if it affects search or manifest; **order** if it affects one order's items | A specific reason |
| Credentials not configured | `NotConfigured` | Attempt, before any call | "Getting orders from TCGplayer isn't set up here. Use packing-slip upload." |

These map to new `FailureType` values: `TcgplayerUnavailable`, `TcgplayerAccessRefused`, `TcgplayerResponseInvalid`, `TcgplayerNotConfigured` (attempt-wide), and `IncompleteOrder` (per order). The service catches the typed exception, never message text (Principle XII).

Orders already committed stay committed, and pressing the button again resumes. Already-imported orders are reported as duplicates, and their items are not fetched again (FR-013).

A `429` is treated as `Unavailable`, not retried. Seeing one would mean the limiter is wrong, and a fresh press is the safe retry.

**Missing configuration** is a runtime state, not a startup failure. Development, CI and E2E environments have no real keys, and the PDF path must keep working there.

## 11. User-Agent and application version (FR-022)

**Decision**:
- Every TCGplayer request carries `User-Agent: LootSinglesFulfillment/{version} (Loot Investments LLC)`.
- `{version}` is the assembly's `InformationalVersion`. The Dockerfile gains `ARG APP_VERSION=0.0.0-local`, passed to `dotnet publish -p:InformationalVersion=$APP_VERSION`.
- `deploy-stage.yml` passes `APP_VERSION=1.0.0+{short sha}` as a build argument. Production deploys the image stage built, so it carries the same version.
- Any SourceLink `+hash` suffix is trimmed so the header stays one clean token.

**Rationale**: The addendum requires an **accurate** version. A hardcoded "1.0" would go stale. The commit SHA identifies exactly what is running and needs no manual bumping.

## 12. Secrets in deployment (FR-020, FR-023)

**Decision**:
- **Deployed environments:** `Tcgplayer__PublicKey`, `Tcgplayer__PrivateKey` and `Tcgplayer__AccessToken` become **Container Apps secrets** referenced by environment variables (`secretref:`), set by hand in the portal like every other setting.
- **Local development:** the same three keys go in `dotnet user-secrets` under the API project.
- **Documentation:** the 019 runbook gains a step, and 019's "Secrets contract: there are none" is updated by this feature, because these are the repository's first deployment secrets.
- **Repository guard:** the existing credential-pattern scan (`DeploymentConfigurationTests`) gains `PrivateKey`, `client_secret` and `X-Tcg-Access-Token` assignment patterns.

**Rationale**: This matches the existing hand-configured, no-Key-Vault deployment (019). Key Vault would add cost and moving parts for three values.

**Stage**: by default, stage gets the same three secrets so the feature can be verified before production. It pulls the same open orders read-only, stores no customer data, and its rate budget is accounted for in §9. If the Product Owner prefers stage unconfigured, it shows `NotConfigured` and nothing else changes.

## 13. Card images per order source (FR-019)

**Decision**: `Order` gains `ImportSource` (`PackingSlipPdf` = 0, the default for existing rows, and `TcgplayerApi` = 1). In `OrdersService.GetByIdAsync`, API orders take `ImageUrl` from the stored line value and **never** call `CardImageEnrichmentService`. PDF orders behave exactly as today.

**Rationale**:
- Enrichment today is chosen only by game. Without a source marker, an API line with no image would be sent to Scryfall, TCGdex or Lorcast, which §4a forbids.
- Storing the URL at import is better than looking it up on every view. Viewing an order then makes no TCGplayer call, so picking never depends on the API being up (Principle XI) and costs no rate budget.
- The spec's assumption that images would be fetched at view time explicitly left this to planning. That assumption is revised here.

**Images in the browser**: TCGplayer-hosted images load from TCGplayer's own CDN, which keeps the data with TCGplayer. The app sets no content security policy, so nothing blocks them.

## 14. Live verification: a read-only probe before implementation, then the first stage run

The calls are read-only. The only `POST` is `/token`, which exchanges the existing keys for a bearer token and creates nothing. The team therefore agreed on 2026-10-08 to use the real keys, on stage and in an exploratory probe, **before** implementation starts.

The probe is `probe/Probe-Tcgplayer.ps1`. A **person** runs it. It makes about 10 calls and writes a local report outside the repository. The report shows response **shapes** with values hidden, plus only the reference vocabulary below: status names, `extendedData` field names, condition and printing wording, and three counts per sampled order. It never writes keys, tokens, order numbers, customer fields, product names or prices.

The person reads the report and records the answers **in words** in this section. The report is never pasted into an AI tool (§4f). Any assumption it disproves is corrected in this plan before `/speckit-tasks`.

**Probe result, 2026-10-08** (reported in words by the person who ran it), run with the public and private keys only and no store access token:

- `POST /token` returned 200, so the existing keys produce a bearer token.
- `GET /stores/self` returned 200 with a store key, so the keys are linked to a store.
- `GET /stores/{storeKey}/orders/manifest` returned **403**, with the error "request forbidden".

**Authorization attempt, 2026-10-08**: approving the app at `store.tcgplayer.com/admin/Apps/{PublicKey}` hung. The page's `authorizeapplication` request stayed pending and never returned a code. The problem is on TCGplayer's side, and it was raised with TCGplayer. Implementation goes ahead meanwhile against synthetic data. The probe-dependent items (open status name, `extendedData` names, `productCount` meaning, condition and printing wording) are tracked in `tasks.md` as needing live confirmation.

**After store authorization, 2026-10-08**: the authorization completed, and the store access token was saved and sent (`X-Tcg-Access-Token` supplied: true). The diagnostic calls then gave:

| Call | Result |
|---|---|
| Catalog categories | 200 |
| `/stores/self` (versioned and unversioned) | 200 |
| `/stores/{storeKey}` store info (versioned and unversioned) | 200 |
| Order manifest and order search (versioned and unversioned) | **403**, "Request forbidden." |

The path form is not the cause. Store info and `/stores/self` also worked **before** authorization, so they don't prove the access token took effect. Two explanations remain:
- **(a)** the authorization isn't being applied, for example because `/stores/self` returns a store other than the one authorized;
- **(b)** the application lacks permission to read orders.

**Comparison, 2026-10-08**: the store being queried is "Loot Card Shop", and the store key is the same with and without the access token, so explanation (a) is ruled out. The keys-only bearer token also gets 403 on the manifest, so the access token makes no observable difference to order access. The remaining cause is (b): the application has no order permission. **Authorization method verified, 2026-10-08.**

- `Check-TcgplayerAccess.ps1` found that 47 of the 68 read-only endpoints return 403. The blocked set includes orders, catalog product and SKU details, all pricing, store inventory, buylist and customers.
- `Test-TcgplayerAuthVariants.ps1` tried every reported token flow:
  - the documented `/token` with `X-Tcg-Access-Token`;
  - the versioned `/v1.39.0/token`;
  - the header sent on the order call itself;
  - the flow from TCGplayer's official Postman collection (github.com/TCGplayer/Postman-Api).

  Every variant obtained a token and still got 403 on orders.
- `/token/access`, which one open-source client uses, returns 405 because it doesn't exist.

The calls match TCGplayer's own reference, so the 403s are application permissions. That fits the addendum's §4(a) purposes ("inventory synchronization, pricing, catalog management, and related functionality"), which never mention orders. Only TCGplayer can grant it, and the questions were sent to them.

**Implementation is paused**: the whole feature depends on reading orders, so nothing is built until TCGplayer confirms it will enable order access for this use.

The keys alone cannot read store orders. The next step is the Store Authorization Workflow (§1), done once by a person. The probe is then re-run with `TCGPLAYER_ACCESS_TOKEN` set, to verify steps 3–8 before `/speckit-tasks`.

An earlier draft of this section blocked the feature pending TCGplayer's confirmation. That draft assumed Loot had a pre-2025 connection to protect, but Loot's keys are new, issued in 2026, so there was none to protect and the block was removed.

The probe settles, and the stage run re-confirms:

1. The token request succeeds with the existing keys plus the access token, and no new authorization was created.
2. `/stores/self` returns the store key, if `StoreKey` was left unset.
3. The manifest contains a status named exactly "Ready to Ship".
4. Whether `productCount` counts units or lines (§5).
5. The `extendedData` names for the collector number and rarity (§6).
6. Condition and printing text maps to the expected Condition and Variant on a few known orders, checked against the seller portal.
7. Images on API-imported lines show the right card.
8. The import logs show the call count, staying within the budget.

## 15. Testing strategy (FR-024, Principle IV)

- **Fixtures**: synthetic JSON under `backend/tests/LootSingles.Fixtures/Tcgplayer/`, written by hand from the documented schemas, using invented order numbers, SKUs and names, with a README stating they are synthetic and why (§4f). Order-detail fixtures **include** invented customer and shipping fields, so tests can prove they never persist (SC-004).
- **Unit**: the translator (every field rule in §7, missing number, foil flag), `OrderCandidateValidator`, the rate limiter driven by `FakeTimeProvider`, the token cache and refresh-once rule, and paging (stall guard, short pages).
- **Integration** (Testcontainers SQL, `WebApplicationFactory`): the full HTTP pipeline against a stub primary handler serving the fixtures, covering:
  - end-to-end import;
  - duplicates against PDF-imported orders;
  - concurrent presses;
  - outage mid-run, then resume;
  - access refused;
  - not configured;
  - no customer fields in the database or captured logs;
  - every request carrying the User-Agent;
  - no request to `/app/authorize`;
  - no catalog provider called for API orders;
  - the PDF regression suite unchanged.
- **E2E (Playwright)**: the E2E host registers the same stub handler. "Get new orders" imports the seeded synthetic orders. A picker sees TCGplayer images and "No number" on the line with no number, and the packing desk shows the no-slip guidance.

## 16. PRD amendment

The PRD is amended to **v0.7**:
- **§25 and §40.6:** the API becomes the primary path and PDF the fallback.
- **§28:** API access is granted under the Legacy Qualified Addendum, with its constraints.
- **§27:** a note that API orders carry no slip.
- **§41:** questions 24 and 26 are answered, and 20 and 21 stay open.

The amendment records decisions already made. It adds no requirement.
