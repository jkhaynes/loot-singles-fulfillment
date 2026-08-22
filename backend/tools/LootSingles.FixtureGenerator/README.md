# LootSingles.FixtureGenerator

Generates the packing-slip PDF fixtures under `backend/tests/LootSingles.Fixtures/PackingSlips/`
that `PdfPigPackingSlipParser` is tested against.

Each fixture is one or more order pages (Ship To block, `Order Number:` line, a
Quantity/Description/Price/Total Price table with a trailing `<N> Total` row) followed by a
summary page listing every valid order identifier as its own word token — this is the layout
`PdfPigPackingSlipParser.ExtractProductLines` and `ExtractOrderIdentifier` rely on. Use this tool
instead of hand-authoring or hand-patching fixture PDFs: a fixture missing the `Total` row, or with
a corrupted trailer, fails in ways that are easy to misdiagnose as a parser bug (see the 2026-08-21
fix that regenerated 6 fixtures which had exactly these problems).

`large-200-order-batch.pdf` is the deterministic production-pipeline scale fixture. It contains
exactly 200 synthetic orders with one valid product line each and is used to verify the real PDF
parser, import service, repository, database, and HTTP streaming path together.

## Usage

```powershell
cd backend/tools/LootSingles.FixtureGenerator
dotnet run
```

Regenerates every fixture defined in `Program.cs` directly into
`backend/tests/LootSingles.Fixtures/PackingSlips/` (resolved relative to this project folder) and
prints a word-position dump of each generated PDF for a quick sanity check. Pass a directory as an
argument to write elsewhere instead, e.g. `dotnet run -- C:\scratch\preview`.

## Adding a new fixture

Add an entry to the `fixtures` array in `Program.cs` using `OrderPageSpec`/`ProductLineSpec`. Reuse
the `validSet`/`validProductName`/etc. constants and only change the field you're deliberately
breaking, so the fixture's intent stays obvious from the diff. Pass `summaryOrderIdentifiers` when
the fixture deliberately needs its summary page to disagree with its valid order pages. Run the
tool, then run the affected tests — `dotnet test` from `backend/`.

Sanitization requirement from `backend/tests/LootSingles.Fixtures/README.md` still applies: don't
put real customer PII into a fixture spec.
