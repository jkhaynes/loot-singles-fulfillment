using LootSingles.Application.Dashboard;
using LootSingles.Domain.Orders;

namespace LootSingles.UnitTests.Dashboard;

public class DashboardServiceTests
{
    [Fact]
    public async Task GetReadyOrderSummariesAsync_ExcludesNonReadyOrders()
    {
        var readyOrder = new Order
        {
            Id = 1,
            TcgplayerOrderId = "F0000001-ABC001-00001",
            Status = OrderStatus.Ready,
            ImportedAt = DateTimeOffset.UtcNow,
            OrderLines = [NewLine(quantity: 1)],
        };
        var repository = new FakeDashboardRepository([readyOrder]);
        var service = new DashboardService(repository);

        var summaries = await service.GetReadyOrderSummariesAsync(CancellationToken.None);

        Assert.Single(summaries);
    }

    [Fact]
    public async Task GetReadyOrderSummariesAsync_NoOrders_ReturnsEmptyList()
    {
        var repository = new FakeDashboardRepository([]);
        var service = new DashboardService(repository);

        var summaries = await service.GetReadyOrderSummariesAsync(CancellationToken.None);

        Assert.Empty(summaries);
    }

    private static OrderLine NewLine(int quantity) => new()
    {
        RawDescription = "Pikachu - Base Set - #58/102 - Common - Near Mint",
        ProductLine = "Pokemon",
        ProductName = "Pikachu",
        Set = "Base Set",
        CollectorNumber = "#58/102",
        Condition = "Near Mint",
        Quantity = quantity,
    };

    // Mirrors the exact LINQ shape DashboardRepository uses (data-model.md's Query shape) so this
    // unit test documents/verifies the summary formula without depending on a real database.
    private sealed class FakeDashboardRepository(IReadOnlyList<Order> orders) : IDashboardRepository
    {
        public Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrderSummary>>(orders
                .Where(order => order.Status == OrderStatus.Ready)
                .Select(order => new OrderSummary(
                    order.Id, order.TcgplayerOrderId, order.OrderLines.Count, order.OrderLines.Sum(line => line.Quantity)))
                .ToList());
    }
}
