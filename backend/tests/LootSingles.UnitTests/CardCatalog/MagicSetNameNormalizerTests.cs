using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class MagicSetNameNormalizerTests
{
    [Theory]
    [InlineData("Tarkir: Dragonstorm")]
    [InlineData("The Hobbit")]
    [InlineData("Duskmourn: House of Horror")]
    [InlineData("Modern Horizons 3")]
    [InlineData("Commander Legends")]
    public void NormalizeCandidates_NoSpecialShape_ReturnsOnlyTheRawText(string rawSetText)
    {
        var candidates = MagicSetNameNormalizer.NormalizeCandidates(rawSetText);

        Assert.Equal([rawSetText], candidates);
    }

    [Theory]
    [InlineData("Commander: FINAL FANTASY", "FINAL FANTASY Commander")]
    [InlineData("Commander: Marvel Super Heroes", "Marvel Super Heroes Commander")]
    public void NormalizeCandidates_CommanderPrefix_AlsoOffersTheReversedForm(
        string rawSetText,
        string expectedReversed
    )
    {
        var candidates = MagicSetNameNormalizer.NormalizeCandidates(rawSetText);

        Assert.Equal([rawSetText, expectedReversed], candidates);
    }

    [Theory]
    [InlineData("Universes Beyond: Fallout", "Fallout")]
    [InlineData("March of the Machine: Multiverse Legends", "Multiverse Legends")]
    public void NormalizeCandidates_KnownDroppedPrefix_AlsoOffersTheSuffixAlone(
        string rawSetText,
        string expectedSuffix
    )
    {
        var candidates = MagicSetNameNormalizer.NormalizeCandidates(rawSetText);

        Assert.Equal([rawSetText, expectedSuffix], candidates);
    }

    [Fact]
    public void NormalizeCandidates_EternalLegalSuffix_AlsoOffersTheEternalForm()
    {
        var candidates = MagicSetNameNormalizer.NormalizeCandidates("The Hobbit: Eternal-Legal");

        Assert.Equal(["The Hobbit: Eternal-Legal", "The Hobbit Eternal"], candidates);
    }
}
