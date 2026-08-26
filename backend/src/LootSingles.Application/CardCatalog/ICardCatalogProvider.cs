namespace LootSingles.Application.CardCatalog;

public interface ICardCatalogProvider
{
    string ProductLine { get; }

    Task<string?> TryMatchImageUrlAsync(CardIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Default: fans out to <see cref="TryMatchImageUrlAsync"/>, identical outward and
    /// concurrency behavior to calling it once per identity. A provider overrides this only when
    /// it has a genuine batch endpoint to use instead (research.md §3) - most providers never
    /// need to.
    ///
    /// Each identity's call is independently caught: one identity's failure maps only that
    /// identity to null and never faults the whole <see cref="Task.WhenAll"/>, preserving the
    /// same per-line isolation `OrdersService` used to provide before dispatch moved to one call
    /// per game rather than one call per line (research.md §7).
    /// </summary>
    async Task<IReadOnlyDictionary<CardIdentity, string?>> TryMatchImageUrlsAsync(
        IReadOnlyList<CardIdentity> identities,
        CancellationToken cancellationToken
    )
    {
        var distinctIdentities = identities.Distinct().ToArray();
        var results = await Task.WhenAll(
            distinctIdentities.Select(async identity =>
            {
                try
                {
                    return (
                        identity,
                        url: await TryMatchImageUrlAsync(identity, cancellationToken)
                    );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (identity, url: (string?)null);
                }
            })
        );
        return results.ToDictionary(r => r.identity, r => r.url);
    }
}
