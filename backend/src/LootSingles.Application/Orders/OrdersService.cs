namespace LootSingles.Application.Orders;

public sealed class OrdersService(IOrderRepository orderRepository)
{
    public Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken) =>
        orderRepository.GetAllAsync(cancellationToken);

    public Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken) =>
        orderRepository.GetByIdAsync(orderId, cancellationToken);
}
