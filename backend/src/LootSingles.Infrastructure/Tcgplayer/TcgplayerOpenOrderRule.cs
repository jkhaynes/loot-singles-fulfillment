using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// The ids each import resolves from the order manifest (call #3) to decide which orders are
/// open (FR-004, research.md §3): the configured open order statuses, open pickup statuses and
/// order types, plus the delivery type that marks an in-store pickup order and the Normal order
/// type an order with no order type counts as.
/// </summary>
public sealed record TcgplayerOpenOrderIds(
    IReadOnlyList<int> OrderStatusIds,
    IReadOnlyList<int> PickupStatusIds,
    IReadOnlyList<int> OrderTypeIds,
    int InStorePickupDeliveryTypeId,
    int NormalOrderTypeId
);

/// <summary>
/// FR-004 as one pure rule over an order details row. TCGplayer's search filters are each
/// partial (contracts/tcgplayer-upstream.md), so the search result is only a list of candidates;
/// this rule, applied to every searched order's details before anything else is fetched for it,
/// is what decides.
/// </summary>
public static class TcgplayerOpenOrderRule
{
    /// <summary>
    /// The manifest's <c>orderDeliveryTypes</c> name for an in-store pickup order. A constant, not
    /// configuration: it names a kind of order rather than choosing which orders are open.
    /// </summary>
    public const string InStorePickupDeliveryType = "InStorePickup";

    /// <summary>
    /// The manifest's <c>orderTypes</c> name an order counts as when its details carry no order
    /// type (ruling: TCGplayer may omit it, and Normal is the ordinary kind of order).
    /// </summary>
    public const string NormalOrderType = "Normal";

    /// <summary>
    /// True when the order's type is one of the configured types and either it is a shipped order
    /// (any delivery type but in-store pickup) in an open order status, or an in-store pickup
    /// order in an open pickup status. A pickup order is decided by its pickup status alone: live,
    /// a Received pickup order's order status is Processing, and so may a collected one's be.
    /// </summary>
    /// <exception cref="TcgplayerFeedException">
    /// <see cref="TcgplayerFeedFailure.ResponseInvalid"/> when the row lacks a value the decision
    /// needs: openness can't be decided, and skipping the order could silently drop an open one.
    /// </exception>
    public static bool IsOpen(TcgplayerOrderDetails order, TcgplayerOpenOrderIds ids)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(ids);

        if (!ids.OrderTypeIds.Contains(order.OrderTypeId ?? ids.NormalOrderTypeId))
            return false;

        var deliveryType = order.OrderDeliveryTypeId ?? throw Undecidable(order, "delivery type");
        if (deliveryType == ids.InStorePickupDeliveryTypeId)
        {
            var pickupStatus =
                order.OrderPickupStatusTypeId ?? throw Undecidable(order, "pickup status");
            return ids.PickupStatusIds.Contains(pickupStatus);
        }

        var status = order.OrderStatusTypeId ?? throw Undecidable(order, "order status");
        return ids.OrderStatusIds.Contains(status);
    }

    private static TcgplayerFeedException Undecidable(TcgplayerOrderDetails order, string field) =>
        new(
            TcgplayerFeedFailure.ResponseInvalid,
            $"{order.OrderNumber}: TCGplayer's order details have no {field}, so whether the order is open can't be decided."
        );
}
