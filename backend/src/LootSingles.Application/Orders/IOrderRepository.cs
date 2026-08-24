namespace LootSingles.Application.Orders;

public interface IOrderRepository
{
    Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken);

    Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken);
}
