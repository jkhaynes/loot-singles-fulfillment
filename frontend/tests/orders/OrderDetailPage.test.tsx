import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { OrderDetailPage } from '../../src/features/orders/OrderDetailPage'
import * as ordersApi from '../../src/features/orders/ordersApi'
import { AuthProvider } from '../../src/features/auth/AuthContext'
import * as authApi from '../../src/features/auth/authApi'
import { buildLine, buildMultiGameLines } from '../support/orderBuilders'
import { installMatchMedia } from '../support/matchMedia'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrderDetail: vi.fn(),
  recordPicked: vi.fn(),
  reportIssue: vi.fn(),
  releaseOrder: vi.fn(),
  claimOrder: vi.fn(),
  forceReleaseOrder: vi.fn(),
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
          <Route path="/orders" element={<p>Browse Orders list</p>} />
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

  // 015-pick-completion T050 (PO decision 2026-09-19): releasing returns the picker to the order
  // list so they can pick up the next one without navigating back by hand.
  it('returns the picker to the order list after releasing', async () => {
    const order = claimedOrder([line(1, 'Pikachu', null)])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.releaseOrder).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      claimedByEmployeeId: null,
      claimedByEmployeeName: null,
    })

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Release' }))

    expect(ordersApi.releaseOrder).toHaveBeenCalledWith(42)
    expect(await screen.findByText('Browse Orders list')).toBeInTheDocument()
  })

  it('stays on the order and shows an error when releasing fails', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder([line(1, 'Pikachu', null)]))
    vi.mocked(ordersApi.releaseOrder).mockRejectedValue(new ordersApi.NotYourClaimError())

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Release' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn.?t release/i)
    expect(screen.queryByText('Browse Orders list')).not.toBeInTheDocument()
  })

  it('keeps a manager on the order after a force-release', async () => {
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 2,
      displayName: 'Test Manager',
      role: 'ManagerAdmin',
    })
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder([line(1, 'Pikachu', null)]))
    vi.mocked(ordersApi.forceReleaseOrder).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      claimedByEmployeeId: null,
      claimedByEmployeeName: null,
    })

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Force-Release' }))

    expect(ordersApi.forceReleaseOrder).toHaveBeenCalledWith(42)
    expect(screen.queryByText('Browse Orders list')).not.toBeInTheDocument()
    expect(await screen.findByLabelText('Order status: Ready')).toBeInTheDocument()
  })

  // 015-pick-completion T058 (branch review BR-003): the server no longer re-resolves card images
  // on a write, so the page must keep the ones it already loaded.
  it('keeps each line card image after recording a pick', async () => {
    const withImage = {
      ...line(1, 'Pikachu', null),
      imageUrl: 'https://example.com/pikachu.png',
    }
    const order = claimedOrder([withImage, line(2, 'Charizard', null)])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.recordPicked).mockResolvedValue({
      ...order,
      lines: [
        { ...withImage, pickOutcome: 'picked', imageUrl: null },
        { ...order.lines[1], imageUrl: null },
      ],
    })

    renderPage()

    const pikachu = await screen.findByRole('article', { name: /Pikachu/i })
    await userEvent.click(within(pikachu).getByRole('button', { name: 'Picked' }))

    expect(await screen.findByText('1 of 2 lines confirmed')).toBeInTheDocument()
    expect(within(pikachu).getByRole('img', { name: /Pikachu/i })).toHaveAttribute(
      'src',
      'https://example.com/pikachu.png',
    )
  })

  it('keeps each line card image after reporting an issue', async () => {
    const withImage = {
      ...line(1, 'Pikachu', null),
      imageUrl: 'https://example.com/pikachu.png',
    }
    const order = claimedOrder([withImage])
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(order)
    vi.mocked(ordersApi.reportIssue).mockResolvedValue({
      ...order,
      status: 'needsAttention',
      lines: [
        {
          ...withImage,
          pickOutcome: 'hasIssue',
          imageUrl: null,
          currentIssue: {
            issueType: 'cardNotFound',
            requiredQuantity: null,
            foundQuantity: null,
            note: null,
            reportedByEmployeeName: 'Test Picker',
            reportedAt: '2026-09-20T12:00:00Z',
          },
        },
      ],
    })

    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Report Issue' }))
    await userEvent.selectOptions(screen.getByLabelText('Issue type'), 'cardNotFound')
    await userEvent.click(screen.getByRole('button', { name: 'Submit Issue' }))

    expect(await screen.findByLabelText(/Order status: Needs Attention/)).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Pikachu/i })).toHaveAttribute(
      'src',
      'https://example.com/pikachu.png',
    )
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

  // 016-mobile-picking T009 — set-aware picking in the list view (spec US1, PRD §13).
  describe('set grouping', () => {
    it('renders one header per set, ordered by game then set', async () => {
      vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(buildMultiGameLines()))

      renderPage()

      const groups = await screen.findAllByRole('group')

      // Magic before Pokemon; within each, sets alphabetical.
      expect(groups.map((group) => group.getAttribute('aria-label'))).toEqual([
        'Magic · Aetherdrift',
        'Magic · Bloomburrow',
        'Pokemon · Black Bolt',
        'Pokemon · Surging Sparks',
      ])
    })

    it('shows product and physical card counts per set', async () => {
      vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(buildMultiGameLines()))

      renderPage()

      const blackBolt = await screen.findByRole('group', { name: 'Pokemon · Black Bolt' })

      // One product line, three physical cards — the distinction the picker needs at the box.
      expect(within(blackBolt).getByText(/1 product/i)).toBeInTheDocument()
      expect(within(blackBolt).getByText(/3 cards/i)).toBeInTheDocument()
    })

    it('keeps every line visible, including one with no recorded set', async () => {
      const lines = [
        buildLine({ productLine: 'Pokemon', set: 'Base Set', productName: 'Pikachu ex' }),
        buildLine({ productLine: 'Pokemon', set: '', productName: 'Mystery Promo' }),
      ]
      vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(lines))

      renderPage()

      // Never grouped out of existence (spec FR-005, Constitution V).
      expect(await screen.findByRole('article', { name: /Mystery Promo/i })).toBeInTheDocument()
      expect(screen.getAllByRole('article')).toHaveLength(2)
      expect(screen.getByRole('group', { name: /Set not recorded/i })).toBeInTheDocument()
    })
  })
})

// 016-mobile-picking: the page-level view switch. Nothing rendered the focused view through
// the page before, which is how a `view is not defined` reference survived a passing suite and
// a clean typecheck — the whole component threw the moment a phone-sized screen loaded it.
describe('OrderDetailPage on a phone', () => {
  let restore: (() => void) | null = null

  beforeEach(() => {
    // Without this, mock call counts accumulate across the tests in this block, and a
    // "was never called" assertion silently reads a previous test's calls.
    vi.resetAllMocks()
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 1,
      displayName: 'Test Picker',
      role: 'Picker',
    })
    restore = installMatchMedia(390).restore
  })

  afterEach(() => {
    restore?.()
    restore = null
  })

  it('renders the card view, not the whole order', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(buildMultiGameLines()))

    renderPage()

    // One card, not five.
    expect(await screen.findByRole('article')).toBeInTheDocument()
    expect(screen.getAllByRole('article')).toHaveLength(1)
    expect(screen.queryByRole('group')).not.toBeInTheDocument()
  })

  it('offers a way out to the dashboard on every card', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(buildMultiGameLines()))

    renderPage()
    await screen.findByRole('article')

    // Before this replaced the view toggle, a picker was stuck on the order until the end.
    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument()
  })

  it('offers no view toggle', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(buildMultiGameLines()))

    renderPage()

    await screen.findByRole('article')
    expect(screen.queryByRole('button', { name: /whole order/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /one card at a time/i })).not.toBeInTheDocument()
  })

  it('drops the order title block that pushed the record button below the fold', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(claimedOrder(buildMultiGameLines()))

    renderPage()

    await screen.findByRole('article')
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /browse orders/i })).not.toBeInTheDocument()
  })
})

// 016-mobile-picking T040-T042 (FR-023, FR-024, FR-027): claiming is an explicit act on the
// order, so viewing one is always safe. The endpoint has existed since feature 013; until now
// nothing surfaced it here, so opening an order from the dashboard was a dead end.
describe('OrderDetailPage — claiming', () => {
  beforeEach(() => {
    // Without this, mock call counts accumulate across the tests in this block, and a
    // "was never called" assertion silently reads a previous test's calls.
    vi.resetAllMocks()
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 1,
      displayName: 'Test Picker',
      role: 'Picker',
    })
  })

  function unclaimedOrder(): ordersApi.OrderDetail {
    return {
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'ready',
      lines: [buildLine({ productName: 'Pikachu ex' })],
      claimedByEmployeeId: null,
      claimedByEmployeeName: null,
    }
  }

  it('offers to claim an unclaimed order, and claims nothing by being opened', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(unclaimedOrder())

    renderPage()

    expect(await screen.findByRole('button', { name: /^claim/i })).toBeInTheDocument()
    // Viewing is safe (FR-023).
    expect(ordersApi.claimOrder).not.toHaveBeenCalled()
  })

  it('claims the order and makes picking available', async () => {
    const user = userEvent.setup()
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(unclaimedOrder())
    vi.mocked(ordersApi.claimOrder).mockResolvedValue({
      orderId: 42,
      tcgplayerOrderId: 'ORDER-DETAIL-42',
      status: 'inProgress',
      claimedByEmployeeId: 1,
      claimedByEmployeeName: 'Test Picker',
    })

    renderPage()
    await user.click(await screen.findByRole('button', { name: /^claim/i }))

    expect(ordersApi.claimOrder).toHaveBeenCalledWith(42)
    expect(await screen.findByRole('button', { name: 'Picked' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^claim/i })).not.toBeInTheDocument()
  })

  it('names the holder when someone else claimed it first', async () => {
    const user = userEvent.setup()
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(unclaimedOrder())
    vi.mocked(ordersApi.claimOrder).mockRejectedValue(new ordersApi.OrderAlreadyClaimedError('Sam'))

    renderPage()
    await user.click(await screen.findByRole('button', { name: /^claim/i }))

    // The server settles the race; this only reports what it said (FR-025).
    expect(await screen.findByRole('alert')).toHaveTextContent(/Sam/)
  })

  it('explains rather than failing when the viewer already holds another order', async () => {
    const user = userEvent.setup()
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(unclaimedOrder())
    vi.mocked(ordersApi.claimOrder).mockRejectedValue(new ordersApi.EmployeeHasActiveClaimError(7))

    renderPage()
    await user.click(await screen.findByRole('button', { name: /^claim/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/already/i)
    // And offers a way to the order they do hold, rather than a dead end (FR-027).
    expect(within(alert).getByRole('link')).toHaveAttribute('href', '/orders/7')
  })

  it('offers no claim action on an order someone else holds', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      ...unclaimedOrder(),
      status: 'inProgress',
      claimedByEmployeeId: 2,
      claimedByEmployeeName: 'Sam',
    })

    renderPage()

    await screen.findByRole('article')
    expect(screen.queryByRole('button', { name: /^claim/i })).not.toBeInTheDocument()
  })
})

// A picker on a phone had no way to let go of an order: Release lived only in the desktop
// navigation, and "Complete" on the review screen navigated away while still holding the claim.
// With one claim per employee enforced server-side, that left them unable to start anything else.
describe('OrderDetailPage on a phone — letting go of an order', () => {
  let restore: (() => void) | null = null

  beforeEach(() => {
    // Without this, mock call counts accumulate across the tests in this block, and a
    // "was never called" assertion silently reads a previous test's calls.
    vi.resetAllMocks()
    vi.mocked(authApi.me).mockResolvedValue({
      employeeId: 1,
      displayName: 'Test Picker',
      role: 'Picker',
    })
    restore = installMatchMedia(390).restore
  })

  afterEach(() => {
    restore?.()
    restore = null
  })

  it('offers Release while holding the order', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(
      claimedOrder([buildLine({ productName: 'Only Card' })]),
    )

    renderPage()

    await screen.findByRole('article')
    expect(screen.getByRole('button', { name: /^release$/i })).toBeInTheDocument()
  })

  it('offers no Release on an order it does not hold', async () => {
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      ...claimedOrder([buildLine({ productName: 'Only Card' })]),
      claimedByEmployeeId: 2,
      claimedByEmployeeName: 'Sam',
    })

    renderPage()

    await screen.findByRole('article')
    expect(screen.queryByRole('button', { name: /^release$/i })).not.toBeInTheDocument()
  })

  it('releases the claim when the picker completes the order', async () => {
    const user = userEvent.setup()
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(
      claimedOrder([buildLine({ productName: 'Only Card', pickOutcome: 'picked' })]),
    )
    vi.mocked(ordersApi.releaseOrder).mockResolvedValue(undefined)

    renderPage()
    await screen.findByRole('article')

    // Past the last card is the review screen.
    await user.click(screen.getByRole('button', { name: /next card/i }))
    await user.click(await screen.findByRole('button', { name: /finish picking/i }))

    // Completing without releasing leaves the picker holding an order they have finished, and
    // unable to claim another.
    expect(ordersApi.releaseOrder).toHaveBeenCalledWith(42)
    expect(await screen.findByText('Browse Orders list')).toBeInTheDocument()
  })

  it('keeps the picker on the order when completing fails', async () => {
    const user = userEvent.setup()
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue(
      claimedOrder([buildLine({ productName: 'Only Card', pickOutcome: 'picked' })]),
    )
    vi.mocked(ordersApi.releaseOrder).mockRejectedValue(new Error('network'))

    renderPage()
    await screen.findByRole('article')
    await user.click(screen.getByRole('button', { name: /next card/i }))
    await user.click(await screen.findByRole('button', { name: /finish picking/i }))

    // Navigating away on a failed release would report work as handed off when it was not.
    expect(screen.queryByText('Browse Orders list')).not.toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn't/i)
  })

  // BR-003. Shipped defect: the review's primary action called release unconditionally, so a
  // picker who had merely walked through an order they never claimed was told the claim could
  // not be released. There is nothing to release, and nothing to finish — only a screen to close.
  it('releases nothing when closing an order it never held', async () => {
    const user = userEvent.setup()
    vi.mocked(ordersApi.getOrderDetail).mockResolvedValue({
      ...claimedOrder([buildLine({ productName: 'Only Card' })]),
      status: 'ready',
      claimedByEmployeeId: null,
      claimedByEmployeeName: null,
    })

    renderPage()
    await screen.findByRole('article')

    await user.click(screen.getByRole('button', { name: /next card/i }))
    // The button says what it does: nothing is being finished here.
    expect(screen.queryByRole('button', { name: /finish picking/i })).not.toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: /^close$/i }))

    expect(ordersApi.releaseOrder).not.toHaveBeenCalled()
    expect(await screen.findByText('Browse Orders list')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
