namespace LootSingles.Application.Import;

/// <summary>
/// Represents one execution of the TCGplayer order import process.
/// Tracks when the import started and completed, any attempt-wide failures, and the collection of results for individual orders.
/// </summary>
public class ImportAttempt
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The timestamp when this import attempt started (FR-012).
    /// </summary>
    public required DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// The timestamp when this import attempt completed (FR-014).
    /// Null while the attempt is still processing; set once at the end of the call.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// The attempt-wide failure code, if any (FR-006).
    /// Only ever set to <see cref="FailureType.UnreadablePdf"/> or <see cref="FailureType.SummaryMismatch"/>.
    /// Null when the file itself was readable and no summary mismatch occurred.
    /// Individual order failures live on <see cref="ImportOrderResult"/>, not here (FR-013).
    /// </summary>
    public FailureType? AttemptFailureCode { get; set; }

    /// <summary>
    /// Human-readable detail message for the attempt-wide failure.
    /// Set together with <see cref="AttemptFailureCode"/> when that is non-null.
    /// </summary>
    public string? AttemptFailureMessage { get; set; }

    /// <summary>
    /// The collection of import results for individual orders from this attempt (FR-015).
    /// Empty when <see cref="AttemptFailureCode"/> is <see cref="FailureType.UnreadablePdf"/>,
    /// since no orders could even be evaluated.
    /// </summary>
    public ICollection<ImportOrderResult> ImportOrderResults { get; set; } = new List<ImportOrderResult>();
}
