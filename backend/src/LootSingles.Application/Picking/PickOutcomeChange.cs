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
}
