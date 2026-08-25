using LootSingles.Application.CardCatalog;
using LootSingles.Infrastructure.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class PokemonTcgApiCardCatalogProviderTests
{
    private static readonly CardIdentity Identity = new(
        "Genesect ex",
        "Scarlet & Violet",
        "#111/198",
        null
    );

    [Fact]
    public async Task TryMatchImageUrlAsync_ExactSetAndNumberMatchWithVerifiedName_ReturnsImageUrl()
    {
        var provider = NewProvider(
            StubHttpMessageHandler.ReturningJson(
                """
                {
                  "data": [
                    {
                      "name": "Genesect ex",
                      "set": { "name": "Scarlet & Violet" },
                      "number": "111",
                      "images": { "small": "https://example.com/small.png", "large": "https://example.com/large.png" }
                    }
                  ]
                }
                """
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Equal("https://example.com/large.png", result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_NoMatchingCard_ReturnsNull()
    {
        var provider = NewProvider(StubHttpMessageHandler.ReturningJson("""{ "data": [] }"""));

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_MultipleCandidateCards_ReturnsNull()
    {
        var provider = NewProvider(
            StubHttpMessageHandler.ReturningJson(
                """
                {
                  "data": [
                    {
                      "name": "Genesect ex",
                      "set": { "name": "Scarlet & Violet" },
                      "number": "111",
                      "images": { "small": "https://example.com/small-1.png", "large": "https://example.com/large-1.png" }
                    },
                    {
                      "name": "Genesect ex",
                      "set": { "name": "Scarlet & Violet" },
                      "number": "111",
                      "images": { "small": "https://example.com/small-2.png", "large": "https://example.com/large-2.png" }
                    }
                  ]
                }
                """
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_ReturnedCardNameDoesNotMatchImportedProductName_ReturnsNull()
    {
        var provider = NewProvider(
            StubHttpMessageHandler.ReturningJson(
                """
                {
                  "data": [
                    {
                      "name": "Some Other Card",
                      "set": { "name": "Scarlet & Violet" },
                      "number": "111",
                      "images": { "small": "https://example.com/small.png", "large": "https://example.com/large.png" }
                    }
                  ]
                }
                """
            )
        );

        var result = await provider.TryMatchImageUrlAsync(Identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_CollectorNumberHasLeadingZeros_QueriesTheApiWithoutThem()
    {
        var handler = StubHttpMessageHandler.ReturningJson("""{ "data": [] }""");
        var provider = NewProvider(handler);

        await provider.TryMatchImageUrlAsync(
            Identity with
            {
                CollectorNumber = "#067/086",
            },
            CancellationToken.None
        );

        var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("number:67", query);
        Assert.DoesNotContain("number:067", query);
    }

    [Fact]
    public async Task TryMatchImageUrlAsync_SetHasKnownSeriesAbbreviationPrefix_QueriesWithoutThePrefix()
    {
        var handler = StubHttpMessageHandler.ReturningJson("""{ "data": [] }""");
        var provider = NewProvider(handler);

        await provider.TryMatchImageUrlAsync(
            Identity with
            {
                Set = "SV: Black Bolt",
            },
            CancellationToken.None
        );

        var query = Uri.UnescapeDataString(handler.LastRequest!.RequestUri!.Query);
        Assert.Contains("set.name:\"Black Bolt\"", query);
        Assert.DoesNotContain("SV: Black Bolt", query);
    }

    private static PokemonTcgApiCardCatalogProvider NewProvider(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.pokemontcg.io/v2/"),
        };
        return new PokemonTcgApiCardCatalogProvider(httpClient);
    }
}
