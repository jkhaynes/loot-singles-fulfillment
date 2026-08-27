import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminPage } from '../../src/features/admin/AdminPage'
import * as adminApi from '../../src/features/admin/adminApi'
import { UsernameTakenError } from '../../src/features/admin/adminApi'

vi.mock('../../src/features/admin/adminApi', async (original) => ({
  ...(await original<typeof import('../../src/features/admin/adminApi')>()),
  listEmployees: vi.fn(),
  createEmployee: vi.fn(),
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

  it('creates a new employee and refreshes the roster to include it', async () => {
    vi.mocked(adminApi.listEmployees)
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        {
          employeeId: 3,
          username: 'newpicker',
          displayName: 'New Picker',
          role: 'Picker',
          isActive: true,
          isLocked: false,
        },
      ])
    vi.mocked(adminApi.createEmployee).mockResolvedValue(undefined)
    const user = userEvent.setup()

    renderPage()
    await screen.findByText(/no employees/i)

    await user.type(screen.getByLabelText(/username/i), 'newpicker')
    await user.type(screen.getByLabelText(/display name/i), 'New Picker')
    await user.type(screen.getByLabelText(/initial pin/i), '1234')
    await user.selectOptions(screen.getByLabelText(/role/i), 'Picker')
    await user.click(screen.getByRole('button', { name: /create employee/i }))

    expect(adminApi.createEmployee).toHaveBeenCalledWith(
      'newpicker',
      'New Picker',
      '1234',
      'Picker',
    )
    const row = await screen.findByRole('row', { name: /newpicker/i })
    expect(within(row).getByText('New Picker')).toBeInTheDocument()
  })

  it('shows a clear conflict message and does not add a row when the username is taken', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([])
    vi.mocked(adminApi.createEmployee).mockRejectedValue(new UsernameTakenError())
    const user = userEvent.setup()

    renderPage()
    await screen.findByText(/no employees/i)

    await user.type(screen.getByLabelText(/username/i), 'dupe')
    await user.type(screen.getByLabelText(/display name/i), 'Dupe Employee')
    await user.type(screen.getByLabelText(/initial pin/i), '1234')
    await user.selectOptions(screen.getByLabelText(/role/i), 'Picker')
    await user.click(screen.getByRole('button', { name: /create employee/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/already in use/i)
    expect(screen.queryByRole('row', { name: /dupe/i })).not.toBeInTheDocument()
  })
})
