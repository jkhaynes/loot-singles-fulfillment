import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PackingDeskPage } from '../../src/features/packing/PackingDeskPage'
import * as packingApi from '../../src/features/packing/packingApi'

vi.mock('../../src/features/packing/packingApi', async (original) => ({
  ...(await original<typeof import('../../src/features/packing/packingApi')>()),
  resolvePackingCode: vi.fn(),
  getAwaitingPacking: vi.fn(),
  markPacked: vi.fn(),
}))

function view(overrides: Partial<packingApi.PackingView> = {}): packingApi.PackingView {
  return {
    orderId: 121,
    tcgplayerOrderId: 'F8433182-7B9B3B-BC75E',
    cardCount: 8,
    pickedBy: [{ employeeId: 1, displayName: 'Jordan' }],
    pickedAt: '2026-09-21T14:14:00Z',
    status: 'Picked',
    canPack: true,
    blockedReason: null,
    unresolvedProducts: [],
    hasPackingSlip: true,
    ...overrides,
  }
}

function renderDesk() {
  render(
    <MemoryRouter>
      <PackingDeskPage />
    </MemoryRouter>,
  )
}

describe('PackingDeskPage', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(packingApi.getAwaitingPacking).mockResolvedValue([])
  })

  it('resolves a scanned or typed code and shows the order', async () => {
    vi.mocked(packingApi.resolvePackingCode).mockResolvedValue(view())
    renderDesk()

    await userEvent.type(await screen.findByLabelText(/scan a label/i), '121{Enter}')

    expect(packingApi.resolvePackingCode).toHaveBeenCalledWith('121')
    expect(await screen.findByText('Order 121')).toBeInTheDocument()
    expect(screen.getByText('8')).toBeInTheDocument()
    expect(screen.getByText(/Jordan/)).toBeInTheDocument()
  })

  it('names the unresolved product when an order cannot be packed', async () => {
    vi.mocked(packingApi.resolvePackingCode).mockResolvedValue(
      view({
        canPack: false,
        blockedReason: 'A manager still has to decide what happens to one of its products.',
        unresolvedProducts: ['Latias ex'],
      }),
    )
    renderDesk()

    await userEvent.type(await screen.findByLabelText(/scan a label/i), '121{Enter}')

    // FR-028 — "this order has a problem" leaves the packer unable to tell whose problem it is.
    expect(await screen.findByText('Latias ex')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /mark packed/i })).not.toBeInTheDocument()
  })

  it('says plainly when an order has no stored slip, and stays usable', async () => {
    vi.mocked(packingApi.resolvePackingCode).mockResolvedValue(view({ hasPackingSlip: false }))
    renderDesk()

    await userEvent.type(await screen.findByLabelText(/scan a label/i), '121{Enter}')

    // FR-022 — every order imported before this feature is in this state. It is normal, and the
    // order can still be packed.
    expect(await screen.findByText(/no packing slip/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /print packing slip/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /mark packed/i })).toBeInTheDocument()
  })

  it('says plainly when a code matches no order', async () => {
    vi.mocked(packingApi.resolvePackingCode).mockRejectedValue(
      new packingApi.PackingOrderNotFoundError(),
    )
    renderDesk()

    await userEvent.type(await screen.findByLabelText(/scan a label/i), 'NOPE{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent(/no order/i)
  })

  it('marks an order packed and drops it from the queue', async () => {
    vi.mocked(packingApi.resolvePackingCode).mockResolvedValue(view())
    vi.mocked(packingApi.markPacked).mockResolvedValue(
      view({
        status: 'Packed',
        canPack: false,
        blockedReason: 'This order has already been packed.',
      }),
    )
    renderDesk()

    await userEvent.type(await screen.findByLabelText(/scan a label/i), '121{Enter}')
    await userEvent.click(await screen.findByRole('button', { name: /mark packed/i }))

    expect(packingApi.markPacked).toHaveBeenCalledWith(121)
    // Terminal: the order reports itself as packed and stops offering the action.
    expect(await screen.findByText(/already been packed/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /mark packed/i })).not.toBeInTheDocument()
    // The queue reloads, so a packed order stops appearing as awaiting packing.
    expect(packingApi.getAwaitingPacking).toHaveBeenCalledTimes(2)
  })

  it('shows every contributor, not a truncated list', async () => {
    // FR-043 — the label shows two and a count because its stock is fixed. The desk has no such
    // constraint, so it shows everyone.
    vi.mocked(packingApi.resolvePackingCode).mockResolvedValue(
      view({
        pickedBy: [
          { employeeId: 1, displayName: 'Jordan' },
          { employeeId: 2, displayName: 'Sam' },
          { employeeId: 3, displayName: 'Alex' },
        ],
      }),
    )
    renderDesk()

    await userEvent.type(await screen.findByLabelText(/scan a label/i), '121{Enter}')

    expect(await screen.findByText(/Jordan/)).toBeInTheDocument()
    expect(screen.getByText(/Sam/)).toBeInTheDocument()
    expect(screen.getByText(/Alex/)).toBeInTheDocument()
    expect(screen.queryByText(/\+1/)).not.toBeInTheDocument()
  })

  it('lists what is awaiting packing without scanning anything', async () => {
    vi.mocked(packingApi.getAwaitingPacking).mockResolvedValue([
      view({ orderId: 119, cardCount: 2 }),
      view({ orderId: 118, cardCount: 19 }),
    ])
    renderDesk()

    expect(await screen.findByText(/awaiting packing/i)).toBeInTheDocument()
    expect(await screen.findByText('Order 119')).toBeInTheDocument()
    expect(screen.getByText('Order 118')).toBeInTheDocument()
  })
})
