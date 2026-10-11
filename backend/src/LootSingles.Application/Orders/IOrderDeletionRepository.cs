namespace LootSingles.Application.Orders;

public enum OrderDeletionOutcome
{
    Deleted,
    NotFound,
    AlreadyDeleted,
    ClaimedBefore,
}

/// <summary>The repository's answer to a single delete. The order number is set only when deleted.</summary>
public sealed record DeleteOrderAttempt(OrderDeletionOutcome Outcome, string? TcgplayerOrderId);

public sealed record DeleteAllCounts(int DeletedCount, int KeptCount);

/// <summary>
/// Counts over visible orders. <see cref="ClaimedCount"/> and <see cref="WithUnresolvedIssueCount"/>
/// cover only deletable orders (contracts/api.md).
/// </summary>
public sealed record OrderDeletionCounts(
    int DeletableCount,
    int KeptCount,
    int ClaimedCount,
    int WithUnresolvedIssueCount
);

public interface IOrderDeletionRepository
{
    /// <summary>
    /// Soft-deletes one order with a single conditional update (021 research R7). A refusal is
    /// worded by re-reading the row, ignoring the deleted-order filter.
    /// </summary>
    Task<DeleteOrderAttempt> DeleteAsync(
        int orderId,
        int actorEmployeeId,
        DateTimeOffset now,
        bool allowTouched,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Soft-deletes every deletable order imported at or before <paramref name="asOf"/>.
    /// </summary>
    Task<DeleteAllCounts> DeleteAllAsync(
        int actorEmployeeId,
        DateTimeOffset now,
        DateTimeOffset asOf,
        bool allowTouched,
        CancellationToken cancellationToken
    );

    Task<OrderDeletionCounts> GetSummaryAsync(
        DateTimeOffset now,
        bool allowTouched,
        CancellationToken cancellationToken
    );
}
