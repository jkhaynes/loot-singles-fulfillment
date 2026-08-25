namespace LootSingles.UnitTests.CardCatalog;

/// <summary>
/// Hand-rolled <see cref="TimeProvider"/> stub (research.md §9 - no mocking library in this
/// codebase) that returns a controllable, manually-advanced current time, so cache-expiry
/// behavior can be tested without waiting on real wall-clock time.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now += duration;
}
