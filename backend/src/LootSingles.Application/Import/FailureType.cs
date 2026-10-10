namespace LootSingles.Application.Import;

/// <summary>
/// Represents failure types that can occur during the TCGplayer order import process.
/// Includes both per-order failures and attempt-wide failures.
/// </summary>
public enum FailureType
{
    /// <summary>Per-order failure: Order identifier is missing (FR-005).</summary>
    MissingOrderIdentifier,

    /// <summary>Per-order failure: Order has no product lines (FR-005).</summary>
    NoProductLines,

    /// <summary>Per-order failure: Product quantity is invalid (FR-005).</summary>
    InvalidQuantity,

    /// <summary>Per-order failure: Product name is missing (FR-005).</summary>
    MissingProductName,

    /// <summary>Per-order failure: Card set is missing (FR-005).</summary>
    MissingSet,

    /// <summary>Per-order failure: Card collector number is missing (FR-005).</summary>
    MissingCollectorNumber,

    /// <summary>Per-order failure: Card condition is missing (FR-005).</summary>
    MissingCondition,

    /// <summary>Per-order failure: Order ID already processed in this import (FR-008).</summary>
    DuplicateOrder,

    /// <summary>Attempt-wide failure: Summary line count does not match actual order count (FR-013).</summary>
    SummaryMismatch,

    /// <summary>Attempt-wide failure: PDF file could not be read or parsed (FR-015).</summary>
    UnreadablePdf,

    /// <summary>Per-order failure: a genuine, non-duplicate persistence error while saving this
    /// already-validated order (FR-016) — not one of FR-006's minimum-set codes, but required so
    /// the failure is represented as data rather than an exception that would block sibling
    /// orders in the same batch. See data-model.md.</summary>
    PersistenceFailure,

    /// <summary>Per-order failure (API import): the order's lines could not all be retrieved, or
    /// their quantities do not match its product count. Nothing is created for that order.</summary>
    IncompleteOrder,

    /// <summary>Attempt-wide failure (API import): TCGplayer could not be reached, timed out, or
    /// returned 5xx or 429.</summary>
    TcgplayerUnavailable,

    /// <summary>Attempt-wide failure (API import): TCGplayer rejected the store credentials
    /// (401 after one refresh, or 403).</summary>
    TcgplayerAccessRefused,

    /// <summary>API import: a response did not have the documented shape, a configured open
    /// status is missing from the manifest, or paging stalled. Attempt-wide when search or the
    /// manifest is affected, per order when only that order's items are.</summary>
    TcgplayerResponseInvalid,

    /// <summary>Attempt-wide failure (API import): no TCGplayer credentials are configured in
    /// this environment. No call was made.</summary>
    TcgplayerNotConfigured,
}
