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
}
