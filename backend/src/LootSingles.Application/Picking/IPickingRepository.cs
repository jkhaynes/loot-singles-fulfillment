namespace LootSingles.Application.Picking;

public interface IPickingRepository
{
    /// <summary>
    /// Records a line's outcome and re-derives its order's status in one transaction, only if the
    /// actor currently holds the order's claim (FR-006, FR-010; research.md §2).
    /// </summary>
    Task<PickingResult> RecordOutcomeAsync(
        int orderId,
        int orderLineId,
        int actorEmployeeId,
        PickOutcomeChange change,
        CancellationToken cancellationToken
    );
}
