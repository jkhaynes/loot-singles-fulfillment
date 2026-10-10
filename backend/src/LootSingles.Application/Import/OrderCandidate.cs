namespace LootSingles.Application.Import;

/// <summary>
/// Untrusted, source-neutral order input produced by an import adapter (PDF or TCGplayer API).
/// It becomes a domain <c>Order</c> only after <see cref="OrderCandidateValidator"/> accepts it.
/// </summary>
/// <param name="SourceOrderIdentifier">The TCGplayer order number, if the adapter could read it.</param>
/// <param name="Lines">The product lines the adapter produced for this order.</param>
/// <param name="RejectedBySource">
/// An adapter-level rejection (for example a PDF line without a collector number). When set, the
/// validator returns it as-is and does not run its own rules.
/// </param>
public sealed record OrderCandidate(
    string? SourceOrderIdentifier,
    IReadOnlyList<OrderLineCandidate> Lines,
    (FailureType Type, string Message)? RejectedBySource = null
);

/// <summary>One untrusted product line of an <see cref="OrderCandidate"/>.</summary>
/// <param name="Quantity">Null means unreadable (PDF text) or missing (API).</param>
public sealed record OrderLineCandidate(
    string RawDescription,
    string? ProductLine,
    string? ProductName,
    string? Set,
    string? CollectorNumber,
    string? Rarity,
    string? Condition,
    string? Variant,
    string? Language,
    string? ImageUrl,
    int? Quantity
);
