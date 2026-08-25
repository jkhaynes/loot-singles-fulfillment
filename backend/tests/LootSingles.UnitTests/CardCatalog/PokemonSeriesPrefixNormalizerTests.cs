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
}
