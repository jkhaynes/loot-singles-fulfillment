using LootSingles.Application.Import;
using LootSingles.Domain.Orders;
using LootSingles.Infrastructure.Import;
using LootSingles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.IntegrationTests.Import;

public class ValidImportTests
{
    private static readonly string[] ExpectedFixtureLines =
    [
        "F0000001-ABC001-00001|2|Pokemon|SV: Black Bolt|Genesect ex|#067/086|Double Rare|Near Mint|Holofoil|Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint Holofoil",
        "F0000002-ABC002-00002|1|Magic|The Hobbit: Eternal-Legal|Galadriel's Dismissal (Borderless)|#16|R|Near Mint|Foil, (Borderless)|Magic - The Hobbit: Eternal-Legal: Galadriel's Dismissal (Borderless) - #16 - R - Near Mint Foil",
        "F0000003-ABC003-00003|1|Magic|Marvel Universe Eternal-Legal|Heroic Intervention (0080) (Borderless)|#80|M|Near Mint|(0080), (Borderless)|Magic - Marvel Universe Eternal-Legal: Heroic Intervention (0080) (Borderless) - #80 - M - Near Mint",
        "F0000003-ABC003-00003|1|Magic|Commander Legends|Colfenor, the Last Yew|#274|R|Lightly Played|Foil|Magic - Commander Legends: Colfenor, the Last Yew - #274 - R - Lightly Played Foil",
        "F0000004-ABC004-00004|4|Magic|Commander: FINAL FANTASY|Y'shtola, Night's Blessed|#7|M|Near Mint|Foil|Magic - Commander: FINAL FANTASY: Y'shtola, Night's Blessed - #7 - M - Near Mint Foil",
        "F0000005-ABC005-00005|1|Magic|The Hobbit|My Precious (Showcase) (Surge Foil)|#271|R|Near Mint|Foil, (Showcase), (Surge Foil)|Magic - The Hobbit: My Precious (Showcase) (Surge Foil) - #271 - R - Near Mint Foil",
        "F0000006-ABC006-00006|3|Magic|The Hobbit|Belladonna Took (Showcase)|#214|R|Near Mint|(Showcase)|Magic - The Hobbit: Belladonna Took (Showcase) - #214 - R - Near Mint",
        "F0000006-ABC006-00006|1|Magic|Tarkir: Dragonstorm|Dalkovan Encampment (Borderless)|#394|R|Near Mint|(Borderless)|Magic - Tarkir: Dragonstorm: Dalkovan Encampment (Borderless) - #394 - R - Near Mint",
        "F0000007-ABC007-00007|1|Magic|Universes Beyond: Fallout|Screeching Scorchbeast|#49|R|Near Mint||Magic - Universes Beyond: Fallout: Screeching Scorchbeast - #49 - R - Near Mint",
        "F0000007-ABC007-00007|2|Magic|Universes Beyond: Fallout|Tato Farmer|#86|R|Near Mint||Magic - Universes Beyond: Fallout: Tato Farmer - #86 - R - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Captain Marvel, Shooting Star|#589|M|Near Mint||Magic - Commander: Marvel Super Heroes: Captain Marvel, Shooting Star - #589 - M - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Doctor Doom, Unrivaled|#654|M|Near Mint||Magic - Commander: Marvel Super Heroes: Doctor Doom, Unrivaled - #654 - M - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Hyperion, Supreme Hero|#599|M|Near Mint||Magic - Commander: Marvel Super Heroes: Hyperion, Supreme Hero - #599 - M - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Immortus, Master of Eternity|#623|M|Near Mint||Magic - Commander: Marvel Super Heroes: Immortus, Master of Eternity - #623 - M - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Nico Minoru, Runaway|#700|M|Near Mint||Magic - Commander: Marvel Super Heroes: Nico Minoru, Runaway - #700 - M - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Return of the Mole Man|#729|M|Near Mint||Magic - Commander: Marvel Super Heroes: Return of the Mole Man - #729 - M - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Time Warp|#788|R|Near Mint||Magic - Commander: Marvel Super Heroes: Time Warp - #788 - R - Near Mint",
        "F0000008-ABC008-00008|1|Magic|Commander: Marvel Super Heroes|Wolverine, Claws Out|#737|R|Near Mint||Magic - Commander: Marvel Super Heroes: Wolverine, Claws Out - #737 - R - Near Mint",
        "F0000009-ABC009-00009|1|Magic|Commander: Marvel Super Heroes|Doctor Doom, Unrivaled|#654|M|Near Mint||Magic - Commander: Marvel Super Heroes: Doctor Doom, Unrivaled - #654 - M - Near Mint",
        "F0000010-ABC010-00010|1|Magic|Modern Horizons 3|Orim's Chant|#265|R|Moderately Played||Magic - Modern Horizons 3: Orim's Chant - #265 - R - Moderately Played",
        "F0000011-ABC011-00011|1|Magic|The Hobbit|Elrond, Moon-Reader (Extended Art)|#290|M|Near Mint|(Extended Art)|Magic - The Hobbit: Elrond, Moon-Reader (Extended Art) - #290 - M - Near Mint",
        "F0000011-ABC011-00011|1|Magic|The Hobbit: Eternal-Legal|Raise the Palisade (Borderless)|#18|M|Near Mint|(Borderless)|Magic - The Hobbit: Eternal-Legal: Raise the Palisade (Borderless) - #18 - M - Near Mint",
        "F0000011-ABC011-00011|1|Magic|The Hobbit|Thranduil, Sindarin Liege (Showcase) (Surge Foil)|#269|U|Near Mint|Foil, (Showcase), (Surge Foil)|Magic - The Hobbit: Thranduil, Sindarin Liege (Showcase) (Surge Foil) - #269 - U - Near Mint Foil",
        "F0000012-ABC012-00012|4|Magic|Duskmourn: House of Horror|Chainsaw (Showcase)|#314|R|Near Mint|(Showcase)|Magic - Duskmourn: House of Horror: Chainsaw (Showcase) - #314 - R - Near Mint",
        "F0000013-ABC013-00013|1|Lorcana TCG|The First Chapter|John Silver - Alien Pirate|#82/204|Legendary|Near Mint||Lorcana TCG - The First Chapter: John Silver - Alien Pirate - #82/204 - Legendary - Near Mint",
        "DUPLICATE-LINE-FIXTURE|1|Pokemon|SV: Black Bolt|Genesect ex|#067/086|Double Rare|Near Mint|Holofoil|Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint Holofoil",
        "DUPLICATE-LINE-FIXTURE|2|Pokemon|SV: Black Bolt|Genesect ex|#067/086|Double Rare|Near Mint|Holofoil|Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint Holofoil",
    ];

    [Fact]
    public async Task ImportAsync_ValidAndDuplicateLineFixtures_PersistsEveryOrderAndSourceRow()
    {
        await using var dbContext = ImportTestSupport.CreateDatabaseContext();
        var parser = new PdfPigPackingSlipParser();
        IPackingSlipImportService service = new PackingSlipImportService(
            parser,
            new ImportRepository(dbContext),
            NullLogger<PackingSlipImportService>.Instance
        );
        var fixtureNames = new[]
        {
            "valid-multi-order-batch.pdf",
            "duplicate-product-line-same-order.pdf",
        };
        var expectedOrders = new List<RawOrderBlock>();

        foreach (var fixtureName in fixtureNames)
        {
            await using var expectedStream = OpenFixture(fixtureName);
            ParsedPackingSlip? expectedPackingSlip = null;
            await foreach (var update in parser.ParseAsync(expectedStream))
            {
                if (update.IsComplete)
                {
                    expectedPackingSlip = update.PackingSlip;
                }
            }
            expectedOrders.AddRange(
                Assert.IsType<ParsedPackingSlip>(expectedPackingSlip).OrderBlocks
            );

            await using var importStream = OpenFixture(fixtureName);
            await foreach (var _ in service.ImportAsync(importStream, CancellationToken.None)) { }
        }

        var persistedOrders = await dbContext
            .Orders.Include(order => order.OrderLines)
            .OrderBy(order => order.Id)
            .ToListAsync();

        Assert.Equal(expectedOrders.Count, persistedOrders.Count);
        Assert.All(persistedOrders, order => Assert.Equal(OrderStatus.Ready, order.Status));
        Assert.Equal(
            expectedOrders.Select(order => order.OrderIdentifier).Order(),
            persistedOrders.Select(order => order.TcgplayerOrderId).Order()
        );

        var expectedLines = ExpectedFixtureLines.Select(ExpectedLine.Parse).ToList();
        var actualLines = persistedOrders
            .SelectMany(order =>
                order
                    .OrderLines.OrderBy(line => line.Id)
                    .Select(line => new { order.TcgplayerOrderId, Line = line })
            )
            .ToList();

        Assert.Equal(expectedLines.Count, actualLines.Count);
        for (var index = 0; index < expectedLines.Count; index++)
        {
            var expected = expectedLines[index];
            var actual = actualLines[index];
            Assert.Equal(expected.OrderIdentifier, actual.TcgplayerOrderId);
            Assert.Equal(expected.RawDescription, actual.Line.RawDescription);
            Assert.Equal(expected.ProductLine, actual.Line.ProductLine);
            Assert.Equal(expected.ProductName, actual.Line.ProductName);
            Assert.Equal(expected.Set, actual.Line.Set);
            Assert.Equal(expected.CollectorNumber, actual.Line.CollectorNumber);
            Assert.Equal(expected.Rarity, actual.Line.Rarity);
            Assert.Equal(expected.Condition, actual.Line.Condition);
            Assert.Equal(expected.Variant, actual.Line.Variant);
            Assert.Equal(expected.Quantity, actual.Line.Quantity);
        }

        var duplicateLineOrder = Assert.Single(
            persistedOrders,
            order => order.TcgplayerOrderId == "DUPLICATE-LINE-FIXTURE"
        );
        var duplicateLines = duplicateLineOrder.OrderLines.OrderBy(line => line.Id).ToList();
        Assert.Equal(2, duplicateLines.Count);
        Assert.Equal(duplicateLines[0].RawDescription, duplicateLines[1].RawDescription);
        Assert.Equal(new[] { 1, 2 }, duplicateLines.Select(line => line.Quantity));
    }

    private static FileStream OpenFixture(string fixtureName) =>
        File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PackingSlips", fixtureName)
        );

    private sealed record ExpectedLine(
        string OrderIdentifier,
        int Quantity,
        string ProductLine,
        string Set,
        string ProductName,
        string CollectorNumber,
        string? Rarity,
        string Condition,
        string? Variant,
        string RawDescription
    )
    {
        public static ExpectedLine Parse(string value)
        {
            var fields = value.Split('|');
            return new ExpectedLine(
                fields[0],
                int.Parse(fields[1]),
                fields[2],
                fields[3],
                fields[4],
                fields[5],
                NullIfEmpty(fields[6]),
                fields[7],
                NullIfEmpty(fields[8]),
                fields[9]
            );
        }

        private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    }
}
