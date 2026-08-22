using LootSingles.FixtureGenerator;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

QuestPDF.Settings.License = LicenseType.Community;

var outputDir =
    args.Length > 0
        ? Path.GetFullPath(args[0])
        : Path.GetFullPath(
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "tests",
                "LootSingles.Fixtures",
                "PackingSlips"
            )
        );

if (!Directory.Exists(outputDir))
{
    throw new DirectoryNotFoundException(
        $"Fixture output directory not found: {outputDir}. Pass the target directory as an argument "
            + "if you're not running via 'dotnet run' from the LootSingles.FixtureGenerator project folder."
    );
}

// Reusable field values that mirror the valid packing-slip fixtures (duplicate-product-line-same-order.pdf,
// valid-multi-order-batch.pdf) so only the field each fixture is deliberately breaking differs.
const string validSet = "SV: Black Bolt";
const string validProductName = "Genesect ex";
const string validCollectorNumber = "#067/086";
const string validRarity = "Double Rare";
const string validCondition = "Near Mint";

string BuildDescription(
    string set,
    string productName,
    string collectorNumber,
    string rarity,
    string condition
) => $"Pokemon - {set}: {productName} - {collectorNumber} - {rarity} - {condition}";

var fixtures = new (string FileName, PackingSlipDocument Document)[]
{
    (
        "large-200-order-batch.pdf",
        new PackingSlipDocument(
            Enumerable
                .Range(1, 200)
                .Select(index => new OrderPageSpec(
                    $"LARGE-BATCH-{index:D3}",
                    [
                        new ProductLineSpec(
                            "1",
                            BuildDescription(
                                validSet,
                                $"{validProductName} {index:D3}",
                                validCollectorNumber,
                                validRarity,
                                validCondition
                            ),
                            "$1.00",
                            "$1.00"
                        ),
                    ]
                ))
                .ToArray()
        )
    ),
    (
        "invalid-quantity.pdf",
        new PackingSlipDocument([
            new OrderPageSpec(
                "INVALID-QUANTITY-FIXTURE",
                [
                    new ProductLineSpec(
                        "0",
                        BuildDescription(
                            validSet,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "$0.00"
                    ),
                    new ProductLineSpec(
                        "-1",
                        BuildDescription(
                            validSet,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "-$1.00"
                    ),
                    new ProductLineSpec(
                        "abc",
                        BuildDescription(
                            validSet,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "$0.00"
                    ),
                ]
            ),
        ])
    ),
    (
        "missing-set.pdf",
        new PackingSlipDocument([
            new OrderPageSpec(
                "MISSING-SET-FIXTURE",
                [
                    // Set left empty (only the separating colon remains) so OrderLineExtractor parses an empty Set.
                    new ProductLineSpec(
                        "1",
                        BuildDescription(
                            string.Empty,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "$1.00"
                    ),
                ]
            ),
        ])
    ),
    (
        "missing-collector-number.pdf",
        new PackingSlipDocument([
            new OrderPageSpec(
                "MISSING-COLLECTOR-FIXTURE",
                [
                    // No "#..." segment anywhere, so OrderLineExtractor can't find a collector-number segment at all.
                    new ProductLineSpec(
                        "1",
                        $"Pokemon - {validSet}: {validProductName} - {validRarity} - {validCondition}",
                        "$1.00",
                        "$1.00"
                    ),
                ]
            ),
        ])
    ),
    (
        "missing-product-name.pdf",
        new PackingSlipDocument([
            new OrderPageSpec(
                "MISSING-PRODUCT-FIXTURE",
                [
                    // Product name left empty after the last colon in the set/product segment.
                    new ProductLineSpec(
                        "1",
                        BuildDescription(
                            validSet,
                            string.Empty,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "$1.00"
                    ),
                ]
            ),
        ])
    ),
    (
        "missing-condition.pdf",
        new PackingSlipDocument([
            new OrderPageSpec(
                "MISSING-CONDITION-FIXTURE",
                [
                    // "Unknown" isn't in ConditionVariantParser.KnownConditions, so Condition resolves to null.
                    new ProductLineSpec(
                        "1",
                        BuildDescription(
                            validSet,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            "Unknown"
                        ),
                        "$1.00",
                        "$1.00"
                    ),
                ]
            ),
        ])
    ),
    (
        "summary-mismatch.pdf",
        new PackingSlipDocument(
            [
                new OrderPageSpec(
                    "SUM-ACTUAL-IDX",
                    [
                        new ProductLineSpec(
                            "1",
                            BuildDescription(
                                validSet,
                                validProductName,
                                validCollectorNumber,
                                validRarity,
                                validCondition
                            ),
                            "$1.00",
                            "$1.00"
                        ),
                    ]
                ),
            ],
            summaryOrderIdentifiers: ["SUM-DIFFERENT-IDX"]
        )
    ),
    (
        "partial-batch-one-bad-order.pdf",
        new PackingSlipDocument([
            new OrderPageSpec(
                "PARTIAL-BATCH-VALID-1",
                [
                    new ProductLineSpec(
                        "1",
                        BuildDescription(
                            validSet,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "$1.00"
                    ),
                ]
            ),
            // No identifier after "Order Number:" -> ExtractOrderIdentifier returns null -> MissingOrderIdentifier.
            new OrderPageSpec(
                null,
                [
                    new ProductLineSpec(
                        "1",
                        BuildDescription(
                            validSet,
                            validProductName,
                            validCollectorNumber,
                            validRarity,
                            validCondition
                        ),
                        "$1.00",
                        "$1.00"
                    ),
                ]
            ),
            new OrderPageSpec(
                "PARTIAL-BATCH-VALID-2",
                [
                    new ProductLineSpec(
                        "2",
                        BuildDescription(
                            validSet,
                            "Mewtwo ex",
                            "#068/086",
                            validRarity,
                            "Lightly Played"
                        ),
                        "$2.00",
                        "$4.00"
                    ),
                ]
            ),
        ])
    ),
};

foreach (var (fileName, document) in fixtures)
{
    var path = Path.Combine(outputDir, fileName);
    document.GeneratePdf(path);
    Console.WriteLine($"Wrote {path}");
    Verify(path);
}

static void Verify(string path)
{
    using var stream = File.OpenRead(path);
    using var pdf = PdfDocument.Open(stream);
    foreach (var page in pdf.GetPages())
    {
        var words = page.GetWords().ToList();
        var lines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3) * 3)
            .OrderByDescending(g => g.Key);
        Console.WriteLine($"  page {page.Number}:");
        foreach (var line in lines)
        {
            var text = string.Join(" ", line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
            Console.WriteLine($"    {text}");
        }
    }
}
