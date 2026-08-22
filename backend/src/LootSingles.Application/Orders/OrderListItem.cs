using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

public sealed record OrderListItem(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    DateTimeOffset ImportedAt
);
