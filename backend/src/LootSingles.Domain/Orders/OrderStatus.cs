namespace LootSingles.Domain.Orders;

/// <summary>
/// Represents the status of an imported order.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Order is ready for picking.
    /// A successfully imported order begins in this state.
    /// </summary>
    Ready = 0,
}
