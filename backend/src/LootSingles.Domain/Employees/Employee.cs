namespace LootSingles.Domain.Employees;

/// <summary>
/// Represents an individual Loot staff member who can authenticate into the picker application.
/// PIN is never stored in plaintext (FR-003); <see cref="IsActive"/> and <see cref="IsLocked"/> are
/// independent (see specs/002-employee-authentication/data-model.md state transitions).
/// </summary>
public class Employee
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The username as entered/displayed. Uniqueness is enforced via <see cref="NormalizedUsername"/>.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Invariant-uppercase form of <see cref="Username"/>, used for case-insensitive uniqueness (FR-014).
    /// </summary>
    public required string NormalizedUsername { get; set; }

    /// <summary>
    /// The employee's display name (PRD §9.3).
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The PBKDF2 hash of the employee's 4-digit PIN (FR-003). Never the raw PIN.
    /// </summary>
    public required string PinHash { get; set; }

    /// <summary>
    /// The employee's role (FR-002).
    /// </summary>
    public required EmployeeRole Role { get; set; }

    /// <summary>
    /// Whether the account can currently authenticate at all. Deactivation preserves identity
    /// for historical attribution rather than deleting the record (FR-017).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The number of consecutive failed PIN attempts since the last successful login or lockout clear (FR-007).
    /// </summary>
    public int FailedAttemptCount { get; set; }

    /// <summary>
    /// Whether the account is locked from authenticating (FR-006). Cleared only by an explicit
    /// Manager/Admin unlock (FR-022) or by reactivating a previously-locked deactivated account
    /// (FR-015) — never by elapsed time.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// When this employee record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
