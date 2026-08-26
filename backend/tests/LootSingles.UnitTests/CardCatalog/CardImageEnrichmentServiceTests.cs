using LootSingles.Application.CardCatalog;
using Microsoft.Extensions.Logging.Abstractions;

namespace LootSingles.UnitTests.CardCatalog;

public sealed class CardImageEnrichmentServiceTests
{
    [Fact]
    public async Task TryGetImageUrlsAsync_NoRegisteredProviderForGame_ReturnsEveryIdentityMappedToNullWithoutInvokingAnyProvider()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon", "https://example.com/image.png");
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );
        var identities = new[] { NewIdentity() };

        var results = await service.TryGetImageUrlsAsync(
            "Magic",
            identities,
            CancellationToken.None
        );

        Assert.Null(results[identities[0]]);
        Assert.Equal(0, provider.BatchCallCount);
    }

    [Fact]
    public async Task TryGetImageUrlsAsync_MatchingProviderCaseInsensitive_DelegatesAndReturnsResult()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon", "https://example.com/image.png");
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );
        var identities = new[] { NewIdentity() };

        var results = await service.TryGetImageUrlsAsync(
            "POKEMON",
            identities,
            CancellationToken.None
        );

        Assert.Equal("https://example.com/image.png", results[identities[0]]);
        Assert.Equal(1, provider.BatchCallCount);
    }

    [Fact]
    public async Task TryGetImageUrlsAsync_MultipleIdentities_EachResolvedIndependentlyInOneDelegatedCall()
    {
        var provider = new RecordingCardCatalogProvider("Pokemon", null);
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );
        CardIdentity[] identities =
        [
            new("Pikachu", "Base Set", "#58/102", null),
            new("Charizard", "Base Set", "#4/102", null),
        ];

        var results = await service.TryGetImageUrlsAsync(
            "Pokemon",
            identities,
            CancellationToken.None
        );

        Assert.Equal(2, results.Count);
        Assert.Equal(1, provider.BatchCallCount);
    }

    [Fact]
    public async Task TryGetImageUrlsAsync_ProviderThrows_ReturnsEveryIdentityMappedToNullRatherThanPropagating()
    {
        var provider = new ThrowingCardCatalogProvider("Pokemon");
        var service = new CardImageEnrichmentService(
            [provider],
            NullLogger<CardImageEnrichmentService>.Instance
        );
        var identities = new[] { NewIdentity() };

        var results = await service.TryGetImageUrlsAsync(
            "Pokemon",
            identities,
            CancellationToken.None
        );

        Assert.Null(results[identities[0]]);
    }

    private static CardIdentity NewIdentity() => new("Pikachu", "Base Set", "#58/102", null);

    private sealed class RecordingCardCatalogProvider(string productLine, string? imageUrl)
        : ICardCatalogProvider
    {
        public string ProductLine { get; } = productLine;
        public int BatchCallCount { get; private set; }

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Only the batch operation is expected to be called.");

        public Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
            IReadOnlyList<CardIdentity> identities,
            CancellationToken cancellationToken
        )
        {
            BatchCallCount++;
            IReadOnlyDictionary<CardIdentity, string?> result = identities
                .Distinct()
                .ToDictionary(identity => identity, _ => imageUrl);
            return Task.FromResult(result);
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
