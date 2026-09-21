namespace LootSingles.Application.Dashboard;

/// <summary>
/// Persistence seam for the Dashboard's read-only order summaries, keeping
/// <c>LootSingles.Application</c> free of a direct EF Core dependency (Constitution IX/XII).
/// </summary>
public interface IDashboardRepository
{
    Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<OrderSummary>> GetInProgressOrderSummariesAsync(
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<NeedsAttentionOrderSummary>> GetNeedsAttentionOrderSummariesAsync(
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<OrderSummary>> GetPickedOrderSummariesAsync(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The order <paramref name="employeeId"/> currently holds, or <c>null</c> when they hold
    /// none (016-mobile-picking FR-026).
    ///
    /// Deliberately keyed on the claim rather than on a list of statuses. An order keeps its
    /// claim when an issue is reported or when it is fully picked, so it can be sitting in Needs
    /// Attention or Picked while still held — and a status-list implementation would need
    /// editing every time the lifecycle gains a state (PRD §20.1 adds three).
    /// </summary>
    Task<OrderSummary?> GetActiveClaimAsync(int employeeId, CancellationToken cancellationToken);
}
