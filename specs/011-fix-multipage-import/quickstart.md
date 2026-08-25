# Quickstart: Validate Multi-Page Packing-Slip Import Fix

This validates that a real multi-page packing slip imports as one complete order with all its
product lines, and that every existing single-page/multi-order import case remains unaffected.

## Prerequisites

- .NET SDK 10.0.x
- A running SQL Server-family database with migrations applied (for the integration suite)
- The new fixture file (built from the provided sanitized sample) present in
  `backend/tests/LootSingles.Fixtures/PackingSlips/multi-page-order-no-total-on-continuation-pages.pdf`

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
```

Expected: `PdfPigPackingSlipParserTests.cs`'s new multi-page case passes, producing exactly one
`RawOrderBlock` for the sample with all 50 product lines from all 3 pages, in source reading order;
the existing 13-order-batch case (`valid-multi-order-batch.pdf`, 13 single-page orders, each still
its own block) and the duplicate-product-line case continue to pass unchanged; the full integration
suite (which exercises the broader import pipeline) remains green.

## Manual validation

1. **Multi-page order imports completely** (US1, FR-001/FR-002, SC-001): run the application
   locally (`dotnet run --project backend/src/LootSingles.Api`), log in, and import the fixture PDF
   (or the original sanitized sample) via Import Orders. Confirm the import succeeds as **one**
   order with no `NoProductLines` rejection for any page, and that the order's detail view
   (features 007/008) shows all 50 lines from the source document.
2. **Existing single-page and multi-order imports are unaffected** (US2, FR-003/FR-006, SC-002):
   import `valid-multi-order-batch.pdf` and confirm all 13 orders still import as 13 separate
   orders with identical results to before this fix.
3. **Genuinely malformed pages are still rejected** (FR-004, SC-003): import
   `missing-order-identifier.pdf` and `zero-product-lines.pdf` and confirm both are still rejected
   with their existing failure classifications (`MissingOrderIdentifier`, `NoProductLines`
   respectively), not silently accepted or incorrectly merged with an unrelated order.
