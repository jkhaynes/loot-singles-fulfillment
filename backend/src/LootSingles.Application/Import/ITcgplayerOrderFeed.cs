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
