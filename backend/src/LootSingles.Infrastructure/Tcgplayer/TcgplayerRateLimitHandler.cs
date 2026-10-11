namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Sits in the TCGplayer <see cref="HttpClient"/> pipeline so every request, token requests
/// included, waits for a slot in the shared <see cref="TcgplayerRateLimiter"/> before it is sent.
/// </summary>
public sealed class TcgplayerRateLimitHandler(TcgplayerRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
