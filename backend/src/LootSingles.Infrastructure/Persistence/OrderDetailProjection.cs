using System.Linq.Expressions;
using LootSingles.Application.Orders;
using LootSingles.Domain.Orders;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// The one <see cref="Order"/> → <see cref="OrderDetail"/> projection, shared by
/// <see cref="OrderRepository.GetByIdAsync"/> and by <see cref="PickingRepository"/>'s
/// in-transaction read (branch review BR-002). Extracted because it now has a second concrete
/// use case (Constitution Principle XIII), so the two reads cannot drift apart.
///
/// <see cref="OrderLineDetail.ImageUrl"/> is always null here: card images are supplemental
/// enrichment applied above persistence by <c>OrdersService</c>, never stored.
/// </summary>
internal static class OrderDetailProjection
{
    public static readonly Expression<Func<Order, OrderDetail>> ToDetail = order => new OrderDetail(
        order.Id,
        order.TcgplayerOrderId,
        order.Status,
        order
            .OrderLines.OrderBy(line => line.Id)
            .Select(line => new OrderLineDetail(
                line.Id,
                line.PickOutcome,
                line.ProductName,
                line.ProductLine,
                line.Set,
                line.CollectorNumber,
                line.Rarity,
                line.Variant,
                line.Condition,
                line.Quantity,
                null,
                line.CurrentPickingIssue == null
                    ? null
                    : new PickingIssueDetail(
                        line.CurrentPickingIssue.IssueType,
                        line.CurrentPickingIssue.RequiredQuantity,
                        line.CurrentPickingIssue.FoundQuantity,
                        line.CurrentPickingIssue.Note,
                        line.CurrentPickingIssue.ReportedByEmployee == null
                            ? null
                            : line.CurrentPickingIssue.ReportedByEmployee.DisplayName,
                        line.CurrentPickingIssue.ReportedAt
                    )
            ))
            .ToList(),
        order.ClaimedByEmployeeId,
        order.ClaimedByEmployee != null ? order.ClaimedByEmployee.DisplayName : null
    );
}
