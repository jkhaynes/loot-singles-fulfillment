using LootSingles.Application.Import;

namespace LootSingles.UnitTests.Import;

public class ConditionVariantParserTests
{
    [Theory]
    [InlineData("Near Mint Holofoil", "Near Mint", "Holofoil")]
    [InlineData("Near Mint Foil", "Near Mint", "Foil")]
    [InlineData("Lightly Played Foil", "Lightly Played", "Foil")]
    [InlineData("Near Mint Reverse Holofoil", "Near Mint", "Reverse Holofoil")]
    [InlineData("Near Mint", "Near Mint", null)]
    public void Parse_SeparatesKnownConditionPrefixFromVariant(
        string source,
        string expectedCondition,
        string? expectedVariant
    )
    {
        var result = ConditionVariantParser.Parse(source);

        Assert.Equal(expectedCondition, result.Condition);
        Assert.Equal(expectedVariant, result.Variant);
    }

    [Fact]
    public void Parse_CombinesConditionSuffixWithParentheticalMarker()
    {
        var result = ConditionVariantParser.Parse("Near Mint Foil", "(Showcase)");

        Assert.Equal("Near Mint", result.Condition);
        Assert.Equal("Foil, (Showcase)", result.Variant);
    }
}
