using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LootSingles.Application.Import;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.UnitTests.CardCatalog;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T027: the typed client for TCGplayer calls #2–#8 (contracts/tcgplayer-upstream.md). It only
/// fetches and pages; translation is the translator's job. Driven by the synthetic fixtures in
/// LootSingles.Fixtures/Tcgplayer, whose search pages assume a page size of 5 and whose paged
/// items assume a page size of 2. The auth and rate-limit handlers are not in this pipeline; they
/// have their own tests.
/// </summary>
public sealed class TcgplayerApiClientTests
{
    private const string BaseAddress = "https://api.tcgplayer.com/v1.39.0/";
    private const string FixtureStoreKey = "SYNSTORE1";
    private const string ConfiguredStoreKey = "SYNCONFIGURED";

    // ---- Store key (#2) ----

    [Fact]
    public async Task GetStoreKeyAsync_uses_the_configured_store_key_without_calling_TCGplayer()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        var key = await harness.Client.GetStoreKeyAsync(CancellationToken.None);

        Assert.Equal(ConfiguredStoreKey, key);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task GetStoreKeyAsync_without_a_configured_key_calls_stores_self_once_per_process()
    {
        var harness = new Harness();

        var first = await harness.Client.GetStoreKeyAsync(CancellationToken.None);
        // Typed clients are transient: a second client in the same process shares the cache.
        var second = await harness.NewClient().GetStoreKeyAsync(CancellationToken.None);

        Assert.Equal(FixtureStoreKey, first);
        Assert.Equal(FixtureStoreKey, second);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1.39.0/stores/self", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Concurrent_store_key_lookups_share_one_stores_self_call()
    {
        var harness = new Harness();

        var keys = await Task.WhenAll(
            Enumerable
                .Range(0, 5)
                .Select(_ => harness.NewClient().GetStoreKeyAsync(CancellationToken.None))
        );

        Assert.All(keys, key => Assert.Equal(FixtureStoreKey, key));
        Assert.Single(harness.Requests);
    }

    // R28: the live field name for the store key is unverified, so both documented spellings are
    // accepted and member names match case-insensitively. Bodies are synthetic.
    [Theory]
    [InlineData("""{"results":[{"storeKey":"SYNSTORE1"}]}""")]
    [InlineData("""{"results":[{"StoreKey":"SYNSTORE1"}]}""")]
    [InlineData("""{"results":[{"sellerKey":"SYNSTORE1"}]}""")]
    [InlineData("""{"results":[{"SellerKey":"SYNSTORE1"}]}""")]
    [InlineData("""{"results":[{"storeKey":" ","sellerKey":"SYNSTORE1"}]}""")]
    public async Task The_store_key_is_read_from_storeKey_or_sellerKey_in_any_case(string body)
    {
        var harness = new Harness();
        harness.Override(Route.StoreSelf, Json(body));

        var key = await harness.Client.GetStoreKeyAsync(CancellationToken.None);

        Assert.Equal(FixtureStoreKey, key);
    }

    [Theory]
    [InlineData("""{"results":[{}]}""")]
    [InlineData("""{"results":[{"storeKey":"","sellerKey":" "}]}""")]
    [InlineData("""{"results":[{"storeName":"Synthetic Test Store"}]}""")]
    public async Task A_stores_self_row_with_no_usable_key_is_ResponseInvalid(string body)
    {
        var harness = new Harness();
        harness.Override(Route.StoreSelf, Json(body));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetStoreKeyAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
    }

    [Fact]
    public async Task A_stores_self_response_without_a_seller_key_is_ResponseInvalid()
    {
        var harness = new Harness();
        harness.Override(Route.StoreSelf, Json("""{"success":true,"errors":[],"results":[{}]}"""));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetStoreKeyAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
    }

    // ---- Manifest (#3) ----

    [Fact]
    public async Task Open_status_names_resolve_to_ids_through_the_manifest()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        var ids = await harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None);

        Assert.Equal([2], ids); // "Ready To Ship", live-confirmed spelling
        var request = Assert.Single(harness.Requests);
        Assert.Equal(
            $"/v1.39.0/stores/{ConfiguredStoreKey}/orders/manifest",
            request.RequestUri!.AbsolutePath
        );
    }

    [Fact]
    public async Task Several_configured_status_names_each_resolve()
    {
        var harness = new Harness(
            storeKey: ConfiguredStoreKey,
            openStatuses: ["Processing", "Ready To Ship"]
        );

        var ids = await harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None);

        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public async Task A_configured_status_missing_from_the_manifest_is_ResponseInvalid_naming_the_status()
    {
        var harness = new Harness(
            storeKey: ConfiguredStoreKey,
            openStatuses: ["Ready To Ship", "Awaiting Pickup"]
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
        Assert.Contains("Awaiting Pickup", failure.Message);
    }

    [Fact]
    public async Task Status_names_match_exactly_so_a_different_spelling_is_missing()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey, openStatuses: ["Ready to Ship"]);

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
        Assert.Contains("Ready to Ship", failure.Message);
    }

    // ---- Search (#4) ----

    [Fact]
    public async Task Search_pages_advance_by_results_returned_until_totalItems()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        var numbers = await harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 10).Select(OrderNumber), numbers);
        Assert.Collection(
            harness.Requests,
            first =>
                Assert.Equal("orderStatusIds=2&offset=0&limit=5", first.RequestUri!.Query[1..]),
            second =>
                Assert.Equal("orderStatusIds=2&offset=5&limit=5", second.RequestUri!.Query[1..])
        );
    }

    [Fact]
    public async Task Search_advances_by_the_count_actually_returned_when_the_server_caps_the_page()
    {
        // The server returns 3 per page whatever limit is asked for.
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            request =>
            {
                var offset = QueryInt(request, "offset");
                var page = Enumerable
                    .Range(offset + 1, Math.Min(3, 7 - offset))
                    .Select(OrderNumber);
                return Json(SearchBody(page, totalItems: 7))(request);
            }
        );

        var numbers = await harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 7).Select(OrderNumber), numbers);
        Assert.Equal(
            [0, 3, 6],
            harness.Requests.Select(request => QueryInt(request, "offset")).ToArray()
        );
    }

    [Fact]
    public async Task Search_with_no_open_orders_makes_one_request()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Search, Json(SearchBody([], totalItems: 0)));

        var numbers = await harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None);

        Assert.Empty(numbers);
        Assert.Single(harness.Requests);
    }

    [Fact]
    public async Task A_zero_result_search_page_before_totalItems_is_ResponseInvalid()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            request =>
                QueryInt(request, "offset") == 0
                    ? Json(SearchBody(Enumerable.Range(1, 5).Select(OrderNumber), totalItems: 10))(
                        request
                    )
                    : Json(SearchBody([], totalItems: 10))(request)
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
        Assert.Equal(2, harness.Requests.Count);
    }

    [Fact]
    public async Task Search_drops_an_order_number_repeated_across_pages_keeping_first_seen_order()
    {
        // Offset paging can return a number twice if orders change between page requests.
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            request =>
                QueryInt(request, "offset") == 0
                    ? Json(SearchBody(Enumerable.Range(1, 5).Select(OrderNumber), totalItems: 10))(
                        request
                    )
                    // SYN-0005-A1 again: it shifted down a place between the two requests.
                    : Json(SearchBody(Enumerable.Range(5, 5).Select(OrderNumber), totalItems: 10))(
                        request
                    )
        );

        var numbers = await harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None);

        Assert.Equal(Enumerable.Range(1, 9).Select(OrderNumber), numbers);
        Assert.Equal(2, harness.Requests.Count);
    }

    [Fact]
    public async Task An_errors_member_of_any_shape_is_ignored()
    {
        // The envelope does not type "errors" or "success"; absence from results is what counts.
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            Json(
                """{"success":"partial","errors":[{"code":7,"detail":"synthetic"}],"totalItems":1,"results":["SYN-0001-A1"]}"""
            )
        );

        var numbers = await harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None);

        Assert.Equal(["SYN-0001-A1"], numbers);
    }

    [Fact]
    public async Task Search_refuses_an_empty_status_filter_rather_than_searching_every_order()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.SearchOrderNumbersAsync([], CancellationToken.None)
        );
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task A_search_response_without_totalItems_is_ResponseInvalid()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            Json("""{"success":true,"errors":[],"results":["SYN-0001-A1"]}""")
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
    }

    // ---- Order details (#5) ----

    [Fact]
    public async Task Order_details_are_fetched_in_batches_of_PageSize_and_bind_only_the_read_fields()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        var wanted = Enumerable.Range(1, 7).Select(OrderNumber).ToList();

        var details = await harness.Client.GetOrderDetailsAsync(wanted, CancellationToken.None);

        Assert.Equal(wanted, details.Select(d => d.OrderNumber));
        Assert.Collection(
            harness.Requests,
            first =>
                Assert.Equal(
                    $"/v1.39.0/stores/{ConfiguredStoreKey}/orders/"
                        + string.Join(',', wanted.Take(5)),
                    first.RequestUri!.AbsolutePath
                ),
            second =>
                Assert.Equal(
                    $"/v1.39.0/stores/{ConfiguredStoreKey}/orders/"
                        + string.Join(',', wanted.Skip(5)),
                    second.RequestUri!.AbsolutePath
                )
        );
        var mismatch = details.Single(d => d.OrderNumber == "SYN-0006-A1");
        Assert.Equal(5, mismatch.ProductCount);
        Assert.Equal(2, mismatch.OrderStatusTypeId);
    }

    // ---- Order items (#6) ----

    [Fact]
    public async Task Items_pages_advance_by_results_returned_until_totalItems()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey, pageSize: 2);

        var page = await harness.Client.GetOrderItemsAsync("SYN-0005-A1", CancellationToken.None);

        Assert.Equal(3, page.TotalItems);
        Assert.Equal([7000006, 7000007, 7000008], page.Items.Select(item => item.SkuId));
        Assert.Collection(
            harness.Requests,
            first =>
            {
                Assert.Equal(
                    $"/v1.39.0/stores/{ConfiguredStoreKey}/orders/SYN-0005-A1/items",
                    first.RequestUri!.AbsolutePath
                );
                Assert.Equal(
                    "includeItemDetails=true&offset=0&limit=2",
                    first.RequestUri.Query[1..]
                );
            },
            second =>
                Assert.Equal(
                    "includeItemDetails=true&offset=2&limit=2",
                    second.RequestUri!.Query[1..]
                )
        );
    }

    [Fact]
    public async Task A_zero_result_items_page_before_totalItems_is_ResponseInvalid()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Items,
            request =>
                QueryInt(request, "offset") == 0
                    ? Json(Fixture("items-paged-page1.json"))(request)
                    : Json("""{"success":true,"errors":[],"totalItems":3,"results":[]}""")(request)
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOrderItemsAsync("SYN-0005-A1", CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
        Assert.Contains("SYN-0005-A1", failure.Message);
    }

    [Fact]
    public async Task An_items_response_without_totalItems_is_ResponseInvalid()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Items, Json("""{"success":true,"errors":[],"results":[]}"""));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOrderItemsAsync("SYN-0001-A1", CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
    }

    // ---- Catalog (#7, #8) ----

    [Fact]
    public async Task Sku_lookups_are_chunked_at_50_distinct_ids()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Skus, Json(Fixture("skus.json")));
        var ids = Enumerable.Range(7000001, 120).Concat([7000001, 7000002]).ToList();

        await harness.Client.GetSkusAsync(ids, CancellationToken.None);

        var chunks = harness
            .Requests.Select(request => IdsInPath(request, "/v1.39.0/catalog/skus/"))
            .ToList();
        Assert.Equal([50, 50, 20], chunks.Select(chunk => chunk.Count));
        Assert.Equal(Enumerable.Range(7000001, 120), chunks.SelectMany(chunk => chunk));
    }

    [Fact]
    public async Task Skus_listed_in_errors_are_not_found()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        var skus = await harness.Client.GetSkusAsync([7000001, 7000099], CancellationToken.None);

        Assert.Equal(8000001, skus[7000001].ProductId);
        Assert.False(skus.ContainsKey(7000099));
    }

    [Fact]
    public async Task A_404_on_a_sku_batch_means_every_sku_in_it_is_not_found()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Skus,
            Json(
                """{"success":false,"errors":["No SKUs found"],"results":[]}""",
                HttpStatusCode.NotFound
            )
        );

        var skus = await harness.Client.GetSkusAsync([7000001, 7000002], CancellationToken.None);

        Assert.Empty(skus);
    }

    [Fact]
    public async Task Product_lookups_are_chunked_at_50_and_ask_for_extended_fields()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Products, Json(Fixture("products.json")));
        var ids = Enumerable.Range(8000001, 51).ToList();

        await harness.Client.GetProductsAsync(ids, CancellationToken.None);

        Assert.Equal(
            [50, 1],
            harness.Requests.Select(request =>
                IdsInPath(request, "/v1.39.0/catalog/products/").Count
            )
        );
        Assert.All(
            harness.Requests,
            request => Assert.Equal("?getExtendedFields=true", request.RequestUri!.Query)
        );
    }

    [Fact]
    public async Task Products_listed_in_errors_are_not_found_and_found_products_keep_extended_data()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        var products = await harness.Client.GetProductsAsync(
            [8000001, 8000011],
            CancellationToken.None
        );

        var found = products[8000001];
        Assert.Equal("https://img.example.test/synthetic/product-8000001.jpg", found.ImageUrl);
        Assert.Contains(found.ExtendedData!, data => data is { Name: "Number", Value: "001/100" });
        Assert.False(products.ContainsKey(8000011));
    }

    [Fact]
    public async Task A_404_on_a_product_batch_means_every_product_in_it_is_not_found()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Products, _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var products = await harness.Client.GetProductsAsync([8000001], CancellationToken.None);

        Assert.Empty(products);
    }

    [Fact]
    public async Task A_207_partial_catalog_result_is_read()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Skus, Json(Fixture("skus.json"), (HttpStatusCode)207));

        var skus = await harness.Client.GetSkusAsync([7000001, 7000099], CancellationToken.None);

        Assert.True(skus.ContainsKey(7000001));
        Assert.False(skus.ContainsKey(7000099));
    }

    [Fact]
    public async Task No_catalog_ids_means_no_catalog_request()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);

        Assert.Empty(await harness.Client.GetSkusAsync([], CancellationToken.None));
        Assert.Empty(await harness.Client.GetProductsAsync([], CancellationToken.None));
        Assert.Empty(harness.Requests);
    }

    // ---- Failure mapping ----

    public static TheoryData<int> UnavailableStatuses => new() { 429, 500, 502, 503, 504 };

    [Theory]
    [MemberData(nameof(UnavailableStatuses))]
    public async Task Status_429_and_5xx_are_Unavailable(int status)
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(Route.Manifest, _ => new HttpResponseMessage((HttpStatusCode)status));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task Status_5xx_on_a_catalog_call_is_Unavailable_not_not_found()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Skus,
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetSkusAsync([7000001], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task Status_403_is_AccessRefused()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            Json("""{"success":false,"errors":["Request forbidden."]}""", HttpStatusCode.Forbidden)
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.AccessRefused, failure.Failure);
    }

    [Fact]
    public async Task A_timeout_is_Unavailable()
    {
        var harness = new Harness(
            storeKey: ConfiguredStoreKey,
            thrown: new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout.",
                new TimeoutException()
            )
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task A_network_error_is_Unavailable()
    {
        var harness = new Harness(
            storeKey: ConfiguredStoreKey,
            thrown: new HttpRequestException("No such host is known.")
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOrderItemsAsync("SYN-0001-A1", CancellationToken.None)
        );

        Assert.Equal(TcgplayerFeedFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_Unavailable()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Client.GetOpenOrderStatusIdsAsync(cancelled.Token)
        );
    }

    [Fact]
    public async Task A_feed_failure_raised_by_the_auth_handler_propagates_unchanged()
    {
        var raised = new TcgplayerFeedException(TcgplayerFeedFailure.AccessRefused, "synthetic");
        var harness = new Harness(storeKey: ConfiguredStoreKey, thrown: raised);

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None)
        );

        Assert.Same(raised, failure);
    }

    public static TheoryData<Route, string> UnparseableBodies =>
        new()
        {
            { Route.Manifest, "not json at all" },
            { Route.Search, "{\"totalItems\":\"ten\",\"results\":[]}" },
            { Route.Items, "<html>Service page</html>" },
            { Route.Skus, "{\"results\":{\"skuId\":1}}" },
        };

    [Theory]
    [MemberData(nameof(UnparseableBodies))]
    public async Task An_unparseable_body_is_ResponseInvalid(Route route, string body)
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(route, Json(body));

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            route switch
            {
                Route.Manifest => harness.Client.GetOpenOrderStatusIdsAsync(CancellationToken.None),
                Route.Search => harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None),
                Route.Items => harness.Client.GetOrderItemsAsync(
                    "SYN-0001-A1",
                    CancellationToken.None
                ),
                _ => harness.Client.GetSkusAsync([7000001], CancellationToken.None),
            }
        );

        Assert.Equal(TcgplayerFeedFailure.ResponseInvalid, failure.Failure);
        Assert.DoesNotContain(body, failure.Message);
    }

    [Fact]
    public async Task A_failure_message_never_carries_the_response_body()
    {
        var harness = new Harness(storeKey: ConfiguredStoreKey);
        harness.Override(
            Route.Search,
            Json(
                """{"secret-marker":"customer@example.test"}""",
                HttpStatusCode.InternalServerError
            )
        );

        var failure = await Assert.ThrowsAsync<TcgplayerFeedException>(() =>
            harness.Client.SearchOrderNumbersAsync([2], CancellationToken.None)
        );

        Assert.DoesNotContain("secret-marker", failure.ToString());
        Assert.DoesNotContain("customer@example.test", failure.ToString());
    }

    // ---- Configuration guard ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_PageSize_below_one_is_refused_so_paging_can_never_spin(int pageSize)
    {
        var handler = StubHttpMessageHandler.ReturningJson("{}");
        var options = new TcgplayerOptions { PageSize = pageSize };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TcgplayerApiClient(
                new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) },
                options,
                new TcgplayerStoreKeyCache()
            )
        );
        Assert.Empty(handler.Requests);
    }

    // ---- The closed list of allowed requests ----

    // contracts/tcgplayer-upstream.md, calls #2–#8 (the token request, #1, is the auth handler's).
    // Each entry is a GET path pattern and the exact query-parameter names it may carry.
    private static readonly (Regex Path, string[] Query)[] AllowedRequests =
    [
        (new Regex(@"^/v1\.39\.0/stores/self$"), []),
        (new Regex(@"^/v1\.39\.0/stores/[^/]+/orders/manifest$"), []),
        (new Regex(@"^/v1\.39\.0/stores/[^/]+/orders$"), ["orderStatusIds", "offset", "limit"]),
        (new Regex(@"^/v1\.39\.0/stores/[^/]+/orders/(?!manifest$)[^/]+$"), []),
        (
            new Regex(@"^/v1\.39\.0/stores/[^/]+/orders/[^/]+/items$"),
            ["includeItemDetails", "offset", "limit"]
        ),
        (new Regex(@"^/v1\.39\.0/catalog/skus/\d+(,\d+)*$"), []),
        (new Regex(@"^/v1\.39\.0/catalog/products/\d+(,\d+)*$"), ["getExtendedFields"]),
    ];

    [Fact]
    public async Task Every_request_is_a_GET_on_the_allowed_list_and_nothing_else()
    {
        // No configured store key, so the walk includes /stores/self too.
        var harness = new Harness();
        var client = harness.Client;

        var statusIds = await client.GetOpenOrderStatusIdsAsync(CancellationToken.None);
        var numbers = await client.SearchOrderNumbersAsync(statusIds, CancellationToken.None);
        await client.GetOrderDetailsAsync(numbers, CancellationToken.None);
        var skuIds = new List<int>();
        foreach (var number in numbers)
        {
            var page = await client.GetOrderItemsAsync(number, CancellationToken.None);
            skuIds.AddRange(page.Items.Select(item => item.SkuId!.Value));
        }

        var skus = await client.GetSkusAsync(skuIds, CancellationToken.None);
        await client.GetProductsAsync(
            skus.Values.Select(sku => sku.ProductId!.Value).ToList(),
            CancellationToken.None
        );

        // Every one of calls #2–#8 was exercised...
        Assert.All(
            AllowedRequests,
            allowed =>
                Assert.Contains(
                    harness.Requests,
                    request => allowed.Path.IsMatch(request.RequestUri!.AbsolutePath)
                )
        );
        // ...and nothing outside the list was sent.
        Assert.All(
            harness.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Null(request.Content);
                Assert.Equal("api.tcgplayer.com", request.RequestUri!.Host);
                Assert.Equal("https", request.RequestUri.Scheme);
                var path = request.RequestUri.AbsolutePath;
                var match = AllowedRequests.Where(allowed => allowed.Path.IsMatch(path)).ToList();
                var allowed = Assert.Single(match);
                Assert.Equal(allowed.Query, QueryNames(request));
                Assert.DoesNotContain("authorize", path, StringComparison.OrdinalIgnoreCase);
            }
        );
    }

    // ---- Harness ----

    private static string OrderNumber(int index) => $"SYN-{index:0000}-A1";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tcgplayer", name));

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(
        string body,
        HttpStatusCode status = HttpStatusCode.OK
    ) => _ => new HttpResponseMessage(status) { Content = new StringContent(body) };

    private static string SearchBody(IEnumerable<string> numbers, int totalItems) =>
        new JsonObject
        {
            ["success"] = true,
            ["errors"] = new JsonArray(),
            ["totalItems"] = totalItems,
            ["results"] = new JsonArray([.. numbers.Select(n => JsonValue.Create(n))]),
        }.ToJsonString();

    private static int QueryInt(HttpRequestMessage request, string name) =>
        int.Parse(QueryValue(request, name)!);

    private static string? QueryValue(HttpRequestMessage request, string name) =>
        request
            .RequestUri!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .Where(pair => pair[0] == name)
            .Select(pair => pair[1])
            .SingleOrDefault();

    private static string[] QueryNames(HttpRequestMessage request) =>
        request
            .RequestUri!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=')[0])
            .ToArray();

    private static List<int> IdsInPath(HttpRequestMessage request, string prefix)
    {
        var path = request.RequestUri!.AbsolutePath;
        Assert.StartsWith(prefix, path);
        return path[prefix.Length..].Split(',').Select(int.Parse).ToList();
    }

    /// <summary>The kinds of request in the allowed list, for routing the stub.</summary>
    public enum Route
    {
        StoreSelf,
        Manifest,
        Search,
        Details,
        Items,
        Skus,
        Products,
    }

    private static Route Classify(string path)
    {
        const string stores = "/v1.39.0/stores/";
        if (path.StartsWith("/v1.39.0/catalog/skus/", StringComparison.Ordinal))
            return Route.Skus;
        if (path.StartsWith("/v1.39.0/catalog/products/", StringComparison.Ordinal))
            return Route.Products;
        if (path == stores + "self")
            return Route.StoreSelf;

        // {storeKey}/orders[/manifest | /{numbers}[/items]]
        var segments = path[stores.Length..].Split('/');
        return segments.Length switch
        {
            2 => Route.Search,
            3 when segments[2] == "manifest" => Route.Manifest,
            3 => Route.Details,
            4 when segments[3] == "items" => Route.Items,
            _ => throw new InvalidOperationException($"No synthetic route for {path}"),
        };
    }

    /// <summary>
    /// Routes each request to its synthetic fixture. Order details return only the order numbers
    /// requested; search and items pages are chosen by offset. A test can override one route.
    /// </summary>
    private sealed class Harness
    {
        private readonly TcgplayerOptions _options;
        private readonly TcgplayerStoreKeyCache _storeKeyCache = new();
        private readonly StubHttpMessageHandler _handler;
        private readonly Dictionary<
            Route,
            Func<HttpRequestMessage, HttpResponseMessage>
        > _overrides = [];

        public Harness(
            string? storeKey = null,
            IReadOnlyList<string>? openStatuses = null,
            Exception? thrown = null,
            int pageSize = 5
        )
        {
            _options = new TcgplayerOptions
            {
                StoreKey = storeKey,
                OpenOrderStatuses = openStatuses ?? ["Ready To Ship"],
                PageSize = pageSize,
            };
            _handler = thrown is null
                ? StubHttpMessageHandler.RespondingPerRequest(Respond)
                : StubHttpMessageHandler.Throwing(thrown);
            Client = NewClient();
        }

        public TcgplayerApiClient Client { get; }

        public List<HttpRequestMessage> Requests => _handler.Requests;

        public TcgplayerApiClient NewClient() =>
            new(
                new HttpClient(_handler, disposeHandler: false)
                {
                    BaseAddress = new Uri(BaseAddress),
                },
                _options,
                _storeKeyCache
            );

        public void Override(Route route, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _overrides[route] = respond;

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var route = Classify(path);
            if (_overrides.TryGetValue(route, out var respond))
                return respond(request);

            var body = route switch
            {
                Route.StoreSelf => Fixture("stores-self.json"),
                Route.Manifest => Fixture("manifest.json"),
                Route.Search => Fixture(
                    QueryInt(request, "offset") == 0 ? "search-page1.json" : "search-page2.json"
                ),
                Route.Details => DetailsFor(path.Split('/')[^1].Split(',')),
                Route.Items => ItemsFor(path.Split('/')[^2], QueryInt(request, "offset")),
                Route.Skus => Fixture("skus.json"),
                _ => Fixture("products.json"),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }

        private static string DetailsFor(string[] numbers)
        {
            var all = JsonNode.Parse(Fixture("order-details.json"))!;
            var wanted = all["results"]!
                .AsArray()
                .Where(row => numbers.Contains((string)row!["orderNumber"]!))
                .Select(row => row!.DeepClone())
                .ToArray();
            all["results"] = new JsonArray(wanted);
            return all.ToJsonString();
        }

        private static string ItemsFor(string orderNumber, int offset) =>
            Fixture(
                orderNumber switch
                {
                    "SYN-0001-A1" => "items-normal.json",
                    "SYN-0002-A1" => "items-quantity-greater-than-one.json",
                    "SYN-0003-A1" => "items-foil.json",
                    "SYN-0004-A1" => "items-non-english.json",
                    "SYN-0005-A1" => offset == 0
                        ? "items-paged-page1.json"
                        : "items-paged-page2.json",
                    "SYN-0006-A1" => "items-count-mismatch.json",
                    "SYN-0007-A1" => "items-missing-product-name.json",
                    "SYN-0008-A1" => "items-normal-printing-lightly-played.json",
                    "SYN-0009-A1" => "items-sealed.json",
                    "SYN-0010-A1" => "items-catalog-not-found.json",
                    _ => throw new InvalidOperationException(
                        $"No synthetic items for {orderNumber}"
                    ),
                }
            );
    }
}
