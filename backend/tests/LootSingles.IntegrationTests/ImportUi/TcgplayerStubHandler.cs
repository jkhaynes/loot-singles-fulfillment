using System.Net;
using System.Text.Json.Nodes;

namespace LootSingles.IntegrationTests.ImportUi;

/// <summary>
/// A stand-in for TCGplayer that serves the synthetic fixtures in
/// LootSingles.Fixtures/Tcgplayer (never live data). Search and items page by the requested
/// offset and limit; order details are filtered to the requested numbers. The search honours its
/// filters the way the live search does, each covering only part of the orders (research.md §3):
/// <c>orderStatusIds</c> and <c>orderTypeIds</c> narrow only shipped orders and
/// <c>pickupStatusIds</c> only in-store pickup orders, so a search missing a filter leaks the
/// orders only that filter excludes. Every request is recorded (method, path, query, User-Agent)
/// so tests can check the agreement guards.
/// </summary>
internal sealed class TcgplayerStubHandler : HttpMessageHandler
{
    public const string BaseUrl = "https://tcgplayer-stub.test/";

    private readonly Lock _gate = new();
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>The open orders the search reports, in order. Defaults to the 10 fixture orders.</summary>
    public IReadOnlyList<string> OpenOrderNumbers { get; set; } =
        Enumerable.Range(1, 10).Select(OrderNumber).ToArray();

    /// <summary>
    /// Orders the store also has that are not open, each excluded by one filter only:
    /// SYN-0011 (shipped, Delivered) by <c>orderStatusIds</c>, SYN-0012 (in-store pickup, Picked
    /// Up) by <c>pickupStatusIds</c> and SYN-0013 (shipped, Direct) by <c>orderTypeIds</c>. The
    /// search returns one only when its filter is missing. They have details but no items; an
    /// import that fetched their items would fail.
    /// </summary>
    public static IReadOnlyList<string> LeakedOrderNumbers { get; } =
        new[] { 11, 12, 13 }.Select(OrderNumber).ToArray();

    /// <summary>
    /// Runs before the fixtures for every non-token request; a non-null response is returned
    /// instead. It may await (the concurrency test uses that to hold requests).
    /// </summary>
    public Func<RecordedRequest, Task<HttpResponseMessage?>>? Override { get; set; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public static string OrderNumber(int index) => $"SYN-{index:0000}-A1";

    /// <summary>The route a recorded path names: token, store, manifest, search, details, items, skus, products.</summary>
    public static string Classify(string path)
    {
        var segments = path.Trim('/').Split('/');
        if (segments is ["token"])
            return "token";
        if (segments.Contains("catalog"))
            return segments[^2];
        if (segments[^1] == "self")
            return "store";
        if (segments[^1] == "manifest")
            return "manifest";
        if (segments[^1] == "items")
            return "items";
        return segments[^1] == "orders" ? "search" : "details";
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            request.Headers.UserAgent.ToString()
        );
        lock (_gate)
        {
            _requests.Add(recorded);
        }

        if (recorded.Route == "token")
            return Json(TokenBody());

        if (Override is { } respond && await respond(recorded) is { } custom)
            return custom;

        return recorded.Route switch
        {
            "store" => Json(Fixture("stores-self.json")),
            "manifest" => Json(Fixture("manifest.json")),
            "search" => Json(SearchPage(recorded)),
            "details" => Json(DetailsFor(recorded.Path.Split('/')[^1].Split(','))),
            "items" => Json(ItemsFor(recorded)),
            "skus" => Json(Fixture("skus.json")),
            _ => Json(Fixture("products.json")),
        };
    }

    private string SearchPage(RecordedRequest request)
    {
        var offset = request.QueryInt("offset");
        var limit = request.QueryInt("limit");
        var searched = Searched(request);
        var page = searched.Skip(offset).Take(limit).Select(n => (JsonNode)n!).ToArray();
        return new JsonObject
        {
            ["success"] = true,
            ["errors"] = new JsonArray(),
            ["totalItems"] = searched.Count,
            ["results"] = new JsonArray(page),
        }.ToJsonString();
    }

    // The fixture manifest's id for the InStorePickup delivery type.
    private const int InStorePickup = 4;

    // The store's orders, the open ones with a leaked one after each of the first three, less
    // those a filter the request sends excludes. A filter covers only its kind of order, and an
    // order with no fixture details row (a packing-slip order a test lists) passes every filter.
    private List<string> Searched(RecordedRequest request)
    {
        var store = new List<string>();
        for (var index = 0; index < OpenOrderNumbers.Count; index++)
        {
            store.Add(OpenOrderNumbers[index]);
            if (index < LeakedOrderNumbers.Count)
                store.Add(LeakedOrderNumbers[index]);
        }

        store.AddRange(LeakedOrderNumbers.Skip(OpenOrderNumbers.Count));

        var statuses = request.QueryIds("orderStatusIds");
        var pickupStatuses = request.QueryIds("pickupStatusIds");
        var types = request.QueryIds("orderTypeIds");
        var rows = JsonNode.Parse(Fixture("order-details.json"))!["results"]!
            .AsArray()
            .ToDictionary(row => (string)row!["orderNumber"]!, row => row!);
        return store
            .Where(number =>
            {
                if (!rows.TryGetValue(number, out var row))
                    return true;
                if ((int?)row["orderDeliveryTypeId"] == InStorePickup)
                    return Passes(pickupStatuses, row["orderPickupStatusTypeId"]);
                return Passes(statuses, row["orderStatusTypeId"])
                    && Passes(types, row["orderTypeId"]);
            })
            .ToList();
    }

    // A filter the request leaves out excludes nothing.
    private static bool Passes(int[]? filter, JsonNode? value) =>
        filter is null || (value is not null && filter.Contains((int)value));

    /// <summary>The fixture details rows for the given order numbers, as the stub serves them.</summary>
    public static string DetailsFor(string[] numbers)
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

    private static string ItemsFor(RecordedRequest request)
    {
        var orderNumber = request.Path.Split('/')[^2];
        return Fixture(
            orderNumber switch
            {
                "SYN-0001-A1" => "items-normal.json",
                "SYN-0002-A1" => "items-quantity-greater-than-one.json",
                "SYN-0003-A1" => "items-foil.json",
                "SYN-0004-A1" => "items-non-english.json",
                "SYN-0005-A1" => request.QueryInt("offset") == 0
                    ? "items-paged-page1.json"
                    : "items-paged-page2.json",
                "SYN-0006-A1" => "items-count-mismatch.json",
                "SYN-0007-A1" => "items-missing-product-name.json",
                "SYN-0008-A1" => "items-normal-printing-lightly-played.json",
                "SYN-0009-A1" => "items-sealed.json",
                "SYN-0010-A1" => "items-catalog-not-found.json",
                _ => throw new InvalidOperationException($"No synthetic items for {orderNumber}"),
            }
        );
    }

    // token.json with its expiry moved to two weeks from now, so the cached token never goes
    // stale as the calendar passes the fixture's fixed date.
    private static string TokenBody()
    {
        var token = JsonNode.Parse(Fixture("token.json"))!;
        token[".expires"] = DateTimeOffset.UtcNow.AddDays(14).ToString("r");
        return token.ToJsonString();
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tcgplayer", name));

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    internal sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Query,
        string UserAgent
    )
    {
        public string Route => Classify(Path);

        public int QueryInt(string name) =>
            int.Parse(
                System.Web.HttpUtility.ParseQueryString(Query)[name] ?? "0",
                System.Globalization.CultureInfo.InvariantCulture
            );

        /// <summary>A comma-separated id filter, or null when the query leaves it out.</summary>
        public int[]? QueryIds(string name) =>
            System
                .Web.HttpUtility.ParseQueryString(Query)[name]
                ?.Split(',')
                .Select(id => int.Parse(id, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
    }
}
