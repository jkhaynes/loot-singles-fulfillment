import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RequireManagerAdmin } from '../../src/features/admin/RequireManagerAdmin'
import { AuthProvider } from '../../src/features/auth/AuthContext'
import * as authApi from '../../src/features/auth/authApi'

vi.mock('../../src/features/auth/authApi', async (original) => ({
  ...(await original<typeof import('../../src/features/auth/authApi')>()),
  me: vi.fn(),
}))

function renderGuard() {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={['/admin']}>
        <Routes>
          <Route
            path="/admin"
            element={
              <RequireManagerAdmin>
                <p>Admin-only content</p>
              </RequireManagerAdmin>
            }
          />
          <Route path="/" element={<p>Dashboard page</p>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  )
}

describe('RequireManagerAdmin', () => {
  beforeEach(() => vi.resetAllMocks())

  it('renders its children when the employee is a Manager/Admin', async () => {
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 1,
      displayName: 'Manager Manny',
      role: 'ManagerAdmin',
    })

    renderGuard()

    expect(await screen.findByText('Admin-only content')).toBeInTheDocument()
  })

  it('redirects away when the employee is not a Manager/Admin', async () => {
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 2,
      displayName: 'Percy Picker',
      role: 'Picker',
    })

    renderGuard()

    expect(await screen.findByText('Dashboard page')).toBeInTheDocument()
    expect(screen.queryByText('Admin-only content')).not.toBeInTheDocument()
  })
})
