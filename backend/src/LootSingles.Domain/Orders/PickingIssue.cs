namespace LootSingles.Domain.Orders;

/// <summary>
/// One picking-issue report on an order line (015-pick-completion FR-002, FR-011).
/// Append-only: rows are never updated or deleted, so every report survives a later revision of
/// its line's outcome. The line's current issue is whichever row
/// <see cref="OrderLine.CurrentPickingIssueId"/> points at.
/// </summary>
public class PickingIssue
{
    public int Id { get; set; }

    public int OrderLineId { get; set; }

    public PickingIssueType IssueType { get; set; }

    /// <summary>
    /// Quantity the line required, when relevant to the issue (e.g. insufficient quantity).
    /// </summary>
    public int? RequiredQuantity { get; set; }

    /// <summary>
    /// Quantity actually found, when relevant to the issue.
    /// </summary>
    public int? FoundQuantity { get; set; }

    /// <summary>
    /// Optional free-text note from the picker.
    /// </summary>
    public string? Note { get; set; }

    public int ReportedByEmployeeId { get; set; }

    /// <summary>
    /// Navigation to the reporting employee, for surfacing who raised the issue.
    /// </summary>
    public Employees.Employee? ReportedByEmployee { get; set; }

    public DateTimeOffset ReportedAt { get; set; }
}
