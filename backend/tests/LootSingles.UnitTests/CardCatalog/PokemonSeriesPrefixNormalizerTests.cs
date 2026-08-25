using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class PokemonSeriesPrefixNormalizerTests
{
    [Theory]
    [InlineData("SV: Black Bolt", "Black Bolt")]
    [InlineData("SV10: Destined Rivals", "Destined Rivals")]
    [InlineData("ME05: Pitch Black", "Pitch Black")]
    [InlineData("SWSH12: Silver Tempest", "Silver Tempest")]
    public void Normalize_KnownSeriesAbbreviationPrefix_StripsPrefix(
        string rawSetText,
        string expected
    )
    {
        var result = PokemonSeriesPrefixNormalizer.Normalize(rawSetText);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Scarlet & Violet")]
    [InlineData("Base Set")]
    [InlineData("Celebrations: Classic Collection")]
    [InlineData("SM Trainer Kit: Alolan Sandslash & Alolan Ninetales")]
    public void Normalize_NoRecognizedSeriesAbbreviationPrefix_ReturnsTextUnchanged(
        string rawSetText
    )
    {
        var result = PokemonSeriesPrefixNormalizer.Normalize(rawSetText);

        Assert.Equal(rawSetText, result);
    }

    [Fact]
    public void NormalizeCandidates_KnownAbbreviationPrefix_ReturnsOnlyTheAbbreviationStrippedCandidate()
    {
        var candidates = PokemonSeriesPrefixNormalizer.NormalizeCandidates("SV: Black Bolt");

        Assert.Equal(["Black Bolt"], candidates);
    }

    [Fact]
    public void NormalizeCandidates_UnrecognizedColonDelimitedPrefix_AlsoOffersTheColonToSpaceCandidate()
    {
        var candidates = PokemonSeriesPrefixNormalizer.NormalizeCandidates(
            "Celebrations: Classic Collection"
        );

        Assert.Equal(
            ["Celebrations: Classic Collection", "Celebrations Classic Collection"],
            candidates
        );
    }

    [Fact]
    public void NormalizeCandidates_NoColonAtAll_ReturnsOnlyTheRawText()
    {
        var candidates = PokemonSeriesPrefixNormalizer.NormalizeCandidates("Base Set");

        Assert.Equal(["Base Set"], candidates);
    }

    [Theory]
    [InlineData(
        "ME: Mega Evolution Promo",
        "Mega Evolution Promo",
        "ME Black Star Promos",
        "MEP Black Star Promos"
    )]
    [InlineData(
        "SV: Scarlet & Violet Promo Cards",
        "Scarlet & Violet Promo Cards",
        "SV Black Star Promos",
        "SVP Black Star Promos"
    )]
    [InlineData(
        "SWSH: Sword & Shield Promo Cards",
        "Sword & Shield Promo Cards",
        "SWSH Black Star Promos",
        "SWSHP Black Star Promos"
    )]
    public void NormalizeCandidates_RecognizedAbbreviationPromoShapedName_AlsoOffersBothBlackStarPromosCandidates(
        string rawSetText,
        string expectedStripped,
        string expectedPlainForm,
        string expectedPSuffixForm
    )
    {
        var candidates = PokemonSeriesPrefixNormalizer.NormalizeCandidates(rawSetText);

        Assert.Equal([expectedStripped, expectedPlainForm, expectedPSuffixForm], candidates);
    }
}
