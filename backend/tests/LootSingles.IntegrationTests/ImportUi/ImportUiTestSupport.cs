using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LootSingles.Api.Controllers;
using LootSingles.Application.Import;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;
using LootSingles.Infrastructure.Persistence;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LootSingles.IntegrationTests.ImportUi;

internal static class ImportUiTestSupport
{
    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LootSinglesDbContext>();
            context.Employees.Add(
                new Employee
                {
                    Username = "importer",
                    NormalizedUsername = "IMPORTER",
                    DisplayName = "Import User",
                    PinHash = new Pbkdf2PinHasher().Hash("1234"),
                    Role = EmployeeRole.Picker,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );

            await context.SaveChangesAsync();
        }

        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
        );

        Assert.Equal(
            HttpStatusCode.OK,
            (
                await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new LoginRequest("importer", "1234")
                )
            ).StatusCode
        );
        return client;
    }

    public static MultipartFormDataContent FileForm(
        byte[] bytes,
        string contentType = "application/pdf",
        string name = "file"
    )
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, name, "orders.pdf");
        return form;
    }

    public static async Task<string[]> PostFixtureAsync(HttpClient client, string fixture)
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PackingSlips",
            fixture
        );
        var bytes = await File.ReadAllBytesAsync(fixturePath);
        using var form = FileForm(bytes);

        var response = await client.PostAsync("/api/imports", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Hosts the API with obviously fake TCGplayer keys (or none) and <paramref name="stub"/> as the
    /// PRIMARY handler of the "Tcgplayer" client only, so the real auth, rate-limit, timeout and
    /// User-Agent pipeline runs in front of it and no request can leave the process.
    /// </summary>
    public static WebApplicationFactory<Program> WithTcgplayerStub(
        WebApplicationFactory<Program> factory,
        TcgplayerStubHandler stub,
        bool configured = true
    ) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tcgplayer:PublicKey", configured ? "synthetic-public-id" : "");
            builder.UseSetting("Tcgplayer:PrivateKey", configured ? "synthetic-private-id" : "");
            builder.UseSetting("Tcgplayer:AccessToken", configured ? "synthetic-store-access" : "");
            builder.UseSetting("Tcgplayer:StoreKey", "");
            builder.UseSetting("Tcgplayer:BaseUrl", TcgplayerStubHandler.BaseUrl);
            builder.UseSetting("Tcgplayer:PageSize", "2");
            builder.ConfigureServices(services =>
                services
                    .AddHttpClient(TcgplayerServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => stub)
            );
        });

    /// <summary>
    /// Reads a TCGplayer NDJSON response to the end and returns its lines.
    /// </summary>
    public static async Task<string[]> PostTcgplayerAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/imports/tcgplayer", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}

/// <summary>
/// A stand-in for TCGplayer that serves the synthetic fixtures in
/// LootSingles.Fixtures/Tcgplayer (never live data). Search and items page by the requested
/// offset and limit; order details are filtered to the requested numbers. Every request is
/// recorded (method, path, query, User-Agent) so tests can check the agreement guards.
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
        var page = OpenOrderNumbers.Skip(offset).Take(limit).Select(n => (JsonNode)n!).ToArray();
        return new JsonObject
        {
            ["success"] = true,
            ["errors"] = new JsonArray(),
            ["totalItems"] = OpenOrderNumbers.Count,
            ["results"] = new JsonArray(page),
        }.ToJsonString();
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
    }
}
