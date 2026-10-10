using System.Net;
using System.Text.Json.Nodes;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.UnitTests.CardCatalog;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T029: the feed composes the typed client and the translator. It is tested over a real
/// <see cref="TcgplayerApiClient"/> on a stubbed HTTP handler and the synthetic fixtures (the
/// client has no interface; adding one only to mock it would be speculative, constitution XIII).
/// </summary>
public sealed class TcgplayerOrderFeedTests
{
    private const string BaseAddress = "https://api.tcgplayer.com/v1.39.0/";
    private const string StoreKey = "SYNCONFIGURED";

    // ---- Not configured ----

    [Fact]
    public async Task Without_credentials_every_call_throws_NotConfigured_before_any_request()
    {
        var harness = new Harness(configured: false);

        var listing = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOpenOrderNumbersAsync(CancellationToken.None)
        );
        var fetching = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOrdersAsync(["SYN-0001-A1"], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.NotConfigured, listing.Failure);
        Assert.Equal(TcgplayerFeedFailure.NotConfigured, fetching.Failure);
        Assert.Empty(harness.Requests);
    }

    // ---- Listing ----

    [Fact]
    public async Task Open_order_numbers_come_from_the_manifest_then_a_paged_search()
    {
        var harness = new Harness();

        var numbers = await harness.Feed.GetOpenOrderNumbersAsync(CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 10).Select(OrderNumber), numbers);
        Assert.Equal(
            ["manifest", "search", "search"],
            harness.Requests.Select(request => Classify(request.RequestUri!.AbsolutePath))
        );
        Assert.Contains("orderStatusIds=2", harness.Requests[1].RequestUri!.Query);
    }

    // ---- Fetching ----

    [Fact]
    public async Task Orders_are_fetched_details_first_then_items_per_order_then_batched_catalog()
    {
        var harness = new Harness();

        var orders = await harness.Feed.GetOrdersAsync(
            [OrderNumber(1), OrderNumber(2)],
            CancellationToken.None
        );

        Assert.Equal([OrderNumber(1), OrderNumber(2)], orders.Select(o => o.SourceOrderIdentifier));
        Assert.All(orders, order => Assert.Null(order.RejectedBySource));
        Assert.Equal([1, 2], orders.Select(o => o.Lines.Count));
        Assert.Equal(
            ["details", "items", "items", "skus", "products"],
            harness.Requests.Select(request => Classify(request.RequestUri!.AbsolutePath))
        );
        Assert.Equal(2, orders[1].Lines[1].Quantity);
        Assert.Equal("#001/100", orders[0].Lines[0].CollectorNumber);
    }

    [Fact]
    public async Task Details_requests_batch_at_PageSize_and_the_catalog_is_looked_up_once_for_all_orders()
    {
        var harness = new Harness(pageSize: 2);
        var wanted = new[] { 1, 2, 3 }.Select(OrderNumber).ToList();

        var orders = await harness.Feed.GetOrdersAsync(wanted, CancellationToken.None);

        Assert.Equal(wanted, orders.Select(o => o.SourceOrderIdentifier));
        var kinds = harness.Requests.Select(r => Classify(r.RequestUri!.AbsolutePath)).ToList();
        Assert.Equal(2, kinds.Count(kind => kind == "details"));
        Assert.Equal(1, kinds.Count(kind => kind == "skus"));
        Assert.Equal(1, kinds.Count(kind => kind == "products"));
    }

    [Fact]
    public async Task Results_follow_the_order_of_the_numbers_asked_for()
    {
        var harness = new Harness();
        var wanted = new[] { 3, 1, 2 }.Select(OrderNumber).ToList();

        var orders = await harness.Feed.GetOrdersAsync(wanted, CancellationToken.None);

        Assert.Equal(wanted, orders.Select(o => o.SourceOrderIdentifier));
    }

    [Fact]
    public async Task An_order_that_fails_the_count_check_is_rejected_IncompleteOrder_by_the_translator()
    {
        var harness = new Harness();

        var orders = await harness.Feed.GetOrdersAsync(
            [OrderNumber(1), OrderNumber(6)],
            CancellationToken.None
        );

        Assert.Null(orders[0].RejectedBySource);
        Assert.Equal(FailureType.IncompleteOrder, orders[1].RejectedBySource!.Value.Type);
    }

    [Fact]
    public async Task No_orders_asked_for_means_no_requests()
    {
        var harness = new Harness();

        var orders = await harness.Feed.GetOrdersAsync([], CancellationToken.None);

        Assert.Empty(orders);
        Assert.Empty(harness.Requests);
    }

    // ---- Per-order data problems ----

    [Fact]
    public async Task A_malformed_items_body_rejects_that_order_only_as_TcgplayerResponseInvalid()
    {
        var harness = new Harness();
        harness.Override(
            "items",
            request =>
                request.RequestUri!.AbsolutePath.Contains(OrderNumber(2))
                    ? Json("<html>Service page</html>")
                    : null
        );

        var orders = await harness.Feed.GetOrdersAsync(
            [OrderNumber(1), OrderNumber(2), OrderNumber(3)],
            CancellationToken.None
        );

        Assert.Equal(3, orders.Count);
        Assert.Null(orders[0].RejectedBySource);
        var (type, message) = orders[1].RejectedBySource!.Value;
        Assert.Equal(FailureType.TcgplayerResponseInvalid, type);
        Assert.Contains(OrderNumber(2), message);
        Assert.DoesNotContain("Service page", message);
        Assert.Empty(orders[1].Lines);
        Assert.Null(orders[2].RejectedBySource);
        Assert.Single(orders[2].Lines);
    }

    [Fact]
    public async Task A_rejected_order_contributes_nothing_to_the_catalog_lookups()
    {
        var harness = new Harness();
        harness.Override(
            "items",
            request =>
                request.RequestUri!.AbsolutePath.Contains(OrderNumber(2)) ? Json("not json") : null
        );

        await harness.Feed.GetOrdersAsync([OrderNumber(1), OrderNumber(2)], CancellationToken.None);

        // Only order 1's SKU (7000001) is looked up; order 2's SKUs are not.
        var skuRequest = Assert.Single(
            harness.Requests,
            request => Classify(request.RequestUri!.AbsolutePath) == "skus"
        );
        Assert.EndsWith("/skus/7000001", skuRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task An_order_missing_from_the_details_response_is_rejected_not_skipped()
    {
        var harness = new Harness();
        harness.Override(
            "details",
            request =>
            {
                var all = JsonNode.Parse(Fixture("order-details.json"))!;
                all["results"] = new JsonArray(
                    all["results"]!
                        .AsArray()
                        .Where(row => (string)row!["orderNumber"]! == OrderNumber(1))
                        .Select(row => row!.DeepClone())
                        .ToArray()
                );
                return Json(all.ToJsonString());
            }
        );

        var orders = await harness.Feed.GetOrdersAsync(
            [OrderNumber(1), OrderNumber(2)],
            CancellationToken.None
        );

        Assert.Null(orders[0].RejectedBySource);
        Assert.Equal(OrderNumber(2), orders[1].SourceOrderIdentifier);
        var (type, message) = orders[1].RejectedBySource!.Value;
        Assert.Equal(FailureType.TcgplayerResponseInvalid, type);
        Assert.Equal($"{OrderNumber(2)}: TCGplayer returned no details for this order", message);
        // No items request was wasted on the order with no details.
        Assert.Single(
            harness.Requests,
            request => Classify(request.RequestUri!.AbsolutePath) == "items"
        );
    }

    // ---- Attempt-wide failures propagate ----

    [Fact]
    public async Task An_unavailable_items_call_fails_the_whole_attempt()
    {
        var harness = new Harness();
        harness.Override(
            "items",
            request =>
                request.RequestUri!.AbsolutePath.Contains(OrderNumber(2))
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : null
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOrdersAsync([OrderNumber(1), OrderNumber(2)], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task A_refused_items_call_fails_the_whole_attempt()
    {
        var harness = new Harness();
        harness.Override("items", _ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOrdersAsync([OrderNumber(1)], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.AccessRefused, failure.Failure);
    }

    [Fact]
    public async Task A_malformed_details_body_fails_the_whole_attempt()
    {
        var harness = new Harness();
        harness.Override("details", _ => Json("not json"));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOrdersAsync([OrderNumber(1)], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
    }

    [Fact]
    public async Task A_malformed_search_body_fails_the_listing()
    {
        var harness = new Harness();
        harness.Override("search", _ => Json("not json"));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOpenOrderNumbersAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
    }

    [Fact]
    public async Task An_unavailable_catalog_fails_the_whole_attempt()
    {
        var harness = new Harness();
        harness.Override("skus", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Feed.GetOrdersAsync([OrderNumber(1)], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    // ---- Call count ----

    [Fact]
    public void CallCount_reports_the_rate_limiters_total()
    {
        var harness = new Harness();

        Assert.Equal(harness.Limiter.CallCount, harness.Feed.CallCount);
    }

    // ---- Harness ----

    private static string OrderNumber(int index) => $"SYN-{index:0000}-A1";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tcgplayer", name));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string Classify(string path)
    {
        if (path.Contains("/catalog/skus/", StringComparison.Ordinal))
            return "skus";
        if (path.Contains("/catalog/products/", StringComparison.Ordinal))
            return "products";
        var segments = path[$"/v1.39.0/stores/{StoreKey}/".Length..].Split('/');
        return segments switch
        {
            ["orders"] => "search",
            ["orders", "manifest"] => "manifest",
            ["orders", _] => "details",
            ["orders", _, "items"] => "items",
            _ => throw new InvalidOperationException($"No synthetic route for {path}"),
        };
    }

    private static int QueryInt(HttpRequestMessage request, string name) =>
        int.Parse(
            request
                .RequestUri!.Query.TrimStart('?')
                .Split('&')
                .Select(pair => pair.Split('='))
                .Single(pair => pair[0] == name)[1]
        );

    private sealed class Harness
    {
        private readonly Dictionary<
            string,
            Func<HttpRequestMessage, HttpResponseMessage?>
        > _overrides = [];
        private readonly StubHttpMessageHandler _handler;

        public Harness(bool configured = true, int pageSize = 5)
        {
            var options = new TcgplayerOptions
            {
                StoreKey = StoreKey,
                PageSize = pageSize,
                PublicKey = configured ? "synthetic-public-id" : null,
                PrivateKey = configured ? "synthetic-private-id" : null,
                AccessToken = configured ? "synthetic-store-access" : null,
            };
            _handler = StubHttpMessageHandler.RespondingPerRequest(Respond);
            Limiter = new TcgplayerRateLimiter(120, TimeProvider.System);
            var client = new TcgplayerApiClient(
                new HttpClient(_handler, disposeHandler: false)
                {
                    BaseAddress = new Uri(BaseAddress),
                },
                options,
                new TcgplayerStoreKeyCache()
            );
            Feed = new TcgplayerOrderFeed(client, options, Limiter);
        }

        public TcgplayerOrderFeed Feed { get; }

        public TcgplayerRateLimiter Limiter { get; }

        public List<HttpRequestMessage> Requests => _handler.Requests;

        /// <summary>A null return from the override falls through to the fixture.</summary>
        public void Override(
            string route,
            Func<HttpRequestMessage, HttpResponseMessage?> respond
        ) => _overrides[route] = respond;

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var route = Classify(path);
            if (_overrides.TryGetValue(route, out var respond) && respond(request) is { } custom)
                return custom;

            return Json(
                route switch
                {
                    "manifest" => Fixture("manifest.json"),
                    "search" => Fixture(
                        QueryInt(request, "offset") == 0 ? "search-page1.json" : "search-page2.json"
                    ),
                    "details" => DetailsFor(path.Split('/')[^1].Split(',')),
                    "items" => ItemsFor(path.Split('/')[^2]),
                    "skus" => Fixture("skus.json"),
                    _ => Fixture("products.json"),
                }
            );
        }

        private static string DetailsFor(string[] numbers)
        {
            var all = JsonNode.Parse(Fixture("order-details.json"))!;
            all["results"] = new JsonArray(
                all["results"]!
                    .AsArray()
                    .Where(row => numbers.Contains((string)row!["orderNumber"]!))
                    .Select(row => row!.DeepClone())
                    .ToArray()
            );
            return all.ToJsonString();
        }

        private static string ItemsFor(string orderNumber) =>
            Fixture(
                orderNumber switch
                {
                    "SYN-0001-A1" => "items-normal.json",
                    "SYN-0002-A1" => "items-quantity-greater-than-one.json",
                    "SYN-0003-A1" => "items-foil.json",
                    "SYN-0006-A1" => "items-count-mismatch.json",
                    _ => throw new InvalidOperationException(
                        $"No synthetic items for {orderNumber}"
                    ),
                }
            );
    }
}
