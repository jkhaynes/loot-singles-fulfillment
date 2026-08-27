import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OrdersPage } from '../../src/features/orders/OrdersPage'
import * as ordersApi from '../../src/features/orders/ordersApi'
import { OrderAlreadyClaimedError } from '../../src/features/orders/ordersApi'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrders: vi.fn(),
  claimOrder: vi.fn(),
}))

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/orders']}>
      <Routes>
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/orders/:orderId" element={<p>Order detail page</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('OrdersPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('renders every returned order with identifier, actual status, and import time', async () => {
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      {
        orderId: 1,
        tcgplayerOrderId: 'ORDER-100',
        status: 'ready',
        importedAt: '2026-08-22T15:00:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
      },
      {
        orderId: 2,
        tcgplayerOrderId: 'ORDER-200',
        status: 'picked',
        importedAt: '2026-08-21T14:30:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
      },
    ])

    renderPage()

    const firstOrder = await screen.findByRole('article', { name: /ORDER-100/i })
    expect(within(firstOrder).getByText('ready')).toBeInTheDocument()
    expect(within(firstOrder).getByText(/Aug/)).toBeInTheDocument()
    const secondOrder = screen.getByRole('article', { name: /ORDER-200/i })
    expect(within(secondOrder).getByText('picked')).toBeInTheDocument()
  })

  it('links each order to its detail route', async () => {
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      {
        orderId: 17,
        tcgplayerOrderId: 'ORDER-100',
        status: 'ready',
        importedAt: '2026-08-22T15:00:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
      },
      {
        orderId: 29,
        tcgplayerOrderId: 'ORDER-200',
        status: 'ready',
        importedAt: '2026-08-21T14:30:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
      },
    ])

    renderPage()

    expect(await screen.findByRole('link', { name: 'ORDER-100' })).toHaveAttribute(
      'href',
      '/orders/17',
    )
    expect(screen.getByRole('link', { name: 'ORDER-200' })).toHaveAttribute('href', '/orders/29')
  })

  it('shows a clear empty state', async () => {
    vi.mocked(ordersApi.getOrders).mockResolvedValue([])

    renderPage()

    expect(await screen.findByText(/no imported orders/i)).toBeInTheDocument()
  })

  it('shows a distinct error state when loading fails', async () => {
    vi.mocked(ordersApi.getOrders).mockRejectedValue(new Error('server unavailable'))

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t load imported orders/i)
    expect(screen.queryByText(/no imported orders/i)).not.toBeInTheDocument()
  })

  it('shows a Claim action for an unclaimed order and navigates to it on success', async () => {
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      {
        orderId: 5,
        tcgplayerOrderId: 'ORDER-CLAIMABLE',
        status: 'ready',
        importedAt: '2026-08-22T15:00:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
      },
    ])
    vi.mocked(ordersApi.claimOrder).mockResolvedValue({
      orderId: 5,
      tcgplayerOrderId: 'ORDER-CLAIMABLE',
      status: 'inProgress',
      claimedByEmployeeId: 1,
      claimedByEmployeeName: 'Test Picker',
    })
    const user = userEvent.setup()

    renderPage()
    const order = await screen.findByRole('article', { name: /ORDER-CLAIMABLE/i })
    await user.click(within(order).getByRole('button', { name: /claim/i }))

    expect(ordersApi.claimOrder).toHaveBeenCalledWith(5)
    expect(await screen.findByText('Order detail page')).toBeInTheDocument()
  })

  it('hides the Claim action for an already-claimed order', async () => {
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      {
        orderId: 6,
        tcgplayerOrderId: 'ORDER-ALREADY-CLAIMED',
        status: 'inProgress',
        importedAt: '2026-08-22T15:00:00Z',
        claimedByEmployeeId: 9,
        claimedByEmployeeName: 'Someone Else',
      },
    ])

    renderPage()

    const order = await screen.findByRole('article', { name: /ORDER-ALREADY-CLAIMED/i })
    expect(within(order).queryByRole('button', { name: /claim/i })).not.toBeInTheDocument()
  })

  it('shows an error message when claiming fails because it is already claimed', async () => {
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      {
        orderId: 7,
        tcgplayerOrderId: 'ORDER-RACE-LOSS',
        status: 'ready',
        importedAt: '2026-08-22T15:00:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
      },
    ])
    vi.mocked(ordersApi.claimOrder).mockRejectedValue(new OrderAlreadyClaimedError('Sam'))
    const user = userEvent.setup()

    renderPage()
    const order = await screen.findByRole('article', { name: /ORDER-RACE-LOSS/i })
    await user.click(within(order).getByRole('button', { name: /claim/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/already claimed by Sam/i)
  })
})
