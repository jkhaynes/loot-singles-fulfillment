using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class CardNameMatcherTests
{
    [Theory]
    [InlineData("Seance", "Séance")]
    [InlineData("Séance", "Seance")]
    [InlineData("Jotun Grunt", "Jötun Grunt")]
    [InlineData("Alakazam V", "Alakazam V")]
    [InlineData("alakazam v", "Alakazam V")]
    public void Matches_DiacriticOrCaseOnlyDifference_ReturnsTrue(
        string productName,
        string providerCardName
    )
    {
        Assert.True(CardNameMatcher.Matches(productName, providerCardName));
    }

    [Fact]
    public void Matches_TrailingParenthetical_IsStrippedBeforeComparison()
    {
        Assert.True(
            CardNameMatcher.Matches("Carrion Feeder (Borderless) (Foil Etched)", "Carrion Feeder")
        );
    }

    [Fact]
    public void Matches_DifferentCardName_ReturnsFalse()
    {
        Assert.False(CardNameMatcher.Matches("Lightning Bolt", "Lightning Strike"));
    }
}
