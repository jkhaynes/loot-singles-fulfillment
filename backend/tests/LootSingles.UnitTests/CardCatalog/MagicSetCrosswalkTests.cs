using System.Text;
using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class MagicSetCrosswalkTests
{
    [Fact]
    public void Load_CheckedInCrosswalk_MapsMarchOfTheMachineMultiverseLegendsToMul()
    {
        using var stream = File.OpenRead(
            Path.Combine(
                AppContext.BaseDirectory,
                "CardCatalog",
                "Data",
                "tcgplayer-magic-to-scryfall-set-codes.json"
            )
        );
        var crosswalk = MagicSetCrosswalk.Load(stream);

        var found = crosswalk.TryGetScryfallSetCodes(
            "March of the Machine: Multiverse Legends",
            out var codes
        );

        Assert.True(found);
        Assert.Equal(["mul"], codes);
    }

    [Theory]
    [InlineData("  MARCH   OF THE MACHINE: MULTIVERSE LEGENDS  ")]
    [InlineData("\tMarch of the Machine:  Multiverse Legends\r\n")]
    public void TryGetScryfallSetCodes_CasingAndWhitespaceDiffer_ReturnsMapping(string setName)
    {
        var crosswalk = Load(
            "March of the Machine: Multiverse Legends",
            "march of the machine: multiverse legends",
            "mul"
        );

        var found = crosswalk.TryGetScryfallSetCodes(setName, out var codes);

        Assert.True(found);
        Assert.Equal(["mul"], codes);
    }

    [Fact]
    public void TryGetScryfallSetCodes_CurlyAndStraightApostrophes_ReturnSameMapping()
    {
        var crosswalk = Load("Urza's Saga", "urza’s saga", "usg");

        var found = crosswalk.TryGetScryfallSetCodes("URZA‘S SAGA", out var codes);

        Assert.True(found);
        Assert.Equal(["usg"], codes);
    }

    [Fact]
    public void TryGetScryfallSetCodes_UnknownSet_ReturnsFalseWithoutGuessing()
    {
        var crosswalk = Load("Multiverse Legends", "multiverse legends", "mul");

        var found = crosswalk.TryGetScryfallSetCodes(
            "March of the Machine: Multiverse Legends",
            out var codes
        );

        Assert.False(found);
        Assert.Empty(codes);
    }

    [Fact]
    public void Load_MappingsMissing_ThrowsClearConfigurationError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => LoadJson("{}"));

        Assert.Contains("mappings", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_NormalizedNameMissing_ThrowsClearConfigurationError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LoadJson("""{ "mappings": { "Set": { "scryfallSetCodes": ["abc"] } } }""")
        );

        Assert.Contains(
            "normalizedTcgplayerSetName",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Load_BlankScryfallCode_ThrowsClearConfigurationError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LoadJson(
                """{ "mappings": { "Set": { "normalizedTcgplayerSetName": "set", "scryfallSetCodes": [" "] } } }"""
            )
        );

        Assert.Contains("nonblank", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_DuplicateNormalizedNames_ThrowsClearConfigurationError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LoadJson(
                """
                {
                  "mappings": {
                    "First": { "normalizedTcgplayerSetName": "Urza’s Saga", "scryfallSetCodes": ["usg"] },
                    "Second": { "normalizedTcgplayerSetName": "  URZA'S   SAGA ", "scryfallSetCodes": ["usg"] }
                  }
                }
                """
            )
        );

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("urza's saga", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MagicSetCrosswalk Load(string displayName, string normalizedName, string code) =>
        LoadJson(
            $$"""
            {
              "mappings": {
                "{{displayName}}": {
                  "normalizedTcgplayerSetName": "{{normalizedName}}",
                  "scryfallSetCodes": ["{{code}}"]
                }
              }
            }
            """
        );

    private static MagicSetCrosswalk LoadJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return MagicSetCrosswalk.Load(stream);
    }
}
