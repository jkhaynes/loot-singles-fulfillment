import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AuthProvider, useAuth } from '../../src/features/auth/AuthContext'
import * as authApi from '../../src/features/auth/authApi'

vi.mock('../../src/features/auth/authApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/features/auth/authApi')>()
  return { ...actual, me: vi.fn(), logout: vi.fn() }
})

function SessionStatus() {
  const { employee, isLoading, logout } = useAuth()
  if (isLoading) return <p>Loading</p>
  if (!employee) return <p>Signed out</p>
  return (
    <div>
      <p>Signed in as {employee.displayName}</p>
      <button onClick={logout}>Log out</button>
    </div>
  )
}

describe('AuthContext', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('restores the authenticated session on load', async () => {
    vi.mocked(authApi.me).mockResolvedValue({ employeeId: 1, displayName: 'Jamie', role: 'Picker' })

    render(
      <AuthProvider>
        <SessionStatus />
      </AuthProvider>,
    )

    expect(await screen.findByText('Signed in as Jamie')).toBeInTheDocument()
  })

  it('calls the logout endpoint and clears the session', async () => {
    vi.mocked(authApi.me).mockResolvedValue({ employeeId: 1, displayName: 'Jamie', role: 'Picker' })
    vi.mocked(authApi.logout).mockResolvedValue()
    const user = userEvent.setup()
    render(
      <AuthProvider>
        <SessionStatus />
      </AuthProvider>,
    )
    await screen.findByText('Signed in as Jamie')

    await user.click(screen.getByRole('button', { name: 'Log out' }))

    expect(authApi.logout).toHaveBeenCalledOnce()
    expect(await screen.findByText('Signed out')).toBeInTheDocument()
  })
})
