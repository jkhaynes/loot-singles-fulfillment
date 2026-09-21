import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { OrderFinish } from '../../src/features/orders/OrderFinish'
import { groupOrderLines } from '../../src/features/orders/orderGrouping'
import type { OrderLineDetail } from '../../src/features/orders/ordersApi'
import { buildIssueLine, buildLine } from '../support/orderBuilders'

/** The big number at the top, scoped: the same digits also appear as quantities in the list. */
function headlineCount(container: HTMLElement): string {
  return container.querySelector('.order-finish__count')?.textContent ?? ''
}

function renderFinish(
  lines: OrderLineDetail[],
  overrides: Partial<Parameters<typeof OrderFinish>[0]> = {},
) {
  const props = {
    groups: groupOrderLines(lines),
    isHolding: true,
    onReturnToLine: vi.fn(),
    onComplete: vi.fn(),
    onBackToCards: vi.fn(),
    ...overrides,
  }
  return { props, ...render(<OrderFinish {...props} />) }
}

describe('OrderFinish — the countable number', () => {
  it('counts physical cards actually pulled, not products', () => {
    const { container } = renderFinish([
      buildLine({ set: 'Alpha', quantity: 3, pickOutcome: 'picked' }),
      buildLine({ set: 'Alpha', quantity: 2, pickOutcome: 'picked' }),
    ])

    // Five cards in the hand from two products — this is the number counted against the sleeve.
    expect(headlineCount(container)).toBe('5')
    expect(screen.getByText(/cards should be in your hand/i)).toBeInTheDocument()
  })

  it('excludes a reported product from the count, because it is not in the sleeve', () => {
    const { container } = renderFinish([
      buildLine({ set: 'Alpha', quantity: 2, pickOutcome: 'picked' }),
      buildIssueLine({ set: 'Alpha', quantity: 3 }),
    ])

    // Ordered five, holding two. Saying "5" would have the picker count 2 and think they erred.
    expect(headlineCount(container)).toBe('2')
    expect(screen.getByText(/1 reported/i)).toBeInTheDocument()
  })

  it('excludes a product never looked at, and says so', () => {
    const { container } = renderFinish([
      buildLine({ set: 'Alpha', quantity: 1, pickOutcome: 'picked' }),
      buildLine({ set: 'Beta', quantity: 4 }),
    ])

    expect(headlineCount(container)).toBe('1')
    expect(screen.getByText(/1 not looked at/i)).toBeInTheDocument()
  })

  it('restates the count on the completing button', () => {
    renderFinish([buildLine({ set: 'Alpha', quantity: 3, pickOutcome: 'picked' })])

    expect(screen.getByRole('button', { name: /finish picking — 3 cards/i })).toBeInTheDocument()
  })
})

describe('OrderFinish — the list', () => {
  function mixedOrder() {
    return [
      buildLine({
        set: 'Alpha',
        productName: 'Three Of These',
        quantity: 3,
        variant: 'Holofoil',
        pickOutcome: 'picked',
      }),
      buildIssueLine({ set: 'Alpha', productName: 'Could Not Find' }),
      buildLine({ set: 'Beta', productName: 'Never Looked At' }),
    ]
  }

  it('lists every product, whatever its outcome', () => {
    renderFinish(mixedOrder())

    expect(screen.getByRole('button', { name: /Three Of These/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Could Not Find/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Never Looked At/i })).toBeInTheDocument()
  })

  it('groups by box, so a box never visited shows as a gap in a familiar order', () => {
    renderFinish(mixedOrder())

    const lists = screen.getAllByRole('list')
    expect(within(lists[0]).getAllByRole('button')).toHaveLength(2)
    expect(within(lists[1]).getAllByRole('button')).toHaveLength(1)
  })

  it('emphasises a quantity greater than one, where a mistake hides', () => {
    renderFinish(mixedOrder())

    const row = screen.getByRole('button', { name: /Three Of These/i })
    expect(within(row).getByText('3')).toHaveAttribute('data-emphasis', 'high')
  })

  it('shows the variant, which no image can convey', () => {
    renderFinish(mixedOrder())

    expect(screen.getByText(/Holofoil/)).toBeInTheDocument()
  })

  it('names the issue on a reported product rather than its collector number', () => {
    renderFinish(mixedOrder())

    expect(screen.getByText('Card Not Found')).toBeInTheDocument()
  })

  it('marks a product never looked at distinctly from one that was reported', () => {
    renderFinish(mixedOrder())

    const row = screen.getByRole('button', { name: /Never Looked At/i })
    expect(within(row).getByText('Not looked at')).toBeInTheDocument()
  })

  it('jumps back to any product', async () => {
    const user = userEvent.setup()
    const lines = mixedOrder()
    const { props } = renderFinish(lines)

    await user.click(screen.getByRole('button', { name: /Never Looked At/i }))

    expect(props.onReturnToLine).toHaveBeenCalledWith(lines[2].id)
  })
})

describe('OrderFinish — completing', () => {
  it('completes on a deliberate press, recording nothing itself', async () => {
    const user = userEvent.setup()
    const lines = [buildLine({ set: 'Alpha', pickOutcome: 'picked' })]
    const { props } = renderFinish(lines)

    await user.click(screen.getByRole('button', { name: /finish picking/i }))

    expect(props.onComplete).toHaveBeenCalledTimes(1)
    expect(props.onReturnToLine).not.toHaveBeenCalled()
  })

  it('returns to the cards without completing', async () => {
    const user = userEvent.setup()
    const { props } = renderFinish([buildLine({ set: 'Alpha' })])

    await user.click(screen.getByRole('button', { name: /back to the cards/i }))

    expect(props.onBackToCards).toHaveBeenCalledTimes(1)
    expect(props.onComplete).not.toHaveBeenCalled()
  })
})

// Viewing an order you do not hold and pressing the primary action tried to release a claim
// that was never yours, and failed with an error. Nothing is being finished there — the picker
// is just closing a screen they were looking at.
describe('OrderFinish — when the order is not held', () => {
  it('offers to close rather than to finish picking', () => {
    renderFinish([buildLine({ set: 'Alpha', pickOutcome: 'picked' })], { isHolding: false })

    expect(screen.getByRole('button', { name: /^close$/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /finish picking/i })).not.toBeInTheDocument()
  })

  it('does not offer a card count it has no claim over', () => {
    renderFinish([buildLine({ set: 'Alpha', quantity: 3, pickOutcome: 'picked' })], {
      isHolding: false,
    })

    // "Finish picking — 3 cards" would imply this employee pulled them.
    expect(screen.queryByRole('button', { name: /3 cards/i })).not.toBeInTheDocument()
  })

  it('names what it does when the order IS held', () => {
    renderFinish([buildLine({ set: 'Alpha', quantity: 3, pickOutcome: 'picked' })], {
      isHolding: true,
    })

    // Finishing the picking, not completing the order — it still has to be packed (PRD §36).
    expect(screen.getByRole('button', { name: /finish picking — 3 cards/i })).toBeInTheDocument()
  })
})
