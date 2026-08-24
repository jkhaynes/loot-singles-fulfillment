import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OrdersPage } from '../../src/features/orders/OrdersPage'
import * as ordersApi from '../../src/features/orders/ordersApi'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrders: vi.fn(),
}))

function renderPage() {
  return render(
    <MemoryRouter>
      <OrdersPage />
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
      },
      {
        orderId: 2,
        tcgplayerOrderId: 'ORDER-200',
        status: 'picked',
        importedAt: '2026-08-21T14:30:00Z',
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
      },
      {
        orderId: 29,
        tcgplayerOrderId: 'ORDER-200',
        status: 'ready',
        importedAt: '2026-08-21T14:30:00Z',
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
})
