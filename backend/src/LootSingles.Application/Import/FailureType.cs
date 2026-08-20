namespace LootSingles.Application.Import;

/// <summary>
/// Represents failure types that can occur during the TCGplayer order import process.
/// Includes both per-order failures and attempt-wide failures.
/// </summary>
public enum FailureType
{
    /// <summary>Per-order failure: Order identifier is missing (FR-005).</summary>
    MissingOrderIdentifier,

    /// <summary>Per-order failure: Order has no product lines (FR-008).</summary>
    NoProductLines,

    /// <summary>Per-order failure: Product quantity is invalid (FR-008).</summary>
    InvalidQuantity,

    /// <summary>Per-order failure: Product name is missing (FR-008).</summary>
    MissingProductName,

    /// <summary>Per-order failure: Card set is missing (FR-008).</summary>
    MissingSet,

    /// <summary>Per-order failure: Card collector number is missing (FR-008).</summary>
    MissingCollectorNumber,

    /// <summary>Per-order failure: Card condition is missing (FR-008).</summary>
    MissingCondition,

    /// <summary>Per-order failure: Order ID already processed in this import (FR-005).</summary>
    DuplicateOrder,

    /// <summary>Attempt-wide failure: Summary line count does not match actual order count (FR-013).</summary>
    SummaryMismatch,

    /// <summary>Attempt-wide failure: PDF file could not be read or parsed (FR-015).</summary>
    UnreadablePdf
}
