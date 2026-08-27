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
