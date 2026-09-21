using LootSingles.Domain.Orders;

namespace LootSingles.Application.Packing;

/// <summary>
/// Reads for the packing workflow (017-pick-completion-handoff).
/// </summary>
public interface IPackingRepository
{
    /// <summary>
    /// The content of an order's label, or null when no such order exists.
    /// Whether there is anything to print is <see cref="LabelContent.HasStarted"/>'s answer, not
    /// this method's — a held order has a label and is never <see cref="OrderStatus.Picked"/>.
    /// </summary>
    Task<LabelContent?> GetLabelContentAsync(int orderId, CancellationToken cancellationToken);
}
