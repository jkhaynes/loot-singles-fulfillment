namespace LootSingles.Infrastructure.Tcgplayer;

/// <summary>
/// Keeps every TCGplayer request this process makes within <c>callsPerMinute</c> in any 60-second
/// window (research.md §9; the API agreement allows 300 a minute across stage and production). A
/// sliding log of admission timestamps: when it is full, a caller waits until the oldest entry is
/// 60 seconds old. Registered as a singleton so all requests share one budget.
/// </summary>
public sealed class TcgplayerRateLimiter(int callsPerMinute, TimeProvider timeProvider)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private readonly Queue<long> _admitted = new();
    private readonly Lock _gate = new();
    private long _callCount;

    /// <summary>Calls admitted since process start.</summary>
    public long CallCount => Interlocked.Read(ref _callCount);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan wait;
            lock (_gate)
            {
                var now = timeProvider.GetTimestamp();
                while (
                    _admitted.Count > 0
                    && timeProvider.GetElapsedTime(_admitted.Peek(), now) >= Window
                )
                {
                    _admitted.Dequeue();
                }

                if (_admitted.Count < callsPerMinute)
                {
                    // Check and claim happen under one lock, so two waiters woken together can
                    // never both take the same freed slot: the loser loops and waits again.
                    _admitted.Enqueue(now);
                    Interlocked.Increment(ref _callCount);
                    return;
                }

                wait = Window - timeProvider.GetElapsedTime(_admitted.Peek(), now);
            }

            // Never awaited while holding the lock. Cancellation throws here, before any slot is
            // claimed.
            await Task.Delay(wait, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
