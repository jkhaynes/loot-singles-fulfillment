namespace LootSingles.Application.Auth;

/// <summary>
/// Configures protection against repeated failed employee PIN attempts (FR-006).
/// </summary>
public sealed class LockoutOptions
{
    public const string SectionName = "Authentication:Lockout";

    public int FailedAttemptThreshold { get; init; } = 5;
}
