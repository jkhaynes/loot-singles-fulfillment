# Quickstart: Validating TCGplayer API Order Import

**Feature**: `020-tcgplayer-api-import`

This guide proves the feature works. Parts A to C need no TCGplayer access: they use the synthetic fixtures and the stub handler. Part D is the first real run, which **a person** performs. Under the Legacy Addendum §4f, no AI tool may make live TCGplayer calls or read their responses.

## Prerequisites

- .NET 10 SDK, Node 24, Docker running (Testcontainers SQL Server)
- `npm ci --prefix frontend`

## 0. Read-only probe, before implementation (a person)

Run this before `/speckit-tasks`, so the response shapes the plan assumes are confirmed against the real API rather than only the documentation.

```powershell
$env:TCGPLAYER_PUBLIC_KEY = '<existing public key>'
$env:TCGPLAYER_PRIVATE_KEY = '<existing private key>'
$env:TCGPLAYER_ACCESS_TOKEN = '<existing store access token>'
./specs/020-tcgplayer-api-import/probe/Probe-Tcgplayer.ps1
Remove-Item Env:TCGPLAYER_PUBLIC_KEY, Env:TCGPLAYER_PRIVATE_KEY, Env:TCGPLAYER_ACCESS_TOKEN
```

Leave the variables unset to be prompted with hidden input instead. The report goes to your temp folder (`tcgplayer-probe-report.txt`). Read it yourself, then record the answers to research.md §14 **in words**. Don't paste the report into an AI tool, an issue or a commit.

## A. Automated tests

PowerShell commands:

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj --no-build
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj --no-build
npm --prefix frontend run test
npm --prefix frontend run build
npm --prefix frontend run lint
```

Expected: everything passes, **including the unchanged PDF import suite**. That suite is the regression proof for the import-core extraction (research.md §8).

Spot-check that these integration scenarios exist and pass (research.md §15):

| Scenario | Proves |
|---|---|
| Import of the synthetic open orders | US1, FR-001 to FR-012 |
| An order already imported by PDF is reported as a duplicate, and its items are not fetched | FR-010 |
| Two concurrent presses: each order exists exactly once | FR-010, SC-003 |
| Stub fails mid-run → `tcgplayerUnavailable`; the next press imports the rest | FR-013, FR-014 |
| Stub returns 401 twice → `tcgplayerAccessRefused`, with no request to `/app/authorize` | FR-015, FR-020 |
| No secrets configured → `tcgplayerNotConfigured`, with zero outbound requests | research.md §10 |
| Synthetic customer fields appear nowhere in the database or the captured logs | FR-017, SC-004 |
| Every stub-received request carries the User-Agent | FR-022 |
| The rate limiter never allows more than N calls in any 60-second window (unit test, `FakeTimeProvider`) | FR-021, SC-002 |
| Viewing an API order calls no `ICardCatalogProvider` | FR-019, SC-005 |
| A line with no catalog `Number` imports with a null collector number | FR-008 |

## B. End-to-end (Playwright, stub-backed)

```powershell
npm --prefix frontend run test:e2e -- tcgplayer-import
```

The E2E host serves the synthetic fixtures through the stub handler. Expected:

1. Sign in as `e2epicker` and open **Import**. "Get new orders" is the primary action, and packing-slip upload sits below it as the fallback.
2. Press **Get new orders**. Progress shows, then every synthetic order is reported as imported.
3. Press it again. The result reads **No new orders**.
4. Open an imported order. Lines show TCGplayer images. The line seeded without a number shows **"No number"**, and the quantity-greater-than-one line is emphasised.
5. Pick the order through to Picked. At the packing desk, scanning it shows "No packing slip is stored for this order. Print it from TCGplayer…" with the order number, and it can be recorded as packed.

## C. Local run with the app

Without secrets, "Get new orders" shows the **not set up here** message and PDF upload still works. This is the expected state on any machine without keys.

## D. First real run (a person, not an AI tool)

**Precondition (spec Assumptions):** the keys have been authorized for Loot's store through the Store Authorization Workflow, and the store access token is stored as a secret (research.md §1).

1. Add the three Container Apps secrets and their `secretref:` env vars on **stage** (contracts/configuration.md). Leave `StoreKey` unset the first time.
2. Deploy, sign in on stage, and press **Get new orders**.
3. Work through the **live-verification list** (research.md §14) and record each answer **in words** in `tasks.md`. Do not paste response data, tokens or order contents into any AI tool, issue or commit.
4. If `productCount` or the `extendedData` names differ from the plan's assumptions, record it. Then:
   - an `extendedData` name difference needs only a configuration change;
   - a `productCount` semantics difference needs a translator change through `/speckit-implement`.
5. Repeat on production, after the stage run is clean.

**Rollback note**: rolling back the image after API orders exist leaves the old image unable to read a null `CollectorNumber` on those orders (data-model.md). Prefer fixing forward. If a rollback is unavoidable, PDF orders are unaffected.
