using System.Net;
using System.Net.Http.Headers;
using LootSingles.Application.Import;

namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Puts <c>Authorization: bearer {token}</c> on every TCGplayer API request (contracts/
/// tcgplayer-upstream.md). The token comes from <see cref="TcgplayerTokenCache"/>, which sends its
/// token request through this handler's own inner pipeline, so placing the rate-limit handler
/// beneath this one counts token requests and retries too. A 401 gets one token refresh and one
/// retry; a second 401 is <see cref="TcgplayerFeedFailure.AccessRefused"/>.
/// </summary>
public sealed class TcgplayerAuthenticationHandler(TcgplayerTokenCache tokenCache)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        // A request can only be sent once; buffering lets the retry copy any body.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        var token = await tokenCache
            .GetTokenAsync(SendInnerAsync, cancellationToken)
            .ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await tokenCache
            .RefreshAsync(token, SendInnerAsync, cancellationToken)
            .ConfigureAwait(false);
        var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue("bearer", refreshed);
        var retried = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        if (retried.StatusCode != HttpStatusCode.Unauthorized)
        {
            return retried;
        }

        retried.Dispose();
        throw new TcgplayerFeedException(
            TcgplayerFeedFailure.AccessRefused,
            "TCGplayer answered 401 to a freshly fetched bearer token."
        );
    }

    private Task<HttpResponseMessage> SendInnerAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => base.SendAsync(request, cancellationToken);

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage original,
        CancellationToken cancellationToken
    )
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (original.Content is not null)
        {
            var body = await original
                .Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            clone.Content = new ByteArrayContent(body);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
