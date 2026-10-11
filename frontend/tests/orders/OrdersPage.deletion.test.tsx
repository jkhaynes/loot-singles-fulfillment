import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { OrdersPage } from '../../src/features/orders/OrdersPage'
import * as ordersApi from '../../src/features/orders/ordersApi'
import {
  OrderAlreadyDeletedError,
  OrderClaimedBeforeError,
} from '../../src/features/orders/ordersApi'
import type { OrderListItem } from '../../src/features/orders/ordersApi'
import { AuthProvider } from '../../src/features/auth/AuthContext'
import * as authApi from '../../src/features/auth/authApi'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrders: vi.fn(),
  claimOrder: vi.fn(),
  deleteOrder: vi.fn(),
  getDeletionSummary: vi.fn(),
  deleteAllOrders: vi.fn(),
}))

vi.mock('../../src/features/auth/authApi', async (original) => ({
  ...(await original<typeof import('../../src/features/auth/authApi')>()),
  me: vi.fn(),
}))

function order(overrides: Partial<OrderListItem> = {}): OrderListItem {
  return {
    orderId: 1,
    tcgplayerOrderId: 'SYN-0001',
    status: 'ready',
    importedAt: '2026-10-01T15:00:00Z',
    claimedByEmployeeId: null,
    claimedByEmployeeName: null,
    canDelete: true,
    deleteBlockedReason: null,
    ...overrides,
  }
}

function signInAs(role: string) {
  vi.mocked(authApi.me).mockResolvedValue({ employeeId: 9, displayName: 'Test User', role })
}

function renderPage() {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={['/orders']}>
        <Routes>
          <Route path="/orders" element={<OrdersPage />} />
          <Route path="/orders/:orderId" element={<p>Order detail page</p>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  )
}

async function openRowMenu(orderNumber: string) {
  const row = await screen.findByRole('article', { name: new RegExp(orderNumber) })
  await userEvent.click(await within(row).findByRole('button', { name: /actions/i }))
  return row
}

describe('OrdersPage deletion', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  it('offers a picker no delete option and no Delete orders… action (FR-010)', async () => {
    signInAs('Picker')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])

    renderPage()

    const row = await screen.findByRole('article', { name: /SYN-0001/ })
    await waitFor(() => expect(authApi.me).toHaveBeenCalled())
    expect(within(row).queryByRole('button', { name: /actions/i })).not.toBeInTheDocument()
    expect(screen.queryByText(/Delete order/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete orders…' })).not.toBeInTheDocument()
  })

  it("offers a Manager/Admin Delete order… from the row's menu when the order can be deleted (FR-021)", async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])

    renderPage()
    await openRowMenu('SYN-0001')

    expect(screen.getByRole('menuitem', { name: 'Delete order…' })).toBeInTheDocument()
  })

  it("says why a claimed-before order can't be deleted instead of offering it (FR-021)", async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      order({ canDelete: false, deleteBlockedReason: 'claimed_before' }),
    ])

    renderPage()
    await openRowMenu('SYN-0001')

    expect(
      screen.getByText(
        "Can't delete on production: this order has been claimed, so its pick history is kept.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Delete order…' })).not.toBeInTheDocument()
  })

  it('closes the row menu on Escape', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('menuitem', { name: 'Delete order…' })).not.toBeInTheDocument()
  })

  it('closes the row menu on an outside click', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('heading', { name: 'Browse Orders' }))

    expect(screen.queryByRole('menuitem', { name: 'Delete order…' })).not.toBeInTheDocument()
  })

  it('names the order in the single confirmation, with no warnings when none apply (FR-014, FR-022)', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))

    const dialog = screen.getByRole('alertdialog', { name: 'Delete order SYN-0001?' })
    expect(within(dialog).getByRole('button', { name: 'Keep order' })).toHaveFocus()
    expect(within(dialog).getByRole('button', { name: 'Delete order' })).toBeInTheDocument()
    expect(within(dialog).queryByText(/picking this order/)).not.toBeInTheDocument()
    expect(within(dialog).queryByText(/unresolved picking issue/)).not.toBeInTheDocument()
  })

  it('warns about a current claim and an unresolved issue only when they apply (FR-014, FR-022)', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      order({
        status: 'needsAttention',
        claimedByEmployeeId: 7,
        claimedByEmployeeName: 'Sam',
      }),
    ])

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))

    const dialog = screen.getByRole('alertdialog', { name: 'Delete order SYN-0001?' })
    expect(within(dialog).getByText('Sam is picking this order.')).toBeInTheDocument()
    expect(
      within(dialog).getByText('This order has an unresolved picking issue.'),
    ).toBeInTheDocument()
  })

  it('keeps the order when Keep order is chosen', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Keep order' }))

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(ordersApi.deleteOrder).not.toHaveBeenCalled()
    expect(screen.getByRole('article', { name: /SYN-0001/ })).toBeInTheDocument()
  })

  it('removes the row once Delete order succeeds', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([
      order(),
      order({ orderId: 2, tcgplayerOrderId: 'SYN-0002' }),
    ])
    vi.mocked(ordersApi.deleteOrder).mockResolvedValue()

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete order' }))

    await waitFor(() =>
      expect(screen.queryByRole('article', { name: /SYN-0001/ })).not.toBeInTheDocument(),
    )
    expect(ordersApi.deleteOrder).toHaveBeenCalledTimes(1)
    expect(ordersApi.deleteOrder).toHaveBeenCalledWith(1)
    expect(screen.getByRole('article', { name: /SYN-0002/ })).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('sends one delete request when Delete order is double-clicked', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.deleteOrder).mockReturnValue(new Promise(() => {}))

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.dblClick(screen.getByRole('button', { name: 'Delete order' }))

    expect(ordersApi.deleteOrder).toHaveBeenCalledTimes(1)
  })

  it('removes the row and says so when the order was already deleted', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.deleteOrder).mockRejectedValue(new OrderAlreadyDeletedError())

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete order' }))

    expect(await screen.findByText('That order was already deleted.')).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: /SYN-0001/ })).not.toBeInTheDocument()
  })

  it("keeps the row and says why when someone claimed the order first", async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.deleteOrder).mockRejectedValue(new OrderClaimedBeforeError())

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete order' }))

    expect(
      await screen.findByText("Someone claimed this order, so it can't be deleted."),
    ).toBeInTheDocument()
    expect(screen.getByRole('article', { name: /SYN-0001/ })).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('shows the reason in the row menu after someone claimed the order first', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.deleteOrder).mockRejectedValue(new OrderClaimedBeforeError())

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete order' }))
    await screen.findByText("Someone claimed this order, so it can't be deleted.")
    await openRowMenu('SYN-0001')

    expect(
      screen.getByText(
        "Can't delete on production: this order has been claimed, so its pick history is kept.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Delete order…' })).not.toBeInTheDocument()
  })

  it('keeps the row and shows an error when the delete fails otherwise', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.deleteOrder).mockRejectedValue(new Error('boom'))

    renderPage()
    await openRowMenu('SYN-0001')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete order…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Delete order' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't delete this order. Try again.")
    expect(screen.getByRole('article', { name: /SYN-0001/ })).toBeInTheDocument()
  })

  it('deletes all orders after the summary is confirmed with DELETE, then reloads the list (FR-012, FR-014, FR-023)', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders)
      .mockResolvedValueOnce([order(), order({ orderId: 2, tcgplayerOrderId: 'SYN-0002' })])
      .mockResolvedValueOnce([order({ orderId: 3, tcgplayerOrderId: 'SYN-0003' })])
    vi.mocked(ordersApi.getDeletionSummary).mockResolvedValue({
      asOf: '2026-10-11T12:34:56.789Z',
      deletableCount: 2,
      keptCount: 0,
      claimedCount: 0,
      withUnresolvedIssueCount: 0,
    })
    vi.mocked(ordersApi.deleteAllOrders).mockResolvedValue({ deletedCount: 2, keptCount: 0 })

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete orders…' }))

    const dialog = await screen.findByRole('alertdialog', { name: 'Delete 2 orders?' })
    expect(within(dialog).queryByText(/kept/)).not.toBeInTheDocument()
    const confirm = within(dialog).getByRole('button', { name: 'Delete orders' })
    expect(confirm).toBeDisabled()
    await userEvent.type(within(dialog).getByLabelText('Type DELETE to confirm'), 'DELETE')
    await userEvent.click(confirm)

    expect(ordersApi.deleteAllOrders).toHaveBeenCalledWith('2026-10-11T12:34:56.789Z')
    expect(await screen.findByText('2 orders deleted')).toBeInTheDocument()
    expect(await screen.findByRole('article', { name: /SYN-0003/ })).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: /SYN-0001/ })).not.toBeInTheDocument()
    expect(ordersApi.getOrders).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('states the kept count and warnings in the delete-all confirmation and the result when orders are kept', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.getDeletionSummary).mockResolvedValue({
      asOf: '2026-10-11T12:34:56.789Z',
      deletableCount: 2,
      keptCount: 4,
      claimedCount: 1,
      withUnresolvedIssueCount: 1,
    })
    vi.mocked(ordersApi.deleteAllOrders).mockResolvedValue({ deletedCount: 2, keptCount: 4 })

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete orders…' }))

    const dialog = await screen.findByRole('alertdialog', { name: 'Delete 2 orders?' })
    expect(
      within(dialog).getByText("4 orders will be kept because they've been claimed."),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText('1 is claimed. 1 has an unresolved picking issue.'),
    ).toBeInTheDocument()
    await userEvent.type(within(dialog).getByLabelText('Type DELETE to confirm'), 'DELETE')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete orders' }))

    expect(await screen.findByText('2 orders deleted · 4 kept')).toBeInTheDocument()
  })

  it.each([
    [1, "1 order will be kept because it's been claimed."],
    [3, "3 orders will be kept because they've been claimed."],
  ])('words the kept line for %i kept order(s)', async (keptCount, expected) => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.getDeletionSummary).mockResolvedValue({
      asOf: '2026-10-11T12:34:56.789Z',
      deletableCount: 2,
      keptCount,
      claimedCount: 0,
      withUnresolvedIssueCount: 0,
    })

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete orders…' }))

    const dialog = await screen.findByRole('alertdialog', { name: 'Delete 2 orders?' })
    expect(within(dialog).getByText(expected)).toBeInTheDocument()
  })

  it('deletes nothing when Keep orders is chosen', async () => {
    signInAs('ManagerAdmin')
    vi.mocked(ordersApi.getOrders).mockResolvedValue([order()])
    vi.mocked(ordersApi.getDeletionSummary).mockResolvedValue({
      asOf: '2026-10-11T12:34:56.789Z',
      deletableCount: 1,
      keptCount: 0,
      claimedCount: 0,
      withUnresolvedIssueCount: 0,
    })

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete orders…' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Keep orders' }))

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(ordersApi.deleteAllOrders).not.toHaveBeenCalled()
  })
})
