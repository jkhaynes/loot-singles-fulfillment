export interface EmployeeListItem {
  employeeId: number
  username: string
  displayName: string
  role: string
  isActive: boolean
  isLocked: boolean
}

export class UsernameTakenError extends Error {
  constructor() {
    super('Username is already in use')
    this.name = 'UsernameTakenError'
  }
}

export class InvalidEmployeeRequestError extends Error {
  constructor() {
    super('Valid employee details are required')
    this.name = 'InvalidEmployeeRequestError'
  }
}

export class WouldRemoveLastManagerAdminError extends Error {
  constructor() {
    super('This would leave zero active Manager/Admin employees')
    this.name = 'WouldRemoveLastManagerAdminError'
  }
}

export class InvalidPinError extends Error {
  constructor() {
    super('A 4-digit numeric PIN is required')
    this.name = 'InvalidPinError'
  }
}

export async function listEmployees(): Promise<EmployeeListItem[]> {
  const response = await fetch('/api/employees', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load employees (status ${response.status})`)
  }

  return (await response.json()) as EmployeeListItem[]
}

export async function createEmployee(
  username: string,
  displayName: string,
  initialPin: string,
  role: string,
): Promise<void> {
  const response = await fetch('/api/employees', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, displayName, initialPin, role }),
  })

  if (response.status === 409) {
    throw new UsernameTakenError()
  }

  if (response.status === 400) {
    throw new InvalidEmployeeRequestError()
  }

  if (!response.ok) {
    throw new Error(`Failed to create employee (status ${response.status})`)
  }
}

export async function deactivateEmployee(id: number): Promise<void> {
  const response = await fetch(`/api/employees/${id}/deactivate`, {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 409) {
    throw new WouldRemoveLastManagerAdminError()
  }

  if (!response.ok) {
    throw new Error(`Failed to deactivate employee (status ${response.status})`)
  }
}

export async function reactivateEmployee(id: number): Promise<void> {
  const response = await fetch(`/api/employees/${id}/reactivate`, {
    method: 'POST',
    credentials: 'include',
  })

  if (!response.ok) {
    throw new Error(`Failed to reactivate employee (status ${response.status})`)
  }
}

export async function changeEmployeeRole(id: number, role: string): Promise<void> {
  const response = await fetch(`/api/employees/${id}/change-role`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ role }),
  })

  if (response.status === 409) {
    throw new WouldRemoveLastManagerAdminError()
  }

  if (!response.ok) {
    throw new Error(`Failed to change employee role (status ${response.status})`)
  }
}

export async function resetPin(id: number, newPin: string): Promise<void> {
  const response = await fetch(`/api/employees/${id}/reset-pin`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ newPin }),
  })

  if (response.status === 400) {
    throw new InvalidPinError()
  }

  if (!response.ok) {
    throw new Error(`Failed to reset PIN (status ${response.status})`)
  }
}

export async function unlockEmployee(id: number): Promise<void> {
  const response = await fetch(`/api/employees/${id}/unlock`, {
    method: 'POST',
    credentials: 'include',
  })

  if (!response.ok) {
    throw new Error(`Failed to unlock employee (status ${response.status})`)
  }
}
