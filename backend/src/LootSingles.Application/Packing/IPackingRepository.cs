using LootSingles.Domain.Orders;

namespace LootSingles.Application.Packing;

/// <summary>
/// Reads and writes for the packing workflow (017-pick-completion-handoff).
/// </summary>
public interface IPackingRepository
{
    /// <summary>
    /// The content of an order's label, or null when no such order exists.
    /// Whether there is anything to print is <see cref="LabelContent.HasStarted"/>'s answer, not
    /// this method's — a held order has a label and is never <see cref="OrderStatus.Picked"/>.
    /// </summary>
    Task<LabelContent?> GetLabelContentAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>
    /// The packing view for a resolved code, or null when it matches no order.
    /// </summary>
    Task<PackingView?> ResolveAsync(PackingCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Orders picked but not yet packed — what is physically still on the shelf (FR-030).
    /// </summary>
    Task<IReadOnlyList<PackingView>> GetAwaitingPackingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records an order as packed. A conditional write, so two simultaneous attempts record
    /// exactly one pack (FR-033) — the same shape feature 013 uses for exclusive claiming, rather
    /// than a second concurrency idiom.
    /// </summary>
    Task<PackOutcome> MarkPackedAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// An order's stored packing slip, recording the retrieval as it goes (FR-038).
    /// </summary>
    Task<PackingSlipResult> GetPackingSlipAsync(
        int orderId,
        int actorEmployeeId,
        CancellationToken cancellationToken
    );
}
