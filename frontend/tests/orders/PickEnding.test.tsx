import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PickEnding } from '../../src/features/orders/PickEnding'
import type { LabelContent } from '../../src/features/orders/ordersApi'

// Both encoders measure text through a canvas 2D context, which jsdom does not implement — an
// environment limit, not a defect. What they draw is proven on real hardware by quickstart
// scenario 0 (T001) and by the E2E pass, neither of which runs in jsdom. These tests are about
// what the screen says, so the encoders are stubbed out of the way.
vi.mock('jsbarcode', () => ({ default: vi.fn() }))
vi.mock('qrcode-generator', () => ({
  default: () => ({ addData: vi.fn(), make: vi.fn(), createSvgTag: () => '<svg />' }),
}))

function label(overrides: Partial<LabelContent> = {}): LabelContent {
  return {
    orderId: 121,
    tcgplayerOrderId: 'F8433182-7B9B3B-BC75E',
    cardCount: 8,
    pickedBy: [{ employeeId: 1, displayName: 'Jordan' }],
    pickedAt: '2026-09-21T14:14:00Z',
    isHeld: false,
    unresolvedProducts: [],
    setAsideCount: null,
    shipsShort: false,
    ...overrides,
  }
}

function renderEnding(content: LabelContent, onNextOrder = vi.fn()) {
  render(<PickEnding label={content} onNextOrder={onNextOrder} onBackToDashboard={vi.fn()} />)
  return { onNextOrder }
}

describe('PickEnding — pick complete', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('states the physical card count and nothing else countable', async () => {
    renderEnding(label({ cardCount: 8 }))

    expect(await screen.findByText('Pick complete')).toBeInTheDocument()
    expect(screen.getByText('8')).toBeInTheDocument()
    expect(screen.getByText(/cards in the sleeve/i)).toBeInTheDocument()
    // FR-004: a product count cannot be checked against a sleeve of loose cards, so it is
    // deliberately absent. This is a Product Owner decision, not an oversight.
    expect(screen.queryByText(/product/i)).not.toBeInTheDocument()
  })

  it('opens no print dialog until asked', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {})

    renderEnding(label())
    await screen.findByText('Pick complete')

    // FR-005. A dialog that opens by itself covers the count the picker is meant to check.
    expect(print).not.toHaveBeenCalled()
  })

  it('prints only when the picker asks, and then makes Next order the primary action', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => {})
    renderEnding(label())
    await screen.findByText('Pick complete')

    const printButton = screen.getByRole('button', { name: /print label/i })
    expect(screen.getByRole('button', { name: /next order/i })).not.toHaveClass(
      'pick-ending__primary',
    )

    await userEvent.click(printButton)

    expect(print).toHaveBeenCalledTimes(1)
    // FR-006 — after printing, moving on is the obvious next step rather than a second decision.
    expect(screen.getByRole('button', { name: /next order/i })).toHaveClass('pick-ending__primary')
  })

  it('continues to the next order when asked', async () => {
    const { onNextOrder } = renderEnding(label())
    await screen.findByText('Pick complete')

    await userEvent.click(screen.getByRole('button', { name: /next order/i }))

    expect(onNextOrder).toHaveBeenCalledTimes(1)
  })
})

describe('PickEnding — needs a manager', () => {
  it('states cards pulled and names what is unresolved', async () => {
    renderEnding(
      label({ cardCount: 7, isHeld: true, unresolvedProducts: ['Latias ex', 'Milotic ex'] }),
    )

    expect(await screen.findByText(/needs a manager/i)).toBeInTheDocument()
    expect(screen.getByText('7')).toBeInTheDocument()
    expect(screen.getByText(/cards pulled/i)).toBeInTheDocument()
    expect(screen.getByText('Latias ex')).toBeInTheDocument()
    expect(screen.getByText('Milotic ex')).toBeInTheDocument()
  })

  it('sends the bundle to the review area, not to ready-to-pack', async () => {
    renderEnding(label({ isHeld: true, unresolvedProducts: ['Latias ex'] }))
    await screen.findByText(/needs a manager/i)

    // The picker's last instruction before the sleeve leaves their hand is where it goes.
    expect(screen.getByText(/review area/i)).toBeInTheDocument()
  })

  it('says nothing about set-aside cards while nothing records them', async () => {
    renderEnding(label({ isHeld: true, unresolvedProducts: ['Latias ex'], setAsideCount: null }))
    await screen.findByText(/needs a manager/i)

    // FR-046 — printing "0 set aside" on every hold label would be worse than silence.
    expect(screen.queryByText(/set aside/i)).not.toBeInTheDocument()
  })

  it('states the set-aside count once something records one', async () => {
    renderEnding(label({ isHeld: true, unresolvedProducts: ['Latias ex'], setAsideCount: 1 }))
    await screen.findByText(/needs a manager/i)

    expect(screen.getByText(/1 card set aside/i)).toBeInTheDocument()
  })
})
