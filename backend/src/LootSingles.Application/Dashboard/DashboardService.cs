namespace LootSingles.Application.Dashboard;

/// <summary>
/// Orchestrates the Dashboard's read-only data: one section per order status.
/// </summary>
public sealed class DashboardService(IDashboardRepository repository)
{
    public Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => repository.GetReadyOrderSummariesAsync(cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> GetInProgressOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => repository.GetInProgressOrderSummariesAsync(cancellationToken);

    public Task<IReadOnlyList<NeedsAttentionOrderSummary>> GetNeedsAttentionOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => repository.GetNeedsAttentionOrderSummariesAsync(cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> GetPickedOrderSummariesAsync(
        CancellationToken cancellationToken
    ) => repository.GetPickedOrderSummariesAsync(cancellationToken);
}
