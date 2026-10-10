using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The typed client for TCGplayer calls #2–#8 in contracts/tcgplayer-upstream.md: store key,
/// manifest, order search, order details, order items, SKUs and products. Every request is a GET
/// on a path relative to the versioned base address that <c>AddTcgplayer</c> sets, so the
/// User-Agent, bearer token and rate limiter always apply. It fetches and pages only; the
/// translator turns what it returns into candidates.
///
/// HTTP outcomes become a <see cref="TcgplayerFeedException"/> (research.md §10). Failures the
/// auth handler raises pass through unchanged. No request or response body is ever logged or put
/// into an exception message.
/// </summary>
public sealed class TcgplayerApiClient
{
    /// <summary>Catalog lookups batch at most this many ids per request (research.md §6).</summary>
    public const int CatalogBatchSize = 50;

    private readonly HttpClient _http;
    private readonly TcgplayerOptions _options;
    private readonly TcgplayerStoreKeyCache _storeKeyCache;

    public TcgplayerApiClient(
        HttpClient http,
        TcgplayerOptions options,
        TcgplayerStoreKeyCache storeKeyCache
    )
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(storeKeyCache);
        // Paging asks for PageSize at a time; a limit below one could never make progress.
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageSize, 1, "Tcgplayer:PageSize");

        _http = http;
        _options = options;
        _storeKeyCache = storeKeyCache;
    }

    /// <summary>
    /// <c>Tcgplayer:StoreKey</c> when configured; otherwise call #2, made once per process.
    /// </summary>
    public Task<string> GetStoreKeyAsync(CancellationToken cancellationToken) =>
        _options.StoreKey is { } configured
            ? Task.FromResult(configured)
            : _storeKeyCache.GetAsync(ResolveStoreKeyAsync, cancellationToken);

    /// <summary>
    /// Call #3: resolves each configured open status name to its id, matching the name exactly.
    /// A configured name the manifest lacks is <see cref="TcgplayerFeedFailure.ResponseInvalid"/>;
    /// the import never falls back to searching every order.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetOpenOrderStatusIdsAsync(
        CancellationToken cancellationToken
    )
    {
        var storeKey = await StorePathAsync(cancellationToken).ConfigureAwait(false);
        var response = await GetAsync<TcgplayerOrderManifest>(
                $"{storeKey}/orders/manifest",
                "The order manifest",
                notFoundMeansEmpty: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        var statusTypes = response.Results is [{ OrderStatusTypes: { } types }, ..]
            ? types
            : throw Invalid("The order manifest has no order status types.");

        var ids = new List<int>();
        var missing = new List<string>();
        foreach (var name in _options.OpenOrderStatuses)
        {
            var match = statusTypes.FirstOrDefault(type =>
                type is { Id: not null } && string.Equals(type.Name, name, StringComparison.Ordinal)
            );
            if (match is null)
            {
                missing.Add(name);
            }
            else if (!ids.Contains(match.Id!.Value))
            {
                ids.Add(match.Id.Value);
            }
        }

        return missing.Count == 0
            ? ids
            : throw Invalid(
                "The order manifest has no status named "
                    + string.Join(", ", missing.Select(name => $"'{name}'"))
                    + "; check Tcgplayer:OpenOrderStatuses."
            );
    }

    /// <summary>
    /// Call #4, paged: the order numbers of every order in the given statuses, each once, in the
    /// order first seen.
    /// </summary>
    public async Task<IReadOnlyList<string>> SearchOrderNumbersAsync(
        IReadOnlyList<int> orderStatusIds,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(orderStatusIds);
        // An empty status filter could mean "every order"; that is never what an import wants.
        if (orderStatusIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one order status id is required.",
                nameof(orderStatusIds)
            );
        }

        var storeKey = await StorePathAsync(cancellationToken).ConfigureAwait(false);
        var statusFilter = string.Join(
            ',',
            orderStatusIds.Select(id => id.ToString(CultureInfo.InvariantCulture))
        );
        var (numbers, _) = await GetAllPagesAsync<string?>(
                offset => $"{storeKey}/orders?orderStatusIds={statusFilter}&{Page(offset)}",
                "The order search",
                cancellationToken
            )
            .ConfigureAwait(false);

        return numbers.Any(string.IsNullOrWhiteSpace)
            ? throw Invalid("The order search returned a blank order number.")
            // Offset paging can return a number twice if orders move between page requests.
            : numbers.Select(number => number!).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Call #5, in batches of <c>PageSize</c>. Binds only the order number, status and
    /// <c>productCount</c>; customer, shipping and value fields are never read.
    /// </summary>
    public async Task<IReadOnlyList<TcgplayerOrderDetails>> GetOrderDetailsAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(orderNumbers);
        var details = new List<TcgplayerOrderDetails>();
        if (orderNumbers.Count == 0)
        {
            return details;
        }

        var storeKey = await StorePathAsync(cancellationToken).ConfigureAwait(false);
        foreach (var batch in orderNumbers.Chunk(_options.PageSize))
        {
            var response = await GetAsync<TcgplayerOrderDetails>(
                    $"{storeKey}/orders/{string.Join(',', batch.Select(Uri.EscapeDataString))}",
                    "The order details",
                    notFoundMeansEmpty: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            details.AddRange(
                response.Results ?? throw Invalid("The order details response has no results.")
            );
        }

        return details;
    }

    /// <summary>
    /// Call #6, paged: every item of one order and the <c>totalItems</c> (lines) it reported.
    /// </summary>
    public async Task<TcgplayerOrderItems> GetOrderItemsAsync(
        string orderNumber,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        var storeKey = await StorePathAsync(cancellationToken).ConfigureAwait(false);
        var escaped = Uri.EscapeDataString(orderNumber);
        var (items, totalItems) = await GetAllPagesAsync<TcgplayerOrderItem>(
                offset =>
                    $"{storeKey}/orders/{escaped}/items?includeItemDetails=true&{Page(offset)}",
                $"The items of order '{orderNumber}'",
                cancellationToken
            )
            .ConfigureAwait(false);
        return new TcgplayerOrderItems(items, totalItems);
    }

    /// <summary>
    /// Call #7, in batches of 50 distinct ids. A SKU absent from the results (listed in
    /// <c>errors</c>, or its batch answered 404) is simply not in the returned map.
    /// </summary>
    public Task<IReadOnlyDictionary<int, TcgplayerSku>> GetSkusAsync(
        IEnumerable<int> skuIds,
        CancellationToken cancellationToken
    ) =>
        GetCatalogAsync<TcgplayerSku>(
            skuIds,
            ids => $"catalog/skus/{ids}",
            sku => sku.SkuId,
            "The SKU lookup",
            cancellationToken
        );

    /// <summary>
    /// Call #8, in batches of 50 distinct ids, with extended fields (collector number, rarity).
    /// A product absent from the results is not found.
    /// </summary>
    public Task<IReadOnlyDictionary<int, TcgplayerProduct>> GetProductsAsync(
        IEnumerable<int> productIds,
        CancellationToken cancellationToken
    ) =>
        GetCatalogAsync<TcgplayerProduct>(
            productIds,
            ids => $"catalog/products/{ids}?getExtendedFields=true",
            product => product.ProductId,
            "The product lookup",
            cancellationToken
        );

    private async Task<string> ResolveStoreKeyAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<TcgplayerStoreSelf>(
                "stores/self",
                "The store lookup",
                notFoundMeansEmpty: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        return response.Results is [{ SellerKey: { } key }, ..] && !string.IsNullOrWhiteSpace(key)
            ? key
            : throw Invalid("The store lookup returned no store key.");
    }

    private async Task<string> StorePathAsync(CancellationToken cancellationToken) =>
        "stores/"
        + Uri.EscapeDataString(await GetStoreKeyAsync(cancellationToken).ConfigureAwait(false));

    private string Page(int offset) =>
        string.Create(CultureInfo.InvariantCulture, $"offset={offset}&limit={_options.PageSize}");

    // research.md §4: advance by the number of results actually returned until totalItems, and
    // treat an empty page before then as a malformed response so the loop can never spin.
    private async Task<(List<T> Results, int TotalItems)> GetAllPagesAsync<T>(
        Func<int, string> pathAt,
        string what,
        CancellationToken cancellationToken
    )
    {
        var results = new List<T>();
        while (true)
        {
            var response = await GetAsync<T>(
                    pathAt(results.Count),
                    what,
                    notFoundMeansEmpty: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (response.TotalItems is not { } totalItems || totalItems < 0)
            {
                throw Invalid($"{what} response has no totalItems.");
            }

            var page = response.Results ?? throw Invalid($"{what} response has no results.");
            results.AddRange(page);
            if (results.Count >= totalItems)
            {
                return (results, totalItems);
            }

            if (page.Count == 0)
            {
                throw Invalid(
                    $"{what} stalled: an empty page at offset {results.Count} of {totalItems}."
                );
            }
        }
    }

    private async Task<IReadOnlyDictionary<int, T>> GetCatalogAsync<T>(
        IEnumerable<int> ids,
        Func<string, string> pathFor,
        Func<T, int?> idOf,
        string what,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(ids);
        var found = new Dictionary<int, T>();
        foreach (var batch in ids.Distinct().Chunk(CatalogBatchSize))
        {
            var joined = string.Join(
                ',',
                batch.Select(id => id.ToString(CultureInfo.InvariantCulture))
            );
            var response = await GetAsync<T>(
                    pathFor(joined),
                    what,
                    notFoundMeansEmpty: true,
                    cancellationToken
                )
                .ConfigureAwait(false);
            foreach (var row in response.Results ?? [])
            {
                // A row without an id identifies nothing, so it counts as not found.
                if (row is not null && idOf(row) is { } id)
                {
                    found.TryAdd(id, row);
                }
            }
        }

        return found;
    }

    private async Task<TcgplayerResponse<T>> GetAsync<T>(
        string relativePath,
        string what,
        bool notFoundMeansEmpty,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await _http
                .GetAsync(relativePath, cancellationToken)
                .ConfigureAwait(false);

            if (notFoundMeansEmpty && response.StatusCode == HttpStatusCode.NotFound)
            {
                return new TcgplayerResponse<T> { Results = [] };
            }

            ThrowForStatus(response.StatusCode, what);

            TcgplayerResponse<T>? body;
            try
            {
                body = await response
                    .Content.ReadFromJsonAsync<TcgplayerResponse<T>>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // The JsonException is not attached: its message can quote part of the body.
                throw Invalid($"{what} response could not be read.");
            }
            catch (NotSupportedException)
            {
                throw Invalid($"{what} response is not JSON.");
            }

            return body ?? throw Invalid($"{what} response was empty.");
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TcgplayerFeedException(
                TcgplayerFeedFailure.Unavailable,
                $"{what} timed out.",
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            throw new TcgplayerFeedException(
                TcgplayerFeedFailure.Unavailable,
                $"{what} could not reach TCGplayer.",
                exception
            );
        }
    }

    private static void ThrowForStatus(HttpStatusCode status, string what)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            return;
        }

        var failure = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                TcgplayerFeedFailure.AccessRefused,
            HttpStatusCode.TooManyRequests => TcgplayerFeedFailure.Unavailable,
            _ when code >= 500 => TcgplayerFeedFailure.Unavailable,
            _ => TcgplayerFeedFailure.ResponseInvalid,
        };
        throw new TcgplayerFeedException(
            failure,
            string.Create(CultureInfo.InvariantCulture, $"{what} was answered with HTTP {code}.")
        );
    }

    private static TcgplayerFeedException Invalid(string message) =>
        new(TcgplayerFeedFailure.ResponseInvalid, message);
}
