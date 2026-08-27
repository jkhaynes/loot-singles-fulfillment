# Phase 1 Data Model: Manager Admin Screen

## Employee (existing entity, `backend/src/LootSingles.Domain/Employees/Employee.cs`)

No schema changes. This feature exposes existing attributes (`Username`, `DisplayName`, `Role`,
`IsActive`, `IsLocked`) through a UI for the first time, and adds the *behavior* of changing the
existing `Role` field after creation — previously fixed for the lifetime of the account, now
mutable via the new `ChangeRoleAsync` service method.

**Invariant this feature introduces**: at least one `Employee` with `Role == EmployeeRole.ManagerAdmin`
and `IsActive == true` must exist at all times. Enforced by the guard in research.md §1 on every
call to `DeactivateAsync` and the new `ChangeRoleAsync` — not a database constraint (a check
constraint can't reference an aggregate across rows), but a serialized application-level guard.

## EmployeeAuditActionType (existing enum, `backend/src/LootSingles.Domain/Employees/EmployeeAuditActionType.cs`)

```csharp
public enum EmployeeAuditActionType
{
    Login = 0,
    Logout = 1,
    AccountCreated = 2,
    Deactivated = 3,
    Reactivated = 4,
    PinReset = 5,
    Unlocked = 6,
    RoleChanged = 7,  // new
}
```

`RoleChanged` is the only addition. Since this is an `int`-backed enum persisted as its integer
value (confirmed: no `HasConversion<string>()` on this column), appending a new value at the end
is a purely additive change — no migration touches existing `EmployeeAuditEvents` rows, and no
existing `MigrationTests.cs`/schema assertions reference this enum's value set.

## EmployeeManagementOutcome (existing enum, `backend/src/LootSingles.Application/Auth/EmployeeManagementResult.cs`)

```csharp
public enum EmployeeManagementOutcome
{
    Success,
    NotFound,
    UsernameTaken,
    InvalidRequest,
    WouldRemoveLastManagerAdmin,  // new — returned by DeactivateAsync and ChangeRoleAsync
}
```

`WouldRemoveLastManagerAdmin` is returned when the FR-007 guard (research.md §1) determines the
requested action would leave zero active Manager/Admin employees. `EmployeeManagementResult` itself
gains no new fields — this is purely a new outcome value, following the exact existing pattern
(`public static readonly EmployeeManagementResult WouldRemoveLastManagerAdmin = new(...)`).

## No new entities

`Employee`, `EmployeeAuditEvent`, and `EmployeeRole` (the existing `Picker`/`ManagerAdmin` two-role
enum) are unchanged in shape. This feature is additive at the enum-value and behavior level only.
