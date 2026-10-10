# Implementation Plan: TCGplayer API Order Import

**Branch**: `020-tcgplayer-api-import` | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/020-tcgplayer-api-import/spec.md`

## Summary

A "Get new orders" button pulls the store's open (provisionally Ready To Ship) orders from the TCGplayer Seller API and imports each new one through the **same per-order pipeline** the PDF importer uses. PDF upload stays as the fallback.

That shared pipeline is extracted from `PackingSlipImportService` into source-neutral pieces: `OrderCandidate`, `OrderCandidateValidator`, `OrderImporter` and `ImportAttemptLog`. Duplicate handling, the concurrency race, result bookkeeping and logging therefore exist once, for both sources.

The TCGplayer adapter lives in Infrastructure:
- a typed `HttpClient`;
- a token handler that uses **only the existing keys and access token**;
- a sliding-window rate limiter defaulting to 120 calls a minute per environment;
- a fixed `User-Agent` carrying the business name and the build's version;
- a pure translator from TCGplayer DTOs to candidates.

Collector numbers, rarity and images come from TCGplayer's own catalog, through a SKU → product lookup. Lines with no number import with "No number".

API orders are marked `ImportSource = TcgplayerApi`. Their images are stored at import and served without any third-party lookup. They store no packing slip, and the packing desk's existing no-slip guidance covers them.

Customer fields are never bound into objects. Test data is synthetic. A person, never an AI tool, runs the first live verification.

## Technical Context

**Language/Version**: C# on .NET 10 (backend). TypeScript with React and Vite (frontend).

**Primary Dependencies**:
- Existing: ASP.NET Core, EF Core (SQL Server), `IHttpClientFactory`, `TimeProvider`.
- No new NuGet or npm packages. The rate limiter is a small in-house class (research.md §9).

**Storage**: Azure SQL / SQL Server. One additive migration, `AddTcgplayerApiImport` (data-model.md).

**Testing**:
- Backend: xUnit unit tests, and integration tests on Testcontainers SQL with `WebApplicationFactory`, using a stub primary `HttpMessageHandler` that serves **synthetic** JSON fixtures.
- Frontend: Vitest and React Testing Library.
- End to end: Playwright through the E2E host with the same stub.

**Target Platform**: Linux container on Azure Container Apps, at most one replica per environment. Browser clients on phone and desktop.

**Project Type**: Web application (backend API and SPA, served from one origin).

**Performance Goals**:
- A typical 50-order day imports in under 30 seconds (SC-001), using about 59 calls with no throttling.
- A 200-order backlog takes about 2 minutes at 120 calls a minute (SC-002).

**Constraints** (Legacy Qualified Addendum; `CLAUDE.md`):
- Never more than 300 calls a minute; 120 per environment by default.
- `User-Agent` on every request.
- Existing credentials only, and never `/app/authorize`.
- No API data to third parties or AI tools.
- Secrets only in user-secrets or Container Apps secrets.
- No customer data persisted or logged.

**Scale/Scope**: one store, a handful of employees, tens to low hundreds of orders a day.

All earlier unknowns are resolved in [research.md](research.md). The items that only a live call could settle (status name, `productCount` semantics, `extendedData` names, the store-key field) were confirmed by a person's read-only probe on 2026-10-09 (research.md §14).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment | Status |
|---|---|---|
| I. Product Owner Authority | Decisions 1–4 and three clarifications are recorded in the spec. The open-status set is provisional, so it is made configurable rather than assumed final. The PRD v0.7 amendment records the decisions without adding requirements. | ✅ |
| II. No Invented Requirements | Every component traces to an FR. Language display traces to FR-007. No change detection or polling (excluded by the spec). | ✅ |
| III. Small, Reviewable Changes | The import-core extraction is required by Principle XIII (a second concrete source), not bundled refactoring. It is sequenced first, with the PDF suite unchanged as its proof. | ✅ |
| IV. TDD | Every behaviour in research.md §15 starts as a failing test. The extraction is a refactor under the existing green suite. | ✅ (plan) |
| V. Safe Failure | No partial order is ever created. Incomplete orders are rejected. A transport failure stops the import rather than storing lines with no number. Unknown statuses fail the import. A missing number or image is shown as missing, never guessed. | ✅ |
| VI. Server-Enforced Rules | Duplicate prevention stays on the unique index plus the existing race handling. The rate limit is enforced server-side for all callers. | ✅ |
| VII. Data Minimization and Credentials | DTOs bind no customer fields. Secrets live only in user-secrets or Container Apps secrets. The scan is extended. Fixtures are synthetic. | ✅ |
| VIII. One Responsive Product | The existing import page gains the action. Picking views gain "No number" and language display. One codebase. | ✅ |
| IX. Replaceable Integrations | The adapter translates to source-neutral `OrderCandidate` before shared validation. The provider's quirks stay in `Infrastructure/Tcgplayer`. TCGplayer data stays authoritative. | ✅ |
| X. Single-Business Scope | One store and one credential set. No tenancy. | ✅ |
| XI. Reliability and Observability | Viewing an order never calls TCGplayer. Every failure is typed and visible. One `ILogger` summary per attempt, with `{Source}`, counts (already-imported orders as a count only on API attempts), `{CallCount}` and the failure category. No bodies, tokens or PII. | ✅ |
| XII. Maintainable Design | Separate concerns: transport (client and handlers), translation (pure), orchestration (service), shared validation and persistence (`OrderImporter`). Failures are typed (`TcgplayerFeedFailure`), never matched on message text. | ✅ |
| XIII. Proportional Abstraction | New abstractions, each with a concrete reason (below): one Application port (`ITcgplayerOrderFeed`), one limiter class, two handlers. No generic "order source" framework: the two services stay concrete. | ✅ |
| EF Core standards | `AnyAsync` duplicate check (existing). One `SaveChanges` per order (existing). Projection for read models. Additive migration. | ✅ |

**Post-design re-check**: still passing. No violations, so Complexity Tracking is empty.

## Architecture and Changeability Review

| Component | Responsibility | Depends on | Foreseeable variation and extension cost | Failure modelling | Abstraction rationale |
|---|---|---|---|---|---|
| `OrderCandidate`, `OrderLineCandidate` (Application/Import) | Source-neutral, untrusted order input | Nothing | A third source would produce the same records, so it needs no change here | `RejectedBySource` carries an adapter-level rejection | Needed so validation never sees PDF text or TCGplayer DTOs (IX) |
| `OrderCandidateValidator` (Application/Import) | Business rules for any source | `FailureType` | Rules change in one place for both sources | Typed `FailureType` and message, as today | Replaces the text-bound checks that would otherwise be duplicated |
| `OrderImporter` (Application/Import) | One order's unit of work: validate, check duplicate, create, hook, save, handle race, record result | `IImportPersistence`, validator, `TimeProvider` | The PDF slip attaches through an optional hook. A new source passes no hook. | Existing `UniqueConstraintViolationException` and `OrderPersistenceException` handling, moved unchanged | Moved out of `PackingSlipImportService`. Concrete class, no interface (single implementation). |
| `ImportAttemptLog` (Application/Import) | One completion log per attempt | `ILogger` | Adds `{Source}` | n/a | The existing `LogCompletion`, now shared. Branches once on source (FR-025): API imports report already-imported orders as a count only (`{OrdersAlreadyImported}`, no order numbers), not a failure, so an attempt with no real rejection logs Information; PDF imports are logged exactly as before (006 FR-004) |
| `PackingSlipImportService` (Application/Import) | PDF orchestration: parse, summary check, slice, then `OrderImporter` per block | Parser, slicer, `OrderImporter` | Unchanged behaviour | Unchanged | Shrinks to orchestration (XII) |
| `ITcgplayerOrderFeed` (Application/Import), implemented by `TcgplayerOrderFeed` (Infrastructure/Tcgplayer) | Lists open order numbers, and turns one batch of order numbers into `OrderCandidate`s | Application port only | The status set is configuration. A different TCGplayer version means changing the adapter only. | Throws `TcgplayerFeedException` with a `TcgplayerFeedFailure` value (`NotConfigured`, `Unavailable`, `AccessRefused`, `ResponseInvalid`); per-order problems become `RejectedBySource` | **Port justified**: isolates an external system and lets the service be unit-tested without HTTP (XIII: dependency isolation and testability) |
| `TcgplayerApiImportService` (Application/Import) | API orchestration: create attempt, list, skip existing, fetch new in batches, `OrderImporter` per candidate, stream progress | Feed port, `OrderImporter`, `IImportPersistence` | none | Maps feed failures to attempt `FailureType`s | Concrete. It mirrors the PDF service's `IAsyncEnumerable<ImportProgressUpdate>` contract so the controller can stream either. |
| `TcgplayerApiClient` (Infrastructure/Tcgplayer) | Typed HTTP calls #2–#8 (contracts/tcgplayer-upstream.md), paging, DTOs | `HttpClient` | A version bump is configuration (`ApiVersion`) | HTTP status → `TcgplayerFeedFailure` in one place | Typed client; no interface (only the feed uses it) |
| `TcgplayerOrderTranslator` (Infrastructure/Tcgplayer) | Pure mapping from DTOs to candidates (research.md §7), reusing `ConditionVariantParser` | Application parser | Field-name changes go through options | Missing pieces become null fields or `RejectedBySource(IncompleteOrder, …)` | Pure static, so it can be unit-tested exhaustively |
| `TcgplayerAuthenticationHandler` (Infrastructure/Tcgplayer) | Attaches the bearer token; fetches it (#1) with the existing credentials; refreshes once on 401 | `TcgplayerTokenCache` (singleton), options | none | A second 401 means `AccessRefused` | `DelegatingHandler`, so every call is covered and the client stays unaware of auth |
| `TcgplayerRateLimitHandler` and `TcgplayerRateLimiter` (Infrastructure/Tcgplayer) | Enforce N calls per 60 seconds across the process, waiting when full | `TimeProvider` | The budget is configuration, capped at 150 per environment at startup, so both together stay at or under 300 | Waits; honours cancellation | Concrete singleton that is directly testable (research.md §9). Pipeline order: auth → rate limit → per-attempt timeout (30 s) → primary; `HttpClient.Timeout` is infinite |
| `ImportsController` | Adds `POST /api/imports/tcgplayer`; the NDJSON streaming is shared by both routes | Both services | none | Unchanged streaming error handling | Extracts the existing `StreamSnapshotsAsync` to take any update stream |
| `OrdersService.GetByIdAsync` | Chooses the image source by `Order.ImportSource` | Enrichment service (PDF orders only) | none | API orders never enrich | One branch at the single image read point |
| Frontend import page | Primary "Get new orders", PDF as fallback, new banners | `importApi` with a shared NDJSON reader | none | Banners per `attemptFailureCode`; "No new orders" | The stream reader is extracted from `importPackingSlip` and shared |

**Domain integrity**: TCGplayer JSON becomes DTOs (shape only), then candidates (untrusted), then validated, and only then becomes `Order` or `OrderLine` in `OrderImporter`.

**Simpler design considered**: calling TCGplayer directly from a controller with inline mapping. Rejected: it would duplicate validation and the duplicate race, and leave the rate limit and User-Agent per-call rather than guaranteed.

## Project Structure

### Documentation (this feature)

```text
specs/020-tcgplayer-api-import/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── import-api.md          # POST /api/imports/tcgplayer, order read changes
│   ├── tcgplayer-upstream.md  # Closed list of TCGplayer calls and rules
│   └── configuration.md       # Tcgplayer options and secrets
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
backend/src/
├── LootSingles.Domain/Orders/
│   ├── Order.cs                         # + ImportSource
│   ├── OrderImportSource.cs             # new enum
│   └── OrderLine.cs                     # CollectorNumber nullable; + Language, ImageUrl
├── LootSingles.Application/Import/
│   ├── OrderCandidate.cs                # new (with OrderLineCandidate)
│   ├── OrderCandidateValidator.cs       # new (shared rules)
│   ├── OrderImporter.cs                 # new (moved per-order pipeline)
│   ├── ImportAttemptLog.cs              # new (moved LogCompletion)
│   ├── ITcgplayerOrderFeed.cs           # new port, + TcgplayerFeedException / TcgplayerFeedFailure
│   ├── TcgplayerApiImportService.cs     # new
│   ├── PackingSlipImportService.cs      # shrinks to PDF orchestration
│   ├── OrderLineExtractor.cs            # now yields OrderLineCandidate
│   ├── OrderLineValidator.cs            # retired into extractor and shared validator
│   ├── ImportAttempt.cs                 # + Source
│   └── FailureType.cs                   # + 5 values
├── LootSingles.Application/Orders/OrdersService.cs   # image source by ImportSource
├── LootSingles.Infrastructure/Tcgplayer/              # new folder
│   ├── TcgplayerOptions.cs
│   ├── TcgplayerApiClient.cs  (+ Dtos.cs)
│   ├── TcgplayerOrderFeed.cs
│   ├── TcgplayerOrderTranslator.cs
│   ├── TcgplayerAuthenticationHandler.cs, TcgplayerTokenCache.cs
│   ├── TcgplayerRateLimiter.cs, TcgplayerRateLimitHandler.cs
│   └── TcgplayerServiceCollectionExtensions.cs        # one registration, used by the API and the E2E host
├── LootSingles.Infrastructure/Persistence/
│   ├── Configurations/ (Order, OrderLine, ImportAttempt)
│   └── Migrations/<timestamp>_AddTcgplayerApiImport.cs
└── LootSingles.Api/
    ├── Controllers/ImportsController.cs              # + POST tcgplayer, shared streaming
    ├── Program.cs                                    # registration via extension
    └── appsettings.json                              # Tcgplayer non-secret defaults

backend/tests/
├── LootSingles.Fixtures/Tcgplayer/*.json + README.md  # synthetic only
├── LootSingles.UnitTests/Import/ and Tcgplayer/
├── LootSingles.IntegrationTests/Import/ and ImportUi/ (Tcgplayer*Tests.cs)
└── LootSingles.E2EHost/Program.cs                     # stub TCGplayer handler + fake keys

frontend/src/features/
├── import/importApi.ts, ImportPage.tsx (+ .css)        # Get new orders, shared stream reader
└── orders/FocusedPickView.tsx, OrderDetailPage.tsx, OrderFinish.tsx  # "No number", language
frontend/e2e/tcgplayer-import.spec.ts

Dockerfile                                   # ARG APP_VERSION → InformationalVersion
.github/workflows/deploy-stage.yml           # pass APP_VERSION build arg
docs/prd/Loot_Singles_Fulfillment_PRD_v0.7.md  # amendment (research.md §16)
specs/019-automated-deployment/quickstart.md, contracts/deployment.md  # first secrets
README.md                                    # TCGplayer Integration section rewritten
```

**Structure Decision**: Use the existing four-project backend (Domain, Application, Infrastructure, Api) and the existing frontend feature folders. All TCGplayer-specific code sits in `Infrastructure/Tcgplayer`. Registration goes through **one extension method**, because the E2E host hand-copies the API's DI today, and a second copy of the handler pipeline could drift and drop the User-Agent or the limiter.

## Sequencing Notes for `/speckit-tasks`

0. **Done 2026-10-09. Before tasks: a person runs the read-only probe** (quickstart §0) and records what it finds in research.md §14. Any assumption it disproves (status name, `productCount` meaning, `extendedData` names, store-key field, condition and printing wording) is corrected in this plan first. Synthetic fixtures are then written to match the confirmed shapes, still with invented values.
1. **Extract the import core, with no behaviour change.** The PDF suite must stay green throughout. This lands before any TCGplayer code.
2. **Make the schema changes and migration** (`ImportSource`, nullable `CollectorNumber`, `Language`, `ImageUrl`, `ImportAttempt.Source`, `FailureType` values).
3. **Build the agreement guards test-first:** limiter, User-Agent, token handler (existing credentials, refresh once, never `/app/authorize`), and options validation (150-per-environment cap, not-configured state).
4. **Add the translator and client with synthetic fixtures**, then the feed.
5. **Add `TcgplayerApiImportService`, the controller route and the integration scenarios** (quickstart A).
6. **Make the image source follow `ImportSource`**, and show "No number" and language in the picking views.
7. **Change the frontend import page**, then the E2E stub and the Playwright flow.
8. **Version build argument, secrets runbook, 019 contract update, PRD v0.7, README.**
9. **Manual: the live-verification run** (quickstart D), performed by a person on stage, then production. The credentials precondition is met (research.md §14).

## Complexity Tracking

No constitution violations to justify.
