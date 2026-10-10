namespace LootSingles.Application.Import;

/// <summary>
/// Why getting orders from TCGplayer failed (research.md §10). The import service branches on
/// this value, never on message text.
/// </summary>
public enum TcgplayerFeedFailure
{
    /// <summary>The TCGplayer credentials are not configured; nothing was sent.</summary>
    NotConfigured,

    /// <summary>TCGplayer could not be reached, timed out, or answered 429 or 5xx.</summary>
    Unavailable,

    /// <summary>TCGplayer refused the store's credentials or token.</summary>
    AccessRefused,

    /// <summary>A response could not be read or lacked a required field.</summary>
    ResponseInvalid,
}

/// <summary>
/// A failure getting orders from TCGplayer. The message is safe to log: it never carries a
/// response body, token or key.
/// </summary>
public sealed class TcgplayerFeedException(
    TcgplayerFeedFailure failure,
    string message,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public TcgplayerFeedFailure Failure { get; } = failure;
}

/// <summary>
/// Gets open orders from TCGplayer's API as the same <see cref="OrderCandidate"/> shape the PDF
/// path produces. Failures surface as <see cref="TcgplayerFeedException"/>.
/// </summary>
public interface ITcgplayerOrderFeed
{
    /// <summary>
    /// The process-wide total of TCGplayer HTTP calls made so far, token requests included, as
    /// counted by the rate limiter. The import service logs the difference between the start and
    /// end of an attempt as <c>{CallCount}</c>.
    /// </summary>
    long CallCount { get; }

    /// <summary>
    /// How many orders to ask for in one <see cref="GetOrdersAsync"/> call: the configured
    /// <c>Tcgplayer:PageSize</c>, at least 1. The import service batches new orders by it.
    /// </summary>
    int PageSize { get; }

    /// <summary>Lists the order numbers of the store's open orders.</summary>
    Task<IReadOnlyList<string>> GetOpenOrderNumbersAsync(CancellationToken cancellationToken);

    /// <summary>Fetches the given orders, with their items, as import candidates.</summary>
    Task<IReadOnlyList<OrderCandidate>> GetOrdersAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken
    );
}
