namespace LootSingles.Application.Orders;

/// <summary>
/// Bound from the <c>OrderDeletion</c> configuration section (021-local-mock-orders research R5).
/// The default is the strict production rule.
/// </summary>
public sealed class OrderDeletionOptions
{
    public const string SectionName = "OrderDeletion";

    /// <summary>
    /// When true (Development, stage), an order in any state can be deleted. When false or absent
    /// (production), only an order nobody has ever claimed can be (FR-013).
    /// </summary>
    public bool AllowTouchedOrders { get; set; }
}
