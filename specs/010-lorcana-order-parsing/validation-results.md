# Validation Results: Fix Lorcana Order Line Parsing

**Date**: 2026-08-25
**Branch**: `010-lorcana-order-parsing`

## Automated validation

| Gate | Command | Result |
|---|---|---|
| Backend build | `dotnet build backend/LootSingles.sln --no-restore` | PASS — 0 warnings, 0 errors |
| Backend unit tests | `dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj --no-build --no-restore` | PASS — 75 passed, 0 failed |
| SQL Server integration tests | `dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj --no-build --no-restore` | PASS — 105 passed, 0 failed |
| C# formatting | `dotnet csharpier check backend` | PASS — 130 files checked |

## Manual validation (quickstart.md)

**Performed directly by the Developer — PASS.** Run against the app connected to the Azure
`loot-singles-dev` database (via `LootSingles.Api`'s User Secrets override of
`ConnectionStrings:LootSingles`, not local LocalDB). All quickstart.md scenarios confirmed working:
the real Lorcana sample imports and displays correctly, Pokémon import remains unaffected, and
genuinely malformed data is still rejected.

## Architecture and Changeability Review

| Review dimension | Assessment | Result |
|---|---|---|
| Responsibility | `OrderLineExtractor.Extract` gained exactly one fallback branch in its `Set`/`ProductName` extraction step. Segment splitting, collector-number detection, rarity extraction, and condition/variant parsing are byte-for-byte unchanged, confirmed by re-reading the full method. | PASS |
| Boundaries | The fix stays entirely inside the one method that already owned this responsibility — no new class, service, or layer. | PASS |
| Dependency direction | Unchanged — no new dependency introduced. | PASS |
| Foreseeable variation | The fallback is triggered by text shape (no colon found), not by checking `ProductLine == "Lorcana TCG"` — confirmed by inspection, no game-name string appears anywhere in the new branching logic — so it generalizes to any future game with a similar format without another branch. | PASS |
| Extension cost | A hypothetical next game with a similar no-colon format would need no code change at all, since the fallback already handles that shape generically. | PASS |
| Failure modeling | The still-ambiguous case (single segment, no colon) continues to return `Invalid(FailureType.MissingProductName, ...)` — no new failure type was introduced, and this exact path now has its own dedicated test (T009). | PASS |
| Domain integrity | No new domain concept; `OrderLine` is constructed identically to before, just with correctly-computed `Set`/`ProductName` values for the new shape. | PASS |
| Testability | Verified via the existing unit-test file with no new test infrastructure — a stub `HttpMessageHandler` or mocking library was never needed since this is pure string parsing. | PASS |
| Abstraction rationale | No new abstraction was introduced. | PASS |
| Simplicity | One conditional branch, reusing already-computed values (`segments`, `collectorIndex`). Matches plan.md's design exactly. | PASS |

### Review outcome

No Must Fix or Advisory findings were identified. No remediation tasks were appended to
`tasks.md`. The implementation remains a pure parsing-logic correction with no schema change, no
new failure mode, and no production logging added — consistent with research.md §4 and the
precedent established by routine, no-new-failure-mode changes elsewhere in this project.
