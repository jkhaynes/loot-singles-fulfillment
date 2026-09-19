namespace LootSingles.Domain.Orders;

/// <summary>
/// The current recorded outcome of picking one order line (015-pick-completion).
/// A line with no outcome recorded yet has a null <see cref="OrderLine.PickOutcome"/>.
/// </summary>
public enum PickOutcome
{
    /// <summary>
    /// The line was successfully picked.
    /// </summary>
    Picked = 0,

    /// <summary>
    /// The line has an unresolved picking issue; see <see cref="OrderLine.CurrentPickingIssueId"/>.
    /// </summary>
    HasIssue = 1,
}
