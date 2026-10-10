namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Bounds one TCGplayer HTTP attempt, response body included, to <see cref="AttemptTimeout"/>
/// (ruling R15). It sits innermost, below the rate-limit handler, so the time a request spends
/// waiting for a slot never counts against it: a large import slows down instead of failing
/// (FR-021). The token request and the 401 retry pass through it too. The client's own
/// <c>HttpClient.Timeout</c> is infinite for the same reason.
///
/// A timeout surfaces as a <see cref="TaskCanceledException"/> wrapping a
/// <see cref="TimeoutException"/>, as <c>HttpClient.Timeout</c> does, while the caller's token is
/// not cancelled, so the client maps it to Unavailable. Caller cancellation passes through as is.
/// </summary>
public sealed class TcgplayerAttemptTimeoutHandler(TimeProvider timeProvider) : DelegatingHandler
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        using var timeout = new CancellationTokenSource(AttemptTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, linked.Token).ConfigureAwait(false);
            // HttpClient would otherwise read the body after this handler returns, unbounded.
            await response.Content.LoadIntoBufferAsync(linked.Token).ConfigureAwait(false);
            return response;
        }
        catch (Exception exception)
        {
            response?.Dispose();
            if (
                exception is OperationCanceledException
                && timeout.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested
            )
            {
                throw new TaskCanceledException(
                    $"The TCGplayer request did not complete within {AttemptTimeout.TotalSeconds:0} seconds.",
                    new TimeoutException(exception.Message, exception)
                );
            }

            throw;
        }
    }
}
