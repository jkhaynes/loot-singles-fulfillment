namespace LootSingles.Application.Dashboard;

/// <summary>
/// Orchestrates the Dashboard's read-only data. Thin today (research.md §2) — the extension
/// point the future In Progress/Needs Attention/Picked sections will attach to once those order
/// states exist.
/// </summary>
public sealed class DashboardService(IDashboardRepository repository)
{
    public Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(CancellationToken cancellationToken) =>
        repository.GetReadyOrderSummariesAsync(cancellationToken);
}
