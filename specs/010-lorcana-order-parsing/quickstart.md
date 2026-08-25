# Quickstart: Validate Lorcana Order Line Parsing Fix

This validates that a real Lorcana packing-slip line imports successfully with correct fields,
and that Pokémon import remains completely unaffected.

## Prerequisites

- .NET SDK 10.0.x
- A running SQL Server-family database with migrations applied (for the integration suite)
- The new fixture file (built from the provided sanitized sample) present in
  `backend/tests/LootSingles.Fixtures/PackingSlips/`

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
```

Expected: `OrderLineExtractionTests.cs`'s new/replaced Lorcana case passes, producing exactly
`Set = "Disney Lorcana Promo Cards"`, `ProductName = "Scrooge McDuck - S.H.U.S.H. Agent"`,
`CollectorNumber = "#36"`, `Rarity = "Promo"`, `Condition = "Near Mint"`, `Variant = "Holofoil"`;
every existing Pokémon case in the same file continues to pass unchanged; the full integration
suite (which exercises the broader import pipeline, including the existing Pokémon fixture PDFs)
remains green.

## Manual validation

1. **Real Lorcana line imports successfully** (US1, FR-001, SC-001): run the application locally
   (`dotnet run --project backend/src/LootSingles.Api`), log in, and import the fixture PDF (or the
   original sanitized sample) via Import Orders. Confirm the import succeeds with no rejection for
   that line, and that Browse Orders / the order's detail view (features 007/008) show the correct
   set, card name (with its internal " - " intact), collector number, rarity, and condition.
2. **Pokémon import is unaffected** (US2, FR-002, SC-002): import an existing Pokémon fixture PDF
   (e.g. `valid-multi-order-batch.pdf`) and confirm every line still imports with identical results
   to before this fix.
3. **Genuinely malformed data is still rejected** (FR-003, SC-003): import a fixture with a
   deliberately malformed product line (e.g. `missing-collector-number.pdf`) and confirm it is
   still rejected with the correct existing failure classification, not silently accepted.
