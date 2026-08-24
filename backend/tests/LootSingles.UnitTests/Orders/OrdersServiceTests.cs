using LootSingles.Application.Orders;
using LootSingles.Domain.Orders;

namespace LootSingles.UnitTests.Orders;

public sealed class OrdersServiceTests
{
    [Fact]
    public async Task GetAllAsync_WhenRepositoryIsEmpty_ReturnsEmptyList()
    {
        var service = new OrdersService(new FakeOrderRepository([]));

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsRepositoryProjectionUnmodified()
    {
        OrderListItem[] expected =
        [
            new(2, "ORDER-2", OrderStatus.Ready, DateTimeOffset.Parse("2026-08-22T12:00:00Z")),
            new(1, "ORDER-1", OrderStatus.Ready, DateTimeOffset.Parse("2026-08-21T12:00:00Z")),
        ];
        var service = new OrdersService(new FakeOrderRepository(expected));

        var result = await service.GetAllAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }

    private sealed class FakeOrderRepository(IReadOnlyList<OrderListItem> orders) : IOrderRepository
    {
        public Task<IReadOnlyList<OrderListItem>> GetAllAsync(
            CancellationToken cancellationToken
        ) => Task.FromResult(orders);

        public Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderDetail?>(null);
    }
}
