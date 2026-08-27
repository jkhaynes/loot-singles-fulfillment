export interface EmployeeListItem {
  employeeId: number
  username: string
  displayName: string
  role: string
  isActive: boolean
  isLocked: boolean
}

export async function listEmployees(): Promise<EmployeeListItem[]> {
  const response = await fetch('/api/employees', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load employees (status ${response.status})`)
  }

  return (await response.json()) as EmployeeListItem[]
}
