import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { DashboardPage } from '../../src/features/dashboard/DashboardPage'
import * as dashboardApi from '../../src/features/dashboard/dashboardApi'
import * as ordersApi from '../../src/features/orders/ordersApi'
import { NoOrdersAvailableError } from '../../src/features/orders/ordersApi'

vi.mock('../../src/features/dashboard/dashboardApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/features/dashboard/dashboardApi')>()
  return { ...actual, getDashboard: vi.fn() }
})

vi.mock('../../src/features/orders/ordersApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/features/orders/ordersApi')>()
  return { ...actual, pickNextOrder: vi.fn() }
})

const employee = { employeeId: 1, displayName: 'Jamie', role: 'Picker' }

/** Fills in whichever sections a test doesn't care about (015-pick-completion). */
function dashboardData(sections: Partial<dashboardApi.DashboardData>): dashboardApi.DashboardData {
  return {
    ready: { count: 0, orders: [] },
    inProgress: { count: 0, orders: [] },
    needsAttention: { count: 0, orders: [] },
    picked: { count: 0, orders: [] },
    ...sections,
  }
}

function renderDashboard(onLogout = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<DashboardPage employee={employee} onLogout={onLogout} />} />
        <Route path="/orders/:orderId" element={<p>Order detail page</p>} />
        <Route path="/packing" element={<p>Packing desk page</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('renders Ready orders from live data', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({
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
      }),
    )

    renderDashboard()

    const row = await screen.findByRole('row', { name: /F0000001-ABC001-00001/ })
    expect(within(row).getByText('2')).toBeInTheDocument()
    expect(within(row).getByText('5')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument() // Ready stat tile count
  })

  // 015-pick-completion T039: the three placeholder tiles now show live counts (US3 AC1, AC2).
  // 017: the last tile counts orders awaiting packing rather than orders ever picked. The
  // old number only grew; this one falls when a sleeve is posted, which is the question anyone
  // actually has (FR-036).
  it('shows live counts for In Progress, Needs Attention and Awaiting Packing', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({
        ready: { count: 1, orders: [] },
        inProgress: { count: 2, orders: [] },
        needsAttention: {
          count: 1,
          orders: [
            {
              orderId: 77,
              tcgplayerOrderId: 'FLAGGED-ORDER',
              productCount: 3,
              totalQuantity: 4,
              flaggedProductNames: ['Lightning Bolt (Foil)'],
            },
          ],
        },
        picked: { count: 5, orders: [] },
      }),
    )

    renderDashboard()

    const inProgressTile = await screen.findByRole('article', { name: 'In Progress' })
    expect(within(inProgressTile).getByText('2')).toBeInTheDocument()
    const needsAttentionTile = screen.getByRole('article', { name: 'Needs Attention' })
    expect(within(needsAttentionTile).getByText('1')).toBeInTheDocument()
    const pickedTile = screen.getByRole('article', { name: 'Awaiting Packing' })
    expect(within(pickedTile).getByText('5')).toBeInTheDocument()
    expect(screen.queryByText('Not yet available')).not.toBeInTheDocument()
  })

  // 017 T113 (Product Owner decision, 2026-09-21): nothing linked to the packing desk, so a packer
  // had no way in short of scanning a label or knowing the address. The tile that counts the
  // sleeves waiting for them is where they look.
  it('opens the packing desk from the Awaiting Packing tile', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 0, orders: [] }, picked: { count: 3, orders: [] } }),
    )
    const user = userEvent.setup()

    renderDashboard()
    const tile = await screen.findByRole('article', { name: 'Awaiting Packing' })
    expect(within(tile).getByText('3')).toBeInTheDocument()

    await user.click(within(tile).getByRole('link', { name: /awaiting packing/i }))

    expect(await screen.findByText('Packing desk page')).toBeInTheDocument()
  })

  it('names the flagged product on a needs-attention order', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({
        needsAttention: {
          count: 1,
          orders: [
            {
              orderId: 77,
              tcgplayerOrderId: 'FLAGGED-ORDER',
              productCount: 3,
              totalQuantity: 4,
              flaggedProductNames: ['Lightning Bolt (Foil)', 'Black Lotus'],
            },
          ],
        },
      }),
    )

    renderDashboard()

    const tile = await screen.findByRole('article', { name: 'Needs Attention' })
    expect(within(tile).getByText(/FLAGGED-ORDER/)).toBeInTheDocument()
    expect(within(tile).getByText(/Lightning Bolt \(Foil\)/)).toBeInTheDocument()
    expect(within(tile).getByText(/Black Lotus/)).toBeInTheDocument()
  })

  it('links each available order to its detail route', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({
        ready: {
          count: 2,
          orders: [
            {
              orderId: 42,
              tcgplayerOrderId: 'F0000001-ABC001-00001',
              productCount: 2,
              totalQuantity: 5,
            },
            {
              orderId: 77,
              tcgplayerOrderId: 'F0000002-ABC002-00002',
              productCount: 1,
              totalQuantity: 1,
            },
          ],
        },
      }),
    )

    renderDashboard()

    expect(await screen.findByRole('link', { name: 'F0000001-ABC001-00001' })).toHaveAttribute(
      'href',
      '/orders/42',
    )
    expect(screen.getByRole('link', { name: 'F0000002-ABC002-00002' })).toHaveAttribute(
      'href',
      '/orders/77',
    )
  })

  it('visually emphasizes an order row whose total quantity is greater than one', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({
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
      }),
    )

    renderDashboard()

    const row = await screen.findByRole('row', { name: /F0000001-ABC001-00001/ })
    const [, , quantityCell] = within(row).getAllByRole('cell')
    expect(quantityCell).toHaveAttribute('data-emphasis', 'high')
  })

  it('does not emphasize an order row whose total quantity is exactly one', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({
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
      }),
    )

    renderDashboard()

    const row = await screen.findByRole('row', { name: /F0000002-ABC002-00002/ })
    const [, , quantityCell] = within(row).getAllByRole('cell')
    expect(quantityCell).not.toHaveAttribute('data-emphasis', 'high')
  })

  it('shows a distinct error message, not the empty-state message, when the dashboard fails to load', async () => {
    vi.mocked(dashboardApi.getDashboard).mockRejectedValue(
      new Error('Failed to load dashboard (status 500)'),
    )

    renderDashboard()

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t load/i)
    expect(screen.queryByText(/no orders ready to pick/i)).not.toBeInTheDocument()
    expect(screen.getByText('Ready to Pick').nextElementSibling).toHaveTextContent('—')
  })

  it('shows an empty-state message when there are no Ready orders', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 0, orders: [] } }),
    )

    renderDashboard()

    expect(await screen.findByText(/no orders ready to pick/i)).toBeInTheDocument()
  })

  // 015-pick-completion: the three sections carry live data now; their placeholder assertion was
  // replaced by the live-count tests above.
  it('renders all four sections with zero counts when nothing is in flight', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 0, orders: [] } }),
    )

    renderDashboard()
    await screen.findByText(/no orders ready to pick/i)

    for (const label of ['Ready to Pick', 'In Progress', 'Needs Attention', 'Awaiting Packing']) {
      const tile = screen.getByRole('article', { name: label })
      expect(within(tile).getByText('0')).toBeInTheDocument()
    }
    expect(screen.queryByText(/not yet available/i)).not.toBeInTheDocument()
  })

  it('invokes logout when the logout control is activated', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 0, orders: [] } }),
    )
    const onLogout = vi.fn()
    const user = userEvent.setup()

    renderDashboard(onLogout)
    await screen.findByText(/no orders ready to pick/i)
    await user.click(screen.getByRole('button', { name: /log out/i }))

    expect(onLogout).toHaveBeenCalledOnce()
  })

  it('provides a prominent Import Orders action targeting the import screen', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 0, orders: [] } }),
    )

    renderDashboard()
    await screen.findByText(/no orders ready to pick/i)

    expect(screen.getByRole('link', { name: /import orders/i })).toHaveAttribute('href', '/import')
  })

  it('claims and navigates to the assigned order when Pick Next Order succeeds', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 1, orders: [] } }),
    )
    vi.mocked(ordersApi.pickNextOrder).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'PICKED-ORDER',
      status: 'inProgress',
      claimedByEmployeeId: 1,
      claimedByEmployeeName: 'Jamie',
    })
    const user = userEvent.setup()

    renderDashboard()
    await user.click(await screen.findByRole('button', { name: /pick next order/i }))

    expect(await screen.findByText('Order detail page')).toBeInTheDocument()
  })

  it('shows a clear message when no orders are available to pick', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ ready: { count: 0, orders: [] } }),
    )
    vi.mocked(ordersApi.pickNextOrder).mockRejectedValue(new NoOrdersAvailableError())
    const user = userEvent.setup()

    renderDashboard()
    await user.click(await screen.findByRole('button', { name: /pick next order/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/no orders are currently available/i)
  })
})

// 016-mobile-picking T039 (FR-026): an employee who already holds an order is offered it back,
// rather than offered a fresh one and then told they cannot have it.
describe('DashboardPage — resuming a held order', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  const heldOrder = {
    orderId: 42,
    tcgplayerOrderId: 'F8433182-HELD-00042',
    productCount: 5,
    totalQuantity: 8,
  }

  it('offers to resume the held order', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ activeClaim: heldOrder }),
    )

    renderDashboard()

    const resume = await screen.findByRole('link', { name: /resume/i })
    expect(resume).toHaveAttribute('href', '/orders/42')
    expect(resume).toHaveTextContent(/F8433182-HELD-00042/)
  })

  it('does not offer to start another order while one is held', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ activeClaim: heldOrder }),
    )

    renderDashboard()

    await screen.findByRole('link', { name: /resume/i })
    // Offering Pick Next here produces a request that is known to fail (FR-027).
    expect(screen.queryByRole('button', { name: /pick next order/i })).not.toBeInTheDocument()
  })

  it('offers Pick Next when no order is held', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(dashboardData({ activeClaim: null }))

    renderDashboard()

    expect(await screen.findByRole('button', { name: /pick next order/i })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /resume/i })).not.toBeInTheDocument()
  })

  it('says what the held order contains, so resuming is an informed choice', async () => {
    vi.mocked(dashboardApi.getDashboard).mockResolvedValue(
      dashboardData({ activeClaim: heldOrder }),
    )

    renderDashboard()

    const resume = await screen.findByRole('link', { name: /resume/i })
    expect(resume).toHaveTextContent(/5 products/i)
    expect(resume).toHaveTextContent(/8 cards/i)
  })
})
