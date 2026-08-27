import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  changeEmployeeRole,
  createEmployee,
  deactivateEmployee,
  InvalidPinError,
  listEmployees,
  reactivateEmployee,
  resetPin,
  unlockEmployee,
  UsernameTakenError,
  WouldRemoveLastManagerAdminError,
} from './adminApi'
import type { EmployeeListItem } from './adminApi'
import './AdminPage.css'

export function AdminPage() {
  const [employees, setEmployees] = useState<EmployeeListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)

  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [initialPin, setInitialPin] = useState('')
  const [role, setRole] = useState('Picker')
  const [isCreating, setIsCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)

  const [actingEmployeeId, setActingEmployeeId] = useState<number | null>(null)
  const [rowActionError, setRowActionError] = useState<string | null>(null)
  const [rowActionSuccess, setRowActionSuccess] = useState<string | null>(null)
  const [pinDrafts, setPinDrafts] = useState<Record<number, string>>({})

  async function refreshEmployees() {
    setIsLoading(true)
    setHasError(false)
    try {
      setEmployees(await listEmployees())
    } catch {
      setHasError(true)
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false

    listEmployees()
      .then((result) => {
        if (!cancelled) setEmployees(result)
      })
      .catch(() => {
        if (!cancelled) setHasError(true)
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  async function handleCreateEmployee(event: FormEvent) {
    event.preventDefault()
    setIsCreating(true)
    setCreateError(null)
    try {
      await createEmployee(username, displayName, initialPin, role)
      setUsername('')
      setDisplayName('')
      setInitialPin('')
      setRole('Picker')
      await refreshEmployees()
    } catch (error) {
      if (error instanceof UsernameTakenError) {
        setCreateError('Username is already in use.')
      } else {
        setCreateError("Couldn't create this employee. Try again.")
      }
    } finally {
      setIsCreating(false)
    }
  }

  async function runRowAction(
    employeeId: number,
    action: () => Promise<void>,
    options: {
      onSuccess?: () => void
      successMessage?: string
      refresh?: boolean
      mapError: (error: unknown) => string
    },
  ) {
    setActingEmployeeId(employeeId)
    setRowActionError(null)
    setRowActionSuccess(null)
    try {
      await action()
      if (options.refresh) {
        await refreshEmployees()
      }
      options.onSuccess?.()
      if (options.successMessage) {
        setRowActionSuccess(options.successMessage)
      }
    } catch (error) {
      setRowActionError(options.mapError(error))
    } finally {
      setActingEmployeeId(null)
    }
  }

  function handleDeactivate(employeeId: number) {
    return runRowAction(employeeId, () => deactivateEmployee(employeeId), {
      refresh: true,
      mapError: (error) =>
        error instanceof WouldRemoveLastManagerAdminError
          ? 'Removing this employee would leave zero active Manager/Admin employees.'
          : "Couldn't remove this employee. Try again.",
    })
  }

  function handleReactivate(employeeId: number) {
    return runRowAction(employeeId, () => reactivateEmployee(employeeId), {
      refresh: true,
      mapError: () => "Couldn't restore this employee. Try again.",
    })
  }

  function handleChangeRole(employeeId: number, newRole: string) {
    return runRowAction(employeeId, () => changeEmployeeRole(employeeId, newRole), {
      refresh: true,
      mapError: (error) =>
        error instanceof WouldRemoveLastManagerAdminError
          ? "Changing this employee's role would leave zero active Manager/Admin employees."
          : "Couldn't change this employee's role. Try again.",
    })
  }

  function handleResetPin(employeeId: number) {
    const newPin = pinDrafts[employeeId] ?? ''
    return runRowAction(employeeId, () => resetPin(employeeId, newPin), {
      onSuccess: () => setPinDrafts((previous) => ({ ...previous, [employeeId]: '' })),
      successMessage: 'PIN reset.',
      mapError: (error) =>
        error instanceof InvalidPinError
          ? 'A 4-digit numeric PIN is required.'
          : "Couldn't reset this employee's PIN. Try again.",
    })
  }

  function handleUnlock(employeeId: number) {
    return runRowAction(employeeId, () => unlockEmployee(employeeId), {
      refresh: true,
      successMessage: 'Account unlocked.',
      mapError: () => "Couldn't unlock this employee. Try again.",
    })
  }

  return (
    <main className="admin-page">
      <header className="admin-header">
        <div>
          <h1>Manage Employees</h1>
          <p>View and manage picker and manager accounts.</p>
        </div>
        <nav className="admin-navigation" aria-label="Admin navigation">
          <Link to="/">Dashboard</Link>
        </nav>
      </header>

      <section className="admin-panel" aria-label="Create Employee">
        <h2>Create Employee</h2>
        <form className="admin-create-form" onSubmit={handleCreateEmployee}>
          {createError && (
            <p role="alert" className="admin-banner admin-banner--error">
              {createError}
            </p>
          )}
          <div className="admin-form-field">
            <label htmlFor="create-employee-username">Username</label>
            <input
              id="create-employee-username"
              className="admin-input"
              type="text"
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              required
            />
          </div>
          <div className="admin-form-field">
            <label htmlFor="create-employee-display-name">Display Name</label>
            <input
              id="create-employee-display-name"
              className="admin-input"
              type="text"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              required
            />
          </div>
          <div className="admin-form-field">
            <label htmlFor="create-employee-initial-pin">Initial PIN</label>
            <input
              id="create-employee-initial-pin"
              className="admin-input"
              type="password"
              inputMode="numeric"
              value={initialPin}
              onChange={(event) => setInitialPin(event.target.value)}
              required
            />
          </div>
          <div className="admin-form-field">
            <label htmlFor="create-employee-role">Role</label>
            <select
              id="create-employee-role"
              className="admin-select"
              value={role}
              onChange={(event) => setRole(event.target.value)}
            >
              <option value="Picker">Picker</option>
              <option value="ManagerAdmin">Manager/Admin</option>
            </select>
          </div>
          <button
            type="submit"
            className="admin-button admin-button--primary"
            disabled={isCreating}
          >
            {isCreating ? 'Creating…' : 'Create Employee'}
          </button>
        </form>
      </section>

      {rowActionError && (
        <p role="alert" className="admin-banner admin-banner--error">
          {rowActionError}
        </p>
      )}
      {rowActionSuccess && <p className="admin-banner admin-banner--success">{rowActionSuccess}</p>}

      <section className="admin-panel">
        <h2>Employee Roster</h2>
        {isLoading ? (
          <p className="admin-state">Loading employees…</p>
        ) : hasError ? (
          <p role="alert" className="admin-state admin-state--error">
            Couldn't load employees. Try refreshing the page.
          </p>
        ) : employees.length === 0 ? (
          <p className="admin-state">No employees yet.</p>
        ) : (
          <div className="admin-table-wrapper">
            <table className="admin-employee-table">
              <thead>
                <tr>
                  <th scope="col">Username</th>
                  <th scope="col">Display Name</th>
                  <th scope="col">Role</th>
                  <th scope="col">Status</th>
                  <th scope="col">Manage</th>
                </tr>
              </thead>
              <tbody>
                {employees.map((employee) => (
                  <tr key={employee.employeeId}>
                    <td className="admin-employee-table__username">{employee.username}</td>
                    <td>{employee.displayName}</td>
                    <td>{employee.role}</td>
                    <td>
                      <span
                        className="admin-badge"
                        data-status={employee.isActive ? 'active' : 'inactive'}
                      >
                        {employee.isActive ? 'Active' : 'Inactive'}
                      </span>
                      {employee.isLocked && (
                        <span className="admin-badge admin-badge--locked">Locked</span>
                      )}
                    </td>
                    <td>
                      <div className="admin-row-actions">
                        <select
                          aria-label="Change role"
                          className="admin-select admin-select--compact"
                          value={employee.role}
                          onChange={(event) =>
                            handleChangeRole(employee.employeeId, event.target.value)
                          }
                          disabled={actingEmployeeId === employee.employeeId}
                        >
                          <option value="Picker">Picker</option>
                          <option value="ManagerAdmin">Manager/Admin</option>
                        </select>

                        <div className="admin-pin-reset">
                          <input
                            aria-label="New PIN"
                            className="admin-input admin-input--compact"
                            type="password"
                            inputMode="numeric"
                            value={pinDrafts[employee.employeeId] ?? ''}
                            onChange={(event) =>
                              setPinDrafts((previous) => ({
                                ...previous,
                                [employee.employeeId]: event.target.value,
                              }))
                            }
                          />
                          <button
                            type="button"
                            className="admin-button admin-button--ghost"
                            onClick={() => handleResetPin(employee.employeeId)}
                            disabled={actingEmployeeId === employee.employeeId}
                          >
                            Reset PIN
                          </button>
                        </div>

                        {employee.isActive ? (
                          <button
                            type="button"
                            className="admin-button admin-button--ghost"
                            onClick={() => handleDeactivate(employee.employeeId)}
                            disabled={actingEmployeeId === employee.employeeId}
                          >
                            Remove
                          </button>
                        ) : (
                          <button
                            type="button"
                            className="admin-button admin-button--ghost"
                            onClick={() => handleReactivate(employee.employeeId)}
                            disabled={actingEmployeeId === employee.employeeId}
                          >
                            Restore
                          </button>
                        )}
                        {employee.isLocked && (
                          <button
                            type="button"
                            className="admin-button admin-button--ghost"
                            onClick={() => handleUnlock(employee.employeeId)}
                            disabled={actingEmployeeId === employee.employeeId}
                          >
                            Unlock
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </main>
  )
}
