namespace LootSingles.Domain.Employees;

/// <summary>
/// The state-changing employee actions this feature attributes to a specific employee (FR-018).
/// </summary>
public enum EmployeeAuditActionType
{
    Login = 0,
    Logout = 1,
    AccountCreated = 2,
    Deactivated = 3,
    Reactivated = 4,
    PinReset = 5,
    Unlocked = 6,
    RoleChanged = 7,
}
