import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminPage } from '../../src/features/admin/AdminPage'
import * as adminApi from '../../src/features/admin/adminApi'
import {
  UsernameTakenError,
  WouldRemoveLastManagerAdminError,
} from '../../src/features/admin/adminApi'

vi.mock('../../src/features/admin/adminApi', async (original) => ({
  ...(await original<typeof import('../../src/features/admin/adminApi')>()),
  listEmployees: vi.fn(),
  createEmployee: vi.fn(),
  deactivateEmployee: vi.fn(),
  reactivateEmployee: vi.fn(),
  changeEmployeeRole: vi.fn(),
  resetPin: vi.fn(),
  unlockEmployee: vi.fn(),
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
    // The Role cell's text ("Picker") also matches the row's role-change <select>'s "Picker"
    // option, so this one assertion targets the Role cell by position rather than by text.
    expect(within(pickerRow).getAllByRole('cell')[2]).toHaveTextContent('Picker')
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

  it('removes an active employee and shows them as inactive afterward', async () => {
    vi.mocked(adminApi.listEmployees)
      .mockResolvedValueOnce([
        {
          employeeId: 5,
          username: 'toremove',
          displayName: 'To Remove',
          role: 'Picker',
          isActive: true,
          isLocked: false,
        },
      ])
      .mockResolvedValueOnce([
        {
          employeeId: 5,
          username: 'toremove',
          displayName: 'To Remove',
          role: 'Picker',
          isActive: false,
          isLocked: false,
        },
      ])
    vi.mocked(adminApi.deactivateEmployee).mockResolvedValue(undefined)
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /toremove/i })
    await user.click(within(row).getByRole('button', { name: /remove/i }))

    expect(adminApi.deactivateEmployee).toHaveBeenCalledWith(5)
    const updatedRow = await screen.findByRole('row', { name: /toremove/i })
    expect(within(updatedRow).getByText(/inactive/i)).toBeInTheDocument()
  })

  it('restores an inactive employee and shows them as active afterward', async () => {
    vi.mocked(adminApi.listEmployees)
      .mockResolvedValueOnce([
        {
          employeeId: 6,
          username: 'torestore',
          displayName: 'To Restore',
          role: 'Picker',
          isActive: false,
          isLocked: false,
        },
      ])
      .mockResolvedValueOnce([
        {
          employeeId: 6,
          username: 'torestore',
          displayName: 'To Restore',
          role: 'Picker',
          isActive: true,
          isLocked: false,
        },
      ])
    vi.mocked(adminApi.reactivateEmployee).mockResolvedValue(undefined)
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /torestore/i })
    await user.click(within(row).getByRole('button', { name: /restore/i }))

    expect(adminApi.reactivateEmployee).toHaveBeenCalledWith(6)
    const updatedRow = await screen.findByRole('row', { name: /torestore/i })
    expect(within(updatedRow).getByText(/^active$/i)).toBeInTheDocument()
  })

  it('shows a clear message and leaves the row unchanged when removal is blocked', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([
      {
        employeeId: 7,
        username: 'lastmanager',
        displayName: 'Last Manager',
        role: 'ManagerAdmin',
        isActive: true,
        isLocked: false,
      },
    ])
    vi.mocked(adminApi.deactivateEmployee).mockRejectedValue(new WouldRemoveLastManagerAdminError())
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /lastmanager/i })
    await user.click(within(row).getByRole('button', { name: /remove/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/manager/i)
    expect(
      within(screen.getByRole('row', { name: /lastmanager/i })).getByText(/^active$/i),
    ).toBeInTheDocument()
  })

  it("changes an employee's role and shows the new role afterward", async () => {
    vi.mocked(adminApi.listEmployees)
      .mockResolvedValueOnce([
        {
          employeeId: 8,
          username: 'promoteme',
          displayName: 'Promote Me',
          role: 'Picker',
          isActive: true,
          isLocked: false,
        },
      ])
      .mockResolvedValueOnce([
        {
          employeeId: 8,
          username: 'promoteme',
          displayName: 'Promote Me',
          role: 'ManagerAdmin',
          isActive: true,
          isLocked: false,
        },
      ])
    vi.mocked(adminApi.changeEmployeeRole).mockResolvedValue(undefined)
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /promoteme/i })
    await user.selectOptions(within(row).getByLabelText(/change role/i), 'ManagerAdmin')

    expect(adminApi.changeEmployeeRole).toHaveBeenCalledWith(8, 'ManagerAdmin')
    const updatedRow = await screen.findByRole('row', { name: /promoteme/i })
    expect(within(updatedRow).getByText('ManagerAdmin')).toBeInTheDocument()
  })

  it('shows a clear message and leaves the role unchanged when the change is blocked', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([
      {
        employeeId: 9,
        username: 'lastmanagerrole',
        displayName: 'Last Manager Role',
        role: 'ManagerAdmin',
        isActive: true,
        isLocked: false,
      },
    ])
    vi.mocked(adminApi.changeEmployeeRole).mockRejectedValue(new WouldRemoveLastManagerAdminError())
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /lastmanagerrole/i })
    await user.selectOptions(within(row).getByLabelText(/change role/i), 'Picker')

    expect(await screen.findByRole('alert')).toHaveTextContent(/manager/i)
    expect(
      within(screen.getByRole('row', { name: /lastmanagerrole/i })).getByText('ManagerAdmin'),
    ).toBeInTheDocument()
  })

  it("resets an employee's PIN and shows a success message", async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([
      {
        employeeId: 10,
        username: 'pinuser',
        displayName: 'Pin User',
        role: 'Picker',
        isActive: true,
        isLocked: false,
      },
    ])
    vi.mocked(adminApi.resetPin).mockResolvedValue(undefined)
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /pinuser/i })
    await user.type(within(row).getByLabelText(/new pin/i), '4321')
    await user.click(within(row).getByRole('button', { name: /reset pin/i }))

    expect(adminApi.resetPin).toHaveBeenCalledWith(10, '4321')
    expect(await screen.findByText(/pin reset/i)).toBeInTheDocument()
  })

  it('shows an error message when resetting a PIN fails', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([
      {
        employeeId: 13,
        username: 'pinfailuser',
        displayName: 'Pin Fail User',
        role: 'Picker',
        isActive: true,
        isLocked: false,
      },
    ])
    vi.mocked(adminApi.resetPin).mockRejectedValue(new Error('server unavailable'))
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /pinfailuser/i })
    await user.type(within(row).getByLabelText(/new pin/i), '4321')
    await user.click(within(row).getByRole('button', { name: /reset pin/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t reset/i)
  })

  it('shows an Unlock action only for a locked employee and unlocks on click', async () => {
    vi.mocked(adminApi.listEmployees)
      .mockResolvedValueOnce([
        {
          employeeId: 11,
          username: 'lockeduser',
          displayName: 'Locked User',
          role: 'Picker',
          isActive: true,
          isLocked: true,
        },
      ])
      .mockResolvedValueOnce([
        {
          employeeId: 11,
          username: 'lockeduser',
          displayName: 'Locked User',
          role: 'Picker',
          isActive: true,
          isLocked: false,
        },
      ])
    vi.mocked(adminApi.unlockEmployee).mockResolvedValue(undefined)
    const user = userEvent.setup()

    renderPage()
    const row = await screen.findByRole('row', { name: /lockeduser/i })
    await user.click(within(row).getByRole('button', { name: /unlock/i }))

    expect(adminApi.unlockEmployee).toHaveBeenCalledWith(11)
    const updatedRow = await screen.findByRole('row', { name: /lockeduser/i })
    expect(within(updatedRow).queryByRole('button', { name: /unlock/i })).not.toBeInTheDocument()
  })

  it('hides the Unlock action for an employee who is not locked', async () => {
    vi.mocked(adminApi.listEmployees).mockResolvedValue([
      {
        employeeId: 12,
        username: 'normaluser',
        displayName: 'Normal User',
        role: 'Picker',
        isActive: true,
        isLocked: false,
      },
    ])

    renderPage()
    const row = await screen.findByRole('row', { name: /normaluser/i })
    expect(within(row).queryByRole('button', { name: /unlock/i })).not.toBeInTheDocument()
  })
})
