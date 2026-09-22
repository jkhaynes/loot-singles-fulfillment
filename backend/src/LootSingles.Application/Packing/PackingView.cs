namespace LootSingles.Application.Packing;

/// <summary>
/// What the packing desk shows for one resolved order
/// (017-pick-completion-handoff FR-025, FR-029).
/// <para>
/// Reflects the order's <em>current</em> state, never the sticker's. An order whose issue was
/// resolved after a hold label was printed is packable, even though the sleeve says HOLD.
/// </para>
/// </summary>
public sealed record PackingView(
    int OrderId,
    string TcgplayerOrderId,
    int CardCount,
    IReadOnlyList<LabelContributor> PickedBy,
    DateTimeOffset? PickedAt,
    string Status,
    bool CanPack,
    string? BlockedReason,
    IReadOnlyList<string> UnresolvedProducts,
    bool HasPackingSlip
);

/// <summary>Why a pack attempt was refused, or that it succeeded.</summary>
public enum PackOutcome
{
    Success,
    OrderNotFound,
    AlreadyPacked,
    HasUnresolvedIssue,
    NotAwaitingPacking,
}

/// <summary>Why a slip could not be handed over, or that it was.</summary>
public enum PackingSlipOutcome
{
    Success,
    OrderNotFound,
    Unavailable,
}

public sealed record PackingSlipResult(PackingSlipOutcome Outcome, byte[]? Content = null);
