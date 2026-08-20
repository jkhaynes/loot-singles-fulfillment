using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

public class OrderLineExtractionTests
{
    private const string RealFixtureDescription =
        "Pokemon - SV: Black Bolt: Genesect ex - #067/086 - Double Rare - Near Mint Holofoil";

    [Fact]
    public void Extract_RealFixtureLine_ExtractsEveryStructuredFieldAndQuantity()
    {
        var source = new RawProductLine
        {
            QuantityText = "2",
            RawDescription = RealFixtureDescription,
        };

        var result = OrderLineExtractor.Extract(source);

        Assert.Equal("Pokemon", result.ProductLine);
        Assert.Equal("Genesect ex", result.ProductName);
        Assert.Equal("SV: Black Bolt", result.Set);
        Assert.Equal("#067/086", result.CollectorNumber);
        Assert.Equal("Double Rare", result.Rarity);
        Assert.Equal("Near Mint", result.Condition);
        Assert.Equal("Holofoil", result.Variant);
        Assert.Equal(2, result.Quantity);
    }

    [Fact]
    public void Extract_RealFixtureLine_PreservesRawDescriptionExactly()
    {
        var source = new RawProductLine
        {
            QuantityText = "2",
            RawDescription = RealFixtureDescription,
        };

        var result = OrderLineExtractor.Extract(source);

        Assert.Equal(RealFixtureDescription, result.RawDescription);
    }

    [Theory]
    [InlineData(
        "Magic - Commander: FINAL FANTASY: Y'shtola, Night's Blessed - #7 - M - Near Mint Foil",
        "Commander: FINAL FANTASY",
        "Y'shtola, Night's Blessed",
        "#7",
        "M",
        "Near Mint",
        "Foil")]
    [InlineData(
        "Magic - The Hobbit: My Precious (Showcase) (Surge Foil) - #271 - R - Near Mint Foil",
        "The Hobbit",
        "My Precious (Showcase) (Surge Foil)",
        "#271",
        "R",
        "Near Mint",
        "Foil, (Showcase), (Surge Foil)")]
    [InlineData(
        "Lorcana TCG - The First Chapter: John Silver - Alien Pirate - #82/204 - Legendary - Near Mint",
        "The First Chapter",
        "John Silver - Alien Pirate",
        "#82/204",
        "Legendary",
        "Near Mint",
        null)]
    public void Extract_RepresentativeFixtureFormats_ExtractsStructuredFields(
        string description,
        string expectedSet,
        string expectedProductName,
        string expectedCollectorNumber,
        string expectedRarity,
        string expectedCondition,
        string? expectedVariant)
    {
        var result = OrderLineExtractor.Extract(new RawProductLine
        {
            QuantityText = "1",
            RawDescription = description,
        });

        Assert.Equal(expectedSet, result.Set);
        Assert.Equal(expectedProductName, result.ProductName);
        Assert.Equal(expectedCollectorNumber, result.CollectorNumber);
        Assert.Equal(expectedRarity, result.Rarity);
        Assert.Equal(expectedCondition, result.Condition);
        Assert.Equal(expectedVariant, result.Variant);
    }
}
