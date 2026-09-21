namespace LootSingles.Application.Import;

/// <summary>
/// Cuts one order's pages out of a batch packing slip document
/// (017-pick-completion-handoff FR-019).
/// <para>
/// A separate seam from <see cref="IPackingSlipParser"/> on purpose. That interface reads a
/// document; this one produces one. They happen to use the same library today, but a future
/// TCGplayer API integration replaces the parser and leaves this unused rather than half
/// reimplemented (research.md §2).
/// </para>
/// <para>
/// Implementations must not persist anything. Storing the result is the caller's job.
/// </para>
/// </summary>
public interface IPackingSlipSlicer
{
    /// <summary>
    /// A standalone document containing exactly <paramref name="pageNumbers"/> from
    /// <paramref name="document"/>, or null when those pages cannot be extracted.
    /// </summary>
    /// <remarks>
    /// Returns null rather than throwing, because a slip that cannot be produced must never
    /// reject the order it belongs to (FR-021). The order's own data parsed successfully and is
    /// authoritative; a missing slip is an inconvenience at the packing bench, not corruption.
    /// </remarks>
    byte[]? Slice(byte[] document, IReadOnlyList<int> pageNumbers);
}
