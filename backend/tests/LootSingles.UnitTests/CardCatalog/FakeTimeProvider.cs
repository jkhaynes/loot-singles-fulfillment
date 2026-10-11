namespace LootSingles.UnitTests.CardCatalog;

/// <summary>
/// Hand-rolled <see cref="TimeProvider"/> stub (research.md §9 - no mocking library in this
/// codebase) that returns a controllable, manually-advanced current time, so cache-expiry
/// behavior can be tested without waiting on real wall-clock time. Its timestamp clock and
/// one-shot timers (what <c>Task.Delay(TimeSpan, TimeProvider, CancellationToken)</c> uses) follow
/// the same manual clock: a timer fires, on the thread that calls <see cref="Advance"/>, once the
/// clock reaches its due time.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    /// <summary>Timers created and not yet fired or disposed.</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    public void Advance(TimeSpan duration)
    {
        lock (_gate)
        {
            _now += duration;
        }

        while (true)
        {
            FakeTimer? due;
            lock (_gate)
            {
                due = _timers.Where(t => t.DueAt <= _now).MinBy(t => t.DueAt);
                if (due is null)
                {
                    return;
                }
                _timers.Remove(due);
            }
            due.Fire();
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        if (period != Timeout.InfiniteTimeSpan)
        {
            throw new NotSupportedException("FakeTimeProvider only supports one-shot timers.");
        }

        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
        : ITimer
    {
        public DateTimeOffset DueAt { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = owner._now + dueTime;
                    owner._timers.Add(this);
                }
            }
            return true;
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
