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
        "Lorcana TCG - Disney Lorcana Promo Cards - Scrooge McDuck - S.H.U.S.H. Agent - #36 - Promo - Near Mint Holofoil",
        "Disney Lorcana Promo Cards",
        "Scrooge McDuck - S.H.U.S.H. Agent",
        "#36",
        "Promo",
        "Near Mint",
        "Holofoil"
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

    [Fact]
    public void Extract_NoColonSingleSegmentBeforeCollectorNumber_IsStillRejected()
    {
        var source = new RawProductLine
        {
            QuantityText = "1",
            RawDescription = "SomeGame - AmbiguousSegment - #12 - Common - Near Mint",
        };

        var result = OrderLineExtractor.Extract(source);

        Assert.False(result.IsValid);
        Assert.Equal(FailureType.MissingProductName, result.FailureType);
    }

    [Fact]
    public void Extract_DuplicateCollectorNumberEcho_ExcludesEchoFromProductName()
    {
        var source = new RawProductLine
        {
            QuantityText = "1",
            RawDescription =
                "Pokemon - ME05: Pitch Black: Fomantis - 085/084 - #085/084 - Illustration Rare - Near Mint Holofoil",
        };

        var result = OrderLineExtractor.Extract(source);

        Assert.True(result.IsValid);
        var orderLine = result.OrderLine!;
        Assert.Equal("ME05: Pitch Black", orderLine.Set);
        Assert.Equal("Fomantis", orderLine.ProductName);
        Assert.Equal("#085/084", orderLine.CollectorNumber);
    }

    [Theory]
    [InlineData(
        "Pokemon - ME05: Pitch Black: Manectric - 065/084 - #065/084 - Illustration Rare - Near Mint Holofoil",
        "ME05: Pitch Black",
        "Manectric"
    )]
    [InlineData(
        "Pokemon - ME05: Pitch Black: Clefairy - 040/084 - #040/084 - Illustration Rare - Near Mint Holofoil",
        "ME05: Pitch Black",
        "Clefairy"
    )]
    public void Extract_OtherRealDuplicateCollectorNumberEchoLines_ExcludesEchoFromProductName(
        string description,
        string expectedSet,
        string expectedProductName
    )
    {
        var result = OrderLineExtractor.Extract(
            new RawProductLine { QuantityText = "1", RawDescription = description }
        );

        Assert.True(result.IsValid);
        var orderLine = result.OrderLine!;
        Assert.Equal(expectedSet, orderLine.Set);
        Assert.Equal(expectedProductName, orderLine.ProductName);
    }

    [Fact]
    public void Extract_NonDuplicateSegmentBeforeCollectorNumber_IsNeverDroppedAsFalsePositive()
    {
        var source = new RawProductLine
        {
            QuantityText = "1",
            RawDescription = "Pokemon - Some Set: Card - 099 - #100/150 - Common - Near Mint",
        };

        var result = OrderLineExtractor.Extract(source);

        Assert.True(result.IsValid);
        var orderLine = result.OrderLine!;
        Assert.Equal("Some Set", orderLine.Set);
        Assert.Equal("Card - 099", orderLine.ProductName);
        Assert.Equal("#100/150", orderLine.CollectorNumber);
    }

    [Theory]
    [InlineData(
        "Pokemon - ME05: Pitch Black: Meowth - #106/094 - Common - Near Mint",
        "ME05: Pitch Black",
        "Meowth"
    )]
    [InlineData(
        "Pokemon - ME05: Pitch Black: Blastoise ex - #030/142 - Double Rare - Near Mint Holofoil",
        "ME05: Pitch Black",
        "Blastoise ex"
    )]
    public void Extract_RealNonDuplicateLinesFromSameOrder_AreUnaffectedByEchoExclusion(
        string description,
        string expectedSet,
        string expectedProductName
    )
    {
        var result = OrderLineExtractor.Extract(
            new RawProductLine { QuantityText = "1", RawDescription = description }
        );

        Assert.True(result.IsValid);
        var orderLine = result.OrderLine!;
        Assert.Equal(expectedSet, orderLine.Set);
        Assert.Equal(expectedProductName, orderLine.ProductName);
    }

    [Fact]
    public void Extract_EchoOnlySpan_IsStillRejectedAsMissingProductName()
    {
        var source = new RawProductLine
        {
            QuantityText = "1",
            RawDescription = "Pokemon - 067/086 - #067/086 - Rare - Near Mint",
        };

        var result = OrderLineExtractor.Extract(source);

        Assert.False(result.IsValid);
        Assert.Equal(FailureType.MissingProductName, result.FailureType);
    }
}
