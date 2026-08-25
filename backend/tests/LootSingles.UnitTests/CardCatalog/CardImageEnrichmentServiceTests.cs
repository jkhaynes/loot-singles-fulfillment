using LootSingles.Application.CardCatalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class CardImageEnrichmentServiceTests
{
    [Fact]
    public async Task TryGetImageUrlAsync_NoRegisteredProviderForGame_ReturnsNullWithoutInvokingAnyProvider()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon", "https://example.com/image.png");
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );

        var result = await service.TryGetImageUrlAsync(
            "Magic",
            NewIdentity(),
            CancellationToken.None
        );

        Assert.Null(result);
        Assert.False(provider.WasInvoked);
    }

    [Fact]
    public async Task TryGetImageUrlAsync_MatchingProviderCaseInsensitive_DelegatesAndReturnsResult()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon", "https://example.com/image.png");
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );

        var result = await service.TryGetImageUrlAsync(
            "POKEMON",
            NewIdentity(),
            CancellationToken.None
        );

        Assert.Equal("https://example.com/image.png", result);
        Assert.True(provider.WasInvoked);
    }

    [Fact]
    public async Task TryGetImageUrlAsync_ProviderThrows_ReturnsNullRatherThanPropagating()
    {
        var provider = new ThrowingCardCatalogProvider("Pokemon");
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );

        var result = await service.TryGetImageUrlAsync(
            "Pokemon",
            NewIdentity(),
            CancellationToken.None
        );

        Assert.Null(result);
    }

    private static CardIdentity NewIdentity() => new("Pikachu", "Base Set", "#58/102", null);

    private sealed class RecordingCardCatalogProvider(string productLine, string? imageUrl)
        : ICardCatalogProvider
    {
        public string ProductLine { get; } = productLine;
        public bool WasInvoked { get; private set; }

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        )
        {
            WasInvoked = true;
            return Task.FromResult(imageUrl);
        }
    }

    private sealed class ThrowingCardCatalogProvider(string productLine) : ICardCatalogProvider
    {
        public string ProductLine { get; } = productLine;

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Simulated provider failure.");
    }
}
