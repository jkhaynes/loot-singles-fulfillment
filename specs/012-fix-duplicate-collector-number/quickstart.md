# Quickstart: Fix Duplicate Collector-Number Echo in Product Names

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Prerequisites

- Backend built: `dotnet build backend/LootSingles.sln`
- No database or running server needed — this validation is at the unit-test level

## Validate the fix (unit tests)

Run the extraction test suite:

```powershell
dotnet test backend/tests/LootSingles.UnitTests --filter FullyQualifiedName~OrderLineExtractionTests
```

**Expected**: All tests pass, including the new duplicate-echo case and the new false-positive
boundary case (see research.md §4–5 for the exact input/expected-output pairs).

## Validate no regression (full unit + integration suites)

```powershell
dotnet test backend/LootSingles.sln
```

**Expected**: 100% pass, including the existing Pokémon/Magic/Lorcana fixtures and feature 011's
multi-page fixture's non-duplicated lines (SC-002).

## Validate against the real order (manual, optional end-to-end confirmation)

1. Start the backend and frontend dev servers.
2. Log in and import `backend/tests/LootSingles.Fixtures/PackingSlips/multi-page-order-no-total-on-continuation-pages.pdf`.
3. Open the imported order's picking detail view.
4. Confirm every affected Pokémon line (Fomantis, Manectric, Clefairy, Mega Clefable ex, Hitmontop,
   etc.) displays its real card name with no trailing duplicated collector number, while lines
   like Meowth (`#106/094`) and Blastoise ex (`#030/142`) — which never had the duplicate shape —
   are visually unchanged from before the fix.

**Expected**: SC-001 confirmed visually; this step also gives feature 009's card-image matching a
correct `ProductName` to match against for these lines, though feature 009 itself is out of scope
for this fix.
