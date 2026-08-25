using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class TrailingParentheticalStripperTests
{
    [Theory]
    [InlineData("Alakazam V (Full Art)", "Alakazam V")]
    [InlineData("Galadriel's Dismissal (Borderless)", "Galadriel's Dismissal")]
    [InlineData("Elrond, Moon-Reader (Extended Art)", "Elrond, Moon-Reader")]
    [InlineData("Genesect ex", "Genesect ex")]
    public void Strip_TrailingParenthetical_RemovesItForComparisonOnly(string name, string expected)
    {
        var result = TrailingParentheticalStripper.Strip(name);

        Assert.Equal(expected, result);
    }
}
