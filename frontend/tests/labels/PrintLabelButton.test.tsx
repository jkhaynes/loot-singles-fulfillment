import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PrintLabelButton } from '../../src/features/labels/PrintLabelButton'
import * as ordersApi from '../../src/features/orders/ordersApi'

vi.mock('../../src/features/orders/ordersApi', async (original) => ({
  ...(await original<typeof import('../../src/features/orders/ordersApi')>()),
  getOrderLabel: vi.fn(),
}))

// Canvas-backed encoders; jsdom has no 2D context. What they draw is proven on hardware (T001).
vi.mock('jsbarcode', () => ({ default: vi.fn() }))
vi.mock('qrcode-generator', () => ({
  default: () => ({ addData: vi.fn(), make: vi.fn(), createSvgTag: () => '<svg />' }),
}))

const label: ordersApi.LabelContent = {
  orderId: 121,
  tcgplayerOrderId: 'F8433182-7B9B3B-BC75E',
  cardCount: 8,
  pickedBy: [{ employeeId: 1, displayName: 'Jordan' }],
  pickedAt: '2026-09-21T14:14:00Z',
  isHeld: false,
  unresolvedProducts: [],
  setAsideCount: null,
  shipsShort: false,
}

describe('PrintLabelButton', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.spyOn(window, 'print').mockImplementation(() => {})
  })

  it('fetches the label and prints it, only when asked', async () => {
    vi.mocked(ordersApi.getOrderLabel).mockResolvedValue(label)
    render(<PrintLabelButton orderId={121} />)

    expect(ordersApi.getOrderLabel).not.toHaveBeenCalled()
    expect(window.print).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: /print label/i }))

    expect(ordersApi.getOrderLabel).toHaveBeenCalledWith(121)
    expect(await screen.findByLabelText('Ready to pack label')).toBeInTheDocument()
    expect(window.print).toHaveBeenCalledTimes(1)
  })

  it('renders the hold variant for a held order', async () => {
    vi.mocked(ordersApi.getOrderLabel).mockResolvedValue({
      ...label,
      isHeld: true,
      unresolvedProducts: ['Latias ex'],
    })
    render(<PrintLabelButton orderId={121} />)

    await userEvent.click(screen.getByRole('button', { name: /print label/i }))

    // FR-016 — a held order's label is the one most likely to be reprinted, so it must be
    // reachable here rather than only on the ending screen.
    expect(await screen.findByLabelText('Hold label')).toBeInTheDocument()
  })

  it('says so when there is nothing to print yet', async () => {
    vi.mocked(ordersApi.getOrderLabel).mockRejectedValue(new ordersApi.OrderNotStartedError())
    render(<PrintLabelButton orderId={121} />)

    await userEvent.click(screen.getByRole('button', { name: /print label/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/nothing has been picked/i)
    expect(window.print).not.toHaveBeenCalled()
  })
})
