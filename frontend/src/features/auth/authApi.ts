export interface AuthenticatedEmployee {
  employeeId: number
  displayName: string
  role: string
}

interface ApiErrorBody {
  error: string
  message: string
}

export class AuthApiError extends Error {
  code: string

  constructor(code: string, message: string) {
    super(message)
    this.name = 'AuthApiError'
    this.code = code
  }
}

async function throwApiError(response: Response): Promise<never> {
  const body = (await response.json()) as ApiErrorBody
  throw new AuthApiError(body.error, body.message)
}

export async function login(username: string, pin: string): Promise<AuthenticatedEmployee> {
  const response = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ username, pin }),
  })

  if (!response.ok) {
    await throwApiError(response)
  }

  return (await response.json()) as AuthenticatedEmployee
}

export async function me(): Promise<AuthenticatedEmployee | null> {
  const response = await fetch('/api/auth/me', { credentials: 'include' })

  if (response.status === 401) {
    return null
  }

  if (!response.ok) {
    await throwApiError(response)
  }

  return (await response.json()) as AuthenticatedEmployee
}

export async function logout(): Promise<void> {
  await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
}
