using LootSingles.Application.Dashboard;
using LootSingles.Domain.Orders;

namespace LootSingles.UnitTests.Dashboard;

public class DashboardServiceTests
{
    [Fact]
    public async Task GetReadyOrderSummariesAsync_NoOrders_ReturnsEmptyList()
    {
        var repository = new FakeDashboardRepository([]);
        var service = new DashboardService(repository);

        var summaries = await service.GetReadyOrderSummariesAsync(CancellationToken.None);

        Assert.Empty(summaries);
    }

    private sealed class FakeDashboardRepository(IReadOnlyList<Order> orders) : IDashboardRepository
    {
        public Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<OrderSummary>>(
                orders
                    .Where(order => order.Status == OrderStatus.Ready)
                    .Select(order => new OrderSummary(
                        order.Id,
                        order.TcgplayerOrderId,
                        order.OrderLines.Count,
                        order.OrderLines.Sum(line => line.Quantity)
                    ))
                    .ToList()
            );
    }
}
