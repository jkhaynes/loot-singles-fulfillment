import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LoginPage } from '../../src/features/auth/LoginPage'
import * as authApi from '../../src/features/auth/authApi'

vi.mock('../../src/features/auth/authApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/features/auth/authApi')>()
  return { ...actual, login: vi.fn() }
})

describe('LoginPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('renders username and PIN fields', () => {
    render(<LoginPage onLoginSuccess={vi.fn()} />)

    expect(screen.getByLabelText(/username/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/pin/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /log in/i })).toBeInTheDocument()
  })

  it('shows the generic error message on a failed login', async () => {
    vi.mocked(authApi.login).mockRejectedValue(
      new authApi.AuthApiError('invalid_credentials', 'Username or PIN is incorrect.'),
    )
    const user = userEvent.setup()
    render(<LoginPage onLoginSuccess={vi.fn()} />)

    await user.type(screen.getByLabelText(/username/i), 'jsmith')
    await user.type(screen.getByLabelText(/pin/i), '9999')
    await user.click(screen.getByRole('button', { name: /log in/i }))

    expect(await screen.findByText('Username or PIN is incorrect.')).toBeInTheDocument()
  })

  it('calls onLoginSuccess with the authenticated employee on success', async () => {
    const employee = { employeeId: 1, displayName: 'Jamie', role: 'Picker' }
    vi.mocked(authApi.login).mockResolvedValue(employee)
    const onLoginSuccess = vi.fn()
    const user = userEvent.setup()
    render(<LoginPage onLoginSuccess={onLoginSuccess} />)

    await user.type(screen.getByLabelText(/username/i), 'jsmith')
    await user.type(screen.getByLabelText(/pin/i), '1234')
    await user.click(screen.getByRole('button', { name: /log in/i }))

    await vi.waitFor(() => expect(onLoginSuccess).toHaveBeenCalledWith(employee))
  })
})
