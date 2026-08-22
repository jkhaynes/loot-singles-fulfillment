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

        Assert.True(result.IsValid);
        var orderLine = result.OrderLine!;
        Assert.Equal("Pokemon", orderLine.ProductLine);
        Assert.Equal("Genesect ex", orderLine.ProductName);
        Assert.Equal("SV: Black Bolt", orderLine.Set);
        Assert.Equal("#067/086", orderLine.CollectorNumber);
        Assert.Equal("Double Rare", orderLine.Rarity);
        Assert.Equal("Near Mint", orderLine.Condition);
        Assert.Equal("Holofoil", orderLine.Variant);
        Assert.Equal(2, orderLine.Quantity);
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

        Assert.True(result.IsValid);
        Assert.Equal(RealFixtureDescription, result.OrderLine!.RawDescription);
    }

    [Theory]
    [InlineData(
        "Magic - Commander: FINAL FANTASY: Y'shtola, Night's Blessed - #7 - M - Near Mint Foil",
        "Commander: FINAL FANTASY",
        "Y'shtola, Night's Blessed",
        "#7",
        "M",
        "Near Mint",
        "Foil"
    )]
    [InlineData(
        "Magic - The Hobbit: My Precious (Showcase) (Surge Foil) - #271 - R - Near Mint Foil",
        "The Hobbit",
        "My Precious (Showcase) (Surge Foil)",
        "#271",
        "R",
        "Near Mint",
        "Foil, (Showcase), (Surge Foil)"
    )]
    [InlineData(
        "Lorcana TCG - The First Chapter: John Silver - Alien Pirate - #82/204 - Legendary - Near Mint",
        "The First Chapter",
        "John Silver - Alien Pirate",
        "#82/204",
        "Legendary",
        "Near Mint",
        null
    )]
    public void Extract_RepresentativeFixtureFormats_ExtractsStructuredFields(
        string description,
        string expectedSet,
        string expectedProductName,
        string expectedCollectorNumber,
        string expectedRarity,
        string expectedCondition,
        string? expectedVariant
    )
    {
        var result = OrderLineExtractor.Extract(
            new RawProductLine { QuantityText = "1", RawDescription = description }
        );

        Assert.True(result.IsValid);
        var orderLine = result.OrderLine!;
        Assert.Equal(expectedSet, orderLine.Set);
        Assert.Equal(expectedProductName, orderLine.ProductName);
        Assert.Equal(expectedCollectorNumber, orderLine.CollectorNumber);
        Assert.Equal(expectedRarity, orderLine.Rarity);
        Assert.Equal(expectedCondition, orderLine.Condition);
        Assert.Equal(expectedVariant, orderLine.Variant);
    }
}
