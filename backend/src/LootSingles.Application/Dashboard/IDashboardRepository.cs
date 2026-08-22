namespace LootSingles.Application.Dashboard;

/// <summary>
/// Persistence seam for the Dashboard's read-only order summaries, keeping
/// <c>LootSingles.Application</c> free of a direct EF Core dependency (Constitution IX/XII).
/// Ready-only for now (research.md §2) — the future order-claiming and picking-workflow features
/// are the natural place to extend this once In Progress/Needs Attention/Picked have real data.
/// </summary>
public interface IDashboardRepository
{
    Task<IReadOnlyList<OrderSummary>> GetReadyOrderSummariesAsync(
        CancellationToken cancellationToken
    );
}
