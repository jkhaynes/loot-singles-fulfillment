namespace LootSingles.Application.Packing;

/// <summary>
/// Bounds on what the packing desk reads (017-pick-completion-handoff, branch review BR-002).
/// </summary>
public static class PackingLimits
{
    /// <summary>
    /// How many awaiting orders the desk lists at once.
    /// <para>
    /// The list is a fallback for a label that fell off and a sense of how much work is waiting;
    /// scanning is the way in. Nobody reads a backlog of 200 to the end, and the constitution
    /// requires limiting potentially large result sets. Fifty is comfortably more than a bench
    /// works through at once while staying a bounded query.
    /// </para>
    /// </summary>
    public const int AwaitingPageSize = 50;
}
