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
/// <para>
/// 017-pick-completion-handoff adds one exception ahead of that rule, and only one: an order with a
/// recorded pack is <see cref="OrderStatus.Packed"/>. Packing is the single status that cannot be
/// derived from lines, because nothing about an order's lines changes when its sleeve goes in the
/// mail. The derivation below is deliberately left exactly as feature 015 wrote it — the new case
/// is a guard placed in front of it, not a fourth branch woven into it, so the rule that has held
/// for two features stays legible and the exception stays obvious (FR-035, research.md §6).
/// </para>
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

        // PackedAt is read pre-update, like every other column in a SET expression. That is correct
        // here precisely because none of the callers change it: claiming, releasing, force-releasing
        // and recording a line outcome all leave a pack alone, so the pre-update value is also the
        // post-update one.
        return order =>
            order.PackedAt != null ? OrderStatus.Packed
            : order.OrderLines.Any(line => line.PickOutcome == PickOutcome.HasIssue)
                ? OrderStatus.NeedsAttention
            : order.OrderLines.Any()
            && order.OrderLines.All(line => line.PickOutcome == PickOutcome.Picked)
                ? OrderStatus.Picked
            : unfinishedStatus;
    }
}
