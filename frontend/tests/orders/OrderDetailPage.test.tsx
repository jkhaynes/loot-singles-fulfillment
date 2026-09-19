import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OrderDetailPage } from '../../src/features/orders/OrderDetailPage'
import * as ordersApi from '../../src/features/orders/ordersApi'
import { AuthProvider } from '../../src/features/auth/AuthContext'
import * as authApi from '../../src/features/auth/authApi'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrderDetail: vi.fn(),
  recordPicked: vi.fn(),
  reportIssue: vi.fn(),
}))

vi.mock('../../src/features/auth/authApi', async (original) => ({
  ...(await original<typeof import('../../src/features/auth/authApi')>()),
  me: vi.fn(),
}))

function line(
  id: number,
  productName: string,
  pickOutcome: ordersApi.PickOutcome | null,
): ordersApi.OrderLineDetail {
  return {
    id,
    pickOutcome,
    productName,
    productLine: 'Pokemon',
    set: 'Base Set',
    collectorNumber: `#${id}`,
    rarity: null,
    variant: null,
    condition: 'Near Mint',
    quantity: 1,
    imageUrl: null,
    currentIssue: null,
  }
}

function claimedOrder(lines: ordersApi.OrderLineDetail[]): ordersApi.OrderDetail {
  return {
    orderId: 42,
    tcgplayerOrderId: 'ORDER-DETAIL-42',
    status: 'inProgress',
    lines,
    claimedByEmployeeId: 1,
    claimedByEmployeeName: 'Test Picker',
  }
}

function renderPage(orderId = 42) {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[`/orders/${orderId}`]}>
        <Routes>
          <Route path="/orders/:orderId" element={<OrderDetailPage />} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  )
}

describe('OrderDetailPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 1,
      displayName: 'Test Picker',
      role: 'Picker',
    })
  })

  it('renders the order identifier and every line field, omitting a missing variant', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      lines: [
        {
          productName: 'Genesect ex',
          productLine: 'Pokemon',
          set: 'SV: Black Bolt',
          collectorNumber: '#067/086',
          rarity: 'Double Rare',
          variant: 'Holofoil',
          condition: 'Near Mint',
          quantity: 3,
          imageUrl: null,
        },
        {
          productName: 'Pikachu',
          productLine: 'Pokemon',
          set: 'Base Set',
          collectorNumber: '#025/102',
          rarity: null,
          variant: null,
          condition: 'Lightly Played',
          quantity: 1,
          imageUrl: null,
        },
      ],
    })

    renderPage()

    expect(await screen.findByRole('heading', { name: /ORDER-DETAIL-42/i })).toBeInTheDocument()
    const genesect = screen.getByRole('article', { name: /Genesect ex/i })
    expect(within(genesect).getByText('Pokemon')).toBeInTheDocument()
    expect(within(genesect).getByText('SV: Black Bolt')).toBeInTheDocument()
    expect(within(genesect).getByText('#067/086')).toBeInTheDocument()
    expect(within(genesect).getByText('Double Rare')).toBeInTheDocument()
    expect(within(genesect).getByText('Holofoil')).toBeInTheDocument()
    expect(within(genesect).getByText('Near Mint')).toBeInTheDocument()
    expect(within(genesect).getByText('3')).toBeInTheDocument()

    const pikachu = screen.getByRole('article', { name: /Pikachu/i })
    expect(within(pikachu).getByText('Pokemon')).toBeInTheDocument()
    expect(within(pikachu).getByText('Base Set')).toBeInTheDocument()
    expect(within(pikachu).getByText('#025/102')).toBeInTheDocument()
    expect(within(pikachu).getByText('Lightly Played')).toBeInTheDocument()
    expect(within(pikachu).getByText('1')).toBeInTheDocument()
    expect(within(pikachu).queryByText(/variant/i)).not.toBeInTheDocument()
    expect(within(pikachu).queryByText(/rarity/i)).not.toBeInTheDocument()
  })

  it('emphasizes a multi-quantity line without repeating its quantity', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      lines: [
        {
          productName: 'Genesect ex',
          productLine: 'Pokemon',
          set: 'SV: Black Bolt',
          collectorNumber: '#067/086',
          rarity: 'Double Rare',
          variant: 'Holofoil',
          condition: 'Near Mint',
          quantity: 4,
          imageUrl: null,
        },
        {
          productName: 'Pikachu',
          productLine: 'Pokemon',
          set: 'Base Set',
          collectorNumber: '#025/102',
          rarity: null,
          variant: null,
          condition: 'Near Mint',
          quantity: 1,
          imageUrl: null,
        },
      ],
    })

    renderPage()

    const genesect = await screen.findByRole('article', { name: /Genesect ex/i })
    const emphasizedQuantity = within(genesect).getByText('4')
    expect(emphasizedQuantity).toHaveAttribute('data-emphasis', 'high')
    expect(within(genesect).getAllByText('4')).toHaveLength(1)

    const pikachu = screen.getByRole('article', { name: /Pikachu/i })
    expect(within(pikachu).getByText('1')).not.toHaveAttribute('data-emphasis')
    expect(within(pikachu).queryByText('×1')).not.toBeInTheDocument()
  })

  it('renders the order status alongside its identifier', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      lines: [
        {
          productName: 'Pikachu',
          productLine: 'Pokemon',
          set: 'Base Set',
          collectorNumber: '#025/102',
          rarity: null,
          variant: null,
          condition: 'Lightly Played',
          quantity: 1,
          imageUrl: null,
        },
      ],
    })

    renderPage()

    const heading = await screen.findByRole('heading', { name: /ORDER-DETAIL-42/i })
    const header = heading.closest('header')
    expect(header).not.toBeNull()
    expect(within(header as HTMLElement).getByText('Ready')).toBeInTheDocument()
    expect(screen.getByLabelText('Order status: Ready')).toBeInTheDocument()
  })

  // 015-pick-completion T017: per-line Picked action and position indicator (US1 AC1, AC3).
  it('shows how many lines are confirmed and records a pick for the claim holder', async () => {
    const order = claimedOrder([
      line(1, 'Genesect ex', 'picked'),
      line(2, 'Pikachu', null),
      line(3, 'Charizard', null),
    ])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.recordPicked).mockResolvedValue({
      ...order,
      lines: [order.lines[0], { ...order.lines[1], pickOutcome: 'picked' }, order.lines[2]],
    })

    renderPage()

    expect(await screen.findByText('1 of 3 lines confirmed')).toBeInTheDocument()
    const pikachu = screen.getByRole('article', { name: /Pikachu/i })
    await userEvent.click(within(pikachu).getByRole('button', { name: 'Picked' }))

    expect(ordersApi.recordPicked).toHaveBeenCalledWith(42, 2)
    expect(await screen.findByText('2 of 3 lines confirmed')).toBeInTheDocument()
    expect(within(pikachu).getByRole('button', { name: 'Picked' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('shows Picked in the header once the last line is confirmed', async () => {
    const order = claimedOrder([line(1, 'Pikachu', null)])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.recordPicked).mockResolvedValue({
      ...order,
      status: 'picked',
      lines: [{ ...order.lines[0], pickOutcome: 'picked' }],
    })

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Picked' }))

    expect(await screen.findByLabelText('Order status: Picked')).toBeInTheDocument()
    expect(screen.queryByText(/In Progress/)).not.toBeInTheDocument()
  })

  it('does not offer the Picked action to someone who does not hold the claim', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      ...claimedOrder([line(1, 'Pikachu', null)]),
      claimedByEmployeeId: 99,
      claimedByEmployeeName: 'Someone Else',
    })

    renderPage()

    await screen.findByRole('article', { name: /Pikachu/i })
    expect(screen.queryByRole('button', { name: 'Picked' })).not.toBeInTheDocument()
  })

  // 015-pick-completion T018: a confirmed line stays revisable while the claim is held (US1 AC4).
  it('lets the claim holder re-record an already-picked line', async () => {
    const order = claimedOrder([line(1, 'Pikachu', 'picked'), line(2, 'Charizard', null)])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.recordPicked).mockResolvedValue(order)

    renderPage()

    const pikachu = await screen.findByRole('article', { name: /Pikachu/i })
    const button = within(pikachu).getByRole('button', { name: 'Picked' })
    expect(button).toBeEnabled()
    await userEvent.click(button)

    expect(ordersApi.recordPicked).toHaveBeenCalledWith(42, 1)
  })

  it('shows an error and keeps the line unconfirmed when recording a pick fails', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder([line(1, 'Pikachu', null)]))
    vi.mocked(ordersApi.recordPicked).mockRejectedValue(new ordersApi.NotYourClaimError())

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Picked' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t record/i)
    expect(screen.getByText('0 of 1 lines confirmed')).toBeInTheDocument()
  })

  // 015-pick-completion T030: inline report-issue form per line (US2 AC1).
  it('reports an issue from an inline form and shows the order needing attention', async () => {
    const order = claimedOrder([line(1, 'Pikachu', null), line(2, 'Charizard', null)])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.reportIssue).mockResolvedValue({
      ...order,
      status: 'needsAttention',
      lines: [
        {
          ...order.lines[0],
          pickOutcome: 'hasIssue',
          currentIssue: {
            issueType: 'insufficientQuantity',
            requiredQuantity: 3,
            foundQuantity: 1,
            note: 'Only one left',
            reportedByEmployeeName: 'Test Picker',
            reportedAt: '2026-09-19T12:00:00Z',
          },
        },
        order.lines[1],
      ],
    })

    renderPage()

    const pikachu = await screen.findByRole('article', { name: /Pikachu/i })
    await userEvent.click(within(pikachu).getByRole('button', { name: 'Report Issue' }))

    await userEvent.selectOptions(
      within(pikachu).getByLabelText('Issue type'),
      'insufficientQuantity',
    )
    await userEvent.type(within(pikachu).getByLabelText('Quantity required'), '3')
    await userEvent.type(within(pikachu).getByLabelText('Quantity found'), '1')
    await userEvent.type(within(pikachu).getByLabelText('Note (optional)'), 'Only one left')
    await userEvent.click(within(pikachu).getByRole('button', { name: 'Submit Issue' }))

    expect(ordersApi.reportIssue).toHaveBeenCalledWith(42, 1, {
      issueType: 'insufficientQuantity',
      requiredQuantity: 3,
      foundQuantity: 1,
      note: 'Only one left',
    })
    expect(await screen.findByLabelText(/Order status: Needs Attention/)).toBeInTheDocument()
    expect(within(pikachu).getByText(/Insufficient Quantity/)).toBeInTheDocument()
    expect(within(pikachu).getByText(/Only one left/)).toBeInTheDocument()
  })

  it('sends no quantities or note when the picker leaves them blank', async () => {
    const order = claimedOrder([line(1, 'Pikachu', null)])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.reportIssue).mockResolvedValue(order)

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Report Issue' }))
    await userEvent.selectOptions(screen.getByLabelText('Issue type'), 'cardNotFound')
    await userEvent.click(screen.getByRole('button', { name: 'Submit Issue' }))

    expect(ordersApi.reportIssue).toHaveBeenCalledWith(42, 1, {
      issueType: 'cardNotFound',
      requiredQuantity: null,
      foundQuantity: null,
      note: null,
    })
  })

  it('closes the issue form without reporting when cancelled', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder([line(1, 'Pikachu', null)]))

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Report Issue' }))
    expect(screen.getByLabelText('Issue type')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByLabelText('Issue type')).not.toBeInTheDocument()
    expect(ordersApi.reportIssue).not.toHaveBeenCalled()
  })

  it('does not offer the Report Issue action to someone who does not hold the claim', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      ...claimedOrder([line(1, 'Pikachu', null)]),
      claimedByEmployeeId: 99,
      claimedByEmployeeName: 'Someone Else',
    })

    renderPage()

    await screen.findByRole('article', { name: /Pikachu/i })
    expect(screen.queryByRole('button', { name: 'Report Issue' })).not.toBeInTheDocument()
  })

  it('shows a distinct not-found state', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockRejectedValue(new ordersApi.OrderNotFoundError())

    renderPage(999)

    expect(await screen.findByRole('alert')).toHaveTextContent(/order not found/i)
    expect(screen.queryByText(/couldn.?t load order/i)).not.toBeInTheDocument()
  })

  it('uses the same neutral placeholder for every line and never renders a sourced image', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      lines: [
        {
          productName: 'Genesect ex',
          productLine: 'Pokemon',
          set: 'SV: Black Bolt',
          collectorNumber: '#067/086',
          rarity: 'Double Rare',
          variant: 'Holofoil',
          condition: 'Near Mint',
          quantity: 1,
          imageUrl: null,
        },
        {
          productName: 'Pikachu',
          productLine: 'Pokemon',
          set: 'Base Set',
          collectorNumber: '#025/102',
          rarity: null,
          variant: null,
          condition: 'Lightly Played',
          quantity: 1,
          imageUrl: null,
        },
      ],
    })

    const { container } = renderPage()

    const lines = await screen.findAllByRole('article')
    expect(lines).toHaveLength(2)
    for (const line of lines) {
      const placeholder = within(line).getByLabelText('Card image unavailable')
      expect(placeholder).toHaveTextContent('No image')
    }
    expect(screen.getAllByLabelText('Card image unavailable')).toHaveLength(2)
    expect(container.querySelectorAll('img[src]')).toHaveLength(0)
  })

  it('renders a real image when imageUrl is present, and the placeholder when it is null', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      lines: [
        {
          productName: 'Genesect ex',
          productLine: 'Pokemon',
          set: 'SV: Black Bolt',
          collectorNumber: '#067/086',
          rarity: 'Double Rare',
          variant: 'Holofoil',
          condition: 'Near Mint',
          quantity: 1,
          imageUrl: 'https://example.com/genesect-ex.png',
        },
        {
          productName: 'Pikachu',
          productLine: 'Pokemon',
          set: 'Base Set',
          collectorNumber: '#025/102',
          rarity: null,
          variant: null,
          condition: 'Lightly Played',
          quantity: 1,
          imageUrl: null,
        },
      ],
    })

    renderPage()

    const genesect = await screen.findByRole('article', { name: /Genesect ex/i })
    const image = within(genesect).getByRole('img', { name: /Genesect ex/i })
    expect(image).toHaveAttribute('src', 'https://example.com/genesect-ex.png')
    expect(within(genesect).queryByLabelText('Card image unavailable')).not.toBeInTheDocument()

    const pikachu = screen.getByRole('article', { name: /Pikachu/i })
    expect(within(pikachu).getByLabelText('Card image unavailable')).toBeInTheDocument()
    expect(within(pikachu).queryByRole('img')).not.toBeInTheDocument()
  })
})
