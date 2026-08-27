# Quickstart: Validate the Manager Admin Screen

This validates that a Manager/Admin can finally reach, through a real screen, the
account-management capabilities feature 002 built server-side but never exposed in any UI (create,
deactivate/reactivate, reset PIN, unlock), plus the new role-change capability — and that the
system can never end up with zero active Manager/Admin employees as a result.

## Prerequisites

- .NET SDK 10.0.x, Node.js, a running SQL Server-family database with migrations applied
- At least two employee accounts to start: one Manager/Admin (to perform the actions) and one
  Picker (to confirm access is denied and to be acted upon)

## Run the app

```powershell
dotnet run --project backend/src/LootSingles.Api
npm --prefix frontend run dev
```

Log in as the Manager/Admin account.

## Manual validation

1. **Roster is visible** (US1, FR-001, SC-001): open the admin screen and confirm every employee —
   active and inactive — is listed with username, display name, role, active/inactive status, and
   locked/unlocked status.
2. **Picker cannot access the screen** (US1 AC2, FR-012, SC-003): log in as the Picker account and
   confirm the admin screen (and its route) is not reachable.
3. **Create a new employee** (US2, FR-002/FR-003, SC-002): create a new Picker account with a
   unique username, initial PIN, and role; confirm it appears in the roster and that employee can
   log in with those credentials. Attempt to create another account with the same username
   (case-insensitively) and confirm it's rejected with a clear conflict message.
4. **Remove and restore an employee** (US3, FR-004/FR-005, SC-004): remove the newly created
   employee and confirm they can no longer log in. Restore them and confirm they can log in again
   with their existing PIN, unchanged.
5. **Cannot remove the last Manager/Admin** (US3 AC3, FR-007, SC-005): with only one active
   Manager/Admin account, attempt to remove it (or remove all others first, then attempt) and
   confirm the action is rejected with a clear explanation, and the account remains active.
6. **Change a role, and the session invalidates immediately** (US4, FR-006/FR-010, SC-006): in one
   browser session, log in as a Picker; in a second (Manager/Admin) session, change that Picker's
   role to Manager/Admin. Confirm the first session's next request/reload requires re-authentication,
   and that logging back in grants Manager/Admin access. Repeat in reverse (demote a Manager/Admin
   to Picker) and confirm the same.
7. **Cannot demote the last Manager/Admin** (US4 AC4, FR-007): with only one active Manager/Admin,
   attempt to change their own role to Picker and confirm it's rejected.
8. **Reset a PIN and unlock an account** (US5, FR-008/FR-009, SC-007): reset an employee's PIN and
   confirm their old PIN no longer works while the new one does. Lock an account (repeated wrong
   PINs) and unlock it from the admin screen; confirm the employee can log in again with their
   existing (unchanged) PIN.
9. **Every action is audited** (FR-011, SC-008): after performing several of the above actions,
   confirm each is retrievable via the existing audit-events endpoint
   (`GET /api/employees/{id}/audit-events`), correctly attributed to the acting Manager/Admin,
   including the new `RoleChanged` event type.

## Automated validation

```powershell
dotnet build backend/LootSingles.sln
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
dotnet csharpier check backend
npm --prefix frontend run lint
npm --prefix frontend run test
npm --prefix frontend run build
npm --prefix frontend run format:check
npm --prefix frontend run test:e2e
```

Expected: a genuine concurrent-request test proves that two admin actions which would each
individually be fine, but together would leave zero active Manager/Admin employees, never both
succeed — mirroring `BootstrapAdminCommandTests.cs`'s existing real-SQL-Server bootstrap-race test
(research.md §1); `EmployeeManagementServiceTests.cs` covers every new outcome
(`WouldRemoveLastManagerAdmin` from both `DeactivateAsync` and `ChangeRoleAsync`); the extended
`EmployeesControllerTests.cs` covers the new endpoint and the extended `deactivate` response; a new
frontend component test covers the admin screen's role-guard and its action flows; the Playwright
suite (`admin.spec.ts`) covers create → see-in-roster → remove → restore, and a role change that
invalidates a second session, using two authenticated browser contexts.
