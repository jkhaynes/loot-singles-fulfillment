namespace LootSingles.Domain.Orders;

/// <summary>
/// Represents one TCGplayer order recovered from a packing slip import.
/// An Order contains one or more OrderLines and does not contain any customer shipping information.
/// </summary>
public class Order
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The TCGplayer order identifier from the packing slip (e.g., "F8433182-7B9B3B-BC75E").
    /// This is the natural business identifier and must be unique across all orders.
    /// </summary>
    public required string TcgplayerOrderId { get; set; }

    /// <summary>
    /// The current status of this order.
    /// A successfully imported Order begins in the "Ready" state.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// The timestamp when this order was imported.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>
    /// The collection of order lines (product/card items) contained in this order.
    /// An Order must contain at least one OrderLine.
    /// </summary>
    public ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();
}
