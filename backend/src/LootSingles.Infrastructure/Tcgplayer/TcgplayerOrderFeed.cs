using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The Infrastructure implementation of <see cref="ITcgplayerOrderFeed"/>: composes
/// <see cref="TcgplayerApiClient"/> (what to ask TCGplayer) with <see cref="TcgplayerOrderTranslator"/>
/// (what the answer means). Call order follows contracts/tcgplayer-upstream.md: manifest, search
/// and every searched order's details, to list the open ones; then, for the orders asked for,
/// manifest and details again, each order's items, and the SKU and product lookups shared by all
/// of them.
///
/// Openness (FR-004): TCGplayer's search filters are partial, so listing applies
/// <see cref="TcgplayerOpenOrderRule.Decide"/> to each searched order's details row, before
/// anything else is fetched or decided for it. An order that is not open is dropped there, so it
/// is never detected, fetched, imported, rejected, reported as already imported or counted. An
/// undecidable order (its details lack a value the rule needs) is kept, so it is detected, and
/// fetching it rejects it alone (ruling R30), as for an order TCGplayer returned no details for.
///
/// Failure scope (research.md section 10): a data problem confined to one order (its items body is
/// unreadable or its items stall, TCGplayer returned no details for it, or its details can't
/// decide its openness) becomes that order's
/// <see cref="OrderCandidate.RejectedBySource"/>, so its siblings still import. Anything that is
/// not about one order's data (unreachable, refused, not configured, or an unreadable search,
/// manifest, details or catalog response) propagates as the attempt-wide
/// <see cref="TcgplayerFeedException"/>.
/// </summary>
public sealed class TcgplayerOrderFeed(
    TcgplayerApiClient client,
    TcgplayerOptions options,
    TcgplayerRateLimiter limiter
) : ITcgplayerOrderFeed
{
    public long CallCount => limiter.CallCount;

    public int PageSize => options.PageSize;

    public async Task<IReadOnlyList<string>> GetOpenOrderNumbersAsync(
        CancellationToken cancellationToken
    )
    {
        ThrowIfNotConfigured();
        var ids = await client.GetOpenOrderIdsAsync(cancellationToken);
        var searched = await client.SearchOrderNumbersAsync(ids, cancellationToken);
        var detailsByNumber = new Dictionary<string, TcgplayerOrderDetails>(StringComparer.Ordinal);
        foreach (var row in await client.GetOrderDetailsAsync(searched, cancellationToken))
        {
            if (row?.OrderNumber is { } number)
                detailsByNumber.TryAdd(number, row);
        }

        // Only a decided "not open" is dropped. A searched order with no details row, or whose row
        // can't decide its openness, stays listed so GetOrdersAsync rejects it on its own.
        return searched
            .Where(number =>
                !detailsByNumber.TryGetValue(number, out var details)
                || TcgplayerOpenOrderRule.Decide(details, ids).Openness != TcgplayerOpenness.NotOpen
            )
            .ToList();
    }

    public async Task<IReadOnlyList<OrderCandidate>> GetOrdersAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(orderNumbers);
        ThrowIfNotConfigured();
        var wanted = orderNumbers.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
            return [];

        // Re-resolved (one manifest call per batch) rather than carried over from the listing, so
        // the feed stays stateless; needed to tell an undecidable row from a decided one.
        var ids = await client.GetOpenOrderIdsAsync(cancellationToken);

        // The client batches this at PageSize. It returns what TCGplayer sent, so a requested
        // order that is absent is noticed here.
        var detailRows = await client.GetOrderDetailsAsync(wanted, cancellationToken);
        var detailsByNumber = new Dictionary<string, TcgplayerOrderDetails>(StringComparer.Ordinal);
        foreach (var row in detailRows)
        {
            if (row?.OrderNumber is { } number)
                detailsByNumber.TryAdd(number, row);
        }

        var loaded = new List<(TcgplayerOrderDetails Details, TcgplayerOrderItems Items)>();
        var rejections = new Dictionary<string, (FailureType, string)>(StringComparer.Ordinal);
        foreach (var number in wanted)
        {
            if (!detailsByNumber.TryGetValue(number, out var details))
            {
                rejections[number] = (
                    FailureType.TcgplayerResponseInvalid,
                    $"{number}: TCGplayer returned no details for this order"
                );
                continue;
            }

            // Only an undecidable row is rejected here. A row that has turned "not open" since
            // listing (seconds ago) is still imported, as one that closes just after import is.
            var decision = TcgplayerOpenOrderRule.Decide(details, ids);
            if (decision.Openness == TcgplayerOpenness.Undecidable)
            {
                rejections[number] = (FailureType.TcgplayerResponseInvalid, decision.Reason!);
                continue;
            }

            try
            {
                loaded.Add((details, await client.GetOrderItemsAsync(number, cancellationToken)));
            }
            catch (TcgplayerFeedException exception)
                when (exception.Failure == TcgplayerFeedFailure.ResponseInvalid)
            {
                // Only this order's items are unusable; the message names no body (client contract).
                rejections[number] = (FailureType.TcgplayerResponseInvalid, exception.Message);
            }
        }

        // One pair of lookups for every order that has items; none when there is nothing to look up.
        var skuIds = loaded
            .SelectMany(order => order.Items.Items)
            .Select(item => item.SkuId)
            .OfType<int>()
            .Distinct()
            .ToList();
        var skus = await client.GetSkusAsync(skuIds, cancellationToken);
        var productIds = skus.Values.Select(sku => sku.ProductId).OfType<int>().Distinct().ToList();
        var products = await client.GetProductsAsync(productIds, cancellationToken);

        var translated = loaded.ToDictionary(
            order => order.Details.OrderNumber!,
            order =>
                TcgplayerOrderTranslator.Translate(
                    order.Details,
                    order.Items.Items,
                    order.Items.TotalItems,
                    skus,
                    products,
                    options
                ),
            StringComparer.Ordinal
        );

        return wanted
            .Select(number =>
                rejections.TryGetValue(number, out var rejection)
                    ? new OrderCandidate(number, [], rejection)
                    : translated[number]
            )
            .ToList();
    }

    // Before any request, so an unconfigured environment never reaches the token request.
    private void ThrowIfNotConfigured()
    {
        if (!options.IsConfigured)
        {
            throw new TcgplayerFeedException(
                TcgplayerFeedFailure.NotConfigured,
                "TCGplayer credentials are not configured."
            );
        }
    }
}
