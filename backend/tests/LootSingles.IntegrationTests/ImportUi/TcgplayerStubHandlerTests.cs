using System.Text.Json;

namespace LootSingles.IntegrationTests.ImportUi;

/// <summary>
/// T072: the stub's search honours its filters the way the live search does, each covering only
/// part of the orders (research.md §3), so the import tests over it catch a search that drops a
/// filter. The query-level regression test for the first live press, which sent one filter only
/// and imported long-collected pickup orders.
/// </summary>
public sealed class TcgplayerStubHandlerTests
{
    // The fixture manifest's ids for the default names: Processing and Ready To Ship, Received,
    // Normal.
    private const string Statuses = "orderStatusIds=1,2";
    private const string PickupStatuses = "pickupStatusIds=1";
    private const string Types = "orderTypeIds=1";

    [Fact]
    public async Task A_search_with_all_three_filters_returns_exactly_the_open_orders()
    {
        var numbers = await SearchAsync($"{Statuses}&{PickupStatuses}&{Types}");

        Assert.Equal(Enumerable.Range(1, 10).Select(TcgplayerStubHandler.OrderNumber), numbers);
    }

    [Fact]
    public async Task A_search_missing_pickupStatusIds_leaks_a_picked_up_pickup_order()
    {
        var numbers = await SearchAsync($"{Statuses}&{Types}");

        Assert.Contains(TcgplayerStubHandler.OrderNumber(12), numbers);
        Assert.DoesNotContain(TcgplayerStubHandler.OrderNumber(11), numbers);
        Assert.DoesNotContain(TcgplayerStubHandler.OrderNumber(13), numbers);
    }

    [Theory]
    [InlineData($"{PickupStatuses}&{Types}", 11)]
    [InlineData($"{Statuses}&{PickupStatuses}", 13)]
    public async Task A_search_missing_another_filter_leaks_the_orders_only_it_excludes(
        string query,
        int leaked
    )
    {
        var numbers = await SearchAsync(query);

        Assert.Equal(
            [TcgplayerStubHandler.OrderNumber(leaked)],
            TcgplayerStubHandler.LeakedOrderNumbers.Intersect(numbers)
        );
    }

    private static async Task<List<string>> SearchAsync(string filters)
    {
        using var client = new HttpClient(new TcgplayerStubHandler())
        {
            BaseAddress = new Uri(TcgplayerStubHandler.BaseUrl),
        };
        using var response = await client.GetAsync(
            $"stores/SYNSTORE1/orders?{filters}&offset=0&limit=50"
        );
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body
            .RootElement.GetProperty("results")
            .EnumerateArray()
            .Select(number => number.GetString()!)
            .ToList();
    }
}
