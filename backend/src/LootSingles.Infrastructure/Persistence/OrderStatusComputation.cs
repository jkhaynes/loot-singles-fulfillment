using System.Linq.Expressions;
using LootSingles.Domain.Orders;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// The one order-status derivation (015-pick-completion FR-006, research.md §1/§5), shared by every
/// write that can change it — claim, release, force-release, and recording a line outcome — so no
/// code path ever sets Status as an independent flag. Translated by EF into a correlated subquery
/// inside the caller's <c>ExecuteUpdateAsync</c>:
/// any line has an unresolved issue → NeedsAttention; else every line picked → Picked;
/// else claimed → InProgress; else Ready.
/// </summary>
internal static class OrderStatusComputation
{
    /// <param name="claimedAfterWrite">
    /// Whether the order is claimed once the caller's write completes. Passed in rather than read
    /// from the row because SQL evaluates SET expressions against pre-update column values.
    /// </param>
    public static Expression<Func<Order, OrderStatus>> FromCurrentLines(bool claimedAfterWrite)
    {
        var unfinishedStatus = claimedAfterWrite ? OrderStatus.InProgress : OrderStatus.Ready;

        return order =>
            order.OrderLines.Any(line => line.PickOutcome == PickOutcome.HasIssue)
                ? OrderStatus.NeedsAttention
            : order.OrderLines.Any()
            && order.OrderLines.All(line => line.PickOutcome == PickOutcome.Picked)
                ? OrderStatus.Picked
            : unfinishedStatus;
    }
}
