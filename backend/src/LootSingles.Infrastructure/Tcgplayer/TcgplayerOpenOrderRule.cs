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

/// <summary>What <see cref="TcgplayerOpenOrderRule.Decide"/> concluded about one order.</summary>
public enum TcgplayerOpenness
{
    /// <summary>Open: the import considers it.</summary>
    Open,

    /// <summary>Not open: skipped silently, never detected, fetched, reported or counted.</summary>
    NotOpen,

    /// <summary>
    /// The details row lacks a value the rule needs. The order is kept (detected, never silently
    /// skipped) and rejected on its own as <c>TcgplayerResponseInvalid</c> (ruling R30).
    /// </summary>
    Undecidable,
}

/// <param name="Openness">The conclusion.</param>
/// <param name="Reason">
/// For <see cref="TcgplayerOpenness.Undecidable"/> only: a message naming the order and the
/// missing field, safe to show and log (no response body).
/// </param>
public readonly record struct TcgplayerOpennessDecision(TcgplayerOpenness Openness, string? Reason);

/// <summary>
/// FR-004 as one pure rule over an order details row. TCGplayer's search filters are each
/// partial (contracts/tcgplayer-upstream.md), so the search result is only a list of candidates;
/// this rule, applied to each order's details before anything else is fetched for it, is what
/// decides.
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
    /// Open when the order's type is one of the configured types and either it is a shipped order
    /// (any delivery type but in-store pickup) in an open order status, or an in-store pickup
    /// order in an open pickup status. A pickup order is decided by its pickup status alone: live,
    /// a Received pickup order's order status is Processing, and so may a collected one's be.
    /// A type that can never be open is not open whatever else is missing; otherwise a missing
    /// delivery type, a shipped order's missing order status or a pickup order's missing pickup
    /// status makes the order undecidable.
    /// </summary>
    public static TcgplayerOpennessDecision Decide(
        TcgplayerOrderDetails order,
        TcgplayerOpenOrderIds ids
    )
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(ids);

        if (!ids.OrderTypeIds.Contains(order.OrderTypeId ?? ids.NormalOrderTypeId))
            return NotOpen;

        if (order.OrderDeliveryTypeId is not { } deliveryType)
            return Undecidable(order, "delivery type");

        if (deliveryType == ids.InStorePickupDeliveryTypeId)
        {
            return order.OrderPickupStatusTypeId is { } pickupStatus
                ? Decided(ids.PickupStatusIds.Contains(pickupStatus))
                : Undecidable(order, "pickup status");
        }

        return order.OrderStatusTypeId is { } status
            ? Decided(ids.OrderStatusIds.Contains(status))
            : Undecidable(order, "order status");
    }

    private static readonly TcgplayerOpennessDecision NotOpen = new(
        TcgplayerOpenness.NotOpen,
        null
    );

    private static TcgplayerOpennessDecision Decided(bool open) =>
        open ? new(TcgplayerOpenness.Open, null) : NotOpen;

    private static TcgplayerOpennessDecision Undecidable(
        TcgplayerOrderDetails order,
        string field
    ) =>
        new(
            TcgplayerOpenness.Undecidable,
            $"{order.OrderNumber}: TCGplayer's order details have no {field}, so whether the order is open can't be decided."
        );
}
