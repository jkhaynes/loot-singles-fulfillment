namespace LootSingles.Application.Orders;

public sealed record OrderDeletionResult(OrderDeletionOutcome Outcome)
{
    public static readonly OrderDeletionResult Deleted = new(OrderDeletionOutcome.Deleted);
    public static readonly OrderDeletionResult NotFound = new(OrderDeletionOutcome.NotFound);
    public static readonly OrderDeletionResult AlreadyDeleted = new(
        OrderDeletionOutcome.AlreadyDeleted
    );
    public static readonly OrderDeletionResult ClaimedBefore = new(
        OrderDeletionOutcome.ClaimedBefore
    );
}

public enum OrderDeleteAllOutcome
{
    Done,
    InvalidAsOf,
}

public sealed record OrderDeleteAllResult(
    OrderDeleteAllOutcome Outcome,
    int DeletedCount = 0,
    int KeptCount = 0
)
{
    public static readonly OrderDeleteAllResult InvalidAsOf = new(
        OrderDeleteAllOutcome.InvalidAsOf
    );

    public static OrderDeleteAllResult Done(int deletedCount, int keptCount) =>
        new(OrderDeleteAllOutcome.Done, deletedCount, keptCount);
}

/// <summary>The deletion summary (contracts/api.md); <see cref="AsOf"/> is the server's time.</summary>
public sealed record OrderDeletionSummary(
    DateTimeOffset AsOf,
    int DeletableCount,
    int KeptCount,
    int ClaimedCount,
    int WithUnresolvedIssueCount
);
