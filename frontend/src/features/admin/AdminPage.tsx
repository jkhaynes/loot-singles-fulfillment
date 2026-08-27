import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { createEmployee, listEmployees, UsernameTakenError } from './adminApi'
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

  return (
    <main className="admin-page">
      <header className="admin-header">
        <div>
          <h1>Manage Employees</h1>
          <p>View and manage picker and manager accounts.</p>
        </div>
        <nav aria-label="Admin navigation">
          <Link to="/">Dashboard</Link>
        </nav>
      </header>

      <section className="admin-create-employee" aria-label="Create Employee">
        <h2>Create Employee</h2>
        {createError && (
          <p role="alert" className="admin-state admin-state--error">
            {createError}
          </p>
        )}
        <form onSubmit={handleCreateEmployee}>
          <div className="admin-form-field">
            <label htmlFor="create-employee-username">Username</label>
            <input
              id="create-employee-username"
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
              value={role}
              onChange={(event) => setRole(event.target.value)}
            >
              <option value="Picker">Picker</option>
              <option value="ManagerAdmin">Manager/Admin</option>
            </select>
          </div>
          <button type="submit" disabled={isCreating}>
            {isCreating ? 'Creating…' : 'Create Employee'}
          </button>
        </form>
      </section>

      {isLoading ? (
        <p className="admin-state">Loading employees…</p>
      ) : hasError ? (
        <p role="alert" className="admin-state admin-state--error">
          Couldn't load employees. Try refreshing the page.
        </p>
      ) : employees.length === 0 ? (
        <p className="admin-state">No employees yet.</p>
      ) : (
        <table className="admin-employee-table">
          <thead>
            <tr>
              <th scope="col">Username</th>
              <th scope="col">Display Name</th>
              <th scope="col">Role</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {employees.map((employee) => (
              <tr key={employee.employeeId}>
                <td>{employee.username}</td>
                <td>{employee.displayName}</td>
                <td>{employee.role}</td>
                <td>
                  {employee.isActive ? 'Active' : 'Inactive'}
                  {employee.isLocked ? ' · Locked' : ''}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  )
}
