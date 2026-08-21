namespace LootSingles.Domain.Employees;

/// <summary>
/// Records attribution for a state-changing employee action, for later auditability (FR-018, SC-005).
/// Written only for actions that succeed.
/// </summary>
public class EmployeeAuditEvent
{
    /// <summary>
    /// Primary key, auto-incremented identity.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// The employee who performed the action. For <see cref="EmployeeAuditActionType.Login"/> and
    /// <see cref="EmployeeAuditActionType.Logout"/>, this is the employee who logged in/out.
    /// </summary>
    public required int ActorEmployeeId { get; set; }

    /// <summary>
    /// The employee acted upon, when different from the actor (e.g., a Manager/Admin action on
    /// another employee). Null for <see cref="EmployeeAuditActionType.Login"/>/<see cref="EmployeeAuditActionType.Logout"/>,
    /// where actor and target are the same employee.
    /// </summary>
    public int? TargetEmployeeId { get; set; }

    /// <summary>
    /// Which state-changing action occurred.
    /// </summary>
    public required EmployeeAuditActionType ActionType { get; set; }

    /// <summary>
    /// The server timestamp when the action occurred.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; set; }
}
