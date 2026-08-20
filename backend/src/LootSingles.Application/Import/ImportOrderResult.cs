namespace LootSingles.Application.Import;

/// <summary>
/// Represents the result of importing a single order from a TCGplayer packing slip.
/// Tracks the outcome (succeeded or rejected), any failure details, and the resulting Order entity if successful.
/// One <see cref="ImportAttempt"/> may evaluate many orders independently, so results are kept separate (FR-005).
/// </summary>
public class ImportOrderResult
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the <see cref="ImportAttempt"/> that produced this result.
    /// </summary>
    public required int ImportAttemptId { get; set; }

    /// <summary>
    /// The TCGplayer order identifier from the packing slip, if it could be read.
    /// Null only when the order identifier itself could not be extracted (the <see cref="FailureType.MissingOrderIdentifier"/> case).
    /// Otherwise set even for rejected orders, since rejection can occur for other reasons
    /// (e.g., <see cref="FailureType.NoProductLines"/>) while the identifier was readable.
    /// </summary>
    public string? SourceOrderIdentifier { get; set; }

    /// <summary>
    /// The outcome of this order's import attempt.
    /// </summary>
    public required ImportOutcome Outcome { get; set; }

    /// <summary>
    /// The failure code if this order was rejected, null if succeeded.
    /// Only the per-order failure codes apply here:
    /// <see cref="FailureType.MissingOrderIdentifier"/>,
    /// <see cref="FailureType.NoProductLines"/>,
    /// <see cref="FailureType.InvalidQuantity"/>,
    /// <see cref="FailureType.MissingProductName"/>,
    /// <see cref="FailureType.MissingSet"/>,
    /// <see cref="FailureType.MissingCollectorNumber"/>,
    /// <see cref="FailureType.MissingCondition"/>, or
    /// <see cref="FailureType.DuplicateOrder"/>.
    /// The attempt-wide codes <see cref="FailureType.SummaryMismatch"/> and <see cref="FailureType.UnreadablePdf"/>
    /// belong on <see cref="ImportAttempt"/>, not here.
    /// </summary>
    public FailureType? FailureCode { get; set; }

    /// <summary>
    /// Human-readable detail message if this order was rejected, null if succeeded.
    /// Set together with <see cref="FailureCode"/> when the order fails.
    /// </summary>
    public string? FailureMessage { get; set; }

    /// <summary>
    /// The ID of the resulting Order entity if the import succeeded, null if rejected.
    /// Per FR-007, no partially-populated Order ever exists for a rejected result, so there is nothing to reference.
    /// </summary>
    public int? ResultingOrderId { get; set; }
}
