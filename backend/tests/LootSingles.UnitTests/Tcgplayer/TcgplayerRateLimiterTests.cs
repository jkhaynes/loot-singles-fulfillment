using System.Collections.Concurrent;
using LootSingles.Infrastructure.Tcgplayer;
using LootSingles.UnitTests.CardCatalog;

namespace LootSingles.UnitTests.Tcgplayer;

/// <summary>
/// T018: the sliding-window limiter that keeps the app under its TCGplayer call budget
/// (research.md §9). Time is a <see cref="FakeTimeProvider"/>; nothing waits on the real clock.
/// </summary>
public sealed class TcgplayerRateLimiterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task N_calls_go_through_at_once_and_call_N_plus_1_waits_until_the_oldest_is_60_seconds_old()
    {
        var time = new FakeTimeProvider(Start);
        var limiter = new TcgplayerRateLimiter(3, time);

        var first = limiter.WaitAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(10));
        var second = limiter.WaitAsync(CancellationToken.None);
        var third = limiter.WaitAsync(CancellationToken.None);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.True(third.IsCompletedSuccessfully);

        time.Advance(TimeSpan.FromSeconds(10)); // t = 20s
        var fourth = limiter.WaitAsync(CancellationToken.None);
        Assert.False(fourth.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(39)); // t = 59s: the oldest call is 59 seconds old
        await SettleAsync(time, fourth);
        Assert.False(fourth.IsCompleted);
        Assert.Equal(3, limiter.CallCount);

        time.Advance(TimeSpan.FromSeconds(1)); // t = 60s: the oldest call leaves the window
        await SettleAsync(time, fourth);
        Assert.True(fourth.IsCompletedSuccessfully);
        Assert.Equal(4, limiter.CallCount);

        // The window is full again (t = 10, 10, 60), so the next call waits for t = 70.
        var fifth = limiter.WaitAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(9));
        await SettleAsync(time, fifth);
        Assert.False(fifth.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await SettleAsync(time, fifth);
        Assert.True(fifth.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Over_a_long_simulated_run_no_60_second_window_holds_more_than_N()
    {
        const int budget = 10;
        var time = new FakeTimeProvider(Start);
        var limiter = new TcgplayerRateLimiter(budget, time);
        var admitted = new ConcurrentBag<DateTimeOffset>();
        var calls = new List<Task>();
        var random = new Random(20);

        async Task Call()
        {
            await limiter.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            admitted.Add(time.GetUtcNow());
        }

        // Five minutes of bursty arrivals well above the budget, with quiet gaps, then drain.
        for (var second = 0; second < 40 * 60; second++)
        {
            if (second < 5 * 60 && random.Next(4) == 0)
            {
                var burst = random.Next(1, 6);
                for (var i = 0; i < burst; i++)
                {
                    calls.Add(Call());
                }
            }
            await SettleAsync(time, [.. calls]);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        await SettleAsync(time, [.. calls]);

        Assert.All(calls, c => Assert.True(c.IsCompletedSuccessfully));
        Assert.Equal(calls.Count, admitted.Count);
        Assert.Equal(calls.Count, limiter.CallCount);
        Assert.True(calls.Count > budget * 5, "the run should be long enough to saturate");

        var sorted = admitted.Order().ToList();
        foreach (var windowStart in sorted)
        {
            var inWindow = sorted.Count(t =>
                t >= windowStart && t < windowStart + TimeSpan.FromSeconds(60)
            );
            Assert.True(inWindow <= budget, $"{inWindow} calls in the window from {windowStart}");
        }

        // A saturated limiter still uses its whole budget: some window holds exactly N.
        Assert.Contains(
            sorted,
            s => sorted.Count(t => t >= s && t < s + TimeSpan.FromSeconds(60)) == budget
        );
    }

    [Fact]
    public async Task Concurrent_callers_share_one_budget()
    {
        const int budget = 4;
        var time = new FakeTimeProvider(Start);
        var limiter = new TcgplayerRateLimiter(budget, time);
        using var go = new ManualResetEventSlim();

        var calls = Enumerable
            .Range(0, 10)
            .Select(_ =>
                Task.Run(async () =>
                {
                    go.Wait();
                    await limiter.WaitAsync(CancellationToken.None);
                })
            )
            .ToArray();
        go.Set();

        await SettleAsync(time, calls);
        Assert.Equal(4, calls.Count(c => c.IsCompletedSuccessfully));
        Assert.Equal(4, limiter.CallCount);

        time.Advance(TimeSpan.FromSeconds(60));
        await SettleAsync(time, calls);
        Assert.Equal(8, calls.Count(c => c.IsCompletedSuccessfully));
        Assert.Equal(8, limiter.CallCount);

        time.Advance(TimeSpan.FromSeconds(60));
        await SettleAsync(time, calls);
        Assert.Equal(10, calls.Count(c => c.IsCompletedSuccessfully));
        Assert.Equal(10, limiter.CallCount);
    }

    [Fact]
    public async Task Cancelling_a_waiting_call_throws_and_does_not_use_up_a_slot()
    {
        var time = new FakeTimeProvider(Start);
        var limiter = new TcgplayerRateLimiter(1, time);
        await limiter.WaitAsync(CancellationToken.None);

        using var cancellation = new CancellationTokenSource();
        var cancelled = limiter.WaitAsync(cancellation.Token);
        var other = limiter.WaitAsync(CancellationToken.None);
        Assert.False(cancelled.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(1, limiter.CallCount);

        // The freed slot goes to the call that is still waiting, not to the cancelled one.
        time.Advance(TimeSpan.FromSeconds(60));
        await SettleAsync(time, other);
        Assert.True(other.IsCompletedSuccessfully);
        Assert.Equal(2, limiter.CallCount);

        // And the window is now full with that one call, so the next waits a full minute.
        var next = limiter.WaitAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(59));
        await SettleAsync(time, next);
        Assert.False(next.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await SettleAsync(time, next);
        Assert.True(next.IsCompletedSuccessfully);
        Assert.Equal(3, limiter.CallCount);
    }

    [Fact]
    public async Task Handler_waits_for_the_limiter_before_sending()
    {
        var time = new FakeTimeProvider(Start);
        var limiter = new TcgplayerRateLimiter(1, time);
        var inner = StubHttpMessageHandler.ReturningJson("{}");
        using var invoker = new HttpMessageInvoker(
            new TcgplayerRateLimitHandler(limiter) { InnerHandler = inner }
        );

        using var firstResponse = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/one"),
            CancellationToken.None
        );
        var second = invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/two"),
            CancellationToken.None
        );
        await SettleAsync(time, second);
        Assert.False(second.IsCompleted);
        Assert.Single(inner.Requests);

        time.Advance(TimeSpan.FromSeconds(60));
        await SettleAsync(time, second);
        using var secondResponse = await second;
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(2, limiter.CallCount);
    }

    /// <summary>
    /// Waits (briefly, in real time) until every call has either finished or is parked on one of
    /// the fake clock's timers, so the next <see cref="FakeTimeProvider.Advance"/> sees a settled
    /// state. The deadline only guards against a hang; it never decides an outcome.
    /// </summary>
    private static async Task SettleAsync(FakeTimeProvider time, params Task[] calls)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (calls.Count(c => !c.IsCompleted) != time.ActiveTimerCount)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Calls did not settle on the fake clock.");
            }
            await Task.Delay(1);
        }
    }
}
