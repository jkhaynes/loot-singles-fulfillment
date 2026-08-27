import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminPage } from '../../src/features/admin/AdminPage'
import * as adminApi from '../../src/features/admin/adminApi'

vi.mock('../../src/features/admin/adminApi', async (original) => ({
  ...(await original<typeof import('../../src/features/admin/adminApi')>()),
  listEmployees: vi.fn(),
}))

function renderPage() {
  return render(
    <MemoryRouter>
      <AdminPage />
    </MemoryRouter>,
  )
}

describe('AdminPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('renders every employee with username, display name, role, and active/locked status', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([
      {
        employeeId: 1,
        username: 'mmanager',
        displayName: 'Manager Manny',
        role: 'ManagerAdmin',
        isActive: true,
        isLocked: false,
      },
      {
        employeeId: 2,
        username: 'ppicker',
        displayName: 'Percy Picker',
        role: 'Picker',
        isActive: false,
        isLocked: true,
      },
    ])

    renderPage()

    const managerRow = await screen.findByRole('row', { name: /mmanager/i })
    expect(within(managerRow).getByText('Manager Manny')).toBeInTheDocument()
    expect(within(managerRow).getByText('ManagerAdmin')).toBeInTheDocument()
    expect(within(managerRow).getByText(/active/i)).toBeInTheDocument()

    const pickerRow = screen.getByRole('row', { name: /ppicker/i })
    expect(within(pickerRow).getByText('Percy Picker')).toBeInTheDocument()
    expect(within(pickerRow).getByText('Picker')).toBeInTheDocument()
    expect(within(pickerRow).getByText(/inactive/i)).toBeInTheDocument()
    expect(within(pickerRow).getByText(/locked/i)).toBeInTheDocument()
  })

  it('shows a distinct error state when loading fails', async () => {
    vi.mocked(adminApi.listEmployees).mockRejectedValue(new Error('server unavailable'))

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t load/i)
  })

  it('shows a clear empty state when there are no employees', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([])

    renderPage()

    expect(await screen.findByText(/no employees/i)).toBeInTheDocument()
  })
})
