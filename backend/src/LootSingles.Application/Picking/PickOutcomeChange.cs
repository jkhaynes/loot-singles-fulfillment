using LootSingles.Domain.Orders;

namespace LootSingles.Application.Picking;

/// <summary>
/// The line outcome a picker is recording (015-pick-completion).
/// </summary>
public abstract record PickOutcomeChange
{
    private PickOutcomeChange() { }

    /// <summary>
    /// The line was successfully picked (FR-001).
    /// </summary>
    public sealed record Picked : PickOutcomeChange;

    /// <summary>
    /// The line could not be picked as ordered (FR-002). Each report is recorded as its own
    /// <see cref="PickingIssue"/> row, so earlier reports survive a later revision (FR-011).
    /// </summary>
    public sealed record IssueReport(
        PickingIssueType IssueType,
        int? RequiredQuantity,
        int? FoundQuantity,
        string? Note
    ) : PickOutcomeChange;
}
