using LootSingles.Domain.Orders;

namespace LootSingles.Application.Orders;

public sealed record OrderListItem(
    int OrderId,
    string TcgplayerOrderId,
    OrderStatus Status,
    DateTimeOffset ImportedAt,
    int? ClaimedByEmployeeId = null,
    string? ClaimedByEmployeeName = null,
    bool CanDelete = false,
    string? DeleteBlockedReason = null
)
{
    /// <summary>The reason a Manager/Admin sees when the production rule blocks deletion (FR-021).</summary>
    public const string ClaimedBeforeReason = "claimed_before";
}
