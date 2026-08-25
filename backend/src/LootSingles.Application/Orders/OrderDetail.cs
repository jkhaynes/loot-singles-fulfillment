using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

public sealed record OrderDetail(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    IReadOnlyList<OrderLineDetail> Lines
);

public sealed record OrderLineDetail(
    string ProductName,
    string ProductLine,
    string Set,
    string CollectorNumber,
    string? Rarity,
    string? Variant,
    string Condition,
    int Quantity
);
