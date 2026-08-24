import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OrderDetailPage } from '../../src/features/orders/OrderDetailPage'
import * as ordersApi from '../../src/features/orders/ordersApi'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrderDetail: vi.fn(),
}))

function renderPage(orderId = 42) {
  return render(
    <MemoryRouter initialEntries={[`/orders/${orderId}`]}>
      <Routes>
        <Route path="/orders/:orderId" element={<OrderDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('OrderDetailPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('renders the order identifier and every line field, omitting a missing variant', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      lines: [
        {
          productName: 'Genesect ex',
          set: 'SV: Black Bolt',
          variant: 'Holofoil',
          condition: 'Near Mint',
          quantity: 3,
        },
        {
          productName: 'Pikachu',
          set: 'Base Set',
          variant: null,
          condition: 'Lightly Played',
          quantity: 1,
        },
      ],
    })

    renderPage()

    expect(await screen.findByRole('heading', { name: /ORDER-DETAIL-42/i })).toBeInTheDocument()
    const genesect = screen.getByRole('article', { name: /Genesect ex/i })
    expect(within(genesect).getByText('SV: Black Bolt')).toBeInTheDocument()
    expect(within(genesect).getByText('Holofoil')).toBeInTheDocument()
    expect(within(genesect).getByText('Near Mint')).toBeInTheDocument()
    expect(within(genesect).getByText('3')).toBeInTheDocument()

    const pikachu = screen.getByRole('article', { name: /Pikachu/i })
    expect(within(pikachu).getByText('Base Set')).toBeInTheDocument()
    expect(within(pikachu).getByText('Lightly Played')).toBeInTheDocument()
    expect(within(pikachu).getByText('1')).toBeInTheDocument()
    expect(within(pikachu).queryByText(/variant/i)).not.toBeInTheDocument()
  })

  it('emphasizes multi-quantity lines with color styling and a text marker', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      lines: [
        {
          productName: 'Genesect ex',
          set: 'SV: Black Bolt',
          variant: 'Holofoil',
          condition: 'Near Mint',
          quantity: 4,
        },
        {
          productName: 'Pikachu',
          set: 'Base Set',
          variant: null,
          condition: 'Near Mint',
          quantity: 1,
        },
      ],
    })

    renderPage()

    const genesect = await screen.findByRole('article', { name: /Genesect ex/i })
    const emphasizedQuantity = within(genesect).getByText('×4')
    expect(emphasizedQuantity).toHaveAttribute('data-emphasis', 'high')

    const pikachu = screen.getByRole('article', { name: /Pikachu/i })
    expect(within(pikachu).getByText('1')).not.toHaveAttribute('data-emphasis')
    expect(within(pikachu).queryByText('×1')).not.toBeInTheDocument()
  })

  it('shows a distinct not-found state', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockRejectedValue(new ordersApi.OrderNotFoundError())

    renderPage(999)

    expect(await screen.findByRole('alert')).toHaveTextContent(/order not found/i)
    expect(screen.queryByText(/couldn.?t load order/i)).not.toBeInTheDocument()
  })
})
