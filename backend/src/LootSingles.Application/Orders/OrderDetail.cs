namespace LootSingles.Application.Orders;

public sealed record OrderDetail(
    int OrderId,
    string TcgplayerOrderId,
    IReadOnlyList<OrderLineDetail> Lines
);

public sealed record OrderLineDetail(
    string ProductName,
    string Set,
    string? Variant,
    string Condition,
    int Quantity
);
