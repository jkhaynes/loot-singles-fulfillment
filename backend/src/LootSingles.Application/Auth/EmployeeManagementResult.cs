using LootSingles.Domain.Employees;

namespace LootSingles.Application.Auth;

public enum EmployeeManagementOutcome
{
    Success,
    NotFound,
    UsernameTaken,
    InvalidRequest,
    WouldRemoveLastManagerAdmin,
}

public sealed record EmployeeManagementResult(
    EmployeeManagementOutcome Outcome,
    Employee? Employee = null
)
{
    public static EmployeeManagementResult Success(Employee employee) =>
        new(EmployeeManagementOutcome.Success, employee);

    public static readonly EmployeeManagementResult NotFound = new(
        EmployeeManagementOutcome.NotFound
    );

    public static readonly EmployeeManagementResult UsernameTaken = new(
        EmployeeManagementOutcome.UsernameTaken
    );

    public static readonly EmployeeManagementResult InvalidRequest = new(
        EmployeeManagementOutcome.InvalidRequest
    );

    public static readonly EmployeeManagementResult WouldRemoveLastManagerAdmin = new(
        EmployeeManagementOutcome.WouldRemoveLastManagerAdmin
    );
}
