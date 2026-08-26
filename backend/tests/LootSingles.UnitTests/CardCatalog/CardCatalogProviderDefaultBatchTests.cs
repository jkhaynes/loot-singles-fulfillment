using LootSingles.Application.CardCatalog;

namespace LootSingles.UnitTests.CardCatalog;

/// <summary>
/// Proves <see cref="ICardCatalogProvider"/>'s default `TryMatchImageUrlsAsync` implementation
/// (research.md §3) is correct against a minimal fake implementing only the single-card
/// operation - exactly the shape every provider except Scryfall uses.
/// </summary>
public sealed class CardCatalogProviderDefaultBatchTests
{
    [Fact]
    public async Task TryMatchImageUrlsAsync_DefaultImplementation_ResolvesEachIdentityIndependently()
    {
        ICardCatalogProvider provider = new RecordingSingleCardProvider();
        CardIdentity[] identities =
        [
            new("Pikachu", "Base Set", "#58/102", null),
            new("Charizard", "Base Set", "#4/102", null),
        ];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Equal("image-for-Pikachu", results[identities[0]]);
        Assert.Equal("image-for-Charizard", results[identities[1]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_DefaultImplementation_FansOutConcurrentlyNotSequentially()
    {
        var provider = new ConcurrencyTrackingProvider();
        ICardCatalogProvider providerInterface = provider;
        CardIdentity[] identities =
        [
            new("A", "Set", "#1", null),
            new("B", "Set", "#2", null),
            new("C", "Set", "#3", null),
        ];

        await providerInterface.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.True(
            provider.MaxConcurrentCalls >= 2,
            $"Expected concurrent fan-out, but max concurrent calls was {provider.MaxConcurrentCalls}."
        );
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_DefaultImplementation_OneIdentityThrows_SiblingIdentityStillResolves()
    {
        ICardCatalogProvider provider = new PartiallyFailingProvider(failingProductName: "Fails");
        CardIdentity[] identities =
        [
            new("Fails", "Set", "#1", null),
            new("Succeeds", "Set", "#2", null),
        ];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Null(results[identities[0]]);
        Assert.Equal("image-for-Succeeds", results[identities[1]]);
    }

    [Fact]
    public async Task TryMatchImageUrlsAsync_DefaultImplementation_DuplicateIdentities_DoesNotThrowAndBothResolve()
    {
        ICardCatalogProvider provider = new RecordingSingleCardProvider();
        var identity = new CardIdentity("Pikachu", "Base Set", "#58/102", null);
        CardIdentity[] identities = [identity, identity];

        var results = await provider.TryMatchImageUrlsAsync(identities, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("image-for-Pikachu", results[identity]);
    }

    private sealed class RecordingSingleCardProvider : ICardCatalogProvider
    {
        public string ProductLine => "TestGame";

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) => Task.FromResult<string?>($"image-for-{identity.ProductName}");
    }

    private sealed class PartiallyFailingProvider(string failingProductName) : ICardCatalogProvider
    {
        public string ProductLine => "TestGame";

        public Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        ) =>
            identity.ProductName == failingProductName
                ? throw new InvalidOperationException("Simulated per-card provider failure.")
                : Task.FromResult<string?>($"image-for-{identity.ProductName}");
    }

    private sealed class ConcurrencyTrackingProvider : ICardCatalogProvider
    {
        private int _current;

        public int MaxConcurrentCalls { get; private set; }

        public string ProductLine => "TestGame";

        public async Task<string?> TryMatchImageUrlAsync(
            CardIdentity identity,
            CancellationToken cancellationToken
        )
        {
            var current = Interlocked.Increment(ref _current);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, current);
            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _current);
            return identity.ProductName;
        }
    }
}
