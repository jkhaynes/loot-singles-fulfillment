using LootSingles.Application.CardCatalog;

namespace LootSingles.Application.Orders;

public sealed class OrdersService(
    IOrderRepository orderRepository,
    CardImageEnrichmentService cardImageEnrichmentService
)
{
    public Task<IReadOnlyList<OrderListItem>> GetAllAsync(CancellationToken cancellationToken) =>
        orderRepository.GetAllAsync(cancellationToken);

    public async Task<OrderDetail?> GetByIdAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var enrichedLines = await Task.WhenAll(
            order.Lines.Select(async line =>
            {
                var imageUrl = await cardImageEnrichmentService.TryGetImageUrlAsync(
                    line.ProductLine,
                    new CardIdentity(
                        line.ProductName,
                        line.Set,
                        line.CollectorNumber,
                        line.Variant
                    ),
                    cancellationToken
                );
                return line with { ImageUrl = imageUrl };
            })
        );

        return order with
        {
            Lines = enrichedLines,
        };
    }
}
