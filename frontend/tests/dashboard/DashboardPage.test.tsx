import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DashboardPage } from '../../src/features/dashboard/DashboardPage'
import * as dashboardApi from '../../src/features/dashboard/dashboardApi'

vi.mock('../../src/features/dashboard/dashboardApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/features/dashboard/dashboardApi')>()
  return { ...actual, getDashboard: vi.fn() }
})

const employee = { employeeId: 1, displayName: 'Jamie', role: 'Picker' }

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('renders Ready orders from live data', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue({
      ready: {
        count: 1,
        orders: [
          {
            orderId: 42,
            tcgplayerOrderId: 'F0000001-ABC001-00001',
            productCount: 2,
            totalQuantity: 5,
          },
        ],
      },
    })

    render(<DashboardPage employee={employee} onLogout={vi.fn()} />)

    const row = await screen.findByRole('row', { name: /F0000001-ABC001-00001/ })
    expect(within(row).getByText('2')).toBeInTheDocument()
    expect(within(row).getByText('5')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument() // Ready stat tile count
  })

  it('visually emphasizes an order row whose total quantity is greater than one', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue({
      ready: {
        count: 1,
        orders: [
          {
            orderId: 42,
            tcgplayerOrderId: 'F0000001-ABC001-00001',
            productCount: 2,
            totalQuantity: 5,
          },
        ],
      },
    })

    render(<DashboardPage employee={employee} onLogout={vi.fn()} />)

    const row = await screen.findByRole('row', { name: /F0000001-ABC001-00001/ })
    const [, , quantityCell] = within(row).getAllByRole('cell')
    expect(quantityCell).toHaveAttribute('data-emphasis', 'high')
  })

  it('does not emphasize an order row whose total quantity is exactly one', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue({
      ready: {
        count: 1,
        orders: [
          {
            orderId: 43,
            tcgplayerOrderId: 'F0000002-ABC002-00002',
            productCount: 1,
            totalQuantity: 1,
          },
        ],
      },
    })

    render(<DashboardPage employee={employee} onLogout={vi.fn()} />)

    const row = await screen.findByRole('row', { name: /F0000002-ABC002-00002/ })
    const [, , quantityCell] = within(row).getAllByRole('cell')
    expect(quantityCell).not.toHaveAttribute('data-emphasis', 'high')
  })

  it('shows a distinct error message, not the empty-state message, when the dashboard fails to load', async () => {
    vi.mocked(dashboardApi.getDashboard).mockRejectedValue(
      new Error('Failed to load dashboard (status 500)'),
    )

    render(<DashboardPage employee={employee} onLogout={vi.fn()} />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t load/i)
    expect(screen.queryByText(/no orders ready to pick/i)).not.toBeInTheDocument()
    expect(screen.getByText('Ready to Pick').nextElementSibling).toHaveTextContent('—')
  })

  it('shows an empty-state message when there are no Ready orders', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue({ ready: { count: 0, orders: [] } })

    render(<DashboardPage employee={employee} onLogout={vi.fn()} />)

    expect(await screen.findByText(/no orders ready to pick/i)).toBeInTheDocument()
  })

  it('renders the three not-yet-available placeholder sections', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue({ ready: { count: 0, orders: [] } })

    render(<DashboardPage employee={employee} onLogout={vi.fn()} />)
    await screen.findByText(/no orders ready to pick/i)

    expect(screen.getByText('In Progress')).toBeInTheDocument()
    expect(screen.getByText('Needs Attention')).toBeInTheDocument()
    expect(screen.getByText('Picked')).toBeInTheDocument()
    expect(screen.getAllByText(/not yet available/i)).toHaveLength(3)
  })

  it('invokes logout when the logout control is activated', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue({ ready: { count: 0, orders: [] } })
    const onLogout = vi.fn()
    const user = userEvent.setup()

    render(<DashboardPage employee={employee} onLogout={onLogout} />)
    await screen.findByText(/no orders ready to pick/i)
    await user.click(screen.getByRole('button', { name: /log out/i }))

    expect(onLogout).toHaveBeenCalledOnce()
  })
})
