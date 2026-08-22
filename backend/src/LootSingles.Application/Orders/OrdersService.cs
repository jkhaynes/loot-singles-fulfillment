namespace LootSingles.Application.Orders;

public sealed class OrdersService(IOrderRepository orderRepository)
{
    public Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken) =>
        orderRepository.GetAllAsync(cancellationToken);
}
