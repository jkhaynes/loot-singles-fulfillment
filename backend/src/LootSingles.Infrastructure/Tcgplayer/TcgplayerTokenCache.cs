using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LootSingles.Application.Import;
using Microsoft.Extensions.Logging;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Sends one TCGplayer request down the rest of the handler pipeline. The auth handler passes its
/// own inner chain, so the token request is rate-limited and identified like any other call.
/// </summary>
public delegate Task<HttpResponseMessage> TcgplayerRequestSender(
    HttpRequestMessage request,
    CancellationToken cancellationToken
);

/// <summary>
/// The process-wide TCGplayer bearer token (research.md §1; contracts/tcgplayer-upstream.md call
/// #1). It is fetched with the store's existing keys and access token, never by creating an
/// authorization, and reused until 24 hours before it expires. A singleton: the
/// <see cref="SemaphoreSlim"/> makes concurrent callers share one token request.
/// </summary>
public sealed class TcgplayerTokenCache(
    TcgplayerOptions options,
    TimeProvider timeProvider,
    ILogger<TcgplayerTokenCache> logger
)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedToken? _current;

    /// <summary>The cached token, or a new one fetched through <paramref name="send"/>.</summary>
    public async Task<string> GetTokenAsync(
        TcgplayerRequestSender send,
        CancellationToken cancellationToken
    )
    {
        var current = Volatile.Read(ref _current);
        if (IsUsable(current))
        {
            return current!.Value;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have fetched it while this one waited.
            if (IsUsable(_current))
            {
                return _current!.Value;
            }

            return await FetchAsync(send, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces a token TCGplayer answered 401 to. If another caller already replaced it, that
    /// newer token is returned instead of fetching again.
    /// </summary>
    public async Task<string> RefreshAsync(
        string rejectedToken,
        TcgplayerRequestSender send,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsable(_current) && _current!.Value != rejectedToken)
            {
                return _current.Value;
            }

            return await FetchAsync(send, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsUsable(CachedToken? token) =>
        token is not null && timeProvider.GetUtcNow() < token.RefreshAt;

    // Called only while holding _gate.
    private async Task<string> FetchAsync(
        TcgplayerRequestSender send,
        CancellationToken cancellationToken
    )
    {
        Volatile.Write(ref _current, null);
        if (!options.IsConfigured)
        {
            throw new TcgplayerFeedException(
                TcgplayerFeedFailure.NotConfigured,
                "TCGplayer credentials are not configured."
            );
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(options.BaseUrl), "token")
        )
        {
            Content = new FormUrlEncodedContent([
                new("grant_type", "client_credentials"),
                new("client_id", options.PublicKey!),
                new("client_secret", options.PrivateKey!),
            ]),
        };
        request.Headers.Add("X-Tcg-Access-Token", options.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await send(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("The TCGplayer token request could not reach TCGplayer.");
            throw new TcgplayerFeedException(
                TcgplayerFeedFailure.Unavailable,
                "Couldn't reach TCGplayer for a bearer token.",
                exception
            );
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                logger.LogWarning(
                    "The TCGplayer token request failed with status {StatusCode}.",
                    status
                );
                // 4xx means TCGplayer turned the credentials down; 429 and 5xx are its side.
                var failure =
                    status is >= 400 and < 500
                    && response.StatusCode != HttpStatusCode.TooManyRequests
                        ? TcgplayerFeedFailure.AccessRefused
                        : TcgplayerFeedFailure.Unavailable;
                throw new TcgplayerFeedException(
                    failure,
                    $"The TCGplayer token request failed with status {status}."
                );
            }

            var token = await ReadTokenAsync(response, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _current, token);
            logger.LogInformation(
                "Obtained a TCGplayer bearer token; it will be refreshed after {RefreshAt:o}.",
                token.RefreshAt
            );
            return token.Value;
        }
    }

    private async Task<CachedToken> ReadTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        TokenResponse? body;
        try
        {
            var stream = await response
                .Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            body = await JsonSerializer
                .DeserializeAsync<TokenResponse>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // The JsonException is not attached: its message can quote the body.
            body = null;
        }

        var expires = ReadExpiry(body);
        if (string.IsNullOrWhiteSpace(body?.AccessToken) || expires is null)
        {
            throw new TcgplayerFeedException(
                TcgplayerFeedFailure.ResponseInvalid,
                "The TCGplayer token response was missing the token or its expiry."
            );
        }

        return new CachedToken(body.AccessToken, expires.Value - RefreshMargin);
    }

    private DateTimeOffset? ReadExpiry(TokenResponse? body)
    {
        if (
            DateTimeOffset.TryParseExact(
                body?.Expires,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var expires
            )
        )
        {
            return expires;
        }

        return body?.ExpiresIn is > 0
            ? timeProvider.GetUtcNow().AddSeconds(body.ExpiresIn.Value)
            : null;
    }

    private sealed record CachedToken(string Value, DateTimeOffset RefreshAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName(".expires")] string? Expires,
        [property: JsonPropertyName("expires_in")] long? ExpiresIn
    );
}
