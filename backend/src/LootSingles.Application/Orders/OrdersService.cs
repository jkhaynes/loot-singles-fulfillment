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

        var indexedLines = order
            .Lines.Select((line, index) => (Index: index, Line: line))
            .ToArray();
        var imageUrlsByIndex = new string?[order.Lines.Count];

        await Task.WhenAll(
            indexedLines
                .GroupBy(entry => entry.Line.ProductLine, StringComparer.OrdinalIgnoreCase)
                .Select(async group =>
                {
                    var groupEntries = group.ToArray();
                    var identities = groupEntries
                        .Select(entry => new CardIdentity(
                            entry.Line.ProductName,
                            entry.Line.Set,
                            entry.Line.CollectorNumber,
                            entry.Line.Variant
                        ))
                        .ToArray();
                    var imageUrls = await cardImageEnrichmentService.TryGetImageUrlsAsync(
                        group.Key,
                        identities,
                        cancellationToken
                    );
                    for (var i = 0; i < groupEntries.Length; i++)
                    {
                        imageUrlsByIndex[groupEntries[i].Index] = imageUrls.TryGetValue(
                            identities[i],
                            out var url
                        )
                            ? url
                            : null;
                    }
                })
        );

        var enrichedLines = order
            .Lines.Select((line, index) => line with { ImageUrl = imageUrlsByIndex[index] })
            .ToList();

        return order with
        {
            Lines = enrichedLines,
        };
    }
}
