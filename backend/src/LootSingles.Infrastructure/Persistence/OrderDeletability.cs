using LootSingles.Domain.Orders;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// The one definition of which orders may be deleted (021 data-model.md "Deletability"), used by
/// deletion itself, the deletion summary, and the order list's <c>canDelete</c>, so they cannot drift.
/// </summary>
internal static class OrderDeletability
{
    /// <summary>
    /// Visible orders that may be deleted. Under the production rule, only an order nobody has ever
    /// claimed. <c>ClaimedByEmployeeId</c> is checked as well as <c>FirstClaimedAt</c> so the rule
    /// doesn't rely on the backfill alone. The <c>DeletedAt == null</c> part comes from the
    /// <c>Order</c> query filter.
    /// </summary>
    public static IQueryable<Order> Deletable(this IQueryable<Order> orders, bool allowTouched) =>
        allowTouched
            ? orders
            : orders.Where(order =>
                order.FirstClaimedAt == null && order.ClaimedByEmployeeId == null
            );
}
