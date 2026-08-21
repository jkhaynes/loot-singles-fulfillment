namespace LootSingles.Domain.Employees;

/// <summary>
/// The V1 employee roles (FR-002). "Manager/Admin" is treated as a single role per PRD §9.4;
/// <see cref="ManagerAdmin"/> is the code-facing name since '/' is not a valid identifier.
/// </summary>
public enum EmployeeRole
{
    Picker = 0,
    ManagerAdmin = 1,
}
