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

    /// <summary>
    /// Order is exclusively claimed by an employee and being worked (013-order-claiming).
    /// </summary>
    InProgress = 1,
}
