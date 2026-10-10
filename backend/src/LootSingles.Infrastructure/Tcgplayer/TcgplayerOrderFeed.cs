using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The Infrastructure implementation of <see cref="ITcgplayerOrderFeed"/>: composes
/// <see cref="TcgplayerApiClient"/> (what to ask TCGplayer) with <see cref="TcgplayerOrderTranslator"/>
/// (what the answer means). Call order follows contracts/tcgplayer-upstream.md: manifest, search;
/// then details, each order's items, and the SKU and product lookups shared by all the orders.
///
/// Failure scope (research.md section 10): a data problem confined to one order (its items body is
/// unreadable or its items stall, or TCGplayer returned no details for it) becomes that order's
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

    public async Task<IReadOnlyList<string>> GetOpenOrderNumbersAsync(
        CancellationToken cancellationToken
    )
    {
        ThrowIfNotConfigured();
        var statusIds = await client.GetOpenOrderStatusIdsAsync(cancellationToken);
        return await client.SearchOrderNumbersAsync(statusIds, cancellationToken);
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
