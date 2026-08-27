import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { listEmployees } from './adminApi'
import type { EmployeeListItem } from './adminApi'
import './AdminPage.css'

export function AdminPage() {
  const [employees, setEmployees] = useState<EmployeeListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)

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
