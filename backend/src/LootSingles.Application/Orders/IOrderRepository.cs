namespace LootSingles.Application.Orders;

public interface IOrderRepository
{
    Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken);
}
